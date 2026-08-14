namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Parts;
using Xunit;

public sealed class PartGraphHotPathTests
{
    [Fact]
    public void CurrentStagePartsPreservesNestedDecouplerSemantics()
    {
        var definitions = PartDefinition.LoadAllFromDirectory(
            Path.Combine(FindRepoRoot().FullName, "data", "parts"));
        var command = new Part(definitions["starship_command"], "stage-command");
        var upperDecoupler = new Part(definitions["decoupler_heavy"], "stage-upper-decoupler");
        var tank = new Part(definitions["starship_tank"], "stage-tank");
        var lowerDecoupler = new Part(definitions["decoupler_heavy"], "stage-lower-decoupler");
        var booster = new Part(definitions["super_heavy_booster"], "stage-booster");

        var graph = new PartGraph();
        graph.SetRoot(command);
        graph.AddJoint(new Joint(command, upperDecoupler, "bottom", "top"));
        graph.AddJoint(new Joint(upperDecoupler, tank, "bottom", "top"));
        graph.AddJoint(new Joint(tank, lowerDecoupler, "bottom", "top"));
        graph.AddJoint(new Joint(lowerDecoupler, booster, "bottom", "top"));

        Assert.Equal(
            ["stage-booster"],
            graph.CurrentStageParts().Select(part => part.InstanceId).ToArray());

        lowerDecoupler.IsStagingActive = false;
        Assert.Equal(
            ["stage-tank", "stage-lower-decoupler", "stage-booster"],
            graph.CurrentStageParts().Select(part => part.InstanceId).ToArray());

        upperDecoupler.IsStagingActive = false;
        Assert.Equal(
            [
                "stage-command",
                "stage-upper-decoupler",
                "stage-tank",
                "stage-lower-decoupler",
                "stage-booster",
            ],
            graph.CurrentStageParts().Select(part => part.InstanceId).ToArray());
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "data"))
                && File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
                return directory;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
