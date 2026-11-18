using System.IO;
using System.Reflection;
using System.Text.Json;
using Companion.Core;

namespace Companion.Brain;

internal sealed class PersonalityExample
{
    public string User { get; set; } = "";
    public string Cortana { get; set; } = "";
}

/// <summary>
/// personality.normal.json (D-044: "version": 1). Name and character live in this text file so they change without
/// recompiling. Anything missing from an edited file falls back to the built-in default, field by field.
/// </summary>
internal sealed class Personality
{
    public int Version { get; set; } = 1;
    public string? Name { get; set; }
    public string? User { get; set; }
    public string? Language { get; set; }
    public string? Identity { get; set; }
    public string? Tone { get; set; }
    public string? ReplyLength { get; set; }
    public Dictionary<string, string[]>? Flirting { get; set; }
    public string[]? Boundaries { get; set; }
    public string? Nicknames { get; set; }
    public PersonalityExample[]? Examples { get; set; }
    /// <summary>Local lines shown while a reply is slow (D-058). Never sent to the model.</summary>
    public string[]? Fillers { get; set; }
    public string? Moods { get; set; }

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
    };

    private const string ResourceName = "personality.normal.json";
    private static Personality? _default;
    private static string? _defaultText;

    /// <summary>The raw text of the built-in default (also what is written to %APPDATA% the first time).</summary>
    public static string DefaultText
    {
        get
        {
            if (_defaultText != null) return _defaultText;
            using var s = typeof(Personality).Assembly.GetManifestResourceStream(ResourceName)
                          ?? throw new InvalidOperationException("Built-in personality missing: " + ResourceName);
            using var r = new StreamReader(s);
            return _defaultText = r.ReadToEnd();
        }
    }

    public static Personality Default => _default ??= JsonSerializer.Deserialize<Personality>(DefaultText, Json)!;

    /// <summary>Parses a personality file. Throws JsonException on bad JSON; fills gaps from the default.</summary>
    public static Personality Parse(string json)
    {
        var p = JsonSerializer.Deserialize<Personality>(json, Json) ?? throw new JsonException("empty personality file");
        p.FillGaps(Default);
        return p;
    }

    private void FillGaps(Personality d)
    {
        Name = Pick(Name, d.Name); User = Pick(User, d.User); Language = Pick(Language, d.Language);
        Identity = Pick(Identity, d.Identity); Tone = Pick(Tone, d.Tone); ReplyLength = Pick(ReplyLength, d.ReplyLength);
        Nicknames = Pick(Nicknames, d.Nicknames); Moods = Pick(Moods, d.Moods);
        Boundaries = Boundaries is { Length: > 0 } ? Boundaries : d.Boundaries;
        Examples = Examples is { Length: > 0 } ? Examples : d.Examples;
        Fillers = Fillers is { Length: > 0 } ? Fillers : d.Fillers;
        Flirting ??= new();
        foreach (var (level, lines) in d.Flirting!) if (!Flirting.TryGetValue(level, out var l) || l.Length == 0) Flirting[level] = lines;
    }

    private static string Pick(string? mine, string? fallback) => string.IsNullOrWhiteSpace(mine) ? fallback! : mine;

    /// <summary>The instructions for one flirt level (0-4). Only these are sent, so a change applies from the next message.</summary>
    public IReadOnlyList<string> FlirtLines(int level)
    {
        level = Math.Clamp(level, 0, 4);
        return Flirting!.TryGetValue(level.ToString(), out var lines) && lines.Length > 0 ? lines : Array.Empty<string>();
    }
}

/// <summary>
/// Where the personality comes from: the file in %APPDATA%\Companion\ (created from the default if missing),
/// reloaded when it changes on disk, or the built-in default when there is no path. A corrupt file is copied to
/// .unreadable.json and the default is used (D-047).
/// </summary>
internal sealed class PersonalitySource
{
    private readonly string? _path;
    private Personality _current;
    private DateTime _stamp = DateTime.MinValue;
    private bool _loadedOnce;

    public PersonalitySource(string? path) { _path = path; _current = Personality.Default; }

    public Personality Current
    {
        get
        {
            if (_path == null) return _current;
            try
            {
                if (!File.Exists(_path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    File.WriteAllText(_path, Personality.DefaultText);
                }
                var stamp = File.GetLastWriteTimeUtc(_path);
                if (_loadedOnce && stamp == _stamp) return _current;
                _stamp = stamp; _loadedOnce = true;
                _current = Personality.Parse(File.ReadAllText(_path));
            }
            catch (JsonException)
            {
                try { File.Copy(_path, Path.ChangeExtension(_path, ".unreadable.json"), overwrite: true); } catch (IOException) { }
                Log.Event("personality_unreadable");
                _current = Personality.Default;
            }
            catch (IOException) { /* keep what we have */ }
            return _current;
        }
    }
}
