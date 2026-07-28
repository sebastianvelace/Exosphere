namespace Exosphere.Simulation.Campaign;

using Exosphere.Simulation.Persistence;

public sealed class MissionDirector
{
    public MissionDefinition Definition { get; }
    public MissionEvidence Evidence { get; }
    public string CurrentPhase { get; private set; } = "PRE_LAUNCH";

    public MissionDirector(
        MissionDefinition definition,
        MissionSaveV2? restoredState = null)
    {
        Definition = definition
            ?? throw new ArgumentNullException(nameof(definition));
        Definition.Validate();
        if (restoredState?.MissionId != null
            && !string.Equals(
                restoredState.MissionId,
                definition.Id,
                StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Mission state '{restoredState.MissionId}' does not match '{definition.Id}'.");
        Evidence = restoredState == null
            ? new MissionEvidence()
            : MissionEvidence.Restore(restoredState);
        if (!string.IsNullOrWhiteSpace(restoredState?.Phase))
            CurrentPhase = restoredState.Phase;
    }

    public MissionDebrief Observe(MissionTelemetrySnapshot snapshot)
    {
        Evidence.Observe(snapshot);
        CurrentPhase = snapshot.Phase;
        return MissionEvaluator.Evaluate(Definition, Evidence, isFinal: false);
    }

    public MissionDebrief EvaluateCurrent() =>
        MissionEvaluator.Evaluate(Definition, Evidence, isFinal: false);

    public MissionDebrief FinalizeMission() =>
        MissionEvaluator.Evaluate(Definition, Evidence, isFinal: true);

    public MissionSaveV2 CaptureState()
    {
        var current = EvaluateCurrent();
        return Evidence.Capture(
            Definition.Id,
            CurrentPhase,
            current.Objectives.Concat(current.Limits)
                .Where(result => result.Passed)
                .Select(result => result.Id));
    }
}
