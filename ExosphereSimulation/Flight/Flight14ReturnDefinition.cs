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
    /// <summary>Osculating burn target above mean body radius; not geodetic altitude.</summary>
    public double TargetPeriapsisAltitudeM { get; set; }
    /// <summary>Engineering bound above mean body radius, independent of geodetic entry gates.</summary>
    public double MaximumTargetPeriapsisAltitudeM { get; set; }
    public double MinimumBurnAltitudeM { get; set; }
    public double MaximumBurnSeconds { get; set; }
    public double MaximumShutdownSeconds { get; set; }
    public double MinimumRemainingPropellantKg { get; set; }
    /// <summary>Descending entry gate above the reference ellipsoid.</summary>
    public double EntryInterfaceAltitudeM { get; set; }
    /// <summary>Descending diagnostic endpoint above the reference ellipsoid.</summary>
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
            TargetPeriapsisAltitudeM, MaximumTargetPeriapsisAltitudeM, MinimumBurnAltitudeM, MaximumBurnSeconds,
            MaximumShutdownSeconds, MinimumRemainingPropellantKg, EntryInterfaceAltitudeM,
            DiagnosticEndAltitudeM, MinimumEntrySpeedMps, MaximumMissionSeconds,
            EntryReferenceRateRadPerSecond, DeorbitReferenceRateRadPerSecond];
        if (string.IsNullOrWhiteSpace(Id) || Status != "engineering-estimate"
            || string.IsNullOrWhiteSpace(Assumptions) || values.Any(v => !double.IsFinite(v) || v <= 0)
            || AlignmentLeadSeconds >= EarliestDeorbitMissionSeconds
            // Periapsis is measured above mean body radius; the diagnostic endpoint
            // is geodetic. A shallow radial target may exceed that endpoint, and
            // production atmospheric propagation decides whether it is reached.
            || TargetPeriapsisAltitudeM > MaximumTargetPeriapsisAltitudeM
            || DiagnosticEndAltitudeM >= EntryInterfaceAltitudeM
            || EntryInterfaceAltitudeM >= MinimumBurnAltitudeM
            || EarliestDeorbitMissionSeconds + MaximumBurnSeconds + MaximumShutdownSeconds >= MaximumMissionSeconds)
            throw new InvalidDataException("Invalid Flight 14 return engineering envelope.");
    }
}
