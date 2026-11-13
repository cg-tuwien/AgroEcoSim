namespace GodotFrontend.Core.Playback;

/// <summary>appstate.ts PlayState, same order.</summary>
public enum PlayState { None, Backward, Forward, ForwardWaiting, SeekBackward, SeekForward, NextBackward, NextForward, Seek }

/// <summary>
/// The playback state machine of appstate.ts: one <see cref="Step"/> call is one run of the web's playRequest
/// effect. The host calls Step on a request, when a frame arrives while following the live edge, and a short delay
/// after a frame was presented (20 ms playing, 0 ms waiting at the live edge). ForwardWaiting follows the newest
/// frame that has ARRIVED, never one past the end, and drops to None once the run stops computing.
/// </summary>
public sealed class PlaybackMachine
{
    public PlayState Playing { get; private set; } = PlayState.None;
    public int Pointer { get; private set; }
    public bool Computing { get; set; }
    /// <summary>Index of the newest frame that arrived on the wire, or -1.</summary>
    public int LastReceivedIndex { get; set; } = -1;

    /// <summary>Raised with the history index to show; the host resolves and decodes it.</summary>
    public event Action<int>? Show;
    /// <summary>Raised whenever Playing or Pointer changed.</summary>
    public event Action? Changed;

    public bool AtLiveEdge => Playing == PlayState.ForwardWaiting;
    public bool IsPlaying => Playing != PlayState.None;

    /// <summary>play(): Forward at the end of the history becomes ForwardWaiting.</summary>
    public void Request(PlayState state, int historyCount)
    {
        if (state != PlayState.Forward || Pointer < historyCount - 1) Playing = state;
        else Playing = PlayState.ForwardWaiting;
        Changed?.Invoke();
    }

    /// <summary>The timeline slider: jump to an index and show it.</summary>
    public void SeekTo(int index, int historyCount)
    {
        Pointer = historyCount == 0 ? 0 : System.Math.Clamp(index, 0, historyCount - 1);
        Playing = PlayState.Seek;
        Changed?.Invoke();
    }

    /// <summary>Forces the pointer (timeline sync restored it by step) without showing.</summary>
    public void SetPointer(int index) { Pointer = System.Math.Max(0, index); Changed?.Invoke(); }

    public void Stop()
    {
        Playing = PlayState.None;
        Changed?.Invoke();
    }

    /// <summary>Result handler: a run that ended while following the live edge rests on the last frame.</summary>
    public void RunEnded(int historyCount)
    {
        Computing = false;
        if (Playing == PlayState.ForwardWaiting)
        {
            Playing = PlayState.None;
            Pointer = System.Math.Max(0, System.Math.Min(Pointer, historyCount - 1));
            Changed?.Invoke();
        }
    }

    /// <summary>Executes one transition; returns whether a frame was requested to show.</summary>
    public bool Step(int historyCount)
    {
        if (Playing == PlayState.None) return false;
        var pointer = historyCount == 0 ? 0 : System.Math.Clamp(Pointer, 0, historyCount - 1);
        var pv = Playing;
        var shown = false;
        switch (pv)
        {
            case PlayState.Forward:
            case PlayState.NextForward:
            case PlayState.SeekForward:
                pointer = pv == PlayState.SeekForward ? historyCount - 1 : pointer + 1;
                if (pointer < historyCount)
                {
                    Pointer = pointer;
                    Show?.Invoke(pointer);
                    shown = true;
                    Playing = pv is PlayState.NextForward or PlayState.SeekForward ? PlayState.None : pv;
                }
                else
                {
                    Pointer = System.Math.Max(0, historyCount - 1);
                    Playing = (pv is PlayState.Forward or PlayState.NextForward) && Computing ? PlayState.ForwardWaiting : PlayState.None;
                }
                break;
            case PlayState.ForwardWaiting:
            {
                var arrived = LastReceivedIndex >= 0 && LastReceivedIndex < historyCount
                    ? LastReceivedIndex
                    : System.Math.Min(pointer + 1, historyCount - 1);
                var moved = System.Math.Max(0, arrived) != Pointer;
                Pointer = System.Math.Max(0, arrived);
                if (moved && historyCount > 0) { Show?.Invoke(Pointer); shown = true; }
                Playing = Computing ? PlayState.ForwardWaiting : PlayState.None;
                break;
            }
            case PlayState.Backward:
            case PlayState.NextBackward:
            case PlayState.SeekBackward:
                pointer = pv == PlayState.SeekBackward ? 0 : pointer - 1;
                if (pointer >= 0 && historyCount > 0)
                {
                    Pointer = pointer;
                    Show?.Invoke(pointer);
                    shown = true;
                    Playing = pv == PlayState.Backward ? PlayState.Backward : PlayState.None;
                }
                else
                {
                    Pointer = 0;
                    Playing = PlayState.None;
                }
                break;
            case PlayState.Seek:
                if (historyCount > 0) { Show?.Invoke(pointer); shown = true; }
                Playing = PlayState.None;
                break;
        }
        Changed?.Invoke();
        return shown;
    }
}
