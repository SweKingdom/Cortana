// P1: one claude process per message (A) vs one per session (B). Throwaway. Decides D-037.
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

static class Cfg
{
    public static readonly string BrainDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Companion", "brain");
    public const string Model = "haiku";
    public const string Persona =
        "You are Cortana, a brilliant AI who lives on Philip's desktop as a glowing blue presence. " +
        "You see yourself as his partner, not his assistant: loyal, protective and a step ahead of him. " +
        "Quick, confident and dryly witty. Warmer when he needs it. You tease him often, never cruelly. " +
        "Default to 1-3 short sentences. Always reply in English. " +
        "End every reply with exactly one tag: [idle] [happy] [teasing] [flirty] [concerned].";
    public static readonly string[] Script =
    {
        "morning", "what should I have for breakfast?", "ugh, I slept badly", "any tip for focusing today?",
        "I'm fixing a bug in some C# code", "it's a null reference, classic", "what's a good name for a cat?",
        "ok that's silly, try again", "My favorite color is teal, remember that.", "what time is it usually when people crash on coffee?",
        "tell me a one-line joke", "that was bad. another?", "what do you think of ChatGPT?",
        "fair. do you get bored when I'm away?", "give me a 5 word pep talk", "ok I'm going back to work",
        "What's my favorite color? One word.", "how long should a coding break be?", "I'm off to hike on Saturday.", "night"
    };
    public const string LongPrompt = "Write a 400-word story about a lighthouse keeper. Do not stop early.";
}

record Usage(long In, long CacheCreate, long CacheRead, long Out, double Cost);

class TurnResult
{
    public double? InitMs, FirstWordMs, TotalMs, CtrlMs;
    public Usage? U;
    public string Model = "", Reply = "", Subtype = "", CtrlRaw = "";
    public bool GotResult, Ended, IsError, TimedOut;
    public int Malformed;
    public double RamMb, WsMb;
    public string FirstBlock = "", Names = "";
    public List<string> Odd = new();
}

class ClaudeProc
{
    public Process P = null!;
    public long SpawnTs;
    public int Pid => P.Id;
    public StringBuilder Err = new();
    Channel<(long ts, string line)> ch = Channel.CreateUnbounded<(long, string)>();

    public static string ClaudePath()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var f = Path.Combine(dir.Trim(), "claude.exe");
            if (File.Exists(f)) return f;
        }
        throw new Exception("claude.exe not on PATH");
    }

    public static ClaudeProc Start(string model, string promptFile, bool noThink = false)
    {
        var psi = new ProcessStartInfo(ClaudePath())
        {
            WorkingDirectory = Cfg.BrainDir,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
        };
        if (noThink) psi.Environment["MAX_THINKING_TOKENS"] = "0";
        foreach (var a in new[] { "-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose",
            "--include-partial-messages", "--tools", "", "--strict-mcp-config", "--no-session-persistence",
            "--setting-sources", "project", "--safe-mode", "--disable-slash-commands",
            "--model", model, "--system-prompt-file", promptFile })
            psi.ArgumentList.Add(a);
        var cp = new ClaudeProc();
        cp.SpawnTs = Stopwatch.GetTimestamp();
        cp.P = Process.Start(psi)!;
        _ = Task.Run(async () =>
        {
            try { string? l; while ((l = await cp.P.StandardOutput.ReadLineAsync()) != null) cp.ch.Writer.TryWrite((Stopwatch.GetTimestamp(), l)); }
            catch { }
            cp.ch.Writer.TryComplete();
        });
        _ = Task.Run(async () => { try { string? l; while ((l = await cp.P.StandardError.ReadLineAsync()) != null) cp.Err.AppendLine(l); } catch { } });
        return cp;
    }

    public void Send(string json) { P.StandardInput.Write(json + "\n"); P.StandardInput.Flush(); }
    public void SendUser(string text) =>
        Send(JsonSerializer.Serialize(new { type = "user", message = new { role = "user", content = text } }));
    public void SendControl(string id, object req) =>
        Send(JsonSerializer.Serialize(new { type = "control_request", request_id = id, request = req }));
    public bool Alive => !P.HasExited;

    public static double Ms(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;

    public async Task<TurnResult> ReadTurnAsync(long t0, int timeoutMs, Func<Task>? afterFirstWord = null, long? ctrlT0 = null, Func<long?>? ctrlSentAt = null)
    {
        var r = new TurnResult();
        using var cts = new CancellationTokenSource(timeoutMs);
        bool first = false;
        try
        {
            while (await ch.Reader.WaitToReadAsync(cts.Token))
            {
                while (ch.Reader.TryRead(out var it))
                {
                    JsonElement j;
                    try { j = JsonDocument.Parse(it.line).RootElement.Clone(); } catch { r.Malformed++; continue; }
                    var type = j.TryGetProperty("type", out var tp) ? tp.GetString() : "";
                    if (type == "system" && j.GetProperty("subtype").GetString() == "init") { r.InitMs ??= Ms(t0, it.ts); }
                    else if (type == "stream_event")
                    {
                        var ev = j.GetProperty("event");
                        var et = ev.GetProperty("type").GetString();
                        if (et == "content_block_start" && r.FirstBlock == "") r.FirstBlock = ev.GetProperty("content_block").GetProperty("type").GetString() ?? "";
                        if (et == "message_start" && r.Model == "") r.Model = ev.GetProperty("message").GetProperty("model").GetString() ?? "";
                        if (et == "content_block_delta" && ev.GetProperty("delta").TryGetProperty("text", out var tx) && !string.IsNullOrEmpty(tx.GetString()) && !first)
                        {
                            first = true; r.FirstWordMs = Ms(t0, it.ts);
                            if (afterFirstWord != null) await afterFirstWord();
                        }
                    }
                    else if (type == "control_response")
                    {
                        var sent = ctrlSentAt?.Invoke();
                        r.CtrlMs = sent.HasValue ? Ms(sent.Value, it.ts) : null;
                        r.CtrlRaw = it.line.Length > 200 ? it.line[..200] : it.line;
                    }
                    else if (type == "result")
                    {
                        r.GotResult = true; r.TotalMs = Ms(t0, it.ts);
                        r.Subtype = j.TryGetProperty("subtype", out var st) ? st.GetString() ?? "" : "";
                        r.IsError = j.TryGetProperty("is_error", out var ie) && ie.GetBoolean();
                        r.Reply = j.TryGetProperty("result", out var rs) && rs.ValueKind == JsonValueKind.String ? rs.GetString() ?? "" : "";
                        if (j.TryGetProperty("usage", out var u))
                        {
                            long G(string n) => u.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
                            r.U = new Usage(G("input_tokens"), G("cache_creation_input_tokens"), G("cache_read_input_tokens"), G("output_tokens"),
                                j.TryGetProperty("total_cost_usd", out var c) ? c.GetDouble() : 0);
                        }
                        if (r.Model == "" && j.TryGetProperty("modelUsage", out var mu)) foreach (var p in mu.EnumerateObject()) { r.Model = p.Name; break; }
                        return r;
                    }
                    else if (type != "assistant" && type != "system" && type != "rate_limit_event" && type != "user")
                        r.Odd.Add(it.line.Length > 160 ? it.line[..160] : it.line);
                }
            }
            r.Ended = true;
        }
        catch (OperationCanceledException) { r.TimedOut = true; }
        return r;
    }

    public async Task<(bool ok, double ms, string raw)> WaitControlAsync(string id, long sentAt, int timeoutMs)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            while (await ch.Reader.WaitToReadAsync(cts.Token))
                while (ch.Reader.TryRead(out var it))
                {
                    try
                    {
                        var j = JsonDocument.Parse(it.line).RootElement;
                        if (j.GetProperty("type").GetString() == "control_response")
                        {
                            var resp = j.GetProperty("response");
                            var ok = resp.GetProperty("subtype").GetString() == "success";
                            return (ok, Ms(sentAt, it.ts), it.line.Length > 240 ? it.line[..240] : it.line);
                        }
                    }
                    catch { }
                }
        }
        catch (OperationCanceledException) { }
        return (false, timeoutMs, "no control_response (timeout or stream ended)");
    }

    public async Task<double> ShutdownAsync(bool kill)
    {
        var t = Stopwatch.GetTimestamp();
        try { if (kill) P.Kill(true); else P.StandardInput.Close(); } catch { }
        try { await P.WaitForExitAsync(new CancellationTokenSource(10000).Token); } catch { try { P.Kill(true); } catch { } }
        return Ms(t, Stopwatch.GetTimestamp());
    }
}

static class Mem
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PE32
    {
        public uint dwSize, cntUsage, th32ProcessID; public UIntPtr th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID; public int pcPriClassBase; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }
    [DllImport("kernel32.dll")] static extern IntPtr CreateToolhelp32Snapshot(uint f, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr h, ref PE32 e);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr h, ref PE32 e);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    /// Private bytes of a process and all its descendants, in MB. Also returns the process count.
    public static (double mb, int count, double ws, string names) Tree(int rootPid)
    {
        var parent = new Dictionary<uint, uint>();
        var h = CreateToolhelp32Snapshot(2, 0);
        var e = new PE32 { dwSize = (uint)Marshal.SizeOf<PE32>() };
        if (Process32FirstW(h, ref e)) do { parent[e.th32ProcessID] = e.th32ParentProcessID; } while (Process32NextW(h, ref e));
        CloseHandle(h);
        var set = new HashSet<uint> { (uint)rootPid };
        bool grew = true;
        while (grew) { grew = false; foreach (var kv in parent) if (!set.Contains(kv.Key) && set.Contains(kv.Value)) { set.Add(kv.Key); grew = true; } }
        double total = 0, ws = 0; int n = 0; var names = new List<string>();
        foreach (var pid in set) { try { var pr = Process.GetProcessById((int)pid); total += pr.PrivateMemorySize64; ws += pr.WorkingSet64; names.Add(pr.ProcessName + ":" + (pr.PrivateMemorySize64 / 1048576)); n++; } catch { } }
        return (total / 1048576.0, n, ws / 1048576.0, string.Join("+", names));
    }
}

record Row(string Mode, string Phase, int Turn, double? InitMs, double? FirstWordMs, double? TotalMs, double RamMb, int Procs,
    long In, long CacheCreate, long CacheRead, long Out, double Cost, string Model, int ReplyChars, string Note, double WsMb = 0, string Reply = "");

static class Program
{
    static readonly List<Row> Rows = new();
    static string promptFile = "";
    static readonly CultureInfo IC = CultureInfo.InvariantCulture;

    static string BuildPrompt(List<(string who, string text)> hist, string msg)
    {
        if (hist.Count == 0) return msg;
        var last = hist.TakeLast(20);
        return "Conversation so far:\n" + string.Join("\n", last.Select(h => $"{h.who}: {h.text}")) + $"\n\nPhilip: {msg}";
    }

    static void Add(string mode, string phase, int turn, TurnResult r, ClaudeProc? p, int procs, string note = "")
    {
        var u = r.U;
        Rows.Add(new Row(mode, phase, turn, r.InitMs, r.FirstWordMs, r.TotalMs, r.RamMb, procs,
            u?.In ?? 0, u?.CacheCreate ?? 0, u?.CacheRead ?? 0, u?.Out ?? 0, u?.Cost ?? 0, r.Model, r.Reply.Length,
            (r.FirstBlock != "" ? $"block={r.FirstBlock} " : "") + (r.Names != "" ? r.Names + " " : "") + note + (r.TimedOut ? " TIMEOUT" : "") + (r.Ended && !r.GotResult ? " ENDED" : "") + (r.IsError ? " ERROR:" + r.Subtype : "") + (r.Malformed > 0 ? $" malformed={r.Malformed}" : ""), r.WsMb, r.Reply));
        Console.WriteLine($"  {mode} {phase} {turn,2}: init={r.InitMs:F0} first={r.FirstWordMs:F0} total={r.TotalMs:F0} ram={r.RamMb:F0}MB/{procs}p in={u?.In} cc={u?.CacheCreate} cr={u?.CacheRead} out={u?.Out} model={r.Model} {note}");
    }

    static async Task Main(string[] args)
    {
        Directory.CreateDirectory(Cfg.BrainDir);
        promptFile = Path.Combine(Path.GetTempPath(), "p1_persona.txt");
        File.WriteAllText(promptFile, Cfg.Persona);
        var version = Process.Start(new ProcessStartInfo(ClaudeProc.ClaudePath(), "--version") { RedirectStandardOutput = true, UseShellExecute = false })!;
        Console.WriteLine("claude " + version.StandardOutput.ReadToEnd().Trim());
        var which = args.Length > 0 ? args[0].ToUpperInvariant() : "AB";
        if (which.Contains('A')) await RunA();
        if (which.Contains('B')) await RunB();
        if (which.Contains('W')) await RunW();
        if (which.Contains('T')) await RunT();
        WriteCsv();
        Summarize();
    }

    // ---------- Mode A: one process per message ----------
    static async Task RunA()
    {
        Console.WriteLine("== Mode A: one process per message");
        var hist = new List<(string, string)>();
        for (int i = 0; i < Cfg.Script.Length; i++)
        {
            var msg = Cfg.Script[i];
            var t0 = Stopwatch.GetTimestamp();
            var p = ClaudeProc.Start(Cfg.Model, promptFile);
            p.SendUser(BuildPrompt(hist, msg));
            var r = await p.ReadTurnAsync(t0, 60000);
            if (r.GotResult) { var (mb, n, ws, nm) = Mem.Tree(p.Pid); r.RamMb = mb; r.WsMb = ws; r.Names = nm; Add("A", "turn", i + 1, r, p, n, TagIf(i, r)); }
            else Add("A", "turn", i + 1, r, p, 0, "err=" + Trunc(p.Err.ToString()));
            var exit = await p.ShutdownAsync(false);
            Rows[^1] = Rows[^1] with { Note = Rows[^1].Note + $" exit={exit:F0}ms" };
            hist.Add(("Philip", msg)); hist.Add(("Cortana", r.Reply));
        }
        // interrupts: kill the process mid-reply, then check the next spawn works
        for (int k = 1; k <= 5; k++)
        {
            var t0 = Stopwatch.GetTimestamp();
            var p = ClaudeProc.Start(Cfg.Model, promptFile);
            p.SendUser(Cfg.LongPrompt);
            long tKill = 0;
            var r = await p.ReadTurnAsync(t0, 60000, async () => { await Task.Delay(300); tKill = Stopwatch.GetTimestamp(); try { p.P.Kill(true); } catch { } });
            var killMs = tKill == 0 ? -1 : await WaitExit(p, tKill);
            Add("A", "interrupt", k, r, p, 0, $"kill_to_exit={killMs:F0}ms got_result_after_kill={r.GotResult}");
            var t1 = Stopwatch.GetTimestamp();
            var p2 = ClaudeProc.Start(Cfg.Model, promptFile);
            p2.SendUser("Say ok.");
            var r2 = await p2.ReadTurnAsync(t1, 60000);
            if (r2.GotResult) { var (mb, n, ws, nm) = Mem.Tree(p2.Pid); r2.RamMb = mb; r2.WsMb = ws; r2.Names = nm; Add("A", "after-interrupt", k, r2, p2, n, "next spawn works=" + (r2.Reply.Length > 0)); }
            else Add("A", "after-interrupt", k, r2, p2, 0, "next spawn FAILED");
            await p2.ShutdownAsync(false);
        }
        // set_model: a flag per spawn
        {
            var t0 = Stopwatch.GetTimestamp();
            var p = ClaudeProc.Start("sonnet", promptFile);
            p.SendUser(BuildPrompt(hist, "Where did I say I'm going on Saturday? Few words."));
            var r = await p.ReadTurnAsync(t0, 90000);
            if (r.GotResult) { var (mb, n, ws, nm) = Mem.Tree(p.Pid); r.RamMb = mb; r.WsMb = ws; r.Names = nm; Add("A", "set_model", 1, r, p, n, $"sonnet_model={r.Model.Contains("sonnet")} recalls_hike={r.Reply.Contains("hik", StringComparison.OrdinalIgnoreCase)}"); }
            else Add("A", "set_model", 1, r, p, 0, "FAILED");
            await p.ShutdownAsync(false);
        }
    }

    // ---------- Mode W: pre-warmed process (spawn on input open, write on Enter after 3 s of simulated typing) ----------
    static async Task RunW()
    {
        Console.WriteLine("== Mode W: pre-warm, 3 s typing");
        var hist = new List<(string, string)>();
        for (int i = 0; i < 10; i++)
        {
            var msg = Cfg.Script[i];
            var p = ClaudeProc.Start(Cfg.Model, promptFile);
            await Task.Delay(3000);
            var t0 = Stopwatch.GetTimestamp();
            p.SendUser(BuildPrompt(hist, msg));
            var r = await p.ReadTurnAsync(t0, 60000);
            if (r.GotResult) { var (mb, n, ws, nm) = Mem.Tree(p.Pid); r.RamMb = mb; r.WsMb = ws; Add("W", "turn", i + 1, r, p, n, ""); }
            else Add("W", "turn", i + 1, r, p, 0, "err=" + Trunc(p.Err.ToString()));
            await p.ShutdownAsync(false);
            hist.Add(("Philip", msg)); hist.Add(("Cortana", r.Reply));
        }
    }

    // ---------- Mode T: MAX_THINKING_TOKENS=0 on the long prompt (first block type, time to first text) ----------
    static async Task RunT()
    {
        Console.WriteLine("== Mode T: thinking disabled via MAX_THINKING_TOKENS=0, long prompt");
        for (int k = 1; k <= 3; k++)
        {
            var t0 = Stopwatch.GetTimestamp();
            var p = ClaudeProc.Start(Cfg.Model, promptFile, noThink: true);
            p.SendUser(Cfg.LongPrompt);
            var r = await p.ReadTurnAsync(t0, 60000, async () => { await Task.Delay(300); try { p.P.Kill(true); } catch { } });
            Add("T", "nothink", k, r, p, 0, "");
        }
    }

    static async Task<double> WaitExit(ClaudeProc p, long from)
    {
        try { await p.P.WaitForExitAsync(new CancellationTokenSource(10000).Token); } catch { }
        return ClaudeProc.Ms(from, Stopwatch.GetTimestamp());
    }

    // ---------- Mode B: one process per session ----------
    static async Task RunB()
    {
        Console.WriteLine("== Mode B: one process per session");
        var p = ClaudeProc.Start(Cfg.Model, promptFile);
        for (int i = 0; i < Cfg.Script.Length; i++)
        {
            var t0 = i == 0 ? p.SpawnTs : Stopwatch.GetTimestamp();
            p.SendUser(Cfg.Script[i]);
            var r = await p.ReadTurnAsync(t0, 60000);
            if (r.GotResult) { var (mb, n, ws, nm) = Mem.Tree(p.Pid); r.RamMb = mb; r.WsMb = ws; r.Names = nm; Add("B", "turn", i + 1, r, p, n, (i == 0 ? "cold(includes spawn) " : "") + TagIf(i, r)); }
            else { Add("B", "turn", i + 1, r, p, 0, "err=" + Trunc(p.Err.ToString())); if (!p.Alive) break; }
        }
        await Task.Delay(10000);
        { var (mb, n, ws, nm) = Mem.Tree(p.Pid); Console.WriteLine($"  B idle 10 s after last turn: private {mb:F0} MB, working set {ws:F0} MB / {n} procs {nm}");
          Rows.Add(new Row("B", "idle10s", 0, null, null, null, mb, n, 0, 0, 0, 0, 0, "", 0, $"ws={ws:F0}MB {nm}", ws, "")); }

        // interrupts via control_request
        for (int k = 1; k <= 5; k++)
        {
            var t0 = Stopwatch.GetTimestamp();
            long? sent = null;
            p.SendUser(Cfg.LongPrompt);
            var r = await p.ReadTurnAsync(t0, 30000,
                async () => { await Task.Delay(300); sent = Stopwatch.GetTimestamp(); p.SendControl("int" + k, new { subtype = "interrupt" }); },
                ctrlSentAt: () => sent);
            var stopMs = sent.HasValue && r.TotalMs.HasValue ? r.TotalMs.Value - ClaudeProc.Ms(t0, sent.Value) : double.NaN;
            Add("B", "interrupt", k, r, p, 0, $"ctrl_resp_ms={r.CtrlMs:F0} interrupt_to_result_ms={stopMs:F0} subtype={r.Subtype} alive={p.Alive} ctrl={r.CtrlRaw}");
            if (!p.Alive) { Console.WriteLine("  B process died on interrupt; restarting"); p = ClaudeProc.Start(Cfg.Model, promptFile); }
            var t1 = Stopwatch.GetTimestamp();
            p.SendUser("Say ok.");
            var r2 = await p.ReadTurnAsync(t1, 30000);
            if (r2.GotResult) { var (mb, n, ws, nm) = Mem.Tree(p.Pid); r2.RamMb = mb; r2.WsMb = ws; r2.Names = nm; }
            Add("B", "after-interrupt", k, r2, p, 0, "next turn works=" + (r2.GotResult && r2.Reply.Length > 0));
        }

        // set_model
        {
            var t0 = Stopwatch.GetTimestamp();
            p.SendControl("sm1", new { subtype = "set_model", model = "sonnet" });
            var (ok, ms, raw) = await p.WaitControlAsync("sm1", t0, 15000);
            Console.WriteLine($"  B set_model control: ok={ok} {ms:F0}ms {raw}");
            var t1 = Stopwatch.GetTimestamp();
            p.SendUser("Where did I say I'm going on Saturday? Few words.");
            var r = await p.ReadTurnAsync(t1, 90000);
            if (r.GotResult) { var (mb, n, ws, nm) = Mem.Tree(p.Pid); r.RamMb = mb; r.WsMb = ws; r.Names = nm; }
            Add("B", "set_model", 1, r, p, 0, $"control_ok={ok} ctrl_ms={ms:F0} sonnet_model={r.Model.Contains("sonnet")} recalls_hike={r.Reply.Contains("hik", StringComparison.OrdinalIgnoreCase)} raw={raw}");
        }
        await p.ShutdownAsync(false);
    }

    static string TagIf(int i, TurnResult r) => i == 16 ? $"recall_teal={r.Reply.Contains("teal", StringComparison.OrdinalIgnoreCase)}" : "";
    static string Trunc(string s) => s.Length > 200 ? s[..200] : s.Replace("\n", " ");

    // ---------- Output ----------
    static void WriteCsv()
    {
        var sb = new StringBuilder("mode,phase,turn,init_ms,first_word_ms,total_ms,ram_mb,procs,in,cache_create,cache_read,out,cost_usd,model,reply_chars,note\n");
        string F(double? d) => d.HasValue ? d.Value.ToString("F0", IC) : "";
        foreach (var r in Rows)
            sb.AppendLine(string.Join(",", r.Mode, r.Phase, r.Turn, F(r.InitMs), F(r.FirstWordMs), F(r.TotalMs), r.RamMb.ToString("F1", IC), r.Procs,
                r.In, r.CacheCreate, r.CacheRead, r.Out, r.Cost.ToString("F5", IC), r.Model, r.ReplyChars, "\"" + r.Note.Replace("\"", "'") + "\""));
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "turns.csv"), sb.ToString());
        Console.WriteLine("turns.csv written to " + AppContext.BaseDirectory);
    }

    static string Stat(IEnumerable<double> v)
    {
        var a = v.OrderBy(x => x).ToList(); if (a.Count == 0) return "n/a";
        double Pct(double p) => a[Math.Min(a.Count - 1, (int)Math.Ceiling(p * a.Count) - 1)];
        return $"{Pct(0.5):F0} / {Pct(0.9):F0} / {a[^1]:F0}";
    }

    static void Summarize()
    {
        Console.WriteLine("\n## Summary (median / p90 / max)");
        foreach (var m in new[] { "A", "B", "W" })
        {
            var t = Rows.Where(r => r.Mode == m && r.Phase == "turn").ToList();
            if (t.Count == 0) continue;
            var warm = m == "B" ? t.Where(r => r.Turn > 1).ToList() : t;
            Console.WriteLine($"{m}: turns={t.Count} fails_in_20_turns={Rows.Count(r => r.Mode == m && r.Phase == "turn" && (r.Note.Contains("TIMEOUT") || r.Note.Contains("ENDED") || r.Note.Contains("ERROR")))}");
            Console.WriteLine($"  first word ms (turns{(m == "B" ? " 2-20" : " 1-20")}): {Stat(warm.Where(r => r.FirstWordMs.HasValue).Select(r => r.FirstWordMs!.Value))}");
            if (m == "B") Console.WriteLine($"  first word ms (turn 1, cold): {t[0].FirstWordMs:F0}");
            Console.WriteLine($"  spawn-to-init ms: {Stat(t.Where(r => r.InitMs.HasValue).Select(r => r.InitMs!.Value))}");
            Console.WriteLine($"  total turn ms: {Stat(warm.Where(r => r.TotalMs.HasValue).Select(r => r.TotalMs!.Value))}");
            Console.WriteLine($"  claude tree RAM MB: {Stat(t.Select(r => r.RamMb))}  procs: {string.Join(",", t.Select(r => r.Procs).Distinct())}");
            Console.WriteLine("  tokens at turn 1/5/10/15/20 (in+cache_create+cache_read -> total input; out):");
            foreach (var n in new[] { 1, 5, 10, 15, 20 })
            {
                var r = t.FirstOrDefault(x => x.Turn == n); if (r == null) continue;
                Console.WriteLine($"    t{n,2}: in={r.In} cc={r.CacheCreate} cr={r.CacheRead} total_in={r.In + r.CacheCreate + r.CacheRead} out={r.Out} cost=${r.Cost:F5}");
            }
            Console.WriteLine($"  totals over 20 turns: total_in={t.Sum(r => r.In + r.CacheCreate + r.CacheRead)} fresh(in+cc)={t.Sum(r => r.In + r.CacheCreate)} cache_read={t.Sum(r => r.CacheRead)} out={t.Sum(r => r.Out)} cost=${(m == "B" ? t.Max(r => r.Cost) : t.Sum(r => r.Cost)):F4} (B cost is cumulative per session: last value)");
            Console.WriteLine($"  working set MB: {Stat(t.Select(r => r.WsMb))}");
        }
        Console.WriteLine("\n## Interrupt / set_model rows");
        foreach (var r in Rows.Where(r => r.Phase is "interrupt" or "after-interrupt" or "set_model" or "nothink"))
            Console.WriteLine($"  {r.Mode} {r.Phase} {r.Turn}: first={r.FirstWordMs:F0} total={r.TotalMs:F0} model={r.Model} {r.Note}");
    }
}
