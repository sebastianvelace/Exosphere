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
    private IReadOnlyList<FlightEngineBoard> _boards = Array.Empty<FlightEngineBoard>();
    private FlightEngineBoard? _leftBoard, _rightBoard;
    private readonly HashSet<string> _enginePartIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EngineHudIndicatorState> _engines = new(StringComparer.Ordinal);
    private readonly List<EngineReadout> _readouts = new();
    private double? _launchEpoch;
    private double _lastTime = double.NaN;
    private double _engineTimer;
    private string _vehicleLabel = "FLIGHT OPERATIONS";
    private bool _starship;
    private (string Label, string Event)[] _milestones = Milestones;
    private static readonly Color Dim = new(0.46f, 0.47f, 0.49f);
    private static readonly Color Readout = new(0.78f, 0.79f, 0.81f);
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
        if (_snapshot == null || rewound || _snapshot.VesselId != snapshot.VesselId)
        {
            _events.Clear();
            _launchEpoch = null;
            _enginePartIds.Clear();
            _engines.Clear();
            _boards = Array.Empty<FlightEngineBoard>();
            _milestones = Milestones;
            _leftBoard = _rightBoard = null;
            _vehicleLabel = snapshot.VesselName.ToUpperInvariant();
            var vessel = bridge.ActiveVessel;
            _starship = vessel?.Parts.Parts.Any(p => p.Definition.IsStarshipFamily) == true;
            if (vessel != null)
            {
                _boards = FlightEngineBoards.Build(vessel.Parts);
                foreach (var board in _boards)
                    foreach (var engine in board.Engines) _enginePartIds.Add(engine.PartId);
                bool suborbital = bridge.ActiveFlightProfileId.Contains("suborbital", StringComparison.Ordinal)
                    || vessel.Parts.Parts.Any(p => p.Definition.EngineModelId.StartsWith("redstone-", StringComparison.Ordinal));
                bool separation = vessel.Parts.Parts.Any(p => p.Definition.Category == PartCategory.Decoupler);
                _milestones = Milestones.Where(m => separation || m.Event != "SEPARATION")
                    .Select(m => suborbital && m.Event == "ORBIT" ? ("COAST", "COAST") : m).ToArray();
            }
        }
        if (bridge.Flight14Preview is { } preview)
        {
            _vehicleLabel = preview.IsObservingBooster ? "SUPER HEAVY / FLIGHT 14 EXPLORATION" : "STARSHIP FLIGHT 14 / EXPLORATION";
            if (double.IsFinite(preview.Run.Controller.LiftoffEpoch))
            {
                _launchEpoch = preview.Run.Controller.LiftoffEpoch;
                _events.Add("LIFTOFF");
            }
            if (preview.Run.Controller.DetachedBooster != null) _events.Add("SEPARATION");
            if (double.IsFinite(preview.Run.Controller.OrbitElapsedSeconds)) _events.Add("ORBIT");
            _milestones = new[]
            {
                ("LIFTOFF", "LIFTOFF"), ("STAGE SEP", "SEPARATION"), ("ORBIT", "ORBIT"),
                ("DEPLOY", "PAYLOAD"), ("ENTRY", "ENTRY"), ("FLIP", "RETRO_BURN"), ("WATER", "WATER_ENTRY"),
            };
            if (preview.Run.PoweredReturnController?.Landing?.WaterEntryWitness != null) _events.Add("WATER_ENTRY");
            if (preview.Run.PayloadController.Releases.Count > 0) _events.Add("PAYLOAD");
            if (preview.IsObservingBooster && preview.Run.BoosterReturnController is { } booster)
            {
                _milestones = new[] { ("LIFTOFF", "LIFTOFF"), ("STAGE SEP", "SEPARATION"),
                    ("BOOSTBACK", "BOOSTER_BOOSTBACK"), ("COAST", "BOOSTER_COAST"),
                    ("LANDING", "BOOSTER_LANDING"), ("WATER", "BOOSTER_WATER") };
                foreach (var witness in booster.Events)
                    _events.Add(witness.Phase switch
                    {
                        Exosphere.Simulation.Flight.Flight14BoosterReturnPhase.Boostback => "BOOSTER_BOOSTBACK",
                        Exosphere.Simulation.Flight.Flight14BoosterReturnPhase.Coast => "BOOSTER_COAST",
                        Exosphere.Simulation.Flight.Flight14BoosterReturnPhase.Landing => "BOOSTER_LANDING",
                        Exosphere.Simulation.Flight.Flight14BoosterReturnPhase.WaterEntry => "BOOSTER_WATER",
                        _ => "SEPARATION",
                    });
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
            if (bridge.ActiveVessel is { } active)
            {
                if (_starship)
                {
                    _leftBoard = _boards.FirstOrDefault(b => b.Label == "SUPER HEAVY");
                    _rightBoard = _boards.FirstOrDefault(b => b.Label == "STARSHIP");
                }
                else (_leftBoard, _rightBoard) = FlightEngineBoards.Select(_boards, active.Parts);
            }
            // Stable engine IDs follow the detached booster as well as the ship.
            // Never substitute commanded throttle for delivered chamber pressure.
            foreach (var vessel in bridge.Universe.Vessels)
            {
                if (!vessel.Parts.Parts.Any(p => _enginePartIds.Contains(p.InstanceId))) continue;
                foreach (var part in vessel.Parts.Parts.Where(p => _enginePartIds.Contains(p.InstanceId)))
                {
                    if (part.HasEngineRuntime)
                        foreach (var state in part.EngineStates)
                            _engines[state.InstanceId] = part.IsBroken || vessel.IsDestroyed
                                || state.FailureCode != null || state.State == Exosphere.Simulation.Propulsion.EngineLifecycleState.Failed
                                ? EngineHudIndicatorState.Failed : EngineHudIndicatorState.Off;
                    else if (part.IsBroken || vessel.IsDestroyed)
                        _engines[part.InstanceId] = EngineHudIndicatorState.Failed;
                }
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
        DrawEngineBoard(new Vector2(83, 90), _leftBoard);
        DrawTimeline(330, width - 330);
        double seconds = _launchEpoch is { } epoch ? System.Math.Max(0, s.MissionTimeS - epoch) : s.MissionTimeS;
        string prefix = _launchEpoch.HasValue ? "T+" : "SIM";
        string clock = $"{prefix} {Clock(seconds)}";
        if (_mission?.IsCountingDown == true) clock = $"T− {Clock(_mission.CountdownTimer)}";
        else if (s.MissionPhase == "PRE_LAUNCH") clock = "T− HOLD";
        Text(clock, new Vector2(width * 0.5f, 95), 36, Colors.White, mono: true);
        Text(_vehicleLabel, new Vector2(width * 0.5f, 117), 13, Dim, maxWidth: width - 680);
        string orbit = s.IsImpactTrajectory ? "PE IMPACT" : $"PE {Distance(s.PeriapsisAltitudeM)}";
        Text($"ALT {Distance(s.AltitudeM)}   ·   VERT {s.VerticalSpeedMps:+0;−0;0} M/S   ·   THR {s.Throttle * 100:0}%   ·   TWR {s.ThrustToWeightRatio:0.00}",
            new Vector2(width * 0.5f, 147), 12, Readout, mono: true);
        Text($"AP {Distance(s.ApoapsisAltitudeM)}   ·   {orbit}", new Vector2(width * 0.5f, 165), 10, Dim, mono: true);
        DrawGauge(new Vector2(width - 222, 90), "SURFACE SPEED", $"{s.SurfaceSpeedMps * 3.6:0}", "KM/H",
            (float)System.Math.Clamp(s.SurfaceSpeedMps / 8000.0, 0, 1));
        if (_rightBoard != null) DrawEngineBoard(new Vector2(width - 83, 90), _rightBoard);
        else DrawGauge(new Vector2(width - 83, 90), "ALTITUDE", $"{s.AltitudeM / 1000:0.0}", "KM",
            (float)System.Math.Clamp(s.AltitudeM / 200000.0, 0, 1));
    }

    private void DrawTimeline(float left, float right)
    {
        if (right <= left) return;
        const int segments = 80;
        Vector2 Point(float t) => new(Mathf.Lerp(left, right, t), 43 + 26 * Mathf.Pow(2 * t - 1, 2));
        int lastReached = -1;
        for (int i = 0; i < _milestones.Length; i++)
            if (_events.Contains(_milestones[i].Event)) lastReached = i;
        float reachedT = lastReached < 0 ? -1 : (lastReached + 0.5f) / _milestones.Length;
        for (int i = 0; i < segments; i++)
            DrawLine(Point(i / (float)segments), Point((i + 1f) / segments),
                i / (float)segments <= reachedT ? Colors.White : Track, 1.6f, true);
        for (int i = 0; i < _milestones.Length; i++)
        {
            var milestone = _milestones[i];
            var point = Point((i + 0.5f) / _milestones.Length);
            bool reached = _events.Contains(milestone.Event);
            DrawCircle(point, 4.5f, new Color(0.01f, 0.01f, 0.01f));
            DrawArc(point, 4.5f, 0, Mathf.Tau, 20, reached ? Colors.White : Track, 1.5f, true);
            if (reached) DrawCircle(point, 1.8f, Colors.White);
            Text(milestone.Label, point + new Vector2(0, i % 2 == 0 ? -11 : 19), 11, reached ? Colors.White : Dim);
        }
    }

    private void DrawEngineBoard(Vector2 centre, FlightEngineBoard? board)
    {
        DrawArc(centre, 49, 0, Mathf.Tau, 72, Track, 1.7f, true);
        var dots = board?.Engines ?? Array.Empty<FlightEngineDot>();
        int on = dots.Count(dot => _engines.GetValueOrDefault(dot.Id) == EngineHudIndicatorState.Running);
        DrawArc(centre, 55, Mathf.DegToRad(150), Mathf.DegToRad(390), 72, Dim, 2.8f, true);
        if (dots.Count > 0 && on > 0)
            DrawArc(centre, 55, Mathf.DegToRad(150), Mathf.DegToRad(150 + 240f * on / dots.Count), 72, Colors.White, 2.8f, true);
        double extent = dots.Count == 0 ? 0 : dots.Max(d => System.Math.Sqrt(d.X * d.X + d.Z * d.Z));
        double nozzleMax = dots.Count == 0 ? 1 : dots.Max(d => d.NozzleRadius);
        float maxRadius = dots.Count > 16 ? 4 : dots.Count > 6 ? 7 : 11;
        foreach (var dot in dots)
        {
            var point = extent > 1e-9
                ? new Vector2((float)(dot.X / extent), (float)(dot.Z / extent)) * 36 : Vector2.Zero;
            float radius = Mathf.Clamp((float)(maxRadius * dot.NozzleRadius / nozzleMax), 3.5f, maxRadius);
            DrawEngine(centre + point, dot.Id, radius);
        }
        if (dots.Count == 0) Text("—", centre + new Vector2(0, 8), 24, Dim);
        Text(board?.Label ?? "NO ENGINES", centre + new Vector2(0, 67), 10, Dim);
        if (dots.Count > 0)
            Text($"{on}/{dots.Count} {(dots.Any(d => d.IsAggregate) ? "GROUPS ON" : "RUNNING")}", centre + new Vector2(0, 81), 9, Dim, mono: true);
    }

    /// <summary>Read-only instrument identities for real-scene validation and diagnostics.</summary>
    public Godot.Collections.Dictionary GetInstrumentState() => new()
    {
        ["vessel_id"] = _snapshot?.VesselId ?? "",
        ["left_label"] = _leftBoard?.Label ?? "NO ENGINES",
        ["right_label"] = _rightBoard?.Label ?? "ALTITUDE",
        ["left_ids"] = (_leftBoard?.Engines.Select(e => e.Id) ?? Array.Empty<string>()).ToArray(),
        ["right_ids"] = (_rightBoard?.Engines.Select(e => e.Id) ?? Array.Empty<string>()).ToArray(),
        ["stage_count"] = _boards.Count,
        ["commanded_throttle"] = _snapshot?.Throttle ?? 0,
    };

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
