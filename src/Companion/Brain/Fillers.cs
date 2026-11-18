namespace Companion.Brain;

/// <summary>When fillers appear (D-058). The only place this is tuned.</summary>
internal static class FillerTuning
{
    /// <summary>No real text this long after Enter: show a filler. Nothing else triggers one.</summary>
    public const int NoWordMs = 1500;
}

/// <summary>
/// Short local lines shown in the bubble while the real reply is slow (D-058). No model call, never saved to
/// history. The lines are the "fillers" list of the personality file (one list per mode); with none given the
/// built-in default personality's list is used.
/// </summary>
internal sealed class Fillers
{
    private readonly Func<IReadOnlyList<string>> _source;
    private readonly Random _rng;
    private int _last = -1;

    /// <param name="lines">A fixed list (tests); null or empty uses the built-in personality's fillers.</param>
    public Fillers(IReadOnlyList<string>? lines = null, Random? rng = null)
        : this(() => lines is { Count: > 0 } ? lines : Personality.Default.Fillers!, rng) { }

    /// <summary>Reads the list on every use, so an edited personality file applies at once.</summary>
    public Fillers(Func<IReadOnlyList<string>> source, Random? rng = null)
    {
        _source = source;
        _rng = rng ?? new Random();
    }

    /// <summary>A random line, never the same one twice in a row (when there is more than one line).</summary>
    public string Next()
    {
        var lines = _source();
        if (lines.Count == 0) lines = Personality.Default.Fillers!;
        int i = lines.Count == 1 ? 0 : _rng.Next(lines.Count - 1);
        if (lines.Count > 1 && _last >= 0 && i >= _last) i++;   // skip over the previous index
        if (i >= lines.Count) i = 0;                            // the list got shorter between two uses
        _last = i;
        return lines[i];
    }
}
