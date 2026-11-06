namespace Exosphere.Simulation.Tests;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

/// <summary>
/// Liquid-engine exhaustion is a resource boundary inside the step: every engine sharing a
/// pool burns until the pool's first limiting reactant runs out, delivers exactly that funded
/// impulse (force and moment), keeps the unused reactant, and is shut down only after the step.
/// Engines feeding from different pools (hot-stage overlap) cut off at their own boundaries.
/// </summary>
public sealed class EngineExhaustionTests
{
    // 980.665 N at Isp 100 s: ṁ = 1 kg/s, mixture 3 → 0.25 kg/s fuel, 0.75 kg/s oxidizer.
    private const double Thrust = 980.665;
    private const double Step = 0.02;

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
        var (universe, vessel) = Fly(engine, coupled);
        double thrust = engine.GetThrustMagnitude();
        double duration = 0.001 / (engine.GetMassFlow() / 4);
        universe.Tick(Step);
        // This unit retains the production frozen end-of-step mass approximation.
        double acceleration = thrust / vessel.TotalMass;
        Assert.Equal(acceleration * duration, vessel.Velocity.Y, 11);
        Assert.Equal(acceleration * duration * (Step - duration / 2),
            vessel.Position.Y, 11);
        Assert.Equal(0.097, engine.Oxidizer, 12);
        Assert.True(engine.FuelDepleted);
        double velocity = vessel.Velocity.Y;
        universe.Tick(Step);
        Assert.Equal(velocity, vessel.Velocity.Y, 12);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AStepThePoolFundsExactlyDeliversItAllAndTheNextNothing(bool coupled)
    {
        var engine = Engine(0.25 * Step, 0.75 * Step);
        var (universe, vessel) = Fly(engine, coupled);
        universe.Tick(Step);
        Assert.Equal(Thrust / vessel.TotalMass * Step, vessel.Velocity.Y, 11);
        Assert.InRange(engine.LiquidFuel, 0.0, 1e-15);
        Assert.InRange(engine.Oxidizer, 0.0, 1e-15);
        double velocity = vessel.Velocity.Y;
        universe.Tick(Step);
        Assert.Equal(velocity, vessel.Velocity.Y, 12);
        Assert.True(engine.FuelDepleted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnEmptyPoolDeliversNoImpulse(bool coupled)
    {
        var engine = Engine(0.0, 1.0);
        var (universe, vessel) = Fly(engine, coupled);
        universe.Tick(Step);
        Assert.Equal(0.0, vessel.Velocity.Y);
        Assert.Equal(0.0, vessel.AngularVelocity.Magnitude);
        Assert.Equal(1.0, engine.Oxidizer, 15);
        Assert.True(engine.FuelDepleted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EnginesSharingAPoolCutOffTogetherAtItsFirstLimitingReactant(bool coupled)
    {
        // A dry tank feeding two engines of different size: they burn simultaneously, so the
        // pool's limiting reactant ends both at the same instant, whatever their order.
        var tank = new Part(new PartDefinition
        {
            Id = "tank", CategoryStr = "fuel_tank", MassDry = 500, LengthM = 1, DiameterM = 1,
            FuelCapacityLF = 0.002, FuelCapacityOx = 1.0,
            AttachmentNodes = [new AttachmentNodeDef { Id = "bottom", Position = [0.0, -0.5, 0.0], Size = 1 }],
        }, "tank");
        var small = Engine(0, 0, "a-small", thrustScale: 1);
        var large = Engine(0, 0, "b-large", thrustScale: 3);
        var vessel = Vessel(coupled, out var universe);
        vessel.Parts.SetRoot(tank);
        vessel.Parts.AddJoint(new Joint(tank, small, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(small, large, "bottom", "top"));
        double massFlow = small.GetMassFlow() + large.GetMassFlow();
        double duration = 0.002 / (massFlow / 4);
        Assert.InRange(duration, 0.0, Step);

        universe.Tick(Step);

        Assert.Equal(4.0 * Thrust / vessel.TotalMass * duration, vessel.Velocity.Y, 11);
        Assert.Equal(0.0, tank.LiquidFuel, 15);
        Assert.Equal(1.0 - 0.75 * massFlow * duration, tank.Oxidizer, 12);
        Assert.True(small.FuelDepleted);
        Assert.True(large.FuelDepleted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HotStageSiblingsCutOffAtTheirOwnBoundaries(bool coupled)
    {
        // Overlap: each stage burns its own tanks, so the two engines exhaust at different
        // instants of the same step and the step carries both funded impulses.
        var upper = Engine(0.003, 1.0, "upper");
        var ring = new Part(new PartDefinition
        {
            Id = "ring", CategoryStr = "decoupler", MassDry = 100, LengthM = 0.2, DiameterM = 1,
            AttachmentNodes =
            [
                new AttachmentNodeDef { Id = "top", Position = [0.0, 0.1, 0.0], Size = 1 },
                new AttachmentNodeDef { Id = "bottom", Position = [0.0, -0.1, 0.0], Size = 1 },
            ],
        }, "ring") { IsStagingActive = true };
        var lower = Engine(0.001, 1.0, "lower", thrustScale: 2);
        var vessel = Vessel(coupled, out var universe);
        vessel.Parts.SetRoot(upper);
        vessel.Parts.AddJoint(new Joint(upper, ring, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(ring, lower, "bottom", "top"));
        vessel.BeginHotStageOverlap(10.0);
        double upperDuration = 0.003 / (upper.GetMassFlow() / 4);
        double lowerDuration = 0.001 / (lower.GetMassFlow() / 4);
        Assert.InRange(lowerDuration, 0.0, upperDuration);
        Assert.InRange(upperDuration, lowerDuration, Step);

        universe.Tick(Step);

        double expected = (Thrust * upperDuration + 2.0 * Thrust * lowerDuration) / vessel.TotalMass;
        Assert.Equal(expected, vessel.Velocity.Y, 11);
        Assert.True(upper.FuelDepleted);
        Assert.True(lower.FuelDepleted);
        Assert.Equal(1.0 - 0.75 * upper.Definition.ThrustVac / (100 * 9.80665) * upperDuration, upper.Oxidizer, 12);
    }

    [Fact]
    public void HeadersExtendTheBurnAndAProtectedFeedIsNeverDrawn()
    {
        // Main tanks first, then the headers; the landing reserve stays isolated. The boundary
        // is where main + header fuel runs out.
        var tank = new Part(new PartDefinition
        {
            Id = "tank", CategoryStr = "fuel_tank", MassDry = 500, LengthM = 1, DiameterM = 1,
            FuelCapacityLF = 0.003, FuelCapacityOx = 1.0,
            HeaderTanks = [new HeaderTankDef { Id = "ch4", Propellant = "liquid_fuel", CapacityKg = 0.001, CenterYM = 0.0 }],
            AttachmentNodes = [new AttachmentNodeDef { Id = "bottom", Position = [0.0, -0.5, 0.0], Size = 1 }],
        }, "tank");
        tank.ReserveLandingPropellant(0.001, 0.5);
        var engine = Engine(0, 0);
        var graph = new PartGraph();
        graph.SetRoot(tank);
        graph.AddJoint(new Joint(tank, engine, "bottom", "top"));
        // Available fuel: 0.002 main + 0.001 header = 0.003 kg → 12 ms at 0.25 kg/s.
        graph.ConsumePropellant(Step, 0);
        Assert.Equal(0.001, tank.LiquidFuel, 15);
        Assert.Equal(0.0, tank.HeaderLiquidFuel, 15);
        Assert.Equal(1.0 - 0.75 * 0.012, tank.Oxidizer, 12);
        Assert.True(engine.FuelDepleted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnOffAxisEngineTurnsTheVehicleOnlyForItsFundedInterval(bool coupled)
    {
        // A side-mounted engine on a dry core: its moment acts only while it is funded, and
        // nothing turns the vehicle once it is out.
        var core = new Part(new PartDefinition
        {
            Id = "core", CategoryStr = "command", MassDry = 2000, LengthM = 4, DiameterM = 2,
            AttachmentNodes = [new AttachmentNodeDef { Id = "side", Position = [1.5, 0.0, 0.0], Size = 1 }],
        }, "core");
        var engine = Engine(0.001, 1.0, "side-engine");
        var vessel = Vessel(coupled, out var universe);
        vessel.Parts.SetRoot(core);
        vessel.Parts.AddJoint(new Joint(core, engine, "side", "top"));
        double duration = 0.001 / (engine.GetMassFlow() / 4);
        // The coupled path turns the engine moment through the full inertia tensor; the
        // legacy path through its scalar pitch/yaw/roll inertias.
        var fullStepRate = (coupled
            ? vessel.Parts.GetMassProperties().InverseInertiaBody.Multiply(vessel.Parts.GetTotalTorque(0.0))
            : vessel.Parts.GetPitchYawRollAngularAcceleration(0.0)) * Step;
        Assert.True(fullStepRate.Magnitude > 1e-6);

        universe.Tick(Step);

        // The propellant is a gram on a 3 t vehicle: the frozen-mass inertia differs from the
        // pre-step one by less than one part in a million.
        double turned = vessel.AngularVelocity.Magnitude;
        Assert.Equal(fullStepRate.Magnitude * duration / Step, turned, fullStepRate.Magnitude * 1e-5);
        Assert.True(engine.FuelDepleted);
        universe.Tick(Step);
        Assert.Equal(turned, vessel.AngularVelocity.Magnitude, 12);
    }

    private static (Universe Universe, Vessel Vessel) Fly(Part engine, bool coupled)
    {
        var vessel = Vessel(coupled, out var universe);
        vessel.Parts.SetRoot(engine);
        return (universe, vessel);
    }

    private static Vessel Vessel(bool coupled, out Universe universe)
    {
        var vessel = new Vessel("exhaustion")
        {
            Position = Vector3d.Right * 10_000, ReferenceBodyId = "body",
            Throttle = 1, SASEnabled = false,
        };
        // These pin the exhaustion boundary under the frozen per-step mass; transport has its own tests.
        universe = new Universe { ActiveVessel = vessel, Coupled6DofIntegrationEnabled = coupled, VariableMassTransportEnabled = false };
        universe.AddBody(new CelestialBody { Id = "body", Radius = 1, SphereOfInfluence = 1e9 });
        universe.AddVessel(vessel);
        return vessel;
    }

    private static Part Engine(double fuel, double oxidizer, string id = "engine", double thrustScale = 1) => new(new PartDefinition
    {
        Id = id, CategoryStr = "engine", MassDry = 1000, LengthM = 1, DiameterM = 1,
        ThrustVac = Thrust * thrustScale, ThrustSL = Thrust * thrustScale, IspVac = 100, IspSL = 100,
        MixtureRatio = 3, FuelTypeStr = "liquidfuel", FuelCapacityLF = fuel, FuelCapacityOx = oxidizer,
        AttachmentNodes =
        [
            new AttachmentNodeDef { Id = "top", Position = [0.0, 0.5, 0.0], Size = 1 },
            new AttachmentNodeDef { Id = "bottom", Position = [0.0, -0.5, 0.0], Size = 1 },
        ],
    }, id) { IsStagingActive = true, ThrottleLevel = 1 };
}
