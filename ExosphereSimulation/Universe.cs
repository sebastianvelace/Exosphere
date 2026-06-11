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
        CelestialBody? best         = null;
        double         bestSoiRatio = double.MaxValue;

        foreach (var body in _bodies)
        {
            double dist  = (position - body.Position).Magnitude;
            if (dist < body.SphereOfInfluence)
            {
                double ratio = dist / body.SphereOfInfluence;
                if (ratio < bestSoiRatio)
                {
                    bestSoiRatio = ratio;
                    best         = body;
                }
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
            // Mixed: active vessel uses RK4; all others go on rails
            TickPhysicsMixed(simDelta);
            CurrentTime += simDelta;
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
            if (vessel.IsOnRails)
            {
                PropagateVesselOnRails(vessel, dt);
                continue;
            }

            var refBody = GetDominantBody(vessel.Position);

            // Internal vessel tick (resource drain, SAS, crew EVA, etc.)
            vessel.Tick(dt, refBody);

            // RK4 orbit integration
            (vessel.Position, vessel.Velocity) = RK4Integrator.StepPosVel(
                vessel.Position,
                vessel.Velocity,
                CurrentTime,
                dt,
                (pos, vel, _) => vessel.ComputeNetAcceleration(_bodies, refBody)
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
                vessel.Velocity = Vector3d.Zero;
                // Push vessel back above the surface
                var dir = (vessel.Position - refBody.Position).Normalized;
                vessel.Position = refBody.Position + dir * (refBody.Radius + 1.0);
            }
        }
    }

    private void TickPhysicsMixed(double dt)
    {
        // All celestial bodies on rails
        KeplerPropagator.PropagateAllBodies(_bodies, CurrentTime + dt);

        foreach (var vessel in _vessels)
        {
            if (vessel == ActiveVessel && !vessel.IsOnRails)
            {
                var refBody = GetDominantBody(vessel.Position);
                vessel.Tick(dt, refBody);
                (vessel.Position, vessel.Velocity) = RK4Integrator.StepPosVel(
                    vessel.Position,
                    vessel.Velocity,
                    CurrentTime,
                    dt,
                    (pos, vel, _) => vessel.ComputeNetAcceleration(_bodies, refBody)
                );
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
