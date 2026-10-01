namespace Exosphere.Simulation.Presentation;

using Exosphere.Simulation.Parts;

/// <summary>A thrust-plane diagram, in metres, with stable runtime engine identities.</summary>
public sealed record FlightEngineDot(string Id, string PartId, double X, double Z, double NozzleRadius, bool IsAggregate = false);

public sealed record FlightEngineBoard(string Label, IReadOnlyList<FlightEngineDot> Engines)
{
    public FlightEngineBoard AttachedTo(PartGraph graph)
    {
        var attached = graph.Parts.Select(p => p.InstanceId).ToHashSet(StringComparer.Ordinal);
        return this with { Engines = Engines.Where(e => attached.Contains(e.PartId)).ToArray() };
    }
}

/// <summary>Read-only topology projection. No staging, engine commands or solver writes.</summary>
public static class FlightEngineBoards
{
    public static IReadOnlyList<FlightEngineBoard> Build(PartGraph graph)
    {
        var parents = graph.Joints.ToDictionary(j => j.Child, j => j.Parent);
        var positions = graph.ComputePartLocalPositions();
        var engines = graph.Parts.Where(p => p.Definition.Category == PartCategory.Engine).ToArray();
        // Parts between the same separation boundaries form a propulsion stage. This
        // keeps Atlas's simultaneously firing booster pair and sustainer together.
        Part Boundary(Part engine)
        {
            var part = engine;
            while (parents.TryGetValue(part, out var parent))
            {
                part = parent;
                if (part.Definition.Category == PartCategory.Decoupler) return part;
            }
            return part;
        }
        var groups = engines.GroupBy(Boundary)
            .OrderBy(g => g.Min(p => positions[p].Y)).ToArray();
        var boards = new List<FlightEngineBoard>();
        for (int stage = 0; stage < groups.Length; stage++)
        {
            var parts = groups[stage].ToArray();
            string label = parts.Any(p => p.Definition.IsStarshipFamily && p.Definition.HasVehicleRole("booster"))
                ? "SUPER HEAVY"
                : parts.Any(p => p.Definition.IsStarshipFamily && p.Definition.HasVehicleRole("ship_engines"))
                    ? "STARSHIP" : $"STAGE {stage + 1}";
            if (parts.All(p => p.Definition.HasVehicleRole("retro_pack"))) label = "RETRO PACK";
            if (parts.All(p => p.Definition.HasVehicleRole("service_propulsion_engine"))) label = "SPS";
            if (parts.All(p => p.Definition.HasVehicleRole("lunar_module_descent_engine"))) label = "DESCENT";
            if (parts.All(p => p.Definition.HasVehicleRole("lunar_module_ascent_engine"))) label = "ASCENT";
            var dots = new List<FlightEngineDot>();
            foreach (var part in parts)
            {
                var origin = positions[part];
                if (!part.HasEngineRuntime)
                {
                    // Legacy aggregate parts have no per-engine telemetry. Show one
                    // aggregate indicator rather than fabricating individual states.
                    dots.Add(new(part.InstanceId, part.InstanceId, origin.X, origin.Z, 1, IsAggregate: true));
                    continue;
                }
                for (int i = 0; i < part.EngineStates.Count; i++)
                {
                    var state = part.EngineStates[i];
                    var mount = part.Definition.ResolvedEngineCluster?.Engines.ElementAtOrDefault(i);
                    double radius = part.Definition.ResolvedEngineModels.TryGetValue(state.EngineModelId, out var model)
                        && model.ExitAreaM2 is > 0 ? System.Math.Sqrt(model.ExitAreaM2.Value / System.Math.PI) : 1;
                    if (mount != null)
                        dots.Add(new(state.InstanceId, part.InstanceId,
                            origin.X + mount.Position.X, origin.Z + mount.Position.Z, radius));
                    else
                    {
                        // Unknown geometry gets a schematic ring, never the six-Raptor template.
                        double angle = 2 * System.Math.PI * i / part.EngineStates.Count;
                        double r = part.EngineStates.Count == 1 ? 0 : 1;
                        dots.Add(new(state.InstanceId, part.InstanceId,
                            origin.X + r * System.Math.Cos(angle), origin.Z + r * System.Math.Sin(angle), radius));
                    }
                }
            }
            boards.Add(new(label, dots));
        }
        return boards;
    }

    public static (FlightEngineBoard? Current, FlightEngineBoard? Next) Select(
        IReadOnlyList<FlightEngineBoard> boards, PartGraph graph)
    {
        // CurrentStageParts also includes broken or deselected motors, which must stay
        // visible. ActiveEngines alone would hide their failures or invent a new stage.
        var stageIds = graph.CurrentStageParts().Select(p => p.InstanceId).ToHashSet(StringComparer.Ordinal);
        int current = -1;
        for (int i = 0; i < boards.Count; i++)
            if (boards[i].Engines.Any(e => stageIds.Contains(e.PartId))) { current = i; break; }
        if (current < 0) return (null, null);
        var first = boards[current].AttachedTo(graph);
        for (int i = current + 1; i < boards.Count; i++)
        {
            var next = boards[i].AttachedTo(graph);
            if (next.Engines.Count > 0) return (first, next);
        }
        return (first, null);
    }
}
