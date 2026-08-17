namespace Exosphere.Simulation.Systems;

using Exosphere.Simulation.Math;

/// <summary>
/// Immutable environment sample consumed by one vessel's systems runtime.
///
/// The game layer owns the sampling of the vessel/body state and the committed simulation
/// epoch. Keeping that boundary explicit prevents a per-vessel runtime from reaching into
/// the active-vessel singleton or accidentally consuming render time.
/// </summary>
public readonly record struct VesselSystemsTickInput(
    double DeltaSeconds,
    double SimulationTime,
    int CrewCount,
    SystemsMissionPhase Phase,
    Vector3d VesselPosition,
    Vector3d EarthPosition,
    Vector3d SunPosition,
    IReadOnlyList<CelestialBody> Bodies,
    double SolarVisibility,
    bool InAtmosphere,
    double AtmosphericTemperatureK,
    double AeroHeatFluxWm2,
    PlasmaBlackoutInput EntryCondition)
{
    /// <summary>True when all sampled values are finite and physically usable.</summary>
    public bool IsValid => DeltaSeconds > 0.0
        && double.IsFinite(DeltaSeconds)
        && double.IsFinite(SimulationTime)
        && SimulationTime >= 0.0
        && CrewCount >= 0
        && Enum.IsDefined(Phase)
        && IsFinite(VesselPosition)
        && IsFinite(EarthPosition)
        && IsFinite(SunPosition)
        && Bodies != null
        && double.IsFinite(SolarVisibility)
        && SolarVisibility >= 0.0
        && SolarVisibility <= 1.0
        && double.IsFinite(AtmosphericTemperatureK)
        && AtmosphericTemperatureK >= 0.0
        && double.IsFinite(AeroHeatFluxWm2)
        && AeroHeatFluxWm2 >= 0.0
        && double.IsFinite(EntryCondition.HeatFluxWm2)
        && EntryCondition.HeatFluxWm2 >= 0.0
        && double.IsFinite(EntryCondition.DensityKgM3)
        && EntryCondition.DensityKgM3 >= 0.0
        && double.IsFinite(EntryCondition.AirspeedMs)
        && EntryCondition.AirspeedMs >= 0.0;

    /// <summary>Throws before any subsystem is advanced when the sample is malformed.</summary>
    public void Validate()
    {
        if (!IsValid)
            throw new ArgumentOutOfRangeException(nameof(VesselSystemsTickInput));
    }

    private static bool IsFinite(Vector3d value) =>
        double.IsFinite(value.X)
        && double.IsFinite(value.Y)
        && double.IsFinite(value.Z);
}

/// <summary>
/// Pure, per-vessel owner for life support, power, thermal and communications state.
///
/// This is deliberately not a Godot node and has no notion of an active vessel. It can be
/// materialized by a future registry for a distant vessel, restored at an exact epoch, and
/// tested independently from rendering and input. The current game layer may continue to
/// use its existing controller until the ownership/scheduler gate is promoted.
/// </summary>
public sealed class VesselSystemsRuntime
{
    private const double EpochToleranceSeconds = 1e-7;

    public VesselSystemsRuntime(string vesselId, double simulationTime = 0.0)
    {
        if (string.IsNullOrWhiteSpace(vesselId))
            throw new ArgumentException("Vessel id is required.", nameof(vesselId));
        if (!double.IsFinite(simulationTime) || simulationTime < 0.0)
            throw new ArgumentOutOfRangeException(nameof(simulationTime));

        VesselId = vesselId;
        SimulationTime = simulationTime;
    }

    public string VesselId { get; }
    public double SimulationTime { get; private set; }
    public SystemsMissionPhase CurrentPhase { get; private set; } = SystemsMissionPhase.Idle;

    public LifeSupportSystem LifeSupport { get; } = new();
    public PowerSystem Power { get; } = new();
    public ThermalSystem Thermal { get; } = new();
    public CommsSystem Comms { get; } = new();

    /// <summary>True when any persistent system condition requires prompt service.</summary>
    public bool HasSystemsAlert =>
        LifeSupport.OxygenAlert
        || LifeSupport.CO2Alert
        || Power.LowPowerAlert
        || Power.NoPowerAlert
        || Thermal.HotAlert
        || Thermal.ColdAlert
        || (!Comms.HasSignal && !Comms.PlasmaBlackout);

    /// <summary>
    /// Returns the earliest alert deadline from all four subsystems. This is a projection
    /// from the last committed sample; it never advances or mutates the runtime.
    /// </summary>
    public double? GetNextAlertDeadlineSeconds(int crewCount)
    {
        if (crewCount < 0)
            throw new ArgumentOutOfRangeException(nameof(crewCount));

        double? deadline = LifeSupport.GetNextAlertDeadlineSeconds(crewCount, CurrentPhase);
        deadline = MinDeadline(deadline, Power.GetNextAlertDeadlineSeconds());
        deadline = MinDeadline(deadline, Thermal.GetNextAlertDeadlineSeconds());
        return deadline;
    }

    /// <summary>
    /// Advances every subsystem using only a committed simulation interval. The target
    /// epoch must equal the previous runtime epoch plus the processed delta; this catches
    /// accidental wall-clock integration and scheduler-debt double consumption.
    /// </summary>
    public void Tick(in VesselSystemsTickInput input)
    {
        input.Validate();
        double expectedEpoch = SimulationTime + input.DeltaSeconds;
        if (!double.IsFinite(expectedEpoch)
            || System.Math.Abs(input.SimulationTime - expectedEpoch) > EpochToleranceSeconds)
        {
            throw new InvalidOperationException(
                $"Systems runtime '{VesselId}' received a non-contiguous epoch: "
                + $"current={SimulationTime:R}, delta={input.DeltaSeconds:R}, "
                + $"target={input.SimulationTime:R}.");
        }

        CurrentPhase = input.Phase;
        LifeSupport.Tick(input.DeltaSeconds, input.CrewCount, CurrentPhase);

        double loadKw = LifeSupport.GetEcLoadKw(input.CrewCount, CurrentPhase)
            + SystemsPhaseLoads.AvionicsExtraKw(CurrentPhase);
        Power.Tick(
            input.DeltaSeconds,
            input.VesselPosition,
            input.SunPosition,
            input.SolarVisibility,
            loadKw);
        Thermal.Tick(
            input.DeltaSeconds,
            input.SolarVisibility,
            input.InAtmosphere,
            input.AtmosphericTemperatureK,
            input.AeroHeatFluxWm2,
            CurrentPhase);
        Comms.Tick(
            input.DeltaSeconds,
            input.VesselPosition,
            input.EarthPosition,
            input.Bodies,
            input.EntryCondition);

        SimulationTime = input.SimulationTime;
    }

    /// <summary>Captures this runtime without accepting a caller-supplied epoch.</summary>
    public VesselSystemsState CaptureState() => new()
    {
        VesselId = VesselId,
        SimulationTime = SimulationTime,
        LifeSupport = LifeSupport.CaptureState(),
        Power = Power.CaptureState(),
        Thermal = Thermal.CaptureState(),
        Comms = Comms.CaptureState(),
    };

    /// <summary>Restores an exact snapshot belonging to this runtime's stable vessel id.</summary>
    public void RestoreState(VesselSystemsState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Validate();
        if (!string.Equals(state.VesselId, VesselId, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Systems state targets '{state.VesselId}', not '{VesselId}'.");

        LifeSupport.RestoreState(state.LifeSupport);
        Power.RestoreState(state.Power);
        Thermal.RestoreState(state.Thermal);
        Comms.RestoreState(state.Comms);
        SimulationTime = state.SimulationTime;
        CurrentPhase = state.Thermal.LastPhase;
    }

    /// <summary>Resets subsystem values while retaining the stable vessel identity.</summary>
    public void Reset(double simulationTime)
    {
        if (!double.IsFinite(simulationTime) || simulationTime < 0.0)
            throw new ArgumentOutOfRangeException(nameof(simulationTime));

        LifeSupport.Reset();
        Power.Reset();
        Thermal.Reset();
        Comms.Reset();
        SimulationTime = simulationTime;
        CurrentPhase = SystemsMissionPhase.Idle;
    }

    private static double? MinDeadline(double? first, double? second)
    {
        if (first is double a && second is double b)
            return System.Math.Min(a, b);
        return first ?? second;
    }
}
