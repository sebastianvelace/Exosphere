namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Math;
using Xunit;

/// <summary>The practice assist keeps tanks full while the engines behave exactly as usual.</summary>
public sealed class InfinitePropellantTests
{
    private static (Universe universe, Vessel vessel, CelestialBody earth) Eagle(bool infinite)
    {
        var root = SaturnVAscentFlight.DataDirectory();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(root, "parts"));
        var universe = Universe.LoadFromDataDirectory(root);
        var earth = universe.GetBody("earth")!;
        var eagle = VehicleVariantDefinition.LoadFromJson(Path.Combine(root, "vehicles",
            "apollo11_lm5_eagle_1969.json")).Build(catalog).ToVessel("Eagle", "eagle");
        double radius = earth.Radius + 300_000.0;
        eagle.Position = earth.Position + Vector3d.Right * radius;
        eagle.Velocity = earth.Velocity + Vector3d.Forward * System.Math.Sqrt(earth.GM / radius);
        eagle.Orientation = Quaterniond.FromTo(Vector3d.Up, Vector3d.Forward);
        eagle.SASEnabled = false;
        eagle.InfinitePropellant = infinite;
        eagle.Throttle = 1.0;
        universe.AddVessel(eagle);
        return (universe, eagle, earth);
    }

    private static double Propellant(Vessel vessel) =>
        vessel.Parts.Parts.Sum(part => part.LiquidFuel + part.Oxidizer + part.SolidFuel + part.Monopropellant);

    [Fact]
    public void AssistedBurnGainsTheSameSpeedWithoutDrainingTheTanks()
    {
        var (normalUniverse, normal, earth) = Eagle(infinite: false);
        var (assistUniverse, assisted, _) = Eagle(infinite: true);
        double before = Propellant(assisted);
        double startSpeed = (assisted.Velocity - earth.Velocity).Magnitude;
        for (int step = 0; step < 50 * 30; step++)
        {
            normalUniverse.Tick(0.02);
            assistUniverse.Tick(0.02);
        }
        Assert.Equal(before, Propellant(assisted), 6);
        Assert.True(Propellant(normal) < before - 100.0, "The unassisted burn must drain the DPS.");
        double gained = (assisted.Velocity - earth.Velocity).Magnitude - startSpeed;
        Assert.True(gained > 50.0, $"Assisted burn gained only {gained:F1} m/s.");
    }
}
