namespace GodotFrontend.Core.Threading;

/// <summary>
/// The one place background work hands results to the main thread. SignalR handlers, HTTP continuations and the
/// decode worker never touch model or engine state directly; they post an action and the host drains the queue once
/// per frame, in order. Tests use <see cref="ManualPump"/>.
/// </summary>
public interface IMainThread
{
    void Post(Action action);
    void PostDelayed(int milliseconds, Action action);
    bool IsCurrent { get; }
}

/// <summary>A pump driven by hand, for tests and the console harness.</summary>
public sealed class ManualPump : IMainThread
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> queue = new();
    private readonly List<(DateTime due, Action action)> delayed = new();
    private readonly int threadId = Environment.CurrentManagedThreadId;

    public void Post(Action action) => queue.Enqueue(action);

    public void PostDelayed(int milliseconds, Action action)
    {
        lock (delayed) delayed.Add((DateTime.UtcNow.AddMilliseconds(milliseconds), action));
    }

    public bool IsCurrent => Environment.CurrentManagedThreadId == threadId;

    /// <summary>
    /// Makes `await` continuations in core code come back through this pump, as Godot's own dispatcher does on
    /// the main thread. Call once on the thread that drains the pump.
    /// </summary>
    public void InstallSynchronizationContext() => SynchronizationContext.SetSynchronizationContext(new PumpContext(this));

    private sealed class PumpContext : SynchronizationContext
    {
        private readonly ManualPump pump;
        public PumpContext(ManualPump pump) => this.pump = pump;
        public override void Post(SendOrPostCallback d, object? state) => pump.Post(() => d(state));
        public override void Send(SendOrPostCallback d, object? state)
        {
            if (pump.IsCurrent) d(state); else throw new InvalidOperationException("Send from a worker thread is not supported");
        }
        public override SynchronizationContext CreateCopy() => this;
    }

    /// <summary>Runs everything queued at the time of the call (not what those actions enqueue), then the due delayed actions.</summary>
    public int Drain()
    {
        var n = queue.Count;
        var ran = 0;
        for (var i = 0; i < n && queue.TryDequeue(out var a); ++i) { a(); ran++; }
        List<Action>? due = null;
        lock (delayed)
        {
            var now = DateTime.UtcNow;
            for (var i = delayed.Count - 1; i >= 0; --i)
                if (delayed[i].due <= now)
                {
                    (due ??= new()).Add(delayed[i].action);
                    delayed.RemoveAt(i);
                }
        }
        if (due != null) foreach (var a in due) { a(); ran++; }
        return ran;
    }

    /// <summary>Drains until the predicate holds or the timeout passes; returns whether it held.</summary>
    public bool DrainUntil(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!predicate())
        {
            if (Drain() == 0) Thread.Sleep(5);
            if (DateTime.UtcNow > deadline) return false;
        }
        return true;
    }
}
