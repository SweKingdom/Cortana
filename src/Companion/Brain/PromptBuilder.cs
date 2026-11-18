using System.Text;
using Companion.Core;
using Companion.Memory;

namespace Companion.Brain;

/// <summary>
/// Builds what the model sees (architecture, "Minne"): the system prompt = personality file with the chosen flirt
/// level, boundaries, examples (filtered by that level) and the memory tool, then facts, impressions and the summary;
/// plus a time line, the last 20 messages, the personality's post-history reminder and the new message. The
/// personality is re-sent verbatim on every call and never trimmed (D-026).
/// Only the history is budgeted (D-075, chars / 4): oldest verbatim messages go first, then long messages are
/// shortened, but at least <see cref="MinHistory"/> messages always stay. The personality, memory, time line,
/// post-history reminder and the new message are never trimmed. The system prompt is constant until the
/// personality, the flirt level or the memory changes, so a pre-warmed process (spawned when the input opens) can
/// be reused.
/// </summary>
internal sealed class PromptBuilder
{
    public const string UserName = "Philip";
    public const string AssistantName = "Cortana";
    /// <summary>Tokens (chars / 4) the replayed history may use (D-075).</summary>
    public const int HistoryBudget = 1500;
    /// <summary>The newest history messages that are never dropped, whatever the budget (a long one may still be shortened).</summary>
    public const int MinHistory = 8;
    /// <summary>A single history message is cut to this many characters when the history is still over budget.</summary>
    public const int ShortenTo = 600;

    private readonly PersonalitySource _personality;
    private readonly Func<int> _flirtLevel;
    private readonly MemoryStore? _memory;

    public PromptBuilder(PersonalitySource personality, Func<int>? flirtLevel = null, MemoryStore? memory = null)
    {
        _personality = personality; _flirtLevel = flirtLevel ?? (() => 4); _memory = memory;
    }

    /// <summary>Built-in personality, flirt level 4, no memory (tests and the default conversation).</summary>
    public static PromptBuilder CreateDefault() => new(new PersonalitySource(null));

    public Personality Personality => _personality.Current;

    public static int EstimateTokens(string s) => (s.Length + 3) / 4;

    private const string MemoryTool =
        "Memory. You can save a note about Philip. Put this tag after your reply text and before the mood tag: " +
        "<tool name=\"remember\">{\"kind\":\"fact\",\"text\":\"...\"}</tool> " +
        "kind is \"fact\" (something Philip told you), \"preference\" (how he wants you to behave, e.g. short replies) or " +
        "\"impression\" (your own read of him, e.g. he seemed stressed). Keep text under 200 characters, in the third " +
        "person, one note per tag, and save only what is worth keeping. To correct a saved note add \"replaces\":\"f12\" " +
        "with the id of the old one. The app shows him an Undo button, so say in your reply what you noted. " +
        "The mood tag is always the very last thing in the reply, after any tool tag. Never mention the tag itself.";

    public string BuildSystem()
    {
        var p = _personality.Current;
        int level = Math.Clamp(_flirtLevel(), 0, 4);
        var sb = new StringBuilder();
        sb.Append(p.Identity).Append("\n\n");
        sb.Append(p.Language).Append("\n\n");

        // Version 2 fields (D-074). A version 1 file has none of them and gets its Tone line instead.
        if (!string.IsNullOrWhiteSpace(p.Backstory)) sb.Append("Backstory: ").Append(p.Backstory).Append("\n\n");
        if (!string.IsNullOrWhiteSpace(p.Relationship)) sb.Append("Relationship: ").Append(p.Relationship).Append("\n\n");
        AppendList(sb, "Who you are:", p.Traits);
        if (p.Interests is { } it && (!string.IsNullOrWhiteSpace(it.Hers) || !string.IsNullOrWhiteSpace(it.His)))
        {
            sb.Append("Interests:\n");
            if (!string.IsNullOrWhiteSpace(it.Hers)) sb.Append("- Yours: ").Append(it.Hers).Append('\n');
            if (!string.IsNullOrWhiteSpace(it.His)) sb.Append("- His: ").Append(it.His).Append('\n');
            sb.Append('\n');
        }
        AppendList(sb, "How you talk:", p.Speech);
        if (!string.IsNullOrWhiteSpace(p.Tone)) sb.Append("Tone: ").Append(p.Tone).Append('\n');
        sb.Append("Reply length: ").Append(p.ReplyLength).Append("\n\n");
        if (!string.IsNullOrWhiteSpace(p.Nagging)) sb.Append("Nudges: ").Append(p.Nagging).Append("\n\n");

        sb.Append("Flirting (level ").Append(level).Append(" of 4):\n");
        foreach (var l in p.FlirtLines(level)) sb.Append("- ").Append(l).Append('\n');
        sb.Append("\nBoundaries:\n");
        foreach (var b in p.Boundaries!) sb.Append("- ").Append(b).Append('\n');
        if (!string.IsNullOrWhiteSpace(p.Never)) sb.Append("\nNever: ").Append(p.Never).Append('\n');
        sb.Append("\nNicknames: ").Append(p.Nicknames).Append("\n\n");

        sb.Append(MemoryTool).Append("\n\n");

        sb.Append("Examples of how you talk:\n");
        foreach (var e in p.ExamplesFor(level)) sb.Append(p.User).Append(": ").Append(e.User).Append('\n').Append(p.Name).Append(": ").Append(e.Cortana).Append("\n\n");

        var facts = _memory?.ForPrompt(MemoryStore.FactsFile) ?? "";
        var impressions = _memory?.ForPrompt(MemoryStore.ImpressionsFile) ?? "";
        var summary = _memory?.ReadSummary() ?? "";
        if (facts.Length == 0 && impressions.Length == 0 && summary.Length == 0)
            sb.Append("Memory: nothing saved yet. Never invent memories about ").Append(p.User).Append(".\n\n");
        if (facts.Length > 0) sb.Append("What you remember about ").Append(p.User).Append(":\n").Append(facts).Append("\n\n");
        if (impressions.Length > 0) sb.Append("Your own impressions of ").Append(p.User).Append(" (interpretations, not facts):\n").Append(impressions).Append("\n\n");
        if (summary.Length > 0) sb.Append("Earlier in your conversation (what happened, not who you are):\n").Append(summary).Append("\n\n");

        sb.Append(p.Moods);
        return sb.ToString();
    }

    private static void AppendList(StringBuilder sb, string heading, string[]? lines)
    {
        if (lines is not { Length: > 0 }) return;
        sb.Append(heading).Append('\n');
        foreach (var l in lines) sb.Append("- ").Append(l).Append('\n');
        sb.Append('\n');
    }

    /// <summary>Changes every minute, so it goes in the message and not in the system prompt.</summary>
    public static string ContextLine(DateTime now) =>
        $"Now: {now.ToString("dddd yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)} (local time).";

    public List<ChatMessage> Build(IReadOnlyList<ChatMessage> history, string userText, DateTime now)
    {
        var system = BuildSystem();
        var context = ContextLine(now);
        var post = _personality.Current.PostHistory;
        var hist = history.TakeLast(Transcript.MaxHistory).ToList();

        int HistTokens() => hist.Sum(m => EstimateTokens(m.Text));

        // 1. Oldest verbatim messages go first, but the newest MinHistory messages (and at least the last turn) stay.
        while (HistTokens() > HistoryBudget && hist.Count > Math.Max(MinHistory, 2)) hist.RemoveAt(0);
        // 2. Then long messages are shortened, oldest first. None are dropped here, so the minimum holds.
        for (int i = 0; i < hist.Count && HistTokens() > HistoryBudget; i++)
            if (hist[i].Text.Length > ShortenTo) hist[i] = hist[i] with { Text = Shorten(hist[i].Text) };
        if (HistTokens() > HistoryBudget) Log.Event("history_over_budget", ("tokens", HistTokens()), ("messages", hist.Count));

        var list = new List<ChatMessage> { new(Role.System, system), new(Role.Context, context) };
        list.AddRange(hist);
        if (!string.IsNullOrWhiteSpace(post)) list.Add(new ChatMessage(Role.PostHistory, post));
        list.Add(new ChatMessage(Role.User, userText));
        return list;
    }

    private static string Shorten(string t) => t[..ShortenTo] + " [shortened]";
}
