namespace Exosphere.Simulation.Navigation;

using Exosphere.Simulation.Flight;

/// <summary>Where the Moon is relative to the flown trajectory, in player terms.</summary>
/// <param name="DistanceM">Centre-to-centre distance to the Moon (m).</param>
/// <param name="InLunarSphere">True inside the Moon's sphere of influence.</param>
/// <param name="EncounterPredicted">
/// Outside the sphere: whether the current coast enters it (null when the trajectory does
/// not reach out to the Moon at all, e.g. still in a parking orbit).
/// </param>
/// <param name="TimeToEventS">
/// Outside the sphere: seconds to the sphere entry, or to the closest approach on a miss.
/// Inside: seconds to perilune (NaN once past it on an escape path).
/// </param>
/// <param name="MissDistanceM">Closest approach to the Moon's centre on a miss (m), else NaN.</param>
/// <param name="PeriluneAltitudeM">
/// Predicted (outside) or osculating (inside) perilune altitude (m); negative means impact.
/// </param>
/// <param name="CapturedInLunarOrbit">Inside the sphere on a bound orbit that clears the surface.</param>
public readonly record struct LunarStatus(
    double DistanceM,
    bool InLunarSphere,
    bool? EncounterPredicted,
    double TimeToEventS,
    double MissDistanceM,
    double PeriluneAltitudeM,
    bool CapturedInLunarOrbit);

public static class LunarStatusReadout
{
    /// <summary>Earth-relative apoapsis radius below which the Moon is out of reach (m).</summary>
    public const double ReachApoapsisRadiusM = 200_000_000.0;
    /// <summary>How far ahead an encounter is searched (s): a slow translunar coast is ~5 days.</summary>
    public const double SearchWindowSeconds = 8.0 * 86_400.0;

    public static LunarStatus? Compute(Vessel vessel, Universe universe)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        ArgumentNullException.ThrowIfNull(universe);
        var moon = universe.GetBody("moon");
        var earth = universe.GetBody("earth");
        if (moon?.OrbitalElements == null || earth == null) return null;

        double distance = (moon.Position - vessel.Position).Magnitude;
        double now = universe.CurrentTime;
        var dominant = universe.GetDominantBody(vessel.Position);
        if (dominant.Id == moon.Id)
        {
            var relative = vessel.Position - moon.Position;
            var velocity = vessel.Velocity - moon.Velocity;
            var orbit = OrbitalElements.FromStateVector(relative, velocity, moon.GM, moon.Id, now);
            bool captured = orbit.Eccentricity < 1.0 && orbit.Apoapsis < moon.SphereOfInfluence
                && orbit.Periapsis > moon.Radius;
            return new LunarStatus(distance, true, true,
                PeriapsisCaptureBurn.TimeToPeriapsis(relative, velocity, moon.GM),
                double.NaN, orbit.Periapsis - moon.Radius, captured);
        }
        if (dominant.Id != earth.Id) return new LunarStatus(distance, false, null, double.NaN, double.NaN, double.NaN, false);

        var earthOrbit = OrbitalElements.FromStateVector(vessel.Position - earth.Position,
            vessel.Velocity - earth.Velocity, earth.GM, earth.Id, now);
        if (earthOrbit.Eccentricity < 1.0 && earthOrbit.Apoapsis < ReachApoapsisRadiusM)
            return new LunarStatus(distance, false, null, double.NaN, double.NaN, double.NaN, false);

        var analysis = LunarTransferPlanner.AnalyzeEncounter(earth.GM, moon.GM, moon.Radius,
            moon.SphereOfInfluence, earthOrbit, moon.OrbitalElements, now, SearchWindowSeconds);
        var encounter = analysis.Encounter;
        return analysis.HasEncounter
            ? new LunarStatus(distance, false, true, encounter.TimeOfSoiEntry - now, double.NaN,
                analysis.PredictedPeriapsisRadius - moon.Radius, false)
            : new LunarStatus(distance, false, false, encounter.TimeOfClosestApproach - now,
                encounter.ClosestApproachDistance, double.NaN, false);
    }
}
