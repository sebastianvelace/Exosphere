namespace Exosphere.Simulation.Systems;

/// Life support: O2, CO2, water, food for the crew.
public class LifeSupportSystem
{
    // Reservas (kg)
    public double OxygenKg   { get; private set; } = 200.0;
    public double CO2Kg      { get; private set; } = 0.0;
    public double WaterKg    { get; private set; } = 500.0;
    public double FoodKg     { get; private set; } = 300.0;

    // Capacidades máximas
    public double MaxOxygen  => 200.0;
    public double MaxWater   => 500.0;
    public double MaxFood    => 300.0;
    public double MaxCO2     => 50.0;   // límite de toxicidad

    public bool OxygenAlert  { get; private set; }
    public bool CO2Alert     { get; private set; }
    public bool CrewAlive    { get; private set; } = true;

    // Consumo por tripulante por segundo
    private const double OxygenPerCrewPerSec = 0.000833;  // ~3 kg/hora/persona
    private const double CO2PerCrewPerSec    = 0.000694;  // ~2.5 kg/hora/persona
    private const double WaterPerCrewPerSec  = 0.000278;  // ~1 kg/hora/persona
    private const double FoodPerCrewPerSec   = 0.0000833; // ~300g/hora/persona
    private const double CO2ScrubPerSec      = 0.000600;  // depuradora base

    public void Tick(double dt, int crewCount)
    {
        if (!CrewAlive || crewCount <= 0) return;

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

    // Duración estimada en horas a consumo actual
    public double EstimatedO2HoursRemaining(int crewCount) =>
        crewCount > 0 ? OxygenKg / (OxygenPerCrewPerSec * crewCount * 3600.0) : double.PositiveInfinity;
}
