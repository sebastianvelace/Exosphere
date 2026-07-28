namespace Exosphere.Simulation;

using System.Text.Json;

/// <summary>Stable, data-driven identity used to place historical crew in campaign vessels.</summary>
public sealed class CrewDefinition
{
    public string Id { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Agency { get; set; } = "";
    public DateOnly AsOfDate { get; set; }
    public int PilotLevel { get; set; }
    public int EngineerLevel { get; set; }
    public int ScientistLevel { get; set; }
    public string PrimarySource { get; set; } = "";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)
            || string.IsNullOrWhiteSpace(FirstName)
            || string.IsNullOrWhiteSpace(LastName)
            || string.IsNullOrWhiteSpace(Agency)
            || AsOfDate == default
            || !Uri.TryCreate(PrimarySource, UriKind.Absolute, out _)
            || PilotLevel is < 0 or > 5
            || EngineerLevel is < 0 or > 5
            || ScientistLevel is < 0 or > 5)
            throw new InvalidDataException($"Crew definition '{Id}' is invalid.");
    }

    public CrewMember CreateMember()
    {
        Validate();
        return new CrewMember
        {
            Id = Id,
            FirstName = FirstName,
            LastName = LastName,
            PilotLevel = PilotLevel,
            EngineerLevel = EngineerLevel,
            ScientistLevel = ScientistLevel,
        };
    }

    public static CrewDefinition LoadFromJson(string path)
    {
        var definition = JsonSerializer.Deserialize<CrewDefinition>(
            File.ReadAllText(path),
            new JsonSerializerOptions
            {
                AllowTrailingCommas = true,
                PropertyNameCaseInsensitive = true,
            }) ?? throw new InvalidDataException($"Empty crew definition '{path}'.");
        definition.Validate();
        return definition;
    }

    public static IReadOnlyDictionary<string, CrewDefinition> LoadAllFromDirectory(
        string directory)
    {
        var result = new Dictionary<string, CrewDefinition>(StringComparer.Ordinal);
        if (!Directory.Exists(directory)) return result;
        foreach (string path in Directory.GetFiles(directory, "*.json"))
        {
            var definition = LoadFromJson(path);
            if (!result.TryAdd(definition.Id, definition))
                throw new InvalidDataException(
                    $"Duplicate crew definition '{definition.Id}'.");
        }
        return result;
    }
}
