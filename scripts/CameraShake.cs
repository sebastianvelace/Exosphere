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
    private const float MaxThrustTrans = 0.9f;   // render units, full-throttle dense-air rumble
    private const float MaxThrustRot   = 0.30f;  // degrees
    private const float MaxBuffetTrans = 1.6f;   // render units, peak Max-Q buffet
    private const float MaxBuffetRot   = 0.55f;  // degrees
    private const float MaxFovKick     = 4.0f;   // degrees of extra FOV under high g

    // Reference dynamic pressure for normalising buffet (Pa). Earth ascent Max-Q
    // is roughly 30-35 kPa; we saturate a little above that.
    private const float MaxQReference  = 32_000f;

    // g-force (above 1g) at which the FOV kick saturates.
    private const float FovGReference  = 3.5f;

    // ── Smoothed intensities (ramp in/out so nothing pops) ───────────────────
    private float _thrustEnv;   // 0..1
    private float _buffetEnv;   // 0..1
    private float _fovEnv;      // 0..1

    // ── Noise phase accumulators ─────────────────────────────────────────────
    private float _t;
    private readonly float _seedX = (float)GD.Randf() * 100f;
    private readonly float _seedY = (float)GD.Randf() * 100f;
    private readonly float _seedZ = (float)GD.Randf() * 100f;

    /// <summary>The base (un-kicked) field of view, captured once from the camera.</summary>
    public float BaseFov { get; set; } = 70f;

    /// <summary>Resulting positional offset for this frame (camera-local render units).</summary>
    public Vector3 PositionOffset { get; private set; } = Vector3.Zero;

    /// <summary>Resulting rotational offset for this frame (radians, pitch/yaw/roll).</summary>
    public Vector3 RotationOffset { get; private set; } = Vector3.Zero;

    /// <summary>Resulting field of view for this frame (degrees).</summary>
    public float Fov { get; private set; } = 70f;

    /// <summary>
    /// Advance the shake one frame. <paramref name="distance"/> is the camera orbit
    /// distance so amplitude can be scaled DOWN when zoomed out (less nauseating).
    /// </summary>
    public void Update(double delta, Vessel? vessel, Universe? universe, float distance)
    {
        float dt = (float)delta;
        if (dt <= 0f) dt = 1f / 60f;
        _t += dt;

        // ── Read flight state ────────────────────────────────────────────────
        float throttleActivity = 0f;   // throttle × engines firing
        float qNorm            = 0f;   // dynamic pressure, normalised 0..1 (peaks at Max-Q)
        float gNorm            = 0f;   // (g − 1) above gravity, normalised 0..1

        if (vessel != null && universe != null && !vessel.IsOnRails)
        {
            var body = universe.GetBody(vessel.ReferenceBodyId ?? "earth")
                       ?? universe.GetBody("earth");

            // Engine thrust shake: only when engines are actually firing.
            bool enginesFiring = false;
            foreach (var _ in vessel.Parts.ActiveEngines) { enginesFiring = true; break; }
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
                }
            }
        }

        // ── Smooth / damp the envelopes (ramp in faster than out) ────────────
        _thrustEnv = Damp(_thrustEnv, throttleActivity, dt, 8f, 3f);
        _buffetEnv = Damp(_buffetEnv, Mathf.Min(qNorm, 1f), dt, 6f, 2.5f);
        _fovEnv    = Damp(_fovEnv,    gNorm,                dt, 4f, 2f);

        // Zoom attenuation: at ~80 units full strength, fading with distance so a
        // wide/zoomed-out view stays calm and readable.
        float zoom = Mathf.Clamp(90f / Mathf.Max(distance, 1f), 0.15f, 1f);

        // ── Translational rumble (engine) — layered sines ~15-30 Hz feel ─────
        float eAmp = _thrustEnv * MaxThrustTrans * zoom;
        var engineTrans = new Vector3(
            Osc(18.3f, _seedX) * 0.6f + Osc(27.1f, _seedX + 3f) * 0.4f,
            Osc(21.7f, _seedY) * 0.6f + Osc(31.5f, _seedY + 3f) * 0.4f,
            Osc(15.9f, _seedZ) * 0.6f + Osc(24.3f, _seedZ + 3f) * 0.4f) * eAmp;

        // ── Buffet — lower, broader frequencies, bigger throws near Max-Q ────
        float bAmp = _buffetEnv * MaxBuffetTrans * zoom;
        var buffetTrans = new Vector3(
            Osc(9.2f,  _seedX + 7f) * 0.7f + Osc(13.7f, _seedX + 9f) * 0.3f,
            Osc(7.6f,  _seedY + 7f) * 0.7f + Osc(12.1f, _seedY + 9f) * 0.3f,
            Osc(10.8f, _seedZ + 7f) * 0.7f + Osc(14.9f, _seedZ + 9f) * 0.3f) * bAmp;

        PositionOffset = engineTrans + buffetTrans;

        // ── Rotational shake (radians) ───────────────────────────────────────
        float eRot = Mathf.DegToRad(_thrustEnv * MaxThrustRot * zoom);
        float bRot = Mathf.DegToRad(_buffetEnv * MaxBuffetRot * zoom);
        RotationOffset = new Vector3(
            Osc(19.4f, _seedY + 13f) * eRot + Osc(8.3f,  _seedY + 17f) * bRot, // pitch
            Osc(22.6f, _seedX + 13f) * eRot + Osc(9.7f,  _seedX + 17f) * bRot, // yaw
            Osc(16.2f, _seedZ + 13f) * eRot + Osc(11.4f, _seedZ + 17f) * bRot); // roll

        // ── FOV kick under high g (subtle widen) ─────────────────────────────
        Fov = BaseFov + _fovEnv * MaxFovKick * zoom;
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
