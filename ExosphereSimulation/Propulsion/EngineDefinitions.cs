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
    public double SpecificImpulseSeaLevelS { get; set; }
    public double SpecificImpulseVacuumS { get; set; }
    public double MinimumThrottle { get; set; }
    public double MaximumThrottle { get; set; } = 1.0;
    public double GimbalRangeDeg { get; set; }
    public double GimbalRateDegPerS { get; set; }
    public double GimbalAccelerationDegPerS2 { get; set; }
    public double StartupSeconds { get; set; }
    public double ShutdownSeconds { get; set; }
    public int RestartLimit { get; set; }
    public double? ExitAreaM2 { get; set; }
    public double? NominalMassFlowKgS { get; set; }
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
        if (!double.IsFinite(MinimumThrottle)
            || MinimumThrottle < 0.0
            || MinimumThrottle > MaximumThrottle
            || MaximumThrottle > 1.0)
            throw new InvalidDataException($"Engine model '{Id}' has an invalid throttle range.");
        if (!double.IsFinite(GimbalRangeDeg)
            || !double.IsFinite(GimbalRateDegPerS)
            || !double.IsFinite(GimbalAccelerationDegPerS2)
            || GimbalRangeDeg < 0.0
            || GimbalRateDegPerS < 0.0
            || GimbalAccelerationDegPerS2 < 0.0
            || RestartLimit < 0)
            throw new InvalidDataException($"Engine model '{Id}' has invalid actuation limits.");
        if (ExitAreaM2 is { } area && (!double.IsFinite(area) || area <= 0.0))
            throw new InvalidDataException($"Engine model '{Id}' has an invalid exit area.");
        if (NominalMassFlowKgS is { } flow && (!double.IsFinite(flow) || flow <= 0.0))
            throw new InvalidDataException($"Engine model '{Id}' has an invalid mass flow.");

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
            "gimbalEnvelope",
            "startupTransient",
            "shutdownTransient");

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
            if (mount.PositionM.Length != 3
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
    string? FailureCode);

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
