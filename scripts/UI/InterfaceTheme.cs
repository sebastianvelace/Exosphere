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

    public static StyleBoxFlat Button(bool primary, bool hover = false, bool pressed = false)
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
            ContentMarginLeft = 22,
            ContentMarginRight = 22,
            ContentMarginTop = 13,
            ContentMarginBottom = 13,
            ShadowColor = new Color(0f, 0f, 0f, primary ? 0.36f : 0.18f),
            ShadowSize = primary ? 8 : 4,
            ShadowOffset = new Vector2(0, primary ? 4 : 2),
            AntiAliasing = true,
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(10);
        return style;
    }

    public static void StyleButton(Button button, bool primary = false)
    {
        button.CustomMinimumSize = new Vector2(238, 50);
        button.AddThemeFontSizeOverride("font_size", 14);
        button.AddThemeColorOverride("font_color", primary ? Void : Text);
        button.AddThemeColorOverride("font_hover_color", primary ? Void : Text);
        button.AddThemeColorOverride("font_pressed_color", primary ? Void : Text);
        button.AddThemeColorOverride("font_focus_color", primary ? Void : Text);
        button.AddThemeStyleboxOverride("normal", Button(primary));
        button.AddThemeStyleboxOverride("hover", Button(primary, hover: true));
        button.AddThemeStyleboxOverride("pressed", Button(primary, hover: true, pressed: true));
        button.AddThemeStyleboxOverride("focus", Button(primary, hover: true));
    }
}
