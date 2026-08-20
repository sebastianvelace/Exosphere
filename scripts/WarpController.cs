namespace Exosphere.Game;

using Godot;

/// <summary>
/// HUD overlay that displays the current time-warp level and the allowed maximum.
/// Press <c>[.]</c> to increase warp and <c>[,]</c> to decrease warp.
/// Added to the scene automatically by <see cref="SimulationBridge._Ready"/>.
/// </summary>
public partial class WarpController : Control
{
    private Font _font = null!;
    private StyleBoxFlat _panelStyle = null!;

    public override void _Ready()
    {
        _font = InterfaceTheme.MonoFont;
        _panelStyle = InterfaceTheme.GlassPanel(0.68f, 12, 0, 0);
        SetAnchorsPreset(LayoutPreset.TopLeft);
        CustomMinimumSize = new Vector2(178, 68);
        OffsetLeft   = 320;
        OffsetTop    = 18;
        OffsetRight  = 498;
        OffsetBottom = 86;
        MouseFilter  = MouseFilterEnum.Ignore;
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventKey { Pressed: true, Echo: false } key)
        {
            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;

            bool handled = false;
            if (key.Keycode == Key.Period)
            {
                bridge.SetWarpIndex(bridge.WarpIndex + 1);
                handled = true;
            }
            else if (key.Keycode == Key.Comma)
            {
                bridge.SetWarpIndex(bridge.WarpIndex - 1);
                handled = true;
            }

            if (handled)
                GetViewport().SetInputAsHandled();
        }
    }

    public override void _Draw()
    {
        var bridge = SimulationBridge.Instance;
        if (bridge == null || _font == null) return;

        double currentRate = SimulationBridge.WarpLevels[bridge.WarpIndex];
        double maxRate     = SimulationBridge.WarpLevels[bridge.MaxAllowedWarpIndex];

        string line1 = $"TIME  x{currentRate:G}";
        string solarPhase = SunController.SolarPhase switch
        {
            "CIVIL_TWILIGHT" => "CIVIL",
            "NAUTICAL_TWILIGHT" => "NAUTICAL",
            "ASTRONOMICAL_TWILIGHT" => "ASTRO",
            _ => SunController.SolarPhase,
        };
        string solarLine = double.IsFinite(SunController.SolarElevationDegrees)
            ? $"SUN  {SunController.SolarElevationDegrees:+0.0;-0.0;0.0}° {solarPhase}"
            : "SUN  -- UNKNOWN";
        string line3 = $"MAXIMUM  x{maxRate:G}";

        var universe = bridge.Universe;
        bool showClamp = bridge.WarpClampReason != null
            && universe != null
            && universe.CurrentTime < bridge.WarpClampReasonUntil;

        float panelH = showClamp ? 87f : 68f;
        CustomMinimumSize = new Vector2(178, panelH);
        OffsetBottom = OffsetTop + panelH;

        DrawStyleBox(_panelStyle, new Rect2(Vector2.Zero, Size));

        var col1 = bridge.WarpIndex > 0 ? InterfaceTheme.Warning : InterfaceTheme.Text;
        DrawString(_font, new Vector2(14, 20), line1, HorizontalAlignment.Left, -1, 13, col1);
        DrawString(_font, new Vector2(14, 39), solarLine, HorizontalAlignment.Left, -1, 10,
            InterfaceTheme.TextMuted);
        DrawString(_font, new Vector2(14, 58), line3, HorizontalAlignment.Left, -1, 10,
            InterfaceTheme.TextMuted);

        if (showClamp)
        {
            string line4 = $"CLAMP — {bridge.WarpClampReason}";
            DrawString(_font, new Vector2(14, 77), line4, HorizontalAlignment.Left, -1, 10, InterfaceTheme.Warning);
        }
    }

    public override void _Process(double delta)
    {
        bool viewAllows = CameraController.Instance?.IsCockpitView != true
            && MapViewController.Instance?.Visible != true;
        Visible = viewAllows && DensityAllows();
        if (Visible) QueueRedraw();
    }

    /// <summary>
    /// C3 density gate. FULL always shows the box; MINIMAL only when the clock is not
    /// running at real time (or a clamp is being explained), so the one datum this widget
    /// owns still appears exactly when it matters; CLEAN never.
    /// </summary>
    private static bool DensityAllows()
    {
        var bridge = SimulationBridge.Instance;
        return UserInterfaceSettings.HudDensity switch
        {
            HudDensity.Full => true,
            HudDensity.Clean => false,
            _ => bridge != null
                && (bridge.WarpIndex > 0
                    || bridge.WarpClampReason != null
                        && bridge.Universe is { } universe
                        && universe.CurrentTime < bridge.WarpClampReasonUntil),
        };
    }
}
