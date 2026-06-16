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
    [Export] public float MaxDistance      { get; set; } = 400_000f;  // pull back into space to see the whole planet

    // ── Pad preset positions [yaw°, pitch°, distance] ─────────────────────
    // Cycle with C key: side view → tower side → wide front
    private static readonly (float yaw, float pitch, float dist)[] PadPresets =
    {
        (  30f,  8f,  95f),   // default: slight side, frames full 43-unit stack
        ( 180f,  4f,  75f),   // tower side (shows Mechazilla arms)
        (   0f, 18f, 140f),   // front wide — shows full profile
    };
    private int _padPresetIdx = 0;

    private bool _dragging;

    // ── Force-feel shake (cosmetic; driven by vessel state) ───────────────────
    private readonly CameraShake _shake = new();
    private bool _baseFovCaptured;

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
                if (Mode == CameraMode.Pad && alt > 2000)
                    Mode = CameraMode.Chase;
                if (Mode == CameraMode.Chase && alt < 1500)
                    Mode = CameraMode.Pad;
            }
        }

        var camera = GetNodeOrNull<Camera3D>("Camera3D");
        if (camera == null) return;

        float yawRad   = Mathf.DegToRad(_yaw);
        float pitchRad = Mathf.DegToRad(_pitch);

        // The active vessel is at the render origin (FloatingOrigin); the ground sits at
        // -alt/2.8 render units below it. Below ~1.5 km, anchor the camera to the GROUND
        // and watch the rocket climb away — over featureless ocean/terrain this is the only
        // clear cue that the rocket is actually rising.
        double trackAlt = 0.0;
        if (bridge?.ActiveVessel is { } tv && bridge.Universe.GetBody("earth") is { } te)
            trackAlt = tv.GetAltitude(te);

        Vector3 camPos;
        Vector3 lookTarget;
        if (Mode == CameraMode.Pad && trackAlt < 2000.0)
        {
            // Ground-anchored tracking shot: the pad sits at groundY, the rocket at the
            // origin (0..43 units tall). Look at the MIDPOINT and pull the camera back as the
            // rocket climbs so BOTH the stationary pad and the rocket stay in frame — the
            // growing gap between them is the clear, readable cue that the rocket is rising.
            float groundY = -(float)(trackAlt / 2.8f);            // render-space ground level
            float dist    = Mathf.Clamp((43f - groundY) * 1.05f, 70f, 850f);
            float midY    = (groundY + 43f) * 0.5f;               // halfway pad→rocket-top
            camPos = new Vector3(dist * Mathf.Sin(yawRad), midY, dist * Mathf.Cos(yawRad));
            lookTarget = new Vector3(0f, midY, 0f);
        }
        else
        {
            // Pad/chase orbit framing.
            float lookAtY = Mode == CameraMode.Pad ? 22f : 0f;
            camPos = new Vector3(
                _distance * Mathf.Cos(pitchRad) * Mathf.Sin(yawRad),
                _distance * Mathf.Sin(pitchRad) + lookAtY,
                _distance * Mathf.Cos(pitchRad) * Mathf.Cos(yawRad));
            lookTarget = new Vector3(0f, lookAtY, 0f);
        }

        camera.Position = camPos;
        camera.LookAt(lookTarget, Vector3.Up);

        // ── Force-feel shake — applied AFTER LookAt so the orbit framing is intact.
        // Drives off the active vessel's throttle/engine activity (rumble), dynamic
        // pressure q = ½ρv² (Max-Q buffet) and g-force (subtle FOV kick). Amplitudes
        // scale DOWN with orbit distance so zooming out stays calm.
        if (!_baseFovCaptured)
        {
            _shake.BaseFov = camera.Fov;
            _baseFovCaptured = true;
        }

        _shake.Update(delta, bridge?.ActiveVessel, bridge?.Universe, _distance);

        // Translate in camera-local space so the jitter tracks the current view.
        camera.Translate(_shake.PositionOffset);

        // Add a small rotational perturbation on top of the LookAt orientation.
        var rot = _shake.RotationOffset;
        camera.RotateObjectLocal(Vector3.Right,   rot.X);  // pitch
        camera.RotateObjectLocal(Vector3.Up,      rot.Y);  // yaw
        camera.RotateObjectLocal(Vector3.Forward, rot.Z);  // roll

        camera.Fov = _shake.Fov;
    }
}
