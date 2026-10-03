namespace Exosphere.Simulation.Flight;

using System.Text.Json;

/// <summary>Estimated terminal control envelope, independent of broadcast clocks.</summary>
public sealed class Flight14LandingDefinition
{
    public string Id { get; set; } = "";
    public string Status { get; set; } = "";
    public string Assumptions { get; set; } = "";
    public bool ContinueToWaterContact { get; set; }
    public double MaximumWaterEntrySpeedMps { get; set; }
    public double WaterObservationSeconds { get; set; }
    public double LowestPointYM { get; set; }
    public double WaterDensityKgPerM3 { get; set; }
    public double WaterDragCoefficient { get; set; }
    public double WettingDepthM { get; set; }
    public double MinimumWaterLatitudeDegrees { get; set; }
    public double MaximumWaterLatitudeDegrees { get; set; }
    public double MinimumWaterLongitudeDegrees { get; set; }
    public double MaximumWaterLongitudeDegrees { get; set; }
    public double FlipAltitudeM { get; set; }
    public double DiagnosticEndAltitudeM { get; set; }
    public double MaximumEndAirspeedMps { get; set; }
    public double MaximumBurnSeconds { get; set; }
    public double DescentProfileSlopePerSecond { get; set; }
    public double TargetFinalDescentSpeedMps { get; set; }
    public double MaximumTiltDegrees { get; set; }
    public double LateralDampingPerSecond { get; set; }
    public double VerticalDampingPerSecond { get; set; }
    public double ProportionalAttitudeGain { get; set; }
    public double AttitudeDampingGain { get; set; }
    public static Flight14LandingDefinition LoadFromJson(string path)
    {
        var definition = JsonSerializer.Deserialize<Flight14LandingDefinition>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Empty Flight 14 landing estimate.");
        definition.Validate(); return definition;
    }
    public void Validate()
    {
        double[] values = [FlipAltitudeM, DiagnosticEndAltitudeM, MaximumEndAirspeedMps,
            MaximumBurnSeconds, DescentProfileSlopePerSecond, TargetFinalDescentSpeedMps,
            MaximumTiltDegrees, LateralDampingPerSecond, VerticalDampingPerSecond,
            ProportionalAttitudeGain, AttitudeDampingGain];
        if (string.IsNullOrWhiteSpace(Id) || Status != "engineering-estimate"
            || string.IsNullOrWhiteSpace(Assumptions) || values.Any(v => !double.IsFinite(v) || v <= 0)
            || DiagnosticEndAltitudeM >= FlipAltitudeM || MaximumTiltDegrees >= 45
            || TargetFinalDescentSpeedMps >= MaximumEndAirspeedMps)
            throw new InvalidDataException("Invalid Flight 14 landing estimate.");
        double[] waterValues = [MaximumWaterEntrySpeedMps, WaterObservationSeconds, LowestPointYM,
            WaterDensityKgPerM3, WaterDragCoefficient, WettingDepthM, MinimumWaterLatitudeDegrees,
            MaximumWaterLatitudeDegrees, MinimumWaterLongitudeDegrees, MaximumWaterLongitudeDegrees];
        if (ContinueToWaterContact && (waterValues.Any(v => !double.IsFinite(v))
            || MaximumWaterEntrySpeedMps <= 0 || MaximumWaterEntrySpeedMps >= MaximumEndAirspeedMps
            || WaterObservationSeconds <= 0 || WaterObservationSeconds >= MaximumBurnSeconds
            || LowestPointYM >= 0 || WaterDensityKgPerM3 <= 0 || WaterDragCoefficient <= 0
            || WettingDepthM <= 0 || MinimumWaterLatitudeDegrees < -90 || MaximumWaterLatitudeDegrees > 90
            || MinimumWaterLatitudeDegrees >= MaximumWaterLatitudeDegrees
            || MinimumWaterLongitudeDegrees < -180 || MaximumWaterLongitudeDegrees > 180
            || MinimumWaterLongitudeDegrees >= MaximumWaterLongitudeDegrees))
            throw new InvalidDataException("Invalid Flight 14 water entry estimate.");
    }
}
