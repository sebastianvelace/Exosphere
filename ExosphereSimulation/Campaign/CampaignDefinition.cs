namespace Exosphere.Simulation.Campaign;

using System.Text.Json;

public sealed class CampaignEntryDefinition
{
    public string MissionId { get; set; } = "";
    public int Sequence { get; set; }
    public string Title { get; set; } = "";
    public string? DefinitionId { get; set; }
}

public sealed class CampaignDefinition
{
    public string Id { get; set; } = "";
    public string TitleEn { get; set; } = "";
    public string TitleEs { get; set; } = "";
    public string DescriptionEn { get; set; } = "";
    public string DescriptionEs { get; set; } = "";
    public List<CampaignEntryDefinition> Missions { get; set; } = new();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)
            || string.IsNullOrWhiteSpace(TitleEn)
            || string.IsNullOrWhiteSpace(TitleEs)
            || string.IsNullOrWhiteSpace(DescriptionEn)
            || string.IsNullOrWhiteSpace(DescriptionEs)
            || Missions.Count == 0)
            throw new InvalidDataException($"Campaign '{Id}' is incomplete.");
        if (Missions.Select(item => item.MissionId).Any(
                string.IsNullOrWhiteSpace)
            || Missions.Select(item => item.MissionId)
                .Distinct(StringComparer.Ordinal).Count() != Missions.Count
            || Missions.Select(item => item.Sequence).Any(sequence => sequence <= 0)
            || Missions.Select(item => item.Sequence).Distinct().Count()
                != Missions.Count
            || Missions.Any(item => string.IsNullOrWhiteSpace(item.Title)))
            throw new InvalidDataException(
                $"Campaign '{Id}' has invalid mission entries.");
        int[] ordered = Missions.Select(item => item.Sequence).Order().ToArray();
        if (!ordered.SequenceEqual(
                Enumerable.Range(1, Missions.Count)))
            throw new InvalidDataException(
                $"Campaign '{Id}' sequence must be contiguous from one.");
    }

    public static CampaignDefinition LoadFromJson(string path)
    {
        var definition = JsonSerializer.Deserialize<CampaignDefinition>(
            File.ReadAllText(path),
            new JsonSerializerOptions
            {
                AllowTrailingCommas = true,
                PropertyNameCaseInsensitive = true,
            }) ?? throw new InvalidDataException(
                $"Empty campaign definition '{path}'.");
        definition.Validate();
        return definition;
    }
}
