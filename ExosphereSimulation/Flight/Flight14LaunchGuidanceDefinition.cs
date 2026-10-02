namespace Exosphere.Simulation.Flight;

using System.Text.Json;

/// <summary>Explicit engineering assumptions, separate from observed mission facts.</summary>
public sealed class Flight14LaunchGuidanceDefinition
{
    public string Id { get; set; } = "";
    public string Status { get; set; } = "";
    public string BaselineVehicleFile { get; set; } = "";
    public string SeaLevelEngineModelId { get; set; } = "";
    public string Assumptions { get; set; } = "";
    public double MaximumIgnitionSeconds { get; set; }
    public double MaximumMissionSeconds { get; set; }
    public double IgnitionRampSeconds { get; set; }
    public double NominalMecoSeconds { get; set; }
    public double MinimumStagingAltitudeM { get; set; }
    public double HotStageSeconds { get; set; }
    public double NominalShipCutoffSeconds { get; set; }
    public double TargetApoapsisAltitudeM { get; set; }
    public double TargetSuborbitalPeriapsisAltitudeM { get; set; }
    public double TargetInsertionPeriapsisAltitudeM { get; set; }
    public double NominalCoastSeconds { get; set; }
    public double MinimumCoastSeconds { get; set; }
    public double MinimumInsertionAltitudeM { get; set; }
    public double InsertionLeadSeconds { get; set; }
    public double MinimumShipReserveKg { get; set; }
    public double MaximumAscentSeconds { get; set; }
    public double MaximumInsertionSeconds { get; set; }
    public double BoosterFaultSeconds { get; set; }
    public double VacuumFaultSeconds { get; set; }
    public double CutoffApoapsisToleranceM { get; set; }
    public double TerminalRadialGainPerSecond { get; set; }
    public double TerminalEnergyFraction { get; set; }
    public double TerminalBlendWidth { get; set; }
    public double PoweredPointingGain { get; set; }
    public double PoweredPointingDamping { get; set; }
    public double MaximumInitialTurnReferenceRateRadPerSecond { get; set; }
    public double TerminalShipThrottle { get; set; }
    public double ShipThrottle { get; set; }
    public List<ControlKnot> BoosterProgram { get; set; } = new();

    public sealed record ControlKnot(double ElapsedSeconds, double ElevationDeg, double Throttle);

    public static Flight14LaunchGuidanceDefinition LoadFromJson(string path)
    {
        var result = JsonSerializer.Deserialize<Flight14LaunchGuidanceDefinition>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Empty Flight 14 guidance definition.");
        result.Validate();
        return result;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Status != "engineering-estimate"
            || string.IsNullOrWhiteSpace(BaselineVehicleFile) || string.IsNullOrWhiteSpace(SeaLevelEngineModelId)
            || string.IsNullOrWhiteSpace(Assumptions))
            throw new InvalidDataException("Flight 14 guidance assumptions must be explicit.");
        double[] positive = [MaximumIgnitionSeconds, MaximumMissionSeconds, IgnitionRampSeconds, NominalMecoSeconds, MinimumStagingAltitudeM,
            HotStageSeconds, NominalShipCutoffSeconds, TargetApoapsisAltitudeM,
            TargetSuborbitalPeriapsisAltitudeM, TargetInsertionPeriapsisAltitudeM,
            NominalCoastSeconds, MinimumCoastSeconds, MinimumInsertionAltitudeM,
            InsertionLeadSeconds, MinimumShipReserveKg, MaximumAscentSeconds,
            MaximumInsertionSeconds, BoosterFaultSeconds, VacuumFaultSeconds,
            CutoffApoapsisToleranceM, TerminalRadialGainPerSecond, PoweredPointingGain, PoweredPointingDamping, MaximumInitialTurnReferenceRateRadPerSecond];
        if (positive.Any(v => !double.IsFinite(v) || v <= 0)
            || NominalShipCutoffSeconds <= NominalMecoSeconds + HotStageSeconds
            || MaximumIgnitionSeconds <= IgnitionRampSeconds
            || MaximumMissionSeconds <= MaximumAscentSeconds+NominalCoastSeconds
            || MaximumAscentSeconds <= NominalShipCutoffSeconds
            || BoosterFaultSeconds >= NominalMecoSeconds
            || VacuumFaultSeconds <= NominalMecoSeconds + HotStageSeconds
            || TargetSuborbitalPeriapsisAltitudeM >= TargetInsertionPeriapsisAltitudeM
            || TargetInsertionPeriapsisAltitudeM >= TargetApoapsisAltitudeM
            || MinimumInsertionAltitudeM <= TargetInsertionPeriapsisAltitudeM
            || MinimumInsertionAltitudeM >= TargetApoapsisAltitudeM
            || MinimumCoastSeconds >= NominalCoastSeconds
            || InsertionLeadSeconds >= MinimumCoastSeconds
            || !double.IsFinite(TerminalEnergyFraction) || TerminalEnergyFraction <= 0 || TerminalEnergyFraction >= 1
            || !double.IsFinite(TerminalBlendWidth) || TerminalBlendWidth <= 0
            || TerminalEnergyFraction+TerminalBlendWidth >= 1
            || !double.IsFinite(TerminalShipThrottle) || TerminalShipThrottle < 0.4 || TerminalShipThrottle > ShipThrottle
            || !double.IsFinite(ShipThrottle) || ShipThrottle < 0.4 || ShipThrottle > 1)
            throw new InvalidDataException("Invalid Flight 14 guidance envelope.");
        if (BoosterProgram.Count < 2 || BoosterProgram[0].ElapsedSeconds != 0)
            throw new InvalidDataException("Booster control program must start at liftoff.");
        double previous = -1;
        foreach (var knot in BoosterProgram)
        {
            if (!double.IsFinite(knot.ElapsedSeconds) || knot.ElapsedSeconds <= previous
                || !double.IsFinite(knot.ElevationDeg) || knot.ElevationDeg < 0 || knot.ElevationDeg > 90
                || !double.IsFinite(knot.Throttle) || knot.Throttle < 0.4 || knot.Throttle > 1)
                throw new InvalidDataException("Invalid booster control program.");
            previous = knot.ElapsedSeconds;
        }
    }

    public (double ElevationDeg, double Throttle) BoosterCommand(double elapsed)
    {
        for (int i = 1; i < BoosterProgram.Count; i++)
        {
            var left = BoosterProgram[i - 1];
            var right = BoosterProgram[i];
            if (elapsed > right.ElapsedSeconds) continue;
            double fraction = System.Math.Clamp((elapsed-left.ElapsedSeconds)/(right.ElapsedSeconds-left.ElapsedSeconds), 0, 1);
            return (left.ElevationDeg + fraction*(right.ElevationDeg-left.ElevationDeg),
                left.Throttle + fraction*(right.Throttle-left.Throttle));
        }
        var last = BoosterProgram[^1];
        return (last.ElevationDeg, last.Throttle);
    }
}
