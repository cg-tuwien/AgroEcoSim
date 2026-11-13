using Godot;

namespace GodotFrontend.App.UI.Panels;

/// <summary>A side-panel tab: a scrolling column of controls refreshed from the app state once per frame when it changed.</summary>
public abstract partial class SidePanelTab : ScrollContainer
{
    protected readonly VBoxContainer Column = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };

    protected SidePanelTab(string title)
    {
        Name = title;
        HorizontalScrollMode = ScrollMode.Disabled;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        Column.AddThemeConstantOverride("separation", 6);
        AddChild(Column);
    }

    public abstract void Refresh();

    protected Label Heading(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeColorOverride("font_color", AppTheme.Accent);
        Column.AddChild(l);
        return l;
    }

    protected Label Note(string text = "")
    {
        var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = AppTheme.Muted };
        Column.AddChild(l);
        return l;
    }

    protected T Add<T>(T control) where T : Control
    {
        Column.AddChild(control);
        return control;
    }
}
