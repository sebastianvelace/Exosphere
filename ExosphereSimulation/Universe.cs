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

            // Aerodynamic heating
            double density = refBody.GetAtmosphericDensity(vessel.Position);
            if (density > 0.0)
            {
                var    surfVel   = vessel.GetSurfaceVelocity(refBody);
                double airspeed  = surfVel.Magnitude;
                double heatFlux  = Physics.ThermalModel.ComputeHeatFlux(density, airspeed);
                Physics.StressSolver.ApplyThermalLoads(vessel.Parts, heatFlux, dt);
            }

            // ── Surface impact detection ──────────────────────────────────
            double altitude = refBody.GetAltitude(vessel.Position);
            if (altitude < 0.0)
            {
                // Calculate impact speed relative to the rotating surface
                var surfVel2     = vessel.GetSurfaceVelocity(refBody);
                double impactSpeed = surfVel2.Magnitude;

                bool isHardImpact = impactSpeed > 12.0 && !vessel.IsGroundHeld;

                if (isHardImpact)
                {
                    // Hard crash: freeze the vessel at the impact point
                    vessel.IsDestroyed     = true;
                    vessel.CrashImpactSpeed  = impactSpeed;
                    vessel.CrashSimPosition  = vessel.Position;
                    var dir = (vessel.Position - refBody.Position).Normalized;
                    vessel.Position = refBody.Position + dir * (refBody.Radius + 0.5);
                    vessel.Velocity = refBody.Velocity + refBody.GetSurfaceVelocity(vessel.Position);
                }
                else
                {
                    // Soft-rest: controlled landing — come to rest on the rotating surface
                    var dir = (vessel.Position - refBody.Position).Normalized;
                    vessel.Position = refBody.Position + dir * (refBody.Radius + 1.0);
                    // Come to rest relative to the ROTATING surface, not the inertial frame —
                    // setting absolute zero would leave the body's full orbital velocity as
                    // the vessel's apparent surface speed (a spurious ~24 km/s spike).
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

                // Decide whether the active vessel should be on rails this step
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
                        var surfVelMixed  = vessel.GetSurfaceVelocity(refBody);
                        double impactSpeedMixed = surfVelMixed.Magnitude;
                        bool isHardImpactMixed  = impactSpeedMixed > 12.0 && !vessel.IsGroundHeld;

                        if (isHardImpactMixed)
                        {
                            vessel.IsDestroyed      = true;
                            vessel.CrashImpactSpeed = impactSpeedMixed;
                            vessel.CrashSimPosition = vessel.Position;
                            var dir = (vessel.Position - refBody.Position).Normalized;
                            vessel.Position = refBody.Position + dir * (refBody.Radius + 0.5);
                            vessel.Velocity = refBody.Velocity + refBody.GetSurfaceVelocity(vessel.Position);
                        }
                        else
                        {
                            var dir = (vessel.Position - refBody.Position).Normalized;
                            vessel.Position = refBody.Position + dir * (refBody.Radius + 1.0);
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
        // Compute or reuse cached orbital elements
        if (vessel.OrbitalState is null)
        {
            var refBody = GetDominantBody(vessel.Position);
            var relPos  = vessel.Position - refBody.Position;
            var relVel  = vessel.Velocity - refBody.Velocity;
            vessel.OrbitalState    = KeplerPropagator.ComputeElements(
                relPos, relVel, refBody.GM, refBody.Id, CurrentTime);
            vessel.ReferenceBodyId = refBody.Id;
        }

        var reference = GetBody(vessel.OrbitalState.ReferenceBodyId);
        if (reference is null) return;

        var (relP, relV) = KeplerPropagator.PropagateToTime(
            vessel.OrbitalState, CurrentTime + dt, reference.GM);

        vessel.Position = reference.Position + relP;
        vessel.Velocity = reference.Velocity + relV;
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
