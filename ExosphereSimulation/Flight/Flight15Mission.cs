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

    /// <param name="CatchEntryMassKg">Ship mass at the 70 km entry state. The main tank is filled to
    /// reach it; the single landing burn then lands near this mass. Zero keeps the legacy
    /// 12 % main-tank fill.</param>
    public sealed record EntryApproach(double UpstreamDistanceM, double NorthOffsetM, double CatchEntryMassKg = 0.0)
    {
        /// <summary>Entry state for the single landing burn, when the chapter flies it.</summary>
        public EntryApproach? SingleBurn { get; init; }
    }

    /// <summary>Opt-in for the single-burn tower catch while it is still being validated.</summary>
    public static bool SingleBurnCatchEnabled =>
        System.Environment.GetEnvironmentVariable("EXOSPHERE_SINGLE_BURN_CATCH") == "1";

    public static Flight14ReturnDefinition LoadReturnEstimate(string dataDirectory) =>
        Flight14ReturnDefinition.LoadFromJson(Path.Combine(dataDirectory, "flight_profiles",
            "starship_flight15_return_estimate.json"));

    public static Flight14DescentDefinition LoadDescentEstimate(string dataDirectory) =>
        Flight14DescentDefinition.LoadFromJson(Path.Combine(dataDirectory, "flight_profiles",
            "starship_flight15_descent_estimate.json"));

    public static EntryApproach LoadEntryApproach(string dataDirectory)
    {
        string path = Path.Combine(dataDirectory, "flight_profiles/starship_flight15_entry_estimate.json");
        var approach = JsonSerializer.Deserialize<EntryApproach>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (approach == null || !double.IsFinite(approach.UpstreamDistanceM)
            || approach.UpstreamDistanceM <= 0 || !double.IsFinite(approach.NorthOffsetM)
            || !double.IsFinite(approach.CatchEntryMassKg) || approach.CatchEntryMassKg < 0)
            throw new InvalidDataException("Flight 15 entry approach requires finite, valid distances.");
        if (SingleBurnCatchEnabled && approach.SingleBurn is { } singleBurn)
        {
            if (!double.IsFinite(singleBurn.UpstreamDistanceM) || singleBurn.UpstreamDistanceM <= 0
                || !double.IsFinite(singleBurn.NorthOffsetM) || !(singleBurn.CatchEntryMassKg > 0))
                throw new InvalidDataException("Flight 15 single-burn entry requires finite, valid values.");
            return singleBurn;
        }
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
