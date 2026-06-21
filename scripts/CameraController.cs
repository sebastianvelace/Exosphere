namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation.Math;

public enum CameraMode { Chase, Pad, Cockpit }

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

    // ── First-person cockpit (IVA) state ──────────────────────────────────────
    private bool       _cockpit;            // [C] cycles into this after the pad presets
    private Vector3d   _lastVel;
    private double     _lastT = -1.0;
    private Vector3    _gOffset, _gTarget;  // eye push from G-force (render units)
    private float      _lookYaw, _lookPitch;
    // Smoothed vessel orientation — prevents raw sim jitter reaching the cockpit camera.
    private Quaternion _smoothedOrientation = Quaternion.Identity;

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
            if (_cockpit)
            {
                // Free-look inside the cockpit, clamped.
                _lookYaw   = Mathf.Clamp(_lookYaw   - mm.Relative.X * 0.25f, -70f, 70f);
                _lookPitch = Mathf.Clamp(_lookPitch - mm.Relative.Y * 0.25f, -70f, 70f);
            }
            else
            {
                _yaw   -= mm.Relative.X * OrbitSensitivity;
                _pitch -= mm.Relative.Y * OrbitSensitivity;
                _pitch  = Mathf.Clamp(_pitch, -89f, 89f);
            }
        }

        // C key: cycle pad/chase presets → first-person cockpit → back to preset 0.
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.C)
        {
            _padPresetIdx = (_padPresetIdx + 1) % (PadPresets.Length + 1);
            _cockpit = _padPresetIdx == PadPresets.Length;
            if (!_cockpit)
            {
                var preset = PadPresets[_padPresetIdx];
                _yaw      = preset.yaw;
                _pitch    = preset.pitch;
                _distance = preset.dist;
            }
        }
    }

    public override void _Process(double delta)
    {
        if (_cockpit) { DriveCockpit(delta); return; }
        SetCockpitVisible(false);

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

    // ── First-person cockpit camera ───────────────────────────────────────────
    private void DriveCockpit(double delta)
    {
        var camera = GetNodeOrNull<Camera3D>("Camera3D");
        var bridge = SimulationBridge.Instance;
        var v = bridge?.ActiveVessel;
        if (camera == null || bridge == null || v == null) return;

        SetCockpitVisible(true);

        // Smooth vessel orientation to absorb high-freq sim jitter. Rate 8/s: fast enough
        // to track real pitch-overs, slow enough to kill single-frame noise spikes.
        Quaternion rawOrient = ToGQuat(v.Orientation);
        _smoothedOrientation = _smoothedOrientation.Slerp(rawOrient, Mathf.Clamp((float)delta * 8f, 0f, 1f));

        // The vessel renders at the origin; the cockpit is a sibling node we orient to the vessel
        // each frame. Eye at local (0,36,0.6) u, forward +Y (nose), up -Z.
        if (GetTree().Root.FindChild("CockpitRenderer", true, false) is Node3D ckn)
        {
            ckn.Position   = Vector3.Zero;
            ckn.Quaternion = _smoothedOrientation;
        }

        // Derive eye/fwd/up from the SMOOTHED orientation, not the raw sim value.
        // The cockpit mesh is oriented with _smoothedOrientation above; using raw
        // vessel orientation for the eye can put the camera through the dash during
        // abrupt state jumps such as debug orbit -> reentry captures.
        Vector3 eye = _smoothedOrientation * new Vector3(0f, 36f, 0.6f);
        Vector3 fwd = (_smoothedOrientation * Vector3.Up).Normalized();
        Vector3 up  = (_smoothedOrientation * Vector3.Back).Normalized();

        // G-force: push the eye OPPOSITE the net acceleration (into the seat under thrust).
        var uni = bridge.Universe;
        if (uni != null)
        {
            double t = uni.CurrentTime;
            if (_lastT > 0 && t - _lastT > 1e-4)
            {
                var a = (v.Velocity - _lastVel) / (t - _lastT);   // m/s²
                Vector3 target = -ToG(a) / 9.80665f * 0.05f;
                float m = target.Length();
                if (m > 0.15f) target *= 0.15f / m;               // cap the seat push
                _gTarget = target;
            }
            _lastVel = v.Velocity; _lastT = t;
        }
        _gOffset = _gOffset.Lerp(_gTarget, Mathf.Clamp((float)delta * 6f, 0f, 1f));

        // Free-look (recenters when not dragging).
        if (!_dragging)
        {
            float k = Mathf.Clamp((float)delta * 3f, 0f, 1f);
            _lookYaw   = Mathf.Lerp(_lookYaw,   0f, k);
            _lookPitch = Mathf.Lerp(_lookPitch, 0f, k);
        }
        Vector3 right = fwd.Cross(up).Normalized();
        // Rest the gaze slightly down toward the console without letting the
        // dashboard dominate the windshield view.
        Vector3 look  = fwd.Rotated(right, Mathf.DegToRad(8f + _lookPitch)).Rotated(up, Mathf.DegToRad(_lookYaw));

        camera.Position = eye + _gOffset;
        camera.LookAt(eye + _gOffset + look, up);

        // Interior vibration — reduced multiplier (×0.6 vs old ×1.8) so ascent stays readable.
        // CameraShake also caps rotational throw to ±2° (see CameraShake.CockpitRotCap).
        if (!_baseFovCaptured) { _shake.BaseFov = camera.Fov; _baseFovCaptured = true; }
        _shake.Update(delta, v, uni, 40f);
        camera.Translate(_shake.PositionOffset * 0.6f);
        var rot = _shake.CockpitRotationOffset;   // already capped to ±2° per axis
        camera.RotateObjectLocal(Vector3.Right,   rot.X);
        camera.RotateObjectLocal(Vector3.Up,      rot.Y);
        camera.RotateObjectLocal(Vector3.Forward, rot.Z);
        camera.Fov = _shake.Fov;
    }

    private void SetCockpitVisible(bool vis)
    {
        if (GetTree().Root.FindChild("CockpitRenderer", true, false) is Node3D ck && ck.Visible != vis)
            ck.Visible = vis;
        // Hide the rocket exterior while inside the cockpit (restore it otherwise).
        if (GetTree().Root.FindChild("StarshipRenderer", true, false) is Node3D sr && sr.Visible == vis)
            sr.Visible = !vis;
    }

    private static Vector3 ToG(Vector3d v) => new((float)v.X, (float)v.Y, (float)v.Z);
    private static Quaternion ToGQuat(Quaterniond q) => new((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);
}
