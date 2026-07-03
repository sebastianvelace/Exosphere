namespace Exosphere.Game;

using Godot;

public partial class SystemsHUD : Control
{
    private static readonly Color NominalBar  = new(0.88f, 0.90f, 0.94f, 1f);
    private static readonly Color YellowBar   = InterfaceTheme.Warning;
    private static readonly Color RedBar      = InterfaceTheme.Alert;
    private static readonly Color LabelDim    = InterfaceTheme.TextMuted;
    private static readonly Color Accent      = InterfaceTheme.Text;

    private Font _font = null!;
    private StyleBoxFlat _panelStyle = null!;

    public override void _Ready()
    {
        _font = ThemeDB.FallbackFont;
        _panelStyle = InterfaceTheme.GlassPanel(0.76f, 12, 0, 0);
        // Secondary health information sits below the orbit block on the right.
        SetAnchorsPreset(LayoutPreset.TopRight);
        GrowHorizontal = GrowDirection.Begin;
        CustomMinimumSize = new Vector2(278, 190);
        OffsetLeft = -296; OffsetTop = 340;
        OffsetRight = -18; OffsetBottom = 530;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta) => QueueRedraw();

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

        string mode = sys.ControlLimited ? "CONTROL LIMITED" : "CONTROL NOMINAL";
        DrawString(_font, new Vector2(10, y + 12), mode,
            HorizontalAlignment.Left, -1, 10, sys.ControlLimited ? RedBar : LabelDim);

        // Signal delay label
        float delay = (float)sys.Comms.SignalDelaySeconds;
        if (delay > 0.01f)
        {
            string delayStr = delay < 1.0f ? $"{delay * 1000:F0} ms" : $"{delay:F1} s";
            DrawString(_font, new Vector2(10, size.Y - 8), $"Dt {delayStr}",
                HorizontalAlignment.Left, -1, 10, LabelDim);
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
