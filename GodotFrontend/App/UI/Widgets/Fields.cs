using Godot;

namespace GodotFrontend.App.UI.Widgets;

/// <summary>How a stepped number field moves on its +/- buttons.</summary>
public interface IStepPolicy
{
    double Up(double value);
    double Down(double value);
}

/// <summary>Steps through a fixed table (HoursPerTick: 1,2,3,4,6,8,12,24,48,...,168).</summary>
public sealed class TableStep : IStepPolicy
{
    private readonly double[] table;
    public TableStep(params double[] table) => this.table = table;
    public double Up(double v) { foreach (var t in table) if (t > v + 1e-9) return t; return table[^1]; }
    public double Down(double v) { for (var i = table.Length - 1; i >= 0; --i) if (table[i] < v - 1e-9) return table[i]; return table[0]; }
}

/// <summary>Adds or subtracts a fixed amount, clamped.</summary>
public sealed class FixedStep : IStepPolicy
{
    private readonly double step, min, max;
    public FixedStep(double step, double min = double.NegativeInfinity, double max = double.PositiveInfinity) { this.step = step; this.min = min; this.max = max; }
    public double Up(double v) => System.Math.Clamp(v + step, min, max);
    public double Down(double v) => System.Math.Clamp(v - step, min, max);
}

/// <summary>A labelled number box with +/- buttons; typed values are applied (the web ignored them for some fields).</summary>
public partial class SteppedNumberField : HBoxContainer
{
    private readonly LineEdit edit = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(70, 0) };
    private readonly Button minus = new() { Text = "-" };
    private readonly Button plus = new() { Text = "+" };
    private readonly Label note = new() { Modulate = AppTheme.Muted };
    private readonly IStepPolicy policy;
    private readonly bool integer;
    private double value;

    public event Action<double>? Changed;

    public SteppedNumberField(string label, double initial, IStepPolicy policy, bool integer, string unit = "")
    {
        this.policy = policy;
        this.integer = integer;
        value = initial;
        AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(120, 0) });
        AddChild(minus);
        AddChild(edit);
        AddChild(plus);
        if (unit.Length > 0) AddChild(new Label { Text = unit, Modulate = AppTheme.Muted });
        AddChild(note);
        edit.Text = Format(value);
        minus.Pressed += () => Set(policy.Down(value));
        plus.Pressed += () => Set(policy.Up(value));
        edit.TextSubmitted += t => { if (double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) Set(v); else edit.Text = Format(value); };
        edit.FocusExited += () => edit.EmitSignal(LineEdit.SignalName.TextSubmitted, edit.Text);
    }

    public double Value => value;
    public string Note { set => note.Text = value; }

    public bool Disabled
    {
        set
        {
            edit.Editable = !value;
            minus.Disabled = value;
            plus.Disabled = value;
        }
    }

    public void SetSilently(double v)
    {
        value = integer ? System.Math.Round(v) : v;
        edit.Text = Format(value);
    }

    private void Set(double v)
    {
        SetSilently(v);
        Changed?.Invoke(value);
    }

    private string Format(double v) => integer ? ((long)v).ToString() : v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}

public partial class LabeledCheck : CheckBox
{
    public LabeledCheck(string label, bool initial, Action<bool> onChanged)
    {
        Text = label;
        SetPressedNoSignal(initial);
        Toggled += on => onChanged(on);
    }
}

public partial class ChoiceField : HBoxContainer
{
    private readonly OptionButton option = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };

    public ChoiceField(string label, IReadOnlyList<string> choices, int initial, Action<int> onChanged)
    {
        AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(120, 0) });
        foreach (var c in choices) option.AddItem(c);
        if (choices.Count > 0) option.Select(System.Math.Clamp(initial, 0, choices.Count - 1));
        option.ItemSelected += i => onChanged((int)i);
        AddChild(option);
    }

    public void SetItems(IReadOnlyList<string> choices, int selected)
    {
        option.Clear();
        foreach (var c in choices) option.AddItem(c);
        if (choices.Count > 0) option.Select(System.Math.Clamp(selected, 0, choices.Count - 1));
    }

    public void SelectSilently(int index) => option.Select(index);

    /// <summary>Lists a choice that cannot be picked yet (shown greyed out).</summary>
    public void SetItemDisabled(int index, bool disabled) => option.SetItemDisabled(index, disabled);
}

public partial class TextField : HBoxContainer
{
    private readonly LineEdit edit = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };

    public TextField(string label, string initial, Action<string> onChanged)
    {
        AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(120, 0) });
        edit.Text = initial;
        edit.TextSubmitted += t => onChanged(t);
        edit.FocusExited += () => onChanged(edit.Text);
        AddChild(edit);
    }

    public string Text { get => edit.Text; set => edit.Text = value; }
    public bool Editable { set => edit.Editable = value; }
}

/// <summary>Arms on the first press and acts on the second within a few seconds (the web's two-click delete).</summary>
public partial class TwoClickButton : Button
{
    private readonly string idle, armed;
    private readonly Action action;
    private double armedFor = -1;

    public TwoClickButton(string idleText, string armedText, Action action)
    {
        idle = idleText;
        armed = armedText;
        this.action = action;
        Text = idle;
        Pressed += () =>
        {
            if (armedFor >= 0) { armedFor = -1; Text = idle; action(); }
            else { armedFor = 0; Text = armed; }
        };
    }

    public override void _Process(double delta)
    {
        if (armedFor < 0) return;
        armedFor += delta;
        if (armedFor > 4) { armedFor = -1; Text = idle; }
    }
}
