namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Persistence;
using Exosphere.Simulation.Propulsion;
using Xunit;

public sealed class EngineReliabilityTests
{
    private static readonly DirectoryInfo Root = FindRepoRoot();

    [Fact]
    public void MerlinFirstStage_AllowsASecondCommandWithoutFailure()
    {
        var engine = CreateEngine("merlin1d_cluster9_block5", "restart-merlin");
        RunToState(engine, EngineLifecycleState.Running, 1.0);
        RunToState(engine, EngineLifecycleState.Off, 0.0);

        engine.AdvanceEngineRuntime(1.0, 0.02);

        Assert.All(engine.EngineStates, state =>
        {
            Assert.Equal(1, state.StartAttempts);
            Assert.Equal(1, state.StartsCompleted);
            Assert.NotEqual(EngineLifecycleState.Failed, state.State);
            Assert.Null(state.FailureCode);
        });
    }

    [Fact]
    public void MerlinVacuum_AllowsRepeatedRestartsWithoutFailure()
    {
        var engine = CreateEngine("merlin1d_vac_block5", "restart-mvac");

        for (int start = 1; start <= 5; start++)
        {
            RunToState(engine, EngineLifecycleState.Running, 1.0);
            Assert.Equal(start, engine.EngineStates[0].StartsCompleted);
            RunToState(engine, EngineLifecycleState.Off, 0.0);
        }

        engine.AdvanceEngineRuntime(1.0, 0.02);

        var state = Assert.Single(engine.EngineStates);
        Assert.Equal(5, state.StartAttempts);
        Assert.Equal(5, state.StartsCompleted);
        Assert.NotEqual(EngineLifecycleState.Failed, state.State);
        Assert.Null(state.FailureCode);
    }

    [Fact]
    public void ScheduledIgnitionFault_IsIgnoredByTheRuntime()
    {
        var engine = CreateEngine("merlin1d_cluster9_block5", "fault-octaweb");
        string targetId = engine.EngineStates[3].InstanceId;
        engine.ScheduleEngineFailure(new EngineFailureInjection
        {
            EngineInstanceId = targetId,
            TriggerState = EngineLifecycleState.Ignition,
            TriggerStartAttempt = 1,
            TriggerAfterStateSeconds = 0.04,
            FailureCode = "IGNITER_NO_LIGHT",
        });

        for (int i = 0; i < 150; i++)
            engine.AdvanceEngineRuntime(1.0, 0.02);

        Assert.Contains(engine.EngineStates, state => state.InstanceId == targetId);
        Assert.All(engine.EngineStates, state =>
        {
            Assert.NotEqual(EngineLifecycleState.Failed, state.State);
            Assert.Null(state.FailureCode);
        });
        Assert.Equal(9, engine.EngineStates.Count(
            state => state.State == EngineLifecycleState.Running));
        Assert.Empty(engine.ScheduledEngineFailures);
    }

    [Fact]
    public void OvertemperatureDoesNotCreateAnEngineFailure()
    {
        var engine = CreateEngine("merlin1d_vac_block5", "hot-mvac");
        var state = Assert.Single(engine.EngineStates);
        state.TemperatureK =
            engine.Definition.ResolvedEngineModel!.MaximumSafeTemperatureK + 0.1;

        engine.AdvanceEngineRuntime(1.0, 0.02);

        Assert.NotEqual(EngineLifecycleState.Failed, state.State);
        Assert.Null(state.FailureCode);
        Assert.Equal(0.0, state.ChamberPressureFraction);
    }

    [Fact]
    public void PendingFailureSchedule_SurvivesSaveV2RoundTrip()
    {
        var catalog = LoadCatalog();
        var vessel = new Vessel("scheduled-failure-vessel");
        var engine = new Part(
            catalog["merlin1d_vac_block5"],
            "scheduled-mvac");
        vessel.Parts.SetRoot(engine);
        RunToState(engine, EngineLifecycleState.Running, 1.0);
        engine.ScheduleEngineFailure(new EngineFailureInjection
        {
            EngineInstanceId = engine.EngineStates[0].InstanceId,
            TriggerState = EngineLifecycleState.Shutdown,
            TriggerStartAttempt = 1,
            TriggerAfterStateSeconds = 0.05,
            FailureCode = "SHUTDOWN_VALVE_STUCK",
        });
        var universe = new Universe();
        universe.AddVessel(vessel);
        universe.SetActiveVessel(vessel.Id);

        string json = SaveGameV2Json.Serialize(SaveGameV2Codec.Capture(universe));
        var restoredUniverse = new Universe();
        SaveGameV2Codec.Restore(
            restoredUniverse,
            SaveGameV2Json.DeserializeOrMigrate(json),
            catalog);
        var restored = Assert.Single(restoredUniverse.Vessels).Parts.Root!;

        Assert.Empty(restored.ScheduledEngineFailures);
        for (int i = 0; i < 10; i++)
            restored.AdvanceEngineRuntime(0.0, 0.02);

        var restoredState = Assert.Single(restored.EngineStates);
        Assert.NotEqual(EngineLifecycleState.Failed, restoredState.State);
        Assert.Null(restoredState.FailureCode);
        Assert.Empty(restored.ScheduledEngineFailures);
    }

    [Fact]
    public void TestStandIgnoresLegacyFailureInjection()
    {
        var definition = LoadCatalog()["merlin1d_cluster9_block5"];
        var profile = EngineTestProfile.Merlin1DAcceptance();
        profile.FailureInjections.Add(new EngineFailureInjection
        {
            EngineInstanceId =
                "test-stand:merlin1d_cluster9_block5:engine:02",
            TriggerState = EngineLifecycleState.Ignition,
            TriggerStartAttempt = 1,
            TriggerAfterStateSeconds = 0.02,
            FailureCode = "TEST_IGNITION_FAILURE",
        });

        var report = EngineTestStand.Run(definition, profile, 0.02);

        Assert.True(report.Passed);
        Assert.Empty(report.FailureCodes);
        Assert.DoesNotContain(report.Telemetry,
            row => row.Phase == EngineTestPhase.Failed);
    }

    private static Part CreateEngine(string definitionId, string instanceId) =>
        new(LoadCatalog()[definitionId], instanceId);

    private static void RunToState(
        Part engine,
        EngineLifecycleState expected,
        double command)
    {
        for (int i = 0; i < 500; i++)
        {
            engine.AdvanceEngineRuntime(command, 0.02);
            if (engine.EngineStates.All(state => state.State == expected))
                return;
        }
        throw new Xunit.Sdk.XunitException(
            $"Engine did not reach {expected}.");
    }

    private static PartCatalog LoadCatalog() =>
        PartCatalog.LoadFromDirectory(
            Path.Combine(Root.FullName, "data", "parts"));

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName, "ExosphereSimulation.sln")))
                return directory;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }
}
