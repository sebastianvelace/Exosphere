namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Data;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Propulsion;
using Xunit;

public sealed class EngineDefinitionCatalogTests
{
    private static readonly DirectoryInfo Root = FindRepoRoot();

    [Fact]
    public void MerlinCatalog_LoadsTracedModelsAndStableOctawebInstances()
    {
        var provenance = DataProvenanceRegistry.LoadFromDirectory(
            Path.Combine(Root.FullName, "data", "provenance"));
        var catalog = EngineDefinitionCatalog.Load(
            Path.Combine(Root.FullName, "data", "engines"),
            Path.Combine(Root.FullName, "data", "engine_clusters"),
            provenance);

        var merlin = catalog.Models["merlin1d-block5-2025"];
        var vacuum = catalog.Models["merlin1d-vac-block5-2025"];
        var octaweb = catalog.Clusters["falcon9-block5-octaweb-2025"];

        Assert.Equal(845_000.0, merlin.RatedThrustSeaLevelN);
        Assert.Equal(981_000.0, vacuum.RatedThrustVacuumN);
        Assert.Equal(9, octaweb.Engines.Count);
        Assert.Equal(9, octaweb.FeedNetwork.Branches.Count);
        Assert.Equal(7_605_000.0, merlin.RatedThrustSeaLevelN * octaweb.Engines.Count);
        Assert.Equal(
            octaweb.Engines.Count,
            octaweb.Engines.Select(e => e.InstanceId)
                .Distinct(StringComparer.Ordinal).Count());
        Assert.All(octaweb.Engines, engine =>
        {
            Assert.True(double.IsFinite(engine.Position.Magnitude));
            Assert.InRange(engine.Direction.Normalized.Magnitude, 0.999999, 1.000001);
        });
        var partCatalog = PartCatalog.LoadFromDirectory(
            Path.Combine(Root.FullName, "data", "parts"));
        Assert.Equal(
            octaweb.Id,
            partCatalog["merlin1d_cluster9_block5"].EngineClusterId);
        Assert.Equal(
            merlin.Id,
            partCatalog["merlin1d_cluster9_block5"].EngineModelId);

        var vehicle = VehicleVariantDefinition.LoadFromJson(
            Path.Combine(
                Root.FullName,
                "data",
                "vehicles",
                "falcon9_block5_standard_2025.json"));
        Assert.Equal(
            new[]
            {
                "falcon9-block5-octaweb-2025",
                "falcon9-block5-second-stage-2025",
            },
            vehicle.EngineClusterIds);
        Assert.All(vehicle.EngineClusterIds, id => Assert.True(catalog.Clusters.ContainsKey(id)));

        var standReport = EngineTestStand.Run(
            merlin,
            EngineTestProfile.Merlin1DAcceptance(),
            stepSeconds: 0.02);
        Assert.True(standReport.Passed);
        Assert.Equal(merlin.Id, standReport.EngineDefinitionId);
        Assert.InRange(standReport.PeakThrustN, 844_000.0, 845_001.0);
    }

    [Fact]
    public void EveryOperationalMerlinField_HasProvenance()
    {
        var provenance = DataProvenanceRegistry.LoadFromDirectory(
            Path.Combine(Root.FullName, "data", "provenance"));

        foreach (string modelId in new[]
                 {
                     "merlin1d-block5-2025",
                     "merlin1d-vac-block5-2025",
                 })
        {
            provenance.RequireFields(
                modelId,
                "ratedThrustSeaLevelN",
                "ratedThrustVacuumN",
                "specificImpulseSeaLevelS",
                "specificImpulseVacuumS",
                "minimumThrottle",
                "gimbalEnvelope",
                "startupTransient",
                "shutdownTransient");
        }
    }

    [Fact]
    public void InstanceStateAndTelemetry_KeepPerEngineIdentity()
    {
        var state = new EngineInstanceState
        {
            InstanceId = "m1-ring-03",
            EngineModelId = "merlin1d-block5-2025",
            State = EngineLifecycleState.Ignition,
            CommandedThrottle = 0.7,
            ActualThrottle = 0.4,
        };
        var telemetry = new EngineTelemetry(
            state.InstanceId,
            state.State,
            state.CommandedThrottle,
            state.ActualThrottle,
            338_000.0,
            122.0,
            0.4,
            2.56,
            state.GimbalDeg,
            850.0,
            null);

        Assert.Equal(state.InstanceId, telemetry.InstanceId);
        Assert.Equal(EngineLifecycleState.Ignition, telemetry.State);
        Assert.Null(telemetry.FailureCode);
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
                return directory;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
