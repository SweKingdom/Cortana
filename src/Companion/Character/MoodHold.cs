using Companion.Core;

namespace Companion.Character;

/// <summary>
/// Which expression the orb holds between messages (D-070). A reply with a mood tag sets it; a reply without a tag,
/// an interrupted reply and a failed turn leave it as it was; [idle] sets it to none. After <c>hold</c> of quiet it
/// asks for the colour to fade back to idle (the fade itself is the animator's). The wait is one one-shot timer that
/// starts when a turn ends and stops when a message is sent, so nothing runs while she is held. The scheduler is
/// injected so a test can drive time.
/// </summary>
internal sealed class MoodHold
{
    private readonly TimeSpan _hold;
    private readonly Func<TimeSpan, Action, IDisposable> _schedule;
    private readonly Action _fade;
    private IDisposable? _timer;

    public MoodHold(TimeSpan hold, Func<TimeSpan, Action, IDisposable> schedule, Action fade)
    {
        _hold = hold; _schedule = schedule; _fade = fade;
    }

    /// <summary>The expression held now.</summary>
    public Expression Current { get; private set; } = Expression.None;

    /// <summary>A reply finished (or ended): apply its tag if it has one and start the quiet wait.</summary>
    public Expression Reply(Mood? mood, bool interrupted = false)
    {
        if (!interrupted && mood is { } m) Current = ExpressionMap.From(m);
        Quiet();
        return Current;
    }

    /// <summary>A turn ended without a usable reply (failed): nothing changes, the wait starts.</summary>
    public void Quiet()
    {
        Stop();
        if (Current != Expression.None) _timer = _schedule(_hold, OnQuiet);
    }

    /// <summary>A message was sent: no waiting while she is busy.</summary>
    public void Chatting() => Stop();

    /// <summary>The fade finished: nothing is held any more.</summary>
    public void Faded() => Current = Expression.None;

    private void OnQuiet()
    {
        Stop();
        _fade();
    }

    private void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
