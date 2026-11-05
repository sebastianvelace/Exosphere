namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Data;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Xunit;

public sealed class ReactionControlCatalogTests
{
    [Theory]
    [InlineData("falcon9_first_stage_tank", 21_370)]
    [InlineData("falcon9_second_stage_tank", 3_530)]
    [InlineData("newglenn_first_stage_tank", 70_000)]
    [InlineData("newglenn_second_stage_tank", 25_000)]
    [InlineData("agena8_target_vehicle_bus", 2_778)]
    public void DeclaredJetsHaveThreeAxisAuthorityAndAFiniteAccountedStore(string partId, double priorInertMass)
    {
        string data = Flight14EntryCheckpointFixture.DataDirectory();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(data, "parts"));
        var definition = catalog[partId];
        Assert.NotEmpty(definition.RcsThrusters);
        Assert.Equal(definition.RcsThrusters.Count,
            definition.RcsThrusters.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(priorInertMass, definition.MassDry + definition.FuelCapacityMono, 9);
        var record = DataProvenanceRegistry.LoadFromDirectory(Path.Combine(data, "provenance"))
            .Require(partId, "rcsThrusters");
        Assert.Equal(ProvenanceStatus.Estimated, record.Status);
        Assert.False(string.IsNullOrWhiteSpace(record.Uncertainty));
        var part = new Part(definition, "acs-test");
        var graph = new PartGraph();
        graph.SetRoot(part);
        graph.ArmStagedReactionControl();
        var thrusters = graph.GetRcsThrusters();
        var authority = RcsAllocation.Authority(thrusters, graph.CenterOfMass);
        Assert.True(authority.X > 0 && authority.Y > 0 && authority.Z > 0, authority.ToString());
        part.Monopropellant = 0;
        Assert.Equal(Vector3d.Zero, RcsAllocation.Authority(thrusters, graph.CenterOfMass));
    }
}
