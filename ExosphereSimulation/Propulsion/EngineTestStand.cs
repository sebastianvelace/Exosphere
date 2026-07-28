namespace Exosphere.Simulation.Propulsion;

using Exosphere.Simulation.Parts;

public enum EngineTestPhase
{
    Preparation,
    Chill,
    Ignition,
    Ramp,
    Running,
    Shutdown,
    Complete,
    Failed,
}

public sealed record ThrottleProgramPoint(double TimeSinceIgnitionSeconds, double Throttle);

public sealed class EngineTestProfile
{
    public string ProfileId { get; set; } = "";
    public double PreparationSeconds { get; set; } = 2.0;
    public double ChillSeconds { get; set; } = 3.0;
    public double IgnitionSeconds { get; set; } = 0.5;
    public double ShutdownSeconds { get; set; } = 1.0;
    public double AmbientPressurePa { get; set; } = 101_325.0;
    public List<ThrottleProgramPoint> ThrottleProgram { get; set; } = new();

    public static EngineTestProfile Merlin1DAcceptance() => new()
    {
        ProfileId = "merlin1d-block5-acceptance-v1",
        PreparationSeconds = 2.0,
        ChillSeconds = 3.0,
        IgnitionSeconds = 0.5,
        ShutdownSeconds = 1.0,
        AmbientPressurePa = 101_325.0,
        ThrottleProgram =
        [
            new(0.0, 0.40),
            new(1.0, 1.00),
            new(5.0, 1.00),
            new(6.0, 0.70),
            new(8.0, 0.70),
            new(9.0, 1.00),
            new(12.0, 1.00),
        ],
    };
}

public sealed record EngineTestTelemetry(
    double TimeSeconds,
    EngineTestPhase Phase,
    double CommandedThrottle,
    double ActualThrottle,
    double ThrustN,
    double MassFlowKgS,
    double ChamberPressureFraction);

public sealed class EngineTestReport
{
    public string ProfileId { get; init; } = "";
    public string EngineDefinitionId { get; init; } = "";
    public bool Passed { get; init; }
    public double TotalImpulseNs { get; init; }
    public double PropellantConsumedKg { get; init; }
    public double PeakThrustN { get; init; }
    public IReadOnlyList<EngineTestTelemetry> Telemetry { get; init; } = [];
}

/// <summary>
/// Deterministic headless engine acceptance stand. It uses the same pressure-corrected
/// thrust, Isp, mass-flow identity and spool response as flight.
/// </summary>
public static class EngineTestStand
{
    public static EngineTestReport Run(
        EngineModelDefinition definition,
        EngineTestProfile profile,
        double stepSeconds = 0.01)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.Validate();
        return Run(new PartDefinition
        {
            Id = definition.Id,
            Name = definition.Name,
            CategoryStr = "engine",
            MassDry = 1.0,
            ThrustSL = definition.RatedThrustSeaLevelN,
            ThrustVac = definition.RatedThrustVacuumN,
            IspSL = definition.SpecificImpulseSeaLevelS,
            IspVac = definition.SpecificImpulseVacuumS,
            GimbalRange = definition.GimbalRangeDeg,
            FuelTypeStr = $"{definition.Fuel}+{definition.Oxidizer}",
            MixtureRatio = definition.MixtureRatioOxidizerToFuel,
            MinThrottle = definition.MinimumThrottle,
            EngineCount = 1,
            EngineModelId = definition.Id,
            EngineStartupSeconds = definition.StartupSeconds,
            EngineShutdownSeconds = definition.ShutdownSeconds,
            ResolvedEngineModel = definition,
        }, profile, stepSeconds);
    }

    public static EngineTestReport Run(
        PartDefinition definition,
        EngineTestProfile profile,
        double stepSeconds = 0.01)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(profile);
        if (definition.Category != PartCategory.Engine)
            throw new ArgumentException("The test stand requires an engine definition.", nameof(definition));
        if (stepSeconds <= 0.0 || !double.IsFinite(stepSeconds))
            throw new ArgumentOutOfRangeException(nameof(stepSeconds));
        if (profile.ThrottleProgram.Count == 0)
            throw new ArgumentException("The throttle program is empty.", nameof(profile));

        var program = profile.ThrottleProgram.OrderBy(p => p.TimeSinceIgnitionSeconds).ToArray();
        if (program[0].TimeSinceIgnitionSeconds < 0.0
            || program.Any(p => !double.IsFinite(p.TimeSinceIgnitionSeconds)
                                || !double.IsFinite(p.Throttle)
                                || p.Throttle is < 0.0 or > 1.0))
            throw new ArgumentException("The throttle program is invalid.", nameof(profile));

        var engine = new Part(definition, $"test-stand:{definition.Id}");
        double ignitionAt = profile.PreparationSeconds + profile.ChillSeconds;
        double burnEndsAt = ignitionAt + profile.IgnitionSeconds
            + program[^1].TimeSinceIgnitionSeconds;
        double completeAt = burnEndsAt + profile.ShutdownSeconds;
        var telemetry = new List<EngineTestTelemetry>();
        double impulse = 0.0;
        double propellant = 0.0;
        double peak = 0.0;

        for (double time = 0.0; time <= completeAt + stepSeconds * 0.5; time += stepSeconds)
        {
            EngineTestPhase phase;
            double commanded;
            if (time < profile.PreparationSeconds)
            {
                phase = EngineTestPhase.Preparation;
                commanded = 0.0;
            }
            else if (time < ignitionAt)
            {
                phase = EngineTestPhase.Chill;
                commanded = 0.0;
            }
            else if (time < ignitionAt + profile.IgnitionSeconds)
            {
                phase = EngineTestPhase.Ignition;
                commanded = program[0].Throttle;
            }
            else if (time < burnEndsAt)
            {
                double programTime = time - ignitionAt - profile.IgnitionSeconds;
                commanded = Interpolate(program, programTime);
                phase = engine.ThrottleLevel + 1e-6 < commanded
                    ? EngineTestPhase.Ramp
                    : EngineTestPhase.Running;
            }
            else if (time < completeAt)
            {
                phase = EngineTestPhase.Shutdown;
                commanded = 0.0;
            }
            else
            {
                phase = EngineTestPhase.Complete;
                commanded = 0.0;
            }

            if (engine.HasEngineRuntime)
                engine.AdvanceEngineRuntime(commanded, stepSeconds);
            else
                engine.SpoolToward(
                    engine.ApplyThrottleFloor(commanded), stepSeconds);
            double thrust = engine.GetThrustMagnitude(profile.AmbientPressurePa);
            double flow = engine.GetMassFlow(profile.AmbientPressurePa);
            impulse += thrust * stepSeconds;
            propellant += flow * stepSeconds;
            peak = System.Math.Max(peak, thrust);
            telemetry.Add(new EngineTestTelemetry(
                time, phase, commanded, engine.ThrottleLevel, thrust, flow,
                engine.HasEngineRuntime
                    ? engine.EngineStates.Average(
                        state => state.ChamberPressureFraction)
                    : definition.ThrustSL > 0.0
                        ? thrust / engine.GetRatedFullThrottleThrustMagnitude(
                            profile.AmbientPressurePa)
                        : engine.ThrottleLevel));
        }

        double rating = engine.GetRatedFullThrottleThrustMagnitude(profile.AmbientPressurePa);
        bool passed = telemetry.Any(t => t.Phase == EngineTestPhase.Running)
            && peak >= rating * 0.995
            && telemetry[^1].ActualThrottle <= 1e-9
            && propellant > 0.0;
        return new EngineTestReport
        {
            ProfileId = profile.ProfileId,
            EngineDefinitionId = definition.Id,
            Passed = passed,
            TotalImpulseNs = impulse,
            PropellantConsumedKg = propellant,
            PeakThrustN = peak,
            Telemetry = telemetry,
        };
    }

    private static double Interpolate(ThrottleProgramPoint[] program, double time)
    {
        if (time <= program[0].TimeSinceIgnitionSeconds) return program[0].Throttle;
        for (int i = 1; i < program.Length; i++)
        {
            if (time > program[i].TimeSinceIgnitionSeconds) continue;
            var a = program[i - 1];
            var b = program[i];
            double span = b.TimeSinceIgnitionSeconds - a.TimeSinceIgnitionSeconds;
            if (span <= 1e-12) return b.Throttle;
            double t = (time - a.TimeSinceIgnitionSeconds) / span;
            return a.Throttle + (b.Throttle - a.Throttle) * t;
        }
        return program[^1].Throttle;
    }
}
