namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation.Presentation;

/// <summary>
/// Flat data column right of the navball: propulsion state, the orbit/propellant block
/// and the entry block a Starship pilot flies the belly-flop on. Positioned as a child of
/// AttitudeNavball with a local Position — not viewport anchors.
/// </summary>
public partial class AttitudeDataStrip : Control
{
    private const double RefreshPeriodSeconds = 1.0 / 30.0;
    public const float BoardWidth = 112f;
    /// <summary>Propulsion + orbit block only (Clean density).</summary>
    public const float BoardHeight = 168f;
    /// <summary>Adds the entry block (Mach / AOA / bank / heat flux).</summary>
    public const float BoardHeightEntry = 272f;

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
    private double _mach = double.NaN;
    private double _angleOfAttackDeg = double.NaN;
    private double _bankDeg = double.NaN;
    private double _heatFluxWPerM2 = double.NaN;
    private double _refreshAccumulator = double.MaxValue;
    private bool _hasPendingSnapshot = true;
    private bool? _showEntryBlock;

    public override void _Ready()
    {
        _labelFont = InterfaceTheme.LabelFont;
        _valueFont = InterfaceTheme.MonoFont;
        _panelStyle = InterfaceTheme.PanelStyle(0.92f, 8, 8);
        CustomMinimumSize = new Vector2(BoardWidth, BoardHeight);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = false;
    }

    /// <summary>
    /// The entry block needs ~100 px the Clean cluster does not have above the screen edge,
    /// so the board keeps its compact height there. Width is owned by the caller: long
    /// failure values need more room than the nominal board.
    /// </summary>
    public void ApplyDensityLayout(bool force = false)
    {
        bool showEntry = UserInterfaceSettings.HudDensity != HudDensity.Clean;
        if (!force && _showEntryBlock == showEntry)
            return;

        _showEntryBlock = showEntry;
        float width = Mathf.Max(BoardWidth, Size.X);
        float height = showEntry ? BoardHeightEntry : BoardHeight;
        CustomMinimumSize = new Vector2(width, height);
        Size = CustomMinimumSize;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        ApplyDensityLayout();
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
        _mach = snapshot.MachNumber;
        _angleOfAttackDeg = snapshot.AngleOfAttackDeg;
        _bankDeg = snapshot.AerodynamicBankDeg;
        _heatFluxWPerM2 = snapshot.StagnationHeatFluxWPerM2;
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
            _twrValid ? $"{_twr:F2}" : "—",
            _twrValid
                ? (_twr >= 1.0 ? InterfaceTheme.Text : InterfaceTheme.Alert)
                : InterfaceTheme.TextMuted);

        y += 6f;
        if (_useOrbit)
        {
            y = Row(y, size.X, "AP", FormatOrbitKm(_apKm), InterfaceTheme.Text);
            y = Row(y, size.X, "PE", FormatOrbitKm(_peKm), InterfaceTheme.Text);
        }
        else
        {
            y = Row(y, size.X, "FUEL", $"{_fuelPct:F0}%",
                _fuelPct < 15.0 ? InterfaceTheme.Warning : InterfaceTheme.Text);
        }

        if (_showEntryBlock != false)
            y = DrawEntryBlock(y, size.X);

        if (!string.IsNullOrWhiteSpace(_primaryFailureCode))
            Row(y, size.X, "FAIL", FormatFailureCode(_primaryFailureCode), InterfaceTheme.Alert);
    }

    /// <summary>
    /// Aerodynamic state during entry and the belly-flop. Every value here is sampled by the
    /// simulation's entry diagnostics; an undefined one (vacuum, zero lift) reads as a dash.
    /// </summary>
    private float DrawEntryBlock(float y, float width)
    {
        y += 4f;
        DrawRect(new Rect2(10f, y, width - 20f, 1f), InterfaceTheme.Edge);
        y += 13f;
        DrawString(_labelFont, new Vector2(10f, y), "ENTRY",
            HorizontalAlignment.Left, -1, 11, InterfaceTheme.TextFaint);
        y += 16f;

        y = Row(y, width, "MACH", FormatMach(_mach), InterfaceTheme.Text, pitch: 20f);
        y = Row(y, width, "AOA", FormatDegrees(_angleOfAttackDeg, signed: false),
            InterfaceTheme.Text, pitch: 20f);
        y = Row(y, width, "BANK", FormatDegrees(_bankDeg, signed: true),
            InterfaceTheme.Text, pitch: 20f);
        return Row(y, width, "HEAT", FormatHeatFlux(_heatFluxWPerM2),
            InterfaceTheme.Text, pitch: 22f);
    }

    private float Row(
        float y, float width, string label, string value, Color valueColor, float pitch = 22f)
    {
        DrawString(_labelFont, new Vector2(10, y), label,
            HorizontalAlignment.Left, -1, 11, InterfaceTheme.TextMuted);
        var vw = _valueFont.GetStringSize(value, HorizontalAlignment.Right, -1, 12);
        DrawString(_valueFont, new Vector2(width - 10 - vw.X, y), value,
            HorizontalAlignment.Left, -1, 12, valueColor);
        return y + pitch;
    }

    private static string FormatOrbitKm(double? km)
    {
        if (km is not { } v || !double.IsFinite(v)) return "—";
        if (v < 0.0) return "neg";
        if (System.Math.Abs(v) >= 1000.0) return $"{v / 1000.0:F1} Mm";
        return $"{v:F0} km";
    }

    private static string FormatMach(double mach) =>
        !double.IsFinite(mach) ? "—"
            : mach >= 10.0 ? $"{mach:F1}"
            : $"{mach:F2}";

    private static string FormatDegrees(double degrees, bool signed) =>
        !double.IsFinite(degrees)
            ? "—"
            : signed ? $"{degrees:+0;-0}°" : $"{degrees:F0}°";

    private static string FormatHeatFlux(double wattsPerSquareMetre)
    {
        if (!double.IsFinite(wattsPerSquareMetre)) return "—";
        double kilowatts = wattsPerSquareMetre / 1000.0;
        return kilowatts >= 1000.0
            ? $"{kilowatts / 1000.0:F2} MW/m²"
            : $"{kilowatts:F0} kW/m²";
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
