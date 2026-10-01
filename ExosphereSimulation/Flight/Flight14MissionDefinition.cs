namespace Exosphere.Simulation.Flight;

using System.Text.Json;

/// <summary>Source-backed mission sequence. Reference clocks never drive physical state.</summary>
public sealed class Flight14MissionDefinition
{
    public string Id { get; set; } = "";
    public DateTimeOffset LaunchUtc { get; set; }
    public string LaunchSiteId { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public DateOnly AccessedDate { get; set; }
    public int PayloadCount { get; set; }
    public string ReturnRegion { get; set; } = "";
    public string BoosterRecovery { get; set; } = "";
    public string ShipRecovery { get; set; } = "";
    public int BoosterAscentEngines { get; set; }
    public int BoosterAscentEngineLosses { get; set; }
    public int BoosterBoostbackEngines { get; set; }
    public List<int> BoosterLandingEngineSequence { get; set; } = new();
    public int ShipAscentEngines { get; set; }
    public int ShipVacuumEngineLosses { get; set; }
    public int InsertionSeaLevelEngines { get; set; }
    public int DeorbitSeaLevelEngines { get; set; }
    public int ShipLandingIgnitionEngines { get; set; }
    public string BoosterBoostbackResourceLimit { get; set; } = "";
    public string ObservationsFile { get; set; } = "";
    public List<string> Unknowns { get; set; } = new();
    public List<PlannedEvent> PlannedEvents { get; set; } = new();
    public List<ReferenceWindow> ReferenceWindows { get; set; } = new();
    public bool ComparisonOnly { get; set; }
    public double ClockUncertaintySeconds { get; set; }

    public sealed record PlannedEvent(string Id, double ElapsedSeconds);
    public sealed record ReferenceWindow(
        string Id, double StartSeconds, double EndSeconds, string Source, string Notes);

    public static Flight14MissionDefinition LoadFromJson(string path)
    {
        var result = JsonSerializer.Deserialize<Flight14MissionDefinition>(
            File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException($"Empty Flight 14 definition '{path}'.");
        result.Validate();
        return result;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || LaunchUtc == default
            || string.IsNullOrWhiteSpace(LaunchSiteId) || AccessedDate == default
            || !Uri.TryCreate(SourceUrl, UriKind.Absolute, out var source)
            || source.Scheme != Uri.UriSchemeHttps || PayloadCount <= 0
            || string.IsNullOrWhiteSpace(ReturnRegion)
            || string.IsNullOrWhiteSpace(BoosterRecovery) || string.IsNullOrWhiteSpace(ShipRecovery)
            || string.IsNullOrWhiteSpace(ObservationsFile)
            || !ComparisonOnly || Unknowns.Count == 0 || Unknowns.Any(string.IsNullOrWhiteSpace)
            || !double.IsFinite(ClockUncertaintySeconds) || ClockUncertaintySeconds < 0)
            throw new InvalidDataException("Flight 14 reference identity/provenance is incomplete.");

        if (BoosterAscentEngines <= 0 || BoosterAscentEngineLosses < 0
            || BoosterAscentEngineLosses >= BoosterAscentEngines
            || BoosterBoostbackEngines <= 0 || BoosterBoostbackEngines > BoosterAscentEngines
            || BoosterLandingEngineSequence.Count == 0
            || BoosterLandingEngineSequence.Any(count => count <= 0 || count > BoosterBoostbackEngines)
            || ShipAscentEngines <= 0 || ShipVacuumEngineLosses < 0
            || ShipVacuumEngineLosses >= ShipAscentEngines
            || InsertionSeaLevelEngines <= 0 || InsertionSeaLevelEngines > ShipAscentEngines
            || DeorbitSeaLevelEngines <= 0 || DeorbitSeaLevelEngines > ShipAscentEngines
            || ShipLandingIgnitionEngines <= 0 || ShipLandingIgnitionEngines > ShipAscentEngines
            || string.IsNullOrWhiteSpace(BoosterBoostbackResourceLimit))
            throw new InvalidDataException("Flight 14 engine/resource sequence is invalid.");

        if (PlannedEvents.Count == 0 || ReferenceWindows.Count == 0
            || PlannedEvents.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != PlannedEvents.Count
            || ReferenceWindows.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != ReferenceWindows.Count)
            throw new InvalidDataException("Flight 14 reference events are missing or duplicated.");
        double previous = -1;
        foreach (var item in PlannedEvents)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || !double.IsFinite(item.ElapsedSeconds)
                || item.ElapsedSeconds < 0 || item.ElapsedSeconds < previous)
                throw new InvalidDataException("Flight 14 planned events are invalid or unordered.");
            previous = item.ElapsedSeconds;
        }
        previous = -1;
        foreach (var item in ReferenceWindows)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Source)
                || string.IsNullOrWhiteSpace(item.Notes) || !double.IsFinite(item.StartSeconds)
                || !double.IsFinite(item.EndSeconds) || item.StartSeconds < 0
                || item.EndSeconds < item.StartSeconds || item.StartSeconds < previous)
                throw new InvalidDataException("Flight 14 observed windows are invalid or unordered.");
            previous = item.StartSeconds;
        }
    }
}
