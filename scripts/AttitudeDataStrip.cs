namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation.Presentation;

/// <summary>
/// Compact glass column to the right of the navball: throttle, engines, TWR,
/// and Ap/Pe (or propellant % when orbit numbers are unavailable).
/// </summary>
public partial class AttitudeDataStrip : Control
{
    private Font _labelFont = null!;
    private Font _valueFont = null!;
    private StyleBoxFlat _panelStyle = null!;

    private double _throttle;
    private int _lit;
    private int _total;
    private double _twr;
    private bool _twrValid;
    private double? _apKm;
    private double? _peKm;
    private double _fuelPct;
    private bool _useOrbit;

    public override void _Ready()
    {
        _labelFont = InterfaceTheme.BodyFont;
        _valueFont = InterfaceTheme.MonoFont;
        _panelStyle = InterfaceTheme.GlassPanel(0.72f, 10, 8, 8);

        SetAnchorsPreset(LayoutPreset.CenterBottom);
        GrowHorizontal = GrowDirection.End;
        GrowVertical = GrowDirection.Begin;
        float width = 108f;
        float height = 168f;
        CustomMinimumSize = new Vector2(width, height);
        // Sit just right of the navball disc (radius 78 + pad 14 ≈ 92).
        OffsetLeft = 102;
        OffsetRight = OffsetLeft + width;
        OffsetBottom = -34;
        OffsetTop = OffsetBottom - height;
        Size = new Vector2(width, height);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void UpdateFromSnapshot(FlightHudSnapshot? snapshot)
    {
        if (snapshot == null)
        {
            QueueRedraw();
            return;
        }

        _throttle = snapshot.Throttle;
        _lit = snapshot.ActiveEngineCount;
        _total = System.Math.Max(1, snapshot.NominalEngineCount);
        _twr = snapshot.ThrustToWeightRatio;
        _twrValid = snapshot.CurrentThrustN > 0.0 && _twr > 0.0;
        _fuelPct = snapshot.LiquidFuelFraction * 100.0;
        _useOrbit = snapshot.ApoapsisAltitudeM is { } || snapshot.PeriapsisAltitudeM is { };
        _apKm = snapshot.ApoapsisAltitudeM is { } ap ? ap / 1000.0 : null;
        _peKm = snapshot.PeriapsisAltitudeM is { } pe ? pe / 1000.0 : null;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Size.X < 8f || Size.Y < 8f) return;
        DrawStyleBox(_panelStyle, new Rect2(Vector2.Zero, Size));

        float y = 16f;
        y = Row(y, "THR", $"{_throttle * 100.0:F0}%", InterfaceTheme.Text);
        y = Row(y, "ENG", $"{_lit}/{_total}",
            _lit > 0 ? InterfaceTheme.Text : InterfaceTheme.TextMuted);
        y = Row(y, "TWR",
            _twrValid ? $"{_twr:F2}" : "---",
            _twrValid
                ? (_twr >= 1.0 ? InterfaceTheme.Text : InterfaceTheme.Alert)
                : InterfaceTheme.TextMuted);

        y += 6f;
        if (_useOrbit)
        {
            y = Row(y, "Ap", FormatOrbitKm(_apKm), InterfaceTheme.Text);
            Row(y, "Pe", FormatOrbitKm(_peKm), InterfaceTheme.Text);
        }
        else
        {
            Row(y, "FUEL", $"{_fuelPct:F0}%",
                _fuelPct < 15.0 ? InterfaceTheme.Warning : InterfaceTheme.Text);
        }
    }

    private float Row(float y, string label, string value, Color valueColor)
    {
        DrawString(_labelFont, new Vector2(10, y), label,
            HorizontalAlignment.Left, -1, 10, InterfaceTheme.TextMuted);
        var vw = _valueFont.GetStringSize(value, HorizontalAlignment.Right, -1, 12);
        DrawString(_valueFont, new Vector2(Size.X - 10 - vw.X, y), value,
            HorizontalAlignment.Left, -1, 12, valueColor);
        return y + 22f;
    }

    private static string FormatOrbitKm(double? km)
    {
        if (km is not { } v || !double.IsFinite(v)) return "---";
        if (v < 0.0) return "neg";
        if (System.Math.Abs(v) >= 1000.0) return $"{v / 1000.0:F1} Mm";
        return $"{v:F0} km";
    }
}
