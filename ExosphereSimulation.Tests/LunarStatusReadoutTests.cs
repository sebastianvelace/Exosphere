namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Navigation;
using Xunit;

/// <summary>The HUD's "where is the Moon" readout.</summary>
public sealed class LunarStatusReadoutTests
{
    private static (Universe universe, Vessel vessel, CelestialBody earth, CelestialBody moon) Setup()
    {
        var root = SaturnVAscentFlight.DataDirectory();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(root, "parts"));
        var universe = Universe.LoadFromDataDirectory(root);
        var assembly = new VesselAssembly(catalog);
        assembly.AddRoot("gemini8_reentry_module");
        var vessel = assembly.ToVessel("Capsule", "capsule");
        universe.AddVessel(vessel);
        return (universe, vessel, universe.GetBody("earth")!, universe.GetBody("moon")!);
    }

    [Fact]
    public void ParkingOrbitHasNoEncounterToReport()
    {
        var (universe, vessel, earth, _) = Setup();
        double radius = earth.Radius + 185_000.0;
        vessel.Position = earth.Position + Vector3d.Right * radius;
        vessel.Velocity = earth.Velocity + Vector3d.Forward * System.Math.Sqrt(earth.GM / radius);
        var status = LunarStatusReadout.Compute(vessel, universe)!.Value;
        Assert.Null(status.EncounterPredicted);
        Assert.False(status.InLunarSphere);
        Assert.InRange(status.DistanceM, 3.3e8, 4.2e8);
    }

    [Fact]
    public void ClosingOnTheMoonPredictsTheSphereEntryAndAMissingTrajectoryDoesNot()
    {
        var (universe, vessel, earth, moon) = Setup();
        var towardMoon = (moon.Position - earth.Position).Normalized;
        vessel.Position = moon.Position - towardMoon * 100_000_000.0;
        // Lead the Moon a little: aim at where it will be in ~10 h.
        var aim = (moon.Position + (moon.Velocity - earth.Velocity) * 36_000.0 - vessel.Position).Normalized;
        vessel.Velocity = earth.Velocity + aim * 2_000.0;
        var hit = LunarStatusReadout.Compute(vessel, universe)!.Value;
        Assert.True(hit.EncounterPredicted);
        Assert.InRange(hit.TimeToEventS, 3_600.0, 3.0 * 86_400.0);

        vessel.Velocity = earth.Velocity - aim * 2_000.0;
        var miss = LunarStatusReadout.Compute(vessel, universe)!.Value;
        Assert.False(miss.EncounterPredicted);
        Assert.True(miss.MissDistanceM > moon.SphereOfInfluence);
    }

    [Fact]
    public void LowLunarOrbitReadsAsCaptured()
    {
        var (universe, vessel, _, moon) = Setup();
        double radius = moon.Radius + 111_000.0;
        vessel.Position = moon.Position + Vector3d.Right * radius;
        vessel.Velocity = moon.Velocity + Vector3d.Forward * System.Math.Sqrt(moon.GM / radius);
        var status = LunarStatusReadout.Compute(vessel, universe)!.Value;
        Assert.True(status.InLunarSphere);
        Assert.True(status.CapturedInLunarOrbit);
        Assert.InRange(status.PeriluneAltitudeM, 105_000.0, 112_000.0);
    }
}
