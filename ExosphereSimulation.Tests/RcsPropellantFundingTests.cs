namespace Exosphere.Simulation.Tests;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

public sealed class RcsPropellantFundingTests
{
    [Fact]
    public void SharedDrawCannotSpendPropellantReservedForOwningThrusters()
    {
        var (universe, vessel, owner) = MixedFeed(0.03);
        universe.Tick(0.02);
        Assert.Equal(0.03, owner.Monopropellant);
        Assert.Equal(0.0, vessel.RcsThrusterMassFlowKgPerS);
        Assert.Equal(Vector3d.Zero, vessel.RcsThrusterForceBody);
        Assert.True(vessel.RcsPropellantDepleted);
    }

    [Fact]
    public void AnOwningStoreCannotBorrowFromTheSharedReserve()
    {
        var (universe, vessel, owner) = MixedFeed(0.01);
        var reserve = AddStore(vessel, 1);
        universe.Tick(0.02);
        Assert.Equal(0.01, owner.Monopropellant);
        Assert.Equal(1, reserve.Monopropellant);
        Assert.Equal(Vector3d.Zero, vessel.RcsThrusterForceBody);
        Assert.True(vessel.RcsPropellantDepleted);
    }

    [Fact]
    public void SuccessfulMixedAllocationRecordsBothFundedNozzlesAndTheActualStoreDraw()
    {
        var (universe, vessel, owner) = MixedFeed(0.05);
        vessel.CaptureRcsFunding = true;
        universe.Tick(0.02);
        var evidence = Assert.IsType<Exosphere.Simulation.Physics.RcsPropellantFundingSnapshot>(vessel.LastRcsFunding);
        Assert.False(evidence.UsesInfinitePropellantAssist);
        Assert.Equal(0.02, evidence.DurationSeconds);
        var withdrawal = Assert.Single(evidence.Withdrawals);
        Assert.Equal(owner.InstanceId, withdrawal.PartInstanceId);
        Assert.Equal(0.05, withdrawal.BeforeKg);
        Assert.Equal(owner.Monopropellant, withdrawal.AfterKg);
        Assert.Equal(vessel.RcsThrusterMassFlowKgPerS * 0.02, evidence.OutflowMassKg, 14);
        Assert.Equal(evidence.WithdrawnMassKg, evidence.OutflowMassKg, 14);
        Assert.Equal(new[] { "owned", "shared" }, evidence.Thrusters.Select(flow => flow.ThrusterId));
        Assert.All(evidence.Thrusters, flow =>
        {
            Assert.Equal(Vector3d.Up, flow.ForceDirectionBody);
            Assert.Equal(flow.MassFlowKgPerS * evidence.DurationSeconds, flow.MassKg);
            Assert.True(flow.MassKg > 0);
        });
    }

    [Fact]
    public void SharedPoolProportionsUseOnlyLoadsRemainingAfterOwningReservations()
    {
        var (universe, vessel, owner) = MixedFeed(0.03);
        var reserve = AddStore(vessel, 0.1);
        vessel.CaptureRcsFunding = true;
        universe.Tick(0.02);
        var evidence = vessel.LastRcsFunding!;
        Assert.NotNull(evidence);
        double ownedUse = evidence.Thrusters.Where(flow => flow.PartInstanceId == "owner").Sum(flow => flow.MassKg);
        double sharedUse = evidence.Thrusters.Where(flow => flow.PartInstanceId == "shared").Sum(flow => flow.MassKg);
        double ownFree = 0.03 - ownedUse;
        double sharedFromOwner = sharedUse * ownFree / (ownFree + 0.1);
        Assert.Equal(0.03 - sharedFromOwner - ownedUse, owner.Monopropellant, 14);
        Assert.Equal(0.1 - sharedUse * 0.1 / (ownFree + 0.1), reserve.Monopropellant, 14);
        Assert.Equal(evidence.OutflowMassKg, evidence.WithdrawnMassKg, 14);
        Assert.All(evidence.Withdrawals, draw => Assert.True(draw.AfterKg >= 0));
    }

    [Fact]
    public void AValvedOffStandbyStoreCannotFundTheSharedDemand()
    {
        var (universe, vessel, owner) = MixedFeed(0.03);
        var standby = AddStore(vessel, 1, standby: true);
        vessel.CaptureRcsFunding = true;
        universe.Tick(0.02);
        Assert.Equal(0.03, owner.Monopropellant);
        Assert.Equal(1, standby.Monopropellant);
        Assert.Null(vessel.LastRcsFunding);
        Assert.Equal(Vector3d.Zero, vessel.RcsThrusterForceBody);
    }

    [Fact]
    public void CapturingFundingDoesNotChangeThePhysicalResult()
    {
        var (plain, plainShip, plainOwner) = MixedFeed(0.2);
        var (captured, capturedShip, capturedOwner) = MixedFeed(0.2);
        capturedShip.CaptureRcsFunding = true;
        for (int i = 0; i < 3; i++)
        {
            plain.Tick(0.02); captured.Tick(0.02);
            Assert.Equal(plainOwner.Monopropellant, capturedOwner.Monopropellant);
            Assert.Equal(plainShip.Position, capturedShip.Position);
            Assert.Equal(plainShip.Velocity, capturedShip.Velocity);
            Assert.Equal(plainShip.Orientation, capturedShip.Orientation);
            Assert.Equal(plainShip.AngularVelocity, capturedShip.AngularVelocity);
            Assert.Equal(plainShip.RcsThrusterMassFlowKgPerS, capturedShip.RcsThrusterMassFlowKgPerS);
        }
        Assert.Null(plainShip.LastRcsFunding);
        Assert.NotNull(capturedShip.LastRcsFunding);
    }

    [Fact]
    public void ReceiptSurvivesLaterMutationAndFailedFundingClearsTheLatestReadout()
    {
        var (universe, vessel, owner) = MixedFeed(0.05);
        vessel.CaptureRcsFunding = true;
        universe.Tick(0.02);
        var first = vessel.LastRcsFunding!;
        Assert.NotNull(first);
        double firstMass = first.WithdrawnMassKg;
        double firstEnd = first.Withdrawals[0].AfterKg;
        universe.Tick(0.02);
        Assert.Null(vessel.LastRcsFunding);
        Assert.Equal(firstMass, first.WithdrawnMassKg);
        Assert.Equal(firstEnd, first.Withdrawals[0].AfterKg);
        owner.Monopropellant = 0;
        Assert.Equal(firstEnd, first.Withdrawals[0].AfterKg);
        var list = Assert.IsAssignableFrom<IList<Exosphere.Simulation.Physics.RcsPropellantWithdrawal>>(first.Withdrawals);
        Assert.Throws<NotSupportedException>(() => list[0] = default);
        var nozzles = Assert.IsAssignableFrom<IList<Exosphere.Simulation.Physics.FundedRcsThruster>>(first.Thrusters);
        Assert.Throws<NotSupportedException>(() => nozzles[0] = default);
    }

    [Fact]
    public void InfinitePropellantReceiptsDescribeGrossFundedFlowBeforeReplenishment()
    {
        var (universe, vessel, owner) = MixedFeed(0.05);
        vessel.CaptureRcsFunding = true;
        vessel.InfinitePropellant = true;
        universe.Tick(0.02);
        var evidence = vessel.LastRcsFunding!;
        Assert.NotNull(evidence);
        Assert.True(evidence.UsesInfinitePropellantAssist);
        Assert.Equal(0.05, owner.Monopropellant);
        Assert.True(evidence.WithdrawnMassKg > 0);
        Assert.Equal(evidence.WithdrawnMassKg, evidence.OutflowMassKg, 14);
        Assert.True(evidence.Withdrawals[0].AfterKg < owner.Monopropellant);
    }

    [Fact]
    public void NoFiringOrDisabledCaptureCannotReuseThePreviousReceipt()
    {
        var (universe, vessel, owner) = MixedFeed(0.2);
        vessel.CaptureRcsFunding = true;
        universe.Tick(0.02);
        Assert.NotNull(vessel.LastRcsFunding);
        vessel.TranslationCommand = Vector3d.Zero;
        double before = owner.Monopropellant;
        universe.Tick(0.02);
        Assert.Null(vessel.LastRcsFunding);
        Assert.Equal(before, owner.Monopropellant);
        vessel.TranslationCommand = Vector3d.Up;
        universe.Tick(0.02);
        Assert.NotNull(vessel.LastRcsFunding);
        vessel.CaptureRcsFunding = false;
        universe.Tick(0.02);
        Assert.Null(vessel.LastRcsFunding);
        Assert.True(vessel.RcsThrusterMassFlowKgPerS > 0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReceiptsFollowTheSameFundingOnBothIntegrationPaths(bool coupled)
    {
        var (universe, vessel, owner) = MixedFeed(0.05);
        universe.Coupled6DofIntegrationEnabled = coupled;
        vessel.CaptureRcsFunding = true;
        universe.Tick(0.02);
        Assert.NotNull(vessel.LastRcsFunding);
        Assert.Equal(0.05 - owner.Monopropellant, vessel.LastRcsFunding.WithdrawnMassKg, 14);
        Assert.Equal(vessel.LastRcsFunding.WithdrawnMassKg, vessel.LastRcsFunding.OutflowMassKg, 14);
    }

    [Fact]
    public void ExactSharedPoolExhaustionLeavesEveryStoreAtZero()
    {
        var graph = new PartGraph();
        foreach (double load in new[] { 0.1, 0.2, 0.3 })
        {
            var part = new Part(new PartDefinition { Id = "store", MassDry = 1, FuelCapacityMono = load });
            if (graph.Root == null) graph.SetRoot(part); else graph.AddPart(part);
        }
        Assert.True(graph.DrawMonopropellant(graph.TotalMonopropellant));
        Assert.All(graph.Parts, part => Assert.Equal(0, part.Monopropellant));
    }

    private static Part AddStore(Vessel vessel, double kilograms, bool standby = false)
    {
        var store = new Part(new PartDefinition
        {
            Id = "reserve", MassDry = 1, LengthM = 1, DiameterM = 1, FuelCapacityMono = kilograms,
            RcsAttitudeStartsEnabled = false,
            RcsThrusters = standby ? [new() { Id = "standby", Direction = [0, 1, 0], ThrustN = 1, IspS = 1 }] : [],
            AttachmentNodes = [new() { Id = "bottom", Position = [0, -0.5, 0] }],
        }, "reserve");
        vessel.Parts.AddJoint(new Joint(vessel.Parts.Root!, store, "top", "bottom"));
        return store;
    }

    private static (Universe, Vessel, Part) MixedFeed(double ownedPropellant)
    {
        RcsThrusterDef Jet(string id, double sign = 1) => new()
        {
            Id = id, Position = [0, 0, 0], Direction = [0, sign, 0],
            ThrustN = 9.80665, IspS = 1,
        };
        var owner = new Part(new PartDefinition
        {
            Id = "owner", CategoryStr = "command", MassDry = 100, LengthM = 1, DiameterM = 1,
            FuelCapacityMono = ownedPropellant, RcsThrusters = [Jet("owned"), Jet("owned-reverse", -1)],
            AttachmentNodes = [new() { Id = "top", Position = [0, 0.5, 0] }],
        }, "owner");
        var shared = new Part(new PartDefinition
        {
            Id = "shared", MassDry = 1, LengthM = 1, DiameterM = 1,
            RcsThrusters = [Jet("shared"), Jet("shared-reverse", -1)],
            AttachmentNodes = [new() { Id = "bottom", Position = [0, -0.5, 0] }],
        }, "shared");
        owner.RcsAttitudeEnabled = shared.RcsAttitudeEnabled = true;
        var vessel = new Vessel("mixed-feed")
        {
            Position = Vector3d.Right * 10_000, SASEnabled = false, TranslationCommand = Vector3d.Up,
            ReferenceBodyId = "test-body",
        };
        vessel.Parts.SetRoot(owner);
        vessel.Parts.AddJoint(new Joint(owner, shared, "top", "bottom"));
        var universe = new Universe { ActiveVessel = vessel };
        universe.AddBody(new CelestialBody { Id = "test-body", Radius = 1, SphereOfInfluence = 1e9 });
        universe.AddVessel(vessel);
        return (universe, vessel, owner);
    }
}
