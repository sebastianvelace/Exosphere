namespace Exosphere.Simulation.Parts;

using Exosphere.Simulation.Math;

/// <summary>
/// Immutable per-engine telemetry row for the HUD. Thrust is in newtons (pressure-corrected),
/// MassFlow in kg/s, Throttle in [0,1]. Built by <see cref="PartGraph.GetEngineReadouts"/>.
/// </summary>
public readonly record struct EngineReadout(
    string InstanceId, string Name, double Throttle, double ThrustN, double MassFlowKgS);

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
    public double VehicleLength
    {
        get
        {
            double specified = _parts.Sum(p => System.Math.Max(0.0, p.Definition.LengthM));
            return specified > 0.0
                ? specified
                : System.Math.Max(1.0, _parts.Count * 12.0);
        }
    }
    public double MaximumDiameter
    {
        get
        {
            if (_parts.Count == 0) return 1.0;
            double specified = _parts.Max(p => System.Math.Max(0.0, p.Definition.DiameterM));
            return specified > 0.0
                ? specified
                : System.Math.Max(1.0, 2.0 * System.Math.Sqrt(_parts.Count * 0.2));
        }
    }

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

    /// <summary>
    /// Approximate transverse inertia (kg·m²) of the declared cylindrical envelope.
    /// It changes continuously as propellant mass is consumed.
    /// </summary>
    public double TransverseMomentOfInertia
    {
        get
        {
            double radius = MaximumDiameter * 0.5;
            return TotalMass * (3.0 * radius * radius + VehicleLength * VehicleLength) / 12.0;
        }
    }

    /// <summary>Approximate roll-axis inertia (kg·m²) of the vehicle envelope.</summary>
    public double AxialMomentOfInertia
    {
        get
        {
            double radius = MaximumDiameter * 0.5;
            return 0.5 * TotalMass * radius * radius;
        }
    }

    /// <summary>
    /// Pitch/yaw angular acceleration available from live engine gimbal torque.
    /// Uses each cluster's physical thrust plane and the current propellant-dependent CoM.
    /// </summary>
    public double GetPitchYawAngularAcceleration(double ambientPressure)
        => GetPitchYawAngularAcceleration(ambientPressure, fullThrottle: false);

    /// <summary>Pitch/yaw authority if every selected engine were at full throttle.</summary>
    public double GetMaximumPitchYawAngularAcceleration(double ambientPressure)
        => GetPitchYawAngularAcceleration(ambientPressure, fullThrottle: true);

    private double GetPitchYawAngularAcceleration(double ambientPressure, bool fullThrottle)
    {
        double inertia = TransverseMomentOfInertia;
        if (inertia <= 0.0) return 0.0;

        var positions = ComputePartLocalPositions();
        double comY = CenterOfMass.Y;
        double torque = 0.0;
        foreach (var engine in ActiveEngines)
        {
            if (!positions.TryGetValue(engine, out var centre)) continue;
            double thrustY = centre.Y + engine.Definition.ThrustPositionYM;
            double lever = System.Math.Abs(thrustY - comY);
            double gimbal = System.Math.Abs(engine.Definition.GimbalRange) * MathUtils.DEG_TO_RAD;
            double thrust = fullThrottle
                ? engine.GetFullThrottleThrustMagnitude(ambientPressure)
                : engine.GetThrustMagnitude(ambientPressure);
            torque += thrust * lever * System.Math.Sin(gimbal);
        }
        return torque / inertia;
    }

    /// <summary>
    /// Roll angular acceleration from differential gimbal across a multi-engine cluster.
    /// A 65% radius is a conservative effective moment arm for concentric Raptor layouts.
    /// </summary>
    public double GetRollAngularAcceleration(double ambientPressure)
    {
        double inertia = AxialMomentOfInertia;
        if (inertia <= 0.0) return 0.0;

        double radius = MaximumDiameter * 0.5 * 0.65;
        double torque = 0.0;
        foreach (var engine in ActiveEngines)
        {
            if (engine.Definition.EngineCount < 2) continue;
            double gimbal = System.Math.Abs(engine.Definition.GimbalRange) * MathUtils.DEG_TO_RAD;
            torque += engine.GetThrustMagnitude(ambientPressure) * radius * System.Math.Sin(gimbal);
        }
        return torque / inertia;
    }

    // ── Empuje total en espacio local ─────────────────────────────────────
    // Overload sin presión: empuje de vacío (compatibilidad).
    public Vector3d GetTotalThrust() => GetTotalThrust(0.0);

    // Empuje total corregido por presión ambiente (Pa).
    public Vector3d GetTotalThrust(double ambientPressure) =>
        ActiveEngines.Aggregate(Vector3d.Zero, (sum, e) => sum + e.GetThrustVector(ambientPressure));

    // ── Read-only telemetry getters (consumed by the HUD) ─────────────────
    // These never mutate the sim; they report what the engines of the CURRENT stage are
    // doing at the given ambient pressure (Pa). Pass the live atmospheric pressure to get
    // pressure-corrected figures, or 0 for the vacuum case. The HUD must not have to touch
    // Part internals or duplicate the thrust equation — it just reads these.

    /// <summary>Number of engines in the current stage that are lit (firing).</summary>
    public int ActiveEngineCount =>
        ActiveEngines
            .Where(e => e.ThrottleLevel > 1e-3)
            .Sum(e => e.SelectedEngineCount);

    /// <summary>Total pressure-corrected thrust magnitude (N) of the current stage now.</summary>
    public double GetCurrentThrust(double ambientPressure) =>
        ActiveEngines.Sum(e => e.GetThrustMagnitude(ambientPressure));

    /// <summary>Pressure-corrected current-stage thrust available at full throttle.</summary>
    public double GetMaximumThrust(double ambientPressure) =>
        ActiveEngines.Sum(e => e.GetFullThrottleThrustMagnitude(ambientPressure));

    /// <summary>Total propellant mass flow of the current stage (kg/s) at this pressure.</summary>
    public double GetCurrentMassFlow(double ambientPressure) =>
        ActiveEngines.Sum(e => e.GetMassFlow(ambientPressure));

    /// <summary>
    /// Thrust-weighted current specific impulse (s) of the firing stage: the effective Isp of
    /// the whole cluster, = ΣF / (Σṁ·g₀). 0 when nothing is firing.
    /// </summary>
    public double GetCurrentIsp(double ambientPressure)
    {
        double mdot = GetCurrentMassFlow(ambientPressure);
        if (mdot <= 1e-9) return 0.0;
        return GetCurrentThrust(ambientPressure) / (mdot * 9.80665);
    }

    /// <summary>Per-engine snapshot for the current stage (one row per engine part).</summary>
    public IEnumerable<EngineReadout> GetEngineReadouts(double ambientPressure) =>
        ActiveEngines.Select(e => new EngineReadout(
            e.InstanceId,
            e.Definition.Name,
            e.ThrottleLevel,
            e.GetThrustMagnitude(ambientPressure),
            e.GetMassFlow(ambientPressure)));

    /// <summary>
    /// Ideal rocket-equation Δv (m/s) for a stage burning from <paramref name="wetMass"/> to
    /// <paramref name="dryMass"/> at the current effective Isp: Δv = Isp·g₀·ln(m0/m1).
    /// Returns 0 if masses or Isp are non-physical.
    /// </summary>
    public double GetStageDeltaV(double wetMass, double dryMass, double ambientPressure)
    {
        double isp = GetCurrentIsp(ambientPressure);
        if (isp <= 1.0 || dryMass <= 0.0 || wetMass <= dryMass) return 0.0;
        return isp * 9.80665 * System.Math.Log(wetMass / dryMass);
    }

    /// <summary>
    /// Δv (m/s) of the CURRENT stage as currently loaded: wet = sum of current-stage part
    /// masses, dry = wet minus the propellant the current-stage engines can actually draw.
    /// Uses the stage's current effective Isp. Convenience wrapper over GetStageDeltaV.
    /// </summary>
    public double GetCurrentStageDeltaV(double ambientPressure)
    {
        var stage = CurrentStageParts();
        // The active booster accelerates every still-attached stage above it. Its rocket-
        // equation mass ratio must therefore include the complete vehicle as carried mass.
        double wet = TotalMass;
        double propellant = stage.Sum(p =>
            p.LiquidFuel + p.Oxidizer + p.SolidFuel + p.Monopropellant);
        double dry = wet - propellant;
        return GetStageDeltaV(wet, dry, ambientPressure);
    }

    /// <summary>
    /// Snaps every firing engine in the current stage UP to its documented minimum throttle
    /// (Raptor 2 ≈ 40 %). Opt-in: ascent and EDL call this so a too-low command never requests
    /// a sub-floor burn; EDL selects fewer engines when it needs lower total thrust. Engines
    /// commanded to ~0 stay off. Returns the floored value applied to the
    /// first engine (or the input if there is none) so a caller can keep Vessel.Throttle in sync.
    /// </summary>
    public double ClampAscentThrottle()
    {
        double applied = 0.0; bool any = false;
        foreach (var e in ActiveEngines)
        {
            e.ThrottleLevel = e.ApplyThrottleFloor(e.ThrottleLevel);
            if (!any) { applied = e.ThrottleLevel; any = true; }
        }
        return applied;
    }

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
        double methaneRate = 0, oxidizerRate = 0;
        foreach (var engine in engines)
        {
            var def = engine.Definition;
            // Isp corregido por presión. No se recorta en 1 atm: mundos densos pueden
            // reducir el rendimiento hasta impedir que el motor produzca empuje neto.
            double pf  = System.Math.Max(0.0, ambientPressure / 101325.0);
            double isp = System.Math.Max(0.0, def.IspVac + (def.IspSL - def.IspVac) * pf);
            if (isp < 1.0) continue;

            // ṁ = F(p)/(Isp·g₀) con el empuje corregido por presión (coherente con
            // GetThrustMagnitude), no el empuje de vacío bruto.
            double massFlow = engine.GetThrustMagnitude(ambientPressure) / (isp * 9.80665);
            var fuelType = def.FuelTypeStr.ToLowerInvariant();

            if (fuelType.Contains("liquidfuel") || fuelType.Contains("liquid_fuel"))
            {
                totalLiquidRate += massFlow;
                if (def.MixtureRatio > 0.0)
                {
                    double fuelFraction = 1.0 / (1.0 + def.MixtureRatio);
                    methaneRate += massFlow * fuelFraction;
                    oxidizerRate += massFlow * (1.0 - fuelFraction);
                }
            }
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
            // Prefer the engine's declared oxidizer/fuel ratio. Falling back to the loaded
            // tank ratio preserves compatibility with old parts that do not declare one.
            double declaredRate = methaneRate + oxidizerRate;
            double inv = totalLF + totalOx;
            double lfFrac = declaredRate > 1e-9
                ? methaneRate / declaredRate
                : inv > 1e-9 ? totalLF / inv : 0.45;
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
