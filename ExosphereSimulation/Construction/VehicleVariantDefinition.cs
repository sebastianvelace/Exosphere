namespace Exosphere.Simulation.Construction;

using System.Text.Json;

public sealed class VehicleVariantDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Family { get; set; } = "";
    public DateOnly AsOfDate { get; set; }
    public string FairingVariant { get; set; } = "standard";
    public string PrimarySource { get; set; } = "";
    public List<string> StackTopToBottom { get; set; } = new();
    public List<string> EngineClusterIds { get; set; } = new();

    public static VehicleVariantDefinition LoadFromJson(string path)
    {
        var definition = JsonSerializer.Deserialize<VehicleVariantDefinition>(
            File.ReadAllText(path),
            new JsonSerializerOptions
            {
                AllowTrailingCommas = true,
                PropertyNameCaseInsensitive = true,
            }) ?? throw new InvalidDataException($"Empty vehicle variant '{path}'.");
        definition.Validate();
        return definition;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name)
            || string.IsNullOrWhiteSpace(Family) || AsOfDate == default
            || string.IsNullOrWhiteSpace(PrimarySource) || StackTopToBottom.Count == 0)
            throw new InvalidDataException($"Vehicle variant '{Id}' is incomplete.");
        if (StackTopToBottom.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException($"Vehicle variant '{Id}' contains an empty part id.");
        if (EngineClusterIds.Any(string.IsNullOrWhiteSpace)
            || EngineClusterIds.Distinct(StringComparer.Ordinal).Count() != EngineClusterIds.Count)
            throw new InvalidDataException(
                $"Vehicle variant '{Id}' contains invalid engine cluster ids.");
    }

    public VesselAssembly Build(PartCatalog catalog)
    {
        Validate();
        var assembly = new VesselAssembly(catalog);
        var current = assembly.AddRoot(StackTopToBottom[0]);
        foreach (string partId in StackTopToBottom.Skip(1))
            current = assembly.AttachPartAutomatically(current.InstanceId, partId);
        return assembly;
    }
}
