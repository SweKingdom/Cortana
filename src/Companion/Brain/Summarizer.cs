using System.Diagnostics;
using System.Text;
using Companion.Core;
using Companion.Memory;

namespace Companion.Brain;

/// <summary>When the history is folded into summary.md (architecture, "Minne" point 4). The only place these are tuned.</summary>
internal static class SummaryTuning
{
    /// <summary>Past this many messages the oldest are folded into the summary.</summary>
    public const int Threshold = 30;
    /// <summary>...leaving this many verbatim messages (the transcript sent with every call).</summary>
    public const int Keep = 20;
    /// <summary>The job starts only after this long without activity, so it never runs while Philip is typing.</summary>
    public const int IdleDelayMs = 15_000;
    /// <summary>The summary is cut to this many characters (about 400 tokens).</summary>
    public const int MaxChars = 1600;
}

internal sealed record SummaryResult(int Folded, int Chars, long Ms);

/// <summary>
/// Folds the oldest history messages and the old summary into a new summary.md with one background Haiku call
/// (`--effort low` through the brain, counted in quota.json). The summary says only what happened, never who she
/// is (D-026). On any failure nothing changes and the job is tried again after a later reply.
/// </summary>
internal sealed class Summarizer
{
    internal const string Instructions =
        "You maintain a short running summary of a chat between Philip and his desktop companion Cortana. " +
        "You get the current summary (may be empty) and the oldest messages that must be folded in. " +
        "Write the updated summary as plain text: at most 120 words, past tense, third person, only what happened " +
        "and was decided or learned (topics, plans, events, moods). Never describe Cortana's personality, voice or " +
        "character, never give instructions, and do not invent anything. Output only the summary.";

    private readonly IBrain _brain;
    private readonly History _history;
    private readonly MemoryStore _memory;
    private readonly Quota _quota;
    private readonly string _quotaModel;

    public Summarizer(IBrain brain, History history, MemoryStore memory, Quota quota, string quotaModel = ClaudeCodeBrain.Model)
    {
        _brain = brain; _history = history; _memory = memory; _quota = quota; _quotaModel = quotaModel;
    }

    public bool Due => _history.Count > SummaryTuning.Threshold;

    /// <summary>Runs the fold. Null when nothing was due, it was cancelled, or the call failed.</summary>
    public async Task<SummaryResult?> RunAsync(CancellationToken ct)
    {
        if (!Due) return null;
        int fold = _history.Count - SummaryTuning.Keep;
        var old = _history.Messages.Take(fold).ToList();
        var sw = Stopwatch.StartNew();

        var body = new StringBuilder();
        var current = _memory.ReadSummary();
        body.Append("Current summary:\n").Append(current.Length > 0 ? current : "(empty)").Append("\n\nMessages to fold in:\n");
        foreach (var m in old)
            body.Append(m.Role == Role.User ? PromptBuilder.UserName : PromptBuilder.AssistantName).Append(": ").Append(m.Text).Append('\n');
        body.Append("\nWrite the updated summary.");

        var messages = new List<ChatMessage> { new(Role.System, Instructions), new(Role.User, body.ToString()) };
        var text = new StringBuilder();
        BrainUsage? usage = null;
        Log.Event("summary_start", ("messages", fold));
        try
        {
            await foreach (var chunk in _brain.StreamAsync(messages, Array.Empty<ToolSpec>(), ct))
            {
                if (chunk is TextChunk t) text.Append(t.Text);
                else if (chunk is DoneChunk d) usage = d.Usage;
            }
        }
        catch (OperationCanceledException) { Log.Event("summary_cancelled"); return null; }
        catch (BrainException ex) { Log.Event("summary_failed", ("kind", ex.Kind)); return null; }
        catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { Log.Event("summary_failed", ("kind", "Other")); return null; }

        if (ct.IsCancellationRequested) return null;
        if (usage != null) _quota.Record(usage.Model is { Length: > 0 } m ? m : _quotaModel);   // a call was made

        var summary = text.ToString().Trim();
        if (summary.Length == 0) { Log.Event("summary_failed", ("kind", "Empty")); return null; }
        if (summary.Length > SummaryTuning.MaxChars) summary = summary[..SummaryTuning.MaxChars].TrimEnd() + "…";

        _memory.WriteSummary(summary);
        _history.FoldOldest(fold);
        Log.Event("summary_done", ("folded", fold), ("chars", summary.Length), ("ms", sw.ElapsedMilliseconds));
        return new SummaryResult(fold, summary.Length, sw.ElapsedMilliseconds);
    }
}
