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

    public double GetEcLoadKw(int crewCount, SystemsMissionPhase phase)
    {
        if (crewCount <= 0 || !CrewAlive) return 0.0;
        return phase == SystemsMissionPhase.Active
            ? EcLoadPerCrewActiveKw * crewCount
            : EcLoadStandbyKw;
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

    public double EstimatedO2HoursRemaining(int crewCount) =>
        crewCount > 0 ? OxygenKg / (OxygenPerCrewPerSec * crewCount * 3600.0) : double.PositiveInfinity;
}
