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

    private static readonly Color PanelBg     = new(0.03f, 0.05f, 0.08f, 0.62f);
    private static readonly Color PanelBorder = new(0.28f, 0.55f, 0.85f, 0.45f);
    private static readonly Color DotOff      = new(0.16f, 0.20f, 0.27f, 0.95f);
    private static readonly Color DotOn       = new(1.00f, 0.62f, 0.20f, 1f);
    private static readonly Color DotOnHot    = new(1.00f, 0.85f, 0.45f, 1f);
    private static readonly Color LabelDim    = new(0.60f, 0.68f, 0.78f, 1f);
    private static readonly Color ValueBright = new(0.92f, 0.96f, 1.00f, 1f);
    private static readonly Color Accent      = new(0.45f, 0.80f, 1.00f, 1f);
    private static readonly Color GreenTwr    = new(0.30f, 1.00f, 0.45f, 1f);
    private static readonly Color RedTwr      = new(1.00f, 0.42f, 0.32f, 1f);

    private Font _font = null!;

    // Cached telemetry computed each frame in _Process, rendered in _Draw.
    private int    _litEngines;
    private int    _totalActiveEngines;
    private double _throttle;
    private double _thrustKN;
    private double _twr;
    private double _ispEff;
    private double _massFlow;     // t/s
    private bool   _twrValid;

    public override void _Ready()
    {
        _font = ThemeDB.FallbackFont;
        // Bottom-right corner, above the controls/altitude band.
        SetAnchorsPreset(LayoutPreset.BottomRight);
        GrowHorizontal = GrowDirection.Begin;
        GrowVertical   = GrowDirection.Begin;
        CustomMinimumSize = new Vector2(208, 232);
        OffsetLeft = -226; OffsetTop = -344;
        OffsetRight = -18; OffsetBottom = -112;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        var vessel = SimulationBridge.Instance?.ActiveVessel;
        var universe = SimulationBridge.Instance?.Universe;
        if (vessel == null || universe == null) return;

        var body = universe.GetDominantBody(vessel.Position);
        double alt = vessel.GetAltitude(body);

        // Ambient pressure proxy: blend SL/Vac figures by atmospheric density ratio so
        // thrust/Isp read realistically through ascent. (We only have density getters.)
        double rho = body.Atmosphere != null ? body.GetAtmosphericDensity(vessel.Position) : 0.0;
        double rho0 = body.Atmosphere?.SeaLevelDensity ?? 1.225;
        double atmFrac = rho0 > 0 ? System.Math.Clamp(rho / rho0, 0.0, 1.0) : 0.0;

        var engines = vessel.Parts.ActiveEngines.ToList();
        _totalActiveEngines = engines.Count;
        _throttle = vessel.Throttle;

        // Light an engine if throttle is up AND there's propellant feeding it.
        bool feeding = _throttle > 0.01 && _totalActiveEngines > 0;
        // Scale the number of "lit" dots by throttle so a partial throttle reads as a
        // partial-thrust board, but always light at least the active count when firing.
        _litEngines = feeding
            ? System.Math.Clamp(_totalActiveEngines, 0, TotalEngines)
            : 0;

        double thrustN = 0, massFlowKgs = 0, ispThrustSum = 0;
        foreach (var en in engines)
        {
            var d = en.Definition;
            double thrustVacN = d.ThrustVac;
            double thrustSLN  = d.ThrustSL > 0 ? d.ThrustSL : d.ThrustVac;
            double isp        = d.IspVac > 0 ? d.IspVac : 1.0;
            double ispSL      = d.IspSL  > 0 ? d.IspSL  : isp;

            double thr = en.ThrottleLevel > 0 ? en.ThrottleLevel : _throttle;
            double thrustHere = Lerp(thrustVacN, thrustSLN, atmFrac) * thr;
            double ispHere    = Lerp(isp, ispSL, atmFrac);

            thrustN += thrustHere;
            ispThrustSum += ispHere * thrustHere;
            // mdot = F / (Isp · g0)
            if (ispHere > 0) massFlowKgs += thrustHere / (ispHere * 9.80665);
        }

        _thrustKN = thrustN / 1000.0;
        _massFlow = massFlowKgs / 1000.0;                  // t/s
        _ispEff   = thrustN > 0 ? ispThrustSum / thrustN : 0;

        double surfG = body.GetSurfaceGravity();
        _twrValid = vessel.TotalMass > 0 && surfG > 0 && thrustN > 0;
        _twr = _twrValid ? thrustN / (vessel.TotalMass * surfG) : 0;

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
        litRemaining = DrawRing(cx, cy, rOuter, RingOuter, litRemaining);
        litRemaining = DrawRing(cx, cy, rMid,   RingMid,   litRemaining);
        DrawRing(cx, cy, rInner, RingInner, litRemaining);

        // Lit / total tally in the centre.
        string tally = $"{_litEngines}/{TotalEngines}";
        var tw = _font.GetStringSize(tally, HorizontalAlignment.Center, -1, 13);
        DrawString(_font, new Vector2(cx - tw.X * 0.5f, cy + 4), tally,
            HorizontalAlignment.Left, -1, 13,
            _litEngines > 0 ? DotOnHot : LabelDim);

        // Readout rows.
        float ry = 150f;
        ry = DrawReadout(12, ry, "THRUST", $"{_thrustKN:N0} kN", ValueBright);
        ry = DrawReadout(12, ry, "TWR",
            _twrValid ? $"{_twr:F2}" : "---",
            _twrValid ? (_twr >= 1.0 ? GreenTwr : RedTwr) : LabelDim);
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
        DrawRect(r, PanelBg, true);
        DrawRect(r, PanelBorder, false, 1.0f);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
