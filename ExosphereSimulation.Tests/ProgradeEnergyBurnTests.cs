namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Xunit;

public sealed class ProgradeEnergyBurnTests
{
    /// <summary>
    /// A stage that runs dry short of the target energy must stop the burn: AS-506 on the
    /// wrong clock reached TLI with too little propellant and "burned" an empty S-IVB for
    /// the whole 900 s envelope at full throttle.
    /// </summary>
    [Fact]
    public void BurnStopsWhenTheStageRunsDryShortOfTheTarget()
    {
        var root = SaturnVAscentFlight.DataDirectory();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(root, "parts"));
        var universe = Universe.LoadFromDataDirectory(root);
        var earth = universe.GetBody("earth")!;
        var eagle = VehicleVariantDefinition.LoadFromJson(Path.Combine(root, "vehicles",
            "apollo11_lm5_eagle_1969.json")).Build(catalog).ToVessel("Eagle", "eagle");
        var descent = eagle.Parts.Parts.Single(part => part.Definition.HasVehicleRole("lunar_module_descent_stage"));
        descent.LiquidFuel *= 0.02;
        descent.Oxidizer *= 0.02;
        double radius = earth.Radius + 185_000.0;
        var tangent = Vector3d.Forward;
        eagle.Position = earth.Position + Vector3d.Right * radius;
        eagle.Velocity = earth.Velocity + tangent * System.Math.Sqrt(earth.GM / radius);
        eagle.Orientation = Quaterniond.FromTo(Vector3d.Up, tangent);
        universe.AddVessel(eagle);

        // Escape energy: far beyond what 2% of the DPS load can give.
        var burn = new ProgradeEnergyBurn(0.0, universe.CurrentTime, maximumBurnSeconds: 900.0);
        while (!burn.IsFinished && burn.BurnSeconds < 900.0)
        {
            burn.Advance(eagle, earth, 0.02, universe.CurrentTime);
            universe.Tick(0.02);
        }
        Assert.Equal(ProgradeEnergyBurnStatus.Blocked, burn.Status);
        Assert.Equal("propellant-depleted", burn.BlockReason);
        Assert.InRange(burn.BurnSeconds, 15.0, 60.0);
        Assert.Equal(0.0, eagle.Throttle);
    }
}
