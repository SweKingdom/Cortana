using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Companion.Brain;
using Companion.Character;

namespace Companion.Core;

/// <summary>
/// Dev hook for tools/perfcheck.ps1 -Chat; does nothing unless COMPANION_PERF_CHAT is set. Drives the real chat
/// path with scripted messages and writes one JSON line per turn to COMPANION_PERF_CHAT_FILE.
/// COMPANION_PERF_CHAT=W:C  W turns through the real flow (input opens, pre-warm, simulated typing, Enter) and
/// C cold turns (no pre-warm). COMPANION_PERF_TYPING_MS sets the simulated typing time (default 3000).
/// History, quota and memory stay in memory and the built-in personality is used, so the real files are untouched.
/// Per turn it also records the mood tag, notes saved with `remember`, and the app's CPU while the reply is being
/// shown (talking animation + typewriter + stream parsing; the claude process is not counted). After the last turn
/// it waits for the background summary if one was scheduled (needs more than 30 messages, i.e. 16 or more turns).
/// </summary>
internal static class PerfProbe
{
    private static readonly string[] Script =
    {
        "morning", "what should I have for breakfast?", "ugh, I slept badly", "any tip for focusing today?",
        "I'm fixing a bug in some C# code", "it's a null reference, classic", "what's a good name for a cat?",
        "I prefer short answers, please remember that", "tell me a one-line joke", "that was bad. another?",
        "what do you think of ChatGPT?", "give me a 5 word pep talk", "how long should a coding break be?",
        "btw, I hate mornings. remember that", "night",
    };

    private static string? Env(string n) => Environment.GetEnvironmentVariable(n);
    public static bool Enabled => !string.IsNullOrEmpty(Env("COMPANION_PERF_CHAT"));

    public static async Task RunAsync(ChatController chat)
    {
        var spec = Env("COMPANION_PERF_CHAT")!.Split(':');
        int warm = int.Parse(spec[0]), cold = spec.Length > 1 ? int.Parse(spec[1]) : 0;
        int typingMs = int.TryParse(Env("COMPANION_PERF_TYPING_MS"), out var t) ? t : 3000;
        var file = Env("COMPANION_PERF_CHAT_FILE");
        void Write(object o) { if (!string.IsNullOrEmpty(file)) File.AppendAllText(file, JsonSerializer.Serialize(o) + "\n"); }

        var bus = chat.Bus;
        var turns = bus.TrackTurns();
        long enter = 0;
        double? first = null; bool filler = false; string? failed = null;
        string? mood = null; int notes = 0, fullNotes = 0;
        long talkStart = 0; TimeSpan talkCpu0 = default;
        var proc = Process.GetCurrentProcess();
        var done = new TaskCompletionSource();
        bus.Subscribe<ReplyChunk>(e =>
        {
            if (first != null || !turns.IsCurrent(e.TurnId)) return;
            first = Ms(enter);   // after the bubble handler
            proc.Refresh(); talkCpu0 = proc.TotalProcessorTime; talkStart = Stopwatch.GetTimestamp();
        });
        bus.Subscribe<MemoryProposal>(e => { if (turns.IsCurrent(e.TurnId)) notes++; });
        bus.Subscribe<MemoryFull>(e => { if (turns.IsCurrent(e.TurnId)) fullNotes++; });
        bus.Subscribe<Filler>(e => { if (turns.IsCurrent(e.TurnId)) filler = true; });
        bus.Subscribe<ReplyDone>(e => { if (turns.IsCurrent(e.TurnId)) { mood = e.Mood?.ToString(); done.TrySetResult(); } });
        bus.Subscribe<TurnFailed>(e => { if (turns.IsCurrent(e.TurnId)) { failed = e.Kind.ToString(); done.TrySetResult(); } });

        for (int i = 0; i < warm + cold; i++)
        {
            bool real = i < warm;
            first = null; filler = false; failed = null; mood = null; notes = 0; fullNotes = 0; talkStart = 0; done = new TaskCompletionSource();
            var msg = Script[i % Script.Length];
            if (real) { chat.ProbeType(msg); await Task.Delay(typingMs); }   // pre-warm starts on the first keystroke; the box stays open between messages (D-071)
            enter = Stopwatch.GetTimestamp();
            chat.ProbeSubmit(msg);
            await Task.WhenAny(done.Task, Task.Delay(120_000));
            var total = Ms(enter);
            // Wait until the reply is fully shown, then take the app's CPU over the whole showing (first character to last).
            for (int w = 0; w < 600 && chat.Revealing; w++) await Task.Delay(100);
            double? talkMs = null, talkCpuPct = null;
            if (talkStart != 0)
            {
                proc.Refresh();
                talkMs = Ms(talkStart);
                talkCpuPct = (proc.TotalProcessorTime - talkCpu0).TotalMilliseconds / talkMs * 100.0;
            }
            var u = chat.Session.Conversation.LastUsage;
            Write(new
            {
                turn = i + 1, mode = real ? "warm" : "cold", firstWordMs = first, totalMs = total, filler, failed,
                block = u?.FirstBlock, outTokens = u?.OutputTokens, history = chat.Session.Conversation.History.Messages.Count,
                mood, notes, fullNotes, talkMs, talkCpuPct,
            });
            await Task.Delay(2000); // let the finished process exit before the next turn
        }
        // The summary job starts 15 s after the last reply if the history is past 30 messages and nothing else happens.
        var summaryTask = chat.Session.Conversation.SummaryTask;
        if (summaryTask != null)
        {
            var finished = await Task.WhenAny(summaryTask, Task.Delay(120_000));
            var result = finished == summaryTask ? await summaryTask : null;
            Write(new { summary = true, folded = result?.Folded, chars = result?.Chars, ms = result?.Ms, historyAfter = chat.Session.Conversation.History.Count });
        }
        Write(new { done = true });
    }

    private static double Ms(long from) => (Stopwatch.GetTimestamp() - from) * 1000.0 / Stopwatch.Frequency;
}
