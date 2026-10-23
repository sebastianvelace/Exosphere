namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Campaign;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Math;
using Xunit;

/// <summary>
/// Mission milestones recognised from the flown state, so hand-flown missions satisfy the
/// same objectives as the historical director.
/// </summary>
public sealed class FlightPhaseObserverTests
{
    private static (Universe universe, Vessel vessel) Setup()
    {
        var root = SaturnVAscentFlight.DataDirectory();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(root, "parts"));
        var universe = Universe.LoadFromDataDirectory(root);
        var assembly = new VesselAssembly(catalog);
        var current = assembly.AddRoot("gemini8_docking_nose");
        assembly.AttachPartAutomatically(current.InstanceId, "gemini8_reentry_module");
        var vessel = assembly.ToVessel("Capsule", "capsule");
        universe.AddVessel(vessel);
        universe.SetActiveVessel(vessel.Id);
        return (universe, vessel);
    }

    private static void Place(Vessel vessel, CelestialBody body, double altitudeM, double speedMps,
        double radialSpeedMps = 0.0)
    {
        double radius = body.Radius + altitudeM;
        vessel.Position = body.Position + Vector3d.Right * radius;
        vessel.Velocity = body.Velocity + Vector3d.Forward * speedMps + Vector3d.Right * radialSpeedMps;
    }

    private static double Circular(CelestialBody body, double altitudeM) =>
        System.Math.Sqrt(body.GM / (body.Radius + altitudeM));

    [Fact]
    public void LowEarthOrbitThenTranslunarThenLunarOrbitAndDepartureAreEachReportedOnce()
    {
        var (universe, vessel) = Setup();
        var earth = universe.GetBody("earth")!;
        var moon = universe.GetBody("moon")!;
        var observer = new FlightPhaseObserver();

        Place(vessel, earth, 185_000.0, Circular(earth, 185_000.0));
        Assert.Equal(ObservedFlightPhase.Orbit, observer.Observe(vessel, universe));
        Assert.Equal(ObservedFlightPhase.None, observer.Observe(vessel, universe));

        // TLI: just under escape speed, apoapsis near the Moon's distance.
        Place(vessel, earth, 185_000.0, 0.996 * System.Math.Sqrt(2.0) * Circular(earth, 185_000.0));
        Assert.Equal(ObservedFlightPhase.TranslunarInjection, observer.Observe(vessel, universe));

        Place(vessel, moon, 5_000_000.0, 1_200.0, radialSpeedMps: -800.0);
        Assert.Equal(ObservedFlightPhase.LunarApproach, observer.Observe(vessel, universe));
        Assert.Equal(ObservedFlightPhase.None, observer.Observe(vessel, universe));

        Place(vessel, moon, 111_000.0, Circular(moon, 111_000.0));
        Assert.Equal(ObservedFlightPhase.LunarOrbit, observer.Observe(vessel, universe));

        Place(vessel, moon, 111_000.0, 1.6 * Circular(moon, 111_000.0));
        Assert.Equal(ObservedFlightPhase.TransearthInjection, observer.Observe(vessel, universe));

        // Back at Earth on the return trajectory: entry interface while descending.
        Place(vessel, earth, 110_000.0, 11_000.0, radialSpeedMps: -1_500.0);
        Assert.Equal(ObservedFlightPhase.Entry, observer.Observe(vessel, universe));
    }

    [Fact]
    public void LunarImpactTrajectoryIsAnApproachButNotAnOrbit()
    {
        var (universe, vessel) = Setup();
        var moon = universe.GetBody("moon")!;
        var observer = new FlightPhaseObserver();
        // Bound but the perilune is below the surface: that is a crash course, not an orbit.
        Place(vessel, moon, 111_000.0, 0.6 * Circular(moon, 111_000.0));
        Assert.Equal(ObservedFlightPhase.LunarApproach, observer.Observe(vessel, universe));
        Assert.Equal(ObservedFlightPhase.None, observer.Observe(vessel, universe));
        Assert.False(observer.HasReached(ObservedFlightPhase.LunarOrbit));
    }

    [Fact]
    public void SuborbitalFlightLandsOnlyAfterLeavingThePad()
    {
        var (universe, vessel) = Setup();
        var earth = universe.GetBody("earth")!;
        var observer = new FlightPhaseObserver();

        // Sitting on the pad is not a landing.
        Place(vessel, earth, 0.5, 0.0);
        vessel.Velocity = earth.Velocity + earth.GetSurfaceVelocity(vessel.Position);
        Assert.Equal(ObservedFlightPhase.None, observer.Observe(vessel, universe));

        // Freedom 7: up to 187 km without orbiting, then a settled splashdown.
        Place(vessel, earth, 187_000.0, 2_000.0);
        Assert.Equal(ObservedFlightPhase.None, observer.Observe(vessel, universe));
        Place(vessel, earth, 0.5, 0.0);
        vessel.Velocity = earth.Velocity + earth.GetSurfaceVelocity(vessel.Position);
        Assert.Equal(ObservedFlightPhase.Landed, observer.Observe(vessel, universe));
        Assert.False(observer.HasReached(ObservedFlightPhase.Orbit));
    }

    [Fact]
    public void RestoredMilestonesAreNotReportedAgain()
    {
        var (universe, vessel) = Setup();
        var earth = universe.GetBody("earth")!;
        var observer = new FlightPhaseObserver();
        observer.Restore([ObservedFlightPhase.Orbit]);
        Place(vessel, earth, 185_000.0, Circular(earth, 185_000.0));
        Assert.Equal(ObservedFlightPhase.None, observer.Observe(vessel, universe));
    }
}
