namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Parts;

public static class ThermalModel
{
    // Flujo de calor convectivo (W/m²) — modelo simplificado Detra-Kemp-Riddell:
    // q ≈ k · sqrt(ρ) · v³
    public static double ComputeHeatFlux(double density, double velocity)
    {
        if (density <= 0.0 || velocity <= 0.0) return 0.0;
        const double k = 1.83e-4;
        return k * System.Math.Sqrt(density) * System.Math.Pow(velocity, 3);
    }

    // Actualiza la temperatura de una pieza dado el flujo de calor y dt (s)
    public static double UpdateTemperature(
        double currentTemp,
        double heatFlux,
        double dt,
        double partMass = 100.0)
    {
        const double specificHeat    = 800.0;   // J/(kg·K)
        const double emissivity      = 0.9;
        const double stefanBoltzmann = 5.67e-8; // W/(m²·K⁴)
        const double surfaceArea     = 1.0;     // m² (approx)

        double thermalMass = System.Math.Max(1.0, partMass) * specificHeat;
        double radiation   = emissivity * stefanBoltzmann
                             * System.Math.Pow(currentTemp, 4) * surfaceArea;
        double netPower    = heatFlux * surfaceArea - radiation;
        double dTemp       = (netPower / thermalMass) * dt;

        return System.Math.Max(3.0, currentTemp + dTemp);
    }

    // Aplica calor a una pieza. Retorna true si la pieza fue destruida (supera HeatTolerance).
    public static bool ApplyHeat(Part part, double heatFlux, double dt)
    {
        part.Temperature = UpdateTemperature(part.Temperature, heatFlux, dt, part.CurrentMass);
        return part.Temperature > part.Definition.HeatTolerance;
    }
}
