namespace Exosphere.Simulation.Campaign;

public enum MissionOutcome
{
    InProgress,
    Success,
    Failure,
}

public sealed record MissionRuleResult(
    string Id,
    string TitleEn,
    string TitleEs,
    bool Required,
    bool Passed,
    double? ActualValue,
    double? TargetValue,
    double? UpperTargetValue,
    string Evidence);

public sealed record MissionDebrief(
    string MissionId,
    MissionOutcome Outcome,
    IReadOnlyList<MissionRuleResult> Objectives,
    IReadOnlyList<MissionRuleResult> Limits,
    IReadOnlyDictionary<string, double> NumericEvidence)
{
    public bool IsSuccess => Outcome == MissionOutcome.Success;
}

public static class MissionEvaluator
{
    public static MissionDebrief Evaluate(
        MissionDefinition definition,
        MissionEvidence evidence,
        bool isFinal)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(evidence);
        definition.Validate();

        var objectives = definition.Objectives
            .Select(rule => EvaluateRule(rule, evidence))
            .ToArray();
        var limits = definition.Limits
            .Select(rule => EvaluateRule(rule, evidence))
            .ToArray();
        bool requiredPassed = objectives.Concat(limits)
            .Where(result => result.Required)
            .All(result => result.Passed);
        bool irreversibleFailure = definition.Limits
            .Select(rule => EvaluateRule(rule, evidence))
            .Any(result => result.Required && !result.Passed
                && IsIrreversible(rule: definition.Limits.Single(
                    item => item.Id == result.Id)));

        MissionOutcome outcome = irreversibleFailure || isFinal && !requiredPassed
            ? MissionOutcome.Failure
            : isFinal && requiredPassed
                ? MissionOutcome.Success
                : MissionOutcome.InProgress;

        return new MissionDebrief(
            definition.Id,
            outcome,
            objectives,
            limits,
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["elapsedSeconds"] = evidence.ElapsedSeconds,
                ["peakAltitudeM"] = evidence.PeakAltitudeM,
                ["peakSurfaceSpeedMps"] = evidence.PeakSurfaceSpeedMps,
                ["peakInertialSpeedMps"] = evidence.PeakInertialSpeedMps,
                ["maximumDynamicPressurePa"] =
                    evidence.MaximumDynamicPressurePa,
                ["maximumGForce"] = evidence.MaximumGForce,
                ["maximumDownrangeM"] = evidence.MaximumDownrangeM,
                ["completedOrbits"] = evidence.CompletedOrbits,
                ["dockingAchieved"] =
                    evidence.DockingAchieved ? 1.0 : 0.0,
                ["maximumAngularRateDegPerS"] =
                    evidence.MaximumAngularRateDegPerS,
                ["emergencyControlRecovered"] =
                    evidence.EmergencyControlRecovered ? 1.0 : 0.0,
            });
    }

    private static MissionRuleResult EvaluateRule(
        MissionRuleDefinition rule,
        MissionEvidence evidence)
    {
        if (rule.Metric == MissionMetric.PhaseReached)
        {
            bool reached = evidence.ReachedPhases.Contains(rule.ExpectedText);
            return new MissionRuleResult(
                rule.Id, rule.TitleEn, rule.TitleEs, rule.Required, reached,
                null, null, null,
                reached
                    ? $"phase {rule.ExpectedText} reached"
                    : $"phase {rule.ExpectedText} not reached");
        }

        double actual = evidence.MetricValue(rule.Metric);
        bool passed = rule.Comparison switch
        {
            MissionComparison.AtLeast => actual >= rule.Target,
            MissionComparison.AtMost => actual <= rule.Target,
            MissionComparison.Between =>
                actual >= rule.Target && actual <= rule.UpperTarget,
            MissionComparison.True => actual >= 0.5,
            MissionComparison.False => actual < 0.5,
            _ => throw new InvalidDataException(
                $"Unsupported comparison '{rule.Comparison}'."),
        };
        string evidenceText = rule.Comparison switch
        {
            MissionComparison.Between =>
                $"{actual:G8} in [{rule.Target:G8}, {rule.UpperTarget:G8}]",
            MissionComparison.True or MissionComparison.False =>
                actual >= 0.5 ? "true" : "false",
            _ => $"{actual:G8} vs {rule.Comparison} {rule.Target:G8}",
        };
        return new MissionRuleResult(
            rule.Id, rule.TitleEn, rule.TitleEs, rule.Required, passed,
            actual, rule.Target, rule.UpperTarget, evidenceText);
    }

    private static bool IsIrreversible(MissionRuleDefinition rule) =>
        rule.Metric is MissionMetric.MaximumDynamicPressurePa
            or MissionMetric.MaximumGForce
            or MissionMetric.MaximumAngularRateDegPerS
            or MissionMetric.CrewAlive
            or MissionMetric.VesselDestroyed
        || rule.Metric == MissionMetric.ElapsedSeconds
            && rule.Comparison == MissionComparison.AtMost;
}
