namespace Exosphere.Simulation.Propulsion;

using System.Text.Json;
using System.Text.Json.Serialization;
using Exosphere.Simulation.Data;
using Exosphere.Simulation.Math;

[JsonConverter(typeof(JsonStringEnumConverter<EngineLifecycleState>))]
public enum EngineLifecycleState
{
    Off,
    Chill,
    SpinPrime,
    Ignition,
    Ramp,
    Running,
    Shutdown,
    Purge,
    Failed,
}

public sealed record EngineFailureInjection
{
    public string EngineInstanceId { get; init; } = "";
    public EngineLifecycleState TriggerState { get; init; }
    public int TriggerStartAttempt { get; init; }
    public double TriggerAfterStateSeconds { get; init; }
    public string FailureCode { get; init; } = "INJECTED_FAILURE";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(EngineInstanceId)
            || !Enum.IsDefined(TriggerState)
            || TriggerState is EngineLifecycleState.Off
                or EngineLifecycleState.Failed
            || TriggerStartAttempt < 0
            || !double.IsFinite(TriggerAfterStateSeconds)
            || TriggerAfterStateSeconds < 0.0
            || string.IsNullOrWhiteSpace(FailureCode))
            throw new InvalidDataException("Invalid deterministic engine failure injection.");
    }
}

public sealed record PressureThrottlePoint
{
    public double AmbientPressurePa { get; init; }
    public double Throttle { get; init; }
    public double ThrustN { get; init; }
    public double SpecificImpulseS { get; init; }
}

public sealed class EngineModelDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Variant { get; set; } = "";
    public DateOnly AsOfDate { get; set; }
    public string Fuel { get; set; } = "";
    public string Oxidizer { get; set; } = "";
    public double MixtureRatioOxidizerToFuel { get; set; }
    public double RatedThrustSeaLevelN { get; set; }
    public double RatedThrustVacuumN { get; set; }
    public double? PublishedThrustLowerN { get; set; }
    public double? PublishedThrustUpperN { get; set; }
    public string PublishedThrustEnvelopeCondition { get; set; } = "";
    public double SpecificImpulseSeaLevelS { get; set; }
    public double SpecificImpulseVacuumS { get; set; }
    public double MinimumThrottle { get; set; }
    public double MaximumThrottle { get; set; } = 1.0;
    public double? PublishedMinimumThrottleLower { get; set; }
    public double? PublishedMinimumThrottleUpper { get; set; }
    public string PublishedThrottleEnvelopeCondition { get; set; } = "";
    public double GimbalRangeDeg { get; set; }
    public double GimbalRateDegPerS { get; set; }
    public double GimbalAccelerationDegPerS2 { get; set; }
    public double StartupSeconds { get; set; }
    public double ShutdownSeconds { get; set; }
    public int RestartLimit { get; set; }
    public double NominalOperatingTemperatureK { get; set; } = 1_100.0;
    public double MaximumSafeTemperatureK { get; set; } = 1_400.0;
    public double ThermalTimeConstantSeconds { get; set; } = 3.0;
    public double CooldownTimeConstantSeconds { get; set; } = 15.0;
    public double? ExitAreaM2 { get; set; }
    public double? NominalMassFlowKgS { get; set; }
    public double? EffectiveExhaustVelocityMps { get; set; }
    public double? EffectiveExitPressurePa { get; set; }
    public List<PressureThrottlePoint> PerformanceMap { get; set; } = new();

    public void Validate(DataProvenanceRegistry? provenance = null)
    {
        if (string.IsNullOrWhiteSpace(Id)
            || string.IsNullOrWhiteSpace(Name)
            || string.IsNullOrWhiteSpace(Variant)
            || AsOfDate == default
            || string.IsNullOrWhiteSpace(Fuel)
            || string.IsNullOrWhiteSpace(Oxidizer))
            throw new InvalidDataException($"Engine model '{Id}' has incomplete identity.");

        RequireFinitePositive(
            (RatedThrustSeaLevelN, nameof(RatedThrustSeaLevelN)),
            (RatedThrustVacuumN, nameof(RatedThrustVacuumN)),
            (SpecificImpulseSeaLevelS, nameof(SpecificImpulseSeaLevelS)),
            (SpecificImpulseVacuumS, nameof(SpecificImpulseVacuumS)),
            (MixtureRatioOxidizerToFuel, nameof(MixtureRatioOxidizerToFuel)),
            (MaximumThrottle, nameof(MaximumThrottle)),
            (StartupSeconds, nameof(StartupSeconds)),
            (ShutdownSeconds, nameof(ShutdownSeconds)));
        if (PublishedThrustLowerN.HasValue != PublishedThrustUpperN.HasValue
            || PublishedThrustLowerN is { } lower
                && (!double.IsFinite(lower) || lower <= 0.0)
            || PublishedThrustUpperN is { } upper
                && (!double.IsFinite(upper) || upper <= 0.0)
            || PublishedThrustLowerN is { } intervalLower
                && PublishedThrustUpperN is { } intervalUpper
                && (intervalUpper < intervalLower
                    || string.IsNullOrWhiteSpace(
                        PublishedThrustEnvelopeCondition)
                    || (RatedThrustSeaLevelN < intervalLower
                        || RatedThrustSeaLevelN > intervalUpper)
                       && (RatedThrustVacuumN < intervalLower
                           || RatedThrustVacuumN > intervalUpper)))
            throw new InvalidDataException(
                $"Engine model '{Id}' has an invalid public thrust envelope.");
        if (!double.IsFinite(MinimumThrottle)
            || MinimumThrottle < 0.0
            || MinimumThrottle > MaximumThrottle
            || MaximumThrottle > 1.0)
            throw new InvalidDataException($"Engine model '{Id}' has an invalid throttle range.");
        if (PublishedMinimumThrottleLower.HasValue
                != PublishedMinimumThrottleUpper.HasValue
            || PublishedMinimumThrottleLower is { } throttleLower
                && (!double.IsFinite(throttleLower)
                    || throttleLower is < 0.0 or > 1.0)
            || PublishedMinimumThrottleUpper is { } throttleUpper
                && (!double.IsFinite(throttleUpper)
                    || throttleUpper is < 0.0 or > 1.0)
            || PublishedMinimumThrottleLower is { } lowerThrottle
                && PublishedMinimumThrottleUpper is { } upperThrottle
                && (upperThrottle < lowerThrottle
                    || MinimumThrottle < lowerThrottle
                    || MinimumThrottle > upperThrottle
                    || string.IsNullOrWhiteSpace(
                        PublishedThrottleEnvelopeCondition)))
            throw new InvalidDataException(
                $"Engine model '{Id}' has an invalid public throttle envelope.");
        if (!double.IsFinite(GimbalRangeDeg)
            || !double.IsFinite(GimbalRateDegPerS)
            || !double.IsFinite(GimbalAccelerationDegPerS2)
            || GimbalRangeDeg < 0.0
            || GimbalRateDegPerS < 0.0
            || GimbalAccelerationDegPerS2 < 0.0
            || RestartLimit < 0)
            throw new InvalidDataException($"Engine model '{Id}' has invalid actuation limits.");
        if (!double.IsFinite(NominalOperatingTemperatureK)
            || !double.IsFinite(MaximumSafeTemperatureK)
            || !double.IsFinite(ThermalTimeConstantSeconds)
            || !double.IsFinite(CooldownTimeConstantSeconds)
            || NominalOperatingTemperatureK <= 290.0
            || MaximumSafeTemperatureK <= NominalOperatingTemperatureK
            || ThermalTimeConstantSeconds <= 0.0
            || CooldownTimeConstantSeconds <= 0.0)
            throw new InvalidDataException(
                $"Engine model '{Id}' has an invalid thermal envelope.");
        if (ExitAreaM2 is { } area && (!double.IsFinite(area) || area <= 0.0))
            throw new InvalidDataException($"Engine model '{Id}' has an invalid exit area.");
        if (NominalMassFlowKgS is { } flow && (!double.IsFinite(flow) || flow <= 0.0))
            throw new InvalidDataException($"Engine model '{Id}' has an invalid mass flow.");
        if (EffectiveExhaustVelocityMps is { } velocity
            && (!double.IsFinite(velocity) || velocity <= 0.0))
            throw new InvalidDataException(
                $"Engine model '{Id}' has an invalid effective exhaust velocity.");
        if (EffectiveExitPressurePa is { } pressure
            && (!double.IsFinite(pressure) || pressure < 0.0))
            throw new InvalidDataException(
                $"Engine model '{Id}' has an invalid effective exit pressure.");
        int equationParameterCount =
            (ExitAreaM2.HasValue ? 1 : 0)
            + (NominalMassFlowKgS.HasValue ? 1 : 0)
            + (EffectiveExhaustVelocityMps.HasValue ? 1 : 0)
            + (EffectiveExitPressurePa.HasValue ? 1 : 0);
        if (equationParameterCount is > 0 and < 4)
            throw new InvalidDataException(
                $"Engine model '{Id}' must define every nozzle-equation parameter or none.");

        foreach (var point in PerformanceMap)
        {
            if (!double.IsFinite(point.AmbientPressurePa)
                || !double.IsFinite(point.Throttle)
                || !double.IsFinite(point.ThrustN)
                || !double.IsFinite(point.SpecificImpulseS)
                || point.AmbientPressurePa < 0.0
                || point.Throttle is < 0.0 or > 1.0
                || point.ThrustN < 0.0
                || point.SpecificImpulseS <= 0.0)
                throw new InvalidDataException($"Engine model '{Id}' has an invalid map point.");
        }

        provenance?.RequireFields(
            Id,
            "ratedThrustSeaLevelN",
            "ratedThrustVacuumN",
            "specificImpulseSeaLevelS",
            "specificImpulseVacuumS",
            "minimumThrottle",
            "performanceMap",
            "restartEnvelope",
            "thermalEnvelope",
            "gimbalEnvelope",
            "startupTransient",
            "shutdownTransient");
        if (PublishedThrustLowerN.HasValue)
            provenance?.RequireFields(Id, "publicThrustEnvelope");
        if (PublishedMinimumThrottleLower.HasValue)
            provenance?.RequireFields(Id, "publicThrottleEnvelope");

        void RequireFinitePositive(params (double value, string field)[] values)
        {
            foreach (var (value, field) in values)
                if (!double.IsFinite(value) || value <= 0.0)
                    throw new InvalidDataException(
                        $"Engine model '{Id}' has invalid {field}.");
        }
    }
}

public sealed record EngineMountDefinition
{
    public string InstanceId { get; init; } = "";
    /// <summary>
    /// Optional per-mount override. Empty mounts use the cluster's default model.
    /// This permits physically mixed clusters such as Starship's three sea-level
    /// and three vacuum Raptors without collapsing them into an average engine.
    /// </summary>
    public string EngineModelId { get; init; } = "";
    public double[] PositionM { get; init; } = [0.0, 0.0, 0.0];
    public double[] ThrustDirection { get; init; } = [0.0, 1.0, 0.0];
    public bool Gimballed { get; init; } = true;

    [JsonIgnore]
    public Vector3d Position => new(PositionM[0], PositionM[1], PositionM[2]);

    [JsonIgnore]
    public Vector3d Direction => new(
        ThrustDirection[0], ThrustDirection[1], ThrustDirection[2]);
}

public sealed class EngineClusterDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string EngineModelId { get; set; } = "";
    public List<EngineMountDefinition> Engines { get; set; } = new();
    public FeedNetwork FeedNetwork { get; set; } = new();

    public void Validate(IReadOnlyDictionary<string, EngineModelDefinition> models)
    {
        if (string.IsNullOrWhiteSpace(Id)
            || string.IsNullOrWhiteSpace(Name)
            || !models.ContainsKey(EngineModelId)
            || Engines.Count == 0)
            throw new InvalidDataException($"Engine cluster '{Id}' is incomplete.");
        if (Engines.Select(e => e.InstanceId).Any(string.IsNullOrWhiteSpace)
            || Engines.Select(e => e.InstanceId).Distinct(StringComparer.Ordinal).Count()
                != Engines.Count)
            throw new InvalidDataException($"Engine cluster '{Id}' has invalid instance ids.");
        foreach (var mount in Engines)
        {
            if (!string.IsNullOrWhiteSpace(mount.EngineModelId)
                    && !models.ContainsKey(mount.EngineModelId)
                || mount.PositionM.Length != 3
                || mount.ThrustDirection.Length != 3
                || mount.PositionM.Any(v => !double.IsFinite(v))
                || mount.ThrustDirection.Any(v => !double.IsFinite(v))
                || mount.Direction.MagnitudeSquared <= 1e-12)
                throw new InvalidDataException($"Engine cluster '{Id}' has an invalid mount.");
        }
        FeedNetwork.Validate(Engines.Select(e => e.InstanceId));
    }
}

public sealed record FeedBranch
{
    public string EngineInstanceId { get; init; } = "";
    public double MaximumFlowKgS { get; init; }
}

public sealed class FeedNetwork
{
    public string FuelResource { get; set; } = "";
    public string OxidizerResource { get; set; } = "";
    public bool CrossfeedEnabled { get; set; }
    public List<FeedBranch> Branches { get; set; } = new();

    public void Validate(IEnumerable<string> engineIds)
    {
        var validIds = engineIds.ToHashSet(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(FuelResource)
            || string.IsNullOrWhiteSpace(OxidizerResource)
            || Branches.Count != validIds.Count
            || Branches.Select(b => b.EngineInstanceId)
                .Distinct(StringComparer.Ordinal).Count() != Branches.Count
            || Branches.Any(b => !validIds.Contains(b.EngineInstanceId)
                || !double.IsFinite(b.MaximumFlowKgS)
                || b.MaximumFlowKgS <= 0.0))
            throw new InvalidDataException("Feed network does not cover every engine exactly once.");
    }
}

public sealed class EngineInstanceState
{
    public required string InstanceId { get; init; }
    public required string EngineModelId { get; init; }
    public EngineLifecycleState State { get; set; } = EngineLifecycleState.Off;
    public double StateElapsedSeconds { get; set; }
    public double CommandedThrottle { get; set; }
    public double ActualThrottle { get; set; }
    public Vector3d GimbalDeg { get; set; } = Vector3d.Zero;
    public Vector3d GimbalVelocityDegPerS { get; set; } = Vector3d.Zero;

    /// <summary>
    /// Per-instance normalized gimbal command (X,Z ∈ [-1,1]) set by
    /// <see cref="Parts.PartGraph.SolveDifferentialGimbal"/> for differential per-mount TVC.
    /// When set, <see cref="Part.AdvanceGimbal"/> servos this instance toward it instead of
    /// the part-wide <see cref="Part.GimbalOffset"/>. Reset to <c>null</c> every tick by
    /// <see cref="Part.ClearGimbalCommandOverrides"/> so a stale differential command never
    /// outlives the tick that computed it.
    /// </summary>
    public Vector3d? GimbalCommandOverride { get; set; }
    public double ChamberPressureFraction { get; set; }
    public double TemperatureK { get; set; } = 290.0;
    public int StartAttempts { get; set; }
    public int StartsCompleted { get; set; }
    public string? FailureCode { get; set; }
}

public sealed record EngineTelemetry(
    string InstanceId,
    EngineLifecycleState State,
    double CommandedThrottle,
    double ActualThrottle,
    double ThrustN,
    double MassFlowKgS,
    double ChamberPressureFraction,
    double MixtureRatio,
    Vector3d GimbalDeg,
    double TemperatureK,
    double MaximumSafeTemperatureK,
    int StartAttempts,
    int StartsCompleted,
    string? FailureCode);

public readonly record struct EnginePerformanceSample(
    double ThrustN,
    double SpecificImpulseS,
    double MassFlowKgS,
    double ChamberPressureFraction);

public static class EnginePerformanceEvaluator
{
    private const double StandardGravity = 9.80665;

    public static EnginePerformanceSample Evaluate(
        EngineModelDefinition model,
        double ambientPressurePa,
        double throttle)
    {
        if (!double.IsFinite(ambientPressurePa)
            || !double.IsFinite(throttle))
            throw new ArgumentOutOfRangeException(nameof(throttle));
        double pressure = System.Math.Max(0.0, ambientPressurePa);
        double command = System.Math.Clamp(throttle, 0.0, model.MaximumThrottle);
        if (command <= 1e-9)
            return new EnginePerformanceSample(0.0, 0.0, 0.0, 0.0);

        if (HasNozzleEquation(model))
        {
            double flow = model.NominalMassFlowKgS!.Value * command;
            double momentum = flow * model.EffectiveExhaustVelocityMps!.Value;
            double pressureThrust = model.ExitAreaM2!.Value
                * (model.EffectiveExitPressurePa!.Value - pressure)
                * command;
            double thrust = System.Math.Max(0.0, momentum + pressureThrust);
            double isp = flow > 1e-12 ? thrust / (flow * StandardGravity) : 0.0;
            return new EnginePerformanceSample(thrust, isp, flow, command);
        }

        if (model.PerformanceMap.Count > 0)
        {
            var mapped = InterpolatePerformanceMap(
                model.PerformanceMap,
                pressure,
                command);
            double mapThrottle = System.Math.Max(mapped.referenceThrottle, 1e-9);
            double scale = command / mapThrottle;
            double thrust = System.Math.Max(0.0, mapped.thrust * scale);
            double isp = System.Math.Max(0.0, mapped.isp);
            double flow = isp > 1e-12 ? thrust / (isp * StandardGravity) : 0.0;
            return new EnginePerformanceSample(thrust, isp, flow, command);
        }

        double pressureFraction = pressure / 101_325.0;
        double ratedThrust = System.Math.Max(
            0.0,
            model.RatedThrustVacuumN
            + (model.RatedThrustSeaLevelN - model.RatedThrustVacuumN)
            * pressureFraction);
        double specificImpulse = System.Math.Max(
            0.0,
            model.SpecificImpulseVacuumS
            + (model.SpecificImpulseSeaLevelS - model.SpecificImpulseVacuumS)
            * pressureFraction);
        double actualThrust = ratedThrust * command;
        double massFlow = specificImpulse > 1e-12
            ? actualThrust / (specificImpulse * StandardGravity)
            : 0.0;
        return new EnginePerformanceSample(
            actualThrust, specificImpulse, massFlow, command);
    }

    /// <summary>
    /// Interpolates the validated pressure/throttle map without materializing LINQ groups,
    /// sorted arrays, or closures. Maps are tiny (normally two throttle levels and two
    /// pressures), so the bounded quadratic scan is cheaper than allocating on every engine
    /// instance during a physics tick. The selection order mirrors GroupBy + OrderBy from the
    /// previous implementation: throttle groups are sorted numerically and pressure points
    /// are sorted numerically within each group.
    /// </summary>
    private static (double thrust, double isp, double referenceThrottle)
        InterpolatePerformanceMap(
            IReadOnlyList<PressureThrottlePoint> points,
            double pressure,
            double command)
    {
        int groupCount = 0;
        double smallestThrottle = double.PositiveInfinity;
        double secondSmallestThrottle = double.PositiveInfinity;
        double largestThrottle = double.NegativeInfinity;
        double secondLargestThrottle = double.NegativeInfinity;
        double lowerThrottle = double.NegativeInfinity;
        double upperThrottle = double.PositiveInfinity;

        for (int i = 0; i < points.Count; i++)
        {
            double throttle = points[i].Throttle;
            bool seen = false;
            for (int previous = 0; previous < i; previous++)
            {
                if (points[previous].Throttle == throttle)
                {
                    seen = true;
                    break;
                }
            }
            if (seen) continue;
            groupCount++;

            if (throttle < smallestThrottle)
            {
                secondSmallestThrottle = smallestThrottle;
                smallestThrottle = throttle;
            }
            else if (throttle < secondSmallestThrottle)
                secondSmallestThrottle = throttle;

            if (throttle > largestThrottle)
            {
                secondLargestThrottle = largestThrottle;
                largestThrottle = throttle;
            }
            else if (throttle > secondLargestThrottle)
                secondLargestThrottle = throttle;

            if (throttle >= command && throttle < upperThrottle)
                upperThrottle = throttle;
            if (throttle < command && throttle > lowerThrottle)
                lowerThrottle = throttle;
        }

        if (groupCount == 1)
        {
            var only = InterpolatePressure(
                points,
                smallestThrottle,
                pressure);
            return (only.thrust, only.isp, smallestThrottle);
        }

        // This is the same boundary rule as InterpolateThrottle: when the command lies
        // outside the map, use the nearest two groups rather than extrapolating from one.
        if (double.IsPositiveInfinity(upperThrottle))
        {
            upperThrottle = largestThrottle;
            lowerThrottle = secondLargestThrottle;
        }
        else if (double.IsNegativeInfinity(lowerThrottle))
        {
            lowerThrottle = smallestThrottle;
            upperThrottle = secondSmallestThrottle;
        }

        var lower = InterpolatePressure(points, lowerThrottle, pressure);
        var upper = InterpolatePressure(points, upperThrottle, pressure);
        double span = upperThrottle - lowerThrottle;
        double t = span > 1e-12
            ? (command - lowerThrottle) / span
            : 0.0;
        return (
            lower.thrust + (upper.thrust - lower.thrust) * t,
            lower.isp + (upper.isp - lower.isp) * t,
            command);
    }

    private static (double thrust, double isp) InterpolatePressure(
        IReadOnlyList<PressureThrottlePoint> points,
        double throttle,
        double pressure)
    {
        int count = 0;
        int minimumIndex = -1;
        int secondMinimumIndex = -1;
        int maximumIndex = -1;
        int secondMaximumIndex = -1;
        int lowerIndex = -1;
        int upperIndex = -1;
        double minimumPressure = double.PositiveInfinity;
        double secondMinimumPressure = double.PositiveInfinity;
        double maximumPressure = double.NegativeInfinity;
        double secondMaximumPressure = double.NegativeInfinity;
        double lowerPressure = double.NegativeInfinity;
        double upperPressure = double.PositiveInfinity;

        for (int i = 0; i < points.Count; i++)
        {
            var point = points[i];
            if (point.Throttle != throttle) continue;
            count++;

            if (point.AmbientPressurePa < minimumPressure)
            {
                secondMinimumPressure = minimumPressure;
                secondMinimumIndex = minimumIndex;
                minimumPressure = point.AmbientPressurePa;
                minimumIndex = i;
            }
            else if (point.AmbientPressurePa < secondMinimumPressure)
            {
                secondMinimumPressure = point.AmbientPressurePa;
                secondMinimumIndex = i;
            }

            if (point.AmbientPressurePa > maximumPressure)
            {
                secondMaximumPressure = maximumPressure;
                secondMaximumIndex = maximumIndex;
                maximumPressure = point.AmbientPressurePa;
                maximumIndex = i;
            }
            else if (point.AmbientPressurePa > secondMaximumPressure)
            {
                secondMaximumPressure = point.AmbientPressurePa;
                secondMaximumIndex = i;
            }

            if (point.AmbientPressurePa >= pressure
                && point.AmbientPressurePa < upperPressure)
            {
                upperPressure = point.AmbientPressurePa;
                upperIndex = i;
            }
            if (point.AmbientPressurePa < pressure
                && point.AmbientPressurePa > lowerPressure)
            {
                lowerPressure = point.AmbientPressurePa;
                lowerIndex = i;
            }
        }

        if (count == 0)
            throw new InvalidOperationException("Performance map has no throttle group.");
        if (count == 1)
        {
            var only = points[minimumIndex];
            return (only.ThrustN, only.SpecificImpulseS);
        }

        // Match the old sorted-array boundary behavior at/below the minimum and above the
        // maximum pressure. Duplicate pressures are retained in original order by the
        // selection rules, just as LINQ's stable OrderBy did.
        if (upperIndex < 0)
        {
            lowerIndex = secondMaximumIndex;
            upperIndex = maximumIndex;
        }
        else if (lowerIndex < 0)
        {
            lowerIndex = minimumIndex;
            upperIndex = secondMinimumIndex;
        }

        var lower = points[lowerIndex];
        var upper = points[upperIndex];
        double span = upper.AmbientPressurePa - lower.AmbientPressurePa;
        double t = span > 1e-12
            ? (pressure - lower.AmbientPressurePa) / span
            : 0.0;
        return (
            lower.ThrustN + (upper.ThrustN - lower.ThrustN) * t,
            lower.SpecificImpulseS + (upper.SpecificImpulseS - lower.SpecificImpulseS) * t);
    }

    private static bool HasNozzleEquation(EngineModelDefinition model) =>
        model.ExitAreaM2.HasValue
        && model.NominalMassFlowKgS.HasValue
        && model.EffectiveExhaustVelocityMps.HasValue
        && model.EffectiveExitPressurePa.HasValue;

}

public sealed record PartVisualDescriptor(
    string PartInstanceId,
    string VisualId,
    Vector3d PositionM,
    Vector3d Forward,
    IReadOnlyDictionary<string, string> Configuration);

public interface IPartVisualFactory<out TVisual>
{
    TVisual Create(PartVisualDescriptor descriptor);
}

public sealed class EngineDefinitionCatalog
{
    public IReadOnlyDictionary<string, EngineModelDefinition> Models { get; }
    public IReadOnlyDictionary<string, EngineClusterDefinition> Clusters { get; }

    private EngineDefinitionCatalog(
        Dictionary<string, EngineModelDefinition> models,
        Dictionary<string, EngineClusterDefinition> clusters)
    {
        Models = models;
        Clusters = clusters;
    }

    public static EngineDefinitionCatalog Load(
        string modelDirectory,
        string clusterDirectory,
        DataProvenanceRegistry provenance)
    {
        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new JsonStringEnumConverter<EngineLifecycleState>(
                    JsonNamingPolicy.SnakeCaseLower),
            },
        };

        var models = LoadDirectory<EngineModelDefinition>(modelDirectory, options)
            .ToDictionary(m => m.Id, StringComparer.Ordinal);
        foreach (var model in models.Values) model.Validate(provenance);

        var clusters = LoadDirectory<EngineClusterDefinition>(clusterDirectory, options)
            .ToDictionary(c => c.Id, StringComparer.Ordinal);
        foreach (var cluster in clusters.Values) cluster.Validate(models);
        return new EngineDefinitionCatalog(models, clusters);
    }

    private static IEnumerable<T> LoadDirectory<T>(
        string directory,
        JsonSerializerOptions options)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException(directory);
        foreach (string path in Directory.GetFiles(directory, "*.json").Order())
        {
            yield return JsonSerializer.Deserialize<T>(File.ReadAllText(path), options)
                ?? throw new InvalidDataException($"Empty definition '{path}'.");
        }
    }
}
