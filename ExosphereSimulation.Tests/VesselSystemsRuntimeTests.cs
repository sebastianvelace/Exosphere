namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Systems;
using Xunit;

public sealed class VesselSystemsRuntimeTests
{
    [Fact]
    public void IdenticalRuntimesAreDeterministicAndDoNotShareState()
    {
        var left = new VesselSystemsRuntime("left", simulationTime: 0.0);
        var right = new VesselSystemsRuntime("right", simulationTime: 0.0);
        VesselSystemsTickInput first = SampleInput(
            deltaSeconds: 0.02,
            simulationTime: 0.02,
            phase: SystemsMissionPhase.Entry);

        left.Tick(first);
        right.Tick(first);

        AssertStateEqual(left.CaptureState(), right.CaptureState(), compareId: false);
        double rightOxygenBefore = right.LifeSupport.OxygenKg;
        double rightBatteryBefore = right.Power.BatteryKwh;

        left.Tick(SampleInput(
            deltaSeconds: 1.0,
            simulationTime: 1.02,
            phase: SystemsMissionPhase.PeakHeating,
            solarVisibility: 0.0,
            aeroHeatFluxWm2: 2.0e6));

        Assert.Equal(rightOxygenBefore, right.LifeSupport.OxygenKg, 12);
        Assert.Equal(rightBatteryBefore, right.Power.BatteryKwh, 12);
        Assert.Equal(0.02, right.SimulationTime, 12);
        Assert.Equal(1.02, left.SimulationTime, 12);
        Assert.True(left.HasSystemsAlert || !left.Thermal.HotAlert);
    }

    [Fact]
    public void SnapshotRestorePreservesEpochDeadlinesAndBlackoutState()
    {
        var source = new VesselSystemsRuntime("flight-7", simulationTime: 10.0);
        source.Tick(SampleInput(
            deltaSeconds: 0.5,
            simulationTime: 10.5,
            phase: SystemsMissionPhase.PeakHeating,
            solarVisibility: 0.0,
            aeroHeatFluxWm2: 2.0e6,
            entryCondition: new PlasmaBlackoutInput
            {
                AirspeedMs = 7_500.0,
                DensityKgM3 = 1.0e-5,
                HeatFluxWm2 = 2.0e5,
            }));

        VesselSystemsState snapshot = source.CaptureState();
        var restored = new VesselSystemsRuntime("flight-7");
        restored.RestoreState(snapshot);

        AssertStateEqual(snapshot, restored.CaptureState());
        Assert.Equal(source.CurrentPhase, restored.CurrentPhase);
        Assert.Equal(
            source.GetNextAlertDeadlineSeconds(crewCount: 4),
            restored.GetNextAlertDeadlineSeconds(crewCount: 4));
        Assert.True(restored.Comms.PlasmaBlackout);

        VesselSystemsTickInput next = SampleInput(
            deltaSeconds: 0.02,
            simulationTime: 10.52,
            phase: SystemsMissionPhase.PeakHeating,
            solarVisibility: 0.0,
            aeroHeatFluxWm2: 1.0e6,
            entryCondition: new PlasmaBlackoutInput
            {
                AirspeedMs = 7_000.0,
                DensityKgM3 = 1.0e-5,
                HeatFluxWm2 = 1.5e5,
            });
        source.Tick(next);
        restored.Tick(next);
        AssertStateEqual(source.CaptureState(), restored.CaptureState());
    }

    [Fact]
    public void NonContiguousOrInvalidEpochCannotAdvanceRuntime()
    {
        var runtime = new VesselSystemsRuntime("epoch-test", simulationTime: 4.0);

        Assert.Throws<InvalidOperationException>(() => runtime.Tick(SampleInput(
            deltaSeconds: 0.02,
            simulationTime: 4.01)));
        Assert.Equal(4.0, runtime.SimulationTime, 12);

        runtime.Tick(SampleInput(
            deltaSeconds: 0.02,
            simulationTime: 4.02));
        Assert.Throws<InvalidOperationException>(() => runtime.Tick(SampleInput(
            deltaSeconds: 0.02,
            simulationTime: 4.03)));
        Assert.Equal(4.02, runtime.SimulationTime, 12);

        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.Tick(SampleInput(
            deltaSeconds: double.NaN,
            simulationTime: double.NaN)));
        Assert.Equal(4.02, runtime.SimulationTime, 12);
    }

    [Fact]
    public void AlertDeadlineIsTheEarliestSubsystemProjectionAndResetKeepsIdentity()
    {
        var runtime = new VesselSystemsRuntime("deadline-test");
        runtime.RestoreState(new VesselSystemsState
        {
            VesselId = "deadline-test",
            SimulationTime = 25.0,
            LifeSupport = new LifeSupportState
            {
                OxygenKg = 39.0,
                CO2Kg = 0.0,
                WaterKg = 500.0,
                FoodKg = 300.0,
                CrewAlive = true,
            },
            Power = new PowerState
            {
                BatteryKwh = 49.0,
                SolarOutputKw = 0.0,
                ExtraLoadKw = 0.0,
            },
            Thermal = new ThermalState
            {
                TemperatureK = 293.0,
                HasLastSample = true,
                LastSolarVisibility = 0.0,
                LastInAtmosphere = false,
                LastAtmosphericTemp = 3.0,
                LastAeroHeatFluxWm2 = 0.0,
                LastPhase = SystemsMissionPhase.Active,
            },
        });

        Assert.Equal(0.0, runtime.GetNextAlertDeadlineSeconds(crewCount: 4));
        Assert.True(runtime.HasSystemsAlert);

        runtime.Reset(simulationTime: 25.0);
        Assert.Equal("deadline-test", runtime.VesselId);
        Assert.Equal(25.0, runtime.SimulationTime, 12);
        Assert.False(runtime.HasSystemsAlert);
        Assert.True(runtime.GetNextAlertDeadlineSeconds(crewCount: 4) > 0.0);
    }

    [Fact]
    public void RestoreRejectsAnotherVesselWithoutMutatingRuntime()
    {
        var runtime = new VesselSystemsRuntime("owner-a", simulationTime: 8.0);
        VesselSystemsState foreign = new()
        {
            VesselId = "owner-b",
            SimulationTime = 9.0,
        };

        Assert.Throws<InvalidDataException>(() => runtime.RestoreState(foreign));
        Assert.Equal("owner-a", runtime.VesselId);
        Assert.Equal(8.0, runtime.SimulationTime, 12);
        Assert.Equal(200.0, runtime.LifeSupport.OxygenKg, 12);
    }

    private static VesselSystemsTickInput SampleInput(
        double deltaSeconds,
        double simulationTime,
        SystemsMissionPhase phase = SystemsMissionPhase.Active,
        double solarVisibility = 0.5,
        double aeroHeatFluxWm2 = 0.0,
        PlasmaBlackoutInput entryCondition = default)
    {
        var earth = new CelestialBody
        {
            Id = "earth",
            Radius = 6_371_000.0,
        };
        return new VesselSystemsTickInput(
            DeltaSeconds: deltaSeconds,
            SimulationTime: simulationTime,
            CrewCount: 4,
            Phase: phase,
            VesselPosition: new Vector3d(7_000_000.0, 0.0, 0.0),
            EarthPosition: Vector3d.Zero,
            SunPosition: new Vector3d(149.6e9, 0.0, 0.0),
            Bodies: [earth],
            SolarVisibility: solarVisibility,
            InAtmosphere: phase is SystemsMissionPhase.Entry
                or SystemsMissionPhase.PeakHeating,
            AtmosphericTemperatureK: 240.0,
            AeroHeatFluxWm2: aeroHeatFluxWm2,
            EntryCondition: entryCondition);
    }

    private static void AssertStateEqual(
        VesselSystemsState expected,
        VesselSystemsState actual,
        bool compareId = true)
    {
        if (compareId)
            Assert.Equal(expected.VesselId, actual.VesselId);
        Assert.Equal(expected.SimulationTime, actual.SimulationTime, 12);
        Assert.Equal(expected.LifeSupport.OxygenKg, actual.LifeSupport.OxygenKg, 12);
        Assert.Equal(expected.LifeSupport.CO2Kg, actual.LifeSupport.CO2Kg, 12);
        Assert.Equal(expected.LifeSupport.WaterKg, actual.LifeSupport.WaterKg, 12);
        Assert.Equal(expected.LifeSupport.FoodKg, actual.LifeSupport.FoodKg, 12);
        Assert.Equal(expected.LifeSupport.CrewAlive, actual.LifeSupport.CrewAlive);
        Assert.Equal(expected.Power.BatteryKwh, actual.Power.BatteryKwh, 12);
        Assert.Equal(expected.Power.SolarOutputKw, actual.Power.SolarOutputKw, 12);
        Assert.Equal(expected.Power.ExtraLoadKw, actual.Power.ExtraLoadKw, 12);
        Assert.Equal(expected.Thermal.TemperatureK, actual.Thermal.TemperatureK, 12);
        Assert.Equal(expected.Thermal.HasLastSample, actual.Thermal.HasLastSample);
        Assert.Equal(
            expected.Thermal.LastAeroHeatFluxWm2,
            actual.Thermal.LastAeroHeatFluxWm2,
            12);
        Assert.Equal(expected.Thermal.LastPhase, actual.Thermal.LastPhase);
        Assert.Equal(expected.Comms.HasSignal, actual.Comms.HasSignal);
        Assert.Equal(expected.Comms.SignalStrength, actual.Comms.SignalStrength, 12);
        Assert.Equal(expected.Comms.SignalDelaySeconds, actual.Comms.SignalDelaySeconds, 12);
        Assert.Equal(
            expected.Comms.PlasmaBlackout.IsBlackedOut,
            actual.Comms.PlasmaBlackout.IsBlackedOut);
        Assert.Equal(
            expected.Comms.PlasmaBlackout.ElapsedSeconds,
            actual.Comms.PlasmaBlackout.ElapsedSeconds,
            12);
    }
}
