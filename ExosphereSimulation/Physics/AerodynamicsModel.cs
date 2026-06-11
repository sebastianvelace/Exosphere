namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

public static class AerodynamicsModel
{
    // Fuerza de arrastre (world space) en Newton
    public static Vector3d ComputeDrag(
        double density,
        Vector3d surfaceVelocity,
        double dragCoefficient,
        double referenceArea)
    {
        double speed = surfaceVelocity.Magnitude;
        if (speed < 1e-3) return Vector3d.Zero;
        double magnitude = 0.5 * density * speed * speed * dragCoefficient * referenceArea;
        return surfaceVelocity.Normalized * (-magnitude);
    }

    // Presión dinámica (Pa)
    public static double ComputeDynamicPressure(double density, double speed) =>
        0.5 * density * speed * speed;

    // Número de Mach
    public static double ComputeMach(double speed, double temperature)
    {
        const double gamma = 1.4;
        const double R     = 287.0;  // J/(kg·K) gas específico del aire
        double sos = System.Math.Sqrt(gamma * R * System.Math.Max(1.0, temperature));
        return speed / sos;
    }

    // Multiplicador de Cd por Mach (pico transónico)
    public static double GetMachDragMultiplier(double mach) =>
        mach < 0.8 ? 1.0
        : mach < 1.0 ? 1.0 + (mach - 0.8) * 5.0
        : mach < 1.2 ? 2.0
        : mach < 5.0 ? 2.0 - (mach - 1.2) * 0.25
        : 1.0;

    // Área de referencia estimada del vessel (m²) basada en el grafo de piezas
    public static double EstimateReferenceArea(PartGraph graph)
    {
        double maxRadius = 0.5;
        maxRadius = System.Math.Max(maxRadius, System.Math.Sqrt(graph.Parts.Count * 0.2));
        return System.Math.PI * maxRadius * maxRadius;
    }
}
