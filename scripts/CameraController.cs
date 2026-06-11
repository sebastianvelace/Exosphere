namespace Exosphere.Game;

using Godot;

public enum CameraMode { Chase, Pad }

public partial class CameraController : Node3D
{
    public static CameraController? Instance { get; private set; }

    public CameraMode Mode { get; set; } = CameraMode.Pad;

    // ── Chase / orbit state ───────────────────────────────────────────────
    private float _yaw      = 25f;
    private float _pitch    = 12f;
    private float _distance = 80f;   // full stack is ~43 units tall; 80 gives a nice frame

    [Export] public float OrbitSensitivity { get; set; } = 0.3f;
    [Export] public float ZoomSensitivity  { get; set; } = 1.2f;
    [Export] public float MinDistance      { get; set; } = 5f;
    [Export] public float MaxDistance      { get; set; } = 500_000f;

    // ── Pad preset positions [yaw°, pitch°, distance] ─────────────────────
    // Cycle with C key: side view → front view → wide shot
    private static readonly (float yaw, float pitch, float dist)[] PadPresets =
    {
        (  30f, 10f,  90f),   // default: slight side angle
        ( 180f,  5f,  70f),   // other side (shows Mechazilla tower)
        (   0f, 20f, 130f),   // front high
    };
    private int _padPresetIdx = 0;

    private bool _dragging;

    public override void _Ready() => Instance = this;

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right)
                _dragging = mb.Pressed;

            if (mb.ButtonIndex == MouseButton.WheelUp)
                _distance = Mathf.Clamp(_distance / ZoomSensitivity, MinDistance, MaxDistance);
            if (mb.ButtonIndex == MouseButton.WheelDown)
                _distance = Mathf.Clamp(_distance * ZoomSensitivity, MinDistance, MaxDistance);
        }

        if (@event is InputEventMouseMotion mm && _dragging)
        {
            _yaw   -= mm.Relative.X * OrbitSensitivity;
            _pitch -= mm.Relative.Y * OrbitSensitivity;
            _pitch  = Mathf.Clamp(_pitch, -89f, 89f);
        }

        // C key: cycle pad camera presets
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.C)
        {
            _padPresetIdx = (_padPresetIdx + 1) % PadPresets.Length;
            var preset = PadPresets[_padPresetIdx];
            _yaw      = preset.yaw;
            _pitch    = preset.pitch;
            _distance = preset.dist;
        }
    }

    public override void _Process(double delta)
    {
        // Auto-switch to Chase mode once vessel is clear of the pad
        var bridge = SimulationBridge.Instance;
        if (bridge?.ActiveVessel != null)
        {
            var earth = bridge.Universe.GetBody("earth");
            if (earth != null)
            {
                double alt = bridge.ActiveVessel.GetAltitude(earth);
                if (Mode == CameraMode.Pad && alt > 500)
                    Mode = CameraMode.Chase;
                if (Mode == CameraMode.Chase && alt < 200)
                    Mode = CameraMode.Pad;
            }
        }

        var camera = GetNodeOrNull<Camera3D>("Camera3D");
        if (camera == null) return;

        float yawRad   = Mathf.DegToRad(_yaw);
        float pitchRad = Mathf.DegToRad(_pitch);

        // In both modes we use the same orbit math; the target is always (0,0,0)
        // because FloatingOrigin keeps the active vessel there.
        // For Pad mode we add a slight upward look target offset so the whole stack is visible.
        float lookAtY = Mode == CameraMode.Pad ? 20f : 0f;

        var camPos = new Vector3(
            _distance * Mathf.Cos(pitchRad) * Mathf.Sin(yawRad),
            _distance * Mathf.Sin(pitchRad) + lookAtY,
            _distance * Mathf.Cos(pitchRad) * Mathf.Cos(yawRad));

        camera.Position = camPos;
        camera.LookAt(new Vector3(0, lookAtY, 0), Vector3.Up);
    }
}
