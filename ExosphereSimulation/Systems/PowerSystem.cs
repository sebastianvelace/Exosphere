namespace Exosphere.Simulation.Systems;

using Exosphere.Simulation.Math;

public class PowerSystem
{
    public double BatteryKwh    { get; private set; } = 50.0;  // kWh
    public double MaxBatteryKwh => 50.0;
    public double SolarOutputKw { get; private set; } = 0.0;   // kW
    public double BaseLoadKw    => 5.0;   // consumo base de sistemas

    public bool LowPowerAlert { get; private set; }
    public bool NoPowerAlert  { get; private set; }

    // Panel area and efficiency
    private const double SolarPanelArea       = 40.0;   // m²
    private const double SolarPanelEfficiency = 0.28;   // 28%
    private const double SolarConstant        = 1361.0; // W/m² a 1 AU

    public void Tick(double dt, Vector3d vesselPosition, Vector3d sunPosition, bool inEclipse)
    {
        // Solar power generation
        if (!inEclipse)
        {
            // Distance falloff from Sun (approximate: use 1 AU = 1.496e11 m as reference)
            double distToSun = (vesselPosition - sunPosition).Magnitude;
            double auDist    = System.Math.Max(distToSun / 1.496e11, 0.1);
            double solarFlux = SolarConstant / (auDist * auDist);
            SolarOutputKw    = solarFlux * SolarPanelArea * SolarPanelEfficiency / 1000.0;
        }
        else
        {
            SolarOutputKw = 0.0; // eclipse
        }

        double netPowerKw = SolarOutputKw - BaseLoadKw;
        double deltaKwh   = netPowerKw * (dt / 3600.0);
        BatteryKwh = System.Math.Clamp(BatteryKwh + deltaKwh, 0.0, MaxBatteryKwh);

        LowPowerAlert = BatteryKwh < MaxBatteryKwh * 0.2;
        NoPowerAlert  = BatteryKwh <= 0.0;
    }

    public double BatteryFraction => BatteryKwh / MaxBatteryKwh;
}
