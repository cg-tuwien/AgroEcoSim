namespace Agro.Mechanics;

/// <summary>
/// Reusable bounded thread-pool dispatch for independent numerical batches.
/// Work items and synchronization belong to an active solver, never an organ.
/// Calls must be sequential; completion joins all workers, including after errors.
/// </summary>
internal sealed class ShellParallelWork
{
    sealed class Item(ShellParallelWork owner, int index) : IThreadPoolWorkItem
    {
        public void Execute()
        {
            try { owner.Invoke(index); }
            finally { if (Interlocked.Decrement(ref owner.remaining) == 0) owner.completed.Set(); }
        }
    }

    readonly Action<int> action;
    readonly Item[] items;
    readonly ManualResetEventSlim completed = new(false);
    System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure;
    int remaining;
    internal int Capacity => items.Length;
    internal long ArrayPayloadBytes => (long)items.Length * IntPtr.Size;

    internal ShellParallelWork(int capacity, Action<int> action)
    {
        if (capacity < 2 || capacity > 32) throw new ArgumentOutOfRangeException(nameof(capacity));
        ArgumentNullException.ThrowIfNull(action);
        this.action = action; items = new Item[capacity];
        for (var i = 1; i < capacity; ++i) items[i] = new(this, i);
    }

    void Invoke(int index)
    {
        try { action(index); }
        catch (Exception e)
        { Interlocked.CompareExchange(ref failure, System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e), null); }
    }

    internal void Run(int workers)
    {
        if (workers < 1 || workers > Capacity) throw new ArgumentOutOfRangeException(nameof(workers));
        failure = null;
        if (workers == 1) { Invoke(0); failure?.Throw(); return; }
        completed.Reset(); remaining = workers - 1;
        for (var i = 1; i < workers; ++i) ThreadPool.UnsafeQueueUserWorkItem(items[i], preferLocal: false);
        Invoke(0);
        completed.Wait();
        failure?.Throw();
    }
}
