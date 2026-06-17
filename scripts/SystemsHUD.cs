namespace Exosphere.Game;

using Godot;

public partial class SystemsHUD : Control
{
    private static readonly Color PanelBg     = new(0.03f, 0.05f, 0.08f, 0.62f);
    private static readonly Color PanelBorder = new(0.28f, 0.55f, 0.85f, 0.45f);
    private static readonly Color GreenBar    = new(0.30f, 1.00f, 0.45f, 1f);
    private static readonly Color YellowBar   = new(1.00f, 0.78f, 0.25f, 1f);
    private static readonly Color RedBar      = new(1.00f, 0.40f, 0.30f, 1f);
    private static readonly Color LabelDim    = new(0.60f, 0.68f, 0.78f, 1f);
    private static readonly Color Accent      = new(0.45f, 0.80f, 1.00f, 1f);

    private Font _font = null!;

    public override void _Ready()
    {
        _font = ThemeDB.FallbackFont;
        SetAnchorsPreset(LayoutPreset.TopRight);
        GrowHorizontal = GrowDirection.Begin;
        CustomMinimumSize = new Vector2(180, 220);
        OffsetLeft  = -198; OffsetTop    = 10;
        OffsetRight =  -18; OffsetBottom = 230;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var sys = SystemsController.Instance;
        if (sys == null) return;

        var size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), PanelBg, true);
        DrawRect(new Rect2(Vector2.Zero, size), PanelBorder, false, 1f);

        DrawString(_font, new Vector2(10, 16), "SYSTEMS", HorizontalAlignment.Left, -1, 12, Accent);

        float y = 28f;
        y = DrawBar(10, y, "O2",   (float)sys.LifeSupport.OxygenFraction,    sys.LifeSupport.OxygenAlert);
        y = DrawBar(10, y, "CO2",  1.0f - (float)sys.LifeSupport.CO2Fraction, sys.LifeSupport.CO2Alert);
        y = DrawBar(10, y, "H2O",  (float)sys.LifeSupport.WaterFraction,      false);
        y = DrawBar(10, y, "PWR",  (float)sys.Power.BatteryFraction,           sys.Power.LowPowerAlert);
        y = DrawBar(10, y, "TEMP", (float)sys.Thermal.ThermalFraction,         sys.Thermal.HotAlert || sys.Thermal.ColdAlert);
        DrawBar(10, y,      "COMM", (float)sys.Comms.SignalStrength,            sys.Comms.LossOfSignalAlert);

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
        DrawRect(new Rect2(barX, y, barW, 9), new Color(0.12f, 0.16f, 0.22f), true);

        Color barCol = alert ? RedBar : (fraction > 0.4f ? GreenBar : YellowBar);
        DrawRect(new Rect2(barX, y, barW * fraction, 9), barCol, true);

        return y + 14f;
    }
}
