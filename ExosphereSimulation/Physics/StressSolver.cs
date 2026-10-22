namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

public static class StressSolver
{
    /// <summary>
    /// Quasi-static joint loads of a rigid vehicle under the non-gravitational acceleration
    /// <paramref name="netAcceleration"/> (gravity causes free fall, not internal stress).
    ///
    /// <para>Each joint carries the force the parent side applies to the child side:
    /// F = m_child·a − ΣF_ext(child side). Engine thrust acts on the engine parts that
    /// produce it; the remaining external force (aerodynamics, contact) is distributed by
    /// part length as a projected-area proxy. A thrusting stack therefore compresses each
    /// interface by the mass it pushes, not by the mass hanging below the root.</para>
    ///
    /// <para>The axial component is tension when it pulls the child toward the parent and
    /// compression when it pushes it away; the lateral component is shear.</para>
    /// </summary>
    public static void ComputeLoads(
        PartGraph graph,
        Vector3d netAcceleration,
        Quaterniond vesselOrientation,
        double ambientPressure = 0.0)
    {
        if (graph.Root == null) return;

        var localAccel = vesselOrientation.Inverse().Rotate(netAcceleration);
        var thrust = new Dictionary<Part, Vector3d>();
        var totalThrust = Vector3d.Zero;
        foreach (var engine in graph.ActiveEngineList)
        {
            var vector = engine.GetThrustVector(ambientPressure);
            thrust[engine] = vector;
            totalThrust += vector;
        }

        double totalMass = 0.0, totalLength = 0.0;
        foreach (var part in graph.Parts)
        {
            totalMass += part.CurrentMass;
            totalLength += System.Math.Max(0.0, part.Definition.LengthM);
        }
        var distributedForce = localAccel * totalMass - totalThrust;
        var positions = graph.ComputePartLocalPositions();
        ComputeJointLoads(graph, graph.Root, localAccel, thrust, distributedForce,
            totalLength, graph.Parts.Count, positions);
    }

    private static (double Mass, Vector3d ExternalForce) ComputeJointLoads(
        PartGraph graph,
        Part part,
        Vector3d localAccel,
        IReadOnlyDictionary<Part, Vector3d> thrust,
        Vector3d distributedForce,
        double totalLength,
        int partCount,
        IReadOnlyDictionary<Part, Vector3d> positions)
    {
        double share = totalLength > 0.0
            ? System.Math.Max(0.0, part.Definition.LengthM) / totalLength
            : 1.0 / System.Math.Max(1, partCount);
        double mass = part.CurrentMass;
        var external = distributedForce * share
            + (thrust.TryGetValue(part, out var partThrust) ? partThrust : Vector3d.Zero);

        foreach (var child in graph.GetChildren(part))
        {
            var (childMass, childExternal) = ComputeJointLoads(graph, child, localAccel,
                thrust, distributedForce, totalLength, partCount, positions);
            mass += childMass;
            external += childExternal;

            var joint = graph.GetJoint(part, child);
            if (joint == null) continue;

            var forceOnChild = localAccel * childMass - childExternal;
            var towardParent = positions.TryGetValue(part, out var parentPosition)
                && positions.TryGetValue(child, out var childPosition)
                && System.Math.Abs(parentPosition.Y - childPosition.Y) > 1e-9
                    ? System.Math.Sign(parentPosition.Y - childPosition.Y)
                    : 1.0;
            double axial = forceOnChild.Y * towardParent;
            joint.CurrentTensileLoad = System.Math.Max(0.0, axial);
            joint.CurrentCompressiveLoad = System.Math.Max(0.0, -axial);
            joint.CurrentShearLoad = System.Math.Sqrt(
                forceOnChild.X * forceOnChild.X + forceOnChild.Z * forceOnChild.Z);
        }

        return (mass, external);
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
        // Sin orientación conocida, asumimos el escudo bien orientado (caso favorable).
        return ApplyThermalLoads(graph, heatFlux, dt, shieldedFraction: 1.0);
    }

    /// <summary>
    /// Orientation-aware thermal application. <paramref name="flowDirLocal"/> is the airflow
    /// direction in the vessel's local frame (<c>orientation⁻¹ · surfaceVelocityDir</c>); a
    /// heat-shielded part is protected only when its data-defined windward face meets that flow.
    /// A shield turned away from the flow gives no protection — the bare side burns.
    /// Returns the parts that exceeded their tolerance this tick.
    /// </summary>
    public static List<Part> ApplyThermalLoads(
        PartGraph graph, double heatFlux, double dt, Vector3d flowDirLocal)
    {
        var destroyed = new List<Part>();
        var shadowed = FindHeatShieldWake(graph, flowDirLocal);
        foreach (var part in graph.Parts)
        {
            if (graph.IsEnclosedPayload(part)) continue;
            // Landing hardware is stowed inside the aft skirt during entry. It must not
            // receive free-stream heating until EDL explicitly deploys it near the pad;
            // otherwise a recoverability kit burns off tens of kilometres above the site
            // before the contact solver ever has a chance to use it.
            if (part.Definition.Category == PartCategory.Landing && !part.IsDeployed)
                continue;

            double shieldedFraction = part.Definition.HasHeatShield
                ? ThermalModel.WindwardFactor(
                    flowDirLocal,
                    ShieldNormal(part.Definition.HeatShieldNormal))
                : 0.0;
            double partFlux = shadowed.Contains(part) ? heatFlux * AfterbodyHeatingFraction : heatFlux;
            if (ThermalModel.ApplyHeat(
                    part, partFlux, dt, shieldedFraction))
            {
                part.IsBroken = true;
                destroyed.Add(part);
            }
        }
        return destroyed;
    }

    /// <summary>
    /// Share of the stagnation heat flux that reaches an afterbody in the wake of a blunt
    /// heat shield (a few percent behind capsule shields).
    /// </summary>
    public const double AfterbodyHeatingFraction = 0.05;

    /// <summary>
    /// Parts in the wake of a heat-shielded part that leads into the flow: downstream of it by
    /// more than 0.1 m and no wider. Without this every part took the full stagnation flux, so
    /// Gemini's docking nose, behind the heat shield, burned through at 72 km.
    /// </summary>
    private static HashSet<Part> FindHeatShieldWake(PartGraph graph, Vector3d flowDirLocal)
    {
        var wake = new HashSet<Part>();
        if (flowDirLocal.MagnitudeSquared < 1e-12) return wake;
        var flow = flowDirLocal.Normalized;
        Dictionary<Part, Vector3d>? positions = null;
        foreach (var shield in graph.Parts)
        {
            if (!shield.Definition.HasHeatShield || shield.IsBroken
                || ThermalModel.WindwardFactor(flow, ShieldNormal(shield.Definition.HeatShieldNormal)) < 0.7)
                continue;
            positions ??= graph.ComputePartLocalPositions();
            if (!positions.TryGetValue(shield, out var shieldPosition)) continue;
            foreach (var part in graph.Parts)
            {
                if (part == shield || !positions.TryGetValue(part, out var position)) continue;
                if ((position - shieldPosition).Dot(flow) < -0.1
                    && part.Definition.DiameterM <= shield.Definition.DiameterM + 1e-6)
                    wake.Add(part);
            }
        }
        return wake;
    }

    /// <summary>
    /// Applies the free-stream flux to every part with a known shielded fraction. The flux is
    /// NOT pre-attenuated: the two-node model decides what actually reaches the structure,
    /// because protection is insulation, not a discount on the incoming heat.
    /// </summary>
    private static List<Part> ApplyThermalLoads(
        PartGraph graph, double heatFlux, double dt, double shieldedFraction)
    {
        var destroyed = new List<Part>();
        foreach (var part in graph.Parts)
        {
            if (graph.IsEnclosedPayload(part)) continue;
            if (part.Definition.Category == PartCategory.Landing && !part.IsDeployed)
                continue;

            if (ThermalModel.ApplyHeat(part, heatFlux, dt, shieldedFraction))
            {
                part.IsBroken = true;
                destroyed.Add(part);
            }
        }
        return destroyed;
    }

    private static Vector3d ShieldNormal(double[]? components) =>
        components is { Length: 3 }
            && components.All(double.IsFinite)
            ? new Vector3d(components[0], components[1], components[2])
            : -Vector3d.Right;

    // ── Thermal damage contract (leído por C/Universe para decidir destrucción) ──

    /// <summary>
    /// Worst-case thermal damage ratio across all exposed parts:
    /// the maximum of <see cref="Part.Temperature"/> / <see cref="PartDefinition.HeatTolerance"/>.
    /// A value ≥ 1.0 means at least one part is over its tolerance and the vessel SHOULD be
    /// destroyed — but the destruction decision belongs to the impact/crash owner
    /// (Universe), which reads this ratio. This solver never sets <c>Vessel.IsDestroyed</c>.
    ///
    /// This reads the STRUCTURE temperature, which already carries whatever protection the
    /// shield gave it: the two-node model insulates the structure while the TPS face runs
    /// white-hot, so no extra discount belongs here. Orientation still decides the outcome —
    /// it decides how fast the structure heats, not how its temperature is scored.
    ///
    /// Razón de daño térmico peor caso: máx(Temperature/heat_tolerance) sobre la estructura.
    /// ≥1.0 ⇒ la nave debería destruirse (lo decide Universe).
    /// </summary>
    public static double WorstHeatRatio(PartGraph parts)
    {
        double worst = 0.0;
        foreach (var part in parts.Parts)
        {
            if (parts.IsEnclosedPayload(part)) continue;
            if (part.IsBroken) continue;
            if (part.Definition.HeatTolerance <= 0.0) continue;
            double ratio = part.ThermalRatio;
            if (ratio > worst) worst = ratio;
        }
        return worst;
    }
}
