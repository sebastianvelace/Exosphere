namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Navigation;

/// <summary>
/// Calcula nodos de maniobra de transferencia Hohmann a un cuerpo destino.
/// Se instancia como hijo de MapViewController y se registra como singleton.
/// </summary>
public partial class TransferPlanner : Node
{
    public static TransferPlanner? Instance { get; private set; }

    public string? TargetBodyId  { get; private set; }
    public ManeuverNode? CurrentNode { get; private set; }

    public override void _Ready() => Instance = this;

    /// <summary>
    /// Calcula una transferencia Hohmann desde la órbita actual hacia el cuerpo destino.
    /// Retorna null si no hay suficiente información.
    /// </summary>
    public ManeuverNode? PlanTransfer(string targetBodyId)
    {
        TargetBodyId = targetBodyId;
        CurrentNode  = null;

        var bridge   = SimulationBridge.Instance;
        var vessel   = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (vessel == null || universe == null) return null;

        var targetBody = universe.GetBody(targetBodyId);
        var sunBody    = universe.GetBody("sun");
        if (targetBody == null || sunBody == null) return null;

        // Posiciones heliocentricas
        var r1Vec = vessel.Position     - sunBody.Position;   // vessel desde el Sol
        var r2Vec = targetBody.Position - sunBody.Position;   // destino desde el Sol

        double r1 = r1Vec.Magnitude;
        double r2 = r2Vec.Magnitude;

        if (r1 < 1000.0 || r2 < 1000.0) return null;

        double mu  = sunBody.GM;
        if (mu <= 0.0) return null;

        HohmannTransferPlan plan;
        try { plan = HohmannTransfer.Compute(mu, r1, r2); }
        catch (ArgumentException) { return null; }

        // Momento del burn = ahora
        double burnTime = universe.CurrentTime;

        // Dirección prograde heliocéntrica del vessel
        var helioVel = vessel.Velocity - sunBody.Velocity;
        var prograde = helioVel.Magnitude > 0.1
            ? helioVel.Normalized
            : r1Vec.Cross(Vector3d.Up).Normalized;

        CurrentNode = new ManeuverNode
        {
            BurnTime     = burnTime,
            DeltaV       = prograde * plan.FirstBurnDeltaV,
            DvMagnitude  = System.Math.Abs(plan.FirstBurnDeltaV),
            TargetBodyId = targetBodyId,
            TimeOfFlight = plan.TimeOfFlight,
            SecondBurnDv = plan.SecondBurnDeltaV,
            TransferSma  = plan.TransferSemiMajorAxis,
            RequiredPhaseAngle = plan.RequiredPhaseAngle,
        };

        GD.Print($"[TransferPlanner] Hohmann to {targetBodyId}: Δv1={plan.FirstBurnDeltaV:F0} m/s, " +
                 $"Δv2={plan.SecondBurnDeltaV:F0} m/s, ToF={plan.TimeOfFlight / 86400.0:F1} days, " +
                 $"phase={plan.RequiredPhaseAngle * MathUtils.RAD_TO_DEG:F1} deg");

        return CurrentNode;
    }

    public void ClearNode()
    {
        CurrentNode  = null;
        TargetBodyId = null;
    }
}

/// <summary>Representa un nodo de maniobra de transferencia calculado.</summary>
public class ManeuverNode
{
    /// <summary>Tiempo de simulación (s) al que iniciar el burn.</summary>
    public double   BurnTime       { get; set; }

    /// <summary>Vector Δv en espacio mundo (m/s).</summary>
    public Vector3d DeltaV         { get; set; }

    /// <summary>Magnitud |Δv| (m/s).</summary>
    public double   DvMagnitude    { get; set; }

    /// <summary>ID del cuerpo destino.</summary>
    public string?  TargetBodyId   { get; set; }

    /// <summary>Tiempo de vuelo hasta la llegada (s).</summary>
    public double   TimeOfFlight   { get; set; }

    /// <summary>Δv del segundo burn (circularización en destino) (m/s).</summary>
    public double   SecondBurnDv   { get; set; }

    /// <summary>Semi-eje mayor de la elipse de transferencia (m).</summary>
    public double   TransferSma    { get; set; }

    /// <summary>Required coplanar target lead angle at departure (rad), [0, 2pi).</summary>
    public double   RequiredPhaseAngle { get; set; }

    /// <summary>Factor de ajuste manual del usuario (±10% por pasos de 5%).</summary>
    public double   DvAdjustFactor { get; set; } = 1.0;
}
