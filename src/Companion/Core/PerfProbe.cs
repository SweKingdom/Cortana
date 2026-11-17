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
/// History and quota stay in memory, so the real files are untouched.
/// </summary>
internal static class PerfProbe
{
    private static readonly string[] Script =
    {
        "morning", "what should I have for breakfast?", "ugh, I slept badly", "any tip for focusing today?",
        "I'm fixing a bug in some C# code", "it's a null reference, classic", "what's a good name for a cat?",
        "ok that's silly, try again", "tell me a one-line joke", "that was bad. another?",
        "what do you think of ChatGPT?", "give me a 5 word pep talk", "how long should a coding break be?",
        "ok I'm going back to work", "night",
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
        var done = new TaskCompletionSource();
        bus.Subscribe<ReplyChunk>(e => { if (first == null && turns.IsCurrent(e.TurnId)) first = Ms(enter); }); // after the bubble handler
        bus.Subscribe<Filler>(e => { if (turns.IsCurrent(e.TurnId)) filler = true; });
        bus.Subscribe<ReplyDone>(e => { if (turns.IsCurrent(e.TurnId)) done.TrySetResult(); });
        bus.Subscribe<TurnFailed>(e => { if (turns.IsCurrent(e.TurnId)) { failed = e.Kind.ToString(); done.TrySetResult(); } });

        for (int i = 0; i < warm + cold; i++)
        {
            bool real = i < warm;
            first = null; filler = false; failed = null; done = new TaskCompletionSource();
            var msg = Script[i % Script.Length];
            if (real) { chat.OpenInput(); await Task.Delay(typingMs); }   // pre-warm starts when the input opens
            enter = Stopwatch.GetTimestamp();
            chat.ProbeSubmit(msg);
            await Task.WhenAny(done.Task, Task.Delay(120_000));
            var total = Ms(enter);
            var u = chat.Session.Conversation.LastUsage;
            Write(new
            {
                turn = i + 1, mode = real ? "warm" : "cold", firstWordMs = first, totalMs = total, filler, failed,
                block = u?.FirstBlock, outTokens = u?.OutputTokens, history = chat.Session.Conversation.History.Messages.Count,
            });
            await Task.Delay(2000); // let the finished process exit before the next turn
        }
        Write(new { done = true });
    }

    private static double Ms(long from) => (Stopwatch.GetTimestamp() - from) * 1000.0 / Stopwatch.Frequency;
}
