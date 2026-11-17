namespace Companion.Brain;

/// <summary>When fillers appear (D-058). The only place this is tuned.</summary>
internal static class FillerTuning
{
    /// <summary>No real text this long after Enter: show a filler. Nothing else triggers one.</summary>
    public const int NoWordMs = 1500;
}

/// <summary>
/// Short local lines shown in the bubble while the real reply is slow (D-058). No model call, never saved to
/// history. TEMPORARY list: step 3 moves these into the personality file as "fillers", one list per mode.
/// </summary>
internal sealed class Fillers
{
    public static readonly string[] Temporary =
    {
        "Hmm...", "Let me think...", "Give me a sec, hotshot.", "One moment...",
        "Let me see...", "Hang on, partner.", "Working on it...", "Mm, good question.",
    };

    private readonly IReadOnlyList<string> _lines;
    private readonly Random _rng;
    private int _last = -1;

    public Fillers(IReadOnlyList<string>? lines = null, Random? rng = null)
    {
        _lines = lines is { Count: > 0 } ? lines : Temporary;
        _rng = rng ?? new Random();
    }

    /// <summary>A random line, never the same one twice in a row (when there is more than one line).</summary>
    public string Next()
    {
        int i = _lines.Count == 1 ? 0 : _rng.Next(_lines.Count - 1);
        if (_lines.Count > 1 && _last >= 0 && i >= _last) i++;   // skip over the previous index
        _last = i;
        return _lines[i];
    }
}
