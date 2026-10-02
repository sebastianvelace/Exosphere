namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Xunit;
using Xunit.Abstractions;

public sealed class Flight14DescentTests(ITestOutputHelper output)
{
    [Fact]
    public void LoadedMissionDescendsWithTheOriginalReserveAndPhysicalState()
    {
        var run = Flight14LaunchDiagnostic.CreateWithDescent(DataDirectory());
        var descent = run.DescentController!;
        var guard = new ControlWriteGuard(descent, run.Ship);
        run.Universe.PhysicsStepController = guard;
        var originalTank = run.Ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("tank"));
        var definition = Definition();
        for (int i = 0; i < (definition.MaximumMissionSeconds + 10) * 60
            && descent.Phase is not (Flight14DescentPhase.DescentReached or Flight14DescentPhase.Blocked); i++)
            run.AdvanceFrame(1.0 / 60);

        Assert.True(descent.Phase == Flight14DescentPhase.DescentReached, descent.BlockReason);
        Assert.False(guard.PhysicalStateChangedByDescentControl);
        Assert.Same(originalTank, run.Ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("tank")));
        Assert.Same(run.ReturnController!.DiagnosticEnd, descent.StartWitness);
        Assert.Equal(26, run.PayloadController.Releases.Count);
        Assert.NotNull(descent.EndWitness);
        var end = descent.EndWitness;
        Assert.InRange(end.GeodeticAltitudeM, definition.DiagnosticEndAltitudeM - 10, definition.DiagnosticEndAltitudeM);
        Assert.InRange(end.AtmosphereRelativeSpeedMps, 1, definition.MaximumEndSpeedMps);
        Assert.True(end.VerticalSpeedMps < 0);
        Assert.True(end.SpecificEnergyJPerKg < descent.StartWitness!.SpecificEnergyJPerKg);
        Assert.Equal(run.ReturnController.PostDeorbitPropellantKg, end.RemainingPropellantKg, 6);
        Assert.Equal(end.RemainingPropellantKg, originalTank.LiquidFuel + originalTank.Oxidizer, 6);
        Assert.Equal(0, run.Ship.Throttle);
        Assert.False(run.Ship.IsDestroyed);
        Assert.False(run.Ship.StructuralControlLost);
        Assert.InRange(end.WindwardShieldDotVelocity, 0.85, 1);
        Assert.InRange(descent.MaximumAngularRateRadPerSecond, 0, 0.15);
        Assert.True(descent.PeakAerodynamicLoadG > 1);
        Assert.True(descent.PeakDynamicPressurePa > 1000);
        Assert.True(descent.PeakStagnationHeatFluxWPerM2 > 100000);
        Assert.Equal(run.Controller.LiftoffEpoch, end.SimulationTimeSeconds - end.MissionElapsedSeconds, 6);
        output.WriteLine($"Deep descent: T+{end.MissionElapsedSeconds:F2}s "
            + $"alt={end.GeodeticAltitudeM:F2}m air={end.AtmosphereRelativeSpeedMps:F2}m/s "
            + $"down={-end.VerticalSpeedMps:F2}m/s reserve={end.RemainingPropellantKg:F2}kg "
            + $"peak load={descent.PeakAerodynamicLoadG:F3}g q={descent.PeakDynamicPressurePa:F1}Pa");
    }

    [Fact]
    public void OwnershipLossStopsCommandsBeforeLaunching()
    {
        var run = Flight14LaunchDiagnostic.CreateWithDescent(DataDirectory());
        run.Ship.Throttle = 1;
        run.Universe.ActiveVessel = null;
        run.DescentController!.BeforePhysicsStep(run.Universe, 0.02);
        Assert.Equal(Flight14DescentPhase.Blocked, run.DescentController.Phase);
        Assert.Equal("active-vessel-changed", run.DescentController.BlockReason);
        Assert.Equal(0, run.Ship.Throttle);
        Assert.Equal(Flight14LaunchPhase.Ignition, run.Controller.Phase);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidControlIntervalsAreRejected(double interval)
    {
        var run = Flight14LaunchDiagnostic.CreateWithDescent(DataDirectory());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            run.DescentController!.BeforePhysicsStep(run.Universe, interval));
    }

    [Theory]
    [InlineData("altitude")]
    [InlineData("speed")]
    [InlineData("angle")]
    [InlineData("pressure")]
    [InlineData("rate")]
    [InlineData("duration")]
    [InlineData("downward-speed")]
    [InlineData("downward-fraction")]
    [InlineData("vertical-response")]
    [InlineData("descent-deadband")]
    public void InvalidEngineeringEnvelopeCannotLoad(string field)
    {
        var definition = Definition();
        switch (field)
        {
            case "altitude": definition.DiagnosticEndAltitudeM = -1; break;
            case "speed": definition.TerminalAngleOfAttackSpeedMps = definition.HypersonicControlThresholdMps; break;
            case "angle": definition.TerminalAngleOfAttackDegrees = 91; break;
            case "pressure": definition.BankFullDynamicPressurePa = definition.BankOnsetDynamicPressurePa; break;
            case "rate": definition.ReferenceSlewRateRadPerSecond = double.NaN; break;
            case "duration": definition.MaximumMissionSeconds = double.PositiveInfinity; break;
            case "downward-speed": definition.MinimumDownwardSpeedMps = 0; break;
            case "downward-fraction": definition.DownwardSpeedFraction = 1; break;
            case "vertical-response": definition.VerticalResponseSeconds = double.NaN; break;
            case "descent-deadband": definition.DownwardSpeedDeadbandMps = -1; break;
        }
        Assert.Throws<InvalidDataException>(definition.Validate);
    }

    private sealed class ControlWriteGuard(Flight14DescentController controller, Vessel ship) : IPhysicsStepController
    {
        public bool PhysicalStateChangedByDescentControl { get; private set; }
        public bool RequiresFixedCadence(Universe universe) => controller.RequiresFixedCadence(universe);
        public void BeforePhysicsStep(Universe universe, double interval)
        {
            if (controller.Phase == Flight14DescentPhase.WaitingForEntry)
            {
                controller.BeforePhysicsStep(universe, interval);
                return;
            }
            var position = ship.Position;
            var velocity = ship.Velocity;
            var orientation = ship.Orientation;
            var angularVelocity = ship.AngularVelocity;
            var resourceAndThermal = ship.Parts.Parts.Select(p =>
                (p.LiquidFuel, p.Oxidizer, p.SkinTemperature, p.Temperature, p.ThermalDamage)).ToArray();
            controller.BeforePhysicsStep(universe, interval);
            PhysicalStateChangedByDescentControl |= position != ship.Position || velocity != ship.Velocity
                    || !orientation.Equals(ship.Orientation) || angularVelocity != ship.AngularVelocity
                    || !resourceAndThermal.SequenceEqual(ship.Parts.Parts.Select(p =>
                        (p.LiquidFuel, p.Oxidizer, p.SkinTemperature, p.Temperature, p.ThermalDamage)));
        }
    }

    private static Flight14DescentDefinition Definition() => Flight14DescentDefinition.LoadFromJson(
        Path.Combine(DataDirectory(), "flight_profiles/starship_flight14_descent_estimate.json"));

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data"))) directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("Repository data not found."), "data");
    }
}
