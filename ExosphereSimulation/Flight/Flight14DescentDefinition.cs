namespace Exosphere.Simulation.Flight;

using System.Text.Json;

/// <summary>Estimated aerodynamic descent controls, separate from observed Flight 14 telemetry.</summary>
public sealed class Flight14DescentDefinition
{
    public string Id { get; set; } = "";
    public string Status { get; set; } = "";
    public string Assumptions { get; set; } = "";
    public double MaximumMissionSeconds { get; set; }
    public double DiagnosticEndAltitudeM { get; set; }
    public double MaximumEndSpeedMps { get; set; }
    public double HypersonicControlThresholdMps { get; set; }
    public double TerminalAngleOfAttackSpeedMps { get; set; }
    public double EntryAngleOfAttackDegrees { get; set; }
    public double TerminalAngleOfAttackDegrees { get; set; }
    public double ReferenceSlewRateRadPerSecond { get; set; }
    public double BankOnsetDynamicPressurePa { get; set; }
    public double BankFullDynamicPressurePa { get; set; }

    public static Flight14DescentDefinition LoadFromJson(string path)
    {
        var result = JsonSerializer.Deserialize<Flight14DescentDefinition>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Empty Flight 14 descent estimate.");
        result.Validate();
        return result;
    }

    public void Validate()
    {
        double[] values = [MaximumMissionSeconds, DiagnosticEndAltitudeM, MaximumEndSpeedMps,
            HypersonicControlThresholdMps, TerminalAngleOfAttackSpeedMps, EntryAngleOfAttackDegrees,
            TerminalAngleOfAttackDegrees, ReferenceSlewRateRadPerSecond, BankOnsetDynamicPressurePa,
            BankFullDynamicPressurePa];
        if (string.IsNullOrWhiteSpace(Id) || Status != "engineering-estimate"
            || string.IsNullOrWhiteSpace(Assumptions) || values.Any(v => !double.IsFinite(v) || v <= 0)
            || EntryAngleOfAttackDegrees > TerminalAngleOfAttackDegrees || TerminalAngleOfAttackDegrees > 90
            || MaximumEndSpeedMps >= TerminalAngleOfAttackSpeedMps
            || TerminalAngleOfAttackSpeedMps >= HypersonicControlThresholdMps
            || BankOnsetDynamicPressurePa >= BankFullDynamicPressurePa)
            throw new InvalidDataException("Invalid Flight 14 descent engineering envelope.");
    }
}
