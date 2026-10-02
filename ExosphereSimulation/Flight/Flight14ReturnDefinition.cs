namespace Exosphere.Simulation.Flight;

using System.Text.Json;

/// <summary>Estimated return commands and safety bounds, separate from observed telemetry.</summary>
public sealed class Flight14ReturnDefinition
{
    public string Id { get; set; } = "";
    public string Status { get; set; } = "";
    public string Assumptions { get; set; } = "";
    public double EarliestDeorbitMissionSeconds { get; set; }
    public double AlignmentLeadSeconds { get; set; }
    public double TargetPeriapsisAltitudeM { get; set; }
    public double MinimumBurnAltitudeM { get; set; }
    public double MaximumBurnSeconds { get; set; }
    public double MaximumShutdownSeconds { get; set; }
    public double MinimumRemainingPropellantKg { get; set; }
    public double EntryInterfaceAltitudeM { get; set; }
    public double DiagnosticEndAltitudeM { get; set; }
    public double MinimumEntrySpeedMps { get; set; }
    public double MaximumMissionSeconds { get; set; }
    public double EntryReferenceRateRadPerSecond { get; set; }
    public double DeorbitReferenceRateRadPerSecond { get; set; }

    public static Flight14ReturnDefinition LoadFromJson(string path)
    {
        var result = JsonSerializer.Deserialize<Flight14ReturnDefinition>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Empty Flight 14 return estimate.");
        result.Validate();
        return result;
    }

    public void Validate()
    {
        double[] values = [EarliestDeorbitMissionSeconds, AlignmentLeadSeconds,
            TargetPeriapsisAltitudeM, MinimumBurnAltitudeM, MaximumBurnSeconds,
            MaximumShutdownSeconds, MinimumRemainingPropellantKg, EntryInterfaceAltitudeM,
            DiagnosticEndAltitudeM, MinimumEntrySpeedMps, MaximumMissionSeconds,
            EntryReferenceRateRadPerSecond, DeorbitReferenceRateRadPerSecond];
        if (string.IsNullOrWhiteSpace(Id) || Status != "engineering-estimate"
            || string.IsNullOrWhiteSpace(Assumptions) || values.Any(v => !double.IsFinite(v) || v <= 0)
            || AlignmentLeadSeconds >= EarliestDeorbitMissionSeconds
            || TargetPeriapsisAltitudeM >= DiagnosticEndAltitudeM
            || DiagnosticEndAltitudeM >= EntryInterfaceAltitudeM
            || EntryInterfaceAltitudeM >= MinimumBurnAltitudeM
            || EarliestDeorbitMissionSeconds + MaximumBurnSeconds + MaximumShutdownSeconds >= MaximumMissionSeconds)
            throw new InvalidDataException("Invalid Flight 14 return engineering envelope.");
    }
}
