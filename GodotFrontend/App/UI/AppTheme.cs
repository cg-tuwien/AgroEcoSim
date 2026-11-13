using Godot;

namespace GodotFrontend.App.UI;

/// <summary>A dark, readable theme built in code so the app has no editor-authored resources to keep in sync.</summary>
public static class AppTheme
{
    public static readonly Color Background = new(0.11f, 0.11f, 0.12f);
    public static readonly Color Panel = new(0.16f, 0.16f, 0.17f);
    public static readonly Color Accent = new(0.42f, 0.75f, 0.36f);
    public static readonly Color Text = new(0.92f, 0.92f, 0.92f);
    public static readonly Color Muted = new(0.65f, 0.65f, 0.65f);
    public static readonly Color Warning = new(0.95f, 0.65f, 0.2f);
    public static readonly Color Error = new(0.95f, 0.3f, 0.3f);

    public static Theme Build()
    {
        var theme = new Theme();
        theme.DefaultFontSize = 13;
        var panel = new StyleBoxFlat { BgColor = Panel, ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6 };
        theme.SetStylebox("panel", "PanelContainer", panel);
        var button = new StyleBoxFlat { BgColor = new Color(0.24f, 0.24f, 0.26f), CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3, ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 3, ContentMarginBottom = 3 };
        var hover = (StyleBoxFlat)button.Duplicate(); hover.BgColor = new Color(0.3f, 0.3f, 0.33f);
        var pressed = (StyleBoxFlat)button.Duplicate(); pressed.BgColor = Accent.Darkened(0.3f);
        var disabled = (StyleBoxFlat)button.Duplicate(); disabled.BgColor = new Color(0.18f, 0.18f, 0.19f);
        theme.SetStylebox("normal", "Button", button);
        theme.SetStylebox("hover", "Button", hover);
        theme.SetStylebox("pressed", "Button", pressed);
        theme.SetStylebox("disabled", "Button", disabled);
        theme.SetColor("font_color", "Button", Text);
        theme.SetColor("font_disabled_color", "Button", Muted);
        theme.SetColor("font_color", "Label", Text);
        theme.SetColor("font_color", "CheckBox", Text);
        theme.SetColor("font_color", "OptionButton", Text);
        theme.SetColor("font_color", "LineEdit", Text);
        var edit = new StyleBoxFlat { BgColor = new Color(0.08f, 0.08f, 0.09f), ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 3, ContentMarginBottom = 3, BorderColor = new Color(0.3f, 0.3f, 0.32f), BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1 };
        theme.SetStylebox("normal", "LineEdit", edit);
        theme.SetStylebox("focus", "LineEdit", edit);
        var tabPanel = new StyleBoxFlat { BgColor = Panel, ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 6, ContentMarginBottom = 6 };
        theme.SetStylebox("panel", "TabContainer", tabPanel);
        // Compact tabs: five of them have to fit the 25 % side panel without scroll arrows.
        StyleBoxFlat Tab(Color bg) => new() { BgColor = bg, ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 4, ContentMarginBottom = 4, CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3 };
        theme.SetStylebox("tab_selected", "TabContainer", Tab(Panel));
        theme.SetStylebox("tab_unselected", "TabContainer", Tab(Background));
        theme.SetStylebox("tab_hovered", "TabContainer", Tab(new Color(0.2f, 0.2f, 0.22f)));
        theme.SetStylebox("tab_disabled", "TabContainer", Tab(Background));
        theme.SetColor("font_selected_color", "TabContainer", Accent);
        theme.SetColor("font_unselected_color", "TabContainer", Muted);
        return theme;
    }

    public static Label Muted_(string text) => new() { Text = text, Modulate = Muted };
}
