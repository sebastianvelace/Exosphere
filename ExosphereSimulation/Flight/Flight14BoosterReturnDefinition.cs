namespace Exosphere.Simulation.Flight;

using System.Text.Json;

/// <summary>Estimated control and inventory partition; published clocks are comparison only.</summary>
public sealed class Flight14BoosterReturnDefinition
{
    public string Id { get; set; } = "";
    public string Status { get; set; } = "";
    public string Assumptions { get; set; } = "";
    public double LandingReserveKg { get; set; }
    public double TargetLatitudeDegrees { get; set; }
    public double TargetLongitudeDegrees { get; set; }
    public double BoostbackThrottle { get; set; }
    public double EstimatedCoastApogeeM { get; set; }
    public double MinimumFlipAlignment { get; set; }
    public double PointingGain { get; set; }
    public double PointingDamping { get; set; }
    public double MaximumReturnSeconds { get; set; }
    public double LandingArmAltitudeM { get; set; }
    public double LandingBrakingMargin { get; set; }
    public double TargetContactSpeedMps { get; set; }
    public double HorizontalDampingPerSecond { get; set; }
    public double MaximumLandingTiltDegrees { get; set; }
    public double MaximumContactSpeedMps { get; set; }
    public double LowestPointYM { get; set; }
    public double WaterDensityKgPerM3 { get; set; }
    public double WaterDragCoefficient { get; set; }
    public double WettingDepthM { get; set; }
    public double WaterObservationSeconds { get; set; }
    public double MinimumWaterLatitudeDegrees { get; set; }
    public double MaximumWaterLatitudeDegrees { get; set; }
    public double MinimumWaterLongitudeDegrees { get; set; }
    public double MaximumWaterLongitudeDegrees { get; set; }

    public static Flight14BoosterReturnDefinition LoadFromJson(string path)
    {
        var value = JsonSerializer.Deserialize<Flight14BoosterReturnDefinition>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Empty Flight 14 booster return estimate.");
        value.Validate(); return value;
    }

    public void Validate()
    {
        double[] positive = [LandingReserveKg, PointingGain, PointingDamping,
            MaximumReturnSeconds, EstimatedCoastApogeeM, LandingArmAltitudeM, TargetContactSpeedMps,
            HorizontalDampingPerSecond, MaximumLandingTiltDegrees,
            MaximumContactSpeedMps, WaterDensityKgPerM3, WaterDragCoefficient, WettingDepthM, WaterObservationSeconds];
        double[] coordinates = [TargetLatitudeDegrees, TargetLongitudeDegrees, MinimumWaterLatitudeDegrees,
            MaximumWaterLatitudeDegrees, MinimumWaterLongitudeDegrees, MaximumWaterLongitudeDegrees];
        if (string.IsNullOrWhiteSpace(Id) || Status != "engineering-estimate" || string.IsNullOrWhiteSpace(Assumptions)
            || positive.Any(v => !double.IsFinite(v) || v <= 0) || coordinates.Any(v => !double.IsFinite(v))
            || !double.IsFinite(BoostbackThrottle) || BoostbackThrottle <= 0 || BoostbackThrottle > 1
            || !double.IsFinite(MinimumFlipAlignment) || MinimumFlipAlignment <= 0 || MinimumFlipAlignment >= 1
            || !double.IsFinite(LowestPointYM) || LowestPointYM >= 0 || MaximumLandingTiltDegrees >= 45
            || !double.IsFinite(LandingBrakingMargin) || LandingBrakingMargin < 1 || LandingBrakingMargin > 2
            || EstimatedCoastApogeeM <= LandingArmAltitudeM
            || TargetContactSpeedMps >= MaximumContactSpeedMps
            || MinimumWaterLatitudeDegrees < -90 || MaximumWaterLatitudeDegrees > 90
            || MinimumWaterLongitudeDegrees < -180 || MaximumWaterLongitudeDegrees > 180
            || MinimumWaterLatitudeDegrees >= MaximumWaterLatitudeDegrees
            || MinimumWaterLongitudeDegrees >= MaximumWaterLongitudeDegrees
            || TargetLatitudeDegrees < MinimumWaterLatitudeDegrees || TargetLatitudeDegrees > MaximumWaterLatitudeDegrees
            || TargetLongitudeDegrees < MinimumWaterLongitudeDegrees || TargetLongitudeDegrees > MaximumWaterLongitudeDegrees)
            throw new InvalidDataException("Invalid Flight 14 booster return estimate.");
    }
}
