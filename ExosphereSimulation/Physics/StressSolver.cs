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

    // Aplica calor a todas las piezas del grafo. Retorna las piezas destruidas.
    public static List<Part> ApplyThermalLoads(PartGraph graph, double heatFlux, double dt)
    {
        var destroyed = new List<Part>();
        foreach (var part in graph.Parts)
        {
            if (ThermalModel.ApplyHeat(part, heatFlux, dt))
            {
                part.IsBroken = true;
                destroyed.Add(part);
            }
        }
        return destroyed;
    }
}
