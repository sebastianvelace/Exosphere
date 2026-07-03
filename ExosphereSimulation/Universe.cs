// Forward declarations note:
// Vessel, Parts.Part, Physics.StressSolver, and Physics.ThermalModel are defined
// in other files within this assembly.  No additional using directives are required
// for types in the same namespace.

namespace Exosphere.Simulation;

using Exosphere.Simulation.Integrators;
using Exosphere.Simulation.Math;

/// <summary>
/// Root simulation container.
/// Owns all celestial bodies and vessels, advances simulation time,
/// and dispatches to the appropriate integrator based on warp factor.
/// </summary>
public class Universe
{
    private readonly List<CelestialBody> _bodies  = new();
    private readonly List<Vessel>        _vessels = new();

    public IReadOnlyList<CelestialBody> Bodies  => _bodies.AsReadOnly();
    public IReadOnlyList<Vessel>        Vessels => _vessels.AsReadOnly();

    /// <summary>Current simulation time (seconds since J2000).</summary>
    public double CurrentTime { get; private set; } = 0.0;

    /// <summary>
    /// Simulation time scale.
    /// 1 = real-time; 4 = full RK4 physics at 4× speed;
    /// up to 1000 = mixed (active vessel RK4, others on rails);
    /// above 1000 = everything on Keplerian rails.
    /// </summary>
    public double TimeScale { get; set; } = 1.0;

    /// <summary>The vessel the player is currently controlling.</summary>
    public Vessel? ActiveVessel { get; set; }

    /// <summary>Maximum physics sub-step (s) used in full-physics mode (50 Hz).</summary>
    private const double MaxPhysicsStep = 0.02;

    /// <summary>
    /// Maximum sub-step (s) used in the mixed time-warp branch for the active vessel.
    /// Bodies on rails are exact, so the only error source here is the vessel's RK4
    /// integration of its own orbit. RK4 keeps the orbit shape to ~1e-6 % even at much
    /// larger steps, but bounding the step also bounds how far the vessel moves before
    /// the dominant body / SOI is re-evaluated and before surface impact is checked —
    /// at 2 s a LEO vessel advances ~16 km/step, so it cannot tunnel through a planet
    /// or jump across an SOI boundary undetected. At warp 1000 (≈16.7 s of sim time per
    /// frame) this is ~8 sub-steps, which is negligible since only one vessel integrates.
    /// </summary>
    private const double MaxCoastStep = 2.0;

    /// <summary>Max RK4 sub-step (s) while the active vessel is THRUSTING under warp — kept
    /// small so a powered burn stays accurate (≈2 steps/frame at x10).</summary>
    private const double MaxThrustStep = 0.1;

    /// <summary>
    /// Maximum surface-relative speed (m/s) that counts as a soft landing.
    /// At or below this threshold the vessel is gently clamped to the surface instead of
    /// being destroyed.  Covers real-time gentle set-down and EDL final approach.
    /// Orbital re-entry speeds (≥ 100 m/s) are several orders of magnitude above this
    /// threshold, so they will always trigger destruction.
    /// </summary>
    private const double SoftLandingThreshold = Flight.AscentStagingPolicy.SoftLandingSpeedMps;

    // ── Object management ─────────────────────────────────────────────────

    /// <summary>Adds a celestial body to the universe (no-op if already present).</summary>
    public void AddBody(CelestialBody body)   { if (!_bodies.Contains(body))   _bodies.Add(body); }

    /// <summary>Adds a vessel to the universe (no-op if already present).</summary>
    public void AddVessel(Vessel vessel)      { if (!_vessels.Contains(vessel)) _vessels.Add(vessel); }

    /// <summary>Removes a vessel from the universe.</summary>
    public void RemoveVessel(Vessel vessel)   { _vessels.Remove(vessel); }

    /// <summary>Finds a celestial body by its <see cref="CelestialBody.Id"/>.</summary>
    public CelestialBody? GetBody(string id)  => _bodies.FirstOrDefault(b => b.Id == id);

    /// <summary>
    /// Returns the celestial body whose sphere of influence contains
    /// <paramref name="position"/>.  When multiple SOIs overlap, the one where the
    /// position is deepest (smallest distance/SOI ratio) wins.
    /// Falls back to the most massive body (the Sun) when no SOI contains the point.
    /// </summary>
    public CelestialBody GetDominantBody(Vector3d position)
    {
        // Pick the body with the smallest SOI that still contains the position.
        // This correctly resolves the hierarchy: Moon < Earth < Sun.
        CelestialBody? best    = null;
        double         bestSoi = double.MaxValue;

        foreach (var body in _bodies)
        {
            double dist = (position - body.Position).Magnitude;
            if (dist < body.SphereOfInfluence && body.SphereOfInfluence < bestSoi)
            {
                bestSoi = body.SphereOfInfluence;
                best    = body;
            }
        }

        return best ?? _bodies.OrderByDescending(b => b.Mass).First();
    }

    // ── Main tick ──────────────────────────────────────────────────────────

    /// <summary>
    /// Advances the simulation by <paramref name="realDeltaTime"/> wall-clock seconds.
    /// The actual simulated time advance equals
    /// <c>realDeltaTime * <see cref="TimeScale"/></c>.
    /// </summary>
    public void Tick(double realDeltaTime)
    {
        double simDelta = realDeltaTime * TimeScale;
        if (simDelta <= 0.0) return;

        if (TimeScale <= 4.0)
        {
            // Full RK4 physics, capped at MaxPhysicsStep per sub-step
            double remaining = simDelta;
            while (remaining > 1e-12)
            {
                double step  = System.Math.Min(remaining, MaxPhysicsStep);
                TickPhysics(step);
                CurrentTime += step;
                remaining   -= step;
            }
        }
        else if (TimeScale <= 1000.0)
        {
            // Mixed: active vessel uses RK4; all others go on rails.
            // Sub-step (capped at MaxCoastStep) so a single big warp dt is never fed to
            // RK4 in one shot — this bounds per-step travel, keeps SOI/dominant-body
            // re-evaluation timely, and lets surface-impact be checked each sub-step.
            // While the active vessel is thrusting, tighten the sub-step so a powered burn under
            // warp integrates accurately (thrust + gravity) and matches a real-time burn.
            bool thrusting = ActiveVessel is { Throttle: > 0.01 };
            double cap = thrusting ? MaxThrustStep : MaxCoastStep;
            double remaining = simDelta;
            while (remaining > 1e-12)
            {
                double step = System.Math.Min(remaining, cap);
                TickPhysicsMixed(step);
                CurrentTime += step;
                remaining   -= step;
            }
        }
        else
        {
            // Pure rails: everything propagated analytically
            TickRails(simDelta);
            CurrentTime += simDelta;
        }
    }

    // ── Integration modes ─────────────────────────────────────────────────

    private void TickPhysics(double dt)
    {
        // 1. Propagate celestial bodies on Keplerian rails
        KeplerPropagator.PropagateAllBodies(_bodies, CurrentTime + dt);

        // 2. Integrate each active vessel with RK4
        foreach (var vessel in _vessels)
        {
            if (vessel.IsDestroyed) continue; // frozen at crash point

            if (vessel.IsGroundHeld)
            {
                // Vessel is clamped to the body surface — follow the body's orbit
                var heldBody = GetDominantBody(vessel.Position);
                vessel.Position = heldBody.Position + vessel.GroundNormal * (heldBody.Radius + vessel.GroundOffset);
                vessel.Velocity = heldBody.Velocity + heldBody.GetSurfaceVelocity(vessel.Position);
                vessel.Tick(dt, heldBody);  // still drain fuel during ignition sequence
                continue;
            }

            if (vessel.IsOnRails)
            {
                PropagateVesselOnRails(vessel, dt);
                continue;
            }

            var refBody = GetDominantBody(vessel.Position);

            // Internal vessel tick (resource drain, SAS, crew EVA, etc.)
            vessel.Tick(dt, refBody);

            // RK4 orbit integration.
            // The acceleration is evaluated at each RK4 sub-step's (pos, vel) — NOT the
            // vessel's stored state — so the higher-order stages k₂…k₄ are meaningful.
            // (Celestial body positions are held fixed across the sub-step, a standard
            // simplification given dt ≤ 0.02 s.)
            (vessel.Position, vessel.Velocity) = RK4Integrator.StepPosVel(
                vessel.Position,
                vessel.Velocity,
                CurrentTime,
                dt,
                (pos, vel, _) => vessel.ComputeNetAccelerationAt(pos, vel, _bodies, refBody)
            );

            // ── Structural loads & thermal ────────────────────────────────
            var netAccel  = vessel.ComputeNetAcceleration(_bodies, refBody);
            var gravAccel = vessel.ComputeGravity(_bodies);
            var nonGrav   = netAccel - gravAccel;   // the g-force the vessel "feels"

            Physics.StressSolver.ComputeLoads(vessel.Parts, nonGrav, vessel.Orientation);
            // FindBreakingJoints returns joints whose load has exceeded their tolerance.
            // A full implementation would split the vessel here.
            _ = Physics.StressSolver.FindBreakingJoints(vessel.Parts).ToList();

            // Aerodynamic heating (orientation-aware re-entry).
            // El flujo de aire incide según la dirección de avance relativa a la superficie;
            // en el marco local del vessel determina si el escudo ventral (cara -Y) lo encara.
            // Si el escudo va mal orientado, la cara desnuda se quema y la pieza supera su
            // tolerancia → la nave se destruye (decisión de Universe, dueño de IsDestroyed).
            double density = refBody.GetAtmosphericDensity(vessel.Position);
            if (density > 0.0 && !vessel.IsGroundHeld)
            {
                var    surfVel   = vessel.GetSurfaceVelocity(refBody);
                double airspeed  = surfVel.Magnitude;
                double heatFlux  = Physics.ThermalModel.ComputeHeatFlux(density, airspeed);

                // Airflow direction in the vessel's local frame (orientation⁻¹ · flowDir).
                var flowDirLocal = airspeed > 1e-6
                    ? vessel.Orientation.Inverse().Rotate(surfVel.Normalized)
                    : Vector3d.Zero;

                var burned = Physics.StressSolver.ApplyThermalLoads(
                    vessel.Parts, heatFlux, dt, flowDirLocal);

                if (burned.Count > 0 && !vessel.IsDestroyed)
                {
                    // Burn-through: the vessel disintegrates in the airstream where it is —
                    // no surface clamp (this is an atmospheric break-up, not a ground impact).
                    vessel.IsDestroyed      = true;
                    vessel.DestructionCause = VesselDestructionCause.ThermalBreakup;
                    vessel.CrashImpactSpeed = airspeed;
                    vessel.CrashSimPosition = vessel.Position;
                }
            }

            // ── Surface impact detection ──────────────────────────────────
            double altitude = refBody.GetAltitude(vessel.Position);
            if (altitude < 0.0)
            {
                // Speed relative to the rotating surface at the impact point.
                var    surfVel2    = vessel.GetSurfaceVelocity(refBody);
                double impactSpeed = surfVel2.Magnitude;

                // Soft landing permission:
                //   (a) vessel is ground-held (hold-down clamps on the pad), OR
                //   (b) very low touch-down speed (≤ SoftLandingThreshold) — genuine EDL soft landing.
                // Everything else → hard impact → destroy.
                // Rationale: orbital re-entry ≥ hundreds of m/s; any accidental subsurface
                // penetration at those speeds must NOT silently bounce the vessel back to orbit.
                bool softLanding = vessel.IsGroundHeld
                    || impactSpeed <= SoftLandingThreshold;

                if (softLanding)
                {
                    // Clamp gently to the surface — vessel comes to rest on the
                    // rotating body without accumulating subsurface penetration.
                    var dir = (vessel.Position - refBody.Position).Normalized;
                    vessel.Position = refBody.Position + dir * (refBody.Radius + 1.0);
                    // Velocity = body orbital velocity + surface rotation at landing site
                    // (setting inertial zero would leave a spurious ~7.9 km/s residual).
                    vessel.Velocity = refBody.Velocity + refBody.GetSurfaceVelocity(vessel.Position);
                }
                else
                {
                    // Hard crash: cualquier velocidad > SoftLandingThreshold → destrucción.
                    // No soft-rest, no rebote a órbita.
                    vessel.IsDestroyed      = true;
                    vessel.DestructionCause = VesselDestructionCause.GroundImpact;
                    vessel.CrashImpactSpeed = impactSpeed;
                    vessel.CrashSimPosition = vessel.Position;
                    // Freeze the wreckage on the surface so the camera has a reference point.
                    var dir = (vessel.Position - refBody.Position).Normalized;
                    vessel.Position = refBody.Position + dir * (refBody.Radius + 0.5);
                    vessel.Velocity = refBody.Velocity + refBody.GetSurfaceVelocity(vessel.Position);
                }
            }
        }
    }

    private void TickPhysicsMixed(double dt)
    {
        // All celestial bodies on rails
        KeplerPropagator.PropagateAllBodies(_bodies, CurrentTime + dt);

        foreach (var vessel in _vessels)
        {
            if (vessel.IsDestroyed) continue; // frozen at crash point

            if (vessel == ActiveVessel)
            {
                var refBody = GetDominantBody(vessel.Position);

                // Decide whether the active vessel should be on rails this step.
                // Conditions: high time-warp AND coasting (throttle ≈ 0) AND above atmosphere.
                // When throttle > 0.01 the vessel exits rails immediately (≤ 1 sub-step latency)
                // so the next RK4 step picks up the thrust correctly.
                bool shouldBeOnRails = TimeScale >= 10.0
                    && vessel.Throttle < 0.01
                    && refBody.GetAtmosphericDensity(vessel.Position) < 0.01;

                if (shouldBeOnRails && !vessel.IsOnRails)
                {
                    vessel.IsOnRails    = true;
                    vessel.OrbitalState = null; // will be computed in PropagateVesselOnRails
                }
                else if (!shouldBeOnRails && vessel.IsOnRails)
                {
                    vessel.IsOnRails    = false;
                    vessel.OrbitalState = null;
                }

                if (vessel.IsOnRails)
                {
                    PropagateVesselOnRails(vessel, dt);
                }
                else
                {
                    vessel.Tick(dt, refBody);
                    (vessel.Position, vessel.Velocity) = RK4Integrator.StepPosVel(
                        vessel.Position,
                        vessel.Velocity,
                        CurrentTime,
                        dt,
                        (pos, vel, _) => vessel.ComputeNetAccelerationAt(pos, vel, _bodies, refBody)
                    );

                    // Surface impact: keep the vessel from sinking through the body during warp.
                    double altitude = refBody.GetAltitude(vessel.Position);
                    if (altitude < 0.0)
                    {
                        var    surfVelMixed    = vessel.GetSurfaceVelocity(refBody);
                        double impactSpeedMixed = surfVelMixed.Magnitude;

                        bool softLandingMixed = vessel.IsGroundHeld
                            || impactSpeedMixed <= SoftLandingThreshold;

                        if (softLandingMixed)
                        {
                            var dir = (vessel.Position - refBody.Position).Normalized;
                            vessel.Position = refBody.Position + dir * (refBody.Radius + 1.0);
                            vessel.Velocity = refBody.Velocity + refBody.GetSurfaceVelocity(vessel.Position);
                        }
                        else
                        {
                            // Hard crash under warp — destroy, no rebound.
                            vessel.IsDestroyed      = true;
                            vessel.DestructionCause = VesselDestructionCause.GroundImpact;
                            vessel.CrashImpactSpeed = impactSpeedMixed;
                            vessel.CrashSimPosition = vessel.Position;
                            var dir = (vessel.Position - refBody.Position).Normalized;
                            vessel.Position = refBody.Position + dir * (refBody.Radius + 0.5);
                            vessel.Velocity = refBody.Velocity + refBody.GetSurfaceVelocity(vessel.Position);
                        }
                    }
                }
            }
            else
            {
                PropagateVesselOnRails(vessel, dt);
            }
        }
    }

    private void TickRails(double dt)
    {
        KeplerPropagator.PropagateAllBodies(_bodies, CurrentTime + dt);
        foreach (var vessel in _vessels)
            PropagateVesselOnRails(vessel, dt);
    }

    // ── Vessel on-rails propagation ───────────────────────────────────────

    private void PropagateVesselOnRails(Vessel vessel, double dt)
    {
        // Compute or reuse cached orbital elements.
        // CRITICAL: the global bodies were already propagated to the tick's END time by
        // PropagateAllBodies before this runs, but vessel.Position/Velocity still correspond
        // to CurrentTime (the conic's epoch). Build the relative state against the reference
        // body's state AT CurrentTime (via BodyStateAt) — using its end-of-tick position would
        // bias the initial conic by (body velocity × dt), which at high warp (dt up to 2000 s)
        // is tens of thousands of km — a wrong orbit the instant warp is engaged.
        if (vessel.OrbitalState is null)
        {
            var refBody      = GetDominantBodyAt(vessel.Position, CurrentTime);
            var (refP, refV) = BodyStateAt(refBody, CurrentTime);
            var relPos       = vessel.Position - refP;
            var relVel       = vessel.Velocity - refV;
            vessel.OrbitalState    = KeplerPropagator.ComputeElements(
                relPos, relVel, refBody.GM, refBody.Id, CurrentTime);
            vessel.ReferenceBodyId = refBody.Id;
        }

        var reference = GetBody(vessel.OrbitalState.ReferenceBodyId);
        if (reference is null) return;

        // ── Patched-conic SOI transition guard (pre-step) ─────────────────
        // The vessel may have drifted into (or out of) another body's sphere of
        // influence since the cached conic was last computed against `reference`.
        // If the dominant body changed, re-frame the state to it and recompute the
        // conic there BEFORE propagating, so this step's arc is integrated in the
        // correct frame. GetDominantBody works on the inertial position — identical
        // in every frame — so re-framing preserves inertial continuity (no jump in
        // absolute position/velocity, only the orbital elements change).
        // vessel.Position corresponds to CurrentTime (end of the previous tick); evaluate the
        // dominant body and re-frame against the body state at THAT instant, not end-of-tick.
        var dominantNow = GetDominantBodyAt(vessel.Position, CurrentTime);
        if (dominantNow.Id != vessel.OrbitalState!.ReferenceBodyId)
        {
            var (bp, bv) = BodyStateAt(dominantNow, CurrentTime);
            ReframeVesselToBody(vessel, dominantNow, bp, bv, CurrentTime);
            reference = dominantNow;
        }

        // ── Periapsis / sub-surface collision check (on-rails) ────────────
        // The Kepler propagator works in conic sections and cannot detect when the
        // orbital arc dips below the surface mid-step — the vessel would silently
        // "tunnel through" the planet and reappear on the other side.
        //
        // Guard: if the conic is suborbital (periapsis below the body radius) OR the
        // trajectory is radial (h≈0 — a straight fall/lob with no well-defined orbit),
        // the path already intersects the surface. Destroy the vessel immediately
        // instead of propagating a physically impossible trajectory.
        //
        // IsSuborbital() uses the true periapsis radius (valid for elliptic, hyperbolic
        // AND radial conics). The old a*(1-e) test hid radial/hyperbolic cases where the
        // numbers sign-flip into a misleading large-positive periapsis — that was the root
        // of the "rocket exits orbit straight through the planet" bug under warp.
        // Only a RADIAL conic (h≈0, degenerate) must be resolved here — it cannot be
        // propagated by Kepler at all. A normal suborbital ELLIPSE (e.g. a vessel that
        // lowered its periapsis to deorbit) is valid and is left to the per-slice surface
        // check below: it coasts down realistically and only impacts when it actually reaches
        // the surface, so the atmosphere/EDL can fly the reentry. Destroying it the instant
        // its periapsis dips below the radius — while still at apoapsis hundreds of km up —
        // made reentry under warp impossible.
        if (vessel.OrbitalState.IsRadial)
        {
            var (refP0, refV0) = BodyStateAt(reference, CurrentTime);
            ResolveOnRailsImpact(vessel, reference, refP0, refV0);
            return;
        }

        // ── Sub-step sampling so high warp cannot skip below the surface ───
        // Even with a periapsis above the surface, a single large warp step could place
        // the sampled point on the far side of a tight periapsis pass without ever
        // "seeing" the dip. Walk the step in bounded slices (≤ MaxCoastStep) and check the
        // propagated radius at each sample. If any sample is below the surface, the arc
        // grazes the body within this step → resolve the impact at that point, never skip it.
        double remaining   = dt;
        double sampleTime  = CurrentTime;
        Vector3d lastRelP  = vessel.Position - reference.Position;
        Vector3d lastRelV  = vessel.Velocity - reference.Velocity;
        while (remaining > 1e-9)
        {
            double slice = System.Math.Min(remaining, MaxCoastStep);
            sampleTime  += slice;
            remaining   -= slice;

            (lastRelP, lastRelV) = KeplerPropagator.PropagateToTime(
                vessel.OrbitalState, sampleTime, reference.GM);

            // Reference body inertial state at THIS sub-step time (bodies are globally
            // frozen at end-of-tick; the crossing/impact happens earlier).
            var (refPosAt, refVelAt) = BodyStateAt(reference, sampleTime);

            if (lastRelP.Magnitude < reference.Radius)
            {
                // The conic crosses the surface inside this step — impact here.
                vessel.Position = refPosAt + lastRelP;
                vessel.Velocity = refVelAt + lastRelV;
                ResolveOnRailsImpact(vessel, reference, refPosAt, refVelAt);
                return;
            }

            // ── Mid-step SOI crossing (patched-conic) ─────────────────────
            // The slice may have carried the vessel across an SOI boundary (e.g.
            // into the Moon's SOI, or out of Earth's into the Sun's). Resolve the
            // dominant body at the propagated inertial point. If it changed, commit
            // the inertial state here, re-frame to the new dominant body with the
            // conic's epoch set to THIS crossing time (sampleTime) — so the remaining
            // slices, sampled at absolute times, stay phase-correct — and keep
            // sub-stepping the rest of the step in the new conic. This walks the
            // boundary instead of tunnelling through it under warp.
            // Reconstruct the inertial crossing point with the reference body at the SAME
            // instant (sampleTime), and decide the dominant body in that same frame.
            var inertialP = refPosAt + lastRelP;
            var inertialV = refVelAt + lastRelV;
            var dominantHere = GetDominantBodyAt(inertialP, sampleTime);
            if (dominantHere.Id != reference.Id)
            {
                vessel.Position = inertialP;
                vessel.Velocity = inertialV;
                var (newRefP, newRefV) = BodyStateAt(dominantHere, sampleTime);
                ReframeVesselToBody(vessel, dominantHere, newRefP, newRefV, sampleTime);
                reference = dominantHere;

                // Only a radial (degenerate) conic must be resolved here; a suborbital ellipse
                // in the new frame coasts down and is caught by the per-slice surface check.
                if (vessel.OrbitalState!.IsRadial)
                {
                    ResolveOnRailsImpact(vessel, reference, newRefP, newRefV);
                    return;
                }

                // Re-anchor the per-slice state from the NEW conic at this crossing time,
                // so a crossing on the final slice still reconstructs consistently below.
                (lastRelP, lastRelV) = KeplerPropagator.PropagateToTime(
                    vessel.OrbitalState!, sampleTime, reference.GM);
            }
        }

        vessel.Position = reference.Position + lastRelP;
        vessel.Velocity = reference.Velocity + lastRelV;
    }

    /// <summary>
    /// Re-frames an on-rails vessel onto a new dominant body, recomputing its conic
    /// in that body's frame from the SAME inertial state. The vessel's absolute
    /// (inertial) position and velocity are unchanged — only the reference body and
    /// the derived Keplerian elements change — so the trajectory is continuous across
    /// the sphere-of-influence boundary (a patched-conic transition).
    /// </summary>
    /// <remarks>
    /// Caller must have set <see cref="Vessel.Position"/>/<see cref="Vessel.Velocity"/>
    /// to the inertial state at the crossing point, and pass the new body's inertial
    /// state <paramref name="bodyPos"/>/<paramref name="bodyVel"/> AT THE SAME instant
    /// (<paramref name="epoch"/>). The global body objects are frozen at the tick's end
    /// time during sub-stepping, so the crossing-time body state must be supplied
    /// explicitly (see <see cref="BodyStateAt"/>) — otherwise the relative state is
    /// computed against the wrong frame and a spurious inertial jump appears under warp.
    /// <paramref name="epoch"/> is the simulation time the inertial state corresponds to;
    /// the recomputed conic stores its mean anomaly at that epoch so subsequent
    /// propagation to absolute times stays phase-correct.
    /// </remarks>
    private static void ReframeVesselToBody(
        Vessel vessel, CelestialBody newBody, Vector3d bodyPos, Vector3d bodyVel, double epoch)
    {
        var relPos = vessel.Position - bodyPos;
        var relVel = vessel.Velocity - bodyVel;

        vessel.OrbitalState    = KeplerPropagator.ComputeElements(
            relPos, relVel, newBody.GM, newBody.Id, epoch);
        vessel.ReferenceBodyId = newBody.Id;
    }

    /// <summary>
    /// Inertial state (position, velocity) of a body at an arbitrary simulation time,
    /// WITHOUT mutating global body state. Mirrors <c>KeplerPropagator.PropagateAllBodies</c>
    /// by walking the reference chain (Moon → Earth → Sun); a root body with no orbital
    /// elements is treated as fixed at its current stored position.
    ///
    /// Needed because the global bodies are propagated once to the tick's END time, but
    /// an on-rails SOI crossing happens at an intermediate sub-step time — the reference
    /// body's state at THAT instant is what keeps the patched-conic re-frame continuous.
    /// </summary>
    private (Vector3d pos, Vector3d vel) BodyStateAt(CelestialBody body, double t)
    {
        if (body.OrbitalElements is null)
            return (body.Position, body.Velocity);   // root (e.g. Sun) — fixed at origin

        var refBody = GetBody(body.OrbitalElements.ReferenceBodyId);
        if (refBody is null)
            return (body.Position, body.Velocity);

        var (refPos, refVel)  = BodyStateAt(refBody, t);
        var (relPos, relVel)  = body.OrbitalElements.GetStateAtTime(t, refBody.GM);
        return (refPos + relPos, refVel + relVel);
    }

    /// <summary>
    /// Like <see cref="GetDominantBody"/> but evaluates each body's position at time
    /// <paramref name="t"/> (via <see cref="BodyStateAt"/>) instead of its frozen end-of-tick
    /// position — so an SOI boundary test during sub-stepping is decided in the right frame.
    /// </summary>
    private CelestialBody GetDominantBodyAt(Vector3d position, double t)
    {
        CelestialBody? best    = null;
        double         bestSoi = double.MaxValue;

        foreach (var body in _bodies)
        {
            var (bp, _) = BodyStateAt(body, t);
            double dist = (position - bp).Magnitude;
            if (dist < body.SphereOfInfluence && body.SphereOfInfluence < bestSoi)
            {
                bestSoi = body.SphereOfInfluence;
                best    = body;
            }
        }

        return best ?? _bodies.OrderByDescending(b => b.Mass).First();
    }

    /// <summary>
    /// Resolves a certain surface impact for an on-rails vessel: marks it destroyed,
    /// records the surface-relative impact speed, clamps the wreck to the surface for the
    /// renderer, and drops it off rails. The vessel reenters and is destroyed — it never
    /// bounces back to orbit or tunnels through the body (R16 user decision).
    /// </summary>
    /// <remarks>
    /// <paramref name="refPos"/>/<paramref name="refVel"/> are the reference body's inertial
    /// state AT THE IMPACT INSTANT (the bodies are globally frozen at the tick end, so under
    /// warp the body has moved tens of thousands of km since the impact). They are used for the
    /// impact-relative speed and the radial direction, so the wreck is clamped on the correct
    /// side of the body (using the body's stale end-of-tick position for the direction could
    /// put it on the far side at high warp). The surface clamp itself uses the body's current
    /// position, since that is where the body renders this frame.
    /// </remarks>
    private void ResolveOnRailsImpact(
        Vessel vessel, CelestialBody reference, Vector3d refPos, Vector3d refVel)
    {
        var    relP0       = vessel.Position - refPos;
        var    relV0       = vessel.Velocity - refVel;
        double impactSpeed = relV0.Magnitude; // conservative: full orbital speed

        vessel.IsDestroyed      = true;
        vessel.DestructionCause = VesselDestructionCause.GroundImpact;
        vessel.CrashImpactSpeed = impactSpeed;
        vessel.CrashSimPosition = vessel.Position;

        // Clamp to the body's current surface for the renderer, along the true impact direction.
        var dir = relP0.Magnitude > 0.0 ? relP0.Normalized : Vector3d.Up;
        vessel.Position = reference.Position + dir * (reference.Radius + 0.5);
        vessel.Velocity = reference.Velocity + reference.GetSurfaceVelocity(vessel.Position);

        // Force off rails so the destroyed state is visible immediately.
        vessel.IsOnRails    = false;
        vessel.OrbitalState = null;
    }

    // ── Factory ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates and populates a <see cref="Universe"/> from a data directory.
    /// Expects a <c>bodies/</c> sub-directory containing <c>*.json</c> body files.
    /// </summary>
    /// <param name="dataDir">Root data directory (e.g. <c>res://data</c>).</param>
    public static Universe LoadFromDataDirectory(string dataDir)
    {
        var universe  = new Universe();
        var bodiesDir = System.IO.Path.Combine(dataDir, "bodies");
        var bodies    = CelestialBody.LoadAllFromDirectory(bodiesDir);

        // Add the root body (Sun) first so it sits at the inertial origin
        if (bodies.TryGetValue("sun", out var sun))
        {
            sun.Position = Vector3d.Zero;
            sun.Velocity = Vector3d.Zero;
            universe.AddBody(sun);
        }

        // Add all other bodies, initialising position from orbital elements at t = 0
        foreach (var (id, body) in bodies)
        {
            if (id == "sun") continue;

            if (body.OrbitalElements is not null)
            {
                var refId = body.OrbitalElements.ReferenceBodyId;
                if (bodies.TryGetValue(refId, out var refBody))
                {
                    var (pos, vel) = body.OrbitalElements.GetStateAtTime(0.0, refBody.GM);
                    body.Position  = refBody.Position + pos;
                    body.Velocity  = refBody.Velocity + vel;
                }
            }

            universe.AddBody(body);
        }

        return universe;
    }
}
