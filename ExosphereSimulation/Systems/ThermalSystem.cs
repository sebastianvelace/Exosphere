namespace Exosphere.Simulation.Systems;

public class ThermalSystem
{
    public double TemperatureK  { get; private set; } = 293.0;  // 20°C inicial
    public double MinSafeTemp   => 253.0;   // -20°C
    public double MaxSafeTemp   => 353.0;   // 80°C

    public bool HotAlert  { get; private set; }
    public bool ColdAlert { get; private set; }

    private const double SpaceBgTemp   = 3.0;     // K fondo cósmico
    private const double SolarHeatFlux = 1361.0;  // W/m² a 1 AU
    private const double VehicleArea   = 200.0;   // m² área expuesta
    private const double Emissivity    = 0.85;
    private const double SolarAbsorb   = 0.25;
    private const double ThermalMass   = 50000.0; // J/K
    private const double Boltzmann     = 5.67e-8;

    public void Tick(double dt, bool inEclipse, bool inAtmosphere, double atmosphericTemp)
    {
        double solarIn = inEclipse ? 0.0 : SolarHeatFlux * SolarAbsorb * VehicleArea * 0.5;

        // Radiation to space
        double radOut = Emissivity * Boltzmann * VehicleArea * System.Math.Pow(TemperatureK, 4);

        // In atmosphere: convective exchange
        double convective = inAtmosphere
            ? (atmosphericTemp - TemperatureK) * 200.0  // simple convection
            : 0.0;

        double netHeat = solarIn - radOut + convective;
        TemperatureK = System.Math.Max(SpaceBgTemp, TemperatureK + netHeat * dt / ThermalMass);

        HotAlert  = TemperatureK > MaxSafeTemp;
        ColdAlert = TemperatureK < MinSafeTemp;
    }

    public double TempCelsius    => TemperatureK - 273.15;
    public double ThermalFraction => System.Math.Clamp(
        (TemperatureK - MinSafeTemp) / (MaxSafeTemp - MinSafeTemp), 0.0, 1.0);
}
