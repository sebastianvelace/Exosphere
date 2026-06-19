namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

public static class StressSolver
{
    // Calcula las cargas en todos los joints dado la aceleración neta no gravitacional del vessel.
    // (La gravedad produce caída libre y no genera estrés interno en órbita.)
    public static void ComputeLoads(
        PartGraph graph,
        Vector3d netAcceleration,
        Quaterniond vesselOrientation)
    {
        if (graph.Root == null) return;

        // Transformar aceleración al espacio local del vessel
        var localAccel = vesselOrientation.Inverse().Rotate(netAcceleration);
        ComputeJointLoads(graph, graph.Root, localAccel);
    }

    private static void ComputeJointLoads(PartGraph graph, Part part, Vector3d localAccel)
    {
        foreach (var child in graph.GetChildren(part))
        {
            var joint = graph.GetJoint(part, child);
            if (joint == null) continue;

            double massBelow    = CollectSubtreeMass(graph, child);
            double axialAccel   = System.Math.Abs(localAccel.Y);
            double lateralAccel = System.Math.Sqrt(
                localAccel.X * localAccel.X + localAccel.Z * localAccel.Z);

            joint.CurrentTensileLoad = massBelow * axialAccel;
            joint.CurrentShearLoad   = massBelow * lateralAccel;

            ComputeJointLoads(graph, child, localAccel);
        }
    }

    private static double CollectSubtreeMass(PartGraph graph, Part root)
    {
        double mass = root.CurrentMass;
        foreach (var child in graph.GetChildren(root))
            mass += CollectSubtreeMass(graph, child);
        return mass;
    }

    // Joints que están sobre sus límites de carga
    public static IEnumerable<Joint> FindBreakingJoints(PartGraph graph) =>
        graph.Joints.Where(j => j.IsBreaking);

    // ── Thermal loads ─────────────────────────────────────────────────────────

    /// <summary>
    /// Applies a free-stream convective heat flux to every part for <paramref name="dt"/>
    /// seconds, accumulating <see cref="Part.Temperature"/>. Returns the parts whose
    /// temperature now exceeds their tolerance (so the impact/crash owner can destroy them).
    ///
    /// Orientation-agnostic overload (the orientation of the windward shield is not known):
    /// a heat-shielded part is given the benefit of the doubt and receives only the shielded
    /// residual flux, so the existing per-tick call path never destroys a correctly built
    /// Starship. The orientation-sensitive verdict (shield pointed the wrong way → destroyed)
    /// is resolved by <see cref="WorstHeatRatio(PartGraph, Vector3d)"/> and the overload below.
    /// </summary>
    public static List<Part> ApplyThermalLoads(PartGraph graph, double heatFlux, double dt)
    {
        var destroyed = new List<Part>();
        foreach (var part in graph.Parts)
        {
            bool shield = ThermalModel.HasHeatShield(part);
            // Sin orientación conocida, asumimos el escudo bien orientado (caso favorable).
            double flux = heatFlux * ThermalModel.EffectiveFluxFactor(shield, windwardFactor: 1.0);
            if (ThermalModel.ApplyHeat(part, flux, dt))
            {
                part.IsBroken = true;
                destroyed.Add(part);
            }
        }
        return destroyed;
    }

    /// <summary>
    /// Orientation-aware thermal application. <paramref name="flowDirLocal"/> is the airflow
    /// direction in the vessel's local frame (<c>orientation⁻¹ · surfaceVelocityDir</c>); a
    /// heat-shielded part is protected only when its windward (belly) face meets that flow.
    /// A shield turned away from the flow gives no protection — the bare side burns.
    /// Returns the parts that exceeded their tolerance this tick.
    /// </summary>
    public static List<Part> ApplyThermalLoads(
        PartGraph graph, double heatFlux, double dt, Vector3d flowDirLocal)
    {
        double windward = ThermalModel.WindwardFactor(flowDirLocal);
        var destroyed = new List<Part>();
        foreach (var part in graph.Parts)
        {
            bool shield = ThermalModel.HasHeatShield(part);
            double flux = heatFlux * ThermalModel.EffectiveFluxFactor(shield, windward);
            if (ThermalModel.ApplyHeat(part, flux, dt))
            {
                part.IsBroken = true;
                destroyed.Add(part);
            }
        }
        return destroyed;
    }

    // ── Thermal damage contract (leído por C/Universe para decidir destrucción) ──

    /// <summary>
    /// Worst-case thermal damage ratio across all exposed parts:
    /// the maximum of <see cref="Part.Temperature"/> / <see cref="PartDefinition.HeatTolerance"/>.
    /// A value ≥ 1.0 means at least one part is over its tolerance and the vessel SHOULD be
    /// destroyed — but the destruction decision belongs to the impact/crash owner
    /// (Universe), which reads this ratio. This solver never sets <c>Vessel.IsDestroyed</c>.
    ///
    /// Orientation-agnostic form: a part carrying a heat shield is assumed best-oriented and
    /// its ratio is scaled down by the shielded flux floor; bare parts count at face value.
    /// For an orientation-sensitive verdict use <see cref="WorstHeatRatio(PartGraph, Vector3d)"/>.
    ///
    /// Razón de daño térmico peor caso: máx(Temperature/heat_tolerance). ≥1.0 ⇒ la nave
    /// debería destruirse (lo decide C). Las piezas con escudo cuentan como protegidas.
    /// </summary>
    public static double WorstHeatRatio(PartGraph parts)
    {
        double worst = 0.0;
        foreach (var part in parts.Parts)
        {
            if (part.IsBroken) continue;
            double tol = part.Definition.HeatTolerance;
            if (tol <= 0.0) continue;
            bool shield = ThermalModel.HasHeatShield(part);
            double protection = shield ? ThermalModel.ShieldedFluxFloor : 1.0;
            double ratio = (part.Temperature / tol) * protection;
            if (ratio > worst) worst = ratio;
        }
        return worst;
    }

    /// <summary>
    /// Orientation-aware worst-case thermal damage ratio. <paramref name="flowDirLocal"/> is
    /// the airflow direction in the vessel's local frame. A heat-shielded part is protected
    /// in proportion to how squarely its windward (belly) face meets the flow: shield into
    /// the flow ⇒ low ratio (survives); shield turned away ⇒ full ratio (the bare side burns
    /// through and the ratio crosses 1.0). Bare parts always count at face value.
    /// </summary>
    public static double WorstHeatRatio(PartGraph parts, Vector3d flowDirLocal)
    {
        double windward = ThermalModel.WindwardFactor(flowDirLocal);
        double worst = 0.0;
        foreach (var part in parts.Parts)
        {
            if (part.IsBroken) continue;
            double tol = part.Definition.HeatTolerance;
            if (tol <= 0.0) continue;
            bool shield = ThermalModel.HasHeatShield(part);
            double protection = ThermalModel.EffectiveFluxFactor(shield, windward);
            double ratio = (part.Temperature / tol) * protection;
            if (ratio > worst) worst = ratio;
        }
        return worst;
    }
}
