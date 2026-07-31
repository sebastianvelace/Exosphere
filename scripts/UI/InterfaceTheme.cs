namespace Exosphere.Game;

using Godot;

/// Shared visual tokens for Exosphere's monochrome interface.
/// Godot does not blur the 3D framebuffer behind Controls, so the "glass" material
/// is an intentionally restrained approximation: translucent charcoal, a bright
/// inner edge and a soft black shadow.
public static class InterfaceTheme
{
    public static readonly Color Void = new(0.015f, 0.018f, 0.024f, 1f);
    public static readonly Color Glass = new(0.025f, 0.030f, 0.040f, 0.78f);
    public static readonly Color GlassStrong = new(0.025f, 0.030f, 0.040f, 0.92f);
    public static readonly Color GlassSoft = new(0.035f, 0.040f, 0.052f, 0.56f);
    public static readonly Color Edge = new(0.88f, 0.92f, 1.00f, 0.20f);
    public static readonly Color EdgeStrong = new(0.96f, 0.98f, 1.00f, 0.48f);
    public static readonly Color Text = new(0.95f, 0.97f, 1.00f, 1f);
    public static readonly Color TextMuted = new(0.63f, 0.67f, 0.74f, 1f);
    public static readonly Color TextFaint = new(0.42f, 0.46f, 0.53f, 1f);
    public static readonly Color Track = new(0.13f, 0.15f, 0.19f, 0.94f);
    public static readonly Color Alert = new(1.00f, 0.40f, 0.34f, 1f);
    public static readonly Color Warning = new(1.00f, 0.73f, 0.28f, 1f);
    public static readonly Color Orbital = new(0.24f, 0.76f, 0.88f, 1f);
    /// Pale green reserved for a passed check / nominal terminal state.
    public static readonly Color Success = new(0.55f, 0.95f, 0.65f, 1f);

    public static Font DisplayFont =>
        GD.Load<Font>("res://assets/fonts/barlow/BarlowCondensed-SemiBold.ttf");
    public static Font BodyFont =>
        GD.Load<Font>("res://assets/fonts/ibm-plex/IBMPlexSans-Regular.ttf");
    public static Font BodyMediumFont =>
        GD.Load<Font>("res://assets/fonts/ibm-plex/IBMPlexSans-Medium.ttf");
    public static Font MonoFont =>
        GD.Load<Font>("res://assets/fonts/ibm-plex/IBMPlexMono-Regular.ttf");

    public static void ApplyDisplay(Label label, int size)
    {
        label.AddThemeFontOverride("font", DisplayFont);
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

    public static StyleBoxFlat GlassPanel(
        float opacity = 0.78f,
        int radius = 12,
        int marginX = 16,
        int marginY = 14)
    {
        var background = Glass;
        background.A = opacity;

        var style = new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = Edge,
            ContentMarginLeft = marginX,
            ContentMarginRight = marginX,
            ContentMarginTop = marginY,
            ContentMarginBottom = marginY,
            ShadowColor = new Color(0f, 0f, 0f, 0.32f),
            ShadowSize = 10,
            ShadowOffset = new Vector2(0, 5),
            AntiAliasing = true,
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(radius);
        return style;
    }

    public static StyleBoxFlat Button(
        bool primary, bool hover = false, bool pressed = false, int paddingX = 22, int paddingY = 13, int radius = 10)
    {
        Color background;
        Color border;
        if (primary)
        {
            background = pressed
                ? new Color(0.72f, 0.75f, 0.80f, 1f)
                : hover
                    ? new Color(1f, 1f, 1f, 1f)
                    : new Color(0.92f, 0.94f, 0.98f, 1f);
            border = background;
        }
        else
        {
            background = hover
                ? new Color(0.12f, 0.13f, 0.16f, 0.94f)
                : new Color(0.04f, 0.045f, 0.055f, 0.78f);
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
            ShadowColor = new Color(0f, 0f, 0f, primary ? 0.36f : 0.18f),
            ShadowSize = primary ? 8 : 4,
            ShadowOffset = new Vector2(0, primary ? 4 : 2),
            AntiAliasing = true,
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(radius);
        return style;
    }

    /// <param name="minSize">Defaults to the large main-menu CTA size (238x50). Pass a
    /// smaller size for dense toolbars (e.g. the VAB's action grid) — the touch-friendly
    /// menu size does not fit a dozen-plus actions in a sidebar.</param>
    public static void StyleButton(
        Button button, bool primary = false, Vector2? minSize = null,
        int fontSize = 14, int paddingX = 22, int paddingY = 13)
    {
        button.CustomMinimumSize = minSize ?? new Vector2(238, 50);
        button.AddThemeFontOverride("font", BodyMediumFont);
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
        disabled.BgColor = new Color(0.04f, 0.045f, 0.055f, 0.4f);
        disabled.BorderColor = new Color(Edge, 0.4f);
        button.AddThemeStyleboxOverride("disabled", disabled);
    }

    /// <summary>Dark inset background for text/list input controls (LineEdit, ItemList,
    /// OptionButton) so they read as part of the glass surface instead of the engine's
    /// default grey control theme.</summary>
    public static StyleBoxFlat FieldPanel(int radius = 8, int paddingX = 10, int paddingY = 8)
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.035f, 0.045f, 0.7f),
            BorderColor = Edge,
            ContentMarginLeft = paddingX,
            ContentMarginRight = paddingX,
            ContentMarginTop = paddingY,
            ContentMarginBottom = paddingY,
            AntiAliasing = true,
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(radius);
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
                le.AddThemeFontOverride("font", BodyFont);
                le.AddThemeFontSizeOverride("font_size", 13);
                le.AddThemeColorOverride("font_color", Text);
                le.AddThemeColorOverride("font_placeholder_color", TextFaint);
                break;
            case ItemList il:
                il.AddThemeStyleboxOverride("panel", panel);
                il.AddThemeFontOverride("font", BodyFont);
                il.AddThemeFontSizeOverride("font_size", 13);
                il.AddThemeColorOverride("font_color", TextMuted);
                il.AddThemeColorOverride("font_selected_color", Text);
                var sel = new StyleBoxFlat { BgColor = new Color(Orbital, 0.16f) };
                sel.SetCornerRadiusAll(6);
                il.AddThemeStyleboxOverride("selected", sel);
                il.AddThemeStyleboxOverride("selected_focus", sel);
                break;
            case OptionButton ob:
                var obHover = FieldPanel();
                obHover.BorderColor = EdgeStrong;
                ob.AddThemeStyleboxOverride("normal", panel);
                ob.AddThemeStyleboxOverride("hover", obHover);
                ob.AddThemeStyleboxOverride("focus", panel);
                ob.AddThemeFontOverride("font", BodyFont);
                ob.AddThemeFontSizeOverride("font_size", 13);
                ob.AddThemeColorOverride("font_color", Text);
                break;
        }
    }

    /// <summary>Small muted caps-style heading for grouping related controls within a
    /// panel (e.g. "QUICK BUILD" over the template buttons) — lighter than a full panel
    /// title, so a dense sidebar doesn't read as one undifferentiated button grid.</summary>
    public static Label SectionLabel(string text)
    {
        var label = new Label { Text = text, Modulate = TextFaint };
        label.AddThemeFontOverride("font", BodyMediumFont);
        label.AddThemeFontSizeOverride("font_size", 11);
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
            source.SetCornerRadiusAll(0);
            button.AddThemeStyleboxOverride(state, source);
        }
    }
}
