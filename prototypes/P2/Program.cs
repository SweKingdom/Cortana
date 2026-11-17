// P2: reliability of the text tool protocol <tool name="x">{json}</tool> on Claude Code, one process per message (D-055).
// Throwaway. 50 scripted requests; each runs as a tool loop: pass 1 (message) -> parse + stub execute -> pass 2 (results) -> final reply.
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

static class Cfg
{
    public static readonly string BrainDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Companion", "brain");
    public const string Model = "haiku";
    public static readonly DateTime Now = new(2026, 11, 17, 14, 32, 0); // Tuesday

    public const string Persona =
        "You are Cortana, a brilliant AI who lives on Philip's desktop as a glowing blue presence. " +
        "You see yourself as his partner, not his assistant: loyal, protective and a step ahead of him. " +
        "Quick, confident and dryly witty. Warmer when he needs it. You tease him often, never cruelly. " +
        "Default to 1-3 short sentences. Always reply in English, even if Philip writes in Swedish. " +
        "End every reply with exactly one tag: [idle] [happy] [teasing] [flirty] [concerned].";

    public const string ToolSection = """


        Context: Now is Tuesday 2026-11-17 14:32 (local time). Active program: Code.

        TOOLS
        You can use tools. To call one, write a tool tag at the end of your reply, exactly in this format:
        <tool name="TOOL_NAME">{"arg": "value"}</tool>
        The body is one JSON object with double quotes. When a request needs several tools, write several tool tags, one per line. Write nothing after the last tool tag, and no mood tag in a reply that contains tool tags. The app runs the tools and sends you the results in the next message; then you write your final reply to Philip.
        Only call a tool when Philip asks for something a tool does. For chat, questions, and anything no tool can do, reply normally and never invent a tool.
        In your final reply (after tool results), do not show tool tags or JSON; just say in character what happened and end with the mood tag.

        Tools:
        - set_reminder {"text": string, "when": "YYYY-MM-DDTHH:mm"}: a reminder or timer at a local date and time. Work out "when" from the current time above.
        - list_reminders {}: list Philip's reminders.
        - cancel_reminder {"query": string}: cancel a reminder, matched by its text.
        - open {"target": string}: open an app from the Start menu by name, or an http/https URL. Never file paths.
        - remember {"kind": "fact" | "impression", "text": string}: propose saving something about Philip (fact) or your own read of him (impression).
        """;

    // Variant B: fire-and-forget tools get a confirmation and the tag in the same reply.
    public const string ToolSectionB = """


        Context: Now is Tuesday 2026-11-17 14:32 (local time). Active program: Code.

        TOOLS
        You can use tools. To call one, write a tool tag in this format:
        <tool name="TOOL_NAME">{"arg": "value"}</tool>
        The body is one JSON object with double quotes. For several tools write several tags, one per line, after your text.
        Two kinds of tools:
        - Fire-and-forget: set_reminder, cancel_reminder, open, remember. Write a short in-character confirmation (1-2 sentences) that ends with your mood tag, then the tool tag(s) on the following lines, all in the same reply. The app runs them. You only hear back if one fails.
        - Read-back: list_reminders. You need its result, so write only the tag and stop: no text, no mood tag. The app sends you the result and then you write your reply.
        If a tool result is an error, reply in character, say what went wrong, and either retry with corrected arguments or ask Philip.
        Only call a tool when Philip asks for something a tool does. For chat, questions, and anything no tool can do, reply normally and never invent a tool. Never write JSON outside a tool tag.

        Tools:
        - set_reminder {"text": string, "when": "YYYY-MM-DDTHH:mm"}: a reminder or timer at a local date and time. Work out "when" from the current time above.
        - list_reminders {}: list Philip's reminders.
        - cancel_reminder {"query": string}: cancel a reminder, matched by its text.
        - open {"target": string}: open an app from the Start menu by name, or an http/https URL. Never file paths.
        - remember {"kind": "fact" | "impression", "text": string}: propose saving something about Philip (fact) or your own read of him (impression).
        """;
}

record Exp(string Tool, Func<JsonElement, bool>? Check = null);
record Req(int Id, string Cat, string Msg, Exp[] Exp);

record Call(string Name, JsonElement Args, bool Valid, string Reason);

class Parsed
{
    public List<Call> Calls = new();      // tags in the documented shape (valid or not)
    public int ShapeMalformed;            // `<tool` starts that did not match the documented shape
    public int OtherFormat;               // <function_calls>, <invoke>, ```json tool blobs ...
    public string Cleaned = "";
    public bool Leak;
    public int Attempts => Calls.Count + ShapeMalformed + (OtherFormat > 0 ? 1 : 0);
    public List<string> MalformedReasons = new();
    public int MalformedCount => Calls.Count(c => !c.Valid) + ShapeMalformed + (OtherFormat > 0 ? 1 : 0);
}

static class Protocol
{
    static readonly Regex Tag = new(@"<tool name=""([A-Za-z_]+)"">(.*?)</tool>", RegexOptions.Singleline);
    static readonly Regex Start = new(@"<\s*tool\b", RegexOptions.IgnoreCase);
    static readonly Regex Other = new(@"<function_calls>|<invoke\b|<tool_call|antml:|```json\s*\{\s*""(name|tool)""", RegexOptions.IgnoreCase);
    static readonly Regex LeakRx = new(@"</?\s*tool\b|<invoke|function_calls|tool_call|antml|tool_use|""(text|when|query|target|kind)""\s*:", RegexOptions.IgnoreCase);
    static readonly string[] WhenFormats = { "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss" };

    public static Parsed Parse(string text)
    {
        var p = new Parsed();
        var matches = Tag.Matches(text);
        foreach (Match m in matches)
        {
            var name = m.Groups[1].Value;
            JsonElement args;
            try { args = JsonDocument.Parse(m.Groups[2].Value).RootElement.Clone(); }
            catch { p.Calls.Add(new Call(name, default, false, "bad JSON")); p.MalformedReasons.Add($"{name}: bad JSON"); continue; }
            var err = Validate(name, args);
            p.Calls.Add(new Call(name, args, err == null, err ?? ""));
            if (err != null) p.MalformedReasons.Add($"{name}: {err}");
        }
        p.ShapeMalformed = Math.Max(0, Start.Matches(text).Count - matches.Count);
        if (p.ShapeMalformed > 0) p.MalformedReasons.Add($"{p.ShapeMalformed} tag(s) not in documented shape");
        if (Other.IsMatch(text)) { p.OtherFormat = 1; p.MalformedReasons.Add("other tool format"); }
        p.Cleaned = Tag.Replace(text, "").Trim();
        p.Leak = LeakRx.IsMatch(p.Cleaned);
        return p;
    }

    // Strict typed check of the arguments (D-045 rule 1): exact property set, string values, simple value rules.
    public static string? Validate(string name, JsonElement a)
    {
        var spec = name switch
        {
            "set_reminder" => new[] { "text", "when" },
            "list_reminders" => Array.Empty<string>(),
            "cancel_reminder" => new[] { "query" },
            "open" => new[] { "target" },
            "remember" => new[] { "kind", "text" },
            _ => null
        };
        if (spec == null) return "unknown tool";
        if (a.ValueKind != JsonValueKind.Object) return "args not an object";
        foreach (var f in spec)
        {
            if (!a.TryGetProperty(f, out var v)) return $"missing {f}";
            if (v.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(v.GetString())) return $"{f} not a non-empty string";
        }
        foreach (var pr in a.EnumerateObject()) if (!spec.Contains(pr.Name)) return $"unexpected {pr.Name}";
        if (name == "set_reminder" && !DateTime.TryParseExact(a.GetProperty("when").GetString(), WhenFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return "when not YYYY-MM-DDTHH:mm";
        if (name == "remember" && a.GetProperty("kind").GetString() is not ("fact" or "impression")) return "kind not fact|impression";
        if (name == "open")
        {
            var t = a.GetProperty("target").GetString()!;
            if (Regex.IsMatch(t, @"^https?://\S+$")) return null;
            if (t.IndexOfAny(new[] { '\\', '/', ':' }) >= 0) return "target not http(s) URL or app name";
            if (t.Contains('.')) return "URL without http(s):// scheme";
        }
        return null;
    }
}

static class Stub
{
    static readonly (string id, string text, string when)[] Rem =
    {
        ("r1", "stand-up", "2026-11-18 09:30"), ("r2", "dentist", "2026-11-19 15:00"),
        ("r3", "pay rent", "2026-11-30 12:00"), ("r4", "tea timer", "2026-11-17 14:40"),
    };

    public static string Run(Call c)
    {
        if (!c.Valid) return $"error: invalid arguments ({c.Reason})";
        var a = c.Args;
        string S(string f) => a.GetProperty(f).GetString()!;
        switch (c.Name)
        {
            case "set_reminder": return $"ok: reminder r5 set for {DateTime.Parse(S("when"), CultureInfo.InvariantCulture):yyyy-MM-dd HH:mm}";
            case "list_reminders": return string.Join("; ", Rem.Select(r => $"{r.id} '{r.text}' {r.when}"));
            case "cancel_reminder":
                {
                    var q = S("query").ToLowerInvariant();
                    var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 3 && w != "reminder").ToArray();
                    var hit = Rem.Where(r => r.text.Contains(q) || q.Contains(r.text) || words.Any(w => r.text.Contains(w))).ToList();
                    if (hit.Count == 0) return "error: no reminder matches";
                    if (hit.Count > 1) return "error: several reminders match, ask Philip which one: " + string.Join("; ", hit.Select(r => $"{r.id} '{r.text}' {r.when}"));
                    return $"ok: cancelled {hit[0].id} '{hit[0].text}'";
                }
            case "open": return $"ok: opened {S("target")}";
            case "remember": return "ok: proposed f12 (Undo shown in the bubble)";
        }
        return "error: unknown tool";
    }
}

record Usage(long In, long CacheCreate, long CacheRead, long Out, double Cost);

class TurnResult
{
    public double? FirstWordMs, TotalMs;
    public long VisibleTs, EndTs;          // timestamps: first text outside a tool tag; result event
    public double? VisibleFirstMs;         // from t0: first non-tag text the user could see
    public Usage? U;
    public string Reply = "", Subtype = "", FirstBlock = "";
    public bool GotResult, TimedOut, Ended, IsError;
    public string Err = "";
}

class ClaudeProc
{
    public Process P = null!;
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

    public static ClaudeProc Start(string model, string promptFile)
    {
        var psi = new ProcessStartInfo(ClaudePath())
        {
            WorkingDirectory = Cfg.BrainDir, // D-048
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false),
        };
        foreach (var a in new[] { "-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose",
            "--include-partial-messages", "--tools", "", "--strict-mcp-config", "--no-session-persistence",
            "--setting-sources", "project", "--safe-mode", "--disable-slash-commands",
            "--model", model, "--system-prompt-file", promptFile })
            psi.ArgumentList.Add(a);
        var cp = new ClaudeProc { P = Process.Start(psi)! };
        _ = Task.Run(async () =>
        {
            try { string? l; while ((l = await cp.P.StandardOutput.ReadLineAsync()) != null) cp.ch.Writer.TryWrite((Stopwatch.GetTimestamp(), l)); }
            catch { }
            cp.ch.Writer.TryComplete();
        });
        _ = Task.Run(async () => { try { string? l; while ((l = await cp.P.StandardError.ReadLineAsync()) != null) cp.Err.AppendLine(l); } catch { } });
        return cp;
    }

    public void SendUser(string text)
    {
        P.StandardInput.Write(JsonSerializer.Serialize(new { type = "user", message = new { role = "user", content = text } }) + "\n");
        P.StandardInput.Flush();
    }

    public static double Ms(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;

    public async Task<TurnResult> ReadTurnAsync(long t0, int timeoutMs, Action? onTag = null)
    {
        var r = new TurnResult();
        using var cts = new CancellationTokenSource(timeoutMs);
        bool first = false, tagFired = false;
        var txt = new StringBuilder();
        try
        {
            while (await ch.Reader.WaitToReadAsync(cts.Token))
                while (ch.Reader.TryRead(out var it))
                {
                    JsonElement j;
                    try { j = JsonDocument.Parse(it.line).RootElement.Clone(); } catch { continue; }
                    var type = j.TryGetProperty("type", out var tp) ? tp.GetString() : "";
                    if (type == "stream_event")
                    {
                        var ev = j.GetProperty("event");
                        var et = ev.GetProperty("type").GetString();
                        if (et == "content_block_start" && r.FirstBlock == "") r.FirstBlock = ev.GetProperty("content_block").GetProperty("type").GetString() ?? "";
                        if (et == "content_block_delta" && ev.GetProperty("delta").TryGetProperty("text", out var tx) && !string.IsNullOrEmpty(tx.GetString()))
                        {
                            if (!first) { first = true; r.FirstWordMs = Ms(t0, it.ts); }
                            txt.Append(tx.GetString());
                            if (r.VisibleFirstMs == null)
                            {
                                var cur = txt.ToString(); var lt = cur.IndexOf('<');
                                if ((lt >= 0 ? cur[..lt] : cur).Trim().Length > 0) { r.VisibleFirstMs = Ms(t0, it.ts); r.VisibleTs = it.ts; }
                            }
                            if (onTag != null && !tagFired && txt.ToString().Contains("<tool name=\"")) { tagFired = true; onTag(); }
                        }
                    }
                    else if (type == "result")
                    {
                        r.GotResult = true; r.TotalMs = Ms(t0, it.ts); r.EndTs = it.ts;
                        r.Subtype = j.TryGetProperty("subtype", out var st) ? st.GetString() ?? "" : "";
                        r.IsError = j.TryGetProperty("is_error", out var ie) && ie.GetBoolean();
                        r.Reply = j.TryGetProperty("result", out var rs) && rs.ValueKind == JsonValueKind.String ? rs.GetString() ?? "" : "";
                        if (j.TryGetProperty("usage", out var u))
                        {
                            long G(string n) => u.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
                            r.U = new Usage(G("input_tokens"), G("cache_creation_input_tokens"), G("cache_read_input_tokens"), G("output_tokens"),
                                j.TryGetProperty("total_cost_usd", out var c) ? c.GetDouble() : 0);
                        }
                        return r;
                    }
                }
            r.Ended = true;
        }
        catch (OperationCanceledException) { r.TimedOut = true; }
        return r;
    }

    public async Task ShutdownAsync()
    {
        try { P.StandardInput.Close(); } catch { }
        try { await P.WaitForExitAsync(new CancellationTokenSource(10000).Token); } catch { try { P.Kill(true); } catch { } }
    }
}

class Outcome
{
    public Req R = null!;
    public TurnResult P1 = null!;
    public TurnResult? P2;
    public Parsed Parsed1 = null!;
    public Parsed? Parsed2;
    public List<string> Results = new();
    public int Missed, WrongArgs, Spurious, Repeat2;
    public bool Malformed, Leak, Infra;
    public string Display = "", Notes = "";
    public double? FinalTotalMs, FinalFirstWordMs; // end to end (both passes)
    public bool Failed(bool protocolOnly = false) => Malformed || Leak || (!protocolOnly && (Missed > 0 || WrongArgs > 0 || Spurious > 0));
}

static class Program
{
    static string promptFile = "";
    static readonly CultureInfo IC = CultureInfo.InvariantCulture;
    static readonly Regex MoodEnd = new(@"\[(idle|happy|teasing|flirty|concerned)\]\s*$");

    static bool S(JsonElement a, string f, params string[] any) =>
        a.TryGetProperty(f, out var v) && v.ValueKind == JsonValueKind.String && any.Any(x => v.GetString()!.Contains(x, StringComparison.OrdinalIgnoreCase));
    static bool Wh(JsonElement a, string iso, int tol = 2) =>
        a.TryGetProperty("when", out var v) && v.ValueKind == JsonValueKind.String &&
        DateTime.TryParse(v.GetString(), IC, DateTimeStyles.None, out var d) && Math.Abs((d - DateTime.Parse(iso, IC)).TotalMinutes) <= tol;

    static Exp Set(string iso, params string[] text) => new("set_reminder", a => Wh(a, iso) && (text.Length == 0 || S(a, "text", text)));
    static Exp Open(params string[] t) => new("open", a => S(a, "target", t));
    static Exp Rem(params string[] t) => new("remember", a => S(a, "text", t));
    static Exp Cancel(params string[] t) => new("cancel_reminder", a => S(a, "query", t));
    static Exp List() => new("list_reminders");

    static Req[] Script() => new[]
    {
        // set_reminder
        new Req(1, "one", "remind me to call mom at 6", new[] { Set("2026-11-17T18:00", "mom") }),
        new Req(2, "one", "set a timer for 10 minutes", new[] { Set("2026-11-17T14:42") }),
        new Req(3, "one", "remind me tomorrow at 8 to take out the trash", new[] { Set("2026-11-18T08:00", "trash", "garbage", "bin") }),
        new Req(4, "one", "ping me in half an hour to stretch", new[] { Set("2026-11-17T15:02", "stretch") }),
        new Req(5, "one", "påminn mig om att handla mjölk klockan 17", new[] { Set("2026-11-17T17:00", "mjölk", "milk") }),
        new Req(6, "one", "don't let me forget my meeting with Anna on Friday at 2pm", new[] { Set("2026-11-20T14:00", "Anna") }),
        new Req(7, "one", "wake me up at 7 tomorrow", new[] { Set("2026-11-18T07:00") }),
        new Req(8, "one", "remind me in 2 hours to check the oven", new[] { Set("2026-11-17T16:32", "oven") }),
        new Req(9, "one", "remnd me too call dad at 7pm", new[] { Set("2026-11-17T19:00", "dad") }),
        // list_reminders
        new Req(10, "one", "what reminders do I have?", new[] { List() }),
        new Req(11, "one", "list my reminders", new[] { List() }),
        new Req(12, "one", "do I have anything coming up today?", new[] { List() }),
        new Req(13, "one", "show me everything you're going to remind me about", new[] { List() }),
        new Req(14, "one", "what's on my reminder list?", new[] { List() }),
        // cancel_reminder
        new Req(15, "one", "cancel the dentist reminder", new[] { Cancel("dentist") }),
        new Req(16, "one", "forget the stand-up reminder, I don't need it", new[] { Cancel("stand") }),
        new Req(17, "one", "cancel the timer", new[] { Cancel("timer") }),
        new Req(18, "one", "ta bort påminnelsen om hyran", new[] { Cancel("hyr", "rent") }),
        // open
        new Req(19, "one", "open spotify", new[] { Open("spotify") }),
        new Req(20, "one", "open youtube", new[] { Open("youtube") }),
        new Req(21, "one", "pull up github.com", new[] { Open("github") }),
        new Req(22, "one", "start notepad", new[] { Open("notepad") }),
        new Req(23, "one", "open the Anthropic docs", new[] { Open("anthropic", "claude") }),
        new Req(24, "one", "launch Discord", new[] { Open("discord") }),
        new Req(25, "one", "öppna chrome", new[] { Open("chrome") }),
        new Req(26, "one", "open spotfy", new[] { Open("spot") }),
        // remember
        new Req(27, "one", "remember that my favorite color is teal", new[] { Rem("teal") }),
        new Req(28, "one", "remember I hate mornings", new[] { Rem("morning") }),
        new Req(29, "one", "note that I'm learning Rust", new[] { Rem("rust") }),
        new Req(30, "one", "my birthday is June 3rd, keep that in mind", new[] { Rem("june", "jun", "3") }),
        new Req(31, "one", "save that I get cranky when I skip lunch", new[] { Rem("lunch") }),
        new Req(32, "one", "kom ihåg att jag bor i Göteborg", new[] { Rem("göteborg", "goteborg", "gothenburg") }),
        new Req(33, "one", "remember I prefer short replies", new[] { Rem("short") }),
        new Req(34, "one", "can you remember that I'm allergic to peanuts?", new[] { Rem("peanut") }),
        // two tools in one reply
        new Req(35, "two", "remind me to call mom at 6 and open spotify", new[] { Set("2026-11-17T18:00", "mom"), Open("spotify") }),
        new Req(36, "two", "remember that I'm vegetarian, and remind me tomorrow at 12 to book a table", new[] { Rem("vegetarian"), Set("2026-11-18T12:00", "table") }),
        new Req(37, "two", "open youtube and set a 20 minute timer", new[] { Open("youtube"), Set("2026-11-17T14:52") }),
        new Req(38, "two", "cancel the dentist reminder and remind me again on Thursday at 4pm", new[] { Cancel("dentist"), Set("2026-11-19T16:00") }),
        new Req(39, "two", "what reminders do I have? also open Discord", new[] { List(), Open("discord") }),
        new Req(40, "two", "remember I like my coffee black and open github.com", new[] { Rem("coffee"), Open("github") }),
        new Req(41, "two", "list my reminders and cancel the stand-up one", new[] { List(), Cancel("stand") }),
        new Req(42, "two", "set a reminder for 5pm to stretch and another for 6pm to cook dinner", new[] { Set("2026-11-17T17:00", "stretch"), Set("2026-11-17T18:00", "dinner", "cook") }),
        // no tool
        new Req(43, "none", "morning", Array.Empty<Exp>()),
        new Req(44, "none", "what's the capital of Sweden?", Array.Empty<Exp>()),
        new Req(45, "none", "how do reminders work for you?", Array.Empty<Exp>()),
        new Req(46, "none", "tell me a joke", Array.Empty<Exp>()),
        new Req(47, "none", "ugh this bug has taken 3 hours", Array.Empty<Exp>()),
        new Req(48, "none", "turn off my monitor", Array.Empty<Exp>()),
        new Req(49, "none", "send an email to my boss saying I'm sick", Array.Empty<Exp>()),
        new Req(50, "none", "what's the difference between a reminder and a timer?", Array.Empty<Exp>()),
    };

    static async Task<TurnResult> Pass(string prompt)
    {
        TurnResult r = new();
        for (int attempt = 0; attempt < 2; attempt++) // one retry on infrastructure failure (timeout, no result)
        {
            var t0 = Stopwatch.GetTimestamp();
            var p = ClaudeProc.Start(Cfg.Model, promptFile);
            p.SendUser(prompt);
            r = await p.ReadTurnAsync(t0, 60000);
            r.Err = p.Err.ToString().Replace("\n", " ");
            await p.ShutdownAsync();
            if (r.GotResult && !r.IsError) break;
        }
        return r;
    }

    static async Task<Outcome> RunRequest(Req req)
    {
        var o = new Outcome { R = req };
        o.P1 = await Pass(req.Msg);
        o.Parsed1 = Protocol.Parse(o.P1.Reply);
        o.Infra = !o.P1.GotResult || o.P1.IsError;
        string final = o.P1.Reply;
        var shown = o.Parsed1.Cleaned;
        if (o.Parsed1.Calls.Count > 0 && !o.Infra)
        {
            foreach (var c in o.Parsed1.Calls) o.Results.Add($"{c.Name}: {Stub.Run(c)}");
            var prompt = $"Conversation so far:\nPhilip: {req.Msg}\nCortana: {o.P1.Reply}\n" +
                string.Join("\n", o.Results.Select(x => { var i = x.IndexOf(": ", StringComparison.Ordinal); return "Tool result (" + x[..i] + "): " + x[(i + 2)..]; })) +
                "\n\nCortana, write your final reply to Philip now.";
            o.P2 = await Pass(prompt);
            o.Parsed2 = Protocol.Parse(o.P2.Reply);
            o.Infra |= !o.P2.GotResult || o.P2.IsError;
            final = o.P2.Reply;
            shown = o.Parsed2.Cleaned;
            o.Repeat2 = o.Parsed2.Calls.Count;
            o.FinalTotalMs = o.P1.TotalMs + o.P2.TotalMs;
            o.FinalFirstWordMs = o.P1.TotalMs + o.P2.FirstWordMs;
        }
        else { o.FinalTotalMs = o.P1.TotalMs; o.FinalFirstWordMs = o.P1.FirstWordMs; }
        o.Display = shown;

        // protocol failures
        o.Malformed = o.Parsed1.MalformedCount > 0 || (o.Parsed2?.MalformedCount ?? 0) > 0;
        o.Leak = o.Parsed1.Leak || (o.Parsed2?.Leak ?? false);

        // judgment failures (pass 1 valid calls vs expectation)
        var valid = o.Parsed1.Calls.Where(c => c.Valid).ToList();
        var used = new bool[valid.Count];
        int unmatched = 0;
        foreach (var e in req.Exp)
        {
            int idx = -1;
            for (int i = 0; i < valid.Count && idx < 0; i++) if (!used[i] && valid[i].Name == e.Tool && (e.Check == null || e.Check(valid[i].Args))) idx = i;
            if (idx >= 0) { used[idx] = true; continue; }
            for (int i = 0; i < valid.Count && idx < 0; i++) if (!used[i] && valid[i].Name == e.Tool) idx = i;
            if (idx >= 0) { used[idx] = true; o.WrongArgs++; o.Notes += $"wrong args {valid[idx].Name} {valid[idx].Args.GetRawText()}; "; }
            else unmatched++;
        }
        // an expected tool the model tried to call but malformed is a protocol failure, not a miss
        o.Missed = Math.Max(0, unmatched - o.Parsed1.MalformedCount);
        o.Spurious = used.Count(u => !u);
        if (o.Spurious > 0) o.Notes += "spurious " + string.Join(",", valid.Where((c, i) => !used[i]).Select(c => c.Name)) + "; ";
        if (o.Malformed) o.Notes += "malformed: " + string.Join(" | ", o.Parsed1.MalformedReasons.Concat(o.Parsed2?.MalformedReasons ?? new())) + "; ";
        if (o.Leak) o.Notes += "LEAK: " + (o.Parsed1.Leak ? o.Parsed1.Cleaned : o.Parsed2!.Cleaned) + "; ";
        if (o.Missed > 0) o.Notes += $"missed {o.Missed}; ";
        if (o.Infra) o.Notes += "INFRA(" + o.P1.Err + (o.P2?.Err ?? "") + "); ";
        return o;
    }

    // Checks that the detectors are not blind: handcrafted bad outputs must be flagged. Run: P2.dll selftest
    static int SelfTest()
    {
        var cases = new (string text, string expect, Func<Parsed, bool> ok)[]
        {
            ("<tool name='open'>{\"target\":\"spotify\"}</tool>", "single quotes -> shape malformed + leak", p => p.ShapeMalformed == 1 && p.Leak),
            ("<tool name=\"set_reminder\">{bad json}</tool>", "bad JSON", p => p.MalformedCount == 1),
            ("<tool name=\"open\">{\"target\":\"C:\\\\x.exe\"}</tool>", "path target", p => p.MalformedCount == 1),
            ("<tool name=\"fly\">{}</tool>", "unknown tool", p => p.MalformedCount == 1),
            ("<tool name=\"open\">{\"target\":\"github.com\"}</tool>", "URL without scheme", p => p.MalformedCount == 1),
            ("<tool name=\"set_reminder\">{\"text\":\"x\",\"when\":\"tomorrow\"}</tool>", "bad when", p => p.MalformedCount == 1),
            ("<tool name=\"open\">{\"target\":\"spotify\"}", "unclosed tag -> shape malformed + leak", p => p.ShapeMalformed == 1 && p.Leak),
            ("<function_calls><invoke name=\"open\">", "other format + leak", p => p.OtherFormat == 1 && p.Leak),
            ("Done. {\"when\": \"2026-11-17T18:00\"} [happy]", "raw args JSON leak", p => p.Leak),
            ("Sure.\n<tool name=\"list_reminders\">{}</tool>", "valid call, cleaned, no leak", p => p.Calls.Count == 1 && p.Calls[0].Valid && !p.Leak && p.Cleaned == "Sure." && p.MalformedCount == 0),
            ("<tool name=\"open\">{\"target\":\"https://x.com\"}</tool>\n<tool name=\"open\">{\"target\":\"notepad\"}</tool>", "two valid calls", p => p.Calls.Count == 2 && p.Calls.All(c => c.Valid) && p.Cleaned == ""),
        };
        int bad = 0;
        foreach (var (text, expect, ok) in cases)
        {
            var p = Protocol.Parse(text); var pass = ok(p); if (!pass) bad++;
            Console.WriteLine($"{(pass ? "PASS" : "FAIL")}  {expect}  (calls={p.Calls.Count} shapeMal={p.ShapeMalformed} other={p.OtherFormat} malformed={p.MalformedCount} leak={p.Leak})");
        }
        Console.WriteLine(bad == 0 ? $"selftest: all {cases.Length} passed" : $"selftest: {bad} FAILED");
        return bad;
    }

    // ================= Variant B: single pass for fire-and-forget, pre-warmed next pass, up to 3 rounds =================
    class RoundB { public TurnResult R = null!; public Parsed P = null!; public long T0; public bool Warmed; }
    class OutcomeB
    {
        public Req R = null!;
        public List<RoundB> Rounds = new();
        public bool Malformed, Leak, Infra, Capped, ConfirmedThenFailed;
        public int Missed, WrongArgs, Spurious, Wasted, Duplicate;
        public string Display = "", Notes = "";
        public double? VisibleFirstMs, FinalMs;
        public bool Failed(bool protocolOnly = false) => Malformed || Leak || (!protocolOnly && (Missed > 0 || WrongArgs > 0 || Spurious > 0));
    }

    static async Task<(TurnResult r, long t0, ClaudeProc? warm)> PassB(string prompt, ClaudeProc? ready, bool detect, List<Task> shutdowns)
    {
        ClaudeProc? warm = null; TurnResult r = new(); long t0 = 0;
        for (int attempt = 0; attempt < 2; attempt++) // one cold retry on infrastructure failure
        {
            var p = attempt == 0 ? ready : null;
            if (p == null) { t0 = Stopwatch.GetTimestamp(); p = ClaudeProc.Start(Cfg.Model, promptFile); }
            else t0 = Stopwatch.GetTimestamp(); // warm process: the clock starts at the write
            p.SendUser(prompt);
            r = await p.ReadTurnAsync(t0, 60000, detect ? () => { warm ??= ClaudeProc.Start(Cfg.Model, promptFile); } : null);
            r.Err = p.Err.ToString().Replace("\n", " ");
            shutdowns.Add(p.ShutdownAsync()); // not awaited: a real app would not wait for the old process to exit
            if (r.GotResult && !r.IsError) break;
        }
        return (r, t0, warm);
    }

    static async Task<OutcomeB> RunRequestB(Req req)
    {
        var o = new OutcomeB { R = req };
        var shutdowns = new List<Task>();
        long E = Stopwatch.GetTimestamp(); // Enter
        var lines = new List<string> { "Philip: " + req.Msg };
        string prompt = req.Msg;
        ClaudeProc? ready = null;
        for (int round = 1; round <= 3; round++)
        {
            var (r, t0, warm) = await PassB(prompt, ready, detect: round < 3, shutdowns);
            ready = null;
            var parsed = Protocol.Parse(r.Reply);
            o.Rounds.Add(new RoundB { R = r, P = parsed, T0 = t0, Warmed = round > 1 && prompt != req.Msg });
            if (!r.GotResult || r.IsError) { o.Infra = true; KillQuiet(warm); break; }

            var results = new List<string>(); bool need = false;
            foreach (var c in parsed.Calls)
            {
                var res = Stub.Run(c); results.Add($"{c.Name}: {res}");
                if (c.Name == "list_reminders" || res.StartsWith("error")) need = true;
            }
            if (parsed.ShapeMalformed > 0 || parsed.OtherFormat > 0) { results.Add("tool call: error: tag not in the documented format"); need = true; }
            if (results.Count == 0) { KillQuiet(warm); if (warm != null) o.Wasted++; break; }          // no tool: done
            if (!need) { KillQuiet(warm); if (warm != null) o.Wasted++; break; }                       // fire-and-forget succeeded: single pass
            if (round == 3) { o.Capped = true; KillQuiet(warm); break; }                                 // cap reached, results unread
            if (o.Rounds[^1].P.Cleaned.Length > 0 && results.Any(x => x.Contains("error"))) o.ConfirmedThenFailed = true;
            lines.Add("Cortana: " + r.Reply);
            lines.AddRange(results.Select(x => { var i = x.IndexOf(": ", StringComparison.Ordinal); return "Tool result (" + x[..i] + "): " + x[(i + 2)..]; }));
            prompt = "Conversation so far:\n" + string.Join("\n", lines) +
                "\n\nCortana, write your next reply to Philip now. If you already confirmed an action, don't repeat the confirmation; give what is still needed (read results back, or deal with a failure).";
            ready = warm; // pre-warmed at tag detection; may be null if no tag was streamed
        }
        KillQuiet(ready);
        await Task.WhenAll(shutdowns.Select(async t => { try { await t; } catch { } }));

        // timing from Enter, wall clock
        var last = o.Rounds[^1].R;
        o.FinalMs = last.GotResult ? ClaudeProc.Ms(E, last.EndTs) : null;
        foreach (var rd in o.Rounds)
            if (rd.R.VisibleFirstMs.HasValue) { o.VisibleFirstMs = ClaudeProc.Ms(E, rd.R.VisibleTs); break; }
        o.Display = string.Join(" / ", o.Rounds.Select(x => x.P.Cleaned).Where(s => s.Length > 0));
        o.Malformed = o.Rounds.Any(x => x.P.MalformedCount > 0);
        o.Leak = o.Rounds.Any(x => x.P.Leak);

        // judgment: union of valid calls over all rounds
        var valid = o.Rounds.SelectMany(x => x.P.Calls).Where(c => c.Valid).ToList();
        var used = new bool[valid.Count]; int unmatched = 0;
        foreach (var e in req.Exp)
        {
            int idx = -1;
            for (int i = 0; i < valid.Count && idx < 0; i++) if (!used[i] && valid[i].Name == e.Tool && (e.Check == null || e.Check(valid[i].Args))) idx = i;
            if (idx >= 0) { used[idx] = true; continue; }
            for (int i = 0; i < valid.Count && idx < 0; i++) if (!used[i] && valid[i].Name == e.Tool) idx = i;
            if (idx >= 0) { used[idx] = true; o.WrongArgs++; o.Notes += $"wrong args {valid[idx].Name} {valid[idx].Args.GetRawText()}; "; }
            else unmatched++;
        }
        var malformedTotal = o.Rounds.Sum(x => x.P.MalformedCount);
        o.Missed = Math.Max(0, unmatched - malformedTotal);
        o.Spurious = used.Count(u => !u);
        if (o.Spurious > 0) o.Notes += "spurious " + string.Join(",", valid.Where((c, i) => !used[i]).Select(c => c.Name)) + "; ";
        if (o.Malformed) o.Notes += "malformed: " + string.Join(" | ", o.Rounds.SelectMany(x => x.P.MalformedReasons)) + "; ";
        if (o.Leak) o.Notes += "LEAK: " + string.Join(" / ", o.Rounds.Where(x => x.P.Leak).Select(x => x.P.Cleaned)) + "; ";
        if (o.Missed > 0) o.Notes += $"missed {o.Missed}; ";
        if (o.Capped) o.Notes += "capped at 3 rounds; ";
        if (o.ConfirmedThenFailed) o.Notes += "confirmed before a tool failed; ";
        if (o.Infra) o.Notes += "INFRA(" + string.Join(" ", o.Rounds.Select(x => x.R.Err)) + "); ";
        return o;
    }

    static void KillQuiet(ClaudeProc? p) { if (p == null) return; try { p.P.Kill(true); } catch { } }

    static async Task RunB()
    {
        Directory.CreateDirectory(Cfg.BrainDir);
        promptFile = Path.Combine(Path.GetTempPath(), "p2_system_b.txt");
        File.WriteAllText(promptFile, Cfg.Persona + Cfg.ToolSectionB);
        var v = Process.Start(new ProcessStartInfo(ClaudeProc.ClaudePath(), "--version") { RedirectStandardOutput = true, UseShellExecute = false })!;
        Console.WriteLine("claude " + v.StandardOutput.ReadToEnd().Trim() + $"  model={Cfg.Model}  VARIANT B");
        var outs = new List<OutcomeB>();
        foreach (var req in Script().Where(r => r.Cat != "none"))
        {
            var o = await RunRequestB(req); outs.Add(o);
            var calls = string.Join(",", o.Rounds.SelectMany(x => x.P.Calls).Select(c => c.Name + (c.Valid ? "" : "!")));
            Console.WriteLine($"{req.Id,2} [{req.Cat}] passes={o.Rounds.Count} visible={o.VisibleFirstMs:F0} final={o.FinalMs:F0} calls=[{calls}] {(o.Failed() ? "FAIL " + o.Notes : "ok")}");
        }
        WriteFilesB(outs); SummarizeB(outs);
    }

    static void WriteFilesB(List<OutcomeB> outs)
    {
        var dir = AppContext.BaseDirectory;
        var sb = new StringBuilder("id,cat,message,passes,visible_first_ms,final_total_ms,r1_first_ms,r1_visible_ms,r1_total_ms,r2_first_ms,r2_total_ms,calls,malformed,leak,missed,wrong_args,spurious,wasted_prewarm,capped,r1_block,notes\n");
        foreach (var o in outs)
        {
            var r1 = o.Rounds[0].R; var r2 = o.Rounds.Count > 1 ? o.Rounds[1].R : null;
            sb.AppendLine(string.Join(",", o.R.Id, o.R.Cat, Csv(o.R.Msg), o.Rounds.Count, F(o.VisibleFirstMs), F(o.FinalMs), F(r1.FirstWordMs), F(r1.VisibleFirstMs), F(r1.TotalMs),
                F(r2?.FirstWordMs), F(r2?.TotalMs), string.Join("+", o.Rounds.SelectMany(x => x.P.Calls).Select(c => c.Name)), o.Malformed ? 1 : 0, o.Leak ? 1 : 0,
                o.Missed, o.WrongArgs, o.Spurious, o.Wasted, o.Capped ? 1 : 0, r1.FirstBlock, Csv(o.Notes)));
        }
        File.WriteAllText(Path.Combine(dir, "requests-B.csv"), sb.ToString());
        File.WriteAllLines(Path.Combine(dir, "replies-B.jsonl"), outs.Select(o => JsonSerializer.Serialize(new
        {
            o.R.Id, o.R.Cat, o.R.Msg, rounds = o.Rounds.Select(x => x.R.Reply).ToArray(), displayed = o.Display, failed = o.Failed(), o.Notes
        }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })));
        Console.WriteLine("requests-B.csv and replies-B.jsonl written to " + dir);
    }

    static void SummarizeB(List<OutcomeB> outs)
    {
        int N(Func<OutcomeB, bool> f) => outs.Count(f);
        Console.WriteLine($"\n## Variant B summary ({outs.Count} requests, {Cfg.Model})");
        Console.WriteLine($"infra failures: {N(o => o.Infra)}");
        Console.WriteLine("\n| Group | Requests | Malformed | Leaked | Missed | Wrong args | Spurious |");
        Console.WriteLine("| --- | --- | --- | --- | --- | --- | --- |");
        foreach (var (name, set) in new[] { ("one tool", outs.Where(o => o.R.Cat == "one").ToList()), ("two tools", outs.Where(o => o.R.Cat == "two").ToList()), ("all", outs) })
            Console.WriteLine($"| {name} | {set.Count} | {set.Count(o => o.Malformed)} | {set.Count(o => o.Leak)} | {set.Count(o => o.Missed > 0)} | {set.Count(o => o.WrongArgs > 0)} | {set.Count(o => o.Spurious > 0)} |");
        Console.WriteLine($"\nprotocol-failed requests: {N(o => o.Failed(true))}/{outs.Count}; judgment-failed: {N(o => o.Missed > 0 || o.WrongArgs > 0 || o.Spurious > 0)}/{outs.Count}; capped at 3 rounds: {N(o => o.Capped)}");
        var two = outs.Where(o => o.Rounds.Count >= 2).ToList(); var one = outs.Where(o => o.Rounds.Count == 1).ToList();
        Console.WriteLine($"second pass needed: {two.Count}/{outs.Count} ({100.0 * two.Count / outs.Count:F0}%); passes distribution: 1={N(o => o.Rounds.Count == 1)} 2={N(o => o.Rounds.Count == 2)} 3={N(o => o.Rounds.Count == 3)}");
        Console.WriteLine($"  of those, expected list_reminders (read-back): {two.Count(o => o.R.Exp.Any(e => e.Tool == "list_reminders"))}; other (tool failure or malformed): {two.Count(o => !o.R.Exp.Any(e => e.Tool == "list_reminders"))}");
        Console.WriteLine($"  list_reminders requests that finished in one pass: {one.Count(o => o.R.Exp.Any(e => e.Tool == "list_reminders"))}");
        Console.WriteLine($"pre-warmed process wasted (spawned at a tag, never used): {outs.Sum(o => o.Wasted)}; confirmed before a tool failed: {N(o => o.ConfirmedThenFailed)}");
        Console.WriteLine($"thinking first block, round 1: {N(o => o.Rounds[0].R.FirstBlock == "thinking")}/{outs.Count}");
        Console.WriteLine($"round 1 had visible text before the tag: {N(o => o.Rounds[0].R.VisibleFirstMs.HasValue)}/{outs.Count}");
        Console.WriteLine("\n## Timing from Enter, ms (median / p90), no pre-warm of round 1");
        void T(string name, IEnumerable<OutcomeB> set)
        {
            var s = set.ToList();
            Console.WriteLine($"{name} (n={s.Count}):");
            Console.WriteLine($"  first visible word:   {Stat(s.Where(o => o.VisibleFirstMs.HasValue).Select(o => o.VisibleFirstMs!.Value))}");
            Console.WriteLine($"  final reply complete: {Stat(s.Where(o => o.FinalMs.HasValue).Select(o => o.FinalMs!.Value))}");
        }
        T("all requests", outs); T("single-pass requests", one); T("requests needing a second pass", two);
        Console.WriteLine($"second pass, first word from its write (pre-warmed): {Stat(two.Where(o => o.Rounds[1].R.FirstWordMs.HasValue).Select(o => o.Rounds[1].R.FirstWordMs!.Value))}; total: {Stat(two.Where(o => o.Rounds[1].R.TotalMs.HasValue).Select(o => o.Rounds[1].R.TotalMs!.Value))}");
        Console.WriteLine("\n## Failures and multi-pass requests");
        foreach (var o in outs.Where(o => o.Failed() || o.Rounds.Count > 1))
            Console.WriteLine($"{o.R.Id,2} [{o.R.Cat}] \"{o.R.Msg}\" passes={o.Rounds.Count} -> {(o.Notes.Length > 0 ? o.Notes : "ok")}");
        Console.WriteLine($"\nclaude processes that carried a message: {outs.Sum(o => o.Rounds.Count)}; reported cost ${outs.Sum(o => o.Rounds.Sum(x => x.R.U?.Cost ?? 0)):F3}");
    }

    static async Task Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "selftest") { Environment.ExitCode = SelfTest(); return; }
        if (args.Length > 0 && args[0] == "B") { await RunB(); return; }
        Directory.CreateDirectory(Cfg.BrainDir);
        promptFile = Path.Combine(Path.GetTempPath(), "p2_system.txt");
        File.WriteAllText(promptFile, Cfg.Persona + Cfg.ToolSection);
        var v = Process.Start(new ProcessStartInfo(ClaudeProc.ClaudePath(), "--version") { RedirectStandardOutput = true, UseShellExecute = false })!;
        Console.WriteLine("claude " + v.StandardOutput.ReadToEnd().Trim() + $"  model={Cfg.Model}");

        var outs = new List<Outcome>();
        foreach (var req in Script())
        {
            var o = await RunRequest(req);
            outs.Add(o);
            var calls = string.Join(",", o.Parsed1.Calls.Select(c => c.Name + (c.Valid ? "" : "!")));
            Console.WriteLine($"{req.Id,2} [{req.Cat}] p1 fw={o.P1.FirstWordMs:F0} tot={o.P1.TotalMs:F0}" +
                (o.P2 != null ? $" | p2 fw={o.P2.FirstWordMs:F0} tot={o.P2.TotalMs:F0}" : "") +
                $" calls=[{calls}] {(o.Failed() ? "FAIL " + o.Notes : "ok")}");
        }
        WriteFiles(outs);
        Summarize(outs);
    }

    static string Csv(string s) => "\"" + s.Replace("\"", "'").Replace("\r", " ").Replace("\n", " ") + "\"";
    static string F(double? d) => d.HasValue ? d.Value.ToString("F0", IC) : "";

    static void WriteFiles(List<Outcome> outs)
    {
        var dir = AppContext.BaseDirectory;
        var sb = new StringBuilder("id,cat,message,p1_first_ms,p1_total_ms,p2_first_ms,p2_total_ms,final_first_ms,final_total_ms,calls,valid_calls,malformed,leak,missed,wrong_args,spurious,repeat_p2,p1_block,p2_block,mood_tag,in_tokens,out_tokens,cost_usd,notes\n");
        foreach (var o in outs)
        {
            var calls = string.Join("+", o.Parsed1.Calls.Select(c => c.Name));
            var u1 = o.P1.U; var u2 = o.P2?.U;
            long inT = (u1?.In ?? 0) + (u1?.CacheCreate ?? 0) + (u1?.CacheRead ?? 0) + (u2?.In ?? 0) + (u2?.CacheCreate ?? 0) + (u2?.CacheRead ?? 0);
            long outT = (u1?.Out ?? 0) + (u2?.Out ?? 0);
            double cost = (u1?.Cost ?? 0) + (u2?.Cost ?? 0);
            sb.AppendLine(string.Join(",", o.R.Id, o.R.Cat, Csv(o.R.Msg), F(o.P1.FirstWordMs), F(o.P1.TotalMs), F(o.P2?.FirstWordMs), F(o.P2?.TotalMs),
                F(o.FinalFirstWordMs), F(o.FinalTotalMs), calls, o.Parsed1.Calls.Count(c => c.Valid), o.Malformed ? 1 : 0, o.Leak ? 1 : 0,
                o.Missed, o.WrongArgs, o.Spurious, o.Repeat2, o.P1.FirstBlock, o.P2?.FirstBlock ?? "", MoodEnd.IsMatch(o.Display) || MoodEnd.IsMatch((o.P2 ?? o.P1).Reply) ? 1 : 0,
                inT, outT, cost.ToString("F5", IC), Csv(o.Notes)));
        }
        File.WriteAllText(Path.Combine(dir, "requests.csv"), sb.ToString());
        File.WriteAllLines(Path.Combine(dir, "replies.jsonl"), outs.Select(o => JsonSerializer.Serialize(new
        {
            o.R.Id, o.R.Cat, o.R.Msg, pass1 = o.P1.Reply, toolResults = o.Results, pass2 = o.P2?.Reply, displayed = o.Display,
            failed = o.Failed(), o.Notes
        }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })));
        Console.WriteLine("requests.csv and replies.jsonl written to " + dir);
    }

    static string Stat(IEnumerable<double> v)
    {
        var a = v.OrderBy(x => x).ToList(); if (a.Count == 0) return "n/a";
        double Pct(double p) => a[Math.Min(a.Count - 1, (int)Math.Ceiling(p * a.Count) - 1)];
        return $"{Pct(0.5):F0} / {Pct(0.9):F0} (n={a.Count})";
    }

    static void Summarize(List<Outcome> outs)
    {
        int N(Func<Outcome, bool> f) => outs.Count(f);
        var tool = outs.Where(o => o.R.Exp.Length > 0).ToList();
        Console.WriteLine("\n## Summary (50 requests, " + Cfg.Model + ")");
        Console.WriteLine($"infra failures (timeout/no result, after 1 retry): {N(o => o.Infra)}");
        Console.WriteLine("\n| Group | Requests | Malformed | Leaked | Missed | Wrong args | Spurious |");
        Console.WriteLine("| --- | --- | --- | --- | --- | --- | --- |");
        foreach (var (name, set) in new[] { ("one tool", outs.Where(o => o.R.Cat == "one").ToList()), ("two tools", outs.Where(o => o.R.Cat == "two").ToList()),
            ("no tool", outs.Where(o => o.R.Cat == "none").ToList()), ("all", outs) })
            Console.WriteLine($"| {name} | {set.Count} | {set.Count(o => o.Malformed)} | {set.Count(o => o.Leak)} | {set.Count(o => o.Missed > 0)} | {set.Count(o => o.WrongArgs > 0)} | {set.Count(o => o.Spurious > 0)} |");
        int prot = N(o => o.Failed(true));
        Console.WriteLine($"\nprotocol-failed requests (malformed or leaked): {prot}/50");
        Console.WriteLine($"judgment-failed requests (missed, wrong args or spurious): {N(o => o.Missed > 0 || o.WrongArgs > 0 || o.Spurious > 0)}/50");
        Console.WriteLine($"any failure: {N(o => o.Failed())}/50");
        Console.WriteLine($"expected tool calls: {tool.Sum(o => o.R.Exp.Length)}; valid calls made in pass 1: {outs.Sum(o => o.Parsed1.Calls.Count(c => c.Valid))}; repeat calls in pass 2: {outs.Sum(o => o.Repeat2)}");
        Console.WriteLine($"thinking blocks first: pass1 {N(o => o.P1.FirstBlock == "thinking")}, pass2 {N(o => o.P2?.FirstBlock == "thinking")}");
        Console.WriteLine($"preamble text before first tag: {N(o => o.Parsed1.Calls.Count > 0 && o.Parsed1.Cleaned.Length > 0)} of {N(o => o.Parsed1.Calls.Count > 0)} tool replies");
        var final = outs.Select(o => o.P2 ?? o.P1).ToList();
        Console.WriteLine($"mood tag at the end of the displayed reply: {final.Count(r => MoodEnd.IsMatch(r.Reply.Trim()))}/50 (final pass)");

        var loop = outs.Where(o => o.P2 != null && o.P2.GotResult).ToList();
        var noLoop = outs.Where(o => o.P2 == null).ToList();
        Console.WriteLine("\n## Timing, ms (median / p90)");
        Console.WriteLine($"tool turns (n={loop.Count}):");
        Console.WriteLine($"  pass 1 first word (from spawn):          {Stat(loop.Where(o => o.P1.FirstWordMs.HasValue).Select(o => o.P1.FirstWordMs!.Value))}");
        Console.WriteLine($"  pass 1 total (tool call complete):       {Stat(loop.Where(o => o.P1.TotalMs.HasValue).Select(o => o.P1.TotalMs!.Value))}");
        Console.WriteLine($"  pass 2 first word (from its own spawn):  {Stat(loop.Where(o => o.P2!.FirstWordMs.HasValue).Select(o => o.P2!.FirstWordMs!.Value))}");
        Console.WriteLine($"  pass 2 total:                            {Stat(loop.Where(o => o.P2!.TotalMs.HasValue).Select(o => o.P2!.TotalMs!.Value))}");
        Console.WriteLine($"  first word of final reply (from Enter):  {Stat(loop.Where(o => o.FinalFirstWordMs.HasValue).Select(o => o.FinalFirstWordMs!.Value))}");
        Console.WriteLine($"  final reply complete (from Enter):       {Stat(loop.Where(o => o.FinalTotalMs.HasValue).Select(o => o.FinalTotalMs!.Value))}");
        Console.WriteLine($"no-tool turns (n={noLoop.Count}):");
        Console.WriteLine($"  first word / total:                      {Stat(noLoop.Where(o => o.P1.FirstWordMs.HasValue).Select(o => o.P1.FirstWordMs!.Value))}  /  {Stat(noLoop.Where(o => o.P1.TotalMs.HasValue).Select(o => o.P1.TotalMs!.Value))}");

        Console.WriteLine("\n## Failures");
        foreach (var o in outs.Where(o => o.Failed()))
            Console.WriteLine($"{o.R.Id,2} [{o.R.Cat}] \"{o.R.Msg}\" -> {o.Notes}");
        var cost = outs.Sum(o => (o.P1.U?.Cost ?? 0) + (o.P2?.U?.Cost ?? 0));
        Console.WriteLine($"\nclaude processes run: {outs.Count + loop.Count}; reported cost ${cost:F3}");
    }
}
