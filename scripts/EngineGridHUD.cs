namespace Exosphere.Game;

using Godot;
using System.Linq;

// ── Engine grid + propulsion readout ────────────────────────────────────────
// SpaceX-webcast style "engine board": a ring/grid of dots (one per Raptor) that
// light up with the active engines and current throttle, alongside the live
// propulsion figures (thrust, TWR, Isp, mass-flow). Drawn as a self-contained
// Control; instantiated as a child of HUDController. Reads ONLY public getters.
public partial class EngineGridHUD : Control
{
    // Super Heavy carries 33 Raptors; we draw the booster ring layout (3 + 10 + 20).
    private const int RingInner = 3;
    private const int RingMid   = 10;
    private const int RingOuter = 20;
    private const int TotalEngines = RingInner + RingMid + RingOuter;

    private static readonly Color DotOff      = InterfaceTheme.Track;
    private static readonly Color DotOn       = new(0.78f, 0.81f, 0.86f, 1f);
    private static readonly Color DotOnHot    = InterfaceTheme.Text;
    private static readonly Color LabelDim    = InterfaceTheme.TextMuted;
    private static readonly Color ValueBright = InterfaceTheme.Text;
    private static readonly Color Accent      = InterfaceTheme.Text;
    private static readonly Color RedTwr      = InterfaceTheme.Alert;

    private Font _font = null!;
    private StyleBoxFlat _panelStyle = null!;

    // Cached telemetry computed each frame in _Process, rendered in _Draw.
    private int    _litEngines;
    private int    _totalActiveEngines;
    private int    _nominalEngines;    // 33 for Super Heavy, 6 for Starship
    private double _throttle;
    private double _thrustKN;
    private double _twr;
    private double _ispEff;
    private double _massFlow;     // t/s
    private bool   _twrValid;

    public override void _Ready()
    {
        _font = ThemeDB.FallbackFont;
        _panelStyle = InterfaceTheme.GlassPanel(0.76f, 12, 0, 0);
        // Bottom-LEFT, above the money/controls band — frees the bottom-right for the MAP.
        SetAnchorsPreset(LayoutPreset.BottomLeft);
        GrowHorizontal = GrowDirection.End;
        GrowVertical   = GrowDirection.Begin;
        CustomMinimumSize = new Vector2(194, 224);
        OffsetLeft = 18; OffsetTop = -340;
        OffsetRight = 212; OffsetBottom = -116;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        var vessel = SimulationBridge.Instance?.ActiveVessel;
        var universe = SimulationBridge.Instance?.Universe;
        if (vessel == null || universe == null) return;

        var body = universe.GetDominantBody(vessel.Position);

        var engines = vessel.Parts.ActiveEngines.ToList();
        _totalActiveEngines = engines.Sum(e => System.Math.Max(1, e.Definition.EngineCount));
        _throttle = vessel.Throttle;

        _nominalEngines = System.Math.Max(1, _totalActiveEngines);

        // Light an engine if throttle is up AND there's propellant feeding it.
        bool feeding = _throttle > 0.01 && _totalActiveEngines > 0;
        // Scale the number of "lit" dots proportionally with throttle.
        _litEngines = feeding
            ? System.Math.Clamp((int)System.Math.Round(_nominalEngines * _throttle), 0, _nominalEngines)
            : 0;

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
        DrawPanel(new Rect2(Vector2.Zero, size));

        DrawString(_font, new Vector2(12, 18), "PROPULSION",
            HorizontalAlignment.Left, -1, 13, Accent);

        // Engine board (concentric rings).
        float cx = size.X * 0.5f;
        float cy = 78f;
        float rOuter = 50f, rMid = 32f, rInner = 13f;
        int litRemaining = _litEngines;
        if (_nominalEngines == 33)
        {
            // Super Heavy: 3 rings (3 inner + 10 mid + 20 outer)
            litRemaining = DrawRing(cx, cy, rOuter, RingOuter, litRemaining);
            litRemaining = DrawRing(cx, cy, rMid,   RingMid,   litRemaining);
            DrawRing(cx, cy, rInner, RingInner, litRemaining);
        }
        else
        {
            // Starship: single ring of 6 at mid radius
            DrawRing(cx, cy, rMid, _nominalEngines, litRemaining);
        }

        // Lit / total tally in the centre.
        string tally = $"{_litEngines}/{_nominalEngines}";
        var tw = _font.GetStringSize(tally, HorizontalAlignment.Center, -1, 13);
        DrawString(_font, new Vector2(cx - tw.X * 0.5f, cy + 4), tally,
            HorizontalAlignment.Left, -1, 13,
            _litEngines > 0 ? DotOnHot : LabelDim);

        // Readout rows.
        float ry = 150f;
        ry = DrawReadout(12, ry, "THRUST", $"{_thrustKN:N0} kN", ValueBright);
        ry = DrawReadout(12, ry, "TWR",
            _twrValid ? $"{_twr:F2}" : "---",
            _twrValid ? (_twr >= 1.0 ? ValueBright : RedTwr) : LabelDim);
        ry = DrawReadout(12, ry, "Isp",
            _ispEff > 0 ? $"{_ispEff:F0} s" : "---", ValueBright);
        DrawReadout(12, ry, "ṁ FLOW",
            _massFlow > 0.001 ? $"{_massFlow:F2} t/s" : "---", ValueBright);
    }

    // Draws up to `count` dots evenly around a ring; lights the first `lit` of them.
    // Returns the lit budget left over for the next (inner) ring.
    private int DrawRing(float cx, float cy, float radius, int count, int lit)
    {
        for (int i = 0; i < count; i++)
        {
            double a = -System.Math.PI / 2.0 + i * (2.0 * System.Math.PI / count);
            var p = new Vector2(cx + radius * (float)System.Math.Cos(a),
                                cy + radius * (float)System.Math.Sin(a));
            bool on = lit > 0;
            if (on) lit--;
            Color c = on
                ? (_throttle >= 0.85 ? DotOnHot : DotOn)
                : DotOff;
            DrawCircle(p, 3.4f, c);
            if (on) DrawArc(p, 4.6f, 0, Mathf.Tau, 12, new Color(c, 0.35f), 1.4f, true);
        }
        return lit;
    }

    private float DrawReadout(float x, float y, string label, string value, Color valCol)
    {
        DrawString(_font, new Vector2(x, y), label,
            HorizontalAlignment.Left, -1, 12, LabelDim);
        var vw = _font.GetStringSize(value, HorizontalAlignment.Right, -1, 14);
        DrawString(_font, new Vector2(Size.X - 12 - vw.X, y), value,
            HorizontalAlignment.Left, -1, 14, valCol);
        return y + 19f;
    }

    private void DrawPanel(Rect2 r)
    {
        DrawStyleBox(_panelStyle, r);
    }
}
