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
    /// <summary>Constant deceleration of the braking segment above the terminal altitude (m/s²).</summary>
    public double BrakingDecelerationMps2 { get; set; }
    /// <summary>Altitude (m) below which the gentler terminal deceleration applies.</summary>
    public double TerminalProfileAltitudeM { get; set; }
    /// <summary>Constant deceleration of the terminal segment down to the water (m/s²).</summary>
    public double TerminalDecelerationMps2 { get; set; }
    public double TargetFinalDescentSpeedMps { get; set; }
    public double MaximumTiltDegrees { get; set; }
    public double LateralDampingPerSecond { get; set; }
    public double VerticalDampingPerSecond { get; set; }
    public double ProportionalAttitudeGain { get; set; }
    public double AttitudeDampingGain { get; set; }
    /// <summary>Rate damping during the flip (lower: the turn runs at the gimbal-limited rate).</summary>
    public double FlipAttitudeDampingGain { get; set; }
    public static Flight14LandingDefinition LoadFromJson(string path)
    {
        var definition = JsonSerializer.Deserialize<Flight14LandingDefinition>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Empty Flight 14 landing estimate.");
        definition.Validate(); return definition;
    }
    /// <summary>
    /// Target descent speed (m/s, positive down) at a clearance above the water: two
    /// constant-deceleration segments, v² = v_f² + 2·a_t·min(h, h_t) + 2·a_b·max(0, h − h_t).
    /// </summary>
    public double TargetDescentSpeedMps(double clearanceM)
    {
        double h = System.Math.Max(0.0, clearanceM);
        return System.Math.Sqrt(TargetFinalDescentSpeedMps * TargetFinalDescentSpeedMps
            + 2.0 * TerminalDecelerationMps2 * System.Math.Min(h, TerminalProfileAltitudeM)
            + 2.0 * BrakingDecelerationMps2 * System.Math.Max(0.0, h - TerminalProfileAltitudeM));
    }

    /// <summary>Deceleration (m/s², upward) the profile asks for at a clearance (its feed-forward).</summary>
    public double TargetDecelerationMps2(double clearanceM) =>
        clearanceM > TerminalProfileAltitudeM ? BrakingDecelerationMps2 : TerminalDecelerationMps2;

    public void Validate()
    {
        double[] values = [FlipAltitudeM, DiagnosticEndAltitudeM, MaximumEndAirspeedMps,
            MaximumBurnSeconds, BrakingDecelerationMps2, TerminalProfileAltitudeM, TerminalDecelerationMps2,
            TargetFinalDescentSpeedMps,
            MaximumTiltDegrees, LateralDampingPerSecond, VerticalDampingPerSecond,
            ProportionalAttitudeGain, AttitudeDampingGain, FlipAttitudeDampingGain];
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
