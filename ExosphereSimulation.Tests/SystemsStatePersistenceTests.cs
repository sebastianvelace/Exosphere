namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Persistence;
using Exosphere.Simulation.Systems;
using Exosphere.Simulation;
using System.IO;

public sealed class SystemsStatePersistenceTests
{
    [Fact]
    public void SystemSnapshotsRestoreDeadlinesAndBlackoutState()
    {
        var lifeSupport = new LifeSupportSystem();
        lifeSupport.Tick(3_600.0, crewCount: 4, SystemsMissionPhase.Active);

        var power = new PowerSystem();
        power.Tick(1.0, Vector3d.Zero, new Vector3d(150e9, 0.0, 0.0),
            inEclipse: true, extraLoadKw: 2.0);

        var thermal = new ThermalSystem();
        thermal.Tick(0.0, solarVisibility: 0.0, inAtmosphere: false, atmosphericTemp: 3.0,
            aeroHeatFluxWm2: 2.0e6, phase: SystemsMissionPhase.PeakHeating);

        var comms = new CommsSystem();
        var earth = new CelestialBody { Id = "earth", Radius = 6_371_000.0 };
        comms.Tick(0.5, Vector3d.Zero, Vector3d.Zero, [earth], new PlasmaBlackoutInput
        {
            AirspeedMs = 7_500.0,
            DensityKgM3 = 1.0e-5,
            HeatFluxWm2 = 2.0e5,
        });

        var snapshot = new VesselSystemsState
        {
            VesselId = "flight-7",
            SimulationTime = 123.5,
            LifeSupport = lifeSupport.CaptureState(),
            Power = power.CaptureState(),
            Thermal = thermal.CaptureState(),
            Comms = comms.CaptureState(),
        };
        snapshot.Validate();

        var restoredLifeSupport = new LifeSupportSystem();
        var restoredPower = new PowerSystem();
        var restoredThermal = new ThermalSystem();
        var restoredComms = new CommsSystem();
        restoredLifeSupport.RestoreState(snapshot.LifeSupport);
        restoredPower.RestoreState(snapshot.Power);
        restoredThermal.RestoreState(snapshot.Thermal);
        restoredComms.RestoreState(snapshot.Comms);

        Assert.Equal(lifeSupport.OxygenKg, restoredLifeSupport.OxygenKg, precision: 12);
        Assert.Equal(lifeSupport.CO2Kg, restoredLifeSupport.CO2Kg, precision: 12);
        Assert.Equal(power.BatteryKwh, restoredPower.BatteryKwh, precision: 12);
        Assert.Equal(power.GetNextAlertDeadlineSeconds(),
            restoredPower.GetNextAlertDeadlineSeconds());
        Assert.Equal(thermal.GetNextAlertDeadlineSeconds(),
            restoredThermal.GetNextAlertDeadlineSeconds());
        Assert.Equal(comms.PlasmaBlackout, restoredComms.PlasmaBlackout);
        Assert.Equal(comms.PlasmaBlackoutSeconds,
            restoredComms.PlasmaBlackoutSeconds, precision: 12);
    }

    [Fact]
    public void SaveV2RejectsSystemsStateAtAnotherEpochOrVessel()
    {
        var save = new SaveGameV2
        {
            SimulationTime = 100.0,
            ActiveVesselId = "vessel-a",
            Vessels = [new VesselSaveV2 { Id = "vessel-a" }],
            VesselSystems = new()
            {
                ["vessel-a"] = new VesselSystemsState
                {
                    VesselId = "vessel-b",
                    SimulationTime = 99.0,
                },
            },
        };

        var error = Assert.Throws<InvalidDataException>(() => SaveGameV2Codec.Validate(save));
        Assert.Contains("Systems state", error.Message);
    }

    [Fact]
    public void TypedVesselSystemsStateRoundTripsThroughSaveJson()
    {
        var state = new VesselSystemsState
        {
            VesselId = "vessel-a",
            SimulationTime = 50.0,
            Thermal = new ThermalState
            {
                TemperatureK = 321.0,
                HasLastSample = true,
                LastSolarVisibility = 0.25,
                LastInAtmosphere = true,
                LastAtmosphericTemp = 240.0,
                LastAeroHeatFluxWm2 = 12_000.0,
                LastPhase = SystemsMissionPhase.Entry,
            },
        };
        var save = new SaveGameV2
        {
            SimulationTime = 50.0,
            ActiveVesselId = "vessel-a",
            Vessels = [new VesselSaveV2 { Id = "vessel-a" }],
            VesselSystems = new() { ["vessel-a"] = state },
        };

        var decoded = SaveGameV2Json.DeserializeOrMigrate(SaveGameV2Json.Serialize(save));
        var restored = Assert.Single(decoded.VesselSystems).Value;

        Assert.Equal("vessel-a", restored.VesselId);
        Assert.Equal(50.0, restored.SimulationTime);
        Assert.Equal(321.0, restored.Thermal.TemperatureK);
        Assert.Equal(SystemsMissionPhase.Entry, restored.Thermal.LastPhase);
        Assert.Equal(12_000.0, restored.Thermal.LastAeroHeatFluxWm2);
    }

    [Fact]
    public void MaterializedSystemsMapRoundTripsAndRestoresOnlySavedVessels()
    {
        const double epoch = 73.25;
        var source = new VesselSystemsRuntimeRegistry();
        var vesselA = source.Materialize("vessel-a", epoch);
        var vesselB = source.Materialize("vessel-b", epoch);
        vesselA.LifeSupport.RestoreState(new LifeSupportState
        {
            OxygenKg = 180.0,
            WaterKg = 450.0,
            FoodKg = 250.0,
        });
        vesselB.Power.RestoreState(new PowerState
        {
            BatteryKwh = 12.5,
            SolarOutputKw = 4.0,
            ExtraLoadKw = 1.5,
        });

        var save = new SaveGameV2
        {
            SimulationTime = epoch,
            ActiveVesselId = "vessel-a",
            Vessels =
            [
                new VesselSaveV2 { Id = "vessel-a" },
                new VesselSaveV2 { Id = "vessel-b" },
            ],
            VesselSystems = source.CaptureStates(epoch),
        };

        var decoded = SaveGameV2Json.DeserializeOrMigrate(
            SaveGameV2Json.Serialize(save));
        var restored = new VesselSystemsRuntimeRegistry();
        restored.RestoreStates(
            decoded.VesselSystems.Values,
            knownVesselIds: ["vessel-a", "vessel-b", "vessel-c"],
            committedEpoch: epoch);

        Assert.Equal(2, restored.Count);
        Assert.True(restored.TryGet("vessel-a", out var restoredA));
        Assert.True(restored.TryGet("vessel-b", out var restoredB));
        Assert.NotNull(restoredA);
        Assert.NotNull(restoredB);
        Assert.Equal(180.0, restoredA!.LifeSupport.OxygenKg, 12);
        Assert.Equal(12.5, restoredB!.Power.BatteryKwh, 12);
        Assert.False(restored.Contains("vessel-c"));
    }
}
