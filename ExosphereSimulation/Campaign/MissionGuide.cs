namespace Exosphere.Simulation.Campaign;

/// <summary>Where the player stands in a mission's <see cref="MissionDefinition.Procedure"/>.</summary>
public static class MissionGuide
{
    /// <summary>
    /// Index of the step to do now: the one after the last completed step, so a step the
    /// player skipped (or flew out of order) never holds the guide back. Equals the step
    /// count once the procedure is complete.
    /// </summary>
    public static int CurrentStepIndex(
        MissionDefinition definition,
        MissionEvidence evidence,
        MissionDebrief? debrief)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(evidence);
        int current = 0;
        for (int i = 0; i < definition.Procedure.Count; i++)
            if (IsDone(definition.Procedure[i], evidence, debrief)) current = i + 1;
        return current;
    }

    public static bool IsDone(MissionProcedureStep step, MissionEvidence evidence, MissionDebrief? debrief)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(evidence);
        if (!string.IsNullOrWhiteSpace(step.DoneWhenPhase))
            return evidence.ReachedPhases.Contains(step.DoneWhenPhase);
        return debrief?.Objectives.Any(result => result.Id == step.DoneWhenObjectiveId && result.Passed) == true;
    }
}
