using System.Collections.Concurrent;
using Godot;
using GodotFrontend.Core.Threading;

namespace GodotFrontend.App;

/// <summary>
/// The main-thread pump: a queue drained in _Process. Only the actions present at the start of a drain run in that
/// frame (an action that posts again runs next frame, never re-entrantly), with a soft time budget so a burst of
/// posts cannot stall rendering. FIFO order is what keeps RunStarted, RunSite and the first Preview in sequence.
/// </summary>
public partial class GodotMainThread : Node, IMainThread
{
    private readonly ConcurrentQueue<Action> queue = new();
    private readonly List<(long dueMs, Action action)> delayed = new();
    private readonly int threadId = System.Environment.CurrentManagedThreadId;
    private const double BudgetMs = 4;

    public void Post(Action action) => queue.Enqueue(action);

    public void PostDelayed(int milliseconds, Action action)
    {
        var due = (long)Time.GetTicksMsec() + milliseconds;
        lock (delayed) delayed.Add((due, action));
    }

    public bool IsCurrent => System.Environment.CurrentManagedThreadId == threadId;

    public override void _Process(double delta)
    {
        var n = queue.Count;
        var start = Time.GetTicksUsec();
        for (var i = 0; i < n && queue.TryDequeue(out var a); ++i)
        {
            try { a(); }
            catch (Exception ex) { GD.PrintErr($"main-thread action failed: {ex}"); }
            if ((Time.GetTicksUsec() - start) / 1000.0 > BudgetMs) break;
        }
        List<Action>? due = null;
        lock (delayed)
        {
            var now = (long)Time.GetTicksMsec();
            for (var i = delayed.Count - 1; i >= 0; --i)
                if (delayed[i].dueMs <= now)
                {
                    (due ??= new()).Add(delayed[i].action);
                    delayed.RemoveAt(i);
                }
        }
        if (due != null)
            foreach (var a in due)
            {
                try { a(); }
                catch (Exception ex) { GD.PrintErr($"delayed action failed: {ex}"); }
            }
    }
}
