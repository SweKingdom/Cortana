using System.Text;

namespace Companion.Character;

/// <summary>
/// The pacing logic of the typewriter display (D-063), with no timer and no UI: text goes in with <see cref="Push"/>,
/// <see cref="Advance"/> says how much of it to show after a given amount of time. Pace is
/// <see cref="TypewriterTuning.BaseCps"/>, rising gradually when a lot is buffered; short pauses follow sentence
/// ends and commas. The first character of a reply is shown at once (no added delay to the first word).
/// </summary>
internal sealed class TypewriterPacer
{
    private string _buf = "";
    private int _pos;
    private double _credit;   // time available for the next character
    private double _hold;     // pause still to wait out
    private bool _fresh = true;

    public bool HasPending => _pos < _buf.Length;
    public int PendingChars => _buf.Length - _pos;

    /// <summary>Characters per second for the given number of buffered characters.</summary>
    public static double RateCps(int pending)
        => Math.Min(TypewriterTuning.MaxCps,
                    TypewriterTuning.BaseCps + Math.Max(0, pending - TypewriterTuning.CatchUpFromChars) * TypewriterTuning.CatchUpGainCps);

    /// <summary>Starts a new reply: drops anything buffered (also what an interrupt does: stop where it is).</summary>
    public void Reset()
    {
        _buf = ""; _pos = 0; _credit = 0; _hold = 0; _fresh = true;
    }

    /// <summary>Everything still buffered, at once (a click on the bubble). Pauses owed are dropped.</summary>
    public string Flush()
    {
        string rest = _buf.Substring(_pos);
        _buf = ""; _pos = 0; _credit = 0; _hold = 0; _fresh = false;
        return rest;
    }

    public void Push(string text)
    {
        if (text.Length == 0) return;
        _buf = _buf.Substring(_pos) + text;
        _pos = 0;
    }

    /// <summary>Time has passed: returns the text to reveal now (empty when it is not time yet).</summary>
    public string Advance(double dtMs)
    {
        if (!HasPending)
        {
            _hold = Math.Max(0, _hold - dtMs);   // a pause keeps running while nothing is buffered
            _credit = 0;                          // and idle time never turns into a burst of text later
            return "";
        }
        _credit += dtMs;
        var sb = new StringBuilder();
        while (HasPending)
        {
            if (_hold > 0)
            {
                double use = Math.Min(_hold, _credit);
                _hold -= use; _credit -= use;
                if (_hold > 0) break;
            }
            double interval = 1000.0 / RateCps(PendingChars);
            if (_fresh) { _credit = Math.Max(_credit, interval); _fresh = false; }   // first character at once
            if (_credit < interval) break;
            _credit -= interval;
            char c = _buf[_pos++];
            sb.Append(c);
            _hold = PauseAfter(c, HasPending ? _buf[_pos] : (char?)null);
        }
        if (!HasPending) _credit = 0;
        return sb.ToString();
    }

    private static double PauseAfter(char c, char? next)
    {
        bool boundary = next is null || char.IsWhiteSpace(next.Value);   // "3.5" and "..." do not pause mid-way
        if (!boundary) return 0;
        return c switch
        {
            '.' or '!' or '?' => TypewriterTuning.SentencePauseMs,
            ',' or ';' or ':' => TypewriterTuning.CommaPauseMs,
            _ => 0,
        };
    }
}
