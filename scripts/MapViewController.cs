namespace Exosphere.Game;

using Godot;
using System.Collections.Generic;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;

/// <summary>
/// Bottom-right orbital map panel (toggle with M). Draws the active vessel's orbit
/// around its dominant body as a perifocal ellipse/conic, a draggable maneuver node,
/// and the projected post-burn orbit (dashed). Mouse wheel adjusts prograde ΔV;
/// Enter arms the autopilot to execute the planned burn.
/// </summary>
public partial class MapViewController : Control
{
    public static MapViewController? Instance { get; private set; }

    public ManeuverPlanner Planner { get; } = new();
    private AutopilotController _autopilot = null!;

    // ── Transfer planner state ────────────────────────────────────────────────
    private string? _selectedTarget;   // body ID selected for Hohmann transfer

    // Planets available for transfer, in keyboard order (keys 1–6)
    private static readonly (string id, string label, Key key)[] TransferTargets =
    {
        ("mars",    "Mars",    Key.Key1),
        ("moon",    "Moon",    Key.Key2),
        ("venus",   "Venus",   Key.Key3),
        ("jupiter", "Jupiter", Key.Key4),
        ("mercury", "Mercury", Key.Key5),
        ("saturn",  "Saturn",  Key.Key6),
    };

    // ── Layout ────────────────────────────────────────────────────────────────
    private const float PanelSize = 460f;
    private const float Margin    = 34f;     // px padding inside panel for the orbit
    private Vector2 _center;                  // panel-local centre of the plot
    private double  _metersPerPixel = 1.0;    // current draw scale
    private Vector2 _plotOrigin;              // panel-local px of refBody centre

    // ── Palette ───────────────────────────────────────────────────────────────
    private static readonly Color BgCol      = new(0.02f, 0.03f, 0.06f, 0.82f);
    private static readonly Color BorderCol  = new(0.35f, 0.65f, 0.95f, 0.65f);
    private static readonly Color OrbitCol   = new(0.40f, 0.85f, 1.00f, 0.95f);
    private static readonly Color ProjCol    = new(1.00f, 0.65f, 0.20f, 0.95f);
    private static readonly Color BodyCol    = new(0.30f, 0.55f, 0.95f, 1.00f);
    private static readonly Color VesselCol  = new(0.95f, 0.98f, 1.00f, 1.00f);
    private static readonly Color NodeCol    = new(1.00f, 0.80f, 0.25f, 1.00f);
    private static readonly Color ApCol      = new(0.55f, 0.95f, 1.00f, 1.00f);
    private static readonly Color PeCol      = new(1.00f, 0.55f, 0.45f, 1.00f);
    private static readonly Color TextDim    = new(0.62f, 0.72f, 0.84f, 1.00f);
    private static readonly Color TextBright = new(0.92f, 0.97f, 1.00f, 1.00f);

    private Font _font = null!;
    private bool _dragging;

    public override void _Ready()
    {
        Instance = this;
        Visible  = false;

        _font = ThemeDB.GetFallbackFont();
        CustomMinimumSize = new Vector2(PanelSize, PanelSize);
        Size = new Vector2(PanelSize, PanelSize);
        SetAnchorsPreset(LayoutPreset.BottomRight);
        GrowHorizontal = GrowDirection.Begin;
        GrowVertical   = GrowDirection.Begin;
        OffsetLeft = -PanelSize - 18; OffsetTop = -PanelSize - 18;
        OffsetRight = -18; OffsetBottom = -18;
        MouseFilter = MouseFilterEnum.Stop;

        _center = new Vector2(PanelSize * 0.5f, PanelSize * 0.5f - 14f);

        _autopilot = new AutopilotController { Name = "AutopilotController" };
        _autopilot.Bind(Planner);
        AddChild(_autopilot);

        // Instanciar el planificador de transferencias y el ejecutor de maniobras
        var planner  = new TransferPlanner  { Name = "TransferPlanner" };
        var executor = new ManeuverExecutor { Name = "ManeuverExecutor" };
        AddChild(planner);
        AddChild(executor);
    }

    public void ToggleVisible() => Visible = !Visible;

    // ── Input ─────────────────────────────────────────────────────────────────

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            switch (key.Keycode)
            {
                case Key.M:
                    ToggleVisible();
                    QueueRedraw();
                    break;

                case Key.Enter or Key.KpEnter when Visible:
                    // Enter with a transfer node selected: arm the ManeuverExecutor
                    if (TransferPlanner.Instance?.CurrentNode is { } tNode)
                    {
                        ManeuverExecutor.Instance?.ExecuteNode(tNode);
                        QueueRedraw();
                        break;
                    }
                    // Fallback: arm the local autopilot for a manual node
                    if (Planner.HasNode)
                        _autopilot.Arm();
                    break;

                case Key.J when Visible && _selectedTarget != null:
                    SimulationBridge.Instance?.JumpToBody(_selectedTarget);
                    Visible = false;
                    break;

                case Key.Delete or Key.Backspace when Visible:
                    Planner.ClearNode();
                    _autopilot.Disarm();
                    TransferPlanner.Instance?.ClearNode();
                    ManeuverExecutor.Instance?.Abort();
                    _selectedTarget = null;
                    QueueRedraw();
                    break;

                // Transfer Δv factor adjustment: [ decreases, ] increases (5% steps)
                case Key.Bracketleft when Visible:
                {
                    var tn = TransferPlanner.Instance?.CurrentNode;
                    if (tn != null)
                    {
                        tn.DvAdjustFactor = System.Math.Max(0.50, tn.DvAdjustFactor - 0.05);
                        QueueRedraw();
                    }
                    break;
                }
                case Key.Bracketright when Visible:
                {
                    var tn = TransferPlanner.Instance?.CurrentNode;
                    if (tn != null)
                    {
                        tn.DvAdjustFactor = System.Math.Min(1.50, tn.DvAdjustFactor + 0.05);
                        QueueRedraw();
                    }
                    break;
                }

                default:
                    // Transfer target selection (1–5) when map is open
                    if (Visible)
                    {
                        foreach (var (id, _, tKey) in TransferTargets)
                        {
                            if (key.Keycode == tKey)
                            {
                                _selectedTarget = id;
                                var node = TransferPlanner.Instance?.PlanTransfer(id);
                                if (node != null)
                                    GD.Print($"[Map] Transfer to {id}: " +
                                             $"Δv={node.DvMagnitude:F0} m/s, " +
                                             $"ToF={node.TimeOfFlight / 86400.0:F1} days");
                                QueueRedraw();
                                break;
                            }
                        }
                    }
                    break;
            }
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (!Visible || !Planner.HasOrbit) return;

        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                _dragging = mb.Pressed;
                if (mb.Pressed) PlaceNodeFromMouse(mb.Position);
                AcceptEvent();
            }
            else if (mb.Pressed && Planner.HasNode &&
                     (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown))
            {
                double dir  = mb.ButtonIndex == MouseButton.WheelUp ? 1.0 : -1.0;
                double step = Input.IsKeyPressed(Key.Shift) ? 100.0 : 10.0;
                if (Input.IsKeyPressed(Key.Alt)) Planner.DvRadial   += dir * step;
                else                             Planner.DvPrograde += dir * step;
                _autopilot.Disarm();   // editing invalidates a previously-armed burn
                QueueRedraw();
                AcceptEvent();
            }
        }
        else if (@event is InputEventMouseMotion motion && _dragging)
        {
            PlaceNodeFromMouse(motion.Position);
            AcceptEvent();
        }
    }

    private void PlaceNodeFromMouse(Vector2 panelPos)
    {
        // Invert the draw transform: panel px → map metres → true anomaly.
        double xm = (panelPos.X - _plotOrigin.X) * _metersPerPixel;
        double ym = -(panelPos.Y - _plotOrigin.Y) * _metersPerPixel;   // screen Y is down
        double nu = System.Math.Atan2(ym, xm);
        Planner.CreateNodeAt(nu);
        _autopilot.Disarm();
        QueueRedraw();
    }

    // ── Per-frame ─────────────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        if (!Visible) return;
        QueueRedraw();   // orbit evolves with the vessel; redraw live
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), BgCol);
        DrawRect(new Rect2(Vector2.Zero, Size), BorderCol, false, 1.5f);

        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (vessel == null || universe == null)
        {
            DrawText("NO VESSEL", new Vector2(16, 28), TextDim, 16);
            return;
        }

        var refBody = universe.GetDominantBody(vessel.Position);
        var relPos  = vessel.Position - refBody.Position;
        var relVel  = vessel.Velocity - refBody.Velocity;
        Planner.SetOrbit(relPos, relVel, refBody.GM);

        DrawText($"MAP · {refBody.Name.ToUpperInvariant()}", new Vector2(14, 24), Accentify(refBody.Id), 15);

        if (!Planner.HasOrbit)
        {
            DrawText("SUB-ORBITAL", new Vector2(14, 46), TextDim, 13);
            DrawBodyOnly(refBody, relPos);
            DrawTransferPanel();
            DrawFooter();
            return;
        }

        // ── Collect all points to fit (current orbit, projection, body) ──────
        var master = (Planner.PeriapsisDir, Planner.AheadDir);
        var curve  = SampleConic(Planner.Eccentricity, Planner.SemiLatusRectum,
                                 Planner.PeriapsisDir, Planner.AheadDir, master,
                                 Planner.TrueAnomalyLimit());

        List<Vector2>? projCurve = null;
        if (Planner.HasNode && Planner.DeltaVMagnitude > 0.01)
        {
            var (pPos, pVel) = Planner.PostBurnState();
            var tmp = new ManeuverPlanner();
            tmp.SetOrbit(pPos, pVel, refBody.GM);
            if (tmp.HasOrbit)
                projCurve = SampleConic(tmp.Eccentricity, tmp.SemiLatusRectum,
                                        tmp.PeriapsisDir, tmp.AheadDir, master,
                                        tmp.TrueAnomalyLimit());
        }

        ComputeScale(curve, projCurve);

        // ── Reference body disc + SOI ring ───────────────────────────────────
        float bodyPx = System.Math.Max(4f, (float)(refBody.Radius / _metersPerPixel));
        DrawCircle(_plotOrigin, bodyPx, BodyCol);
        if (refBody.SphereOfInfluence < 1e12)
        {
            float soiPx = (float)(refBody.SphereOfInfluence / _metersPerPixel);
            if (soiPx < PanelSize) DrawArc(_plotOrigin, soiPx, 0, Mathf.Tau, 64, new Color(BodyCol, 0.18f), 1f);
        }

        // ── Orbit ────────────────────────────────────────────────────────────
        DrawPolylineFrom(curve, OrbitCol, 2f);
        if (projCurve != null) DrawDashed(projCurve, ProjCol, 1.6f);

        // Apo / Peri markers (closed orbits only)
        if (Planner.Eccentricity < 1.0)
        {
            DrawMarker(ToPx(Planner.PositionAt(0.0), master), PeCol, "Pe");
            DrawMarker(ToPx(Planner.PositionAt(System.Math.PI), master), ApCol, "Ap");
        }

        // ── Vessel marker ────────────────────────────────────────────────────
        Vector2 vPx = ToPx(Planner.PositionAt(Planner.TrueAnomalyNow), master);
        DrawVesselTriangle(vPx, VesselCol);

        // ── Maneuver node ────────────────────────────────────────────────────
        if (Planner.HasNode)
        {
            Vector2 nPx = ToPx(Planner.PositionAt(Planner.NodeTrueAnomaly), master);
            DrawNodeDiamond(nPx, NodeCol);
        }

        DrawReadout(vessel, refBody);
        DrawTransferPanel();
        DrawFooter();
    }

    // ── Drawing helpers ───────────────────────────────────────────────────────

    private void DrawBodyOnly(CelestialBody body, Vector3d relPos)
    {
        double rMax = System.Math.Max(relPos.Magnitude, body.Radius) * 1.25;
        _metersPerPixel = rMax / (PanelSize * 0.5 - Margin);
        _plotOrigin = _center;
        float bodyPx = System.Math.Max(6f, (float)(body.Radius / _metersPerPixel));
        DrawCircle(_plotOrigin, bodyPx, BodyCol);
        Vector2 vPx = _plotOrigin + new Vector2((float)(relPos.X / _metersPerPixel),
                                                -(float)(relPos.Y / _metersPerPixel));
        DrawVesselTriangle(vPx, VesselCol);
    }

    private void ComputeScale(List<Vector2> a, List<Vector2>? b)
    {
        float minX = 0, maxX = 0, minY = 0, maxY = 0;   // include body at (0,0)
        void Acc(List<Vector2> pts)
        {
            foreach (var p in pts)
            {
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
            }
        }
        Acc(a); if (b != null) Acc(b);

        float spanX = System.Math.Max(maxX - minX, 1f);
        float spanY = System.Math.Max(maxY - minY, 1f);
        float usable = PanelSize - 2f * Margin;
        double scale = System.Math.Max(spanX, spanY) / usable;   // metres per pixel
        if (scale <= 0 || double.IsNaN(scale)) scale = 1.0;
        _metersPerPixel = scale;

        // Centre the bounding box (in metres) within the plot.
        float midXm = (minX + maxX) * 0.5f;
        float midYm = (minY + maxY) * 0.5f;
        _plotOrigin = _center - new Vector2((float)(midXm / scale), -(float)(midYm / scale));
    }

    // Maps a relative 3D position to panel pixels via the master (p̂, q̂) projection.
    private Vector2 ToPx(Vector3d rel, (Vector3d p, Vector3d q) master)
    {
        double xm = rel.Dot(master.p);
        double ym = rel.Dot(master.q);
        return _plotOrigin + new Vector2((float)(xm / _metersPerPixel),
                                         -(float)(ym / _metersPerPixel));
    }

    // Samples a conic (a or p form) and projects onto the master frame, in metres.
    private static List<Vector2> SampleConic(double e, double p,
        Vector3d pHat, Vector3d qHat, (Vector3d p, Vector3d q) master, double nuLimit)
    {
        const int N = 220;
        var pts = new List<Vector2>(N + 1);
        double start = e < 1.0 ? 0.0 : -nuLimit;
        double end   = e < 1.0 ? 2.0 * System.Math.PI : nuLimit;
        for (int i = 0; i <= N; i++)
        {
            double nu = start + (end - start) * i / N;
            double denom = 1.0 + e * System.Math.Cos(nu);
            if (System.Math.Abs(denom) < 1e-6) continue;
            double rr = p / denom;
            if (rr <= 0) continue;
            Vector3d rel = pHat * (rr * System.Math.Cos(nu)) + qHat * (rr * System.Math.Sin(nu));
            pts.Add(new Vector2((float)rel.Dot(master.p), (float)rel.Dot(master.q)));
        }
        return pts;
    }

    private void DrawPolylineFrom(List<Vector2> metresPts, Color col, float width)
    {
        if (metresPts.Count < 2) return;
        var px = new Vector2[metresPts.Count];
        for (int i = 0; i < metresPts.Count; i++)
            px[i] = _plotOrigin + new Vector2(metresPts[i].X / (float)_metersPerPixel,
                                              -metresPts[i].Y / (float)_metersPerPixel);
        DrawPolyline(px, col, width, true);
    }

    private void DrawDashed(List<Vector2> metresPts, Color col, float width)
    {
        for (int i = 0; i < metresPts.Count - 1; i += 2)
        {
            Vector2 a = _plotOrigin + new Vector2(metresPts[i].X / (float)_metersPerPixel,
                                                  -metresPts[i].Y / (float)_metersPerPixel);
            Vector2 b = _plotOrigin + new Vector2(metresPts[i + 1].X / (float)_metersPerPixel,
                                                  -metresPts[i + 1].Y / (float)_metersPerPixel);
            DrawLine(a, b, col, width, true);
        }
    }

    private void DrawMarker(Vector2 px, Color col, string tag)
    {
        DrawCircle(px, 3.5f, col);
        DrawText(tag, px + new Vector2(5, -4), col, 11);
    }

    private void DrawVesselTriangle(Vector2 c, Color col)
    {
        var pts = new[]
        {
            c + new Vector2(0, -6), c + new Vector2(-5, 5), c + new Vector2(5, 5),
        };
        DrawColoredPolygon(pts, col);
    }

    private void DrawNodeDiamond(Vector2 c, Color col)
    {
        var pts = new[]
        {
            c + new Vector2(0, -7), c + new Vector2(7, 0),
            c + new Vector2(0, 7),  c + new Vector2(-7, 0),
        };
        DrawColoredPolygon(pts, new Color(col, 0.85f));
        DrawPolyline(new[] { pts[0], pts[1], pts[2], pts[3], pts[0] }, new Color(0, 0, 0, 0.6f), 1f);
    }

    private void DrawReadout(Vessel vessel, CelestialBody refBody)
    {
        float x = 14, y = PanelSize - 96;
        if (Planner.Eccentricity < 1.0)
        {
            double ap = Planner.SemiMajorAxis * (1 + Planner.Eccentricity) - refBody.Radius;
            double pe = Planner.SemiMajorAxis * (1 - Planner.Eccentricity) - refBody.Radius;
            DrawText($"Ap {Fmt(ap)}   Pe {Fmt(pe)}", new Vector2(x, y), TextBright, 13);
        }
        else
        {
            DrawText("HYPERBOLIC ESCAPE", new Vector2(x, y), ProjCol, 13);
        }
        y += 18;

        if (Planner.HasNode)
        {
            double thrustVac = 0;
            foreach (var en in vessel.Parts.ActiveEngines) thrustVac += en.Definition.ThrustVac;
            double burn = Planner.EstimateBurnTime(thrustVac, vessel.TotalMass);
            DrawText($"ΔV {Planner.DeltaVMagnitude:F0} m/s  (pro {Planner.DvPrograde:+0;-0}  rad {Planner.DvRadial:+0;-0})",
                     new Vector2(x, y), NodeCol, 13);
            y += 17;
            string burnStr = thrustVac > 0 ? $"{burn:F1} s" : "no engines";
            string armed = _autopilot.IsArmed ? "  [ARMED]" : "";
            DrawText($"burn {burnStr}{armed}", new Vector2(x, y), _autopilot.IsArmed ? GreenCol : TextDim, 13);
        }
        else
        {
            DrawText("click orbit to add maneuver node", new Vector2(x, y), TextDim, 12);
        }
    }

    // ── Transfer panel (top-right corner) ────────────────────────────────────

    private void DrawTransferPanel()
    {
        // Draw compact planet-selector list in the top-right of the panel
        float x = PanelSize - 110f;
        float y = 14f;
        DrawText("TRANSFER", new Vector2(x, y), TextDim, 10);
        y += 14f;

        foreach (var (id, label, tKey) in TransferTargets)
        {
            bool selected = id == _selectedTarget;
            Color col = selected ? Accentify(id) : TextDim;
            string prefix = selected ? ">" : " ";
            int keyNum = (int)(tKey - Key.Key1) + 1;
            DrawText($"{prefix}[{keyNum}] {label}", new Vector2(x, y), col, 11);
            y += 14f;
        }

        // Show current node details if one is planned
        var node = TransferPlanner.Instance?.CurrentNode;
        if (node != null)
        {
            y += 4f;
            Color tCol = Accentify(node.TargetBodyId ?? "");
            DrawText($"Δv1 {node.DvMagnitude * node.DvAdjustFactor:F0} m/s", new Vector2(x, y), tCol, 11);
            y += 13f;
            DrawText($"Δv2 {node.SecondBurnDv:F0} m/s", new Vector2(x, y), TextDim, 11);
            y += 13f;
            DrawText($"ToF {node.TimeOfFlight / 86400.0:F1} d", new Vector2(x, y), TextDim, 11);
            y += 13f;
            string adjPct = $"{node.DvAdjustFactor * 100.0:F0}%";
            DrawText($"adj {adjPct}", new Vector2(x, y), TextDim, 11);
            y += 13f;

            // Executor status
            if (ManeuverExecutor.Instance is { IsExecuting: true } exec)
            {
                DrawText($"BURN {exec.RemainingDv:F0} m/s", new Vector2(x, y),
                         new Color(1f, 0.4f, 0.4f, 1f), 11);
            }
            else
            {
                DrawText("⏎ EXECUTE", new Vector2(x, y), GreenCol, 11);
            }
        }
    }

    private void DrawFooter()
    {
        DrawText("wheel: ΔV  ⇧×10  alt: radial  ⏎ execute  ⌫ clear  1-5: transfer  [/]: adj ΔV",
                 new Vector2(14, PanelSize - 14), new Color(0.50f, 0.58f, 0.68f, 0.9f), 10);
    }

    private static readonly Color GreenCol = new(0.35f, 1.0f, 0.5f, 1f);

    private void DrawText(string text, Vector2 pos, Color col, int size) =>
        DrawString(_font, pos, text, HorizontalAlignment.Left, -1, size, col);

    private static Color Accentify(string id) => id switch
    {
        "earth"   => new Color(0.45f, 0.80f, 1.00f),
        "moon"    => new Color(0.80f, 0.82f, 0.86f),
        "mars"    => new Color(1.00f, 0.55f, 0.35f),
        "sun"     => new Color(1.00f, 0.85f, 0.35f),
        "venus"   => new Color(0.95f, 0.85f, 0.50f),
        "jupiter" => new Color(0.90f, 0.72f, 0.50f),
        "mercury" => new Color(0.70f, 0.68f, 0.66f),
        "saturn"  => new Color(0.95f, 0.88f, 0.60f),
        _         => TextBright,
    };

    private static string Fmt(double m)
    {
        if (System.Math.Abs(m) >= 1e6) return $"{m / 1e6:F2} Mm";
        if (System.Math.Abs(m) >= 1e3) return $"{m / 1e3:F0} km";
        return $"{m:F0} m";
    }
}
