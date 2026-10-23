namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Xunit;

public sealed class LandingReadoutTests
{
    [Fact]
    public void EagleFallingAtTheLowGateKnowsItsImpactTimeAndBurnHeight()
    {
        var root = SaturnVAscentFlight.DataDirectory();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(root, "parts"));
        var universe = Universe.LoadFromDataDirectory(root);
        var moon = universe.GetBody("moon")!;
        var eagle = VehicleVariantDefinition.LoadFromJson(Path.Combine(root, "vehicles",
            "apollo11_lm5_eagle_1969.json")).Build(catalog).ToVessel("Eagle", "eagle");
        double radius = moon.Radius + 1_000.0;
        eagle.Position = moon.Position + Vector3d.Right * radius;
        eagle.Velocity = moon.Velocity + moon.GetSurfaceVelocity(eagle.Position) - Vector3d.Right * 30.0;
        universe.AddVessel(eagle);

        var status = LandingReadout.Compute(eagle, moon);
        double g = moon.GM / (radius * radius);
        Assert.Equal(-30.0, status.VerticalSpeedMps, 3);
        double t = status.TimeToImpactS;
        Assert.Equal(0.0, 1_000.0 + status.VerticalSpeedMps * t - 0.5 * g * t * t, 3);
        double net = eagle.GetMaximumThrust(moon) / eagle.TotalMass - g;
        Assert.Equal(30.0 * 30.0 / (2.0 * net), status.StoppingDistanceM, 3);
        Assert.True(status.BurnMarginM > 0.0, "A 15 t LM at 30 m/s and 1 km is not yet late.");
    }

    [Fact]
    public void OrbitIsNotAFallAndNoEnginesMeansNoBurnHeight()
    {
        var root = SaturnVAscentFlight.DataDirectory();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(root, "parts"));
        var universe = Universe.LoadFromDataDirectory(root);
        var earth = universe.GetBody("earth")!;
        var assembly = new VesselAssembly(catalog);
        assembly.AddRoot("gemini8_reentry_module");
        var capsule = assembly.ToVessel("Capsule", "capsule");
        double radius = earth.Radius + 300_000.0;
        capsule.Position = earth.Position + Vector3d.Right * radius;
        capsule.Velocity = earth.Velocity + Vector3d.Forward * System.Math.Sqrt(earth.GM / radius);
        universe.AddVessel(capsule);
        var status = LandingReadout.Compute(capsule, earth);
        Assert.True(double.IsPositiveInfinity(status.TimeToImpactS));
        Assert.True(double.IsNaN(status.StoppingDistanceM));
    }
}
