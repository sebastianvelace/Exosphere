namespace Exosphere.Simulation.Parts;

using Exosphere.Simulation.Math;

public class PartGraph
{
    private readonly List<Part>  _parts  = new();
    private readonly List<Joint> _joints = new();
    private Part? _root;

    public IReadOnlyList<Part>  Parts  => _parts.AsReadOnly();
    public IReadOnlyList<Joint> Joints => _joints.AsReadOnly();
    public Part? Root => _root;

    public void SetRoot(Part part) { _root = part; if (!_parts.Contains(part)) _parts.Add(part); }
    public void AddPart(Part part) { if (!_parts.Contains(part)) _parts.Add(part); }
    public void AddJoint(Joint joint)
    {
        _joints.Add(joint);
        if (!_parts.Contains(joint.Parent)) _parts.Add(joint.Parent);
        if (!_parts.Contains(joint.Child))  _parts.Add(joint.Child);
    }

    public IEnumerable<Part>  GetChildren(Part parent) =>
        _joints.Where(j => j.Parent == parent).Select(j => j.Child);
    public Joint? GetJoint(Part parent, Part child) =>
        _joints.FirstOrDefault(j => j.Parent == parent && j.Child == child);

    // ── Propiedades calculadas ────────────────────────────────────────────
    public double TotalMass        => _parts.Sum(p => p.CurrentMass);
    public double DryMass          => _parts.Sum(p => p.Definition.MassDry);
    public double TotalLiquidFuel  => _parts.Sum(p => p.LiquidFuel);
    public double TotalOxidizer    => _parts.Sum(p => p.Oxidizer);
    public double TotalElectricCharge => _parts.Sum(p => p.ElectricCharge);

    public IEnumerable<Part> ActiveEngines =>
        _parts.Where(p => p.Definition.Category == PartCategory.Engine
                       && p.IsStagingActive && !p.IsBroken);

    // Centro de masa en espacio local del vessel (+Y = arriba, raíz en Y=0)
    public Vector3d CenterOfMass
    {
        get
        {
            double totalMass = TotalMass;
            if (totalMass <= 0.0 || _root == null) return Vector3d.Zero;
            var positions = ComputePartLocalPositions();
            var com = Vector3d.Zero;
            foreach (var p in _parts)
            {
                if (positions.TryGetValue(p, out var pos))
                    com = com + pos * p.CurrentMass;
            }
            return com / totalMass;
        }
    }

    // ── Empuje total en espacio local ─────────────────────────────────────
    public Vector3d GetTotalThrust() =>
        ActiveEngines.Aggregate(Vector3d.Zero, (sum, e) => sum + e.GetThrustVector());

    // ── Consumir propelante en todos los motores activos ──────────────────
    public void ConsumePropellant(double dt, double ambientPressure)
    {
        foreach (var engine in ActiveEngines.ToList())
        {
            if (!engine.ConsumePropellant(dt, ambientPressure))
                engine.IsStagingActive = false;  // flame-out
        }
    }

    // ── Staging: dispara el primer desacoplador disponible ────────────────
    // Retorna el PartGraph separado (la sección inferior), o null si nada
    public PartGraph? FireNextStage()
    {
        var decoupler = _parts.FirstOrDefault(
            p => p.Definition.Category == PartCategory.Decoupler && p.IsStagingActive);
        if (decoupler == null) return null;

        decoupler.IsStagingActive = false;

        // El desacoplador se une a su hijo por un Joint
        var separationJoint = _joints.FirstOrDefault(
            j => j.Parent == decoupler || j.Child == decoupler);
        if (separationJoint == null) return null;

        // El lado separado es el "child" del joint
        var separationRoot = separationJoint.Child == decoupler
            ? separationJoint.Parent
            : separationJoint.Child;

        var detachedParts  = CollectSubtree(separationRoot);
        var detachedGraph  = new PartGraph();
        detachedGraph.SetRoot(separationRoot);
        foreach (var p in detachedParts)
            detachedGraph.AddPart(p);

        // Mover los joints del subárbol al nuevo grafo
        foreach (var j in _joints.Where(j => detachedParts.Contains(j.Parent)).ToList())
        {
            detachedGraph.AddJoint(j);
            _joints.Remove(j);
        }
        _joints.Remove(separationJoint);
        foreach (var p in detachedParts) _parts.Remove(p);

        return detachedGraph;
    }

    // ── Posiciones locales de piezas (para CoM y renderizado) ─────────────
    public Dictionary<Part, Vector3d> ComputePartLocalPositions()
    {
        var positions = new Dictionary<Part, Vector3d>();
        if (_root == null) return positions;
        AssignPositions(_root, Vector3d.Zero, positions);
        return positions;
    }

    private void AssignPositions(Part part, Vector3d pos, Dictionary<Part, Vector3d> map)
    {
        map[part] = pos;
        foreach (var child in GetChildren(part))
        {
            var joint = GetJoint(part, child);
            var parentNode = part.Definition.AttachmentNodes
                .FirstOrDefault(n => n.Id == joint?.ParentNodeId);
            var childNode  = child.Definition.AttachmentNodes
                .FirstOrDefault(n => n.Id == joint?.ChildNodeId);

            var pOff = parentNode != null
                ? new Vector3d(parentNode.Position[0], parentNode.Position[1], parentNode.Position[2])
                : Vector3d.Zero;
            var cOff = childNode != null
                ? new Vector3d(childNode.Position[0],  childNode.Position[1],  childNode.Position[2])
                : Vector3d.Zero;

            AssignPositions(child, pos + pOff - cOff, map);
        }
    }

    private List<Part> CollectSubtree(Part root)
    {
        var result = new List<Part>();
        var queue  = new Queue<Part>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            result.Add(p);
            foreach (var child in GetChildren(p)) queue.Enqueue(child);
        }
        return result;
    }
}
