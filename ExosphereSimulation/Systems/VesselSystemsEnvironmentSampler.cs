namespace Exosphere.Simulation.Systems;

using Exosphere.Simulation.Math;

/// <summary>
/// Builds the committed-state environment sample one vessel's systems runtime consumes.
///
/// The sample reads only state the physics already integrated (position, surface velocity,
/// atmosphere, ephemerides) for the vessel it is given, so a non-active materialized
/// runtime is advanced with its own environment rather than the active vessel's.
/// </summary>
public static class VesselSystemsEnvironmentSampler
{
    /// <summary>
    /// Samples <paramref name="vessel"/> at the universe's committed state. When
    /// <paramref name="solarVisibility"/> is null the limb-darkened solar-disc visibility is
    /// computed from every non-solar body; the active vessel passes the presentation's
    /// shared sample instead so power, thermal and lighting stay on one eclipse value.
    /// </summary>
    public static VesselSystemsTickInput Sample(
        Universe universe,
        Vessel vessel,
        double deltaSeconds,
        double simulationTime,
        int crewCount,
        SystemsMissionPhase phase,
        double? solarVisibility = null)
    {
        ArgumentNullException.ThrowIfNull(universe);
        ArgumentNullException.ThrowIfNull(vessel);

        CelestialBody refBody = universe.GetDominantBody(vessel.Position);
        double altitude = vessel.GetAltitude(refBody);
        CelestialBody? earth = universe.GetBody("earth");
        CelestialBody? sun = universe.GetBody("sun");
        Vector3d sunPosition = sun?.Position ?? Vector3d.Zero;

        bool inAtmosphere = refBody.Atmosphere != null
            && altitude < refBody.Atmosphere.MaxAltitude;
        double atmosphereTemperature = inAtmosphere
            ? refBody.AtmosphericTemperatureAt(vessel.Position, altitude)
            : 3.0;
        // Heating and the plasma blackout read the air, not the ground (environment plan E0.1).
        Vector3d surfaceVelocity = vessel.GetAirRelativeVelocity(refBody);
        double airspeed = surfaceVelocity.Magnitude;
        double airDensity = refBody.GetAtmosphericDensity(vessel.Position);
        double heatFlux = vessel.ComputeStagnationHeatFlux(airDensity, surfaceVelocity);

        return new VesselSystemsTickInput(
            DeltaSeconds: deltaSeconds,
            SimulationTime: simulationTime,
            CrewCount: crewCount,
            Phase: phase,
            VesselPosition: vessel.Position,
            EarthPosition: earth?.Position ?? Vector3d.Zero,
            SunPosition: sunPosition,
            Bodies: universe.Bodies,
            SolarVisibility: solarVisibility ?? ComputeSolarVisibility(universe, vessel, sun),
            InAtmosphere: inAtmosphere,
            AtmosphericTemperatureK: atmosphereTemperature,
            AeroHeatFluxWm2: heatFlux,
            EntryCondition: new PlasmaBlackoutInput
            {
                HeatFluxWm2 = heatFlux,
                DensityKgM3 = airDensity,
                AirspeedMs = airspeed,
            });
    }

    private static double ComputeSolarVisibility(
        Universe universe,
        Vessel vessel,
        CelestialBody? sun)
    {
        if (sun is null) return 1.0;
        double visibility = 1.0;
        foreach (CelestialBody body in universe.Bodies)
        {
            if (ReferenceEquals(body, sun)) continue;
            visibility = System.Math.Min(visibility,
                MissionGeometry.LimbDarkenedSolarDiscVisibility(
                    vessel.Position, body.Position, body.Radius, sun.Position, sun.Radius));
        }
        return System.Math.Clamp(visibility, 0.0, 1.0);
    }
}
