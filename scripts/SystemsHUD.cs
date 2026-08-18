namespace Exosphere.Game;

using Godot;

public partial class SystemsHUD : Control
{
    private const double RefreshPeriodSeconds = 0.10;
    private static readonly Color NominalBar  = new(0.88f, 0.90f, 0.94f, 1f);
    private static readonly Color YellowBar   = InterfaceTheme.Warning;
    private static readonly Color RedBar      = InterfaceTheme.Alert;
    private static readonly Color LabelDim    = InterfaceTheme.TextMuted;
    private static readonly Color Accent      = InterfaceTheme.Text;

    private Font _font = null!;
    private StyleBoxFlat _panelStyle = null!;
    private double _refreshAccumulator = double.MaxValue;
    private bool _wasVisible;

    public override void _Ready()
    {
        _font = InterfaceTheme.MonoFont;
        _panelStyle = InterfaceTheme.GlassPanel(0.76f, 12, 0, 0);
        // Secondary health information sits below the orbit block on the right.
        SetAnchorsPreset(LayoutPreset.TopRight);
        GrowHorizontal = GrowDirection.Begin;
        CustomMinimumSize = new Vector2(278, 200);
        OffsetLeft = -296; OffsetTop = 340;
        OffsetRight = -18; OffsetBottom = 540;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        // C3: consumables and comms are reference data, not fly-the-vehicle data — FULL only.
        bool shouldBeVisible = CameraController.Instance?.IsCockpitView != true
            && MapViewController.Instance?.Visible != true
            && UserInterfaceSettings.HudDensity == HudDensity.Full;
        if (Visible != shouldBeVisible)
        {
            Visible = shouldBeVisible;
            _refreshAccumulator = double.MaxValue;
        }

        if (!shouldBeVisible)
        {
            _wasVisible = false;
            return;
        }

        if (!_wasVisible)
        {
            _wasVisible = true;
            _refreshAccumulator = 0.0;
            QueueRedraw();
            return;
        }

        _refreshAccumulator += System.Math.Max(0.0, delta);
        if (_refreshAccumulator < RefreshPeriodSeconds) return;
        _refreshAccumulator %= RefreshPeriodSeconds;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var sys = SystemsController.Instance;
        if (sys == null) return;

        var size = Size;
        DrawStyleBox(_panelStyle, new Rect2(Vector2.Zero, size));

        DrawString(_font, new Vector2(14, 20), "SYSTEMS", HorizontalAlignment.Left, -1, 11, Accent);

        float y = 34f;
        y = DrawBar(14, y, "O2",   (float)sys.LifeSupport.OxygenFraction,    sys.LifeSupport.OxygenAlert);
        y = DrawBar(14, y, "CO2",  1.0f - (float)sys.LifeSupport.CO2Fraction, sys.LifeSupport.CO2Alert);
        y = DrawBar(14, y, "H2O",  (float)sys.LifeSupport.WaterFraction,      false);
        y = DrawBar(14, y, "FOOD", (float)sys.LifeSupport.FoodFraction,       false);
        y = DrawBar(14, y, "PWR",  (float)sys.Power.BatteryFraction,           sys.Power.LowPowerAlert);
        y = DrawBar(14, y, "TEMP", (float)sys.Thermal.ThermalFraction,         sys.Thermal.HotAlert || sys.Thermal.ColdAlert);
        y = DrawBar(14, y, "COMM", (float)sys.Comms.SignalStrength,            sys.Comms.LossOfSignalAlert);

        var vessel = SimulationBridge.Instance?.ActiveVessel;
        string mode;
        if (vessel?.StructuralControlLost == true)
            mode = "CONTROL LOST (STRUCT)";
        else if (vessel != null
                 && Exosphere.Simulation.Flight.ControlAuthority.IsDegraded(vessel.ControlAuthorityFactor))
            mode = $"CONTROL DEGRADED ({vessel.ControlAuthorityFactor:P0})";
        else if (sys.ControlLimited)
            mode = "CONTROL LIMITED";
        else
            mode = "CONTROL NOMINAL";
        bool alert = sys.ControlLimited || (vessel?.StructuralControlLost ?? false)
            || (vessel != null && Exosphere.Simulation.Flight.ControlAuthority.IsDegraded(
                vessel.ControlAuthorityFactor));
        DrawString(_font, new Vector2(10, y + 12), mode,
            HorizontalAlignment.Left, -1, 10, alert ? RedBar : LabelDim);

        // Comms / ground-link cue (blackout ≠ control loss; delay is gameplay)
        float delay = (float)sys.Comms.SignalDelaySeconds;
        string linkCue;
        Color linkColor = LabelDim;
        if (sys.Comms.PlasmaBlackout)
        {
            linkCue = $"BLACKOUT {sys.Comms.PlasmaBlackoutSeconds:F0}s";
            linkColor = RedBar;
        }
        else if (!sys.Comms.HasSignal)
        {
            linkCue = "LOS — GROUND UPLINK DEAD";
            linkColor = RedBar;
        }
        else if (sys.GroundDelayActive)
        {
            string delayStr = delay < 1.0f ? $"{delay * 1000:F0} ms" : $"{delay:F1} s";
            linkCue = $"GROUND DELAY Dt {delayStr}";
            linkColor = YellowBar;
        }
        else if (delay > 0.01f)
        {
            string delayStr = delay < 1.0f ? $"{delay * 1000:F0} ms" : $"{delay:F1} s";
            linkCue = $"Dt {delayStr}";
        }
        else
        {
            linkCue = "";
        }

        if (linkCue.Length > 0)
        {
            DrawString(_font, new Vector2(10, size.Y - 8), linkCue,
                HorizontalAlignment.Left, -1, 10, linkColor);
        }
    }

    private float DrawBar(float x, float y, string label, float fraction, bool alert)
    {
        fraction = System.Math.Clamp(fraction, 0f, 1f);
        DrawString(_font, new Vector2(x, y + 9), label, HorizontalAlignment.Left, -1, 10, LabelDim);

        float barX = x + 36;
        float barW = Size.X - barX - 10;
        DrawRect(new Rect2(barX, y, barW, 7), InterfaceTheme.Track, true);

        Color barCol = alert ? RedBar : (fraction > 0.4f ? NominalBar : YellowBar);
        DrawRect(new Rect2(barX, y, barW * fraction, 7), barCol, true);

        return y + 14f;
    }
}
