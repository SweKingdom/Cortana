using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Companion.Core;

namespace Companion.Brain;

internal sealed class PersonalityExample
{
    public string User { get; set; } = "";
    public string Cortana { get; set; } = "";
    /// <summary>The example is only sent at this flirt level or higher (version 2). Missing = every level.</summary>
    public int? MinLevel { get; set; }
}

/// <summary>What she likes (version 2): her own interests and his.</summary>
internal sealed class PersonalityInterests
{
    public string? Hers { get; set; }
    public string? His { get; set; }
}

/// <summary>
/// personality.normal.json. Name and character live in this text file so they change without recompiling.
/// Version 2 (D-074) adds backstory, relationship, traits, interests, speech, nagging, never and postHistory, and
/// a minLevel on examples; version 1 files still load and behave as before. Anything missing from an edited file
/// falls back to the built-in default, field by field. The version 2 fields are only filled in for files that
/// say version 2 or higher, so an edited version 1 file does not silently change.
/// </summary>
internal sealed class Personality
{
    public int Version { get; set; } = 1;
    public string? Name { get; set; }
    public string? User { get; set; }
    public string? Language { get; set; }
    public string? Identity { get; set; }
    /// <summary>Version 1 only; version 2 describes the tone through traits and speech.</summary>
    public string? Tone { get; set; }
    public string? ReplyLength { get; set; }

    // Version 2 fields.
    public string? Backstory { get; set; }
    public string? Relationship { get; set; }
    public string[]? Traits { get; set; }
    public PersonalityInterests? Interests { get; set; }
    public string[]? Speech { get; set; }
    public string? Nagging { get; set; }
    public string? Never { get; set; }
    /// <summary>A reminder placed after the history, just before the new message (D-074).</summary>
    public string? PostHistory { get; set; }

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
    private const string V1ResourceName = "personality.v1.default.json";
    private static Personality? _default;
    private static string? _defaultText;
    private static string? _v1Text;

    private static string ReadResource(string name)
    {
        using var s = typeof(Personality).Assembly.GetManifestResourceStream(name)
                      ?? throw new InvalidOperationException("Built-in personality missing: " + name);
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    /// <summary>The raw text of the built-in default (also what is written to %APPDATA% the first time).</summary>
    public static string DefaultText => _defaultText ??= ReadResource(ResourceName);

    /// <summary>The version 1 default of step 3, only used to tell an unedited old file from an edited one.</summary>
    public static string V1DefaultText => _v1Text ??= ReadResource(V1ResourceName);

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
        Identity = Pick(Identity, d.Identity); ReplyLength = Pick(ReplyLength, d.ReplyLength);
        Nicknames = Pick(Nicknames, d.Nicknames); Moods = Pick(Moods, d.Moods);
        Boundaries = Boundaries is { Length: > 0 } ? Boundaries : d.Boundaries;
        Examples = Examples is { Length: > 0 } ? Examples : d.Examples;
        Fillers = Fillers is { Length: > 0 } ? Fillers : d.Fillers;
        Flirting ??= new();
        foreach (var (level, lines) in d.Flirting!) if (!Flirting.TryGetValue(level, out var l) || l.Length == 0) Flirting[level] = lines;

        if (Version >= 2)
        {
            Backstory = Pick(Backstory, d.Backstory); Relationship = Pick(Relationship, d.Relationship);
            Nagging = Pick(Nagging, d.Nagging); Never = Pick(Never, d.Never); PostHistory = Pick(PostHistory, d.PostHistory);
            Traits = Traits is { Length: > 0 } ? Traits : d.Traits;
            Speech = Speech is { Length: > 0 } ? Speech : d.Speech;
            Interests = new PersonalityInterests { Hers = Pick(Interests?.Hers, d.Interests?.Hers), His = Pick(Interests?.His, d.Interests?.His) };
        }
        else Tone = Pick(Tone, d.Tone); // d.Tone is null in version 2, so a version 1 file keeps only its own tone
    }

    private static string? Pick(string? mine, string? fallback) => string.IsNullOrWhiteSpace(mine) ? fallback : mine;

    /// <summary>The instructions for one flirt level (0-4). Only these are sent, so a change applies from the next message.</summary>
    public IReadOnlyList<string> FlirtLines(int level)
    {
        level = Math.Clamp(level, 0, 4);
        return Flirting!.TryGetValue(level.ToString(), out var lines) && lines.Length > 0 ? lines : Array.Empty<string>();
    }

    /// <summary>The examples for one flirt level: those whose minLevel is at or below it (a missing minLevel means 0).</summary>
    public IReadOnlyList<PersonalityExample> ExamplesFor(int level)
    {
        level = Math.Clamp(level, 0, 4);
        return Examples!.Where(e => (e.MinLevel ?? 0) <= level).ToList();
    }
}

/// <summary>What happened to a version 1 file when it was first loaded (D-074).</summary>
internal enum V1Outcome { NotV1, Upgraded, KeptEdited }

/// <summary>
/// Where the personality comes from: the file in %APPDATA%\Companion\ (created from the default if missing),
/// reloaded when it changes on disk, or the built-in default when there is no path. A corrupt file is copied to
/// .unreadable.json and the default is used (D-047). A version 1 file that is still the unedited step 3 default is
/// replaced by the version 2 default (the old one is kept as personality.normal.v1.json); an edited version 1 file
/// is left alone and a note is logged (D-074).
/// </summary>
internal sealed class PersonalitySource
{
    private readonly string? _path;
    private Personality _current;
    private DateTime _stamp = DateTime.MinValue;
    private bool _loadedOnce;

    public PersonalitySource(string? path) { _path = path; _current = Personality.Default; }

    /// <summary>Set the first time a version 1 file is seen; for tests and the log.</summary>
    public V1Outcome V1 { get; private set; } = V1Outcome.NotV1;

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
                var text = File.ReadAllText(_path);
                if (!_loadedOnce) text = UpgradeIfUneditedV1(text);
                _stamp = File.GetLastWriteTimeUtc(_path); _loadedOnce = true;
                _current = Personality.Parse(text);
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

    /// <summary>Checked once per run, on the first read. Returns the text to parse.</summary>
    private string UpgradeIfUneditedV1(string text)
    {
        var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        int version = node?["version"] is JsonValue v && v.TryGetValue<int>(out var vi) ? vi : 1;
        if (version >= 2) return text;

        if (JsonNode.DeepEquals(node, JsonNode.Parse(Personality.V1DefaultText)))
        {
            try
            {
                File.Copy(_path!, Path.Combine(Path.GetDirectoryName(_path!)!, "personality.normal.v1.json"), overwrite: true);
                File.WriteAllText(_path!, Personality.DefaultText);
                V1 = V1Outcome.Upgraded;
                Log.Event("personality_upgraded_to_v2");
                return Personality.DefaultText;
            }
            catch (IOException) { /* could not write: use the old file as it is and try again next run */ return text; }
        }
        V1 = V1Outcome.KeptEdited;
        Log.Event("personality_v1_kept_edited");
        return text;
    }
}
