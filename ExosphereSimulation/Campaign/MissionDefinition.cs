namespace Exosphere.Simulation.Campaign;

using System.Text.Json;
using System.Text.Json.Serialization;

[JsonConverter(typeof(JsonStringEnumConverter<MissionMetric>))]
public enum MissionMetric
{
    PeakAltitudeM,
    PeakSurfaceSpeedMps,
    MaximumDynamicPressurePa,
    MaximumGForce,
    MaximumDownrangeM,
    ElapsedSeconds,
    CrewAlive,
    VesselDestroyed,
    Splashdown,
    PhaseReached,
}

[JsonConverter(typeof(JsonStringEnumConverter<MissionComparison>))]
public enum MissionComparison
{
    AtLeast,
    AtMost,
    Between,
    True,
    False,
    Reached,
}

public sealed class MissionRuleDefinition
{
    public string Id { get; set; } = "";
    public string TitleEn { get; set; } = "";
    public string TitleEs { get; set; } = "";
    public MissionMetric Metric { get; set; }
    public MissionComparison Comparison { get; set; }
    public double Target { get; set; }
    public double? UpperTarget { get; set; }
    public string ExpectedText { get; set; } = "";
    public bool Required { get; set; } = true;
}

public sealed class MissionRewardDefinition
{
    public string Id { get; set; } = "";
    public int Amount { get; set; }
    public List<string> UnlockIds { get; set; } = new();
}

public sealed class MissionAnomalyDefinition
{
    public string Id { get; set; } = "";
    public string DescriptionEn { get; set; } = "";
    public string DescriptionEs { get; set; } = "";
    public bool EnabledByDefault { get; set; }
}

public sealed class MissionSourceDefinition
{
    public string Id { get; set; } = "";
    public string Url { get; set; } = "";
    public DateOnly AccessedDate { get; set; }
    public string Description { get; set; } = "";
}

public sealed class MissionDefinition
{
    public string Id { get; set; } = "";
    public int Sequence { get; set; }
    public string Program { get; set; } = "";
    public string TitleEn { get; set; } = "";
    public string TitleEs { get; set; } = "";
    public string SummaryEn { get; set; } = "";
    public string SummaryEs { get; set; } = "";
    public DateOnly HistoricalDate { get; set; }
    public string VehicleVariantId { get; set; } = "";
    public string LaunchSiteId { get; set; } = "";
    public string FlightProfileId { get; set; } = "";
    public List<string> CrewIds { get; set; } = new();
    public List<string> PrerequisiteMissionIds { get; set; } = new();
    public List<MissionRuleDefinition> Objectives { get; set; } = new();
    public List<MissionRuleDefinition> Limits { get; set; } = new();
    public List<MissionAnomalyDefinition> Anomalies { get; set; } = new();
    public List<MissionRewardDefinition> Rewards { get; set; } = new();
    public List<MissionSourceDefinition> Sources { get; set; } = new();

    public void Validate()
    {
        RequireText(Id, nameof(Id));
        RequireText(Program, nameof(Program));
        RequireText(TitleEn, nameof(TitleEn));
        RequireText(TitleEs, nameof(TitleEs));
        RequireText(SummaryEn, nameof(SummaryEn));
        RequireText(SummaryEs, nameof(SummaryEs));
        RequireText(VehicleVariantId, nameof(VehicleVariantId));
        RequireText(LaunchSiteId, nameof(LaunchSiteId));
        RequireText(FlightProfileId, nameof(FlightProfileId));
        if (Sequence <= 0 || HistoricalDate == default || Objectives.Count == 0
            || Sources.Count == 0)
            throw new InvalidDataException($"Mission '{Id}' is incomplete.");

        RequireUnique(CrewIds, "crew");
        RequireUnique(PrerequisiteMissionIds, "prerequisite");
        ValidateRules(Objectives, "objective");
        ValidateRules(Limits, "limit");
        RequireUnique(Anomalies.Select(item => item.Id), "anomaly");
        RequireUnique(Rewards.Select(item => item.Id), "reward");
        RequireUnique(Sources.Select(item => item.Id), "source");

        foreach (var anomaly in Anomalies)
        {
            RequireText(anomaly.DescriptionEn, "anomaly description");
            RequireText(anomaly.DescriptionEs, "anomaly description");
        }
        foreach (var reward in Rewards)
        {
            if (reward.Amount < 0)
                throw new InvalidDataException(
                    $"Mission '{Id}' reward '{reward.Id}' is negative.");
            RequireUnique(reward.UnlockIds, $"reward '{reward.Id}' unlock");
        }
        foreach (var source in Sources)
        {
            RequireText(source.Url, "source URL");
            RequireText(source.Description, "source description");
            if (!Uri.TryCreate(source.Url, UriKind.Absolute, out _)
                || source.AccessedDate == default)
                throw new InvalidDataException(
                    $"Mission '{Id}' source '{source.Id}' is invalid.");
        }
    }

    public static MissionDefinition LoadFromJson(string path)
    {
        var definition = JsonSerializer.Deserialize<MissionDefinition>(
            File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"Empty mission definition '{path}'.");
        definition.Validate();
        return definition;
    }

    private void ValidateRules(
        IReadOnlyCollection<MissionRuleDefinition> rules,
        string kind)
    {
        RequireUnique(rules.Select(rule => rule.Id), kind);
        foreach (var rule in rules)
        {
            RequireText(rule.TitleEn, $"{kind} title");
            RequireText(rule.TitleEs, $"{kind} title");
            if (!double.IsFinite(rule.Target)
                || rule.UpperTarget is { } upper && !double.IsFinite(upper)
                || rule.Comparison == MissionComparison.Between
                    && (rule.UpperTarget is not { } maximum
                        || maximum < rule.Target)
                || rule.Comparison == MissionComparison.Reached
                    && string.IsNullOrWhiteSpace(rule.ExpectedText)
                || rule.Metric == MissionMetric.PhaseReached
                    && rule.Comparison != MissionComparison.Reached
                || rule.Metric != MissionMetric.PhaseReached
                    && rule.Comparison == MissionComparison.Reached)
                throw new InvalidDataException(
                    $"Mission '{Id}' {kind} '{rule.Id}' is invalid.");
        }
    }

    private void RequireUnique(IEnumerable<string> values, string kind)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string value in values)
        {
            RequireText(value, kind);
            if (!seen.Add(value))
                throw new InvalidDataException(
                    $"Mission '{Id}' contains duplicate {kind} '{value}'.");
        }
    }

    private static void RequireText(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"Mission definition requires {field}.");
    }

    internal static JsonSerializerOptions JsonOptions { get; } = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter<MissionMetric>(
                JsonNamingPolicy.SnakeCaseLower),
            new JsonStringEnumConverter<MissionComparison>(
                JsonNamingPolicy.SnakeCaseLower),
        },
    };
}

public sealed class MissionDefinitionCatalog
{
    private readonly Dictionary<string, MissionDefinition> _definitions;

    private MissionDefinitionCatalog(IEnumerable<MissionDefinition> definitions)
    {
        _definitions = new(StringComparer.Ordinal);
        foreach (var definition in definitions.OrderBy(item => item.Sequence))
        {
            definition.Validate();
            if (!_definitions.TryAdd(definition.Id, definition))
                throw new InvalidDataException(
                    $"Duplicate mission definition '{definition.Id}'.");
        }
        if (_definitions.Values.Select(item => item.Sequence).Distinct().Count()
            != _definitions.Count)
            throw new InvalidDataException("Mission sequence numbers must be unique.");
    }

    public IReadOnlyDictionary<string, MissionDefinition> Definitions => _definitions;
    public IReadOnlyList<MissionDefinition> Ordered =>
        _definitions.Values.OrderBy(item => item.Sequence).ToArray();
    public MissionDefinition this[string id] => _definitions[id];

    public static MissionDefinitionCatalog LoadFromDirectory(string directory) =>
        new(Directory.GetFiles(directory, "*.json")
            .Order()
            .Select(MissionDefinition.LoadFromJson));
}
