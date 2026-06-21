namespace Exosphere.Simulation.Construction;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

public sealed record AssemblyPart(
    string InstanceId,
    string DefinitionId,
    string? ParentInstanceId,
    string? ParentNodeId,
    string? ChildNodeId);

public sealed record AssemblyConnection(
    string ParentInstanceId,
    string ChildInstanceId,
    string ParentNodeId,
    string ChildNodeId);

public sealed record VesselMetrics(
    double WetMass,
    double DryMass,
    double PropellantMass,
    double SeaLevelThrust,
    double VacuumThrust,
    double SeaLevelTwr,
    double VacuumDeltaV);

public sealed class VesselAssembly
{
    private const double G0 = 9.80665;

    private readonly PartCatalog _catalog;
    private readonly List<AssemblyPart> _parts = new();
    private readonly List<AssemblyConnection> _connections = new();
    private readonly HashSet<(string instanceId, string nodeId)> _usedNodes = new();

    public VesselAssembly(PartCatalog catalog)
    {
        _catalog = catalog;
    }

    public IReadOnlyList<AssemblyPart> Parts => _parts;
    public IReadOnlyList<AssemblyConnection> Connections => _connections;
    public string? RootInstanceId => _parts.FirstOrDefault(p => p.ParentInstanceId == null)?.InstanceId;

    public AssemblyPart AddRoot(string definitionId)
    {
        if (_parts.Count > 0)
            throw new InvalidOperationException("Assembly already has a root part.");
        var def = RequirePart(definitionId);
        var part = new AssemblyPart(NewInstanceId(definitionId), def.Id, null, null, null);
        _parts.Add(part);
        return part;
    }

    public AssemblyPart AttachPart(
        string parentInstanceId,
        string parentNodeId,
        string childDefinitionId,
        string childNodeId)
    {
        var parent = RequireInstance(parentInstanceId);
        var parentDef = RequirePart(parent.DefinitionId);
        var childDef = RequirePart(childDefinitionId);
        var parentNode = RequireNode(parentDef, parentNodeId);
        var childNode = RequireNode(childDef, childNodeId);

        ValidateCompatibleNodes(parentInstanceId, parentNode, childDef.Id, childNode);

        var child = new AssemblyPart(NewInstanceId(childDef.Id), childDef.Id, parent.InstanceId, parentNode.Id, childNode.Id);
        _parts.Add(child);
        _connections.Add(new AssemblyConnection(parent.InstanceId, child.InstanceId, parentNode.Id, childNode.Id));
        _usedNodes.Add((parent.InstanceId, parentNode.Id));
        _usedNodes.Add((child.InstanceId, childNode.Id));
        return child;
    }

    public bool DeletePart(string instanceId)
    {
        var part = _parts.FirstOrDefault(p => p.InstanceId == instanceId);
        if (part == null) return false;

        var remove = new HashSet<string> { instanceId };
        bool changed;
        do
        {
            changed = false;
            foreach (var child in _parts.Where(p => p.ParentInstanceId != null && remove.Contains(p.ParentInstanceId)))
            {
                if (remove.Add(child.InstanceId)) changed = true;
            }
        }
        while (changed);

        _parts.RemoveAll(p => remove.Contains(p.InstanceId));
        _connections.RemoveAll(c => remove.Contains(c.ParentInstanceId) || remove.Contains(c.ChildInstanceId));
        RebuildUsedNodes();
        return true;
    }

    public VesselMetrics ComputeMetrics(double gravity = G0)
    {
        double wet = 0.0;
        double dry = 0.0;
        double thrustSl = 0.0;
        double thrustVac = 0.0;
        double ispWeightedThrust = 0.0;

        foreach (var item in _parts)
        {
            var def = RequirePart(item.DefinitionId);
            double prop = def.FuelCapacityLF + def.FuelCapacityOx + def.FuelCapacitySolid + def.FuelCapacityMono;
            dry += def.MassDry;
            wet += def.MassDry + prop;

            if (def.Category == PartCategory.Engine)
            {
                double sl = def.ThrustSL > 0.0 ? def.ThrustSL : def.ThrustVac;
                double vac = def.ThrustVac > 0.0 ? def.ThrustVac : sl;
                thrustSl += sl;
                thrustVac += vac;
                if (def.IspVac > 0.0)
                    ispWeightedThrust += vac * def.IspVac;
            }
        }

        double ispVac = thrustVac > 0.0 ? ispWeightedThrust / thrustVac : 0.0;
        double dv = wet > dry && dry > 0.0 && ispVac > 0.0
            ? ispVac * G0 * System.Math.Log(wet / dry)
            : 0.0;
        double twr = wet > 0.0 && gravity > 0.0 ? thrustSl / (wet * gravity) : 0.0;

        return new VesselMetrics(wet, dry, wet - dry, thrustSl, thrustVac, twr, dv);
    }

    public PartGraph ToPartGraph()
    {
        if (_parts.Count == 0)
            throw new InvalidOperationException("Cannot export an empty assembly.");

        var graph = new PartGraph();
        var liveParts = new Dictionary<string, Part>();
        foreach (var item in _parts)
        {
            liveParts[item.InstanceId] = new Part(RequirePart(item.DefinitionId));
        }

        var root = RootInstanceId ?? _parts[0].InstanceId;
        graph.SetRoot(liveParts[root]);
        foreach (var item in _parts)
            graph.AddPart(liveParts[item.InstanceId]);

        foreach (var connection in _connections)
        {
            graph.AddJoint(new Joint(
                liveParts[connection.ParentInstanceId],
                liveParts[connection.ChildInstanceId],
                connection.ParentNodeId,
                connection.ChildNodeId));
        }

        return graph;
    }

    public Vessel ToVessel(string name = "Constructed Vessel")
    {
        var vessel = new Vessel
        {
            Name = name,
            Orientation = Quaterniond.Identity,
            SASEnabled = true,
        };

        var graph = ToPartGraph();
        foreach (var part in graph.Parts)
            vessel.Parts.AddPart(part);
        if (graph.Root != null)
            vessel.Parts.SetRoot(graph.Root);
        foreach (var joint in graph.Joints)
            vessel.Parts.AddJoint(joint);

        return vessel;
    }

    public VesselCraftDefinition ToCraft(string name = "Constructed Vessel") => new()
    {
        Name = name,
        Parts = _parts.ToList(),
        Connections = _connections.ToList(),
    };

    public static VesselAssembly FromCraft(PartCatalog catalog, VesselCraftDefinition craft)
    {
        var assembly = new VesselAssembly(catalog);
        if (craft.Parts.Count == 0) return assembly;

        var seen = new HashSet<string>();
        foreach (var part in craft.Parts)
        {
            if (!seen.Add(part.InstanceId))
                throw new InvalidOperationException($"Duplicate assembly part '{part.InstanceId}'.");
            _ = assembly.RequirePart(part.DefinitionId);
            assembly._parts.Add(part);
        }

        if (assembly._parts.Count(p => p.ParentInstanceId == null) != 1)
            throw new InvalidOperationException("Craft must contain exactly one root part.");

        foreach (var connection in craft.Connections)
        {
            var parent = assembly.RequireInstance(connection.ParentInstanceId);
            var child = assembly.RequireInstance(connection.ChildInstanceId);
            var parentDef = assembly.RequirePart(parent.DefinitionId);
            var childDef = assembly.RequirePart(child.DefinitionId);
            var parentNode = RequireNode(parentDef, connection.ParentNodeId);
            var childNode = RequireNode(childDef, connection.ChildNodeId);

            assembly.ValidateCompatibleNodes(parent.InstanceId, parentNode, child.DefinitionId, childNode);
            assembly._connections.Add(connection);
            assembly._usedNodes.Add((parent.InstanceId, parentNode.Id));
            assembly._usedNodes.Add((child.InstanceId, childNode.Id));
        }

        return assembly;
    }

    public IEnumerable<AttachmentNodeDef> AvailableNodes(string instanceId)
    {
        var part = RequireInstance(instanceId);
        return RequirePart(part.DefinitionId).AttachmentNodes
            .Where(n => IsAttachable(n) && !_usedNodes.Contains((instanceId, n.Id)));
    }

    public static bool NodesAreCompatible(AttachmentNodeDef parentNode, AttachmentNodeDef childNode)
    {
        if (!IsAttachable(parentNode) || !IsAttachable(childNode)) return false;
        if (!string.Equals(parentNode.Type, childNode.Type, StringComparison.OrdinalIgnoreCase)) return false;
        if (parentNode.Type.Equals("stack", StringComparison.OrdinalIgnoreCase))
            return parentNode.Size == childNode.Size;
        return parentNode.Type.Equals("radial", StringComparison.OrdinalIgnoreCase);
    }

    private void ValidateCompatibleNodes(
        string parentInstanceId,
        AttachmentNodeDef parentNode,
        string childDefinitionId,
        AttachmentNodeDef childNode)
    {
        if (_usedNodes.Contains((parentInstanceId, parentNode.Id)))
            throw new InvalidOperationException($"Parent node '{parentNode.Id}' is already occupied.");
        if (!NodesAreCompatible(parentNode, childNode))
            throw new InvalidOperationException(
                $"Node '{parentNode.Id}' is not compatible with {childDefinitionId}.{childNode.Id}.");
    }

    private AssemblyPart RequireInstance(string instanceId) =>
        _parts.FirstOrDefault(p => p.InstanceId == instanceId)
        ?? throw new ArgumentException($"Unknown assembly part '{instanceId}'.", nameof(instanceId));

    private PartDefinition RequirePart(string definitionId) =>
        _catalog.TryGet(definitionId, out var def)
            ? def
            : throw new ArgumentException($"Unknown part definition '{definitionId}'.", nameof(definitionId));

    private static AttachmentNodeDef RequireNode(PartDefinition def, string nodeId) =>
        def.AttachmentNodes.FirstOrDefault(n => n.Id == nodeId)
        ?? throw new ArgumentException($"Part '{def.Id}' has no attachment node '{nodeId}'.", nameof(nodeId));

    private static bool IsAttachable(AttachmentNodeDef node) =>
        !node.Type.Equals("engine_bell", StringComparison.OrdinalIgnoreCase);

    private void RebuildUsedNodes()
    {
        _usedNodes.Clear();
        foreach (var connection in _connections)
        {
            _usedNodes.Add((connection.ParentInstanceId, connection.ParentNodeId));
            _usedNodes.Add((connection.ChildInstanceId, connection.ChildNodeId));
        }
    }

    private static string NewInstanceId(string definitionId) =>
        $"{definitionId}:{Guid.NewGuid():N}";
}
