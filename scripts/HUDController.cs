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

        // Controls hint label
        var hint = new Label();
        hint.Text = "[Z/X] throttle   [W/S] pitch   [A/D] yaw   [Q/E] roll   [T] SAS   [,/.] warp   [Space] stage";
        hint.Position = new Vector2(10, 900);
        hint.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
        hint.AddThemeFontSizeOverride("font_size", 13);
        AddChild(hint);
    }

    public override void _Process(double delta)
    {
        var bridge   = SimulationBridge.Instance;
        var vessel   = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (vessel == null || universe == null) return;

        // ── Controles de rotación (leídos cada frame como teclas sostenidas) ──
        // W/S = pitch  A/D = yaw  Q/E = roll
        double pitch = 0, yaw = 0, roll = 0;
        if (Input.IsKeyPressed(Key.W)) pitch += 1.0;
        if (Input.IsKeyPressed(Key.S)) pitch -= 1.0;
        if (Input.IsKeyPressed(Key.A)) yaw   -= 1.0;
        if (Input.IsKeyPressed(Key.D)) yaw   += 1.0;
        if (Input.IsKeyPressed(Key.Q)) roll  -= 1.0;
        if (Input.IsKeyPressed(Key.E)) roll  += 1.0;
        vessel.PitchYawRoll = new Exosphere.Simulation.Math.Vector3d(pitch, yaw, roll);

        var refBody  = universe.GetDominantBody(vessel.Position);
        double alt   = vessel.GetAltitude(refBody);
        double speed = (vessel.Velocity - refBody.Velocity).Magnitude;

        // Altitude
        SetLabel(_altLabel, FormatDistance(alt));

        // Speed (orbital)
        SetLabel(_speedLabel, $"{speed:F1} m/s");

        // Apoapsis / Periapsis — compute live from state vector
        try
        {
            var relPos = vessel.Position - refBody.Position;
            var relVel = vessel.Velocity - refBody.Velocity;
            var elements = Exosphere.Simulation.OrbitalElements.FromStateVector(
                relPos, relVel, refBody.GM, refBody.Id, universe.CurrentTime);
            double ap = elements.Apoapsis  - refBody.Radius;
            double pe = elements.Periapsis - refBody.Radius;
            SetLabel(_apLabel, FormatDistance(ap));
            SetLabel(_peLabel, FormatDistance(pe));
        }
        catch
        {
            SetLabel(_apLabel, "---");
            SetLabel(_peLabel, "---");
        }

        // Throttle + engine status
        bool enginesActive = vessel.Parts.ActiveEngines.Any();
        string engStatus   = vessel.Throttle > 0.01
            ? (enginesActive ? "FIRING" : "FLAME-OUT")
            : "OFF";
        SetLabel(_throttleLabel, $"THR {vessel.Throttle * 100.0:F0}%  [{engStatus}]");
        if (_throttleLabel != null)
            _throttleLabel.Modulate = vessel.Throttle > 0.01 && enginesActive
                ? new Color(1.0f, 0.6f, 0.1f)
                : new Color(1.0f, 1.0f, 1.0f);

        // Fuel
        double fuel = vessel.Parts.TotalLiquidFuel + vessel.Parts.TotalOxidizer;
        SetLabel(_fuelLabel, $"{fuel:F0} kg");

        // Mass
        SetLabel(_massLabel, $"{vessel.TotalMass / 1000.0:F2} t");

        // Simulation time (formato misión: días, horas, minutos, segundos)
        SetLabel(_timeLabel, FormatMissionTime(universe.CurrentTime));

        // Time warp — show "Real Time" at x1, otherwise "× N" with no decimals
        if (_warpLabel != null)
        {
            double timeScale = universe.TimeScale;
            _warpLabel.Text = timeScale <= 1.0
                ? "Real Time"
                : $"× {(int)timeScale}";

            _warpLabel.Modulate = timeScale > 1.0
                ? new Color(0.4f, 1.0f, 1.0f)   // cyan when warping
                : new Color(1.0f, 1.0f, 1.0f);   // white at real time
        }
    }

    // ── Controles de teclado básicos ──────────────────────────────────────
    public override void _UnhandledInput(InputEvent @event)
    {
        var bridge = SimulationBridge.Instance;
        if (bridge == null) return;

        if (@event is InputEventKey key && key.Pressed)
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
            }
        }
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
