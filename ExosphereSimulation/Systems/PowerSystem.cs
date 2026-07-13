namespace Exosphere.Simulation.Systems;

using Exosphere.Simulation.Math;

public class PowerSystem
{
    public double BatteryKwh    { get; private set; } = 50.0;  // kWh
    public double MaxBatteryKwh => 50.0;
    public double SolarOutputKw { get; private set; } = 0.0;   // kW
    public double BaseLoadKw    => 5.0;   // avionics, computers, SAS standby
    public double ExtraLoadKw   { get; private set; }

    public bool LowPowerAlert { get; private set; }
    public bool NoPowerAlert  { get; private set; }

    private const double SolarPanelArea       = 40.0;
    private const double SolarPanelEfficiency = 0.28;
    private const double SolarConstant        = 1361.0;

    public void Tick(double dt, Vector3d vesselPosition, Vector3d sunPosition, bool inEclipse,
                     double extraLoadKw = 0.0)
        => Tick(dt, vesselPosition, sunPosition, inEclipse ? 0.0 : 1.0, extraLoadKw);

    public void Tick(double dt, Vector3d vesselPosition, Vector3d sunPosition,
                     double solarVisibility, double extraLoadKw = 0.0)
    {
        ExtraLoadKw = System.Math.Max(0.0, extraLoadKw);
        solarVisibility = System.Math.Clamp(solarVisibility, 0.0, 1.0);

        if (solarVisibility > 0.0)
        {
            double distToSun = (vesselPosition - sunPosition).Magnitude;
            double auDist    = System.Math.Max(distToSun / 1.496e11, 0.1);
            double solarFlux = SolarConstant / (auDist * auDist);
            SolarOutputKw    = solarFlux * SolarPanelArea * SolarPanelEfficiency
                * solarVisibility / 1000.0;
        }
        else
        {
            SolarOutputKw = 0.0;
        }

        double netPowerKw = SolarOutputKw - BaseLoadKw - ExtraLoadKw;
        double deltaKwh   = netPowerKw * (dt / 3600.0);
        BatteryKwh = System.Math.Clamp(BatteryKwh + deltaKwh, 0.0, MaxBatteryKwh);

        LowPowerAlert = BatteryKwh < MaxBatteryKwh * 0.2;
        NoPowerAlert  = BatteryKwh <= 0.0;
    }

    public double BatteryFraction => BatteryKwh / MaxBatteryKwh;
}
