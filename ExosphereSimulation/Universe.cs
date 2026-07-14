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
    private const double MaxContactStep = 0.005;

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

        bool anyForceSensitive = _vessels.Any(RequiresBoundedWarpPropagation);
        bool anyContactSensitive = _bodies.Count > 0 && _vessels.Any(v =>
            v.HasDeployedLandingGear
            && GetDominantBody(v.Position).GetAltitude(v.Position) < 100.0);

        if (TimeScale <= 4.0)
        {
            // Full RK4 physics, capped at MaxPhysicsStep per sub-step
            double remaining = simDelta;
            while (remaining > 1e-12)
            {
                double step  = System.Math.Min(remaining,
                    anyContactSensitive ? MaxContactStep : MaxPhysicsStep);
                TickPhysics(step);
                CurrentTime += step;
                remaining   -= step;
            }
        }
        else if (TimeScale <= 1000.0 || anyForceSensitive)
        {
            // Mixed: active vessel uses RK4; all others go on rails.
            // Sub-step (capped at MaxCoastStep) so a single big warp dt is never fed to
            // RK4 in one shot — this bounds per-step travel, keeps SOI/dominant-body
            // re-evaluation timely, and lets surface-impact be checked each sub-step.
            // While the active vessel is thrusting, tighten the sub-step so a powered burn under
            // warp integrates accurately (thrust + gravity) and matches a real-time burn.
            bool thrusting = ActiveVessel is { Throttle: > 0.01 };
            bool forceSensitive = ActiveVessel != null && RequiresOffRailsPhysics(ActiveVessel);
            double cap = anyContactSensitive ? MaxContactStep
                       : thrusting ? MaxThrustStep
                       : forceSensitive ? MaxPhysicsStep
                       : MaxCoastStep;
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

    /// <summary>
    /// True when analytic Kepler rails would discard a force or event that materially changes
    /// the outcome: thrust/spool, atmospheric loads or heating, ground proximity/contact.
    /// </summary>
    public bool RequiresOffRailsPhysics(Vessel vessel)
    {
        if (vessel.IsDestroyed) return false;
        if (vessel.IsGroundHeld || vessel.Throttle > 1e-3
            || vessel.Parts.ActiveEngines.Any(e => e.ThrottleLevel > 1e-3))
            return true;
        if (_bodies.Count == 0) return false;

        var body = GetDominantBody(vessel.Position);
        double altitude = body.GetAltitude(vessel.Position);
        if (altitude < 1_000.0) return true; // airless landing/contact corridor too
        if (body.Atmosphere == null) return false;

        double density = body.GetAtmosphericDensity(vessel.Position);
        if (density <= 0.0) return false;

        // Residual thermosphere (R7) above MaxAltitude still exerts drag. Analytic rails
        // ignore that force, so low LEO would become immortal under warp ≥ 10. Keep RK4
        // while any modeled density remains below ThermosphereTopAltitude.
        double thermoTop = body.Atmosphere.ThermosphereTopAltitude;
        if (thermoTop > body.Atmosphere.MaxAltitude && altitude < thermoTop)
            return true;

        double speed = vessel.GetSurfaceVelocity(body).Magnitude;
        double q = 0.5 * density * speed * speed;
        double heatFlux = Physics.ThermalModel.ComputeHeatFlux(
            density, speed, System.Math.Max(0.1, vessel.MaximumDiameter * 0.5));
        return altitude <= body.Atmosphere.MaxAltitude * 1.05
            || q >= 0.5
            || heatFlux >= 500.0;
    }

    /// <summary>
    /// Also guards a coasting conic whose periapsis will enter the atmosphere. It may stay
    /// on rails while high, but warp must use bounded slices so the atmosphere boundary is
    /// detected instead of jumping across the entire entry in one analytic step.
    /// </summary>
    public bool RequiresBoundedWarpPropagation(Vessel vessel)
    {
        if (RequiresOffRailsPhysics(vessel)) return true;
        if (_bodies.Count == 0 || vessel.IsDestroyed) return false;
        var body = GetDominantBody(vessel.Position);
        if (body.Atmosphere == null) return false;

        try
        {
            var state = vessel.OrbitalState ?? OrbitalElements.FromStateVector(
                vessel.Position - body.Position,
                vessel.Velocity - body.Velocity,
                body.GM,
                body.Id,
                CurrentTime);
            return state.Periapsis <= body.Radius + body.Atmosphere.MaxAltitude * 1.05;
        }
        catch (ArgumentException)
        {
            return true; // degenerate/suborbital state: choose bounded physics safely
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

            if (vessel.IsSurfaceSettled)
            {
                var settledBody = GetDominantBody(vessel.Position);
                if (vessel.Throttle > 0.01 || !vessel.HasDeployedLandingGear)
                {
                    vessel.IsSurfaceSettled = false;
                    vessel.SurfaceSettledDuration = 0.0;
                }
                else
                {
                    AdvanceGroundHoldFrame(vessel, settledBody, dt);
                    vessel.Position = settledBody.Position
                        + vessel.GroundNormal * (settledBody.Radius + vessel.GroundOffset);
                    vessel.Velocity = settledBody.Velocity
                        + settledBody.GetSurfaceVelocity(vessel.Position);
                    vessel.AngularVelocity = Vector3d.Zero;
                    vessel.PitchYawRoll = Vector3d.Zero;
                    vessel.IsOnRails = false;
                    vessel.Tick(dt, settledBody);
                    continue;
                }
            }

            if (vessel.IsGroundHeld)
            {
                // Vessel is clamped to the body surface — follow the body's orbit
                var heldBody = GetDominantBody(vessel.Position);
                AdvanceGroundHoldFrame(vessel, heldBody, dt);
                vessel.Position = heldBody.Position + vessel.GroundNormal * (heldBody.Radius + vessel.GroundOffset);
                vessel.Velocity = heldBody.Velocity + heldBody.GetSurfaceVelocity(vessel.Position);
                vessel.Tick(dt, heldBody);  // still drain fuel during ignition sequence
                continue;
            }

            if (vessel.IsOnRails)
            {
                if (RequiresOffRailsPhysics(vessel))
                {
                    vessel.IsOnRails = false;
                    vessel.OrbitalState = null;
                }
                else
                {
                    PropagateVesselOnRails(vessel, dt);
                    continue;
                }
            }

            var refBody = GetDominantBody(vessel.Position);

            IntegrateVesselOffRails(vessel, refBody, dt);
        }
    }

    private static void AdvanceGroundHoldFrame(Vessel vessel, CelestialBody body, double dt)
    {
        if (body.AngularSpeed == 0.0 || dt <= 0.0) return;
        var rotation = Math.Quaterniond.FromAxisAngle(
            body.RotationAxis, body.AngularSpeed * dt);
        vessel.GroundNormal = rotation.Rotate(vessel.GroundNormal).Normalized;
        vessel.Orientation = (rotation * vessel.Orientation).Normalize();
    }

    private void ApplyPostIntegrationPhysics(Vessel vessel, CelestialBody refBody, double dt)
    {
        var netAccel  = vessel.ComputeNetAcceleration(_bodies, refBody);
        var gravAccel = vessel.ComputeGravity(_bodies);
        var contactAccel = vessel.TotalMass > 0.0
            ? vessel.LastContactForceWorld / vessel.TotalMass
            : Vector3d.Zero;
        var nonGrav   = netAccel - gravAccel + contactAccel;
        Physics.StressSolver.ComputeLoads(vessel.Parts, nonGrav, vessel.Orientation);
        _ = Physics.StressSolver.FindBreakingJoints(vessel.Parts).ToList();

        double density = refBody.GetAtmosphericDensity(vessel.Position);
        if (density > 0.0 && !vessel.IsGroundHeld)
        {
            var surfVel = vessel.GetSurfaceVelocity(refBody);
            double airspeed = surfVel.Magnitude;
            double heatFlux = Physics.ThermalModel.ComputeHeatFlux(
                density, airspeed, System.Math.Max(0.1, vessel.MaximumDiameter * 0.5));
            var flowDirLocal = airspeed > 1e-6
                ? vessel.Orientation.Inverse().Rotate(surfVel.Normalized)
                : Vector3d.Zero;
            var burned = Physics.StressSolver.ApplyThermalLoads(
                vessel.Parts, heatFlux, dt, flowDirLocal);
            if (burned.Count > 0 && !vessel.IsDestroyed)
            {
                vessel.IsDestroyed      = true;
                vessel.DestructionCause = VesselDestructionCause.ThermalBreakup;
                vessel.CrashImpactSpeed = airspeed;
                vessel.CrashSimPosition = vessel.Position;
            }
        }

        HandleSurfaceImpact(vessel, refBody);
    }

    private static void HandleSurfaceImpact(Vessel vessel, CelestialBody refBody)
    {
        if (refBody.GetAltitude(vessel.Position) >= 0.0) return;

        double impactSpeed = vessel.GetSurfaceVelocity(refBody).Magnitude;
        bool softLanding = vessel.IsGroundHeld || impactSpeed <= SoftLandingThreshold;
        var dir = (vessel.Position - refBody.Position).Normalized;
        if (softLanding)
        {
            vessel.Position = refBody.Position + dir * (refBody.Radius + 1.0);
            vessel.Velocity = refBody.Velocity + refBody.GetSurfaceVelocity(vessel.Position);
        }
        else
        {
            vessel.IsDestroyed      = true;
            vessel.DestructionCause = VesselDestructionCause.GroundImpact;
            vessel.CrashImpactSpeed = impactSpeed;
            vessel.CrashSimPosition = vessel.Position;
            vessel.Position = refBody.Position + dir * (refBody.Radius + 0.5);
            vessel.Velocity = refBody.Velocity + refBody.GetSurfaceVelocity(vessel.Position);
        }
    }

    private void TickPhysicsMixed(double dt)
    {
        // All celestial bodies on rails
        KeplerPropagator.PropagateAllBodies(_bodies, CurrentTime + dt);

        foreach (var vessel in _vessels)
        {
            if (vessel.IsDestroyed) continue; // frozen at crash point

            var refBody = GetDominantBody(vessel.Position);
            if (vessel.IsSurfaceSettled)
            {
                // Rigid-body sleep for a landed vehicle.  This is not a launch
                // clamp: any commanded thrust wakes the solver immediately.
                if (vessel.Throttle > 0.01 || !vessel.HasDeployedLandingGear)
                {
                    vessel.IsSurfaceSettled = false;
                    vessel.SurfaceSettledDuration = 0.0;
                }
                else
                {
                    AdvanceGroundHoldFrame(vessel, refBody, dt);
                    vessel.Position = refBody.Position
                        + vessel.GroundNormal * (refBody.Radius + vessel.GroundOffset);
                    vessel.Velocity = refBody.Velocity
                        + refBody.GetSurfaceVelocity(vessel.Position);
                    vessel.AngularVelocity = Vector3d.Zero;
                    vessel.PitchYawRoll = Vector3d.Zero;
                    vessel.IsOnRails = false;
                    vessel.Tick(dt, refBody);
                    continue;
                }
            }
            if (vessel.IsGroundHeld)
            {
                AdvanceGroundHoldFrame(vessel, refBody, dt);
                vessel.Position = refBody.Position
                    + vessel.GroundNormal * (refBody.Radius + vessel.GroundOffset);
                vessel.Velocity = refBody.Velocity + refBody.GetSurfaceVelocity(vessel.Position);
                vessel.IsOnRails = false;
                vessel.Tick(dt, refBody);
                continue;
            }

            bool requiresForces = RequiresOffRailsPhysics(vessel);

            if (vessel == ActiveVessel)
            {
                // Decide whether the active vessel should be on rails this step.
                // Conditions: high time-warp AND coasting (throttle ≈ 0) AND above atmosphere.
                // When throttle > 0.01 the vessel exits rails immediately (≤ 1 sub-step latency)
                // so the next RK4 step picks up the thrust correctly.
                bool shouldBeOnRails = TimeScale >= 10.0
                    && !requiresForces;

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
                    IntegrateVesselOffRails(vessel, refBody, dt);
                }
            }
            else if (requiresForces)
            {
                vessel.IsOnRails = false;
                vessel.OrbitalState = null;
                IntegrateVesselOffRails(vessel, refBody, dt);
            }
            else
            {
                PropagateVesselOnRails(vessel, dt);
            }
        }
    }

    private void IntegrateVesselOffRails(Vessel vessel, CelestialBody refBody, double dt)
    {
        var contactBefore = EvaluateLandingContact(vessel, refBody, vessel.Position, vessel.Velocity);
        vessel.LastContactForceWorld = contactBefore?.ForceWorld ?? Vector3d.Zero;
        vessel.LastContactTorqueWorld = contactBefore?.TorqueWorld ?? Vector3d.Zero;

        vessel.Tick(dt, refBody, vessel.LastContactTorqueWorld);
        (vessel.Position, vessel.Velocity) = RK4Integrator.StepPosVel(
            vessel.Position,
            vessel.Velocity,
            CurrentTime,
            dt,
            (pos, vel, _) =>
            {
                var stageContact = EvaluateLandingContact(vessel, refBody, pos, vel);
                var contactAcceleration = vessel.TotalMass > 0.0
                    ? (stageContact?.ForceWorld ?? Vector3d.Zero) / vessel.TotalMass
                    : Vector3d.Zero;
                return vessel.ComputeNetAccelerationAt(pos, vel, _bodies, refBody)
                    + contactAcceleration;
            });

        var contactAfter = EvaluateLandingContact(vessel, refBody, vessel.Position, vessel.Velocity);
        UpdateLandingContactState(vessel, refBody, contactAfter, dt);
        ApplyPostIntegrationPhysics(vessel, refBody, dt);
    }

    private static Physics.ContactWrench? EvaluateLandingContact(
        Vessel vessel, CelestialBody body, Vector3d position, Vector3d velocity)
    {
        if (!vessel.HasDeployedLandingGear || vessel.LandingContactPoints.Count == 0)
            return null;
        var input = vessel.GetContactInput(position, velocity);
        return Physics.SurfaceContactSolver.EvaluateSphere(
            input, vessel.LandingContactPoints, body);
    }

    private static void UpdateLandingContactState(
        Vessel vessel, CelestialBody body, Physics.ContactWrench? contact, double dt)
    {
        vessel.LastSurfaceContact = contact;
        vessel.LastContactForceWorld = contact?.ForceWorld ?? Vector3d.Zero;
        vessel.LastContactTorqueWorld = contact?.TorqueWorld ?? Vector3d.Zero;

        if (contact == null || contact.ContactCount == 0)
        {
            vessel.SurfaceSettledDuration = 0.0;
            vessel.IsSurfaceSettled = false;
            return;
        }

        // Landing-leg joints dissipate residual pitch/yaw once the load is shared by at
        // least three feet. This is passive structural damping, not an attitude snap: the
        // integrated angular velocity decays continuously while contact torque remains free
        // to tip an actually unstable vehicle.
        if (contact.ContactCount >= 3)
            vessel.AngularVelocity *= System.Math.Exp(-8.0 * dt);

        double impactSpeed = vessel.GetSurfaceVelocity(body).Magnitude;
        // Bottom-out remains diagnostic until a non-linear bump-stop/primary-structure load
        // path exists. The penalty force is not capped, so the declared ultimate leg load is
        // the physical failure gate instead of an arbitrary penetration epsilon.
        if (contact.HasOverload)
        {
            vessel.IsDestroyed = true;
            vessel.DestructionCause = VesselDestructionCause.GroundImpact;
            vessel.CrashImpactSpeed = impactSpeed;
            vessel.CrashSimPosition = vessel.Position;
            vessel.SurfaceSettledDuration = 0.0;
            vessel.IsSurfaceSettled = false;
            return;
        }

        var normal = (vessel.Position - body.Position).Normalized;
        var surfaceVelocity = vessel.GetSurfaceVelocity(body);
        double normalSpeed = System.Math.Abs(surfaceVelocity.Dot(normal));
        double tangentialSpeed = (surfaceVelocity - normal * surfaceVelocity.Dot(normal)).Magnitude;
        double upright = vessel.Orientation.Rotate(Vector3d.Up).Normalized.Dot(normal);
        double localGravity = body.GetGravityAt(vessel.Position).Magnitude;
        double normalSupportAcceleration = vessel.TotalMass > 0.0
            ? contact.ForceWorld.Dot(normal) / vessel.TotalMass
            : 0.0;
        // Penalty contacts can retain a short-lived bump-stop preload after the
        // landing transient.  Low kinetic state sustained for 0.5 s is the
        // decisive sleep criterion; overload remains the separate hard failure
        // gate above, so removing the narrow 1.25 g ceiling cannot hide damage.
        bool adequatelySupported = localGravity > 0.0
            && normalSupportAcceleration > 0.65 * localGravity;
        bool settledNow = contact.ContactCount >= 3
            && normalSpeed < 0.25
            && tangentialSpeed < 0.50
            && vessel.AngularVelocity.Magnitude < 0.03
            && upright > System.Math.Cos(10.0 * MathUtils.DEG_TO_RAD)
            && adequatelySupported;
        vessel.SurfaceSettledDuration = settledNow
            ? vessel.SurfaceSettledDuration + dt
            : 0.0;
        vessel.IsSurfaceSettled = vessel.SurfaceSettledDuration >= 0.50;
        if (vessel.IsSurfaceSettled)
        {
            vessel.GroundNormal = normal;
            vessel.GroundOffset = body.GetAltitude(vessel.Position);
            vessel.Velocity = body.Velocity + body.GetSurfaceVelocity(vessel.Position);
            vessel.AngularVelocity = Vector3d.Zero;
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

        // Root body sits at the inertial origin. PropagateAllBodies recursively resolves
        // parents before children, independent of JSON/filesystem enumeration order.
        if (bodies.TryGetValue("sun", out var sun))
        {
            sun.Position = Vector3d.Zero;
            sun.Velocity = Vector3d.Zero;
        }
        KeplerPropagator.PropagateAllBodies(bodies.Values, 0.0);

        foreach (var body in bodies.Values)
            universe.AddBody(body);

        return universe;
    }
}
