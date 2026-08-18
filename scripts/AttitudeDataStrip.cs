namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation.Presentation;

/// <summary>
/// Glass column right of the navball. Positioned as a child of AttitudeNavball
/// with a local Position — not viewport anchors.
/// </summary>
public partial class AttitudeDataStrip : Control
{
    private const double RefreshPeriodSeconds = 1.0 / 30.0;
    public const float BoardWidth = 112f;
    public const float BoardHeight = 168f;

    private Font _labelFont = null!;
    private Font _valueFont = null!;
    private StyleBoxFlat _panelStyle = null!;

    private double _throttle;
    private int _lit;
    private int _total = 1;
    private double _twr;
    private bool _twrValid;
    private string? _primaryFailureCode;
    private double? _apKm;
    private double? _peKm;
    private double _fuelPct;
    private bool _useOrbit;
    private double _refreshAccumulator = double.MaxValue;
    private bool _hasPendingSnapshot = true;

    public override void _Ready()
    {
        _labelFont = InterfaceTheme.BodyFont;
        _valueFont = InterfaceTheme.MonoFont;
        _panelStyle = InterfaceTheme.GlassPanel(0.82f, 10, 8, 8);
        CustomMinimumSize = new Vector2(BoardWidth, BoardHeight);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = false;
    }

    public override void _Process(double delta)
    {
        if (Size.X < 8f || Size.Y < 8f)
            Size = CustomMinimumSize;
        if (!Visible) return;

        _refreshAccumulator += System.Math.Max(0.0, delta);
        if (!_hasPendingSnapshot || _refreshAccumulator < RefreshPeriodSeconds) return;
        _refreshAccumulator %= RefreshPeriodSeconds;
        _hasPendingSnapshot = false;
        QueueRedraw();
    }

    public void UpdateFromSnapshot(FlightHudSnapshot? snapshot)
    {
        if (snapshot == null)
        {
            _hasPendingSnapshot = true;
            return;
        }

        _throttle = snapshot.Throttle;
        _lit = snapshot.ActiveEngineCount;
        _total = System.Math.Max(1, snapshot.NominalEngineCount);
        _twr = snapshot.ThrustToWeightRatio;
        _twrValid = snapshot.CurrentThrustN > 0.0 && _twr > 0.0;
        _primaryFailureCode = snapshot.FailedEngineCount > 0
            ? snapshot.PrimaryEngineFailureCode
            : null;
        _fuelPct = snapshot.LiquidFuelFraction * 100.0;
        _useOrbit = snapshot.ApoapsisAltitudeM is { } || snapshot.PeriapsisAltitudeM is { };
        _apKm = snapshot.ApoapsisAltitudeM is { } ap ? ap / 1000.0 : null;
        _peKm = snapshot.PeriapsisAltitudeM is { } pe ? pe / 1000.0 : null;
        _hasPendingSnapshot = true;
    }

    public override void _Draw()
    {
        var size = Size.X >= 8f ? Size : CustomMinimumSize;
        if (size.X < 8f || size.Y < 8f) return;
        DrawStyleBox(_panelStyle, new Rect2(Vector2.Zero, size));

        float y = 16f;
        y = Row(y, size.X, "THR", $"{_throttle * 100.0:F0}%", InterfaceTheme.Text);
        y = Row(y, size.X, "ENG", $"{_lit}/{_total}",
            _lit > 0 ? InterfaceTheme.Text : InterfaceTheme.TextMuted);
        y = Row(y, size.X, "TWR",
            _twrValid ? $"{_twr:F2}" : "---",
            _twrValid
                ? (_twr >= 1.0 ? InterfaceTheme.Text : InterfaceTheme.Alert)
                : InterfaceTheme.TextMuted);

        y += 6f;
        if (_useOrbit)
        {
            y = Row(y, size.X, "Ap", FormatOrbitKm(_apKm), InterfaceTheme.Text);
            y = Row(y, size.X, "Pe", FormatOrbitKm(_peKm), InterfaceTheme.Text);
        }
        else
        {
            y = Row(y, size.X, "FUEL", $"{_fuelPct:F0}%",
                _fuelPct < 15.0 ? InterfaceTheme.Warning : InterfaceTheme.Text);
        }
        if (!string.IsNullOrWhiteSpace(_primaryFailureCode))
            Row(y, size.X, "FAIL", FormatFailureCode(_primaryFailureCode), InterfaceTheme.Alert);
    }

    private float Row(float y, float width, string label, string value, Color valueColor)
    {
        DrawString(_labelFont, new Vector2(10, y), label,
            HorizontalAlignment.Left, -1, 10, InterfaceTheme.TextMuted);
        var vw = _valueFont.GetStringSize(value, HorizontalAlignment.Right, -1, 12);
        DrawString(_valueFont, new Vector2(width - 10 - vw.X, y), value,
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

    private static string FormatFailureCode(string? code) => code switch
    {
        "PROPELLANT_STARVATION" => "STARVATION",
        "FEED_BRANCH_FLOW_LIMIT" => "FEED LIMIT",
        "ENGINE_OVERTEMPERATURE" => "OVERHEAT",
        "RESTART_LIMIT_EXCEEDED" => "RESTART LIMIT",
        _ when !string.IsNullOrWhiteSpace(code) && code!.Length > 13 => code[..13],
        _ => code ?? "UNKNOWN",
    };
}
