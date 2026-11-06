namespace Exosphere.Simulation.Tests;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;

public sealed class LiquidStoreDepletionTests
{
    [Fact]
    public void ActualMainHeaderAndCoastIntervalsDriveTheMassSampler()
    {
        var part = Engine("engine", 0.375, 2, fuelHeaders: [0.125]);
        part.ReserveLandingPropellant(0.125, 0.5);
        var receipt = Consume(part, 2);
        var main = Find(receipt, LiquidPropellantKind.LiquidFuel);
        var header = Find(receipt, LiquidPropellantKind.LiquidFuel, 0);
        var oxidizer = Find(receipt, LiquidPropellantKind.Oxidizer);
        AssertInterval(main, 0, 1);
        AssertInterval(header, 1, 1.5);
        AssertInterval(oxidizer, 0, 1.5);
        Assert.Equal(0.125, main.Withdrawal.AfterKg);
        Assert.Equal(0, header.Withdrawal.AfterKg);
        Assert.Equal(0.875, oxidizer.Withdrawal.AfterKg);

        var components = Components(receipt);
        var distribution = new BodyMassDistribution(2, components);
        Assert.Equal(new[] { 0.0, 1, 1.5, 2 }, distribution.BoundariesSeconds);
        Assert.Equal(1002.5, distribution.Sample(0).MassKg);
        Assert.Equal(1001.75, distribution.Sample(0.75).MassKg);
        Assert.Equal(1001.25, distribution.Sample(1.25).MassKg);
        Assert.Equal(1001, distribution.Sample(1.5).MassKg);
        Assert.Equal(1001, distribution.Sample(2).MassKg);
        Assert.Equal(-1, distribution.Sample(1, MassBoundarySide.Left).MassRateKgPerS);
        Assert.Equal(-1, distribution.Sample(1, MassBoundarySide.Right).MassRateKgPerS);
        Assert.Equal(-1, distribution.Sample(1.5, MassBoundarySide.Left).MassRateKgPerS);
        Assert.Equal(0, distribution.Sample(1.5, MassBoundarySide.Right).MassRateKgPerS);
        Assert.NotEqual(distribution.Sample(1, MassBoundarySide.Left).CenterOfMassRateBodyMps,
            distribution.Sample(1, MassBoundarySide.Right).CenterOfMassRateBodyMps);
        Assert.Equal(Vector3d.Zero, distribution.Sample(2).CenterOfMassRateBodyMps);
        // Reconstruct the ending stores at the same declared stations, independently
        // of the receipt's schedule, and compare the resulting central mass properties.
        var ending = new BodyMassDistribution(2,
        [new("dry", Vector3d.Zero, Matrix3x3d.Identity, [new(0, 1000)]),
            new("fuel", Vector3d.Up, Matrix3x3d.Identity, [new(0, part.LiquidFuel)]),
            new("oxidizer", Vector3d.Up * 2, Matrix3x3d.Identity, [new(0, part.Oxidizer)]),
            new("header", Vector3d.Up * 3, Matrix3x3d.Identity, [new(0, part.HeaderPropellantKg[0])])]);
        Assert.Equal(ending.Sample(2), distribution.Sample(2));
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(0.25, 0.25, 1)]
    [InlineData(0.5, 0.5, 1)]
    [InlineData(1, 1, 1)]
    public void EachReactantSwitchesAtItsOwnMainExhaustion(
        double mainFuel, double expectedSwitch, double expectedEnd)
    {
        // Fuel flow is 1 kg/s, oxidizer flow is 3 kg/s.
        var part = Engine("engine", mainFuel, 1.5, fuelHeaders: [2], oxidizerHeaders: [1.5]);
        part.Definition.ThrustVac *= 4;
        part.Definition.ThrustSL *= 4;
        var receipt = Consume(part, 2);
        if (mainFuel > 0) AssertInterval(Find(receipt, LiquidPropellantKind.LiquidFuel), 0, expectedSwitch);
        if (mainFuel < 1) AssertInterval(Find(receipt, LiquidPropellantKind.LiquidFuel, 0), expectedSwitch, expectedEnd);
        else Assert.DoesNotContain(receipt.StoreDepletions, draw => draw.Withdrawal.HeaderTankIndex == 0);
        AssertInterval(Find(receipt, LiquidPropellantKind.Oxidizer), 0, 0.5);
        AssertInterval(Find(receipt, LiquidPropellantKind.Oxidizer, 1), 0.5, 1);
    }

    [Fact]
    public void SharedMainAndHeaderStoresDrainProportionallyOverTheSameIntervals()
    {
        var first = Engine("first", 0.125, 1, fuelHeaders: [0.0625, 0.1875]);
        var second = Engine("second", 0.375, 3, fuelHeaders: [0.75]);
        var graph = Graph(first);
        graph.AddJoint(new Joint(first, second, "bottom", "top"));
        graph.ConsumePropellant(2, 0);
        var receipt = graph.LastLiquidEngineFunding!;
        Assert.Equal(5, receipt.StoreDepletions.Count(draw => draw.Withdrawal.Resource == LiquidPropellantKind.LiquidFuel));
        foreach (var draw in receipt.StoreDepletions.Where(draw => draw.Withdrawal.Resource == LiquidPropellantKind.LiquidFuel))
        {
            AssertInterval(draw, draw.Withdrawal.HeaderTankIndex.HasValue ? 1 : 0,
                draw.Withdrawal.HeaderTankIndex.HasValue ? 2 : 1);
            double duration = draw.EndTimeSeconds - draw.StartTimeSeconds;
            Assert.Equal(draw.Withdrawal.HeaderTankIndex.HasValue ? 0.5 : 1,
                draw.Withdrawal.MassKg / duration / draw.Withdrawal.BeforeKg, 14);
        }
        Assert.Equal(receipt.Withdrawals, receipt.StoreDepletions.Select(draw => draw.Withdrawal));
    }

    [Fact]
    public void EngineReceiptProfilesSurviveLaterVentAndLiveStoreMutation()
    {
        var part = Engine("engine", 1, 3, fuelHeaders: [0.125]);
        var graph = Graph(part);
        graph.ConsumePropellant(0.5, 0);
        var receipt = graph.LastLiquidEngineFunding!;
        var intervals = receipt.StoreDepletions.ToArray();
        var before = new BodyMassDistribution(0.5, Components(receipt)).Sample(0.5);
        graph.VentMainPropellant(100);
        part.LiquidFuel = part.Oxidizer = part.HeaderPropellantKg[0] = 0;
        graph.ConsumePropellant(0.5, 0);
        Assert.Equal(intervals, receipt.StoreDepletions);
        Assert.Equal(before, new BodyMassDistribution(0.5, Components(receipt)).Sample(0.5));
        Assert.Throws<NotSupportedException>(() => ((IList<LiquidStoreDepletion>)receipt.StoreDepletions)[0] = default);
        // Profiles describe only the engine-funded subinventory. They deliberately
        // retain pre-vent endpoints, rather than claiming to include the later vent.
        Assert.All(receipt.StoreDepletions, draw => Assert.True(draw.Withdrawal.AfterKg > 0));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AssistedGrossDebitsCannotBecomePhysicalMassComponents(bool coupled)
    {
        var part = Engine("engine", 1, 3);
        var vessel = new Vessel("assist")
        {
            Position = Vector3d.Right * 10000, ReferenceBodyId = "body", Throttle = 1,
            SASEnabled = false, InfinitePropellant = true, CapturePropellantFunding = true,
        };
        vessel.Parts.SetRoot(part);
        var universe = new Universe { Coupled6DofIntegrationEnabled = coupled, ActiveVessel = vessel };
        universe.AddBody(new CelestialBody { Id = "body", Radius = 1, SphereOfInfluence = 1e9 });
        universe.AddVessel(vessel);
        universe.Tick(0.02);
        var receipt = vessel.LastLiquidEngineFunding!;
        Assert.NotEmpty(receipt.StoreDepletions);
        Assert.Equal(1, part.LiquidFuel);
        Assert.Throws<InvalidOperationException>(() => receipt.CreateStoreMassComponent(0, "store",
            Vector3d.Zero, Matrix3x3d.Identity));
    }

    [Fact]
    public void TinyMainIntervalDoesNotUnderflowWhenTheFullBurnIsLong()
    {
        var part = Engine("engine", 1e-300, 7.5e307, fuelHeaders: [2.5e307]);
        var receipt = Consume(part, 1e308);
        var main = Find(receipt, LiquidPropellantKind.LiquidFuel);
        Assert.True(main.EndTimeSeconds > 0);
        Assert.Equal(4e-300, main.EndTimeSeconds);
        int index = receipt.StoreDepletions.ToList().IndexOf(main);
        var component = receipt.CreateStoreMassComponent(index, "main", Vector3d.Zero, Matrix3x3d.Identity);
        Assert.Equal(4e-300, component.Knots[1].TimeSeconds);
        Assert.Equal(0, component.Knots[1].MassKg);
    }

    [Fact]
    public void AnUnresolvableHeaderCutIsCapturedButRejectedAsAContinuousProfile()
    {
        const double dt = 8.932577720277969;
        var part = Engine("engine", 0, 100);
        part.Definition.ThrustVac = part.Definition.ThrustSL = 8785.189379678674;
        double needed = part.GetMassFlow(0) * 0.25 * dt;
        part.LiquidFuel = double.BitDecrement(needed);
        part.Definition.HeaderTanks.Add(new() { Id = "tiny", Propellant = "liquid_fuel", CapacityKg = needed - part.LiquidFuel });
        // Reconstruct to initialize the newly declared header, then restore the main load.
        double mainFuel = part.LiquidFuel;
        part = new Part(part.Definition, "engine") { IsStagingActive = true, ThrottleLevel = 1,
            LiquidFuel = mainFuel };
        var receipt = Consume(part, dt);
        var header = Find(receipt, LiquidPropellantKind.LiquidFuel, 0);
        Assert.True(header.Withdrawal.MassKg > 0);
        Assert.Equal(header.StartTimeSeconds, header.EndTimeSeconds);
        int index = receipt.StoreDepletions.ToList().IndexOf(header);
        Assert.Throws<InvalidOperationException>(() => receipt.CreateStoreMassComponent(index,
            "header", Vector3d.Zero, Matrix3x3d.Identity));
    }

    [Fact]
    public void HotStagePoolsKeepTheirOwnHeaderAndExhaustionCuts()
    {
        var upper = Engine("upper", 0.125, 3, fuelHeaders: [0.125]);
        var lower = Engine("lower", 0.25, 3, fuelHeaders: [0.25]);
        var ring = new Part(new PartDefinition
        {
            Id = "ring", CategoryStr = "decoupler", MassDry = 10, LengthM = 0.2, DiameterM = 1,
            AttachmentNodes = [new() { Id = "top", Position = [0, 0.1, 0] },
                new() { Id = "bottom", Position = [0, -0.1, 0] }],
        }) { IsStagingActive = true };
        var graph = Graph(upper);
        graph.AddJoint(new Joint(upper, ring, "bottom", "top"));
        graph.AddJoint(new Joint(ring, lower, "bottom", "top"));
        graph.HotStageOverlapActive = true;
        graph.ConsumePropellant(3, 0);
        var receipt = graph.LastLiquidEngineFunding!;
        foreach (var draw in receipt.StoreDepletions)
        {
            double cut = draw.Withdrawal.PartInstanceId == "upper" ? 0.5 : 1;
            AssertInterval(draw, draw.Withdrawal.HeaderTankIndex.HasValue ? cut : 0,
                draw.Withdrawal.HeaderTankIndex.HasValue || draw.Withdrawal.Resource == LiquidPropellantKind.Oxidizer
                    ? cut * 2 : cut);
            var consumer = Assert.Single(receipt.Engines.Where(engine => engine.PartInstanceId == draw.Withdrawal.PartInstanceId));
            Assert.Equal(consumer.FeedPoolIndex, draw.Withdrawal.FeedPoolIndex);
        }
    }

    private static LiquidStoreDepletion Find(LiquidEngineFundingSnapshot receipt,
        LiquidPropellantKind resource, int? header = null) => Assert.Single(receipt.StoreDepletions.Where(
            draw => draw.Withdrawal.Resource == resource && draw.Withdrawal.HeaderTankIndex == header));

    private static void AssertInterval(LiquidStoreDepletion draw, double start, double end)
    {
        Assert.Equal(start, draw.StartTimeSeconds, 14);
        Assert.Equal(end, draw.EndTimeSeconds, 14);
    }

    private static List<FixedShapeMassComponent> Components(LiquidEngineFundingSnapshot receipt)
    {
        var components = new List<FixedShapeMassComponent>
        { new("dry", Vector3d.Zero, Matrix3x3d.Identity, [new(0, 1000)]) };
        for (int i = 0; i < receipt.StoreDepletions.Count; i++)
        {
            var draw = receipt.StoreDepletions[i].Withdrawal;
            double y = draw.HeaderTankIndex.HasValue ? 3 : draw.Resource == LiquidPropellantKind.Oxidizer ? 2 : 1;
            components.Add(receipt.CreateStoreMassComponent(i, $"store-{i}", Vector3d.Up * y, Matrix3x3d.Identity));
        }
        return components;
    }

    private static LiquidEngineFundingSnapshot Consume(Part part, double dt)
    {
        var graph = Graph(part);
        graph.ConsumePropellant(dt, 0);
        return graph.LastLiquidEngineFunding!;
    }

    private static PartGraph Graph(Part part)
    {
        var graph = new PartGraph { CapturePropellantFunding = true };
        graph.SetRoot(part);
        return graph;
    }

    private static Part Engine(string id, double fuel, double oxidizer,
        double[]? fuelHeaders = null, double[]? oxidizerHeaders = null)
    {
        var headers = new List<HeaderTankDef>();
        foreach (double mass in fuelHeaders ?? [])
            headers.Add(new() { Id = $"fuel-{headers.Count}", Propellant = "liquid_fuel", CapacityKg = mass });
        foreach (double mass in oxidizerHeaders ?? [])
            headers.Add(new() { Id = $"oxidizer-{headers.Count}", Propellant = "oxidizer", CapacityKg = mass });
        var definition = new PartDefinition
        {
            Id = id, CategoryStr = "engine", MassDry = 1000, LengthM = 1, DiameterM = 1,
            ThrustVac = 980.665, ThrustSL = 980.665, IspVac = 100, IspSL = 100,
            MixtureRatio = 3, FuelTypeStr = "liquidfuel", FuelCapacityLF = fuel, FuelCapacityOx = oxidizer,
            HeaderTanks = headers,
            AttachmentNodes = [new() { Id = "top", Position = [0, 0.5, 0] },
                new() { Id = "bottom", Position = [0, -0.5, 0] }],
        };
        return new Part(definition, id) { IsStagingActive = true, ThrottleLevel = 1 };
    }
}
