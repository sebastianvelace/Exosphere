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
