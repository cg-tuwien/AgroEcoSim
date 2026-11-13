using Godot;
using GodotFrontend.Core.Playback;
using GodotFrontend.Core.Run;
using GodotFrontend.Core.Time;

namespace GodotFrontend.App.UI.Widgets;

/// <summary>
/// The transport bar of Start.tsx: seek-start, previous, play-backward, pause, play, next, seek-end, the timeline
/// slider and the clock. Disabled rules follow the web; next and seek-end are also off while the tab follows the
/// live edge, which is what keeps them from flickering while a run streams.
/// </summary>
public partial class TransportBar : VBoxContainer
{
    private readonly RunController controller;
    private readonly Button seekStart = new() { Text = "|<" };
    private readonly Button prev = new() { Text = "<" };
    private readonly Button playBack = new() { Text = "<|" };
    private readonly Button pause = new() { Text = "||" };
    private readonly Button play = new() { Text = "|>" };
    private readonly Button next = new() { Text = ">" };
    private readonly Button seekEnd = new() { Text = ">|" };
    private readonly HSlider slider = new() { MinValue = 0, MaxValue = 0, Step = 1, SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly Label clock = new() { Text = "" };
    private readonly Label elapsed = new() { Text = "" };
    private bool updating;

    public TransportBar(RunController controller)
    {
        this.controller = controller;
        var row = new HBoxContainer();
        foreach (var b in new[] { seekStart, prev, playBack, pause, play, next, seekEnd }) row.AddChild(b);
        AddChild(row);
        AddChild(slider);
        var clockRow = new HBoxContainer();
        clockRow.AddChild(clock);
        elapsed.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        elapsed.HorizontalAlignment = HorizontalAlignment.Right;
        clockRow.AddChild(elapsed);
        AddChild(clockRow);

        seekStart.Pressed += () => controller.Play(PlayState.SeekBackward);
        prev.Pressed += () => controller.Play(PlayState.NextBackward);
        playBack.Pressed += () => controller.Play(PlayState.Backward);
        pause.Pressed += () => controller.Play(PlayState.None);
        play.Pressed += () => controller.Play(PlayState.Forward);
        next.Pressed += () => controller.Play(PlayState.NextForward);
        seekEnd.Pressed += () => controller.Play(PlayState.SeekForward);
        slider.ValueChanged += v => { if (!updating) controller.Seek((int)v); };
    }

    public void Refresh()
    {
        var h = controller.History;
        var p = controller.Playback;
        var count = h.Count;
        var pointer = p.Pointer;
        var playing = p.Playing;
        var live = p.AtLiveEdge;
        updating = true;
        slider.MaxValue = System.Math.Max(0, count - 1);
        slider.SetValueNoSignal(pointer);
        slider.Editable = count > 1;
        updating = false;
        seekStart.Disabled = count == 0 || pointer <= 0;
        prev.Disabled = count == 0 || pointer <= 0;
        playBack.Disabled = count == 0 || playing == PlayState.Backward || pointer <= 0;
        pause.Disabled = playing == PlayState.None;
        play.Disabled = count == 0 || playing is PlayState.Forward or PlayState.ForwardWaiting;
        next.Disabled = count == 0 || live || pointer >= count - 1;
        seekEnd.Disabled = count == 0 || live || pointer >= count - 1;

        if (count == 0)
        {
            clock.Text = "";
            elapsed.Text = "";
            return;
        }
        var step = h.StepAt(pointer);
        var hpt = controller.Session.SimHoursPerTick;
        var date = SimClock.ParseDate(controller.Session.InitialDate);
        var zone = controller.Session.Site.TimeZone ?? SimClock.DefaultTimeZone;
        var when = SimClock.TimeOfTimestep(date, step, hpt, zone);
        clock.Text = SimClock.FormatClock(when, zone);
        elapsed.Text = "+" + SimClock.FormatElapsed((double)step * SimClock.Snap(hpt));
        clock.TooltipText = $"step {step}, {step * SimClock.Snap(hpt)} h, {when:O}";
    }
}
