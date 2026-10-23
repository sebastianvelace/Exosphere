namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Campaign;
using Xunit;

/// <summary>The in-flight mission guide: which procedure step the player is on.</summary>
public sealed class MissionGuideTests
{
    private static MissionDefinition Load(string file) =>
        MissionDefinition.LoadFromJson(Path.Combine(SaturnVAscentFlight.DataDirectory(), "missions", file));

    [Theory]
    [InlineData("freedom7_1961.json")]
    [InlineData("friendship7_1962.json")]
    [InlineData("gemini8_1966.json")]
    [InlineData("apollo8_1968.json")]
    [InlineData("apollo11_1969.json")]
    public void PlayableMissionsCarryABilingualProcedureThatEndsOnTheLastRequiredObjective(string file)
    {
        var mission = Load(file);
        Assert.True(mission.Procedure.Count >= 3);
        Assert.All(mission.Procedure, step =>
        {
            Assert.False(string.IsNullOrWhiteSpace(step.HintEn));
            Assert.False(string.IsNullOrWhiteSpace(step.HintEs));
        });
        // Finishing the guide must mean finishing the mission: its last step is a required objective.
        var last = mission.Procedure[^1];
        Assert.Contains(mission.Objectives, rule => rule.Id == last.DoneWhenObjectiveId && rule.Required);
    }

    [Fact]
    public void GuideAdvancesWithPhasesAndObjectivesAndNeverWaitsOnASkippedStep()
    {
        var mission = Load("apollo8_1968.json");
        var director = new MissionDirector(mission);
        Assert.Equal(0, MissionGuide.CurrentStepIndex(mission, director.Evidence, null));

        var debrief = director.Observe(new MissionTelemetrySnapshot
        {
            MissionTimeSeconds = 10.0, Phase = "LIFTOFF", AltitudeM = 200.0, CrewAlive = true,
        });
        Assert.Equal(1, MissionGuide.CurrentStepIndex(mission, director.Evidence, debrief));

        debrief = director.Observe(new MissionTelemetrySnapshot
        {
            MissionTimeSeconds = 700.0, Phase = "ORBIT", AltitudeM = 185_000.0, CrewAlive = true,
        });
        Assert.Equal("tli", mission.Procedure[MissionGuide.CurrentStepIndex(mission, director.Evidence, debrief)].Id);

        // Reaching lunar orbit without the guide having seen the coast step still moves past it.
        debrief = director.Observe(new MissionTelemetrySnapshot
        {
            MissionTimeSeconds = 250_000.0, Phase = "LUNAR_ORBIT", AltitudeM = 111_000.0, CrewAlive = true,
        });
        Assert.Equal("revolutions", mission.Procedure[MissionGuide.CurrentStepIndex(mission, director.Evidence, debrief)].Id);
    }

    [Fact]
    public void AStepMustNameExactlyOneCompletionCondition()
    {
        var mission = Load("freedom7_1961.json");
        mission.Procedure[0].DoneWhenObjectiveId = "reach-space";
        Assert.Throws<InvalidDataException>(mission.Validate);
        mission.Procedure[0].DoneWhenPhase = "";
        mission.Procedure[0].DoneWhenObjectiveId = "no-such-objective";
        Assert.Throws<InvalidDataException>(mission.Validate);
    }
}
