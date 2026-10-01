namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Presentation;

public sealed class FlightEngineBoardsTests
{
    private static readonly string Data = LocateData();

    [Theory]
    [InlineData("starship_flight12_v3_2026", "33,6")]
    [InlineData("starship_flight7_block2_2025", "33,6")]
    [InlineData("falcon9_block5_standard_2025", "9,1")]
    [InlineData("falcon9_block5_extended_2025", "9,1")]
    [InlineData("newglenn_7x2_public_2026", "7,2")]
    [InlineData("mercury_redstone3_freedom7_1961", "1,1")]
    [InlineData("mercury_atlas6_friendship7_1962", "3,1")]
    [InlineData("gemini8_titan2_1966", "2,1")]
    [InlineData("apollo8_saturn5_as503_1968", "5,5,1,1")]
    [InlineData("apollo11_saturn5_as506_1969", "5,5,1,1")]
    [InlineData("apollo11_lm5_eagle_1969", "1,1")]
    [InlineData("agena8_target_5003_1966", "")]
    public void CatalogBoardsPreserveStageBoundariesAndInstalledEngineIdentities(string variant, string counts)
    {
        var graph = Load(variant);
        var boards = FlightEngineBoards.Build(graph);
        Assert.Equal(counts, string.Join(",", boards.Select(b => b.Engines.Count)));
        var expectedIds = graph.Parts.Where(p => p.Definition.Category == PartCategory.Engine)
            .SelectMany(p => p.HasEngineRuntime ? p.EngineStates.Select(e => e.InstanceId) : new[] { p.InstanceId })
            .OrderBy(id => id);
        Assert.Equal(expectedIds, boards.SelectMany(b => b.Engines).Select(e => e.Id).OrderBy(id => id));
        Assert.Equal(boards.Sum(b => b.Engines.Count), boards.SelectMany(b => b.Engines).Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public void SaturnAdvancesThroughAllFourPropulsionStagesWithoutShowingSpentEngines()
    {
        var graph = Load("apollo11_saturn5_as506_1969");
        var boards = FlightEngineBoards.Build(graph);
        for (int i = 0; i < 4; i++)
        {
            var (current, next) = FlightEngineBoards.Select(boards, graph);
            Assert.Equal(i == 3 ? "SPS" : $"STAGE {i + 1}", current!.Label);
            Assert.Equal(i < 2 ? 5 : 1, current.Engines.Count);
            Assert.Equal(i == 2 ? "SPS" : i < 3 ? $"STAGE {i + 2}" : null, next?.Label);
            if (i < 3) Assert.NotNull(graph.FireNextStage());
        }
    }

    [Fact]
    public void AtlasHalfStageJettisonKeepsOnlySustainerInTheCurrentBoard()
    {
        var graph = Load("mercury_atlas6_friendship7_1962");
        var boards = FlightEngineBoards.Build(graph);
        Assert.Equal(3, FlightEngineBoards.Select(boards, graph).Current!.Engines.Count);
        var package = graph.Parts.Single(p => p.Definition.HasVehicleRole("booster_engine_package"));
        Assert.NotNull(graph.DetachSubtree(package.InstanceId));
        var current = FlightEngineBoards.Select(boards, graph).Current!;
        Assert.Equal("STAGE 1", current.Label);
        Assert.Single(current.Engines);
        Assert.DoesNotContain(current.Engines, e => e.PartId == package.InstanceId);
    }

    [Fact]
    public void EngineSelectionAndFailureDoNotEraseInstalledEngineDots()
    {
        var graph = Load("starship_flight12_v3_2026");
        var booster = graph.Parts.Single(p => p.Definition.HasVehicleRole("booster"));
        booster.SelectEngineCount(3);
        booster.IsBroken = true;
        var boards = FlightEngineBoards.Build(graph);
        Assert.Equal(33, FlightEngineBoards.Select(boards, graph).Current!.Engines.Count);
        var detached = graph.FireNextStage()!;
        Assert.Equal(33, boards[0].AttachedTo(detached).Engines.Count);
        Assert.Equal(6, FlightEngineBoards.Select(boards, graph).Current!.Engines.Count);
    }

    [Fact]
    public void FalconUsesOctawebMountsAndSingleVacuumEngineIsCentered()
    {
        var graph = Load("falcon9_block5_standard_2025");
        var boards = FlightEngineBoards.Build(graph);
        Assert.Single(boards[0].Engines.Where(e => e.X == 0 && e.Z == 0));
        Assert.Equal(9, boards[0].Engines.Select(e => (e.X, e.Z)).Distinct().Count());
        var vacuum = Assert.Single(boards[1].Engines);
        Assert.Equal(0, vacuum.X);
        Assert.Equal(0, vacuum.Z);
    }

    private static PartGraph Load(string name) => VehicleVariantDefinition.LoadFromJson(
        Path.Combine(Data, "vehicles", name + ".json"))
        .Build(PartCatalog.LoadFromDirectory(Path.Combine(Data, "parts"))).ToPartGraph();

    private static string LocateData()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "data", "vehicles"))) root = root.Parent;
        return Path.Combine(root!.FullName, "data");
    }
}
