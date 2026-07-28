namespace Exosphere.Simulation.Campaign;

using Exosphere.Simulation.Persistence;

public sealed record MissionTelemetrySnapshot
{
    public double MissionTimeSeconds { get; init; }
    public string Phase { get; init; } = "";
    public double AltitudeM { get; init; }
    public double SurfaceSpeedMps { get; init; }
    public double DynamicPressurePa { get; init; }
    public double GForce { get; init; }
    public double DownrangeM { get; init; }
    public bool CrewAlive { get; init; } = true;
    public bool VesselDestroyed { get; init; }
    public bool Splashdown { get; init; }

    public void Validate()
    {
        foreach (double value in new[]
                 {
                     MissionTimeSeconds, AltitudeM, SurfaceSpeedMps,
                     DynamicPressurePa, GForce, DownrangeM,
                 })
            if (!double.IsFinite(value) || value < 0.0)
                throw new ArgumentOutOfRangeException(
                    nameof(MissionTelemetrySnapshot),
                    "Mission telemetry must be finite and non-negative.");
        if (string.IsNullOrWhiteSpace(Phase))
            throw new ArgumentException("Mission telemetry requires a phase.");
    }
}

public sealed class MissionEvidence
{
    public double ElapsedSeconds { get; private set; }
    public double PeakAltitudeM { get; private set; }
    public double PeakSurfaceSpeedMps { get; private set; }
    public double MaximumDynamicPressurePa { get; private set; }
    public double MaximumGForce { get; private set; }
    public double MaximumDownrangeM { get; private set; }
    public bool CrewAlive { get; private set; } = true;
    public bool VesselDestroyed { get; private set; }
    public bool Splashdown { get; private set; }
    public HashSet<string> ReachedPhases { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Observe(MissionTelemetrySnapshot snapshot)
    {
        snapshot.Validate();
        if (snapshot.MissionTimeSeconds + 1e-9 < ElapsedSeconds)
            throw new InvalidOperationException(
                "Mission telemetry time cannot move backwards.");

        ElapsedSeconds = snapshot.MissionTimeSeconds;
        PeakAltitudeM = System.Math.Max(PeakAltitudeM, snapshot.AltitudeM);
        PeakSurfaceSpeedMps =
            System.Math.Max(PeakSurfaceSpeedMps, snapshot.SurfaceSpeedMps);
        MaximumDynamicPressurePa =
            System.Math.Max(MaximumDynamicPressurePa, snapshot.DynamicPressurePa);
        MaximumGForce = System.Math.Max(MaximumGForce, snapshot.GForce);
        MaximumDownrangeM =
            System.Math.Max(MaximumDownrangeM, snapshot.DownrangeM);
        CrewAlive &= snapshot.CrewAlive;
        VesselDestroyed |= snapshot.VesselDestroyed;
        Splashdown |= snapshot.Splashdown;
        ReachedPhases.Add(snapshot.Phase);
    }

    public double MetricValue(MissionMetric metric) => metric switch
    {
        MissionMetric.PeakAltitudeM => PeakAltitudeM,
        MissionMetric.PeakSurfaceSpeedMps => PeakSurfaceSpeedMps,
        MissionMetric.MaximumDynamicPressurePa => MaximumDynamicPressurePa,
        MissionMetric.MaximumGForce => MaximumGForce,
        MissionMetric.MaximumDownrangeM => MaximumDownrangeM,
        MissionMetric.ElapsedSeconds => ElapsedSeconds,
        MissionMetric.CrewAlive => CrewAlive ? 1.0 : 0.0,
        MissionMetric.VesselDestroyed => VesselDestroyed ? 1.0 : 0.0,
        MissionMetric.Splashdown => Splashdown ? 1.0 : 0.0,
        MissionMetric.PhaseReached => double.NaN,
        _ => throw new ArgumentOutOfRangeException(nameof(metric)),
    };

    public MissionSaveV2 Capture(
        string missionId,
        string currentPhase,
        IEnumerable<string>? completedObjectives = null) => new()
    {
        MissionId = missionId,
        Phase = currentPhase,
        ObjectiveProgress = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["elapsedSeconds"] = ElapsedSeconds,
            ["peakAltitudeM"] = PeakAltitudeM,
            ["peakSurfaceSpeedMps"] = PeakSurfaceSpeedMps,
            ["maximumDynamicPressurePa"] = MaximumDynamicPressurePa,
            ["maximumGForce"] = MaximumGForce,
            ["maximumDownrangeM"] = MaximumDownrangeM,
            ["crewAlive"] = CrewAlive ? 1.0 : 0.0,
            ["vesselDestroyed"] = VesselDestroyed ? 1.0 : 0.0,
            ["splashdown"] = Splashdown ? 1.0 : 0.0,
        },
        ReachedPhases = new HashSet<string>(
            ReachedPhases, StringComparer.OrdinalIgnoreCase),
        CompletedObjectives = completedObjectives == null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(
                completedObjectives, StringComparer.Ordinal),
    };

    public static MissionEvidence Restore(MissionSaveV2 save)
    {
        ArgumentNullException.ThrowIfNull(save);
        var evidence = new MissionEvidence
        {
            ElapsedSeconds = Read("elapsedSeconds"),
            PeakAltitudeM = Read("peakAltitudeM"),
            PeakSurfaceSpeedMps = Read("peakSurfaceSpeedMps"),
            MaximumDynamicPressurePa = Read("maximumDynamicPressurePa"),
            MaximumGForce = Read("maximumGForce"),
            MaximumDownrangeM = Read("maximumDownrangeM"),
            CrewAlive = Read("crewAlive", 1.0) >= 0.5,
            VesselDestroyed = Read("vesselDestroyed") >= 0.5,
            Splashdown = Read("splashdown") >= 0.5,
        };
        evidence.ReachedPhases.UnionWith(save.ReachedPhases);
        return evidence;

        double Read(string key, double fallback = 0.0) =>
            save.ObjectiveProgress.TryGetValue(key, out double value)
                ? value : fallback;
    }
}
