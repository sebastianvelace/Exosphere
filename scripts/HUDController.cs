namespace Exosphere.Game;

using Godot;
using System.Linq;

public partial class HUDController : Node
{
    // Nodos de la UI (asignados en el editor o buscados por nombre)
    [Export] public NodePath AltitudeLabelPath  { get; set; } = "";
    [Export] public NodePath SpeedLabelPath     { get; set; } = "";
    [Export] public NodePath ApoapsisLabelPath  { get; set; } = "";
    [Export] public NodePath PeriapsisLabelPath { get; set; } = "";
    [Export] public NodePath ThrottleLabelPath  { get; set; } = "";
    [Export] public NodePath FuelLabelPath      { get; set; } = "";
    [Export] public NodePath TimeLabelPath      { get; set; } = "";
    [Export] public NodePath WarpLabelPath      { get; set; } = "";
    [Export] public NodePath MassLabelPath      { get; set; } = "";

    private Label? _altLabel, _speedLabel, _apLabel, _peLabel,
                   _throttleLabel, _fuelLabel, _timeLabel, _warpLabel, _massLabel;

    // Dynamically created labels
    private Label? _phaseLabel;
    private Label? _twrLabel;
    private Label? _countdownLabel;

    public override void _Ready()
    {
        _altLabel      = GetNodeOrNull<Label>(AltitudeLabelPath);
        _speedLabel    = GetNodeOrNull<Label>(SpeedLabelPath);
        _apLabel       = GetNodeOrNull<Label>(ApoapsisLabelPath);
        _peLabel       = GetNodeOrNull<Label>(PeriapsisLabelPath);
        _throttleLabel = GetNodeOrNull<Label>(ThrottleLabelPath);
        _fuelLabel     = GetNodeOrNull<Label>(FuelLabelPath);
        _timeLabel     = GetNodeOrNull<Label>(TimeLabelPath);
        _warpLabel     = GetNodeOrNull<Label>(WarpLabelPath);
        _massLabel     = GetNodeOrNull<Label>(MassLabelPath);

        // Mission phase label (top-centre)
        _phaseLabel = MakeLabel(text: "PRE-LAUNCH", x: 760, y: 16, size: 16);
        _phaseLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _phaseLabel.CustomMinimumSize = new Vector2(400, 30);
        _phaseLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.8f, 0.2f));
        AddChild(_phaseLabel);

        // TWR label (right column)
        _twrLabel = MakeLabel(text: "TWR: ---", x: 1550, y: 16, size: 14);
        AddChild(_twrLabel);

        // Countdown label (big, centre) — hidden until countdown starts
        _countdownLabel = MakeLabel(text: "", x: 860, y: 460, size: 48);
        _countdownLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _countdownLabel.CustomMinimumSize = new Vector2(200, 60);
        _countdownLabel.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.1f));
        _countdownLabel.Visible = false;
        AddChild(_countdownLabel);

        // Controls hint
        var hint = new Label();
        hint.Text = "[Z/X] throttle  [W/S] pitch  [A/D] yaw  [Q/E] roll  [T] SAS  [,/.] warp  [Space] stage  [L] launch  [C] camera";
        hint.Position = new Vector2(10, 940);
        hint.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.55f));
        hint.AddThemeFontSizeOverride("font_size", 13);
        AddChild(hint);
    }

    public override void _Process(double delta)
    {
        var bridge   = SimulationBridge.Instance;
        var vessel   = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        var mission  = MissionManager.Instance;
        if (vessel == null || universe == null) return;

        // ── Rotation controls ──────────────────────────────────────────────
        double pitch = 0, yaw = 0, roll = 0;
        if (Input.IsKeyPressed(Key.W)) pitch += 1.0;
        if (Input.IsKeyPressed(Key.S)) pitch -= 1.0;
        if (Input.IsKeyPressed(Key.A)) yaw   -= 1.0;
        if (Input.IsKeyPressed(Key.D)) yaw   += 1.0;
        if (Input.IsKeyPressed(Key.Q)) roll  -= 1.0;
        if (Input.IsKeyPressed(Key.E)) roll  += 1.0;
        vessel.PitchYawRoll = new Exosphere.Simulation.Math.Vector3d(pitch, yaw, roll);

        var refBody = universe.GetDominantBody(vessel.Position);
        double alt   = vessel.GetAltitude(refBody);
        double speed = (vessel.Velocity - refBody.Velocity).Magnitude;

        // ── Standard HUD labels ────────────────────────────────────────────
        SetLabel(_altLabel, FormatDistance(alt));
        SetLabel(_speedLabel, $"{speed:F1} m/s");

        try
        {
            var relPos = vessel.Position - refBody.Position;
            var relVel = vessel.Velocity - refBody.Velocity;
            var el = Exosphere.Simulation.OrbitalElements.FromStateVector(
                relPos, relVel, refBody.GM, refBody.Id, universe.CurrentTime);
            SetLabel(_apLabel, FormatDistance(el.Apoapsis  - refBody.Radius));
            SetLabel(_peLabel, FormatDistance(el.Periapsis - refBody.Radius));
        }
        catch
        {
            SetLabel(_apLabel, "---");
            SetLabel(_peLabel, "---");
        }

        bool enginesActive = vessel.Parts.ActiveEngines.Any();
        string engStatus   = vessel.Throttle > 0.01
            ? (enginesActive ? "FIRING" : "FLAME-OUT")
            : "OFF";
        SetLabel(_throttleLabel, $"THR {vessel.Throttle * 100:F0}%  [{engStatus}]");
        if (_throttleLabel != null)
            _throttleLabel.Modulate = vessel.Throttle > 0.01 && enginesActive
                ? new Color(1f, 0.6f, 0.1f) : new Color(1f, 1f, 1f);

        double fuel = vessel.Parts.TotalLiquidFuel + vessel.Parts.TotalOxidizer;
        SetLabel(_fuelLabel, $"{fuel / 1000.0:F1} t");
        SetLabel(_massLabel, $"{vessel.TotalMass / 1000.0:F1} t");
        SetLabel(_timeLabel, FormatMissionTime(universe.CurrentTime));

        if (_warpLabel != null)
        {
            double ts = universe.TimeScale;
            _warpLabel.Text     = ts <= 1.0 ? "Real Time" : $"× {(int)ts}";
            _warpLabel.Modulate = ts > 1.0 ? new Color(0.4f, 1f, 1f) : new Color(1f, 1f, 1f);
        }

        // ── TWR label ──────────────────────────────────────────────────────
        if (_twrLabel != null && vessel.TotalMass > 0)
        {
            double surfG = refBody.GetSurfaceGravity();
            double thrust = 0;
            foreach (var e in vessel.Parts.ActiveEngines)
                thrust += e.Definition.ThrustVac * e.ThrottleLevel;
            double twr = thrust / (vessel.TotalMass * surfG);
            _twrLabel.Text     = vessel.Throttle > 0.01 ? $"TWR  {twr:F2}" : "TWR  ---";
            _twrLabel.Modulate = twr >= 1.0 ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.4f, 0.3f);
        }

        // ── Mission phase label ────────────────────────────────────────────
        if (_phaseLabel != null && mission != null)
        {
            _phaseLabel.Text = mission.Phase.ToString().Replace("_", " ");
        }

        // ── Countdown display ──────────────────────────────────────────────
        if (_countdownLabel != null && mission != null)
        {
            bool show = mission.Phase is MissionPhase.COUNTDOWN or MissionPhase.IGNITION;
            _countdownLabel.Visible = show;
            if (show)
            {
                int secs = (int)System.Math.Ceiling(mission.CountdownTimer);
                _countdownLabel.Text = secs > 0 ? $"T-{secs:D2}" : "LIFTOFF";
            }
        }
    }

    // ── Keyboard input ────────────────────────────────────────────────────
    public override void _UnhandledInput(InputEvent @event)
    {
        var bridge = SimulationBridge.Instance;
        if (bridge == null) return;

        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            switch (key.Keycode)
            {
                case Key.Z:
                    bridge.SetThrottle(System.Math.Min(1.0, (bridge.ActiveVessel?.Throttle ?? 0) + 0.05));
                    break;
                case Key.X:
                    bridge.SetThrottle(System.Math.Max(0.0, (bridge.ActiveVessel?.Throttle ?? 0) - 0.05));
                    break;
                case Key.Space:
                    bridge.TriggerStaging();
                    break;
                case Key.T:
                    bridge.SetSAS(!(bridge.ActiveVessel?.SASEnabled ?? true));
                    break;
                case Key.L:
                    MissionManager.Instance?.StartCountdown();
                    break;
                case Key.Period:
                    bridge.SetTimeScale(System.Math.Min(1000.0, bridge.Universe.TimeScale * 2.0));
                    break;
                case Key.Comma:
                    bridge.SetTimeScale(System.Math.Max(1.0, bridge.Universe.TimeScale / 2.0));
                    break;
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static Label MakeLabel(string text, int x, int y, int size)
    {
        var lbl = new Label { Text = text };
        lbl.Position = new Vector2(x, y);
        lbl.AddThemeFontSizeOverride("font_size", size);
        return lbl;
    }

    private static void SetLabel(Label? label, string text) { if (label != null) label.Text = text; }

    private static string FormatDistance(double meters)
    {
        if (System.Math.Abs(meters) >= 1e9)  return $"{meters / 1e9:F3} Gm";
        if (System.Math.Abs(meters) >= 1e6)  return $"{meters / 1e6:F3} Mm";
        if (System.Math.Abs(meters) >= 1000) return $"{meters / 1000.0:F1} km";
        return $"{meters:F0} m";
    }

    private static string FormatMissionTime(double seconds)
    {
        if (seconds < 0) return "T-00:00:00";
        int d = (int)(seconds / 86400);
        int h = (int)(seconds % 86400 / 3600);
        int m = (int)(seconds % 3600 / 60);
        int s = (int)(seconds % 60);
        return d > 0 ? $"T+{d}d {h:D2}:{m:D2}:{s:D2}" : $"T+{h:D2}:{m:D2}:{s:D2}";
    }
}
