namespace Exosphere.Simulation.Flight;

using System.Text.Json;

/// <summary>Estimated terminal control envelope, independent of broadcast clocks.</summary>
public sealed class Flight14LandingDefinition
{
    public string Id { get; set; } = "";
    public string Status { get; set; } = "";
    public string Assumptions { get; set; } = "";
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
    }
}
