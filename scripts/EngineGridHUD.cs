namespace Exosphere.Game;

using Godot;
using System.Linq;

// ── Engine grid + propulsion readout ────────────────────────────────────────
// Attitude-cluster board: sits left of the navball. Compact ring in Minimal/Full;
// Clean shrinks to a micro lit/total tally so the disc is never alone.
public partial class EngineGridHUD : Control
{
    private const int RingInner = 3;
    private const int RingMid   = 10;
    private const int RingOuter = 20;

    private static readonly Color DotOff      = InterfaceTheme.Track;
    private static readonly Color DotOn       = new(0.78f, 0.81f, 0.86f, 1f);
    private static readonly Color DotOnHot    = InterfaceTheme.Text;
    private static readonly Color LabelDim    = InterfaceTheme.TextMuted;
    private static readonly Color ValueBright = InterfaceTheme.Text;
    private static readonly Color Accent      = InterfaceTheme.Text;

    private Font _labelFont = null!;
    private Font _valueFont = null!;
    private StyleBoxFlat _panelStyle = null!;
    private bool _micro;
    private bool _showReadouts;

    private int    _litEngines;
    private int    _nominalEngines;
    private double _throttle;
    private double _thrustKN;
    private double _twr;
    private double _ispEff;
    private double _massFlow;
    private bool   _twrValid;
    private readonly List<double> _engineThrottles = new();
    private readonly List<bool> _engineFailures = new();
    private int _drawEngineIndex;

    public override void _Ready()
    {
        _labelFont = InterfaceTheme.BodyFont;
        _valueFont = InterfaceTheme.MonoFont;
        _panelStyle = InterfaceTheme.GlassPanel(0.72f, 10, 0, 0);
        SetAnchorsPreset(LayoutPreset.CenterBottom);
        GrowHorizontal = GrowDirection.Begin;
        GrowVertical   = GrowDirection.Begin;
        MouseFilter = MouseFilterEnum.Ignore;
        ApplyDensityLayout(force: true);
    }

    /// <summary>
    /// Anchor left of the navball (CenterBottom). Compact ring for Minimal/Full;
    /// micro N/M tally for Clean.
    /// </summary>
    private void ApplyDensityLayout(bool force = false)
    {
        var density = UserInterfaceSettings.HudDensity;
        bool micro = density == HudDensity.Clean;
        bool showReadouts = density == HudDensity.Full;
        if (!force && micro == _micro && showReadouts == _showReadouts
            && CustomMinimumSize.Y > 0f)
            return;

        _micro = micro;
        _showReadouts = showReadouts;

        if (_micro)
        {
            CustomMinimumSize = new Vector2(56, 36);
            OffsetRight = -102;
            OffsetLeft = OffsetRight - CustomMinimumSize.X;
            OffsetBottom = -48;
            OffsetTop = OffsetBottom - CustomMinimumSize.Y;
            return;
        }

        float height = _showReadouts ? 210f : 118f;
        float width = 112f;
        CustomMinimumSize = new Vector2(width, height);
        OffsetRight = -102;
        OffsetLeft = OffsetRight - width;
        OffsetBottom = -34;
        OffsetTop = OffsetBottom - height;
    }

    public override void _Process(double delta)
    {
        ApplyDensityLayout();
        var vessel = SimulationBridge.Instance?.ActiveVessel;
        var universe = SimulationBridge.Instance?.Universe;
        if (vessel == null || universe == null) return;

        var body = universe.GetDominantBody(vessel.Position);

        var engines = vessel.Parts.ActiveEngines.ToList();
        _throttle = vessel.Throttle;

        _nominalEngines = System.Math.Max(1,
            engines.Sum(e => System.Math.Max(1, e.Definition.EngineCount)));

        _litEngines = System.Math.Clamp(vessel.ActiveEngineCount, 0, _nominalEngines);
        _engineThrottles.Clear();
        _engineFailures.Clear();
        var readouts = vessel.GetEngineReadouts(body).ToList();
        if (readouts.Count == _nominalEngines)
        {
            _engineThrottles.AddRange(readouts.Select(row => row.Throttle));
            _engineFailures.AddRange(readouts.Select(row => row.FailureCode != null));
        }

        double thrustN = vessel.GetCurrentThrust(body);
        _thrustKN = thrustN / 1000.0;
        _massFlow = vessel.GetCurrentMassFlowTps(body);
        _ispEff   = vessel.GetCurrentIsp(body);

        double localWeight = vessel.GetWeightNewtons(body);
        _twrValid = localWeight > 0 && thrustN > 0;
        _twr = _twrValid ? thrustN / localWeight : 0;

        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        DrawStyleBox(_panelStyle, new Rect2(Vector2.Zero, size));

        if (_micro)
        {
            string tally = $"{_litEngines}/{_nominalEngines}";
            var tw = _valueFont.GetStringSize(tally, HorizontalAlignment.Center, -1, 13);
            DrawString(_valueFont, new Vector2((size.X - tw.X) * 0.5f, size.Y * 0.5f + 4f), tally,
                HorizontalAlignment.Left, -1, 13,
                _litEngines > 0 ? DotOnHot : LabelDim);
            return;
        }

        DrawString(_labelFont, new Vector2(10, 16), "ENGINES",
            HorizontalAlignment.Left, -1, 10, Accent);

        float cx = size.X * 0.5f;
        float cy = 62f;
        float rOuter = 38f, rMid = 24f, rInner = 10f;
        int litRemaining = _litEngines;
        _drawEngineIndex = 0;
        if (_nominalEngines == 33)
        {
            litRemaining = DrawRing(cx, cy, rOuter, RingOuter, litRemaining);
            litRemaining = DrawRing(cx, cy, rMid,   RingMid,   litRemaining);
            DrawRing(cx, cy, rInner, RingInner, litRemaining);
        }
        else
        {
            DrawRing(cx, cy, rMid, _nominalEngines, litRemaining);
        }

        string centre = $"{_litEngines}/{_nominalEngines}";
        var cw = _valueFont.GetStringSize(centre, HorizontalAlignment.Center, -1, 12);
        DrawString(_valueFont, new Vector2(cx - cw.X * 0.5f, cy + 4), centre,
            HorizontalAlignment.Left, -1, 12,
            _litEngines > 0 ? DotOnHot : LabelDim);

        if (!_showReadouts) return;

        float ry = 118f;
        ry = DrawReadout(10, ry, "THRUST", $"{_thrustKN:N0} kN", ValueBright);
        ry = DrawReadout(10, ry, "TWR",
            _twrValid ? $"{_twr:F2}" : "---",
            _twrValid ? (_twr >= 1.0 ? ValueBright : InterfaceTheme.Alert) : LabelDim);
        ry = DrawReadout(10, ry, "Isp",
            _ispEff > 0 ? $"{_ispEff:F0} s" : "---", ValueBright);
        DrawReadout(10, ry, "ṁ",
            _massFlow > 0.001 ? $"{_massFlow:F2} t/s" : "---", ValueBright);
    }

    private int DrawRing(float cx, float cy, float radius, int count, int lit)
    {
        for (int i = 0; i < count; i++)
        {
            double a = -System.Math.PI / 2.0 + i * (2.0 * System.Math.PI / count);
            var p = new Vector2(cx + radius * (float)System.Math.Cos(a),
                                cy + radius * (float)System.Math.Sin(a));
            bool on = lit > 0;
            bool failed = false;
            double throttle = _throttle;
            if (_engineThrottles.Count == _nominalEngines)
            {
                throttle = _engineThrottles[_drawEngineIndex];
                failed = _engineFailures[_drawEngineIndex];
                on = throttle > 1e-3;
            }
            else if (on)
            {
                lit--;
            }
            _drawEngineIndex++;
            Color c = failed
                ? InterfaceTheme.Alert
                : on
                    ? (throttle >= 0.85 ? DotOnHot : DotOn)
                    : DotOff;
            DrawCircle(p, 2.8f, c);
            if (on || failed)
                DrawArc(p, 3.8f, 0, Mathf.Tau, 12, new Color(c, 0.35f), 1.2f, true);
        }
        return lit;
    }

    private float DrawReadout(float x, float y, string label, string value, Color valCol)
    {
        DrawString(_labelFont, new Vector2(x, y), label,
            HorizontalAlignment.Left, -1, 10, LabelDim);
        var vw = _valueFont.GetStringSize(value, HorizontalAlignment.Right, -1, 12);
        DrawString(_valueFont, new Vector2(Size.X - 10 - vw.X, y), value,
            HorizontalAlignment.Left, -1, 12, valCol);
        return y + 18f;
    }
}
