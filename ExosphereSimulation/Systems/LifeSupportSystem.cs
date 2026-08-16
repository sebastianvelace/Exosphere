namespace Exosphere.Simulation.Systems;

public class LifeSupportSystem
{
    public double OxygenKg   { get; private set; } = 200.0;
    public double CO2Kg      { get; private set; } = 0.0;
    public double WaterKg    { get; private set; } = 500.0;
    public double FoodKg     { get; private set; } = 300.0;

    public double MaxOxygen  => 200.0;
    public double MaxWater   => 500.0;
    public double MaxFood    => 300.0;
    public double MaxCO2     => 50.0;

    public bool OxygenAlert  { get; private set; }
    public bool CO2Alert     { get; private set; }
    public bool CrewAlive    { get; private set; } = true;

    private const double OxygenPerCrewPerSec = 0.000833;
    private const double CO2PerCrewPerSec    = 0.000694;
    private const double WaterPerCrewPerSec  = 0.000278;
    private const double FoodPerCrewPerSec   = 0.0000833;
    private const double CO2ScrubPerSec      = 0.000600;

    private const double EcLoadPerCrewActiveKw = 0.45;
    private const double EcLoadStandbyKw       = 0.15;

    public LifeSupportState CaptureState() => new()
    {
        OxygenKg = OxygenKg,
        CO2Kg = CO2Kg,
        WaterKg = WaterKg,
        FoodKg = FoodKg,
        CrewAlive = CrewAlive,
    };

    public void RestoreState(LifeSupportState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Validate();
        OxygenKg = state.OxygenKg;
        CO2Kg = state.CO2Kg;
        WaterKg = state.WaterKg;
        FoodKg = state.FoodKg;
        CrewAlive = state.CrewAlive;
        OxygenAlert = OxygenKg < MaxOxygen * 0.2;
        CO2Alert = CO2Kg > MaxCO2 * 0.8;
    }

    public void Reset() => RestoreState(new LifeSupportState());

    public double GetEcLoadKw(int crewCount, SystemsMissionPhase phase)
    {
        if (crewCount <= 0 || !CrewAlive) return 0.0;
        if (phase == SystemsMissionPhase.Idle) return EcLoadStandbyKw;

        double perCrew = EcLoadPerCrewActiveKw * phase switch
        {
            SystemsMissionPhase.HighLoad => 1.2,
            SystemsMissionPhase.Entry => 1.3,
            SystemsMissionPhase.PeakHeating => 1.5,
            _ => 1.0, // Active / cruise
        };
        return perCrew * crewCount;
    }

    public void Tick(double dt, int crewCount, SystemsMissionPhase phase = SystemsMissionPhase.Active)
    {
        if (!CrewAlive || crewCount <= 0) return;
        if (phase == SystemsMissionPhase.Idle) return;

        double o2Used   = OxygenPerCrewPerSec * crewCount * dt;
        double co2Gen   = CO2PerCrewPerSec    * crewCount * dt;
        double h2oUsed  = WaterPerCrewPerSec  * crewCount * dt;
        double foodUsed = FoodPerCrewPerSec   * crewCount * dt;

        OxygenKg = System.Math.Max(0, OxygenKg - o2Used);
        CO2Kg    = System.Math.Clamp(CO2Kg + co2Gen - CO2ScrubPerSec * dt, 0, MaxCO2 * 2);
        WaterKg  = System.Math.Max(0, WaterKg - h2oUsed);
        FoodKg   = System.Math.Max(0, FoodKg  - foodUsed);

        OxygenAlert = OxygenKg < MaxOxygen * 0.2;
        CO2Alert    = CO2Kg    > MaxCO2    * 0.8;

        if (OxygenKg <= 0 || CO2Kg >= MaxCO2)
            CrewAlive = false;
    }

    public double OxygenFraction => OxygenKg / MaxOxygen;
    public double CO2Fraction    => CO2Kg    / MaxCO2;
    public double WaterFraction  => WaterKg  / MaxWater;
    public double FoodFraction   => FoodKg   / MaxFood;

    /// <summary>
    /// Returns the first time at which an active crew resource reaches an existing alert
    /// threshold. The result is a projection only: it does not mutate the system and it
    /// deliberately uses the same rates as <see cref="Tick"/>. <c>null</c> means that the
    /// selected phase has no consumption or that no finite alert is reachable.
    /// </summary>
    public double? GetNextAlertDeadlineSeconds(
        int crewCount,
        SystemsMissionPhase phase = SystemsMissionPhase.Active)
    {
        if (!CrewAlive || crewCount <= 0 || phase == SystemsMissionPhase.Idle)
            return null;

        if (OxygenKg <= 0.0 || CO2Kg >= MaxCO2)
            return 0.0;

        double oxygenRate = OxygenPerCrewPerSec * crewCount;
        double co2Rate = CO2PerCrewPerSec * crewCount - CO2ScrubPerSec;
        double? oxygenSeconds = OxygenKg <= MaxOxygen * 0.2
            ? 0.0
            : oxygenRate > 0.0
                ? (OxygenKg - MaxOxygen * 0.2) / oxygenRate
                : null;
        double? co2Seconds = CO2Kg >= MaxCO2 * 0.8
            ? 0.0
            : co2Rate > 0.0
                ? (MaxCO2 * 0.8 - CO2Kg) / co2Rate
                : null;

        return MinFiniteNonNegative(oxygenSeconds, co2Seconds);
    }

    public double EstimatedO2HoursRemaining(int crewCount) =>
        crewCount > 0 ? OxygenKg / (OxygenPerCrewPerSec * crewCount * 3600.0) : double.PositiveInfinity;

    private static double? MinFiniteNonNegative(double? first, double? second)
    {
        if (first is double a && second is double b)
            return System.Math.Min(System.Math.Max(0.0, a), System.Math.Max(0.0, b));
        if (first is double onlyFirst)
            return System.Math.Max(0.0, onlyFirst);
        if (second is double onlySecond)
            return System.Math.Max(0.0, onlySecond);
        return null;
    }
}
