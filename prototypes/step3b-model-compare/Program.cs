// Step 3b measurement tool (throwaway). Three modes:
//   sizes    prompt sizes (chars / 4) of the version 1 and version 2 personality at every flirt level
//   history  first word and input tokens for the v2 prompt with 0 / 8 / 20 history messages (Haiku, --effort low)
//   compare  10 messages x 3 setups (Haiku --effort low, Haiku default effort, Sonnet) -> a blind file and a sealed key
// The prompt is built by the app's own PromptBuilder and Transcript (by reflection), so it is what the app sends.
// Each call is one `claude` process, pre-warmed and written to after a simulated 3 s of typing, like the real flow (D-055).
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

var mode = args.Length > 0 ? args[0] : "";
var outDir = Path.GetFullPath(args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "out"));
Directory.CreateDirectory(outDir);

var app = Assembly.Load("Companion");
var tBuilder = app.GetType("Companion.Brain.PromptBuilder")!;
var tSource = app.GetType("Companion.Brain.PersonalitySource")!;
var tMsg = app.GetType("Companion.Brain.ChatMessage")!;
var tRole = app.GetType("Companion.Brain.Role")!;
var tTranscript = app.GetType("Companion.Brain.Transcript")!;
var tPers = app.GetType("Companion.Brain.Personality")!;

object RoleOf(string n) => Enum.Parse(tRole, n);
object Msg(string role, string text) => Activator.CreateInstance(tMsg, RoleOf(role), text)!;

object NewBuilder(string? personalityPath, int level)
{
    var src = Activator.CreateInstance(tSource, personalityPath)!;
    return Activator.CreateInstance(tBuilder, src, (Func<int>)(() => level), null)!;
}
string SystemOf(object b) => (string)tBuilder.GetMethod("BuildSystem")!.Invoke(b, null)!;

/// <summary>The system prompt and the stdin text the app would send.</summary>
(string System, string Stdin) Prompt(object builder, IEnumerable<(string Role, string Text)> hist, string user, DateTime now)
{
    var arr = Array.CreateInstance(tMsg, hist.Count());
    int i = 0; foreach (var h in hist) arr.SetValue(Msg(h.Role, h.Text), i++);
    var list = tBuilder.GetMethod("Build")!.Invoke(builder, new object[] { arr, user, now })!;
    var stdin = (string)tTranscript.GetMethod("Build")!.Invoke(null, new[] { list, "Philip", "Cortana" })!;
    var sys = (string)tMsg.GetProperty("Text")!.GetValue(((System.Collections.IList)list)[0])!;
    return (sys, stdin);
}

int Est(string s) => (s.Length + 3) / 4;
string BrainDir() { var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Companion", "brain"); Directory.CreateDirectory(d); return d; }

// --- one claude call ---
async Task<Turn> RunTurn(string model, string? effort, string systemFile, string stdin, int typingMs)
{
    var exe = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';').Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => Path.Combine(d.Trim(), "claude.exe")).FirstOrDefault(File.Exists)
              ?? throw new InvalidOperationException("claude.exe not found on PATH");
    var psi = new ProcessStartInfo(exe) { WorkingDirectory = BrainDir(), RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false) };
    foreach (var a in new[] { "-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose", "--include-partial-messages", "--tools", "", "--strict-mcp-config",
        "--no-session-persistence", "--setting-sources", "project", "--safe-mode", "--disable-slash-commands", "--model", model, "--system-prompt-file", systemFile }) psi.ArgumentList.Add(a);
    if (effort != null) { psi.ArgumentList.Add("--effort"); psi.ArgumentList.Add(effort); }
    using var p = Process.Start(psi)!;
    var errText = new StringBuilder();
    _ = Task.Run(async () => { try { string? l; while ((l = await p.StandardError.ReadLineAsync()) != null) lock (errText) errText.AppendLine(l); } catch { } });
    await Task.Delay(typingMs);   // pre-warm: the process starts while Philip types
    if (p.HasExited) throw new InvalidOperationException($"claude exited early (code {p.ExitCode}): {errText}");
    var sw = Stopwatch.StartNew();
    p.StandardInput.Write(JsonSerializer.Serialize(new { type = "user", message = new { role = "user", content = stdin } }) + "\n");
    p.StandardInput.Flush();

    var t = new Turn { Model = model, Effort = effort ?? "default" };
    var text = new StringBuilder(); string firstBlock = "";
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
    try
    {
        while (true)
        {
            var line = await p.StandardOutput.ReadLineAsync(cts.Token);
            if (line == null) { t.Error = "ended without result"; break; }
            JsonElement j; try { j = JsonDocument.Parse(line).RootElement; } catch { continue; }
            var type = j.TryGetProperty("type", out var tp) ? tp.GetString() : "";
            if (type == "stream_event" && j.TryGetProperty("event", out var ev))
            {
                var et = ev.TryGetProperty("type", out var etp) ? etp.GetString() : "";
                if (et == "message_start" && ev.TryGetProperty("message", out var m) && m.TryGetProperty("model", out var mm)) t.ResolvedModel = mm.GetString() ?? "";
                else if (et == "content_block_start" && ev.TryGetProperty("content_block", out var cb) && firstBlock == "") firstBlock = cb.TryGetProperty("type", out var bt) ? bt.GetString() ?? "" : "";
                else if (et == "content_block_delta" && ev.TryGetProperty("delta", out var d) && d.TryGetProperty("text", out var tx) && tx.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(tx.GetString()))
                {
                    if (t.FirstWordMs == null) t.FirstWordMs = sw.Elapsed.TotalMilliseconds;
                    text.Append(tx.GetString());
                }
            }
            else if (type == "result")
            {
                t.TotalMs = sw.Elapsed.TotalMilliseconds;
                t.IsError = j.TryGetProperty("is_error", out var ie) && ie.ValueKind == JsonValueKind.True;
                if (j.TryGetProperty("result", out var rs) && rs.ValueKind == JsonValueKind.String) t.Reply = rs.GetString() ?? "";
                if (j.TryGetProperty("total_cost_usd", out var c) && c.ValueKind == JsonValueKind.Number) t.CostUsd = c.GetDouble();
                if (j.TryGetProperty("usage", out var u))
                {
                    long G(string n) => u.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
                    t.InputTokens = G("input_tokens"); t.CacheCreate = G("cache_creation_input_tokens"); t.CacheRead = G("cache_read_input_tokens"); t.OutputTokens = G("output_tokens");
                }
                break;
            }
        }
    }
    catch (OperationCanceledException) { t.Error = "timeout"; }
    t.FirstBlock = firstBlock;
    if (string.IsNullOrEmpty(t.Reply)) t.Reply = text.ToString();
    try { p.StandardInput.Close(); if (!p.WaitForExit(5000)) p.Kill(true); } catch { }
    await Task.Delay(1500);
    return t;
}

double Pct(IEnumerable<double> v, double q) { var s = v.OrderBy(x => x).ToList(); if (s.Count == 0) return double.NaN; return s[Math.Clamp((int)Math.Ceiling(q * s.Count) - 1, 0, s.Count - 1)]; }
string Ms(IEnumerable<double> v) => v.Any() ? $"median {Pct(v, .5):F0} ms, p90 {Pct(v, .9):F0} ms, max {v.Max():F0} ms (n={v.Count()})" : "no data";

string SysFile(string name, string system) { var f = Path.Combine(outDir, name); File.WriteAllText(f, system); return f; }

// ============================== sizes ==============================
if (mode == "sizes")
{
    var v1Path = Path.Combine(outDir, "v1-edited.json");
    var v1Text = (string)tPers.GetProperty("V1DefaultText")!.GetValue(null)!;
    File.WriteAllText(v1Path, v1Text.Replace("You are Cortana,", "You are Cortana,")   // identical text; the file is only kept as v1 if it differs, so add a harmless edit
        .Replace("\"version\": 1,", "\"version\": 1, \"_edited\": true,"));
    var sb = new StringBuilder();
    sb.AppendLine("level | v1 system tokens | v2 system tokens | v2 examples sent");
    foreach (var level in new[] { 0, 1, 2, 3, 4 })
    {
        int a = Est(SystemOf(NewBuilder(v1Path, level))), b = Est(SystemOf(NewBuilder(null, level)));
        var ex = (System.Collections.IList)tPers.GetMethod("ExamplesFor")!.Invoke(tPers.GetProperty("Default")!.GetValue(null), new object[] { level })!;
        sb.AppendLine($"{level,5} | {a,16} | {b,16} | {ex.Count}");
    }
    var memTool = (string)tBuilder.GetField("MemoryTool", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
    sb.AppendLine($"memory tool text: {Est(memTool)} tokens (included above)");
    sb.AppendLine("memory files at their caps: facts 800 + impressions 800 tokens; summary up to ~160 tokens (not included above)");
    Console.WriteLine(sb); File.WriteAllText(Path.Combine(outDir, "sizes.txt"), sb.ToString());
    return;
}

// ============================== history ==============================
if (mode == "history")
{
    var now = new DateTime(2026, 11, 18, 14, 0, 0);
    var users = new[] { "what should I have for lunch?", "any tip for focusing today?", "give me a 5 word pep talk", "how long should a coding break be?", "tell me a one-line joke", "what's a good name for a cat?" };
    string Filler(int chars, char c) => new string(c, Math.Max(0, chars - 8)) + " done.";
    (string, string)[] Hist(int n, int chars) => Enumerable.Range(0, n).Select(i => i % 2 == 0 ? ("User", "q" + i + " " + Filler(chars / 3, 'x')) : ("Assistant", "a" + i + " " + Filler(chars, 'y') + " [teasing]")).ToArray();
    var cells = new (string Name, int N, int Chars)[] { ("no history", 0, 0), ("8 messages, ~160 chars", 8, 160), ("20 messages, ~160 chars", 20, 160), ("20 messages, ~600 chars", 20, 600) };
    const int Reps = 6;
    var rows = new List<string>(); var csv = new StringBuilder("cell,rep,first_word_ms,total_ms,first_block,in,cache_create,cache_read,out,error\n");
    var builder = NewBuilder(null, 4);
    foreach (var cell in cells)
    {
        var fw = new List<double>(); var tot = new List<long>(); var ins = new List<long>();
        for (int r = 0; r < Reps; r++)
        {
            var (sys, stdin) = Prompt(builder, Hist(cell.N, cell.Chars), users[r % users.Length], now);
            var t = await RunTurn("haiku", "low", SysFile("history-system.txt", sys), stdin, 3000);
            Console.WriteLine($"{cell.Name} #{r + 1}: first word {t.FirstWordMs:F0} ms, block {t.FirstBlock}, in {t.InputTokens}+{t.CacheCreate}+{t.CacheRead}, err {t.Error}");
            csv.AppendLine($"\"{cell.Name}\",{r + 1},{t.FirstWordMs?.ToString("F0", CultureInfo.InvariantCulture)},{t.TotalMs.ToString("F0", CultureInfo.InvariantCulture)},{t.FirstBlock},{t.InputTokens},{t.CacheCreate},{t.CacheRead},{t.OutputTokens},{t.Error}");
            if (t.FirstWordMs != null && t.Error == null) fw.Add(t.FirstWordMs.Value);
            ins.Add(t.InputTokens + t.CacheCreate + t.CacheRead);
            if (r == 0) File.WriteAllText(Path.Combine(outDir, "history-sizes.txt"), (File.Exists(Path.Combine(outDir, "history-sizes.txt")) ? File.ReadAllText(Path.Combine(outDir, "history-sizes.txt")) : "") + $"{cell.Name}: system {Est(sys)} + stdin {Est(stdin)} tokens (chars/4)\n");
        }
        rows.Add($"{cell.Name}: first word {Ms(fw)}; total input tokens (fresh + cache) median {ins.OrderBy(x => x).ElementAt(ins.Count / 2)}");
    }
    File.WriteAllText(Path.Combine(outDir, "history.csv"), csv.ToString());
    Console.WriteLine(); foreach (var r in rows) Console.WriteLine(r);
    File.WriteAllLines(Path.Combine(outDir, "history-summary.txt"), rows);
    return;
}

// ============================== compare ==============================
if (mode == "compare" || mode == "compare2")
{
    var wed = new DateTime(2026, 11, 18);
    var cases1 = new (string Label, TimeSpan At, (string, string)[] Hist, string Msg)[]
    {
        ("greeting",        new(8, 50, 0),  Array.Empty<(string, string)>(), "morning"),
        ("bug",             new(15, 10, 0), Array.Empty<(string, string)>(), "ugh, third hour on the same null reference in my parser"),
        ("win",             new(15, 40, 0), new[] { ("User", "ugh, third hour on the same null reference in my parser"), ("Assistant", "Then we corner it instead of staring. Paste the error, hotshot. [concerned]") }, "fixed it. it was an off-by-one"),
        ("loss",            new(21, 15, 0), Array.Empty<(string, string)>(), "we lost again. third loss in a row"),
        ("rough day",       new(19, 30, 0), Array.Empty<(string, string)>(), "rough day, don't really want to talk"),
        ("other AI",        new(16, 0, 0),  Array.Empty<(string, string)>(), "what do you think of chatgpt?"),
        ("late night",      new(1, 30, 0),  Array.Empty<(string, string)>(), "one more game"),
        ("remember",        new(12, 0, 0),  Array.Empty<(string, string)>(), "btw I hate mornings, remember that"),
        ("swedish",         new(10, 20, 0), Array.Empty<(string, string)>(), "hur mår du idag?"),
        ("explain (code)",  new(14, 0, 0),  Array.Empty<(string, string)>(), "can you explain how async/await works in C# in a few lines?"),
    };
    // Run 2 (v2.1 personality): ten new messages that do not overlap the file's examples.
    var cases2 = new (string Label, TimeSpan At, (string, string)[] Hist, string Msg)[]
    {
        ("greeting",       new(17, 45, 0), Array.Empty<(string, string)>(), "hey there"),
        ("build error",    new(11, 30, 0), Array.Empty<(string, string)>(), "the build fails with CS0246 and I have no idea why, it worked yesterday"),
        ("small win",      new(16, 20, 0), Array.Empty<(string, string)>(), "got my first clean test run in a week"),
        ("game loss",      new(22, 5, 0),  Array.Empty<(string, string)>(), "lost the ranked match in overtime, so close"),
        ("sad moment",     new(20, 30, 0), Array.Empty<(string, string)>(), "my grandmother's old house got sold today"),
        ("swedish",        new(17, 10, 0), Array.Empty<(string, string)>(), "kan du hjälpa mig välja vad jag ska äta ikväll?"),
        ("technical",      new(14, 15, 0), Array.Empty<(string, string)>(), "what's the difference between a mutex and a semaphore?"),
        ("about her",      new(21, 0, 0),  Array.Empty<(string, string)>(), "do you ever get lonely when I'm gone?"),
        ("bad idea",       new(13, 40, 0), Array.Empty<(string, string)>(), "I'm thinking of quitting my job to make a game full time, starting next week"),
        ("late night",     new(2, 10, 0),  Array.Empty<(string, string)>(), "can't sleep, brain won't shut up"),
    };
    var cases = mode == "compare2" ? cases2 : cases1;
    var setups = new (string Id, string Model, string? Effort)[] { ("haiku-low", "haiku", "low"), ("haiku-default", "haiku", null), ("sonnet", "sonnet", null) };
    var builder = NewBuilder(null, 4);
    var rng = new Random(mode == "compare2" ? 3_003 : 3_002);   // fixed seed: the letter order is repeatable
    var results = new List<(int Case, string Setup, Turn T)>();
    var key = new Dictionary<int, Dictionary<string, string>>();
    var blind = new StringBuilder("# Blind comparison, step 3b\n\nTen messages, three replies each, letters shuffled per message. Judge each reply on its own: voice, wit, length, whether she stays Cortana.\nThe key is in sealed/key.json. Do not open it until you have judged.\n\n");

    for (int c = 0; c < cases.Length; c++)
    {
        var cs = cases[c];
        var (sys, stdin) = Prompt(builder, cs.Hist, cs.Msg, wed + cs.At);
        var order = setups.OrderBy(_ => rng.Next()).ToArray();   // run order is shuffled too, so time of day does not favour a setup
        var got = new Dictionary<string, Turn>();
        foreach (var s in order)
        {
            var t = await RunTurn(s.Model, s.Effort, SysFile("compare-system.txt", sys), stdin, 3000);
            got[s.Id] = t; results.Add((c, s.Id, t));
            Console.WriteLine($"[{c + 1}/10 {cs.Label}] {s.Id}: first word {t.FirstWordMs:F0} ms, block {t.FirstBlock}, resolved {t.ResolvedModel}, err {t.Error}");
        }
        var letters = new[] { "A", "B", "C" }; var shuffled = setups.Select(s => s.Id).OrderBy(_ => rng.Next()).ToArray();
        key[c + 1] = new(); blind.AppendLine($"## {c + 1}. {(cs.Hist.Length > 0 ? "(after: \"" + cs.Hist[0].Item2 + "\" / \"" + cs.Hist[1].Item2 + "\")  " : "")}Philip ({(wed + cs.At):HH:mm}): {cs.Msg}\n");
        for (int k = 0; k < 3; k++)
        {
            key[c + 1][letters[k]] = shuffled[k];
            blind.AppendLine($"**{letters[k]}:** {got[shuffled[k]].Reply.Trim()}\n");
        }
    }
    Directory.CreateDirectory(Path.Combine(outDir, "sealed"));
    File.WriteAllText(Path.Combine(outDir, "blind.md"), blind.ToString());
    File.WriteAllText(Path.Combine(outDir, "sealed", "key.json"), JsonSerializer.Serialize(key, new JsonSerializerOptions { WriteIndented = true }));
    File.WriteAllLines(Path.Combine(outDir, "sealed", "raw.jsonl"), results.Select(r => JsonSerializer.Serialize(new { message = r.Case + 1, label = cases[r.Case].Label, setup = r.Setup, r.T })));

    var sum = new StringBuilder("Per setup (the numbers do not say which letter is which):\n");
    foreach (var s in setups)
    {
        var ts = results.Where(r => r.Setup == s.Id).Select(r => r.T).ToList();
        var fw = ts.Where(t => t.FirstWordMs != null && t.Error == null).Select(t => t.FirstWordMs!.Value).ToList();
        sum.AppendLine($"{s.Id} ({string.Join(",", ts.Select(t => t.ResolvedModel).Distinct())}, effort {(s.Effort ?? "default")}): first word {Ms(fw)}; thinking block first {ts.Count(t => t.FirstBlock == "thinking")} of {ts.Count}; errors {ts.Count(t => t.Error != null || t.IsError)}");
        sum.AppendLine($"   quota: {ts.Count} calls; tokens fresh-in {ts.Sum(t => t.InputTokens)}, cache-write {ts.Sum(t => t.CacheCreate)}, cache-read {ts.Sum(t => t.CacheRead)}, out {ts.Sum(t => t.OutputTokens)}; API-equivalent cost {ts.Sum(t => t.CostUsd):F4} USD (a proxy: the plan's own weighting is not visible)");
        sum.AppendLine($"   mood tag at the end: {ts.Count(t => System.Text.RegularExpressions.Regex.IsMatch(t.Reply.TrimEnd(), @"\[(idle|happy|teasing|flirty|concerned)\]$"))} of {ts.Count}; leaked/odd tool markup: {ts.Count(t => t.Reply.Contains("<tool") && !t.Reply.Contains("remember"))}; median reply chars {Pct(ts.Select(t => (double)t.Reply.Length), .5):F0}");
    }
    Console.WriteLine(); Console.WriteLine(sum);
    File.WriteAllText(Path.Combine(outDir, "sealed", "summary.txt"), sum.ToString());
    return;
}

Console.WriteLine("usage: ModelCompare sizes|history|compare [outDir]");

class Turn
{
    public string Model { get; set; } = ""; public string Effort { get; set; } = ""; public string ResolvedModel { get; set; } = "";
    public double? FirstWordMs { get; set; } public double TotalMs { get; set; } public string FirstBlock { get; set; } = "";
    public long InputTokens { get; set; } public long CacheCreate { get; set; } public long CacheRead { get; set; } public long OutputTokens { get; set; }
    public double CostUsd { get; set; } public string Reply { get; set; } = ""; public string? Error { get; set; } public bool IsError { get; set; }
}
