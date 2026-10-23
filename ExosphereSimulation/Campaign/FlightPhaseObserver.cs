namespace Exosphere.Simulation.Campaign;

using Exosphere.Simulation.Flight;

/// <summary>Mission milestones recognised from the flown state, whoever is flying.</summary>
public enum ObservedFlightPhase
{
    None,
    Orbit,
    TranslunarInjection,
    LunarApproach,
    LunarOrbit,
    TransearthInjection,
    Entry,
    Landed,
}

/// <summary>
/// Recognises mission milestones from physics alone, so a hand-flown mission counts exactly
/// like one flown by the historical director: a bound orbit clear of the atmosphere, a
/// translunar trajectory, the lunar sphere of influence, a bound lunar orbit, the departure
/// back to Earth, atmospheric entry and a settled touchdown or splashdown. Each milestone is
/// reported once, in flight order; later milestones may be reached without the earlier ones
/// (a suborbital Mercury flight lands without ever reaching orbit).
/// </summary>
public sealed class FlightPhaseObserver
{
    /// <summary>Earth-relative apoapsis radius (m) beyond which a trajectory is translunar.</summary>
    public const double TranslunarApoapsisRadiusM = 250_000_000.0;
    /// <summary>A vehicle settles when it is this close to the surface and this slow.</summary>
    public const double LandedAltitudeM = 1.1;
    public const double LandedSurfaceSpeedMps = 1.0;
    /// <summary>Altitude a flight must have exceeded before a touchdown counts as a landing.</summary>
    public const double MinimumFlightAltitudeM = 1_000.0;

    private readonly HashSet<ObservedFlightPhase> _reached = [];
    private double _peakAltitudeM;
    private string? _peakAltitudeBodyId;

    public IReadOnlyCollection<ObservedFlightPhase> Reached => _reached;

    public bool HasReached(ObservedFlightPhase phase) => _reached.Contains(phase);

    /// <summary>Marks milestones already flown (a resumed save).</summary>
    public void Restore(IEnumerable<ObservedFlightPhase> reached)
    {
        ArgumentNullException.ThrowIfNull(reached);
        foreach (var phase in reached)
            if (phase != ObservedFlightPhase.None) _reached.Add(phase);
    }

    /// <summary>
    /// Inspects the vessel and returns the milestone newly reached on this call, or
    /// <see cref="ObservedFlightPhase.None"/>.
    /// </summary>
    public ObservedFlightPhase Observe(Vessel vessel, Universe universe)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        ArgumentNullException.ThrowIfNull(universe);
        if (vessel.IsDestroyed) return ObservedFlightPhase.None;

        var body = universe.GetDominantBody(vessel.Position);
        double altitude = vessel.GetAltitude(body);
        if (_peakAltitudeBodyId != body.Id || altitude > _peakAltitudeM)
        {
            if (_peakAltitudeBodyId != body.Id) _peakAltitudeM = 0.0;
            _peakAltitudeBodyId = body.Id;
            _peakAltitudeM = System.Math.Max(_peakAltitudeM, altitude);
        }

        var relative = vessel.Position - body.Position;
        var velocity = vessel.Velocity - body.Velocity;
        var orbit = OrbitalElements.FromStateVector(relative, velocity, body.GM, body.Id, universe.CurrentTime);
        bool bound = orbit.Eccentricity < 1.0;
        bool lunar = body.Id == "moon";
        double atmosphereTop = body.Atmosphere?.MaxAltitude ?? 0.0;

        if (altitude <= LandedAltitudeM
            && vessel.GetSurfaceVelocity(body).Magnitude < LandedSurfaceSpeedMps
            && (_peakAltitudeM >= MinimumFlightAltitudeM || HasReached(ObservedFlightPhase.LunarOrbit) && lunar))
            return Reach(ObservedFlightPhase.Landed);

        if (lunar)
        {
            if (HasReached(ObservedFlightPhase.LunarOrbit) && !bound)
                return Reach(ObservedFlightPhase.TransearthInjection);
            var approach = Reach(ObservedFlightPhase.LunarApproach);
            if (approach != ObservedFlightPhase.None) return approach;
            if (bound && orbit.Apoapsis < body.SphereOfInfluence
                && OrbitQualificationPolicy.HasSafePeriapsis(orbit, body.Radius, atmosphereTop))
                return Reach(ObservedFlightPhase.LunarOrbit);
            return ObservedFlightPhase.None;
        }

        if (body.Id != "earth") return ObservedFlightPhase.None;
        if (HasReached(ObservedFlightPhase.LunarOrbit))
        {
            var departure = Reach(ObservedFlightPhase.TransearthInjection);
            if (departure != ObservedFlightPhase.None) return departure;
        }
        if (atmosphereTop > 0.0 && altitude < atmosphereTop && relative.Dot(velocity) < 0.0
            && (HasReached(ObservedFlightPhase.Orbit) || HasReached(ObservedFlightPhase.TranslunarInjection)))
            return Reach(ObservedFlightPhase.Entry);
        if (!bound || orbit.Apoapsis >= TranslunarApoapsisRadiusM)
            return HasReached(ObservedFlightPhase.LunarOrbit) ? ObservedFlightPhase.None
                : Reach(ObservedFlightPhase.TranslunarInjection);
        if (OrbitQualificationPolicy.HasSafePeriapsis(orbit, body.Radius, atmosphereTop))
            return Reach(ObservedFlightPhase.Orbit);
        return ObservedFlightPhase.None;
    }

    private ObservedFlightPhase Reach(ObservedFlightPhase phase) =>
        _reached.Add(phase) ? phase : ObservedFlightPhase.None;
}
