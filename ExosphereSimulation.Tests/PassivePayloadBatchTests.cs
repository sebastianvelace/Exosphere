namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Xunit;

public sealed class PassivePayloadBatchTests
{
    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data/bodies")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }

    private static (Universe Universe, Vessel Ship, Vessel Payload) Fixture(double altitude = 260000)
    {
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        var earth = universe.GetBody("earth")!;
        Vessel Craft(string id, bool payload)
        {
            var vessel = new Vessel(id)
            {
                Position = earth.Position + Vector3d.Right * (earth.Radius + altitude),
                Velocity = earth.Velocity + Vector3d.Up * System.Math.Sqrt(earth.GM / (earth.Radius + altitude)),
                ReferenceBodyId = earth.Id,
                SASEnabled = true,
            };
            var definition = payload
                ? PartDefinition.LoadFromJson(Path.Combine(DataDirectory(), "parts/starlink_v3_flight14_estimate.json"))
                : new PartDefinition { Id = "batch-carrier", MassDry = 1000, LengthM = 5, DiameterM = 2 };
            vessel.Parts.SetRoot(new Part(definition));
            universe.AddVessel(vessel);
            return vessel;
        }
        var ship = Craft("carrier", false);
        var satellite = Craft("satellite", true);
        satellite.Position += Vector3d.Up * 100;
        universe.SetActiveVessel(ship.Id);
        return (universe, ship, satellite);
    }

    [Theory]
    [InlineData(260000, 120, 1)] // Residual thermospheric drag: must stay RK4, never immortal Kepler rails.
    [InlineData(1500000, 120, 1)]
    [InlineData(260000, 12000, 25)]
    public void BatchedPayloadRetainsGravityDragAndCurrentEpochWhileCarrierMatches50Hz(double altitude, int frames, double positionTolerance)
    {
        var baseline = Fixture(altitude);
        var batch = Fixture(altitude);
        for (int frame = 0; frame < frames; frame++)
        {
            for (int step = 0; step < Universe.MaxPassivePayloadBatchSteps; step++) baseline.Universe.Tick(0.02);
            batch.Universe.TickPassivePayloadBatch(Universe.MaxPassivePayloadBatchSteps, [batch.Payload, batch.Ship], () => false);
        }
        Assert.True(batch.Universe.LastSchedulerTelemetry.DeadlineDeferredSkips > 0);
        Assert.Equal(baseline.Universe.CurrentTime, batch.Universe.CurrentTime);
        Assert.Equal(baseline.Universe.GetBody("earth")!.Position, batch.Universe.GetBody("earth")!.Position);
        Assert.InRange((baseline.Ship.Position - batch.Ship.Position).Magnitude, 0, 1e-6);
        Assert.InRange((baseline.Ship.Velocity - batch.Ship.Velocity).Magnitude, 0, 1e-9);
        // <=0.5 s passive RK4 versus a 20 ms reference, including 100 minutes of coast.
        Assert.InRange((baseline.Payload.Position - batch.Payload.Position).Magnitude, 0, positionTolerance);
        Assert.InRange((baseline.Payload.Velocity - batch.Payload.Velocity).Magnitude, 0, 0.02);
        Assert.Equal(baseline.Payload.TotalMass, batch.Payload.TotalMass);
        Assert.InRange(System.Math.Abs(baseline.Payload.Parts.Parts[0].Temperature - batch.Payload.Parts.Parts[0].Temperature), 0, 1);
    }

    [Fact]
    public void EntryEnvelopeFallsBackToNormalPhysics()
    {
        var baseline = Fixture(80000);
        var batch = Fixture(80000);
        for (int step = 0; step < Universe.MaxPassivePayloadBatchSteps; step++) baseline.Universe.Tick(0.02);
        batch.Universe.TickPassivePayloadBatch(Universe.MaxPassivePayloadBatchSteps, [batch.Payload], () => false);
        Assert.Equal(baseline.Payload.Position, batch.Payload.Position);
        Assert.Equal(baseline.Payload.Velocity, batch.Payload.Velocity);
        Assert.Equal(baseline.Payload.Parts.Parts[0].Temperature, batch.Payload.Parts.Parts[0].Temperature);
    }


    private sealed class WakePayload(Vessel payload) : IPhysicsStepController
    {
        public bool RequiresFixedCadence(Universe universe) => true;
        public void BeforePhysicsStep(Universe universe, double interval)
        {
            if (universe.CurrentTime >= 0.22)
                payload.PitchYawRoll = new Vector3d(0.05, 0, 0);
        }
    }

    [Fact]
    public void MidBatchCommandMaterializesMovingEarthFrameBeforeWakingPayload()
    {
        var baseline = Fixture();
        var batch = Fixture();
        baseline.Universe.PhysicsStepController = new WakePayload(baseline.Payload);
        batch.Universe.PhysicsStepController = new WakePayload(batch.Payload);
        for (int step = 0; step < Universe.MaxPassivePayloadBatchSteps; step++) baseline.Universe.Tick(0.02);
        batch.Universe.TickPassivePayloadBatch(Universe.MaxPassivePayloadBatchSteps, [batch.Payload], () => false);
        Assert.InRange((baseline.Payload.Position - batch.Payload.Position).Magnitude, 0, 0.01);
        Assert.InRange((baseline.Payload.Velocity - batch.Payload.Velocity).Magnitude, 0, 0.001);
        Assert.Equal(baseline.Universe.CurrentTime, batch.Universe.CurrentTime);
        Assert.Equal(baseline.Universe.GetBody("earth")!.Position, batch.Universe.GetBody("earth")!.Position);
        Assert.Equal(baseline.Payload.PitchYawRoll, batch.Payload.PitchYawRoll);
        Assert.False(batch.Payload.IsOnRails);
    }

    [Fact]
    public void InterruptedBatchMaterializesStateAndLeavesNoDeferralForResume()
    {
        var batch = Fixture();
        double advanced = batch.Universe.TickPassivePayloadBatch(Universe.MaxPassivePayloadBatchSteps, [batch.Payload],
            () => batch.Universe.CurrentTime >= 0.34);
        Assert.InRange(advanced, 0.339999, 0.340001);
        var before = batch.Payload.Position;
        batch.Universe.Tick(0.02);
        Assert.NotEqual(before, batch.Payload.Position);
        Assert.Equal(0, batch.Universe.LastSchedulerTelemetry.DeadlineDeferredSkips);
        Assert.Throws<ArgumentOutOfRangeException>(() => batch.Universe.TickPassivePayloadBatch(101, [], () => false));
        batch.Universe.TimeScale = 200;
        Assert.Throws<InvalidOperationException>(() => batch.Universe.TickPassivePayloadBatch(1, [], () => false));
    }
}
