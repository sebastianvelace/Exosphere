namespace Exosphere.Simulation.Data;

using System.Text.Json;
using System.Text.Json.Serialization;

[JsonConverter(typeof(JsonStringEnumConverter<ProvenanceStatus>))]
public enum ProvenanceStatus
{
    Published,
    Derived,
    Estimated,
    Calibrated,
    Unknown,
    RegulatoryEnvelope,
}

public sealed class DataProvenanceRecord
{
    public string DataId { get; set; } = "";
    public string Field { get; set; } = "";
    public ProvenanceStatus Status { get; set; }
    public string Source { get; set; } = "";
    public DateOnly AccessedDate { get; set; }
    public string Variant { get; set; } = "";
    public string Conditions { get; set; } = "";
    public string Uncertainty { get; set; } = "";
}

public sealed class DataProvenanceManifest
{
    public string ManifestId { get; set; } = "";
    public List<DataProvenanceRecord> Records { get; set; } = new();
}

public sealed class DataProvenanceRegistry
{
    private readonly Dictionary<(string dataId, string field), DataProvenanceRecord> _records;

    private DataProvenanceRegistry(IEnumerable<DataProvenanceRecord> records)
    {
        _records = new();
        foreach (var record in records)
        {
            ValidateRecord(record);
            if (!_records.TryAdd((record.DataId, record.Field), record))
                throw new InvalidDataException(
                    $"Duplicate provenance for '{record.DataId}.{record.Field}'.");
        }
    }

    public IReadOnlyCollection<DataProvenanceRecord> Records => _records.Values;

    public DataProvenanceRecord Require(string dataId, string field) =>
        _records.TryGetValue((dataId, field), out var record)
            ? record
            : throw new InvalidDataException($"Missing provenance for '{dataId}.{field}'.");

    public void RequireFields(string dataId, params string[] fields)
    {
        foreach (string field in fields) _ = Require(dataId, field);
    }

    public static DataProvenanceRegistry LoadFromDirectory(string directory)
    {
        var options = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter<ProvenanceStatus>(JsonNamingPolicy.SnakeCaseLower) },
        };
        var records = new List<DataProvenanceRecord>();
        foreach (string path in Directory.GetFiles(directory, "*.json").Order())
        {
            string json = File.ReadAllText(path);
            var manifest = JsonSerializer.Deserialize<DataProvenanceManifest>(json, options)
                ?? throw new InvalidDataException($"Empty provenance manifest '{path}'.");
            if (string.IsNullOrWhiteSpace(manifest.ManifestId))
                throw new InvalidDataException($"Missing manifest id in '{path}'.");
            records.AddRange(manifest.Records);
        }
        return new DataProvenanceRegistry(records);
    }

    private static void ValidateRecord(DataProvenanceRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.DataId)
            || string.IsNullOrWhiteSpace(record.Field)
            || string.IsNullOrWhiteSpace(record.Source)
            || string.IsNullOrWhiteSpace(record.Variant)
            || string.IsNullOrWhiteSpace(record.Conditions)
            || string.IsNullOrWhiteSpace(record.Uncertainty)
            || record.AccessedDate == default)
            throw new InvalidDataException("Provenance records require source, date, variant, conditions and uncertainty.");
    }
}
