namespace Exosphere.Simulation.Tests;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Exosphere.Simulation.Propulsion;

public sealed class SingleResourceExhaustionTests
{
    [Theory]
    [InlineData("solid", true, 0)]
    [InlineData("solid", false, 0)]
    [InlineData("solid", true, 0.005)]
    [InlineData("solid", false, 0.005)]
    [InlineData("solid", true, 0.02)]
    [InlineData("solid", false, 0.02)]
    [InlineData("monopropellant", true, 0)]
    [InlineData("monopropellant", false, 0)]
    [InlineData("monopropellant", true, 0.005)]
    [InlineData("monopropellant", false, 0.005)]
    [InlineData("monopropellant", true, 0.02)]
    [InlineData("monopropellant", false, 0.02)]
    public void FundedImpulseEndsAtTheActualSingleResourceCutoff(string resource, bool coupled, double load)
    {
        var part = Engine("engine", resource, load);
        var ship = Ship(part);
        var world = World(ship, coupled);
        var position = ship.Position;
        world.Tick(0.02);
        const double acceleration = 980.665 / 1000;
        double duration = System.Math.Min(load, 0.02);
        Assert.Equal(acceleration * duration, ship.Velocity.Y, 12);
        Assert.InRange(System.Math.Abs(acceleration * duration * (0.02 - duration / 2)
            - (ship.Position.Y - position.Y)), 0, 1e-14);
        Assert.Equal(0, part.SolidFuel + part.Monopropellant);
        Assert.True(part.FuelDepleted);
        var window = ship.LastResourceFunding!;
        Assert.True(window.HasCompleteCoverage, window.CoverageError);
        if (duration > 0)
        {
            var flow = Assert.Single(window.OtherEngines);
            Assert.Equal(part.InstanceId, flow.PartInstanceId);
            Assert.Equal(duration, flow.DurationSeconds, 14);
            Assert.Equal(1, flow.MassFlowKgPerS, 14);
            var debit = Assert.Single(window.Debits);
            Assert.Equal(duration, debit.EndTimeSeconds, 14);
            Assert.Equal(load, debit.MassKg);
        }
        else { Assert.Empty(window.OtherEngines); Assert.Empty(window.Debits); }
        double velocity = ship.Velocity.Y;
        world.Tick(0.02);
        Assert.Equal(velocity, ship.Velocity.Y);
        Assert.Empty(ship.LastResourceFunding!.Debits);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MixedLiquidSolidAndMonopropellantBranchesRetainTheirOwnFundedImpulses(bool coupled)
    {
        var liquid = Engine("liquid", "liquidfuel", 0);
        liquid.LiquidFuel = 1; liquid.Oxidizer = 3;
        var solid = Engine("solid", "solid", 0.005);
        var mono = Engine("mono", "monopropellant", 0.01);
        var ship = Ship(liquid);
        ship.Parts.AddJoint(new Joint(liquid, solid, "bottom", "top"));
        ship.Parts.AddJoint(new Joint(solid, mono, "bottom", "top"));
        var world = World(ship, coupled);
        world.Tick(0.02);
        double expectedVelocity = 980.665 / 3003.98 * (0.02 + 0.005 + 0.01);
        double expectedDistance = 980.665 / 3003.98 *
            (0.02 * 0.01 + 0.005 * (0.02 - 0.005 / 2) + 0.01 * (0.02 - 0.01 / 2));
        Assert.Equal(expectedVelocity, ship.Velocity.Y, 12);
        Assert.Equal(expectedDistance, ship.Position.Y, 12);
        Assert.False(liquid.FuelDepleted);
        Assert.True(solid.FuelDepleted);
        Assert.True(mono.FuelDepleted);
        var window = ship.LastResourceFunding!;
        Assert.True(window.HasCompleteCoverage, window.CoverageError);
        Assert.Equal(2, window.OtherEngines.Count);
        Assert.NotNull(window.LiquidEngines);
        Assert.Equal(0.015, window.Debits.Where(draw => draw.Source == ResourceWithdrawalSource.OtherEngine)
            .Sum(draw => draw.MassKg), 14);
        Assert.Equal(0.02, window.LiquidEngines.FundedMassKg, 14);
        world.Tick(0.02);
        Assert.False(liquid.FuelDepleted);
        Assert.True(ship.Velocity.Y > expectedVelocity);
        Assert.Empty(ship.LastResourceFunding!.OtherEngines);
    }

    [Theory]
    [InlineData("solid", true)]
    [InlineData("solid", false)]
    [InlineData("monopropellant", true)]
    [InlineData("monopropellant", false)]
    public void RuntimeClusterRetainsFundedInstanceFlowsAfterPartDepletion(string resource, bool coupled)
    {
        var template = Engine("engine", resource, 0);
        template.Definition.EngineCount = 3;
        template.Definition.EngineModelId = "synthetic";
        template.Definition.ResolvedEngineModel = new EngineModelDefinition
        {
            Id = "synthetic", RatedThrustSeaLevelN = 980.665, RatedThrustVacuumN = 980.665,
            SpecificImpulseSeaLevelS = 100, SpecificImpulseVacuumS = 100, MaximumThrottle = 1,
        };
        var part = new Part(template.Definition, "cluster") { IsStagingActive = true, ThrottleLevel = 1 };
        foreach (var state in part.EngineStates)
        {
            state.State = EngineLifecycleState.Running;
            state.ActualThrottle = state.CommandedThrottle = state.ChamberPressureFraction = 1;
        }
        part.EngineStates[0].State = EngineLifecycleState.Failed;
        if (resource == "solid") part.SolidFuel = 0.01; else part.Monopropellant = 0.01;
        var ids = part.EngineStates.Skip(1).Select(state => state.InstanceId).Order().ToArray();
        var ship = Ship(part); var world = World(ship, coupled); world.Tick(0.02);
        Assert.True(part.FuelDepleted);
        Assert.Equal(0.00980665, ship.Velocity.Y, 12);
        Assert.Equal(ids, ship.LastResourceFunding!.OtherEngines.Select(flow => flow.EngineInstanceId).Order());
        Assert.All(ship.LastResourceFunding.OtherEngines, flow =>
        {
            Assert.Equal(1, flow.MassFlowKgPerS, 14);
            Assert.Equal(0.005, flow.DurationSeconds, 14);
        });
    }

    private static Part Engine(string id, string resource, double load)
    {
        var part = new Part(new PartDefinition
        {
            Id = id, CategoryStr = "engine", MassDry = 1000, LengthM = 1, DiameterM = 1,
            ThrustVac = 980.665, ThrustSL = 980.665, IspVac = 100, IspSL = 100,
            MixtureRatio = 3, FuelTypeStr = resource,
            AttachmentNodes = [new() { Id = "top", Position = [0, 0.5, 0] },
                new() { Id = "bottom", Position = [0, -0.5, 0] }],
        }, id) { IsStagingActive = true, ThrottleLevel = 1 };
        if (resource == "solid") part.SolidFuel = load;
        else if (resource == "monopropellant") part.Monopropellant = load;
        return part;
    }
    private static Vessel Ship(Part part)
    {
        var ship = new Vessel("single resource") { Position = Vector3d.Right * 10000, ReferenceBodyId = "body",
            Throttle = 1, SASEnabled = false, CaptureResourceFunding = true };
        ship.Parts.SetRoot(part); return ship;
    }
    private static Universe World(Vessel ship, bool coupled)
    {
        var world = new Universe { ActiveVessel = ship, Coupled6DofIntegrationEnabled = coupled };
        world.AddBody(new CelestialBody { Id = "body", Radius = 1, SphereOfInfluence = 1e9 });
        world.AddVessel(ship); return world;
    }
}
