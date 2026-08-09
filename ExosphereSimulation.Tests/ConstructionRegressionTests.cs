namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Construction;
using System.Text.Json;
using Xunit;

public sealed class ConstructionRegressionTests
{
    [Fact]
    public void CatalogLoadsPartsFromJsonDirectory()
    {
        var catalog = LoadCatalog();

        Assert.True(catalog.Parts.Count >= 10);
        Assert.True(catalog.TryGet("starship_command", out var command));
        Assert.Equal("command", command.CategoryStr);
        Assert.Contains(command.AttachmentNodes, n => n.Id == "bottom");
    }

    [Fact]
    public void AssemblyValidatesCompatibleAttachmentNodes()
    {
        var assembly = new VesselAssembly(LoadCatalog());
        var root = assembly.AddRoot("starship_command");
        var tank = assembly.AttachPart(root.InstanceId, "bottom", "starship_tank", "top");

        Assert.Equal(2, assembly.Parts.Count);
        Assert.Single(assembly.Connections);
        Assert.Equal(root.InstanceId, tank.ParentInstanceId);
        Assert.DoesNotContain(assembly.AvailableNodes(root.InstanceId), n => n.Id == "bottom");
    }

    [Fact]
    public void AssemblyRejectsIncompatibleConnections()
    {
        var assembly = new VesselAssembly(LoadCatalog());
        var root = assembly.AddRoot("command_pod_mk1");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            assembly.AttachPart(root.InstanceId, "bottom", "fuel_tank_large", "top"));

        Assert.Contains("not compatible", ex.Message);
    }

    [Fact]
    public void MetricsRecalculateMassPropellantTwrAndDeltaV()
    {
        var assembly = BuildStarshipLikeAssembly();
        var metrics = assembly.ComputeMetrics();

        Assert.True(metrics.WetMass > metrics.DryMass);
        Assert.True(metrics.PropellantMass > 0.0);
        Assert.Equal(74_400_000.0, metrics.SeaLevelThrust);
        Assert.True(metrics.SeaLevelTwr > 1.0);
        Assert.InRange(metrics.VacuumDeltaV, 3_900.0, 4_200.0);
    }

    [Fact]
    public void ExportCreatesVesselWithPartGraphAndJoints()
    {
        var vessel = BuildStarshipLikeAssembly().ToVessel("VAB Export");

        Assert.Equal("VAB Export", vessel.Name);
        Assert.Equal(5, vessel.Parts.Parts.Count);
        Assert.Equal(4, vessel.Parts.Joints.Count);
        Assert.NotNull(vessel.Parts.Root);
        Assert.True(vessel.TotalMass > 0.0);
    }

    [Fact]
    public void CraftDefinitionRoundTripsThroughJsonAndRebuildsAssembly()
    {
        var original = BuildStarshipLikeAssembly();
        var craft = original.ToCraft("Round Trip");
        string json = JsonSerializer.Serialize(craft);
        var restoredCraft = JsonSerializer.Deserialize<VesselCraftDefinition>(json)!;

        var restored = VesselAssembly.FromCraft(LoadCatalog(), restoredCraft);

        Assert.Equal("Round Trip", restoredCraft.Name);
        Assert.Equal(original.Parts.Count, restored.Parts.Count);
        Assert.Equal(original.Connections.Count, restored.Connections.Count);
        Assert.Equal(original.ComputeMetrics().WetMass, restored.ComputeMetrics().WetMass);
        Assert.Equal(5, restored.ToVessel("Restored").Parts.Parts.Count);
    }

    [Fact]
    public void CraftDocumentV2IsAuthoritativeAndPreservesPartIdsIntoFlight()
    {
        var original = BuildStarshipLikeAssembly();
        var document = original.ToCraftDocument("V2 Round Trip");
        document.VehicleVariantId = "starship-flight-7-block-2";
        document.Stages.Add(new CraftStageV2
        {
            Sequence = 0,
            Actions =
            [
                new CraftActionV2
                {
                    Kind = CraftActionKind.EngineStart,
                    TargetInstanceId = document.Parts[^1].InstanceId,
                },
            ],
        });

        string json = JsonSerializer.Serialize(document);
        var decoded = JsonSerializer.Deserialize<CraftDocumentV2>(json)!;
        var restored = VesselAssembly.FromCraft(LoadCatalog(), decoded);
        var vessel = restored.ToVessel(decoded.Name);

        Assert.Equal(2, decoded.FormatVersion);
        Assert.Equal("m", decoded.LengthUnit);
        Assert.Equal("kg", decoded.MassUnit);
        Assert.Single(decoded.Stages);
        Assert.Equal(
            decoded.Parts.Select(p => p.InstanceId).Order(),
            vessel.Parts.Parts.Select(p => p.InstanceId).Order());
    }

    [Fact]
    public void PayloadDeclarationRoundTripsWithMeasuredMassAndIndependentFlag()
    {
        var original = BuildStarshipLikeAssembly();
        var payloadPart = original.Parts.First(p => p.DefinitionId == "starship_command");
        var declaration = original.MarkPayload(
            payloadPart.InstanceId,
            "WeatherSat-1",
            declaredMassKg: 2_450.0,
            becomesIndependentVessel: false);

        var document = original.ToCraftDocument("Payload integration");
        string json = JsonSerializer.Serialize(document);
        var restoredDocument = JsonSerializer.Deserialize<CraftDocumentV2>(json)!;
        var restored = VesselAssembly.FromCraft(LoadCatalog(), restoredDocument);

        var restoredPayload = Assert.Single(restored.PayloadManifest);
        Assert.Equal(declaration.PayloadId, restoredPayload.PayloadId);
        Assert.Equal("WeatherSat-1", restoredPayload.Name);
        Assert.Equal(2_450.0, restoredPayload.DeclaredMassKg);
        Assert.False(restoredPayload.BecomesIndependentVessel);
        Assert.Equal(payloadPart.InstanceId, restoredPayload.PartInstanceId);
    }

    [Fact]
    public void DeletingPayloadHardwareRemovesStaleManifestEntry()
    {
        var assembly = BuildStarshipLikeAssembly();
        var payloadPart = assembly.Parts.First(p => p.DefinitionId == "starship_command");
        assembly.MarkPayload(payloadPart.InstanceId, "Payload");

        Assert.True(assembly.DeletePart(payloadPart.InstanceId));
        Assert.Empty(assembly.PayloadManifest);
    }

    [Fact]
    public void PayloadMassMustBeFiniteAndPositive()
    {
        var assembly = BuildStarshipLikeAssembly();
        var payloadPart = assembly.Parts.First();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            assembly.MarkPayload(payloadPart.InstanceId, declaredMassKg: 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            assembly.MarkPayload(payloadPart.InstanceId, declaredMassKg: double.NaN));
    }

    [Fact]
    public void DeletingPartRemovesItsSubtreeAndFreesParentNode()
    {
        var assembly = new VesselAssembly(LoadCatalog());
        var root = assembly.AddRoot("starship_command");
        var tank = assembly.AttachPart(root.InstanceId, "bottom", "starship_tank", "top");
        assembly.AttachPart(tank.InstanceId, "bottom", "starship_engines", "top");

        Assert.True(assembly.DeletePart(tank.InstanceId));

        Assert.Single(assembly.Parts);
        Assert.Empty(assembly.Connections);
        Assert.Contains(assembly.AvailableNodes(root.InstanceId), n => n.Id == "bottom");
    }

    [Fact]
    public void AutomaticAttachmentChoosesACompatibleFreeNode()
    {
        var assembly = new VesselAssembly(LoadCatalog());
        var command = assembly.AddRoot("starship_command");

        var tank = assembly.AttachPartAutomatically(command.InstanceId, "starship_tank");

        Assert.Equal(command.InstanceId, tank.ParentInstanceId);
        Assert.Equal("bottom", tank.ParentNodeId);
        Assert.Equal("top", tank.ChildNodeId);
        Assert.Empty(assembly.CompatibleAttachments(command.InstanceId, "starship_tank"));
    }

    [Fact]
    public void LaunchValidationExplainsIncompleteAndFlightReadyCrafts()
    {
        var incomplete = new VesselAssembly(LoadCatalog());
        incomplete.AddRoot("starship_command");

        var invalid = incomplete.ValidateForLaunch();
        var valid = BuildStarshipLikeAssembly().ValidateForLaunch();

        Assert.False(invalid.CanLaunch);
        Assert.Contains(invalid.Errors, e => e.Contains("engine", StringComparison.OrdinalIgnoreCase));
        Assert.True(valid.CanLaunch, string.Join("; ", valid.Errors));
    }

    [Fact]
    public void AutomaticAttachmentRejectsAFullOrIncompatibleParentClearly()
    {
        var assembly = new VesselAssembly(LoadCatalog());
        var command = assembly.AddRoot("starship_command");
        assembly.AttachPartAutomatically(command.InstanceId, "starship_tank");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            assembly.AttachPartAutomatically(command.InstanceId, "starship_tank"));

        Assert.Contains("No compatible free node", ex.Message);
    }

    [Fact]
    public void StarshipTemplateSequenceAutoBuildsAFlightReadyTree()
    {
        var assembly = new VesselAssembly(LoadCatalog());
        var current = assembly.AddRoot("starship_command");
        foreach (string definitionId in new[]
                 {
                     "starship_tank", "starship_engines", "starship_landing_gear",
                     "decoupler_heavy", "super_heavy_booster",
                 })
            current = assembly.AttachPartAutomatically(current.InstanceId, definitionId);

        Assert.Equal(6, assembly.Parts.Count);
        Assert.Equal(5, assembly.Connections.Count);
        Assert.True(assembly.ValidateForLaunch().CanLaunch);
    }

    private static VesselAssembly BuildStarshipLikeAssembly()
    {
        var assembly = new VesselAssembly(LoadCatalog());
        var command = assembly.AddRoot("starship_command");
        var tank = assembly.AttachPart(command.InstanceId, "bottom", "starship_tank", "top");
        var engines = assembly.AttachPart(tank.InstanceId, "bottom", "starship_engines", "top");
        var decoupler = assembly.AttachPart(engines.InstanceId, "bottom", "decoupler_heavy", "top");
        assembly.AttachPart(decoupler.InstanceId, "bottom", "super_heavy_booster", "top");
        return assembly;
    }

    private static PartCatalog LoadCatalog() =>
        PartCatalog.LoadFromDirectory(Path.Combine(FindRepoRoot().FullName, "data", "parts"));

    private static DirectoryInfo FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data"))
                && File.Exists(Path.Combine(dir.FullName, "ExosphereSimulation.sln")))
            {
                return dir;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
