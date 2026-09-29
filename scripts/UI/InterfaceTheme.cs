namespace Exosphere.Game;

using Godot;

/// Shared visual tokens for Exosphere's flight-test interface.
/// Surfaces are flat near-black with hairline rules and square corners — an instrument
/// panel, not a glass dashboard. Type is split by role: condensed caps for labels,
/// monospace for every number, proportional sans only where a sentence is needed.
/// Colour is a status channel and never decoration: <see cref="Text"/> is nominal,
/// <see cref="Warning"/> is caution, <see cref="Alert"/> is a warning, and
/// <see cref="Success"/> is reserved for a check that has passed.
public static class InterfaceTheme
{
    public static readonly Color Void = new(0.012f, 0.013f, 0.015f, 1f);
    /// Standard instrument surface: opaque enough to read numbers over a bright horizon.
    public static readonly Color Panel = new(0.026f, 0.028f, 0.031f, 0.90f);
    /// Denser surface for a panel that must stay readable over the plume or the ground.
    public static readonly Color PanelDeep = new(0.019f, 0.021f, 0.023f, 0.96f);
    public static readonly Color Edge = new(1f, 1f, 1f, 0.11f);
    public static readonly Color EdgeStrong = new(1f, 1f, 1f, 0.24f);
    public static readonly Color Text = new(0.94f, 0.95f, 0.96f, 1f);
    public static readonly Color TextMuted = new(0.62f, 0.64f, 0.66f, 1f);
    public static readonly Color TextFaint = new(0.40f, 0.41f, 0.43f, 1f);
    public static readonly Color Track = new(0.098f, 0.103f, 0.110f, 0.92f);
    public static readonly Color Alert = new(0.95f, 0.29f, 0.24f, 1f);
    public static readonly Color Warning = new(1.00f, 0.71f, 0.20f, 1f);
    /// Pale green reserved for a passed check / nominal terminal state.
    public static readonly Color Success = new(0.56f, 0.88f, 0.60f, 1f);

    public static Font DisplayFont =>
        GD.Load<Font>("res://assets/fonts/barlow/BarlowCondensed-SemiBold.ttf");
    public static Font BodyFont =>
        GD.Load<Font>("res://assets/fonts/ibm-plex/IBMPlexSans-Regular.ttf");
    public static Font BodyMediumFont =>
        GD.Load<Font>("res://assets/fonts/ibm-plex/IBMPlexSans-Medium.ttf");
    public static Font MonoFont =>
        GD.Load<Font>("res://assets/fonts/ibm-plex/IBMPlexMono-Regular.ttf");

    private static FontVariation? _labelFont;

    /// <summary>Condensed caps with a little tracking — the callout label of a flight
    /// display. Built once: a FontVariation is a resource, not a per-label value.</summary>
    public static Font LabelFont => _labelFont ??= new FontVariation
    {
        BaseFont = DisplayFont,
        SpacingGlyph = 1,
    };

    public static void ApplyDisplay(Label label, int size)
    {
        label.AddThemeFontOverride("font", DisplayFont);
        label.AddThemeFontSizeOverride("font_size", size);
    }

    /// <summary>Caption/label type: condensed, tracked, meant to be read in caps.</summary>
    public static void ApplyLabel(Label label, int size)
    {
        label.AddThemeFontOverride("font", LabelFont);
        label.AddThemeFontSizeOverride("font_size", size);
    }

    public static void ApplyBody(Label label, int size, bool medium = false)
    {
        label.AddThemeFontOverride("font", medium ? BodyMediumFont : BodyFont);
        label.AddThemeFontSizeOverride("font_size", size);
    }

    public static void ApplyMono(Label label, int size)
    {
        label.AddThemeFontOverride("font", MonoFont);
        label.AddThemeFontSizeOverride("font_size", size);
    }

    /// <summary>Flat instrument surface: square corners, one hairline border, no shadow.</summary>
    public static StyleBoxFlat PanelStyle(
        float opacity = 0.90f,
        int marginX = 14,
        int marginY = 11,
        bool deep = false)
    {
        var background = deep ? PanelDeep : Panel;
        background.A = opacity;

        var style = new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = Edge,
            ContentMarginLeft = marginX,
            ContentMarginRight = marginX,
            ContentMarginTop = marginY,
            ContentMarginBottom = marginY,
            AntiAliasing = false,
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(0);
        return style;
    }

    public static StyleBoxFlat Button(
        bool primary, bool hover = false, bool pressed = false, int paddingX = 20, int paddingY = 12)
    {
        Color background;
        Color border;
        if (primary)
        {
            background = pressed
                ? new Color(0.70f, 0.71f, 0.72f, 1f)
                : hover
                    ? new Color(1f, 1f, 1f, 1f)
                    : new Color(0.90f, 0.91f, 0.92f, 1f);
            border = background;
        }
        else
        {
            background = hover
                ? new Color(0.085f, 0.090f, 0.095f, 0.94f)
                : new Color(0.026f, 0.028f, 0.031f, 0.86f);
            border = hover ? EdgeStrong : Edge;
        }

        var style = new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = border,
            ContentMarginLeft = paddingX,
            ContentMarginRight = paddingX,
            ContentMarginTop = paddingY,
            ContentMarginBottom = paddingY,
            AntiAliasing = false,
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(0);
        return style;
    }

    public static void AddStateRail(StyleBoxFlat style, Color color, int width = 2)
    {
        style.BorderColor = color;
        style.BorderWidthLeft = width;
    }

    /// <param name="minSize">Defaults to the large main-menu CTA size (238x50). Pass a
    /// smaller size for dense toolbars (e.g. the VAB's action grid) — the touch-friendly
    /// menu size does not fit a dozen-plus actions in a sidebar.</param>
    public static void StyleButton(
        Button button, bool primary = false, Vector2? minSize = null,
        int fontSize = 13, int paddingX = 20, int paddingY = 12)
    {
        button.CustomMinimumSize = minSize ?? new Vector2(238, 50);
        button.AddThemeFontOverride("font", LabelFont);
        button.AddThemeFontSizeOverride("font_size", fontSize);
        button.AddThemeColorOverride("font_color", primary ? Void : Text);
        button.AddThemeColorOverride("font_hover_color", primary ? Void : Text);
        button.AddThemeColorOverride("font_pressed_color", primary ? Void : Text);
        button.AddThemeColorOverride("font_focus_color", primary ? Void : Text);
        button.AddThemeColorOverride("font_disabled_color", TextFaint);
        var normal = Button(primary, paddingX: paddingX, paddingY: paddingY);
        var hoverStyle = Button(primary, hover: true, paddingX: paddingX, paddingY: paddingY);
        var pressedStyle = Button(primary, hover: true, pressed: true, paddingX: paddingX, paddingY: paddingY);
        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hoverStyle);
        button.AddThemeStyleboxOverride("pressed", pressedStyle);
        button.AddThemeStyleboxOverride("focus", hoverStyle);
        var disabled = Button(primary, paddingX: paddingX, paddingY: paddingY);
        disabled.BgColor = new Color(0.026f, 0.028f, 0.031f, 0.45f);
        disabled.BorderColor = new Color(Edge, 0.5f);
        button.AddThemeStyleboxOverride("disabled", disabled);
    }

    /// <summary>Dark inset background for text/list input controls (LineEdit, ItemList,
    /// OptionButton) so they read as part of the instrument surface instead of the engine's
    /// default grey control theme.</summary>
    public static StyleBoxFlat FieldPanel(int paddingX = 10, int paddingY = 8)
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.018f, 0.020f, 0.022f, 0.86f),
            BorderColor = Edge,
            ContentMarginLeft = paddingX,
            ContentMarginRight = paddingX,
            ContentMarginTop = paddingY,
            ContentMarginBottom = paddingY,
            AntiAliasing = false,
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(0);
        return style;
    }

    public static void StyleField(Control control)
    {
        var panel = FieldPanel();
        switch (control)
        {
            case LineEdit le:
                var leFocus = FieldPanel();
                leFocus.BorderColor = EdgeStrong;
                le.AddThemeStyleboxOverride("normal", panel);
                le.AddThemeStyleboxOverride("focus", leFocus);
                le.AddThemeFontOverride("font", MonoFont);
                le.AddThemeFontSizeOverride("font_size", 12);
                le.AddThemeColorOverride("font_color", Text);
                le.AddThemeColorOverride("font_placeholder_color", TextFaint);
                break;
            case ItemList il:
                il.AddThemeStyleboxOverride("panel", panel);
                il.AddThemeFontOverride("font", MonoFont);
                il.AddThemeFontSizeOverride("font_size", 12);
                il.AddThemeColorOverride("font_color", TextMuted);
                il.AddThemeColorOverride("font_selected_color", Text);
                var sel = new StyleBoxFlat { BgColor = new Color(1f, 1f, 1f, 0.10f) };
                sel.SetCornerRadiusAll(0);
                il.AddThemeStyleboxOverride("selected", sel);
                il.AddThemeStyleboxOverride("selected_focus", sel);
                break;
            case OptionButton ob:
                var obHover = FieldPanel();
                obHover.BorderColor = EdgeStrong;
                ob.AddThemeStyleboxOverride("normal", panel);
                ob.AddThemeStyleboxOverride("hover", obHover);
                ob.AddThemeStyleboxOverride("focus", panel);
                ob.AddThemeFontOverride("font", MonoFont);
                ob.AddThemeFontSizeOverride("font_size", 12);
                ob.AddThemeColorOverride("font_color", Text);
                break;
        }
    }

    /// <summary>Small muted caps heading for grouping related controls within a panel
    /// (e.g. "QUICK BUILD" over the template buttons) — lighter than a full panel title,
    /// so a dense sidebar doesn't read as one undifferentiated button grid.</summary>
    public static Label SectionLabel(string text)
    {
        var label = new Label { Text = text, Modulate = TextFaint };
        label.AddThemeFontOverride("font", LabelFont);
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeConstantOverride("outline_size", 0);
        return label;
    }

    public static void StyleDossierButton(Button button, bool primary = false)
    {
        StyleButton(button, primary);
        foreach (string state in new[] { "normal", "hover", "pressed", "focus" })
        {
            var source = Button(
                primary,
                hover: state is "hover" or "focus" or "pressed",
                pressed: state == "pressed");
            if (primary || state is "hover" or "focus" or "pressed")
            {
                // A selection rail, not an accent: the same white the numbers use.
                float alpha = state == "normal" ? 0.45f : 0.75f;
                AddStateRail(source, new Color(Text, alpha), primary ? 3 : 2);
            }
            button.AddThemeStyleboxOverride(state, source);
        }
    }
}
