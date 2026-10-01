namespace Exosphere.Game;

using Godot;
using System.Linq;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Presentation;

/// <summary>Broadcast-style instruments backed by live telemetry and observed events.</summary>
public partial class FlightBroadcastHUD : Control
{
    public const float DesignHeight = 182f;
    private FlightHudSnapshot? _snapshot;
    private MissionManager? _mission;
    private SimulationBridge? _bridge;
    private readonly HashSet<string> _events = new(StringComparer.Ordinal);
    private readonly List<string> _boosterIds = new();
    private readonly List<string> _shipIds = new();
    private readonly HashSet<string> _enginePartIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EngineHudIndicatorState> _engines = new(StringComparer.Ordinal);
    private readonly List<EngineReadout> _readouts = new();
    private readonly List<int> _rings = new();
    private double? _launchEpoch;
    private double _lastTime = double.NaN;
    private double _engineTimer;
    private string _vehicleLabel = "FLIGHT OPERATIONS";
    private bool _starship;
    private static readonly Color Dim = new(0.46f, 0.47f, 0.49f);
    private static readonly Color Track = new(0.22f, 0.23f, 0.24f);
    private static readonly (string Label, string Event)[] Milestones =
    {
        ("LIFTOFF", "LIFTOFF"), ("MAX Q", "MAX_Q"), ("STAGE SEP", "SEPARATION"),
        ("ORBIT", "ORBIT"), ("ENTRY", "ENTRY"), ("LANDING", "LANDED"),
    };

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        GrowVertical = GrowDirection.Begin;
        OffsetTop = -DesignHeight;
        OffsetBottom = 0;
        ZIndex = 20;
    }

    public void UpdateFromSnapshot(FlightHudSnapshot snapshot, double delta)
    {
        var bridge = SimulationBridge.Instance;
        if (bridge == null) return;
        if (_bridge != bridge)
        {
            if (IsInstanceValid(_bridge)) _bridge!.SimulationLoaded -= ResetFlight;
            _bridge = bridge;
            _bridge.SimulationLoaded += ResetFlight;
        }
        if (_mission != MissionManager.Instance)
        {
            if (IsInstanceValid(_mission)) _mission!.PhaseChanged -= ObservePhase;
            _mission = MissionManager.Instance;
            if (_mission != null) _mission.PhaseChanged += ObservePhase;
        }
        bool rewound = double.IsFinite(_lastTime) && snapshot.MissionTimeS < _lastTime - 0.01;
        if (_snapshot == null || rewound)
        {
            _events.Clear();
            _launchEpoch = null;
            _boosterIds.Clear();
            _shipIds.Clear();
            _enginePartIds.Clear();
            _starship = false;
            _vehicleLabel = snapshot.VesselName.ToUpperInvariant();
            var vessel = bridge.ActiveVessel;
            if (vessel != null)
            {
                foreach (var part in vessel.Parts.Parts.Where(p => p.Definition.Category == PartCategory.Engine))
                {
                    _enginePartIds.Add(part.InstanceId);
                    bool booster = part.Definition.IsStarshipFamily && part.Definition.HasVehicleRole("booster");
                    bool ship = part.Definition.IsStarshipFamily && part.Definition.HasVehicleRole("ship_engines");
                    _starship |= booster || ship;
                    var ids = ship ? _shipIds : _boosterIds;
                    if (part.HasEngineRuntime)
                        ids.AddRange(part.EngineStates.Take(part.SelectedEngineCount).Select(e => e.InstanceId));
                    else ids.Add(part.InstanceId);
                }
            }
        }
        _snapshot = snapshot;
        _lastTime = snapshot.MissionTimeS;
        RecordPhase(snapshot.MissionPhase);
        _engineTimer += delta;
        if (_engineTimer >= 0.1 || _engines.Count == 0)
        {
            _engineTimer = 0;
            _engines.Clear();
            // Stable engine IDs follow the detached booster as well as the ship.
            // Never substitute commanded throttle for delivered chamber pressure.
            foreach (var vessel in bridge.Universe.Vessels)
            {
                if (!vessel.Parts.Parts.Any(p => _enginePartIds.Contains(p.InstanceId))) continue;
                vessel.FillEngineReadouts(bridge.Universe.GetDominantBody(vessel.Position), _readouts);
                foreach (var row in _readouts)
                    _engines[row.InstanceId] = vessel.IsDestroyed
                        ? EngineHudIndicatorState.Failed : EngineHudPresentation.Classify(row);
            }
        }
        QueueRedraw();
    }

    private void ObservePhase(string phase)
    {
        // Reached markers require actual observed events, not an enum rank or
        // the elapsed time in a planned broadcast timeline.
        if (phase == "LIFTOFF" && !_launchEpoch.HasValue)
            _launchEpoch = SimulationBridge.Instance?.Universe.CurrentTime;
        RecordPhase(phase);
    }

    private void RecordPhase(string phase)
    {
        // A loaded current phase is visible evidence, but has no launch epoch.
        if (phase == "CAUGHT") _events.Add("LANDED");
        _events.Add(phase);
    }

    public override void _ExitTree()
    {
        if (IsInstanceValid(_mission)) _mission!.PhaseChanged -= ObservePhase;
        if (IsInstanceValid(_bridge)) _bridge!.SimulationLoaded -= ResetFlight;
    }

    private void ResetFlight() => _snapshot = null;

    public override void _Draw()
    {
        if (_snapshot is not { } s) return;
        if (Size.X <= 0) return;
        float scale = Mathf.Min(Size.X / 1600f, 1f);
        float width = Size.X / scale;
        DrawSetTransform(Vector2.Zero, 0, Vector2.One * scale);
        DrawRect(new Rect2(0, 0, width, DesignHeight), new Color(0.008f, 0.009f, 0.011f, 0.88f));
        DrawLine(new Vector2(28, 1), new Vector2(width - 28, 1), new Color(1, 1, 1, 0.12f), 1);
        DrawEngineBoard(new Vector2(83, 90), _boosterIds, _starship ? "SUPER HEAVY" : "ENGINES");
        DrawTimeline(330, width - 330);
        double seconds = _launchEpoch is { } epoch ? System.Math.Max(0, s.MissionTimeS - epoch) : s.MissionTimeS;
        string prefix = _launchEpoch.HasValue ? "T+" : "SIM";
        string clock = $"{prefix} {Clock(seconds)}";
        if (_mission?.IsCountingDown == true) clock = $"T− {Clock(_mission.CountdownTimer)}";
        else if (s.MissionPhase == "PRE_LAUNCH") clock = "T− HOLD";
        Text(clock, new Vector2(width * 0.5f, 95), 36, Colors.White, mono: true);
        Text(_vehicleLabel, new Vector2(width * 0.5f, 117), 13, Dim, maxWidth: width - 680);
        string orbit = s.IsImpactTrajectory ? "PE IMPACT" : $"PE {Distance(s.PeriapsisAltitudeM)}";
        Text($"ALT {Distance(s.AltitudeM)}   ·   VERT {s.VerticalSpeedMps:+0;−0;0} M/S   ·   THR {s.Throttle:P0}   ·   TWR {s.ThrustToWeightRatio:0.00}",
            new Vector2(width * 0.5f, 147), 11, Dim, mono: true);
        Text($"AP {Distance(s.ApoapsisAltitudeM)}   ·   {orbit}", new Vector2(width * 0.5f, 165), 10, Dim, mono: true);
        DrawGauge(new Vector2(width - 222, 90), "SURFACE SPEED", $"{s.SurfaceSpeedMps * 3.6:0}", "KM/H",
            (float)System.Math.Clamp(s.SurfaceSpeedMps / 8000.0, 0, 1));
        if (_starship) DrawEngineBoard(new Vector2(width - 83, 90), _shipIds, "STARSHIP");
        else DrawGauge(new Vector2(width - 83, 90), "ALTITUDE", $"{s.AltitudeM / 1000:0.0}", "KM",
            (float)System.Math.Clamp(s.AltitudeM / 200000.0, 0, 1));
    }

    private void DrawTimeline(float left, float right)
    {
        if (right <= left) return;
        const int segments = 80;
        Vector2 Point(float t) => new(Mathf.Lerp(left, right, t), 43 + 26 * Mathf.Pow(2 * t - 1, 2));
        int lastReached = -1;
        for (int i = 0; i < Milestones.Length; i++)
            if (_events.Contains(Milestones[i].Event)) lastReached = i;
        float reachedT = lastReached < 0 ? -1 : (lastReached + 0.5f) / Milestones.Length;
        for (int i = 0; i < segments; i++)
            DrawLine(Point(i / (float)segments), Point((i + 1f) / segments),
                i / (float)segments <= reachedT ? Colors.White : Track, 1.6f, true);
        for (int i = 0; i < Milestones.Length; i++)
        {
            var milestone = Milestones[i];
            var point = Point((i + 0.5f) / Milestones.Length);
            bool reached = _events.Contains(milestone.Event);
            DrawCircle(point, 4.5f, new Color(0.01f, 0.01f, 0.01f));
            DrawArc(point, 4.5f, 0, Mathf.Tau, 20, reached ? Colors.White : Track, 1.5f, true);
            if (reached) DrawCircle(point, 1.8f, Colors.White);
            Text(milestone.Label, point + new Vector2(0, i % 2 == 0 ? -11 : 19), 11, reached ? Colors.White : Dim);
        }
    }

    private void DrawEngineBoard(Vector2 centre, List<string> ids, string label)
    {
        DrawArc(centre, 49, 0, Mathf.Tau, 72, Track, 1.7f, true);
        int on = ids.Count(id => _engines.GetValueOrDefault(id) == EngineHudIndicatorState.Running);
        DrawArc(centre, 55, Mathf.DegToRad(150), Mathf.DegToRad(390), 72, Dim, 2.8f, true);
        if (ids.Count > 0 && on > 0)
            DrawArc(centre, 55, Mathf.DegToRad(150), Mathf.DegToRad(150 + 240f * on / ids.Count), 72, Colors.White, 2.8f, true);
        if (ids.Count == 6)
        {
            for (int i = 0; i < 6; i++)
            {
                bool outer = i >= 3;
                int j = outer ? i - 3 : i;
                float a = Mathf.DegToRad(outer ? 30 + 120 * j : -90 + 120 * j);
                DrawEngine(centre + Vector2.FromAngle(a) * (outer ? 29 : 10), ids[i], outer ? 11 : 4.8f);
            }
        }
        else
        {
            EngineHudPresentation.FillBoardRings(ids.Count, _rings);
            int index = 0;
            for (int ring = 0; ring < _rings.Count; ring++)
            {
                float radius = ids.Count == 33 ? new[] { 37f, 25f, 11f }[ring]
                    : _rings.Count == 1 ? 25 : Mathf.Lerp(37, 8, ring / (float)(_rings.Count - 1));
                for (int i = 0; i < _rings[ring]; i++)
                    DrawEngine(centre + Vector2.FromAngle(-Mathf.Pi / 2 + Mathf.Tau * i / _rings[ring]) * radius,
                        ids[index++], 4);
            }
        }
        Text(label, centre + new Vector2(0, 77), 10, Dim);
    }

    private void DrawEngine(Vector2 point, string id, float radius)
    {
        Color color = _engines.GetValueOrDefault(id) switch
        {
            EngineHudIndicatorState.Running => Colors.White,
            EngineHudIndicatorState.Starting => InterfaceTheme.Warning,
            EngineHudIndicatorState.Failed => InterfaceTheme.Alert,
            _ => Track,
        };
        DrawCircle(point, radius, color);
    }

    private void DrawGauge(Vector2 centre, string caption, string value, string unit, float fraction)
    {
        float start = Mathf.DegToRad(150), end = Mathf.DegToRad(390);
        DrawArc(centre, 55, start, end, 72, Track, 3, true);
        DrawArc(centre, 55, start, Mathf.Lerp(start, end, System.Math.Max(fraction, 0.04f)), 72, Colors.White, 3, true);
        Text(caption, centre + new Vector2(0, -20), 11, Dim);
        Text(value, centre + new Vector2(0, 11), 28, Colors.White, mono: true);
        Text(unit, centre + new Vector2(0, 30), 11, Dim);
    }

    private void Text(string text, Vector2 centre, int size, Color color, bool mono = false, float maxWidth = -1)
    {
        var font = mono ? InterfaceTheme.MonoFont : InterfaceTheme.LabelFont;
        if (maxWidth > 0)
            while (text.Length > 3 && font.GetStringSize(text, fontSize: size).X > maxWidth)
                text = text[..^2] + "…";
        float width = font.GetStringSize(text, fontSize: size).X;
        DrawString(font, centre - new Vector2(width * 0.5f, 0), text, fontSize: size, modulate: color);
    }

    private static string Clock(double seconds)
    {
        int t = (int)System.Math.Max(0, seconds);
        return $"{t / 3600:00}:{t / 60 % 60:00}:{t % 60:00}";
    }
    private static string Distance(double? value) => value is { } v && double.IsFinite(v)
        ? System.Math.Abs(v) >= 1e6 ? $"{v / 1e6:0.0} MM" : $"{v / 1000:0.0} KM" : "—";
}
