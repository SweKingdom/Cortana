using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Companion.Core;

namespace Companion.Memory;

/// <summary>fact and preference live in facts.md (preferences under "## Preferences"); impression in impressions.md (D-042).</summary>
internal enum MemoryKind { Fact, Preference, Impression }

/// <summary>One memory line: <c>- [f12 | 2026-11-16 | chat] text</c>, a correction adds <c>| replaces f9</c> (D-042).</summary>
internal sealed record MemoryEntry(string Id, MemoryKind Kind, string Date, string Source, string? Replaces, string Text);

internal enum AddStatus { Added, Full, Duplicate, Invalid }
internal sealed record AddResult(AddStatus Status, MemoryEntry? Entry = null);

/// <summary>
/// The normal mode's memory folder (D-026, D-032, D-042): facts.md, impressions.md and summary.md. Each file is
/// capped at about 800 tokens (chars / 4); at the cap new entries are refused and the owner says so (a tidy-up,
/// approved by Philip, comes with the settings window in step 4). A correction is a new line that replaces an
/// older id; the old line stays in the file but is not sent to the model. With no folder it stays in memory
/// (tests and the perfcheck chat probe).
/// </summary>
internal sealed class MemoryStore
{
    public const int CapTokens = 800;
    public const int MaxTextChars = 200;
    public const string FactsFile = "facts.md", ImpressionsFile = "impressions.md", SummaryFile = "summary.md";
    private const string PreferencesHeading = "## Preferences";

    private static readonly Regex EntryLine = new(
        @"^- \[(?<id>[fi]\d+) \| (?<date>[^|\]]+?) \| (?<src>[^|\]]+?)(?: \| replaces (?<rep>[fi]\d+))?\] (?<text>.*)$",
        RegexOptions.Compiled);

    private readonly string? _dir;
    private readonly Func<DateTime> _now;
    private readonly Dictionary<string, string> _mem = new();   // used when there is no folder

    public MemoryStore(string? dir, Func<DateTime>? now = null) { _dir = dir; _now = now ?? (() => DateTime.Now); }

    public static int EstimateTokens(string s) => (s.Length + 3) / 4;

    // ---- raw files ------------------------------------------------------------------------------------------

    public string Read(string file)
    {
        if (_dir == null) return _mem.GetValueOrDefault(file, "");
        try { var p = Path.Combine(_dir, file); return File.Exists(p) ? File.ReadAllText(p) : ""; }
        catch (IOException) { return ""; }
    }

    private void Write(string file, string text)
    {
        if (_dir == null) { _mem[file] = text; return; }
        try
        {
            Directory.CreateDirectory(_dir);
            var p = Path.Combine(_dir, file);
            var tmp = p + ".tmp";
            File.WriteAllText(tmp, text);
            File.Move(tmp, p, overwrite: true);
        }
        catch (IOException) { Log.Event("memory_write_failed", ("file", file)); }
    }

    public string ReadSummary() => Read(SummaryFile).Trim();
    public void WriteSummary(string text) => Write(SummaryFile, text.Trim() + "\n");

    // ---- parsing ----------------------------------------------------------------------------------------------

    private static string FileFor(MemoryKind k) => k == MemoryKind.Impression ? ImpressionsFile : FactsFile;
    private static string Prefix(MemoryKind k) => k == MemoryKind.Impression ? "i" : "f";

    private static List<string> Lines(string text) => text.Replace("\r\n", "\n").Split('\n').ToList();

    private static MemoryEntry? ParseLine(string line, MemoryKind kindHint)
    {
        var m = EntryLine.Match(line);
        if (!m.Success) return null;
        var id = m.Groups["id"].Value;
        var kind = id[0] == 'i' ? MemoryKind.Impression : kindHint;
        return new MemoryEntry(id, kind, m.Groups["date"].Value, m.Groups["src"].Value,
            m.Groups["rep"].Success ? m.Groups["rep"].Value : null, m.Groups["text"].Value);
    }

    /// <summary>Every entry in a file, in order; "kind" is Preference for lines under the Preferences heading.</summary>
    private static List<MemoryEntry> Entries(string text, bool impressions)
    {
        var list = new List<MemoryEntry>();
        bool inPrefs = false;
        foreach (var line in Lines(text))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal)) inPrefs = line.TrimEnd().Equals(PreferencesHeading, StringComparison.OrdinalIgnoreCase);
            var e = ParseLine(line, impressions ? MemoryKind.Impression : inPrefs ? MemoryKind.Preference : MemoryKind.Fact);
            if (e != null) list.Add(e);
        }
        return list;
    }

    /// <summary>Entries of this kind that no later line has replaced.</summary>
    public IReadOnlyList<MemoryEntry> Active(MemoryKind kind) => ActiveInFile(kind).Where(e => e.Kind == kind).ToList();

    /// <summary>Every active entry of the file this kind lives in (facts.md holds facts and preferences).</summary>
    private List<MemoryEntry> ActiveInFile(MemoryKind kind)
    {
        var all = Entries(Read(FileFor(kind)), kind == MemoryKind.Impression);
        var replaced = all.Where(e => e.Replaces != null).Select(e => e.Replaces!).ToHashSet();
        return all.Where(e => !replaced.Contains(e.Id)).ToList();
    }

    // ---- what goes into the prompt ----------------------------------------------------------------------------

    /// <summary>
    /// The file as the model should see it: active entries as <c>- [id] text</c> (she needs the id to correct one),
    /// headings and hand-written lines unchanged, replaced entries left out. Empty when there is nothing.
    /// </summary>
    public string ForPrompt(string file)
    {
        var text = Read(file);
        if (string.IsNullOrWhiteSpace(text)) return "";
        bool impressions = file == ImpressionsFile;
        var all = Entries(text, impressions);
        var replaced = all.Where(e => e.Replaces != null).Select(e => e.Replaces!).ToHashSet();
        var sb = new StringBuilder();
        foreach (var line in Lines(text))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var e = ParseLine(line, MemoryKind.Fact);
            if (e == null) sb.Append(line.TrimEnd()).Append('\n');
            else if (!replaced.Contains(e.Id)) sb.Append("- [").Append(e.Id).Append("] ").Append(e.Text).Append('\n');
        }
        var result = sb.ToString().TrimEnd();
        // A file that only has an empty Preferences heading counts as empty.
        return result.Replace(PreferencesHeading, "").Trim().Length == 0 ? "" : result;
    }

    // ---- changes ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Adds a line. Full when the file would pass the cap (D-032); Duplicate when an active entry already says
    /// the same; Invalid for empty or too long text, or a "replaces" id that is not an active entry of that file.
    /// </summary>
    public AddResult Add(MemoryKind kind, string text, string? replaces = null, string source = "chat")
    {
        text = Regex.Replace(text ?? "", @"\s+", " ").Trim();
        if (text.Length == 0 || text.Length > MaxTextChars) return new AddResult(AddStatus.Invalid);

        var file = FileFor(kind);
        var raw = Read(file);
        var all = Entries(raw, kind == MemoryKind.Impression);
        var active = ActiveInFile(kind);
        if (replaces != null && active.All(e => e.Id != replaces)) return new AddResult(AddStatus.Invalid);
        if (active.Any(e => e.Text.Equals(text, StringComparison.OrdinalIgnoreCase) && e.Id != replaces)) return new AddResult(AddStatus.Duplicate);

        int next = all.Select(e => int.Parse(e.Id[1..])).DefaultIfEmpty(0).Max() + 1;
        var entry = new MemoryEntry(Prefix(kind) + next, kind, _now().ToString("yyyy-MM-dd"), source, replaces, text);
        var line = Format(entry);

        if (EstimateTokens(raw) + EstimateTokens(line + "\n") > CapTokens) return new AddResult(AddStatus.Full);

        var lines = Lines(raw);
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1])) lines.RemoveAt(lines.Count - 1);
        int heading = lines.FindIndex(l => l.TrimEnd().Equals(PreferencesHeading, StringComparison.OrdinalIgnoreCase));
        if (kind == MemoryKind.Preference)
        {
            if (heading < 0) { if (lines.Count > 0) lines.Add(""); lines.Add(PreferencesHeading); }
            lines.Add(line);
        }
        else if (kind == MemoryKind.Fact && heading >= 0)
        {
            int at = heading;
            while (at > 0 && string.IsNullOrWhiteSpace(lines[at - 1])) at--;   // keep the blank line before the heading
            lines.Insert(at, line);
        }
        else lines.Add(line);
        Write(file, string.Join("\n", lines) + "\n");
        return new AddResult(AddStatus.Added, entry);
    }

    private static string Format(MemoryEntry e)
        => $"- [{e.Id} | {e.Date} | {e.Source}{(e.Replaces != null ? " | replaces " + e.Replaces : "")}] {e.Text}";

    /// <summary>Undo for a note just saved: removes that line (if a correction, the line it replaced is active again). Null if it is gone.</summary>
    public MemoryEntry? Undo(string id)
    {
        foreach (var file in new[] { FactsFile, ImpressionsFile })
        {
            var lines = Lines(Read(file));
            for (int i = 0; i < lines.Count; i++)
            {
                var e = ParseLine(lines[i], MemoryKind.Fact);
                if (e == null || e.Id != id) continue;
                lines.RemoveAt(i);
                Write(file, string.Join("\n", lines).TrimEnd() + "\n");
                return e;
            }
        }
        return null;
    }

    /// <summary>Token estimate of one memory file (what the cap counts).</summary>
    public int Tokens(string file) => EstimateTokens(Read(file));
}
