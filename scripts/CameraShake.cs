namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;

/// <summary>
/// Cosmetic camera force-feel system. Produces a small translational + rotational
/// offset (and an FOV kick) driven by the active vessel's flight state so the player
/// can FEEL engine thrust, transonic / Max-Q buffeting and high-g acceleration.
///
/// This is purely visual — it never touches the deterministic simulation. Continuous
/// noise (sums of sines at incommensurate frequencies) is used for smooth shake rather
/// than per-frame white noise. A tiny bit of GD.Randf() seeds the phase so repeat
/// launches don't look identical.
///
/// Owned by CameraController; the offsets are applied AFTER LookAt so the orbit logic
/// is untouched.
/// </summary>
public sealed class CameraShake
{
    // ── Tunables (kept SMALL — enhance, don't nauseate) ───────────────────────
    // Render scale is ~2.8 m/unit; chase/pad distances are ~80-140 units.
    private const float MaxThrustTrans = 0.20f;  // render units, full-throttle dense-air rumble
    private const float MaxThrustRot   = 0.08f;  // degrees
    private const float MaxBuffetTrans = 0.35f;  // render units, peak Max-Q buffet
    private const float MaxBuffetRot   = 0.12f;  // degrees
    private const float MaxFovKick     = 1.6f;   // degrees of extra FOV under high g

    // Entry deceleration shake. A hypersonic entry is not the same event as Max-Q: the load
    // is an order of magnitude longer-lived and far lower in frequency, so it gets its own
    // envelope and its own oscillator band rather than being folded into the buffet term.
    private const float MaxEntryTrans  = 0.45f;  // render units at the full-scale entry load
    private const float MaxEntryRot    = 0.16f;  // degrees

    // Reference dynamic pressure for normalising buffet (Pa). Earth ascent Max-Q
    // is roughly 30-35 kPa; we saturate a little above that.
    private const float MaxQReference  = 32_000f;

    // g-force (above 1g) at which the FOV kick saturates.
    private const float FovGReference  = 3.5f;

    // Aerodynamic deceleration (g) at which the entry shake saturates. Chosen to match the
    // real orbital-entry wall: a nominal Mercury/Gemini/Soyuz entry peaks at 4-5 g, so
    // saturating at 4 g means the player is pinned exactly when the crew would be.
    private const float EntryGReference = 4.0f;

    // IVA represents a restrained head, not an external chase camera: cap at ~0.23° and
    // centimetre-scale translation (render scale 2.8 m/u).
    public const float CockpitRotCap = 0.004f;
    public const float CockpitTransCap = 0.006f;

    // ── Smoothed intensities (ramp in/out so nothing pops) ───────────────────
    private float _thrustEnv;   // 0..1
    private float _buffetEnv;   // 0..1
    private float _fovEnv;      // 0..1
    private float _entryEnv;    // 0..1, aerodynamic deceleration load

    // Camera shake is a presentation consumer. Keep envelope integration per frame for
    // smoothness, but sample the physical inputs at a bounded cadence.
    private const double PhysicsSamplePeriodSeconds = 1.0 / 20.0;
    private double _physicsSampleTimer;
    private Vessel? _sampledVessel;
    private Universe? _sampledUniverse;
    private float _sampledThrottleActivity;
    private float _sampledQNorm;
    private float _sampledGNorm;
    private float _sampledEntryNorm;

    // ── Noise phase accumulators ─────────────────────────────────────────────
    private float _t;
    private readonly float _seedX = (float)GD.Randf() * 100f;
    private readonly float _seedY = (float)GD.Randf() * 100f;
    private readonly float _seedZ = (float)GD.Randf() * 100f;
    private const float PositionFilterRate = 16f;
    private const float RotationFilterRate = 18f;

    /// <summary>The base (un-kicked) field of view, captured once from the camera.</summary>
    public float BaseFov { get; set; } = 70f;

    /// <summary>Resulting positional offset for this frame (camera-local render units).</summary>
    public Vector3 PositionOffset { get; private set; } = Vector3.Zero;

    /// <summary>Resulting rotational offset for this frame (radians, pitch/yaw/roll).</summary>
    public Vector3 RotationOffset { get; private set; } = Vector3.Zero;

    /// <summary>
    /// Rotational offset for the cockpit (IVA) view — same shake as <see cref="RotationOffset"/>
    /// but each axis clamped to ±<see cref="CockpitRotCap"/> so ascent buffeting never throws
    /// the interior camera far enough to obscure the windows/console.
    /// </summary>
    public Vector3 CockpitRotationOffset { get; private set; } = Vector3.Zero;
    public Vector3 CockpitPositionOffset { get; private set; } = Vector3.Zero;

    /// <summary>Resulting field of view for this frame (degrees).</summary>
    public float Fov { get; private set; } = 70f;

    /// <summary>Largest per-frame cosmetic step observed since this instance was created.</summary>
    public float PeakPositionStepPerSecond { get; private set; }
    public float PeakRotationStepDegreesPerSecond { get; private set; }
    public float PeakFovStepPerSecond { get; private set; }

    private Vector3 _lastPositionOffset = Vector3.Zero;
    private Vector3 _lastRotationOffset = Vector3.Zero;
    private float _lastFov = 70f;

    /// <summary>
    /// Advance the shake one frame. <paramref name="distance"/> is the camera orbit
    /// distance so amplitude can be scaled DOWN when zoomed out (less nauseating).
    /// </summary>
    public void Update(double delta, Vessel? vessel, Universe? universe, float distance)
    {
        float dt = (float)delta;
        if (dt <= 0f) dt = 1f / 60f;
        _t += dt;

        _physicsSampleTimer -= System.Math.Max(0.0, delta);
        if (_physicsSampleTimer <= 0.0
            || !ReferenceEquals(vessel, _sampledVessel)
            || !ReferenceEquals(universe, _sampledUniverse))
        {
            _physicsSampleTimer = PhysicsSamplePeriodSeconds;
            _sampledVessel = vessel;
            _sampledUniverse = universe;
            SampleFlightState(vessel, universe);
        }

        // ── Read the latest sampled flight state ─────────────────────────────
        float throttleActivity = _sampledThrottleActivity;
        float qNorm            = _sampledQNorm;
        float gNorm            = _sampledGNorm;
        float entryNorm        = _sampledEntryNorm;

        // ── Smooth / damp the envelopes (ramp in faster than out) ────────────
        _thrustEnv = Damp(_thrustEnv, throttleActivity, dt, 8f, 3f);
        _buffetEnv = Damp(_buffetEnv, Mathf.Min(qNorm, 1f), dt, 6f, 2.5f);
        _fovEnv    = Damp(_fovEnv,    gNorm,                dt, 4f, 2f);
        // Entry load builds and decays over tens of seconds, so damp it much more slowly
        // than the engine/buffet envelopes — it should swell, not flicker.
        _entryEnv  = Damp(_entryEnv,  entryNorm,            dt, 2.5f, 1.2f);

        // Zoom attenuation: at ~80 units full strength, fading with distance so a
        // wide/zoomed-out view stays calm and readable.
        float zoom = Mathf.Clamp(90f / Mathf.Max(distance, 1f), 0.15f, 1f);

        // Accessibility: reduced motion suppresses every cosmetic camera offset. The
        // envelopes are still integrated above so re-enabling mid-flight does not pop.
        if (UserInterfaceSettings.ReducedMotion) zoom = 0f;

        // ── Translational rumble (engine) — bounded low-frequency motion ─────
        float eAmp = _thrustEnv * MaxThrustTrans * zoom;
        var engineTrans = new Vector3(
            Osc(6.4f, _seedX) * 0.6f + Osc(9.1f, _seedX + 3f) * 0.4f,
            Osc(7.2f, _seedY) * 0.6f + Osc(10.4f, _seedY + 3f) * 0.4f,
            Osc(5.8f, _seedZ) * 0.6f + Osc(8.3f, _seedZ + 3f) * 0.4f) * eAmp;

        // ── Buffet — lower, broader frequencies, bigger throws near Max-Q ────
        float bAmp = _buffetEnv * MaxBuffetTrans * zoom;
        var buffetTrans = new Vector3(
            Osc(3.1f,  _seedX + 7f) * 0.7f + Osc(5.2f, _seedX + 9f) * 0.3f,
            Osc(2.8f,  _seedY + 7f) * 0.7f + Osc(4.7f, _seedY + 9f) * 0.3f,
            Osc(3.6f,  _seedZ + 7f) * 0.7f + Osc(5.5f, _seedZ + 9f) * 0.3f) * bAmp;

        // ── Entry deceleration — the slowest, heaviest band (~3-7 Hz) ────────
        // Low frequency and large throw: a hypersonic entry reads as the whole vehicle
        // being shoved and wallowing, not as the high-frequency rattle of a live engine.
        float rAmp = _entryEnv * MaxEntryTrans * zoom;
        var entryTrans = new Vector3(
            Osc(1.1f, _seedX + 21f) * 0.75f + Osc(2.2f, _seedX + 23f) * 0.25f,
            Osc(1.3f, _seedY + 21f) * 0.75f + Osc(2.5f, _seedY + 23f) * 0.25f,
            Osc(0.9f, _seedZ + 21f) * 0.75f + Osc(2.0f, _seedZ + 23f) * 0.25f) * rAmp;

        var targetPositionOffset = engineTrans + buffetTrans + entryTrans;
        float positionBlend = 1f - Mathf.Exp(-PositionFilterRate * dt);
        PositionOffset = PositionOffset.Lerp(targetPositionOffset, positionBlend);
        CockpitPositionOffset = ClampLength(PositionOffset * 0.006f, CockpitTransCap);

        // ── Rotational shake (radians) ───────────────────────────────────────
        float eRot = Mathf.DegToRad(_thrustEnv * MaxThrustRot * zoom);
        float bRot = Mathf.DegToRad(_buffetEnv * MaxBuffetRot * zoom);
        float rRot = Mathf.DegToRad(_entryEnv * MaxEntryRot * zoom);
        var targetRotationOffset = new Vector3(
            Osc(6.8f, _seedY + 13f) * eRot + Osc(3.8f,  _seedY + 17f) * bRot
                + Osc(1.1f, _seedY + 27f) * rRot,  // pitch
            Osc(7.6f, _seedX + 13f) * eRot + Osc(4.2f,  _seedX + 17f) * bRot
                + Osc(0.9f, _seedX + 27f) * rRot,  // yaw
            Osc(5.9f, _seedZ + 13f) * eRot + Osc(4.8f,  _seedZ + 17f) * bRot
                + Osc(1.3f, _seedZ + 27f) * rRot); // roll
        float rotationBlend = 1f - Mathf.Exp(-RotationFilterRate * dt);
        RotationOffset = RotationOffset.Lerp(targetRotationOffset, rotationBlend);

        // Cockpit variant: clamp each axis so interior buffeting stays readable.
        CockpitRotationOffset = new Vector3(
            Mathf.Clamp(RotationOffset.X, -CockpitRotCap, CockpitRotCap),
            Mathf.Clamp(RotationOffset.Y, -CockpitRotCap, CockpitRotCap),
            Mathf.Clamp(RotationOffset.Z, -CockpitRotCap, CockpitRotCap));

        // ── FOV kick under high g (subtle widen) ─────────────────────────────
        Fov = BaseFov + _fovEnv * MaxFovKick * zoom;

        PeakPositionStepPerSecond = Mathf.Max(PeakPositionStepPerSecond,
            _lastPositionOffset.DistanceTo(PositionOffset) / dt);
        PeakRotationStepDegreesPerSecond = Mathf.Max(PeakRotationStepDegreesPerSecond,
            Mathf.RadToDeg(_lastRotationOffset.DistanceTo(RotationOffset)) / dt);
        PeakFovStepPerSecond = Mathf.Max(PeakFovStepPerSecond,
            Mathf.Abs(_lastFov - Fov) / dt);
        _lastPositionOffset = PositionOffset;
        _lastRotationOffset = RotationOffset;
        _lastFov = Fov;
    }

    private void SampleFlightState(Vessel? vessel, Universe? universe)
    {
        float throttleActivity = 0f;   // throttle × engines firing
        float qNorm            = 0f;   // dynamic pressure, normalised 0..1 (peaks at Max-Q)
        float gNorm            = 0f;   // (g − 1) above gravity, normalised 0..1
        float entryNorm        = 0f;   // aerodynamic deceleration, normalised 0..1

        if (vessel != null && universe != null && !vessel.IsOnRails)
        {
            var body = universe.GetBody(vessel.ReferenceBodyId ?? "earth")
                       ?? universe.GetBody("earth");

            // Engine thrust shake: only when engines are actually firing.
            bool enginesFiring = vessel.HasActiveEngineParts;
            if (enginesFiring)
                throttleActivity = Mathf.Clamp((float)vessel.Throttle, 0f, 1f);

            if (body != null)
            {
                // Aerodynamic buffeting: q = ½·ρ·v².
                double density = body.GetAtmosphericDensity(vessel.Position);
                if (density > 0.0)
                {
                    double v = vessel.GetSurfaceVelocity(body).Magnitude;
                    double q = 0.5 * density * v * v;
                    qNorm = Mathf.Clamp((float)(q / MaxQReference), 0f, 1.4f);
                }

                // g-force from non-gravitational forces (thrust + drag) / weight.
                double mass = vessel.TotalMass;
                if (mass > 0.0)
                {
                    var thrust = vessel.ComputeThrust(body);
                    var drag   = vessel.ComputeDrag(body);
                    double accel = (thrust + drag).Magnitude / mass;
                    double g = accel / 9.81;
                    gNorm = Mathf.Clamp((float)(g / FovGReference), 0f, 1f);

                    // Aerodynamic deceleration ALONE — the entry load. Isolating drag from
                    // thrust is what makes this read as "the atmosphere is stopping us" and
                    // not as "the engines are pushing us"; the two never coincide in EDL.
                    double aeroG = drag.Magnitude / mass / 9.80665;
                    entryNorm = Mathf.Clamp((float)(aeroG / EntryGReference), 0f, 1f);
                }
            }
        }

        _sampledThrottleActivity = throttleActivity;
        _sampledQNorm = qNorm;
        _sampledGNorm = gNorm;
        _sampledEntryNorm = entryNorm;
    }

    private static Vector3 ClampLength(Vector3 value, float maxLength)
    {
        float length = value.Length();
        return length > maxLength && length > 1e-8f ? value * (maxLength / length) : value;
    }

    // Single normalised oscillator in [-1, 1].
    private float Osc(float freq, float phase) => Mathf.Sin(_t * freq + phase);

    // Asymmetric exponential smoothing: rate `up` when rising, `down` when falling.
    private static float Damp(float current, float target, float dt, float up, float down)
    {
        float rate = target > current ? up : down;
        float k = 1f - Mathf.Exp(-rate * dt);
        return current + (target - current) * k;
    }
}
