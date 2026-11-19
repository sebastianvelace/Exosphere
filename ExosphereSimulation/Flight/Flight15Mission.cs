namespace Exosphere.Simulation.Flight;

using System.Text.Json;

/// <summary>Two independently initialized chapters of the Flight 15 reconstruction.</summary>
public static class Flight15Mission
{
    public const string AscentProfileId = "starship-flight15-ascent";
    public const string CatchProfileId = "starship-flight15-catch";
    public const string LaunchSiteId = "starbase_pad2";
    public const string VehicleFile = "starship_flight12_v3_2026.json";
    public const int PayloadCount = 26;

    public sealed record EntryApproach(double UpstreamDistanceM, double NorthOffsetM);

    public static Flight14ReturnDefinition LoadReturnEstimate(string dataDirectory) =>
        Flight14ReturnDefinition.LoadFromJson(Path.Combine(dataDirectory, "flight_profiles",
            "starship_flight15_return_estimate.json"));

    public static EntryApproach LoadEntryApproach(string dataDirectory)
    {
        string path = Path.Combine(dataDirectory, "flight_profiles/starship_flight15_entry_estimate.json");
        var approach = JsonSerializer.Deserialize<EntryApproach>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (approach == null || !double.IsFinite(approach.UpstreamDistanceM)
            || approach.UpstreamDistanceM <= 0 || !double.IsFinite(approach.NorthOffsetM))
            throw new InvalidDataException("Flight 15 entry approach requires finite, valid distances.");
        return approach;
    }

    public static Flight14LaunchDiagnostic CreateAscent(string dataDirectory, Universe? universe = null)
    {
        string profiles = Path.Combine(dataDirectory, "flight_profiles");
        var guidance = Flight14LaunchGuidanceDefinition.LoadFromJson(Path.Combine(profiles,
            "starship_flight15_guidance_estimate.json"));
        var payload = Flight14PayloadDefinition.LoadFromJson(Path.Combine(profiles,
            "starship_flight15_payload_estimate.json"));
        return Flight14LaunchDiagnostic.CreateLaunch(dataDirectory, universe, guidance, payload,
            PayloadCount, LaunchSiteId, "Starship Flight 15");
    }
}
