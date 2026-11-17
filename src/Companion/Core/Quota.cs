using System.IO;
using System.Text.Json;

namespace Companion.Core;

/// <summary>
/// Daily call counter per model for the plan quota (D-029). Step 2 only counts; the caps and the settings
/// display come with the settings window (step 4). Stored in quota.json, resets when the date changes.
/// With no path it stays in memory (tests and the perfcheck chat probe).
/// </summary>
internal sealed class Quota
{
    private sealed class Data
    {
        public int Version { get; set; } = 1;
        public string Date { get; set; } = "";
        public Dictionary<string, int> Counts { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly string? _path;
    private readonly Func<DateTime> _now;
    private Data _data = new();

    public Quota(string? path, Func<DateTime>? now = null)
    {
        _path = path;
        _now = now ?? (() => DateTime.Now);
        Load();
    }

    /// <summary>"claude-haiku-4-5-..." becomes "haiku", anything unknown is kept as is.</summary>
    public static string ModelKey(string model)
    {
        var m = model.ToLowerInvariant();
        foreach (var k in new[] { "haiku", "sonnet", "opus" }) if (m.Contains(k)) return k;
        return m.Length == 0 ? "unknown" : m;
    }

    public int CountToday(string model) { Roll(); return _data.Counts.GetValueOrDefault(ModelKey(model)); }
    public int TotalToday() { Roll(); return _data.Counts.Values.Sum(); }

    public void Record(string model)
    {
        Roll();
        var k = ModelKey(model);
        _data.Counts[k] = _data.Counts.GetValueOrDefault(k) + 1;
        Save();
    }

    private void Roll()
    {
        var today = _now().ToString("yyyy-MM-dd");
        if (_data.Date != today) _data = new Data { Date = today };
    }

    private void Load()
    {
        if (_path == null || !File.Exists(_path)) return;
        try { _data = JsonSerializer.Deserialize<Data>(File.ReadAllText(_path), Json) ?? new Data(); }
        catch (JsonException)
        {
            // D-047: corrupt JSON -> keep a copy, start from defaults.
            try { File.Copy(_path, Path.ChangeExtension(_path, ".unreadable.json"), overwrite: true); } catch (IOException) { }
            _data = new Data();
        }
        catch (IOException) { }
    }

    private void Save()
    {
        if (_path == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_data, Json));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (IOException) { }
    }
}
