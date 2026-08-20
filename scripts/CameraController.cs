namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Visual;

public enum CameraMode { Chase, Pad, Cockpit }

public partial class CameraController : Node3D
{
    public static CameraController? Instance { get; private set; }

    public CameraMode Mode { get; set; } = CameraMode.Pad;

    /// <summary>True while the first-person cockpit camera is active ([C] cycle).</summary>
    public bool IsCockpitView => _cockpit;

    /// <summary>Switch to first-person cockpit (used by debug/visual harnesses).</summary>
    public void EnterCockpitView()
    {
        _cockpit = true;
        _padPresetIdx = PadPresets.Length;
    }

    /// <summary>Return to an exterior chase view and frame the standalone Ship.</summary>
    public void EnterShipChaseView()
    {
        _cockpit = false;
        _padPresetIdx = 0;
        Mode = CameraMode.Chase;
        _presentationDistanceTarget = null;
        _hasSmoothedFrame = false;
        _yaw = 28f;
        _pitch = 10f;
        // Frame the active geometry instead of assuming a 50 m Starship. Keep the
        // exterior beauty distance close enough to read hull detail and engine layout;
        // the former 2.5x length multiplier left an orbital Starship as a sub-pixel
        // silhouette even though the chase camera itself was correctly positioned.
        double lengthM = SimulationBridge.Instance?.ActiveVessel?.VehicleLength
            ?? 50.0;
        _distance = Mathf.Clamp(
            (float)(lengthM / 2.8 * 1.7),
            10f,
            36f);
    }

    /// <summary>Set a deterministic external chase frame for visual acceptance scenes.</summary>
    public void SetExternalChaseFrame(float yaw, float pitch, float distance)
    {
        _cockpit = false;
        _padPresetIdx = 0;
        Mode = CameraMode.Chase;
        _presentationDistanceTarget = null;
        _hasSmoothedFrame = false;
        _yaw = yaw;
        _pitch = Mathf.Clamp(pitch, -89f, 89f);
        _distance = Mathf.Clamp(distance, MinDistance, MaxDistance);
    }

    // ── Chase / orbit state ───────────────────────────────────────────────
    private float _yaw      = 25f;
    private float _pitch    = 12f;
    private float _distance = 80f;   // full stack is ~43 units tall; 80 gives a nice frame
    private float? _presentationDistanceTarget;

    // Event changes (Pad -> Chase, staging, and the return from EDL presentation) update
    // the requested frame below. Keep the rendered frame in local vessel coordinates and
    // ease it before applying the floating-origin surface frame; this removes camera pops
    // without taking ownership of the player's yaw/pitch/zoom controls after presentation.
    private const float CameraFrameTransitionSeconds = 0.42f;
    private Vector3 _smoothedFramePosition;
    private Vector3 _smoothedFrameTarget;
    private bool _hasSmoothedFrame;

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
    private bool _trackedVehicleInitialized;
    private bool _trackedHadBooster;
    private float _externalFov = 75f;
    private float _externalNear = 0.5f;
    private Camera3D? _camera;
    private Node3D? _cockpitRenderer;
    private Node3D? _exteriorRenderer;
    private double _presentationLookupCooldown;
    private const double PresentationLookupRetrySeconds = 0.25;
    private const float CockpitFov = 60f;
    private const float CockpitNear = 0.04f;
    // After staging, keep the automatic chase camera on the outward hemisphere so
    // the active body's limb remains in front of the camera. The player can still
    // orbit freely afterwards; this only prevents a sun-facing pitch from hiding
    // the planet during the deterministic transition into orbit.
    private const float MinimumOrbitPlanetPitchDeg = 45f;
    // EDL is a presentation-critical exterior shot: at the old 38-unit staging frame the
    // separated Ship occupied only a small fraction of the 1920px capture during entry.
    // This bound is applied only while the EDL overlay is active and eases through the same
    // frame interpolation as staging; it does not alter vessel state or player zoom input.
    private const float EdlPresentationDistance = 28f;

    public override void _Ready()
    {
        Instance = this;
        ResolvePresentationNodes();
        if (_camera is { } camera)
        {
            _externalFov = camera.Fov;
            _externalNear = camera.Near;
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right)
                _dragging = mb.Pressed;

            if (mb.ButtonIndex == MouseButton.WheelUp)
            {
                _presentationDistanceTarget = null;
                _distance = Mathf.Clamp(_distance / ZoomSensitivity, MinDistance, MaxDistance);
            }
            if (mb.ButtonIndex == MouseButton.WheelDown)
            {
                _presentationDistanceTarget = null;
                _distance = Mathf.Clamp(_distance * ZoomSensitivity, MinDistance, MaxDistance);
            }
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
                _presentationDistanceTarget = null;
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
                _presentationDistanceTarget = null;
                var preset = PadPresets[_padPresetIdx];
                _yaw      = preset.yaw;
                _pitch    = preset.pitch;
                _distance = preset.dist;
            }
        }
    }

    public override void _Process(double delta)
    {
        ResolvePresentationNodes(delta);
        if (_cockpit) { DriveCockpit(delta); return; }
        SetCockpitVisible(false);

        // Auto-switch to Chase mode once vessel is clear of the pad
        var bridge = SimulationBridge.Instance;
        if (bridge?.ActiveVessel != null)
        {
            var body = bridge.Universe.GetDominantBody(bridge.ActiveVessel.Position);
            bool hasBooster = bridge.ActiveVessel.Parts.Parts.Any(
                p => p.Definition.IsStarshipFamily
                    && p.Definition.HasVehicleRole("booster"));
            if (_trackedVehicleInitialized && _trackedHadBooster && !hasBooster
                && _distance is > 70f and < 105f)
            {
                _presentationDistanceTarget = 38f;
                // Start the separated-stage view from the illuminated quarter.
                // The user can still orbit freely afterwards.
                var sun = bridge.Universe.GetBody("sun");
                if (sun != null)
                {
                    var localSun = BuildSurfaceFrame(body, bridge.ActiveVessel.Position).Inverse()
                        * ToG((sun.Position - bridge.ActiveVessel.Position).Normalized);
                    _yaw = Mathf.RadToDeg(Mathf.Atan2(localSun.X, localSun.Z));
                    _pitch = Mathf.Clamp(
                        Mathf.RadToDeg(Mathf.Asin(localSun.Y)),
                        MinimumOrbitPlanetPitchDeg, 65f);
                }
            }
            _trackedHadBooster = hasBooster;
            _trackedVehicleInitialized = true;
            if (body != null)
            {
                double alt = bridge.ActiveVessel.GetAltitude(body);
                // Keep pad framing through ~1.1 km so the tower does not vanish in 2 s.
                if (Mode == CameraMode.Pad && alt > 1100)
                    Mode = CameraMode.Chase;
                if (Mode == CameraMode.Chase && alt < 800)
                    Mode = CameraMode.Pad;
            }
        }

        var camera = _camera;
        if (camera == null) return;
        camera.Near = _externalNear;
        _shake.BaseFov = _externalFov;

        float yawRad   = Mathf.DegToRad(_yaw);
        float pitchRad = Mathf.DegToRad(_pitch);

        // The active vessel is at the render origin (FloatingOrigin); the ground sits at
        // -alt/2.8 render units below it. Below ~1.5 km, anchor the camera to the GROUND
        // and watch the rocket climb away — over featureless ocean/terrain this is the only
        // clear cue that the rocket is actually rising.
        double trackAlt = 0.0;
        Basis surfaceFrame = Basis.Identity;
        Vector3 renderUp = Vector3.Up;
        if (bridge?.ActiveVessel is { } tv)
        {
            var trackingBody = bridge.Universe.GetDominantBody(tv.Position);
            trackAlt = tv.GetAltitude(trackingBody);
            surfaceFrame = BuildSurfaceFrame(trackingBody, tv.Position);
            renderUp = surfaceFrame.Y;
        }

        Vector3 targetCamPos;
        Vector3 targetLookTarget;
        if (Mode == CameraMode.Pad && trackAlt < 1100.0)
        {
            // Ground-anchored tracking shot: the pad sits at groundY, the rocket at the
            // origin (0..43 units tall). Look at the MIDPOINT and pull the camera back as the
            // rocket climbs so BOTH the stationary pad and the rocket stay in frame — the
            // growing gap between them is the clear, readable cue that the rocket is rising.
            float groundY = -(float)(trackAlt / 2.8f);            // render-space ground level
            float vehicleHeight = bridge?.ActiveVessel is { } padVessel
                ? (float)(padVessel.VehicleLength / 2.8)
                : 43f;
            float dist = (float)VehicleCameraFraming.PadTrackingDistance(
                vehicleHeight, groundY);
            float midY = (groundY + vehicleHeight) * 0.5f;
            targetCamPos = new Vector3(dist * Mathf.Sin(yawRad), midY, dist * Mathf.Cos(yawRad));
            targetLookTarget = new Vector3(0f, midY, 0f);
        }
        else
        {
            // Pad/chase orbit framing.
            var active = bridge?.ActiveVessel;
            float lookAtY = Mode == CameraMode.Pad ? 22f : 0f;
            float requestedDistance = _presentationDistanceTarget ?? _distance;
            if (EDLController.Instance?.IsPresentationActive == true
                && Mode == CameraMode.Chase)
                requestedDistance = Mathf.Min(requestedDistance, EdlPresentationDistance);
            float effectiveDistance = requestedDistance;
            Vector3 vesselCenter = Vector3.Zero;
            if (Mode == CameraMode.Chase && active != null)
            {
                effectiveDistance = Mathf.Max(effectiveDistance, (float)
                    VehicleCameraFraming.MinimumOrbitDistance(
                        active.VehicleLength, active.MaximumDiameter, camera.Fov));
                float centerU = (float)(active.VehicleLength / (2.0 * 2.8));
                vesselCenter = ToGQuat(active.Orientation) * (Vector3.Up * centerU);
            }
            targetCamPos = new Vector3(
                effectiveDistance * Mathf.Cos(pitchRad) * Mathf.Sin(yawRad),
                effectiveDistance * Mathf.Sin(pitchRad) + lookAtY,
                effectiveDistance * Mathf.Cos(pitchRad) * Mathf.Cos(yawRad));
            targetLookTarget = new Vector3(0f, lookAtY, 0f);
            if (Mode == CameraMode.Chase)
            {
                targetCamPos += surfaceFrame.Inverse() * vesselCenter;
                targetLookTarget = surfaceFrame.Inverse() * vesselCenter;
            }
        }

        if (!_hasSmoothedFrame)
        {
            _smoothedFramePosition = targetCamPos;
            _smoothedFrameTarget = targetLookTarget;
            _hasSmoothedFrame = true;
        }
        else
        {
            float blend = CameraFrameBlend(delta);
            _smoothedFramePosition = _smoothedFramePosition.Lerp(targetCamPos, blend);
            _smoothedFrameTarget = _smoothedFrameTarget.Lerp(targetLookTarget, blend);
        }

        // The presets above are authored in local launch coordinates. Rotate them into the
        // vessel's geodetic frame so screen-up follows radial up instead of inertial +Y.
        camera.Position = surfaceFrame * _smoothedFramePosition;
        Vector3 lookTarget = surfaceFrame * _smoothedFrameTarget;
        camera.LookAt(lookTarget, renderUp);

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
        var camera = _camera;
        var bridge = SimulationBridge.Instance;
        var v = bridge?.ActiveVessel;
        SetCockpitVisible(true);
        if (camera == null || bridge == null || v == null) return;

        // Smooth vessel orientation to absorb high-freq sim jitter. Rate 8/s: fast enough
        // to track real pitch-overs, slow enough to kill single-frame noise spikes.
        Quaternion rawOrient = ToGQuat(v.Orientation);
        _smoothedOrientation = _smoothedOrientation.Slerp(rawOrient, Mathf.Clamp((float)delta * 8f, 0f, 1f));

        // The vessel renders at the origin. The authored cockpit sits at y=36 for the full
        // 121 m stack, but standalone Starship is rebuilt with its engines at y≈0 and nose at
        // y≈18. Move both interior and eye down by the 25.36-unit separation-plane shift
        // (36 → 10.64) after staging; otherwise the astronaut
        // camera floats ~50 m above the separated Ship.
        bool hasSuperHeavy = v.Parts.Parts.Any(
            p => p.Definition.IsStarshipFamily && p.Definition.HasVehicleRole("booster"));
        bool isStarship = v.Parts.Parts.Any(p =>
            p.Definition.IsStarshipFamily
            && (p.Definition.HasVehicleRole("command")
                || p.Definition.HasVehicleRole("ship_engines")));
        float eyeLocalY = hasSuperHeavy ? CockpitRenderer.AuthoredEyeY
            : isStarship ? 10.64f
            : Mathf.Max(1.2f, (float)(v.VehicleLength / 2.8 * 0.72));
        float cockpitLocalOffsetY = eyeLocalY - CockpitRenderer.AuthoredEyeY;

        if (_cockpitRenderer is { } ckn)
        {
            ckn.Position   = _smoothedOrientation * new Vector3(0f, cockpitLocalOffsetY, 0f);
            ckn.Quaternion = _smoothedOrientation;
        }

        // Derive eye/fwd/up from the SMOOTHED orientation, not the raw sim value.
        // The cockpit mesh is oriented with _smoothedOrientation above; using raw
        // vessel orientation for the eye can put the camera through the dash during
        // abrupt state jumps such as debug orbit -> reentry captures.
        Vector3 eye = _smoothedOrientation * new Vector3(
            0f, eyeLocalY, CockpitRenderer.AuthoredEyeZ);
        Vector3 fwd = (_smoothedOrientation * Vector3.Up).Normalized();
        Vector3 up  = (_smoothedOrientation * Vector3.Forward).Normalized();

        // G-force: push the eye OPPOSITE the net acceleration (into the seat under thrust).
        var uni = bridge.Universe;
        if (uni != null)
        {
            double t = uni.CurrentTime;
            if (_lastT > 0 && t - _lastT > 1e-4)
            {
                var body = uni.GetDominantBody(v.Position);
                var properAccel = v.GetProperAcceleration(body);
                Vector3 target = -ToG(properAccel) / 9.80665f * 0.004f;
                float m = target.Length();
                if (m > 0.015f) target *= 0.015f / m;
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
        Vector3 look  = fwd.Rotated(right, Mathf.DegToRad(-14f + _lookPitch)).Rotated(up, Mathf.DegToRad(_lookYaw));

        camera.Near = CockpitNear;
        camera.Position = eye + _gOffset;
        camera.LookAt(eye + _gOffset + look, up);

        // Interior vibration — reduced multiplier (×0.6 vs old ×1.8) so ascent stays readable.
        // CameraShake also caps rotational throw to ±2° (see CameraShake.CockpitRotCap).
        if (!_baseFovCaptured) { _shake.BaseFov = camera.Fov; _baseFovCaptured = true; }
        _shake.Update(delta, v, uni, 40f);
        camera.Translate(_shake.CockpitPositionOffset);
        var rot = _shake.CockpitRotationOffset;   // already capped to ±2° per axis
        camera.RotateObjectLocal(Vector3.Right,   rot.X);
        camera.RotateObjectLocal(Vector3.Up,      rot.Y);
        camera.RotateObjectLocal(Vector3.Forward, rot.Z);
        camera.Fov = CockpitFov; // stable IVA optics; no exterior wide-angle distortion
    }

    private void SetCockpitVisible(bool vis)
    {
        if (_cockpitRenderer is { } cockpit
            && GodotObject.IsInstanceValid(cockpit)
            && cockpit.Visible != vis)
            cockpit.Visible = vis;
        // Hide the rocket exterior while inside the cockpit (restore it otherwise).
        if (_exteriorRenderer is { } exterior
            && GodotObject.IsInstanceValid(exterior)
            && exterior.Visible == vis)
            exterior.Visible = !vis;
    }

    /// <summary>
    /// Resolves presentation nodes once they exist and reuses them on subsequent frames.
    /// SimulationBridge creates the active renderer after this controller's _Ready(), so
    /// the lookup remains lazy until dynamic nodes are present. The old StarshipRenderer
    /// name is retained for temporary visual harness compatibility; production uses
    /// ActiveVesselRenderer.
    /// </summary>
    private void ResolvePresentationNodes(double delta = 0.0)
    {
        bool cachedFallback = _exteriorRenderer != null
            && GodotObject.IsInstanceValid(_exteriorRenderer)
            && _exteriorRenderer.Name == "StarshipRenderer";
        bool needsLookup = _camera == null
            || !GodotObject.IsInstanceValid(_camera)
            || _cockpitRenderer == null
            || !GodotObject.IsInstanceValid(_cockpitRenderer)
            || _exteriorRenderer == null
            || !GodotObject.IsInstanceValid(_exteriorRenderer)
            || cachedFallback;
        if (!needsLookup) return;

        if (delta > 0.0)
        {
            _presentationLookupCooldown -= System.Math.Max(0.0, delta);
            if (_presentationLookupCooldown > 0.0) return;
        }

        if (_camera == null || !GodotObject.IsInstanceValid(_camera))
            _camera = GetNodeOrNull<Camera3D>("Camera3D");

        var root = GetTree().Root;
        if (_cockpitRenderer == null || !GodotObject.IsInstanceValid(_cockpitRenderer))
            _cockpitRenderer = root.FindChild("CockpitRenderer", true, false) as Node3D;

        if (_exteriorRenderer == null
            || !GodotObject.IsInstanceValid(_exteriorRenderer)
            || cachedFallback)
        {
            _exteriorRenderer = root.FindChild("ActiveVesselRenderer", true, false) as Node3D
                ?? (cachedFallback
                    ? _exteriorRenderer
                    : root.FindChild("StarshipRenderer", true, false) as Node3D);
        }

        // ActiveVesselRenderer and CockpitRenderer are created lazily by
        // SimulationBridge. Retry while a dynamic scene is incomplete, but never
        // turn a missing-node startup state or legacy harness fallback into a
        // full scene-tree walk on every render frame.
        _presentationLookupCooldown = PresentationLookupRetrySeconds;
    }

    private static Vector3 ToG(Vector3d v) => new((float)v.X, (float)v.Y, (float)v.Z);
    private static Quaternion ToGQuat(Quaterniond q) => new((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);

    private static float CameraFrameBlend(double delta)
    {
        if (delta <= 0.0) return 1f;
        return 1f - Mathf.Exp(-(float)delta / CameraFrameTransitionSeconds);
    }

    private static Basis BuildSurfaceFrame(Exosphere.Simulation.CelestialBody body, Vector3d position)
    {
        Vector3d up = (position - body.Position).Normalized;
        Vector3d east = body.GetEastDirection(position);
        if (east.MagnitudeSquared < 1e-12)
        {
            Vector3d reference = System.Math.Abs(up.X) < 0.9
                ? Vector3d.Right : Vector3d.Forward;
            east = reference.Cross(up).Normalized;
        }
        Vector3d south = east.Cross(up).Normalized;
        return new Basis(ToG(east), ToG(up), ToG(south));
    }
}
