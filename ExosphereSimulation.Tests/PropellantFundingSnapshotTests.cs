namespace Exosphere.Simulation.Tests;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Exosphere.Simulation.Propulsion;

public sealed class PropellantFundingSnapshotTests
{
    [Theory]
    [InlineData(0.001, 1, 0.004)]
    [InlineData(1, 0.003, 0.004)]
    [InlineData(0.005, 0.015, 0.02)]
    [InlineData(1, 3, 0.02)]
    public void LiquidReceiptRetainsTheFundedIntervalAndActualReactantDebits(
        double fuel, double oxidizer, double duration)
    {
        var part = Engine(fuel, oxidizer);
        var graph = Graph(part);
        graph.ConsumePropellant(0.02, 0);
        var receipt = Assert.IsType<LiquidEngineFundingSnapshot>(graph.LastLiquidEngineFunding);
        var engine = Assert.Single(receipt.Engines);
        Assert.Equal(part.InstanceId, engine.PartInstanceId);
        Assert.Equal(part.InstanceId, engine.EngineInstanceId);
        Assert.Equal(0.02, receipt.StepDurationSeconds);
        Assert.Equal(duration, engine.DurationSeconds, 14);
        Assert.Equal(0.25, engine.LiquidFuelFlowKgPerS, 14);
        Assert.Equal(0.75, engine.OxidizerFlowKgPerS, 14);
        Assert.Equal(duration, receipt.FundedMassKg, 14);
        Assert.Equal(receipt.FundedMassKg, receipt.WithdrawnMassKg, 14);
        Assert.All(receipt.Withdrawals, draw =>
        {
            Assert.Equal(engine.FeedPoolIndex, draw.FeedPoolIndex);
            Assert.Null(draw.HeaderTankIndex);
            Assert.Null(draw.HeaderTankId);
            Assert.True(draw.MassKg > 0);
            Assert.True(draw.AfterKg >= 0);
        });
        Assert.False(receipt.UsesInfinitePropellantAssist);
    }

    [Fact]
    public void HeadersAndProtectedMainLoadsRemainDistinctStores()
    {
        var part = Engine(0.003, 1, headers: true);
        part.ReserveLandingPropellant(0.001, 0.5);
        var graph = Graph(part);
        graph.ConsumePropellant(0.02, 0);
        var receipt = graph.LastLiquidEngineFunding!;
        Assert.Equal(0.012, Assert.Single(receipt.Engines).DurationSeconds, 14);
        var header = Assert.Single(receipt.Withdrawals.Where(draw => draw.HeaderTankIndex.HasValue));
        Assert.Equal(0, header.HeaderTankIndex);
        Assert.Equal("fuel-header", header.HeaderTankId);
        Assert.Equal(LiquidPropellantKind.LiquidFuel, header.Resource);
        Assert.Equal(0.001, header.MassKg, 14);
        var mainFuel = Assert.Single(receipt.Withdrawals.Where(draw =>
            !draw.HeaderTankIndex.HasValue && draw.Resource == LiquidPropellantKind.LiquidFuel));
        Assert.Equal(0.001, mainFuel.AfterKg, 14);
        Assert.Equal(0.002, mainFuel.MassKg, 14);
        Assert.Equal(receipt.FundedMassKg, receipt.WithdrawnMassKg, 14);
    }

    [Fact]
    public void FundingUsesPreparedPressureAndThrottleRatherThanRatedVacuumFlow()
    {
        var part = Engine(1, 3);
        part.Definition.ThrustSL = 200;
        part.Definition.ThrustVac = 600;
        part.Definition.IspSL = 100;
        part.Definition.IspVac = 200;
        part.ThrottleLevel = 0.4;
        double expectedFlow = part.GetMassFlow(101325);
        var graph = Graph(part);
        graph.ConsumePropellant(0.02, 101325);
        var receipt = graph.LastLiquidEngineFunding!;
        Assert.Equal(expectedFlow * 0.02, receipt.FundedMassKg, 14);
        Assert.Equal(receipt.FundedMassKg, receipt.WithdrawnMassKg, 14);
    }

    [Fact]
    public void SharedFeedRetainsEachConsumersMixtureInsteadOfAssigningPipeRoutes()
    {
        var first = Engine(0.003, 1, id: "first");
        var second = Engine(0, 0, id: "second");
        second.Definition.MixtureRatio = 1;
        var graph = Graph(first);
        graph.AddJoint(new Joint(first, second, "bottom", "top"));
        graph.ConsumePropellant(0.02, 0);
        var receipt = graph.LastLiquidEngineFunding!;
        Assert.Equal(2, receipt.Engines.Count);
        Assert.All(receipt.Engines, flow => Assert.Equal(0.004, flow.DurationSeconds, 14));
        Assert.Single(receipt.Engines.Select(flow => flow.FeedPoolIndex).Distinct());
        Assert.Equal(0.25, receipt.Engines[0].LiquidFuelFlowKgPerS, 14);
        Assert.Equal(0.5, receipt.Engines[1].LiquidFuelFlowKgPerS, 14);
        Assert.All(receipt.Withdrawals, draw => Assert.Equal("first", draw.PartInstanceId));
        Assert.Equal(0.008, receipt.FundedMassKg, 14);
        Assert.Equal(receipt.FundedMassKg, receipt.WithdrawnMassKg, 14);
    }

    [Fact]
    public void AClusterKeepsEachLiveRuntimeIdentityAfterAggregateDepletion()
    {
        var part = Engine(0.001, 1, runtimeCount: 3);
        part.EngineStates[0].State = EngineLifecycleState.Failed;
        var graph = Graph(part);
        graph.ConsumePropellant(0.02, 0);
        var receipt = graph.LastLiquidEngineFunding!;
        Assert.True(part.FuelDepleted);
        Assert.Equal(new[] { part.EngineStates[1].InstanceId, part.EngineStates[2].InstanceId },
            receipt.Engines.Select(flow => flow.EngineInstanceId));
        Assert.All(receipt.Engines, flow => Assert.Equal(0.002, flow.DurationSeconds, 14));
        Assert.Equal(receipt.FundedMassKg, receipt.WithdrawnMassKg, 14);
        Assert.All(part.EngineStates, state => Assert.Equal(0, state.ChamberPressureFraction));
        Assert.All(receipt.Engines, flow => Assert.Equal(1, flow.LiquidFuelFlowKgPerS + flow.OxidizerFlowKgPerS, 14));
    }

    [Fact]
    public void CapturePreservesExistingToleranceOfUndeclaredHeaderSlots()
    {
        var part = Engine(0.001, 1, headers: true);
        // Existing header consumers ignore surplus saved slots after a definition change.
        part.Definition.HeaderTanks.Clear();
        var plain = Engine(0.001, 1, headers: true);
        plain.Definition.HeaderTanks.Clear();
        var plainGraph = Graph(plain);
        plainGraph.CapturePropellantFunding = false;
        plainGraph.ConsumePropellant(0.02, 0);
        var graph = Graph(part);
        graph.ConsumePropellant(0.02, 0);
        Assert.Equal(plain.LiquidFuel, part.LiquidFuel);
        Assert.Equal(plain.Oxidizer, part.Oxidizer);
        Assert.Equal(plain.HeaderPropellantKg, part.HeaderPropellantKg);
        Assert.All(graph.LastLiquidEngineFunding!.Withdrawals, draw => Assert.Null(draw.HeaderTankIndex));
        Assert.Equal(0.004, graph.LastLiquidEngineFunding.FundedMassKg, 14);
    }

    [Fact]
    public void HotStageReceiptsDoNotConnectSeparateFeeds()
    {
        var upper = Engine(0.003, 1, id: "upper");
        var lower = Engine(0.001, 1, id: "lower");
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
        graph.ConsumePropellant(0.02, 0);
        var receipt = graph.LastLiquidEngineFunding!;
        Assert.Equal(2, receipt.Engines.Count);
        var up = Assert.Single(receipt.Engines.Where(flow => flow.PartInstanceId == "upper"));
        var down = Assert.Single(receipt.Engines.Where(flow => flow.PartInstanceId == "lower"));
        Assert.NotEqual(up.FeedPoolIndex, down.FeedPoolIndex);
        Assert.Equal(0.012, up.DurationSeconds, 14);
        Assert.Equal(0.004, down.DurationSeconds, 14);
        Assert.All(receipt.Withdrawals, draw => Assert.Equal(draw.PartInstanceId == "upper"
            ? up.FeedPoolIndex : down.FeedPoolIndex, draw.FeedPoolIndex));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EngineAndVentReceiptsComposeWithoutDoubleCounting(bool coupled)
    {
        var (universe, vessel, part) = Fly(0.001, 0.1, coupled);
        vessel.CapturePropellantFunding = true;
        vessel.PropellantVentRateKgPerS = 10;
        universe.Tick(0.02);
        var engine = vessel.LastLiquidEngineFunding!;
        var vent = vessel.LastMainVentFunding!;
        Assert.Equal(0.004, engine.FundedMassKg, 14);
        Assert.Equal(0.097, vent.FundedMassKg, 14);
        Assert.Equal(0.2, vent.RequestedMassKg, 14);
        Assert.Equal(0.02, vent.StepDurationSeconds);
        Assert.Equal(0.0097, vent.FundedDurationSeconds!.Value, 14);
        Assert.Equal(0.101, engine.WithdrawnMassKg + vent.WithdrawnMassKg, 14);
        var oxidizer = Assert.Single(vent.Withdrawals);
        Assert.Equal(0.097, oxidizer.BeforeKg, 14);
        Assert.Equal(part.Oxidizer, oxidizer.AfterKg);
        Assert.Null(oxidizer.FeedPoolIndex);
        Assert.False(vent.UsesInfinitePropellantAssist);
        universe.Tick(0.02);
        Assert.Null(vessel.LastLiquidEngineFunding);
        Assert.Null(vessel.LastMainVentFunding);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OptInCaptureHasBitIdenticalPhysicalResults(bool coupled)
    {
        var (plain, plainShip, plainPart) = Fly(1, 3, coupled);
        var (captured, capturedShip, capturedPart) = Fly(1, 3, coupled);
        capturedShip.CapturePropellantFunding = true;
        plainShip.PropellantVentRateKgPerS = capturedShip.PropellantVentRateKgPerS = 0.1;
        for (int i = 0; i < 3; i++)
        {
            plain.Tick(0.02); captured.Tick(0.02);
            Assert.Equal(plainShip.Position, capturedShip.Position);
            Assert.Equal(plainShip.Velocity, capturedShip.Velocity);
            Assert.Equal(plainShip.Orientation, capturedShip.Orientation);
            Assert.Equal(plainShip.AngularVelocity, capturedShip.AngularVelocity);
            Assert.Equal(plainPart.LiquidFuel, capturedPart.LiquidFuel);
            Assert.Equal(plainPart.Oxidizer, capturedPart.Oxidizer);
            Assert.Equal(plainShip.VentedPropellantKg, capturedShip.VentedPropellantKg);
            Assert.Equal(plainPart.FuelDepleted, capturedPart.FuelDepleted);
        }
        Assert.Null(plainShip.LastLiquidEngineFunding);
        Assert.Null(plainShip.LastMainVentFunding);
        Assert.NotNull(capturedShip.LastLiquidEngineFunding);
        Assert.NotNull(capturedShip.LastMainVentFunding);
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(1)]
    public void PracticeReplenishmentIsMarkedAndDoesNotEraseGrossWithdrawals(double fuel)
    {
        var (universe, vessel, part) = Fly(fuel, 3, true);
        vessel.CapturePropellantFunding = true;
        vessel.InfinitePropellant = true;
        vessel.PropellantVentRateKgPerS = 0.1;
        universe.Tick(0.02);
        Assert.Equal(fuel, part.LiquidFuel);
        Assert.Equal(3, part.Oxidizer);
        Assert.True(vessel.LastLiquidEngineFunding!.UsesInfinitePropellantAssist);
        Assert.True(vessel.LastMainVentFunding!.UsesInfinitePropellantAssist);
        Assert.True(vessel.LastLiquidEngineFunding.WithdrawnMassKg > 0);
        Assert.True(vessel.LastMainVentFunding.WithdrawnMassKg > 0);
        Assert.Equal(fuel < 0.005, part.FuelDepleted);
    }

    [Fact]
    public void PublishedReceiptsSurviveLaterMutationAndAreReadOnly()
    {
        var graph = Graph(Engine(1, 3, headers: true));
        graph.ConsumePropellant(0.02, 0);
        var receipt = graph.LastLiquidEngineFunding!;
        graph.VentMainPropellant(0.1);
        var vent = graph.LastMainVentFunding!;
        var original = receipt.Withdrawals.ToArray();
        var originalVent = vent.Withdrawals.ToArray();
        graph.Root!.LiquidFuel = 0;
        graph.Root.Oxidizer = 0;
        graph.Root.HeaderPropellantKg[0] = 0;
        graph.ConsumePropellant(0.02, 0);
        graph.VentMainPropellant(0.1);
        Assert.Null(graph.LastLiquidEngineFunding);
        Assert.Null(graph.LastMainVentFunding);
        Assert.Equal(original, receipt.Withdrawals);
        Assert.Equal(originalVent, vent.Withdrawals);
        Assert.Throws<NotSupportedException>(() => ((IList<LiquidStoreWithdrawal>)receipt.Withdrawals)[0] = default);
        Assert.Throws<NotSupportedException>(() => ((IList<FundedLiquidEngine>)receipt.Engines)[0] = default);
        Assert.Throws<NotSupportedException>(() => ((IList<LiquidStoreWithdrawal>)vent.Withdrawals)[0] = default);
    }

    [Fact]
    public void MassOnlyVentCallsDoNotInventAnIntervalOrDrawProtectedStoresOrHeaders()
    {
        var part = Engine(1, 3, headers: true);
        part.ReserveLandingPropellant(0.1, 0.3);
        var graph = Graph(part);
        graph.VentMainPropellant(10);
        var receipt = graph.LastMainVentFunding!;
        Assert.Null(receipt.StepDurationSeconds);
        Assert.Null(receipt.FundedDurationSeconds);
        Assert.Equal(3.6, receipt.FundedMassKg, 14);
        Assert.Equal(receipt.FundedMassKg, receipt.WithdrawnMassKg, 14);
        Assert.All(receipt.Withdrawals, draw => Assert.Null(draw.HeaderTankIndex));
        Assert.Equal(0.001, part.HeaderLiquidFuel);
        Assert.Equal(0.1, part.LiquidFuel, 14);
        Assert.Equal(0.3, part.Oxidizer, 14);
    }

    [Fact]
    public void NoFiringAndDisabledCaptureClearTheReadoutsOnTheNextPreparation()
    {
        var (universe, vessel, part) = Fly(1, 3, true);
        vessel.CapturePropellantFunding = true;
        vessel.PropellantVentRateKgPerS = 0.1;
        universe.Tick(0.02);
        Assert.NotNull(vessel.LastLiquidEngineFunding);
        Assert.NotNull(vessel.LastMainVentFunding);
        part.IsStagingActive = false;
        vessel.PropellantVentRateKgPerS = 0;
        universe.Tick(0.02);
        Assert.Null(vessel.LastLiquidEngineFunding);
        Assert.Null(vessel.LastMainVentFunding);
        part.IsStagingActive = true;
        universe.Tick(0.02);
        Assert.NotNull(vessel.LastLiquidEngineFunding);
        vessel.CapturePropellantFunding = false;
        vessel.PropellantVentRateKgPerS = 0.1;
        universe.Tick(0.02);
        Assert.Null(vessel.LastLiquidEngineFunding);
        Assert.Null(vessel.LastMainVentFunding);
    }

    private static PartGraph Graph(Part part)
    {
        var graph = new PartGraph { CapturePropellantFunding = true };
        graph.SetRoot(part);
        return graph;
    }

    private static (Universe, Vessel, Part) Fly(double fuel, double oxidizer, bool coupled)
    {
        var part = Engine(fuel, oxidizer);
        var vessel = new Vessel("funding")
        {
            Position = Vector3d.Right * 10_000, ReferenceBodyId = "body", Throttle = 1, SASEnabled = false,
        };
        vessel.Parts.SetRoot(part);
        var universe = new Universe { ActiveVessel = vessel, Coupled6DofIntegrationEnabled = coupled };
        universe.AddBody(new CelestialBody { Id = "body", Radius = 1, SphereOfInfluence = 1e9 });
        universe.AddVessel(vessel);
        return (universe, vessel, part);
    }

    private static Part Engine(double fuel, double oxidizer, string id = "engine", bool headers = false,
        int runtimeCount = 0)
    {
        var definition = new PartDefinition
        {
            Id = id, CategoryStr = "engine", MassDry = 1000, LengthM = 1, DiameterM = 1,
            ThrustVac = 980.665, ThrustSL = 980.665, IspVac = 100, IspSL = 100,
            MixtureRatio = 3, FuelTypeStr = "liquidfuel", FuelCapacityLF = fuel, FuelCapacityOx = oxidizer,
            HeaderTanks = headers ? [new() { Id = "fuel-header", Propellant = "liquid_fuel",
                CapacityKg = 0.001, CenterYM = 0.2 }] : [],
            AttachmentNodes = [new() { Id = "top", Position = [0, 0.5, 0] },
                new() { Id = "bottom", Position = [0, -0.5, 0] }],
        };
        if (runtimeCount > 0)
        {
            definition.EngineCount = runtimeCount;
            definition.EngineModelId = "synthetic";
            definition.ResolvedEngineModel = new EngineModelDefinition
            {
                Id = "synthetic", RatedThrustSeaLevelN = 980.665, RatedThrustVacuumN = 980.665,
                SpecificImpulseSeaLevelS = 100, SpecificImpulseVacuumS = 100,
                MaximumThrottle = 1, StartupSeconds = 1, ShutdownSeconds = 1,
            };
        }
        var part = new Part(definition, id) { IsStagingActive = true, ThrottleLevel = 1 };
        foreach (var state in part.EngineStates)
        {
            state.State = EngineLifecycleState.Running;
            state.ActualThrottle = state.CommandedThrottle = state.ChamberPressureFraction = 1;
        }
        return part;
    }
}
