using System.Text;
using Companion.Core;
using Companion.Memory;

namespace Companion.Brain;

/// <summary>
/// Builds what the model sees (architecture, "Minne"): the system prompt = personality file with the chosen flirt
/// level, boundaries, examples and the memory tool, then facts, impressions and the summary; plus a time line, the
/// last 20 messages and the new one. The personality is re-sent verbatim on every call and never trimmed (D-026).
/// The whole prompt is kept under about 3 000 tokens (chars / 4, D-042): oldest verbatim messages go first, then
/// long messages are shortened. The system prompt is constant until the personality, the flirt level or the memory
/// changes, so a pre-warmed process (spawned when the input opens) can be reused.
/// </summary>
internal sealed class PromptBuilder
{
    public const string UserName = "Philip";
    public const string AssistantName = "Cortana";
    public const int TokenBudget = 3000;
    /// <summary>A single message is cut to this many characters when the prompt is still over budget.</summary>
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
        sb.Append("Tone: ").Append(p.Tone).Append('\n');
        sb.Append("Reply length: ").Append(p.ReplyLength).Append("\n\n");

        sb.Append("Flirting (level ").Append(level).Append(" of 4):\n");
        foreach (var l in p.FlirtLines(level)) sb.Append("- ").Append(l).Append('\n');
        sb.Append("\nBoundaries:\n");
        foreach (var b in p.Boundaries!) sb.Append("- ").Append(b).Append('\n');
        sb.Append("\nNicknames: ").Append(p.Nicknames).Append("\n\n");

        sb.Append(MemoryTool).Append("\n\n");

        sb.Append("Examples of how you talk:\n");
        foreach (var e in p.Examples!) sb.Append(p.User).Append(": ").Append(e.User).Append('\n').Append(p.Name).Append(": ").Append(e.Cortana).Append("\n\n");

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

    /// <summary>Changes every minute, so it goes in the message and not in the system prompt.</summary>
    public static string ContextLine(DateTime now) =>
        $"Now: {now.ToString("dddd yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)} (local time).";

    public List<ChatMessage> Build(IReadOnlyList<ChatMessage> history, string userText, DateTime now)
    {
        var system = BuildSystem();
        var context = ContextLine(now);
        var hist = history.TakeLast(Transcript.MaxHistory).ToList();

        int Total() => EstimateTokens(system) + EstimateTokens(context) + hist.Sum(m => EstimateTokens(m.Text)) + EstimateTokens(userText);

        // 1. Oldest verbatim messages go first, but the most recent turn (2 messages) stays.
        while (Total() > TokenBudget && hist.Count > 2) hist.RemoveAt(0);
        // 2. Then long messages are shortened, history first, Philip's new message last.
        for (int i = 0; i < hist.Count && Total() > TokenBudget; i++)
            if (hist[i].Text.Length > ShortenTo) hist[i] = hist[i] with { Text = Shorten(hist[i].Text) };
        if (Total() > TokenBudget && userText.Length > ShortenTo * 3) userText = userText[..(ShortenTo * 3)] + " [shortened]";
        if (Total() > TokenBudget) Log.Event("prompt_over_budget", ("tokens", Total()));

        var list = new List<ChatMessage> { new(Role.System, system), new(Role.Context, context) };
        list.AddRange(hist);
        list.Add(new ChatMessage(Role.User, userText));
        return list;
    }

    private static string Shorten(string t) => t[..ShortenTo] + " [shortened]";
}
