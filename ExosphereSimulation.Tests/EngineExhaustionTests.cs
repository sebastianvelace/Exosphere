namespace Exosphere.Simulation.Tests;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

public sealed class EngineExhaustionTests
{
    [Theory]
    [InlineData(0.1, 10, 0, 9.7)]
    [InlineData(10, 0.3, 9.9, 0)]
    public void ExhaustionPreservesTheUnusedReactant(double fuel, double oxidizer,
        double remainingFuel, double remainingOxidizer)
    {
        var engine = Engine(fuel, oxidizer);
        var graph = new PartGraph();
        graph.SetRoot(engine);
        graph.ConsumePropellant(1, 0);
        Assert.Equal(remainingFuel, engine.LiquidFuel, 12);
        Assert.Equal(remainingOxidizer, engine.Oxidizer, 12);
        Assert.True(engine.FuelDepleted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void APartiallyFundedStepDeliversItsBurnBeforeCoasting(bool coupled)
    {
        var engine = Engine(0.001, 0.1);
        var vessel = new Vessel("exhaustion")
        {
            Position = Vector3d.Right * 10_000, ReferenceBodyId = "body",
            Throttle = 1, SASEnabled = false,
        };
        vessel.Parts.SetRoot(engine);
        var universe = new Universe { ActiveVessel = vessel, Coupled6DofIntegrationEnabled = coupled };
        universe.AddBody(new CelestialBody { Id = "body", Radius = 1, SphereOfInfluence = 1e9 });
        universe.AddVessel(vessel);
        double thrust = engine.GetThrustMagnitude();
        double duration = 0.001 / (engine.GetMassFlow() / 4);
        universe.Tick(0.02);
        // This unit retains the production frozen end-of-step mass approximation.
        double acceleration = thrust / vessel.TotalMass;
        Assert.Equal(acceleration * duration, vessel.Velocity.Y, 11);
        Assert.Equal(acceleration * duration * (0.02 - duration / 2),
            vessel.Position.Y, 11);
        Assert.Equal(0.097, engine.Oxidizer, 12);
        Assert.True(engine.FuelDepleted);
        double velocity = vessel.Velocity.Y;
        universe.Tick(0.02);
        Assert.Equal(velocity, vessel.Velocity.Y, 12);
    }

    private static Part Engine(double fuel, double oxidizer, string id = "engine") => new(new PartDefinition
    {
        Id = id, CategoryStr = "engine", MassDry = 1000, LengthM = 1, DiameterM = 1,
        ThrustVac = 980.665, ThrustSL = 980.665, IspVac = 100, IspSL = 100,
        MixtureRatio = 3, FuelTypeStr = "liquidfuel", FuelCapacityLF = fuel, FuelCapacityOx = oxidizer,
    }, id) { IsStagingActive = true, ThrottleLevel = 1 };
}
