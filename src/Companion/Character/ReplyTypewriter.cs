using System.Diagnostics;
using System.Windows.Threading;

namespace Companion.Character;

/// <summary>
/// Drives <see cref="TypewriterPacer"/> with a UI-thread timer that runs only while text is waiting to be shown
/// (D-063): when everything is revealed the timer stops, so a finished reply or an idle orb costs nothing.
/// The first character of a reply is revealed synchronously inside <see cref="Push"/>.
/// </summary>
internal sealed class ReplyTypewriter
{
    private readonly TypewriterPacer _pacer = new();
    private readonly DispatcherTimer _timer;
    private readonly Action<string> _reveal;
    private readonly Action _drained;
    private long _last = Stopwatch.GetTimestamp();

    /// <param name="reveal">Called with each piece of text to show.</param>
    /// <param name="drained">Called whenever everything pushed so far has been shown.</param>
    public ReplyTypewriter(Action<string> reveal, Action drained)
    {
        _reveal = reveal; _drained = drained;
        _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(TypewriterTuning.TickMs) };
        _timer.Tick += (_, _) => Tick();
    }

    public bool HasPending => _pacer.HasPending;
    public bool IsRunning => _timer.IsEnabled;

    /// <summary>New reply, or an interrupt: forget what is buffered and stop the timer (text stops where it is).</summary>
    public void Reset()
    {
        _timer.Stop();
        _pacer.Reset();
        _last = Stopwatch.GetTimestamp();
    }

    /// <summary>Shows everything that is waiting right now (a click on the bubble) and stops the timer.</summary>
    public void Flush()
    {
        string rest = _pacer.Flush();
        _timer.Stop();
        _last = Stopwatch.GetTimestamp();
        if (rest.Length > 0) _reveal(rest);
        _drained();
    }

    public void Push(string text)
    {
        if (text.Length == 0) return;
        if (!_timer.IsEnabled)
        {
            _pacer.Advance(ElapsedMs());   // lets a pause that was running out the last character finish in real time
            _pacer.Push(text);
            Tick();                        // reveals the first character now when this is the start of a reply
            return;
        }
        _pacer.Push(text);
    }

    private void Tick()
    {
        string s = _pacer.Advance(ElapsedMs());
        if (s.Length > 0) _reveal(s);
        if (_pacer.HasPending) { if (!_timer.IsEnabled) _timer.Start(); }
        else { _timer.Stop(); _drained(); }
    }

    private double ElapsedMs()
    {
        long now = Stopwatch.GetTimestamp();
        double ms = Stopwatch.GetElapsedTime(_last, now).TotalMilliseconds;
        _last = now;
        return Math.Min(ms, TypewriterTuning.MaxStepMs);
    }
}
