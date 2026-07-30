namespace Exosphere.Simulation.Parts;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Propulsion;

/// <summary>
/// Immutable per-engine telemetry row for the HUD. Thrust is in newtons (pressure-corrected),
/// MassFlow in kg/s, Throttle in [0,1]. Built by <see cref="PartGraph.GetEngineReadouts"/>.
/// </summary>
public readonly record struct EngineReadout(
    string InstanceId,
    string Name,
    double Throttle,
    double ThrustN,
    double MassFlowKgS,
    EngineLifecycleState State,
    string? FailureCode);

public class PartGraph
{
    private readonly record struct LiquidEngineDemand(
        Part EnginePart,
        string EngineInstanceId,
        double MassFlowKgS,
        double MixtureRatio);

    private readonly List<Part>  _parts  = new();
    private readonly List<Joint> _joints = new();
    private Part? _root;

    public IReadOnlyList<Part>  Parts  => _parts.AsReadOnly();
    public IReadOnlyList<Joint> Joints => _joints.AsReadOnly();
    public Part? Root => _root;

    /// <summary>
    /// When true, upper-stage engines may fire and drain their own tanks while the booster
    /// stage is still attached (hot-stage overlap). Cleared automatically at mechanical stage.
    /// </summary>
    public bool HotStageOverlapActive { get; set; }

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
    /// <summary>Forward stagnation radius of curvature (m); hull radius when undeclared.</summary>
    public double NoseRadius
    {
        get
        {
            double declared = _parts
                .Select(p => p.Definition.NoseRadiusM)
                .Where(r => r > 0.0)
                .DefaultIfEmpty(0.0)
                .Min();
            return declared > 0.0 ? declared : MaximumDiameter * 0.5;
        }
    }
    public double AxialDragCoefficient
    {
        get
        {
            // In an axial stack the exposed nose/root defines the stagnation geometry.
            // Averaging a blunt capsule hidden behind an escape tower into the launch Cd
            // made the complete rocket behave as though the capsule were exposed.
            if (_root?.Definition.AxialDragCoefficient > 0.0)
                return _root.Definition.AxialDragCoefficient;
            var declared = _parts
                .Where(p => p.Definition.AxialDragCoefficient > 0.0)
                .ToArray();
            if (declared.Length == 0) return 0.6;
            double totalLength = declared.Sum(
                p => System.Math.Max(0.01, p.Definition.LengthM));
            return declared.Sum(p =>
                    p.Definition.AxialDragCoefficient
                    * System.Math.Max(0.01, p.Definition.LengthM))
                / totalLength;
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
            // Hot-stage overlap deliberately lights both the booster and ship clusters while
            // the stack is still one graph. Outside that window only CurrentStageParts burn.
            IEnumerable<Part> pool = HotStageOverlapActive ? _parts : CurrentStageParts();
            return pool.Where(p => p.Definition.Category == PartCategory.Engine
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

    /// <summary>
    /// Genuine geometric torque (N·m) about the vessel's centre of mass: τ = Σ r_i × F_i
    /// over every live engine instance of every active engine part, where r_i is that
    /// instance's real 3D mount position (part-local position + mount offset) minus the
    /// current centre of mass, and F_i is its actual gimballed, pressure-corrected thrust
    /// vector. Unlike <see cref="GetPitchYawAngularAcceleration(double)"/> and
    /// <see cref="GetRollAngularAcceleration(double)"/> (which use a single scalar lever
    /// per part), this sums real per-engine moment arms, so an asymmetric engine failure or
    /// gimbal deflection produces genuine, correctly-signed torque instead of only reducing
    /// total thrust proportionally.
    /// </summary>
    public Vector3d GetTotalTorque(double ambientPressure)
    {
        var positions = ComputePartLocalPositions();
        var com = CenterOfMass;
        var torque = Vector3d.Zero;
        foreach (var engine in ActiveEngines)
        {
            if (!positions.TryGetValue(engine, out var partPosition)) continue;
            foreach (var (mountPosition, thrustVector) in
                     engine.GetEngineInstanceThrustGeometry(ambientPressure))
            {
                var r = partPosition + mountPosition - com;
                torque += r.Cross(thrustVector);
            }
        }
        return torque;
    }

    /// <summary>
    /// Genuine geometric angular acceleration (rad/s²) about all three vessel axes, derived
    /// from <see cref="GetTotalTorque(double)"/> divided component-wise by the appropriate
    /// moment of inertia: X/Z (pitch/yaw) by <see cref="TransverseMomentOfInertia"/>, Y
    /// (roll) by <see cref="AxialMomentOfInertia"/> — the same two inertia properties the
    /// existing scalar <see cref="GetPitchYawAngularAcceleration(double)"/> and
    /// <see cref="GetRollAngularAcceleration(double)"/> already use. Each component is 0
    /// when its inertia is non-positive.
    /// </summary>
    public Vector3d GetPitchYawRollAngularAcceleration(double ambientPressure)
    {
        var torque = GetTotalTorque(ambientPressure);
        double transverse = TransverseMomentOfInertia;
        double axial = AxialMomentOfInertia;
        double pitch = transverse > 0.0 ? torque.X / transverse : 0.0;
        double roll = axial > 0.0 ? torque.Y / axial : 0.0;
        double yaw = transverse > 0.0 ? torque.Z / transverse : 0.0;
        return new Vector3d(pitch, roll, yaw);
    }

    // ── Read-only telemetry getters (consumed by the HUD) ─────────────────
    // These never mutate the sim; they report what the engines of the CURRENT stage are
    // doing at the given ambient pressure (Pa). Pass the live atmospheric pressure to get
    // pressure-corrected figures, or 0 for the vacuum case. The HUD must not have to touch
    // Part internals or duplicate the thrust equation — it just reads these.

    /// <summary>Number of engines in the current stage that are lit (firing).</summary>
    public int ActiveEngineCount =>
        ActiveEngines
            .Sum(e => e.HasEngineRuntime
                ? e.EngineStates.Count(s => s.ChamberPressureFraction > 1e-3)
                : e.ThrottleLevel > 1e-3 ? e.SelectedEngineCount : 0);

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
        ActiveEngines.SelectMany(e => e.HasEngineRuntime
            ? e.GetEngineTelemetry(ambientPressure).Select(t => new EngineReadout(
                t.InstanceId,
                e.Definition.Name,
                t.ChamberPressureFraction,
                t.ThrustN,
                t.MassFlowKgS,
                t.State,
                t.FailureCode))
            : new[]
            {
                new EngineReadout(
                    e.InstanceId,
                    e.Definition.Name,
                    e.ThrottleLevel,
                    e.GetThrustMagnitude(ambientPressure),
                    e.GetMassFlow(ambientPressure),
                    e.ThrottleLevel > 1e-3
                        ? EngineLifecycleState.Running
                        : EngineLifecycleState.Off,
                    e.IsBroken ? "PART_BROKEN" : null),
            });

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
    // During hot-stage overlap both stage pools burn, each into its own tanks.
    public void ConsumePropellant(double dt, double ambientPressure)
    {
        if (HotStageOverlapActive)
        {
            var bottom = CurrentStageParts();
            var bottomSet = new HashSet<Part>(bottom);
            var upper = _parts.Where(p => !bottomSet.Contains(p)).ToList();
            ConsumePropellantFromPool(bottom, bottom, dt, ambientPressure);
            ConsumePropellantFromPool(upper, upper, dt, ambientPressure);
            return;
        }

        var stage = CurrentStageParts();
        ConsumePropellantFromPool(stage, stage, dt, ambientPressure);
    }

    private static void ConsumePropellantFromPool(
        IReadOnlyList<Part> enginePool,
        IReadOnlyList<Part> tankPool,
        double dt,
        double ambientPressure)
    {
        var engines = enginePool.Where(p => p.Definition.Category == PartCategory.Engine
                                         && p.IsStagingActive && !p.IsBroken).ToList();
        if (engines.Count == 0) return;

        // Calcular flujo de masa total de todos los motores activos
        double totalSolidRate = 0, totalMonoRate = 0;
        var liquidDemands = new List<LiquidEngineDemand>();
        foreach (var engine in engines)
        {
            var def = engine.Definition;
            double pf  = System.Math.Max(0.0, ambientPressure / 101325.0);
            double isp = System.Math.Max(0.0, def.IspVac + (def.IspSL - def.IspVac) * pf);
            if (isp < 1.0) continue;

            // ṁ = F(p)/(Isp·g₀) con el empuje corregido por presión (coherente con
            // GetThrustMagnitude), no el empuje de vacío bruto.
            var fuelType = def.FuelTypeStr.ToLowerInvariant();

            if (fuelType.Contains("liquidfuel") || fuelType.Contains("liquid_fuel"))
            {
                if (engine.HasEngineRuntime)
                {
                    var telemetry = engine.GetEngineTelemetry(ambientPressure).ToArray();
                    for (int i = 0; i < telemetry.Length; i++)
                    {
                        var row = telemetry[i];
                        if (row.MassFlowKgS <= 1e-12) continue;
                        if (row.MassFlowKgS
                            > engine.GetEngineFeedLimitKgS(i) + 1e-9)
                        {
                            engine.FailEngine(
                                row.InstanceId, "FEED_BRANCH_FLOW_LIMIT");
                            continue;
                        }
                        liquidDemands.Add(new LiquidEngineDemand(
                            engine,
                            row.InstanceId,
                            row.MassFlowKgS,
                            def.MixtureRatio));
                    }
                }
                else
                    liquidDemands.Add(new LiquidEngineDemand(
                        engine,
                        engine.InstanceId,
                        engine.GetMassFlow(ambientPressure),
                        def.MixtureRatio));
            }
            else if (fuelType.Contains("solid"))
                totalSolidRate += engine.GetMassFlow(ambientPressure);
            else if (fuelType.Contains("mono"))
                totalMonoRate += engine.GetMassFlow(ambientPressure);
        }

        // Consumir de los tanques de la etapa activa (cross-feed dentro de la etapa)
        bool nonLiquidFlameOut = false;

        if (liquidDemands.Count > 0)
        {
            double totalLF   = tankPool.Sum(p => p.LiquidFuel);
            double totalOx   = tankPool.Sum(p => p.Oxidizer);
            double remainingLF = totalLF;
            double remainingOx = totalOx;
            double fundedLF = 0.0;
            double fundedOx = 0.0;
            double loadedTotal = totalLF + totalOx;
            double fallbackFuelFraction = loadedTotal > 1e-9
                ? totalLF / loadedTotal
                : 0.45;

            foreach (var demand in liquidDemands
                         .OrderBy(item => item.EngineInstanceId, StringComparer.Ordinal))
            {
                double fuelFraction = demand.MixtureRatio > 0.0
                    ? 1.0 / (1.0 + demand.MixtureRatio)
                    : fallbackFuelFraction;
                double lfNeeded = demand.MassFlowKgS * fuelFraction * dt;
                double oxNeeded = demand.MassFlowKgS * (1.0 - fuelFraction) * dt;
                if (remainingLF + 1e-9 >= lfNeeded
                    && remainingOx + 1e-9 >= oxNeeded)
                {
                    remainingLF -= lfNeeded;
                    remainingOx -= oxNeeded;
                    fundedLF += lfNeeded;
                    fundedOx += oxNeeded;
                    continue;
                }

                if (demand.EnginePart.HasEngineRuntime)
                    demand.EnginePart.FailEngine(
                        demand.EngineInstanceId, "PROPELLANT_STARVATION");
                else
                    demand.EnginePart.IsStagingActive = false;
            }

            if (fundedLF > 0.0 || fundedOx > 0.0)
            {
                foreach (var p in tankPool)
                {
                    if (totalLF > 0.0)
                        p.LiquidFuel -= fundedLF * (p.LiquidFuel / totalLF);
                    if (totalOx > 0.0)
                        p.Oxidizer -= fundedOx * (p.Oxidizer / totalOx);
                }
            }
        }

        if (totalSolidRate > 0)
        {
            double solidNeeded = totalSolidRate * dt;
            double totalSolid  = tankPool.Sum(p => p.SolidFuel);
            if (totalSolid < solidNeeded) nonLiquidFlameOut = true;
            else foreach (var p in tankPool.Where(p2 => p2.SolidFuel > 0))
                p.SolidFuel -= solidNeeded * (p.SolidFuel / totalSolid);
        }

        if (totalMonoRate > 0)
        {
            double monoNeeded = totalMonoRate * dt;
            double totalMono  = tankPool.Sum(p => p.Monopropellant);
            if (totalMono < monoNeeded) nonLiquidFlameOut = true;
            else foreach (var p in tankPool.Where(p2 => p2.Monopropellant > 0))
                p.Monopropellant -= monoNeeded * (p.Monopropellant / totalMono);
        }

        if (nonLiquidFlameOut)
        {
            foreach (var engine in engines)
            {
                if (engine.HasEngineRuntime)
                    engine.FailAllEngines("PROPELLANT_STARVATION");
                else
                    engine.IsStagingActive = false;
            }
        }
    }

    // ── Staging: dispara el primer desacoplador disponible ────────────────
    // Retorna el PartGraph separado (la sección inferior), o null si nada
    public PartGraph? FireNextStage()
    {
        var decoupler = _parts
            .Where(p =>
                p.Definition.Category == PartCategory.Decoupler
                && p.IsStagingActive)
            .OrderByDescending(p => p.Definition.StagePriority)
            .FirstOrDefault();
        if (decoupler == null) return null;

        HotStageOverlapActive = false;
        decoupler.IsStagingActive = false;

        if (decoupler.Definition.DetachWithLowerStage)
        {
            var upperJoint = _joints.FirstOrDefault(j => j.Child == decoupler);
            if (upperJoint == null) return null;
            return DetachSubtree(decoupler, upperJoint);
        }

        // Buscamos primero el joint donde decoupler es Parent (separa lo que está DEBAJO).
        // Esto garantiza que SH se detache correctamente en stack command→tank→eng→decoupler→SH.
        var separationJoint = _joints.FirstOrDefault(j => j.Parent == decoupler)
            ?? _joints.FirstOrDefault(j => j.Child  == decoupler);
        if (separationJoint == null) return null;

        // El lado separado es el Child si decoupler es Parent, o el Parent si decoupler es Child.
        var separationRoot = separationJoint.Parent == decoupler
            ? separationJoint.Child
            : separationJoint.Parent;

        // FireNextStage may reverse-orient the decoupler joint; the structural path always
        // detaches Child. Reuse the same move once the detached root is known.
        return DetachSubtree(separationRoot, separationJoint);
    }

    /// <summary>
    /// Structural split: remove <paramref name="joint"/> and move its child subtree into a
    /// new graph. Returns null if the joint is not in this graph, if detaching would remove
    /// the root, or if the child side cannot form a valid subtree.
    /// </summary>
    public PartGraph? SplitAtJoint(Joint joint)
    {
        if (joint == null || !_joints.Contains(joint) || _root == null)
            return null;
        if (joint.Child == _root)
            return null;

        HotStageOverlapActive = false;
        return DetachSubtree(joint.Child, joint);
    }

    private PartGraph? DetachSubtree(Part separationRoot, Joint separationJoint)
    {
        var detachedParts = CollectSubtree(separationRoot);
        if (detachedParts.Count == 0 || (_root != null && detachedParts.Contains(_root)))
            return null;

        var detachedGraph = new PartGraph();
        detachedGraph.SetRoot(separationRoot);
        foreach (var p in detachedParts)
            detachedGraph.AddPart(p);

        foreach (var j in _joints.Where(j => detachedParts.Contains(j.Parent)).ToList())
        {
            detachedGraph.AddJoint(j);
            _joints.Remove(j);
        }
        _joints.Remove(separationJoint);
        foreach (var p in detachedParts) _parts.Remove(p);

        return detachedGraph;
    }

    /// <summary>
    /// Detaches an arbitrary non-root subtree, used for payload, capsule and rover
    /// deployment. Part objects and their stable IDs move to the new graph unchanged.
    /// </summary>
    public PartGraph? DetachSubtree(string rootInstanceId)
    {
        var separationRoot = _parts.FirstOrDefault(p => p.InstanceId == rootInstanceId);
        if (separationRoot == null) return null;
        if (separationRoot == _root)
        {
            var rootChildren = GetChildren(separationRoot).ToList();
            if (rootChildren.Count != 1) return null;
            var rootJoint = GetJoint(separationRoot, rootChildren[0]);
            if (rootJoint == null) return null;
            var detachedRoot = new PartGraph();
            detachedRoot.SetRoot(separationRoot);
            _joints.Remove(rootJoint);
            _parts.Remove(separationRoot);
            _root = rootChildren[0];
            return detachedRoot;
        }
        var separationJoint = _joints.FirstOrDefault(j => j.Child == separationRoot);
        if (separationJoint == null) return null;

        var detachedParts = CollectSubtree(separationRoot);
        var detachedGraph = new PartGraph();
        detachedGraph.SetRoot(separationRoot);
        foreach (var part in detachedParts) detachedGraph.AddPart(part);
        foreach (var joint in _joints
                     .Where(j => detachedParts.Contains(j.Parent) && detachedParts.Contains(j.Child))
                     .ToList())
        {
            detachedGraph.AddJoint(joint);
            _joints.Remove(joint);
        }
        _joints.Remove(separationJoint);
        foreach (var part in detachedParts) _parts.Remove(part);
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

    /// <summary>
    /// Resolves one attachment node into vessel-local coordinates using the same
    /// topology and dimensions that drive centre-of-mass and rendering.
    /// </summary>
    public bool TryGetAttachmentNodeLocalPosition(
        string partInstanceId,
        string nodeId,
        out Vector3d position)
    {
        position = Vector3d.Zero;
        var part = _parts.FirstOrDefault(candidate =>
            candidate.InstanceId == partInstanceId);
        if (part == null) return false;
        var node = part.Definition.AttachmentNodes.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, nodeId, StringComparison.Ordinal));
        if (node?.Position is not { Length: >= 3 }) return false;
        var partPositions = ComputePartLocalPositions();
        if (!partPositions.TryGetValue(part, out var partPosition))
            return false;
        position = partPosition + new Vector3d(
            node.Position[0], node.Position[1], node.Position[2]);
        return double.IsFinite(position.X)
            && double.IsFinite(position.Y)
            && double.IsFinite(position.Z);
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
