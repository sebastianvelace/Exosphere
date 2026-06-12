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

    // Parts belonging to the currently-firing stage: the subtree hanging below the
    // lowest still-attached decoupler (the side away from the root command section).
    // With no active decoupler the whole vessel is one stage. Engines only fire — and
    // only draw propellant — within this set, so a multi-stage stack burns its bottom
    // stage first and the upper stage stays fuelled until separation (real rockets do
    // not cross-feed across stage interfaces, nor light all stages at liftoff).
    public List<Part> CurrentStageParts()
    {
        var activeDecouplers = _parts
            .Where(p => p.Definition.Category == PartCategory.Decoupler && p.IsStagingActive)
            .ToList();
        if (activeDecouplers.Count == 0) return new List<Part>(_parts);

        foreach (var d in activeDecouplers)
        {
            var child = GetChildren(d).FirstOrDefault();
            if (child == null) continue;
            var farSide = CollectSubtree(child);   // subtree below the decoupler
            // The bottom stage's subtree contains no further attached decoupler.
            if (!farSide.Any(p => p.Definition.Category == PartCategory.Decoupler && p.IsStagingActive))
                return farSide;
        }
        return new List<Part>(_parts);
    }

    public IEnumerable<Part> ActiveEngines
    {
        get
        {
            var stage = CurrentStageParts();
            return stage.Where(p => p.Definition.Category == PartCategory.Engine
                                 && p.IsStagingActive && !p.IsBroken);
        }
    }

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
    // Overload sin presión: empuje de vacío (compatibilidad).
    public Vector3d GetTotalThrust() => GetTotalThrust(0.0);

    // Empuje total corregido por presión ambiente (Pa).
    public Vector3d GetTotalThrust(double ambientPressure) =>
        ActiveEngines.Aggregate(Vector3d.Zero, (sum, e) => sum + e.GetThrustVector(ambientPressure));

    // ── Consumir propelante de los motores de la etapa activa ─────────────
    // Cross-feed dentro de la etapa: los motores extraen combustible de los tanques de
    // su propia etapa (los motores no tienen capacidad propia), pero NO a través de un
    // desacoplador activo — así la etapa superior conserva su propelante hasta separarse.
    public void ConsumePropellant(double dt, double ambientPressure)
    {
        var stage   = CurrentStageParts();
        var engines = stage.Where(p => p.Definition.Category == PartCategory.Engine
                                    && p.IsStagingActive && !p.IsBroken).ToList();
        if (engines.Count == 0) return;

        // Calcular flujo de masa total de todos los motores activos
        double totalLiquidRate = 0, totalSolidRate = 0, totalMonoRate = 0;
        foreach (var engine in engines)
        {
            var def = engine.Definition;
            // Isp interpolado por presión: pf=0 (vacío)→IspVac, pf=1 (mar)→IspSL.
            double pf  = System.Math.Clamp(ambientPressure / 101325.0, 0.0, 1.0);
            double isp = def.IspVac + (def.IspSL - def.IspVac) * pf;
            if (isp < 1.0) continue;

            // ṁ = F(p)/(Isp·g₀) con el empuje corregido por presión (coherente con
            // GetThrustMagnitude), no el empuje de vacío bruto.
            double massFlow = engine.GetThrustMagnitude(ambientPressure) / (isp * 9.80665);
            var fuelType = def.FuelTypeStr.ToLowerInvariant();

            if (fuelType.Contains("liquidfuel") || fuelType.Contains("liquid_fuel"))
                totalLiquidRate += massFlow;     // se reparte LF/Ox según la carga del tanque
            else if (fuelType.Contains("solid"))
                totalSolidRate += massFlow;
            else if (fuelType.Contains("mono"))
                totalMonoRate += massFlow;
        }

        // Consumir de los tanques de la etapa activa (cross-feed dentro de la etapa)
        bool flameOut = false;

        if (totalLiquidRate > 0)
        {
            double totalLF   = stage.Sum(p => p.LiquidFuel);
            double totalOx   = stage.Sum(p => p.Oxidizer);
            // Repartir el flujo de masa entre LF y Ox según la proporción cargada, de modo
            // que ambos se agoten juntos (sin oxidante varado por una mezcla mal calibrada).
            double inv = totalLF + totalOx;
            double lfFrac = inv > 1e-9 ? totalLF / inv : 0.45;
            double lfNeeded = totalLiquidRate * lfFrac * dt;
            double oxNeeded = totalLiquidRate * (1.0 - lfFrac) * dt;

            if (totalLF < lfNeeded || totalOx < oxNeeded)
            {
                flameOut = true;
            }
            else
            {
                // Drenar proporcionalmente de cada tanque que tenga combustible
                foreach (var p in stage)
                {
                    if (totalLF > 0) p.LiquidFuel -= lfNeeded * (p.LiquidFuel / totalLF);
                    if (totalOx > 0) p.Oxidizer   -= oxNeeded * (p.Oxidizer   / totalOx);
                }
            }
        }

        if (totalSolidRate > 0)
        {
            double solidNeeded = totalSolidRate * dt;
            double totalSolid  = stage.Sum(p => p.SolidFuel);
            if (totalSolid < solidNeeded) flameOut = true;
            else foreach (var p in stage.Where(p2 => p2.SolidFuel > 0))
                p.SolidFuel -= solidNeeded * (p.SolidFuel / totalSolid);
        }

        if (totalMonoRate > 0)
        {
            double monoNeeded = totalMonoRate * dt;
            double totalMono  = stage.Sum(p => p.Monopropellant);
            if (totalMono < monoNeeded) flameOut = true;
            else foreach (var p in stage.Where(p2 => p2.Monopropellant > 0))
                p.Monopropellant -= monoNeeded * (p.Monopropellant / totalMono);
        }

        if (flameOut)
            foreach (var engine in engines)
                engine.IsStagingActive = false;
    }

    // ── Staging: dispara el primer desacoplador disponible ────────────────
    // Retorna el PartGraph separado (la sección inferior), o null si nada
    public PartGraph? FireNextStage()
    {
        var decoupler = _parts.FirstOrDefault(
            p => p.Definition.Category == PartCategory.Decoupler && p.IsStagingActive);
        if (decoupler == null) return null;

        decoupler.IsStagingActive = false;

        // Buscamos primero el joint donde decoupler es Parent (separa lo que está DEBAJO).
        // Esto garantiza que SH se detache correctamente en stack command→tank→eng→decoupler→SH.
        var separationJoint = _joints.FirstOrDefault(j => j.Parent == decoupler)
            ?? _joints.FirstOrDefault(j => j.Child  == decoupler);
        if (separationJoint == null) return null;

        // El lado separado es el Child si decoupler es Parent, o el Parent si decoupler es Child.
        var separationRoot = separationJoint.Parent == decoupler
            ? separationJoint.Child
            : separationJoint.Parent;

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
