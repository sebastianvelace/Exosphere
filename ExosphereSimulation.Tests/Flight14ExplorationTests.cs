namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Xunit;

public sealed class Flight14ExplorationTests
{
    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data/flight_profiles")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }

    [Fact]
    public void GameplayFactoryRetainsMovingEarthEphemerisAndInitialPadVelocity()
    {
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        var earth = universe.GetBody("earth")!;
        var position = earth.Position; var velocity = earth.Velocity; var elements = earth.OrbitalElements;
        int bodies = universe.Bodies.Count;
        var run = Flight14LaunchDiagnostic.CreateWithPoweredReturn(DataDirectory(), universe);
        Assert.Same(universe, run.Universe);
        Assert.Same(earth, run.Earth);
        Assert.Equal(bodies, universe.Bodies.Count);
        Assert.Same(elements, earth.OrbitalElements);
        Assert.Equal(position, earth.Position); Assert.Equal(velocity, earth.Velocity);
        Assert.NotNull(universe.GetBody("sun"));
        Assert.True(velocity.Magnitude > 10000);
        Assert.InRange(run.Ship.GetSurfaceVelocity(earth).Magnitude, 0, 0.01);
        Assert.Equal(26, run.Ship.Parts.Parts.Count(p => p.Definition.HasVehicleRole("payload")));
        Assert.Same(run.PoweredReturnController, universe.PhysicsStepController);
        Assert.Throws<InvalidOperationException>(() => Flight14LaunchDiagnostic.Create(DataDirectory(), universe));
    }

    [Theory]
    [InlineData(30, 10)]
    [InlineData(120, 10)]
    [InlineData(120, 200)]
    [InlineData(240, 200)]
    public void WarpAndRenderCadenceMatchWholeStepIgnitionAndAscent(int fps, int warp)
    {
        var baseline = Flight14LaunchDiagnostic.CreateWithPoweredReturn(DataDirectory());
        var accelerated = Flight14LaunchDiagnostic.CreateWithPoweredReturn(DataDirectory());
        var preview = new Flight14Exploration(accelerated);
        for (int i = 0; i < 1000; i++) baseline.Universe.Tick(0.02);
        accelerated.Universe.TimeScale = warp;
        for (int i = 0; i < 20 * fps / warp; i++) preview.AdvanceFrame(1.0 / fps);
        Assert.Equal(warp, accelerated.Universe.TimeScale);
        Assert.InRange(System.Math.Abs(baseline.Universe.CurrentTime - accelerated.Universe.CurrentTime), 0, 1e-10);
        Assert.Equal(baseline.Controller.Phase, accelerated.Controller.Phase);
        Assert.InRange((baseline.Ship.Position - accelerated.Ship.Position).Magnitude, 0, 1e-6);
        Assert.InRange((baseline.Ship.Velocity - accelerated.Ship.Velocity).Magnitude, 0, 1e-6);
        Assert.InRange(System.Math.Abs(baseline.Ship.TotalMass - accelerated.Ship.TotalMass), 0, 1e-6);
    }

    [Fact]
    public void PauseAndCpuBudgetDoNotCommitOrQueueUnboundedSimulationTime()
    {
        var run = Flight14LaunchDiagnostic.CreateWithPoweredReturn(DataDirectory());
        var preview = new Flight14Exploration(run) { IsPaused = true };
        run.Universe.TimeScale = 200;
        Assert.Equal(0, preview.AdvanceFrame(30));
        Assert.Equal(0, run.Universe.CurrentTime);
        Assert.Equal(0, run.Ship.Throttle);
        preview.IsPaused = false;
        Assert.InRange(preview.AdvanceFrame(30, 2), 0.039999, 0.040001);
        run.Universe.TimeScale = 1;
        Assert.InRange(preview.AdvanceFrame(0.02, 2), 0.019999, 0.020001);
        Assert.Equal(1, run.Universe.TimeScale);
    }

    [Fact]
    public void MovingSolarSystemPreviewPropagatesLoadedShipToWaterEntryAndFreezes()
    {
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        var run = Flight14LaunchDiagnostic.CreateWithPoweredReturn(DataDirectory(), universe, continueToWaterContact: true);
        var preview = new Flight14Exploration(run);
        universe.TimeScale = 200;
        var shipId = run.Ship.Id;
        for (int i = 0; i < 16000 && !preview.IsStopped; i++) preview.AdvanceFrame(0.02);
        Assert.Null(preview.BlockReason);
        Assert.False(run.Ship.IsDestroyed);
        Assert.True(preview.IsTerminal, preview.Phase);
        Assert.Equal(shipId, universe.ActiveVessel!.Id);
        Assert.Equal(26, run.PayloadController.Releases.Count);
        Assert.NotNull(run.PoweredReturnController!.Landing!.WaterEntryWitness);
        Assert.Equal(Flight14LandingPhase.SplashdownReached, run.PoweredReturnController.Landing.Phase);
        Assert.InRange(run.PoweredReturnController.Landing.WaterEntryWitness.AtmosphereRelativeSpeedMps, 0, 5);
        Assert.NotNull(run.Ship.LastWaterContact);
        Assert.True(run.Ship.LastWaterContact.Value.LowestPointAltitudeM < 0);
        Assert.False(run.Ship.IsGroundHeld);
        var up = run.Earth.GetGeodeticUp(run.Ship.Position);
        Assert.True(run.Ship.Orientation.Rotate(Exosphere.Simulation.Math.Vector3d.Up).Dot(up)
            > System.Math.Cos(15*Exosphere.Simulation.Math.MathUtils.DEG_TO_RAD));
        Assert.NotNull(run.PoweredReturnController!.Landing!.EndWitness);
        double epoch = universe.CurrentTime;
        var position = run.Ship.Position; var velocity = run.Ship.Velocity; double mass = run.Ship.TotalMass;
        Assert.Equal(0, preview.AdvanceFrame(10));
        Assert.Equal(epoch, universe.CurrentTime);
        Assert.Equal(position, run.Ship.Position); Assert.Equal(velocity, run.Ship.Velocity);
        Assert.Equal(mass, run.Ship.TotalMass);
        Assert.False(run.Ship.IsAttemptingTowerCatch);
    }
}
