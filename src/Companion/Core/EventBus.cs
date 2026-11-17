namespace Companion.Core;

/// <summary>
/// Plain pub/sub between modules (architecture: "notifications go on the bus"). UI thread only:
/// publishers and subscribers all run on the dispatcher thread, so there is no locking.
/// </summary>
internal sealed class EventBus
{
    private readonly Dictionary<Type, List<Delegate>> _subs = new();

    public IDisposable Subscribe<T>(Action<T> handler)
    {
        if (!_subs.TryGetValue(typeof(T), out var list)) _subs[typeof(T)] = list = new List<Delegate>();
        list.Add(handler);
        return new Unsubscriber(() => list.Remove(handler));
    }

    public void Publish<T>(T e)
    {
        if (!_subs.TryGetValue(typeof(T), out var list)) return;
        foreach (var h in list.ToArray()) ((Action<T>)h)(e); // copy: handlers may subscribe or unsubscribe
    }

    /// <summary>
    /// Remembers the current TurnId from <see cref="TurnStarted"/> so listeners can drop stale events (D-038):
    /// a late chunk from an interrupted reply must never reach the next bubble.
    /// </summary>
    public TurnTracker TrackTurns() => new(this);

    private sealed class Unsubscriber(Action remove) : IDisposable
    {
        public void Dispose() => remove();
    }
}

internal sealed class TurnTracker
{
    public int Current { get; private set; }
    public TurnTracker(EventBus bus) => bus.Subscribe<TurnStarted>(e => Current = e.TurnId);
    public bool IsCurrent(int turnId) => turnId == Current;
}
