using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Companion.Core;

namespace Companion.Brain;

/// <summary>
/// Claude through the user's plan (D-012): one `claude` process per message (D-055). The app owns the history and
/// sends it as a transcript, so changing model, flirt level or mode costs nothing. Pre-warm: the process is
/// spawned when the chat input opens and the message is written on Enter; an unused warm process is killed after
/// 60 s or when the input closes unsent. One brain object per model: chat uses <see cref="ChatModel"/> (Sonnet, D-076),
/// the summary and later proactive comments use <see cref="LightModel"/> (Haiku, low effort). ModelRouter is step 4.
/// </summary>
internal sealed class ClaudeCodeBrain : IBrain
{
    /// <summary>The default chat model (D-076). Dev override for measuring: COMPANION_CHAT_MODEL=haiku.</summary>
    public const string ChatModel = "sonnet";
    /// <summary>The cheap model for the summary and proactive comments (D-076).</summary>
    public const string LightModel = "haiku";
    /// <summary>The model the chat runs on: <see cref="ChatModel"/>, or what COMPANION_CHAT_MODEL says (read once; for perfcheck).</summary>
    public static string ChatModelFromEnvironment()
        => Environment.GetEnvironmentVariable("COMPANION_CHAT_MODEL") is { Length: > 0 } m ? m : ChatModel;
    /// <summary>Haiku turns run at low effort so they start with text, not a thinking block (D-061). Other models (Sonnet) keep the default effort.</summary>
    public static string? EffortFor(string model) => model.Contains("haiku", StringComparison.OrdinalIgnoreCase) ? "low" : null;
    private const int WarmLifetimeMs = 60_000;
    private const int SilenceMs = 60_000;

    private readonly string _model;
    private readonly object _gate = new();
    private ClaudeProcess? _warm;
    private string? _warmPrompt;
    private Timer? _warmTimer;

    public ClaudeCodeBrain(string model = ChatModel) => _model = model;

    public string Name => "ClaudeCode";
    public bool SupportsVision => false;
    public bool SupportsTools => false; // text protocol arrives with the tool loop in step 5 (D-057)

    public void PreWarm(string systemPrompt)
    {
        lock (_gate)
        {
            if (_warm is { HasExited: false } && _warmPrompt == systemPrompt) return;
            DropWarm();
            try
            {
                _warm = ClaudeProcess.Start(_model, WriteSystemFile(systemPrompt), EffortFor(_model));
                _warmPrompt = systemPrompt;
                _warmTimer = new Timer(_ => CancelPreWarm(), null, WarmLifetimeMs, Timeout.Infinite); // one-shot, not a poll
                Log.Event("prewarm_start");
            }
            catch (BrainException ex) { Log.Event("prewarm_failed", ("kind", ex.Kind)); } // the real send reports it
        }
    }

    public void CancelPreWarm()
    {
        lock (_gate)
        {
            if (_warm != null) Log.Event("prewarm_cancel");
            DropWarm();
        }
    }

    private void DropWarm()
    {
        _warmTimer?.Dispose(); _warmTimer = null;
        _warm?.Kill(); _warm = null; _warmPrompt = null;
    }

    private ClaudeProcess TakeProcess(string systemPrompt, out bool wasWarm)
    {
        lock (_gate)
        {
            if (_warm is { HasExited: false } w && _warmPrompt == systemPrompt)
            {
                _warmTimer?.Dispose(); _warmTimer = null;
                _warm = null; _warmPrompt = null;
                wasWarm = true;
                return w;
            }
            DropWarm();
        }
        wasWarm = false;
        return ClaudeProcess.Start(_model, WriteSystemFile(systemPrompt), EffortFor(_model));
    }

    private static string WriteSystemFile(string prompt)
    {
        Directory.CreateDirectory(AppPaths.Brain);
        var path = Path.Combine(AppPaths.Brain, "system-prompt.txt");
        try { if (!File.Exists(path) || File.ReadAllText(path) != prompt) File.WriteAllText(path, prompt); }
        catch (IOException) { /* a running process already has its own copy; spawning still works if the file exists */ }
        return path;
    }

    public async IAsyncEnumerable<BrainChunk> StreamAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolSpec> tools,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var system = messages.FirstOrDefault(m => m.Role == Role.System)?.Text ?? "";
        var stdin = Transcript.Build(messages, PromptBuilder.UserName, PromptBuilder.AssistantName);

        // Recovery (D-047): if the process dies before any text arrives, restart once, then give up.
        for (int attempt = 0; ; attempt++)
        {
            var proc = TakeProcess(system, out bool wasWarm);
            bool yielded = false, retry = false;
            try
            {
                await using var e = ReadTurn(proc, stdin, ct).GetAsyncEnumerator(ct);
                while (true)
                {
                    bool has;
                    try { has = await e.MoveNextAsync(); }
                    catch (BrainException ex) when (!yielded && attempt == 0 && ex.Kind == FailKind.Offline) { retry = true; break; }
                    if (!has) break;
                    yielded = true;
                    yield return e.Current;
                }
            }
            finally
            {
                if (ct.IsCancellationRequested || retry) proc.Kill(); else _ = proc.ShutdownAsync();
            }
            if (!retry) yield break;
            Log.Event("brain_retry", ("warm", wasWarm));
        }
    }

    private static async IAsyncEnumerable<BrainChunk> ReadTurn(ClaudeProcess proc, string stdin, [EnumeratorCancellation] CancellationToken ct)
    {
        using var reg = ct.Register(proc.Kill); // interrupt: kill the process (D-055)
        proc.WriteUser(stdin);

        string firstBlock = "", model = "";
        bool thinkingSignalled = false;

        while (true)
        {
            string? line;
            using var silence = CancellationTokenSource.CreateLinkedTokenSource(ct);
            silence.CancelAfter(SilenceMs);
            try { line = await proc.ReadLineAsync(silence.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                proc.Kill();
                throw new BrainException(FailKind.Other, "no answer from claude within 60 s");
            }
            if (line == null) throw EndedWithoutResult(proc);

            JsonElement j;
            try { j = JsonDocument.Parse(line).RootElement; } catch (JsonException) { continue; }
            var type = j.TryGetProperty("type", out var tp) ? tp.GetString() : "";

            if (type == "stream_event" && j.TryGetProperty("event", out var ev))
            {
                var et = ev.TryGetProperty("type", out var etp) ? etp.GetString() : "";
                if (et == "message_start" && ev.TryGetProperty("message", out var msg) && msg.TryGetProperty("model", out var mdl))
                    model = mdl.GetString() ?? "";
                else if (et == "content_block_start" && ev.TryGetProperty("content_block", out var cb))
                {
                    var bt = cb.TryGetProperty("type", out var btp) ? btp.GetString() ?? "" : "";
                    if (firstBlock == "") firstBlock = bt;
                    if (bt == "thinking" && !thinkingSignalled) { thinkingSignalled = true; yield return new ThinkingChunk(); }
                }
                else if (et == "content_block_delta" && ev.TryGetProperty("delta", out var d)
                         && d.TryGetProperty("text", out var tx) && tx.ValueKind == JsonValueKind.String)
                {
                    var t = tx.GetString();
                    if (!string.IsNullOrEmpty(t)) yield return new TextChunk(t);
                }
            }
            else if (type == "result")
            {
                var subtype = j.TryGetProperty("subtype", out var st) ? st.GetString() ?? "" : "";
                bool isError = j.TryGetProperty("is_error", out var ie) && ie.ValueKind == JsonValueKind.True;
                var text = j.TryGetProperty("result", out var rs) && rs.ValueKind == JsonValueKind.String ? rs.GetString() ?? "" : "";
                if (subtype == "error_during_execution" && ct.IsCancellationRequested) yield break; // normal end after an interrupt
                if (isError) throw Classify(text, subtype);

                long inT = 0, cacheT = 0, outT = 0;
                if (j.TryGetProperty("usage", out var u))
                {
                    long G(string n) => u.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
                    inT = G("input_tokens"); cacheT = G("cache_creation_input_tokens") + G("cache_read_input_tokens"); outT = G("output_tokens");
                }
                if (model == "" && j.TryGetProperty("modelUsage", out var mu)) foreach (var p in mu.EnumerateObject()) { model = p.Name; break; }
                yield return new DoneChunk(new BrainUsage(model, inT, cacheT, outT, firstBlock));
                yield break;
            }
        }
    }

    private static BrainException EndedWithoutResult(ClaudeProcess proc)
    {
        var err = proc.Stderr.ToLowerInvariant();
        // Older Claude Code lacks --safe-mode and the -file flags (D-055) and exits at once complaining about them.
        if (err.Contains("unknown option") || err.Contains("unknown argument") || err.Contains("unrecognized"))
            return new BrainException(FailKind.TooOld, "claude rejected a command-line option");
        return new BrainException(FailKind.Offline, "claude ended without an answer");
    }

    private static BrainException Classify(string text, string subtype)
    {
        var t = text.ToLowerInvariant();
        if (t.Contains("usage limit") || t.Contains("rate limit") || t.Contains("limit reached") || t.Contains("quota"))
            return new BrainException(FailKind.UsageLimit, "plan limit reached");
        if (t.Contains("network") || t.Contains("econn") || t.Contains("enotfound") || t.Contains("fetch failed") || t.Contains("timed out") || t.Contains("unable to connect"))
            return new BrainException(FailKind.Offline, "no connection to Claude");
        return new BrainException(FailKind.Other, "claude reported an error (" + subtype + ")");
    }

    public void Dispose() => CancelPreWarm();
}
