namespace Exosphere.Simulation.Systems;

using System.IO;

/// <summary>
/// Versioned, per-vessel systems state used by SaveGameV2. Derived alert values are
/// recomputed on restore; transient command queues are deliberately not part of this DTO.
/// </summary>
public sealed class VesselSystemsState
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string VesselId { get; set; } = "";
    public double SimulationTime { get; set; }
    /// <summary>
    /// Last committed systems phase. Older saves omitted this field and deserialize to
    /// Active, which is the conservative choice for deadline materialization.
    /// </summary>
    public SystemsMissionPhase Phase { get; set; } = SystemsMissionPhase.Active;
    public LifeSupportState LifeSupport { get; set; } = new();
    public PowerState Power { get; set; } = new();
    public ThermalState Thermal { get; set; } = new();
    public CommsState Comms { get; set; } = new();

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported systems schema {SchemaVersion}.");
        RequireId(VesselId, "systems vessel");
        RequireFinite(SimulationTime, nameof(SimulationTime));
        if (!Enum.IsDefined(Phase))
            throw new InvalidDataException("Invalid systems mission phase.");
        LifeSupport?.Validate();
        Power?.Validate();
        Thermal?.Validate();
        Comms?.Validate();
        if (LifeSupport == null || Power == null || Thermal == null || Comms == null)
            throw new InvalidDataException("Systems snapshot contains a null subsystem.");
    }

    private static void RequireFinite(double value, string field)
    {
        if (!double.IsFinite(value))
            throw new InvalidDataException($"Non-finite systems value in '{field}'.");
    }

    private static void RequireId(string value, string kind)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"Missing stable {kind} id.");
    }
}

public sealed class LifeSupportState
{
    public double OxygenKg { get; set; } = 200.0;
    public double CO2Kg { get; set; }
    public double WaterKg { get; set; } = 500.0;
    public double FoodKg { get; set; } = 300.0;
    public bool CrewAlive { get; set; } = true;

    public void Validate()
    {
        RequireRange(OxygenKg, 0.0, 200.0, nameof(OxygenKg));
        RequireRange(CO2Kg, 0.0, 100.0, nameof(CO2Kg));
        RequireRange(WaterKg, 0.0, 500.0, nameof(WaterKg));
        RequireRange(FoodKg, 0.0, 300.0, nameof(FoodKg));
        if (CrewAlive && (OxygenKg <= 0.0 || CO2Kg >= 50.0))
            throw new InvalidDataException("CrewAlive conflicts with depleted life support.");
    }

    private static void RequireRange(double value, double min, double max, string field)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new InvalidDataException($"Invalid life-support value '{field}'.");
    }
}

public sealed class PowerState
{
    public double BatteryKwh { get; set; } = 50.0;
    public double SolarOutputKw { get; set; }
    public double ExtraLoadKw { get; set; }

    public void Validate()
    {
        RequireRange(BatteryKwh, 0.0, 50.0, nameof(BatteryKwh));
        RequireRange(SolarOutputKw, 0.0, double.MaxValue, nameof(SolarOutputKw));
        RequireRange(ExtraLoadKw, 0.0, double.MaxValue, nameof(ExtraLoadKw));
    }

    private static void RequireRange(double value, double min, double max, string field)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new InvalidDataException($"Invalid power value '{field}'.");
    }
}

public sealed class ThermalState
{
    public double TemperatureK { get; set; } = 293.0;
    public bool HasLastSample { get; set; }
    public double LastSolarVisibility { get; set; }
    public bool LastInAtmosphere { get; set; }
    public double LastAtmosphericTemp { get; set; }
    public double LastAeroHeatFluxWm2 { get; set; }
    public SystemsMissionPhase LastPhase { get; set; } = SystemsMissionPhase.Active;

    public void Validate()
    {
        RequireRange(TemperatureK, 3.0, double.MaxValue, nameof(TemperatureK));
        if (!HasLastSample) return;
        RequireFinite(LastSolarVisibility, nameof(LastSolarVisibility));
        RequireFinite(LastAtmosphericTemp, nameof(LastAtmosphericTemp));
        RequireRange(LastAeroHeatFluxWm2, 0.0, double.MaxValue, nameof(LastAeroHeatFluxWm2));
        if (!Enum.IsDefined(LastPhase))
            throw new InvalidDataException("Invalid thermal mission phase.");
    }

    private static void RequireFinite(double value, string field)
    {
        if (!double.IsFinite(value))
            throw new InvalidDataException($"Non-finite thermal value in '{field}'.");
    }

    private static void RequireRange(double value, double min, double max, string field)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new InvalidDataException($"Invalid thermal value '{field}'.");
    }
}

public sealed class CommsState
{
    public bool HasSignal { get; set; } = true;
    public double SignalStrength { get; set; } = 1.0;
    public double SignalDelaySeconds { get; set; }
    public bool LossOfSignalAlert { get; set; }
    public PlasmaBlackoutState PlasmaBlackout { get; set; } = new();

    public void Validate()
    {
        RequireRange(SignalStrength, 0.0, 1.0, nameof(SignalStrength));
        RequireRange(SignalDelaySeconds, 0.0, double.MaxValue, nameof(SignalDelaySeconds));
        PlasmaBlackout?.Validate();
        if (PlasmaBlackout == null)
            throw new InvalidDataException("Communications snapshot has null blackout state.");
    }

    private static void RequireRange(double value, double min, double max, string field)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new InvalidDataException($"Invalid communications value '{field}'.");
    }
}

public sealed class PlasmaBlackoutState
{
    public bool IsBlackedOut { get; set; }
    public double ElapsedSeconds { get; set; }
    public double LongestBlackoutSeconds { get; set; }
    public double EngageDwellSeconds { get; set; }

    public void Validate()
    {
        RequireRange(ElapsedSeconds, 0.0, double.MaxValue, nameof(ElapsedSeconds));
        RequireRange(LongestBlackoutSeconds, 0.0, double.MaxValue,
            nameof(LongestBlackoutSeconds));
        RequireRange(EngageDwellSeconds, 0.0, double.MaxValue, nameof(EngageDwellSeconds));
        if (LongestBlackoutSeconds < ElapsedSeconds)
            throw new InvalidDataException("Blackout maximum precedes current duration.");
    }

    private static void RequireRange(double value, double min, double max, string field)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new InvalidDataException($"Invalid blackout value '{field}'.");
    }
}
