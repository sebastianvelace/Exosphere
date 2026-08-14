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
    private readonly List<Part> _tickStageParts = new();
    private readonly List<Part> _tickActiveEngines = new();
    private readonly List<Part> _queryStageParts = new();
    private readonly List<Part> _queryActiveEngines = new();
    private readonly List<Part> _tickEngineScratch = new();
    private readonly List<Part> _tickSubtreeScratch = new();
    private readonly List<LiquidEngineDemand> _liquidDemands = new();
    private readonly List<(EngineInstanceState State, Vector3d Jx, Vector3d Jz)>
        _gimbalContributions = new();
    private readonly Dictionary<Part, Vector3d> _partLocalPositions = new();
    private Part? _root;
    private bool _partLocalPositionsValid;
    private bool _physicsTickActive;
    private bool _tickStageCacheValid;
    private bool _tickActiveEngineCacheValid;
    private bool _tickMassPropertiesValid;
    private double _tickTotalMass;
    private Vector3d _tickCenterOfMass;
    private double _tickTransverseMomentOfInertia;
    private double _tickAxialMomentOfInertia;

    public IReadOnlyList<Part>  Parts  => _parts.AsReadOnly();
    public IReadOnlyList<Joint> Joints => _joints.AsReadOnly();
    public Part? Root => _root;

    /// <summary>
    /// Enables the short-lived physics-tick caches. They are intentionally scoped to one
    /// <see cref="Vessel.Tick"/> so resource consumption and staging remain authoritative
    /// outside the cache window.
    /// </summary>
    internal void BeginPhysicsTick()
    {
        _physicsTickActive = true;
        _tickStageCacheValid = false;
        _tickActiveEngineCacheValid = false;
        _tickMassPropertiesValid = false;
        _partLocalPositionsValid = false;
    }

    internal void EndPhysicsTick()
    {
        _physicsTickActive = false;
        _tickStageCacheValid = false;
        _tickActiveEngineCacheValid = false;
        _tickMassPropertiesValid = false;
        _partLocalPositionsValid = false;
    }

    internal void InvalidateTickActiveEngineCache() => _tickActiveEngineCacheValid = false;

    private void InvalidateTopologyCaches()
    {
        _partLocalPositionsValid = false;
        _tickStageCacheValid = false;
        _tickActiveEngineCacheValid = false;
        _tickMassPropertiesValid = false;
    }

    /// <summary>
    /// When true, upper-stage engines may fire and drain their own tanks while the booster
    /// stage is still attached (hot-stage overlap). Cleared automatically at mechanical stage.
    /// </summary>
    private bool _hotStageOverlapActive;

    public bool HotStageOverlapActive
    {
        get => _hotStageOverlapActive;
        set
        {
            if (_hotStageOverlapActive == value) return;
            _hotStageOverlapActive = value;
            _tickActiveEngineCacheValid = false;
        }
    }

    public void SetRoot(Part part)
    {
        _root = part;
        if (!_parts.Contains(part)) _parts.Add(part);
        InvalidateTopologyCaches();
    }

    public void AddPart(Part part)
    {
        if (!_parts.Contains(part))
        {
            _parts.Add(part);
            InvalidateTopologyCaches();
        }
    }

    public void AddJoint(Joint joint)
    {
        _joints.Add(joint);
        if (!_parts.Contains(joint.Parent)) _parts.Add(joint.Parent);
        if (!_parts.Contains(joint.Child))  _parts.Add(joint.Child);
        InvalidateTopologyCaches();
    }

    public IEnumerable<Part>  GetChildren(Part parent) =>
        _joints.Where(j => j.Parent == parent).Select(j => j.Child);
    public Joint? GetJoint(Part parent, Part child) =>
        _joints.FirstOrDefault(j => j.Parent == parent && j.Child == child);

    // ── Propiedades calculadas ────────────────────────────────────────────
    public double TotalMass
    {
        get
        {
            if (_physicsTickActive)
            {
                EnsureTickMassProperties();
                return _tickTotalMass;
            }
            return _parts.Sum(p => p.CurrentMass);
        }
    }
    public double DryMass          => _parts.Sum(p => p.EffectiveMassDry);
    public double TotalLiquidFuel  => _parts.Sum(p => p.LiquidFuel);
    public double TotalOxidizer    => _parts.Sum(p => p.Oxidizer);
    public double TotalElectricCharge => _parts.Sum(p => p.ElectricCharge);
    public double VehicleLength
    {
        get
        {
            double specified = 0.0;
            foreach (var part in _parts)
                specified += System.Math.Max(0.0, part.Definition.LengthM);
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
            double specified = 0.0;
            foreach (var part in _parts)
                specified = System.Math.Max(specified, part.Definition.DiameterM);
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
            double declared = 0.0;
            bool found = false;
            foreach (var part in _parts)
            {
                double radius = part.Definition.NoseRadiusM;
                if (radius > 0.0 && (!found || radius < declared))
                {
                    declared = radius;
                    found = true;
                }
            }
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
            double totalLength = 0.0;
            double weightedCoefficient = 0.0;
            foreach (var part in _parts)
            {
                if (part.Definition.AxialDragCoefficient <= 0.0) continue;
                double length = System.Math.Max(0.01, part.Definition.LengthM);
                totalLength += length;
                weightedCoefficient += part.Definition.AxialDragCoefficient * length;
            }
            return totalLength > 0.0 ? weightedCoefficient / totalLength : 0.6;
        }
    }

    private void EnsureTickStageCache()
    {
        if (_tickStageCacheValid) return;
        BuildCurrentStageParts(_tickStageParts);
        _tickStageCacheValid = true;
        _tickActiveEngineCacheValid = false;
    }

    private void EnsureTickActiveEngineCache()
    {
        if (_tickActiveEngineCacheValid) return;
        _tickActiveEngines.Clear();
        if (HotStageOverlapActive)
        {
            for (int i = 0; i < _parts.Count; i++)
            {
                var part = _parts[i];
                if (part.Definition.Category == PartCategory.Engine
                    && part.IsStagingActive && !part.IsBroken)
                    _tickActiveEngines.Add(part);
            }
        }
        else
        {
            EnsureTickStageCache();
            for (int i = 0; i < _tickStageParts.Count; i++)
            {
                var part = _tickStageParts[i];
                if (part.Definition.Category == PartCategory.Engine
                    && part.IsStagingActive && !part.IsBroken)
                    _tickActiveEngines.Add(part);
            }
        }
        _tickActiveEngineCacheValid = true;
    }

    private void EnsureTickMassProperties()
    {
        if (_tickMassPropertiesValid) return;

        _tickTotalMass = 0.0;
        _tickCenterOfMass = Vector3d.Zero;
        if (_root != null)
        {
            var positions = GetCachedPartLocalPositions();
            for (int i = 0; i < _parts.Count; i++)
            {
                var part = _parts[i];
                double mass = part.CurrentMass;
                _tickTotalMass += mass;
                if (positions.TryGetValue(part, out var position))
                    _tickCenterOfMass += position * mass;
            }
            if (_tickTotalMass > 0.0)
                _tickCenterOfMass /= _tickTotalMass;
        }

        double radius = MaximumDiameter * 0.5;
        _tickTransverseMomentOfInertia = _tickTotalMass
            * (3.0 * radius * radius + VehicleLength * VehicleLength) / 12.0;
        _tickAxialMomentOfInertia = 0.5 * _tickTotalMass * radius * radius;
        _tickMassPropertiesValid = true;
    }

    private Dictionary<Part, Vector3d> GetCachedPartLocalPositions()
    {
        if (!_physicsTickActive)
            return ComputePartLocalPositions();
        if (_partLocalPositionsValid)
            return _partLocalPositions;

        _partLocalPositions.Clear();
        if (_root != null)
            AssignPositions(_root, Vector3d.Zero, _partLocalPositions);
        _partLocalPositionsValid = true;
        return _partLocalPositions;
    }

    // Parts belonging to the currently-firing stage: the subtree hanging below the
    // lowest still-attached decoupler (the side away from the root command section).
    // With no active decoupler the whole vessel is one stage. Engines only fire — and
    // only draw propellant — within this set, so a multi-stage stack burns its bottom
    // stage first and the upper stage stays fuelled until separation (real rockets do
    // not cross-feed across stage interfaces, nor light all stages at liftoff).
    public List<Part> CurrentStageParts()
    {
        if (_physicsTickActive)
        {
            EnsureTickStageCache();
            return _tickStageParts;
        }

        var result = new List<Part>(_parts.Count);
        BuildCurrentStageParts(result);
        return result;
    }

    private void BuildCurrentStageParts(List<Part> result)
    {
        result.Clear();
        var activeDecouplers = _parts
            .Where(p => p.Definition.Category == PartCategory.Decoupler && p.IsStagingActive)
            .ToList();
        if (activeDecouplers.Count == 0)
        {
            result.AddRange(_parts);
            return;
        }

        foreach (var d in activeDecouplers)
        {
            var child = GetChildren(d).FirstOrDefault();
            if (child == null) continue;
            var farSide = CollectSubtree(child);   // subtree below the decoupler
            // The bottom stage's subtree contains no further attached decoupler.
            if (!farSide.Any(p => p.Definition.Category == PartCategory.Decoupler && p.IsStagingActive))
            {
                result.AddRange(farSide);
                return;
            }
        }
        result.AddRange(_parts);
    }

    public IEnumerable<Part> ActiveEngines
    {
        get
        {
            if (_physicsTickActive)
            {
                EnsureTickActiveEngineCache();
                return _tickActiveEngines;
            }

            return GetQueryActiveEngineList();
        }
    }

    private List<Part> GetQueryActiveEngineList()
    {
        _queryActiveEngines.Clear();
        if (HotStageOverlapActive)
        {
            for (int i = 0; i < _parts.Count; i++)
            {
                var part = _parts[i];
                if (part.Definition.Category == PartCategory.Engine
                    && part.IsStagingActive && !part.IsBroken)
                    _queryActiveEngines.Add(part);
            }
            return _queryActiveEngines;
        }

        _queryStageParts.Clear();
        BuildCurrentStageParts(_queryStageParts);
        for (int i = 0; i < _queryStageParts.Count; i++)
        {
            var part = _queryStageParts[i];
            if (part.Definition.Category == PartCategory.Engine
                && part.IsStagingActive && !part.IsBroken)
                _queryActiveEngines.Add(part);
        }
        return _queryActiveEngines;
    }

    // Centro de masa en espacio local del vessel (+Y = arriba, raíz en Y=0)
    public Vector3d CenterOfMass
    {
        get
        {
            if (_physicsTickActive)
            {
                EnsureTickMassProperties();
                return _tickCenterOfMass;
            }

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
            if (_physicsTickActive)
            {
                EnsureTickMassProperties();
                return _tickTransverseMomentOfInertia;
            }
            double radius = MaximumDiameter * 0.5;
            return TotalMass * (3.0 * radius * radius + VehicleLength * VehicleLength) / 12.0;
        }
    }

    /// <summary>Approximate roll-axis inertia (kg·m²) of the vehicle envelope.</summary>
    public double AxialMomentOfInertia
    {
        get
        {
            if (_physicsTickActive)
            {
                EnsureTickMassProperties();
                return _tickAxialMomentOfInertia;
            }
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

        var positions = GetCachedPartLocalPositions();
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

    /// <summary>
    /// R5b — pitch/yaw and roll angular-acceleration envelope actually deliverable by
    /// <see cref="SolveDifferentialGimbal"/>: unlike <see cref="GetPitchYawAngularAcceleration(double)"/>
    /// and <see cref="GetRollAngularAcceleration(double)"/> (which scale every active engine's
    /// full thrust by its part-wide <c>GimbalRange</c>, regardless of whether that specific
    /// mount can gimbal at all — e.g. Super Heavy's 20 ungimballed outer-ring Raptors), this
    /// sums only over <see cref="Part.GetEngineInstanceGimbalAuthority"/>'s live, selected,
    /// gimballed instances, using each one's own real lever arm (the same mount geometry
    /// <see cref="SolveDifferentialGimbal"/> and <see cref="GetTotalTorque"/> read). Sizing the
    /// differential-TVC target from this instead of the legacy scalar estimate keeps the
    /// commanded torque within what the allocator can actually deliver, instead of chronically
    /// over-demanding it into full saturation.
    /// </summary>
    public Vector3d GetDifferentialTVCAngularAccelerationEnvelope(double ambientPressure)
    {
        double transverse = TransverseMomentOfInertia;
        double axial = AxialMomentOfInertia;
        if (transverse <= 0.0 && axial <= 0.0) return Vector3d.Zero;

        var positions = GetCachedPartLocalPositions();
        double comY = CenterOfMass.Y;
        double rollRadius = MaximumDiameter * 0.5 * 0.65;
        double pitchYawTorque = 0.0;
        double rollTorque = 0.0;

        foreach (var engine in ActiveEngines)
        {
            if (!positions.TryGetValue(engine, out var partPosition)) continue;
            bool countsForRoll = engine.Definition.EngineCount >= 2;
            var authority = engine.GetEngineInstanceGimbalAuthoritySnapshot(ambientPressure);
            for (int i = 0; i < authority.Count; i++)
            {
                var (_, mountPosition, thrustN, gimbalRangeDeg) = authority[i];
                double gimbalRad = gimbalRangeDeg * MathUtils.DEG_TO_RAD;
                double lever = System.Math.Abs(partPosition.Y + mountPosition.Y - comY);
                pitchYawTorque += thrustN * lever * System.Math.Sin(gimbalRad);
                if (countsForRoll)
                    rollTorque += thrustN * rollRadius * System.Math.Sin(gimbalRad);
            }
        }

        double pitchYaw = transverse > 0.0 ? pitchYawTorque / transverse : 0.0;
        double roll = axial > 0.0 ? rollTorque / axial : 0.0;
        return new Vector3d(pitchYaw, roll, pitchYaw);
    }

    // ── Empuje total en espacio local ─────────────────────────────────────
    // Overload sin presión: empuje de vacío (compatibilidad).
    public Vector3d GetTotalThrust() => GetTotalThrust(0.0);

    // Empuje total corregido por presión ambiente (Pa).
    public Vector3d GetTotalThrust(double ambientPressure)
    {
        var thrust = Vector3d.Zero;
        foreach (var engine in ActiveEngines)
            thrust += engine.GetThrustVector(ambientPressure);
        return thrust;
    }

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
        var positions = GetCachedPartLocalPositions();
        var com = CenterOfMass;
        var torque = Vector3d.Zero;
        foreach (var engine in ActiveEngines)
        {
            if (!positions.TryGetValue(engine, out var partPosition)) continue;
            var geometry = engine.GetEngineInstanceThrustGeometrySnapshot(ambientPressure);
            for (int i = 0; i < geometry.Count; i++)
            {
                var (mountPosition, thrustVector) = geometry[i];
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

    /// <summary>
    /// R5b — differential per-mount TVC. Commands each live, gimballed engine instance's own
    /// normalized gimbal (X,Z ∈ [-1,1]) so the vessel's real thrust cluster targets
    /// <paramref name="desiredTorque"/> (N·m, pitch=X/roll=Y/yaw=Z about the CoM) instead of
    /// every mount mirroring one shared command
    /// (the pre-R5b behaviour <see cref="Vessel"/> still falls back to when this produces no
    /// contributions). Every engine's share is proportional to its own lever-arm/thrust
    /// authority: for each instance, linearize its lateral-force Jacobian at zero deflection —
    /// <c>J_x = r × (F₀·range_rad·X̂)</c>, <c>J_z = r × (F₀·range_rad·Ẑ)</c>, where r is the
    /// mount's position relative to the CoM (same geometry <see cref="GetTotalTorque"/> reads)
    /// — then solve the minimum-norm least-squares command via the 3×3 Gramian
    /// <c>M = Σ(J_xJ_xᵀ + J_zJ_zᵀ)</c>: <c>Mλ = desiredTorque</c>, <c>g_i = (J_x·λ, J_z·λ)</c>.
    /// This is the standard closed-form Moore-Penrose control-allocation solve — proportional
    /// distribution by authority, not a full constrained optimal-control redistribution after
    /// clamping (each instance's command is independently clamped to [-1,1] at the end, with
    /// no rebalancing of the others). A Tikhonov term keeps the Gramian solve stable when the
    /// cluster's geometry is degenerate (e.g. a single engine, or two engines coincident on an
    /// axis) instead of producing NaN/exploding commands.
    /// </summary>
    public void SolveDifferentialGimbal(Vector3d desiredTorque, double ambientPressure)
    {
        var positions = GetCachedPartLocalPositions();
        var com = CenterOfMass;

        var contributions = _physicsTickActive
            ? _gimbalContributions
            : new List<(EngineInstanceState State, Vector3d Jx, Vector3d Jz)>();
        contributions.Clear();
        double m00 = 0, m01 = 0, m02 = 0, m11 = 0, m12 = 0, m22 = 0;

        foreach (var engine in ActiveEngines)
        {
            if (!positions.TryGetValue(engine, out var partPosition)) continue;
            var authorityRows = engine.GetEngineInstanceGimbalAuthoritySnapshot(ambientPressure);
            for (int i = 0; i < authorityRows.Count; i++)
            {
                var (state, mountPosition, thrustN, gimbalRangeDeg) = authorityRows[i];
                var r = partPosition + mountPosition - com;
                double mountAuthority = thrustN * (gimbalRangeDeg * MathUtils.DEG_TO_RAD);
                var jx = r.Cross(new Vector3d(mountAuthority, 0.0, 0.0));
                var jz = r.Cross(new Vector3d(0.0, 0.0, mountAuthority));
                contributions.Add((state, jx, jz));

                m00 += jx.X * jx.X + jz.X * jz.X;
                m01 += jx.X * jx.Y + jz.X * jz.Y;
                m02 += jx.X * jx.Z + jz.X * jz.Z;
                m11 += jx.Y * jx.Y + jz.Y * jz.Y;
                m12 += jx.Y * jx.Z + jz.Y * jz.Z;
                m22 += jx.Z * jx.Z + jz.Z * jz.Z;
            }
        }

        if (contributions.Count == 0) return;

        // Tikhonov regularization: without it, a Gramian built from fewer than three
        // independent lever-arm directions (e.g. one engine, or engines coincident on an
        // axis) is singular and unsolvable. εI keeps the solve well-posed and damps the
        // allocation toward zero for axes the cluster genuinely cannot influence, instead of
        // failing outright.
        double epsilon = 1e-6 * System.Math.Max(1.0, m00 + m11 + m22);
        var lambda = SolveSymmetric3x3(
            m00 + epsilon, m01, m02,
            m11 + epsilon, m12,
            m22 + epsilon,
            desiredTorque);

        foreach (var (state, jx, jz) in contributions)
        {
            state.GimbalCommandOverride = new Vector3d(
                System.Math.Clamp(jx.Dot(lambda), -1.0, 1.0),
                0.0,
                System.Math.Clamp(jz.Dot(lambda), -1.0, 1.0));
        }
    }

    /// <summary>
    /// Solves the symmetric 3×3 system [[a,b,c],[b,d,e],[c,e,f]]·x = rhs via Cramer's rule.
    /// Returns zero for a (near-)singular matrix rather than dividing by ~0 — the caller
    /// already adds a Tikhonov term, so this only guards residual numerical degeneracy.
    /// </summary>
    private static Vector3d SolveSymmetric3x3(
        double a, double b, double c, double d, double e, double f, Vector3d rhs)
    {
        double det = a * (d * f - e * e) - b * (b * f - e * c) + c * (b * e - d * c);
        if (System.Math.Abs(det) < 1e-15) return Vector3d.Zero;

        double rx = rhs.X, ry = rhs.Y, rz = rhs.Z;
        double x = (rx * (d * f - e * e) - b * (ry * f - e * rz) + c * (ry * e - d * rz)) / det;
        double y = (a * (ry * f - e * rz) - rx * (b * f - e * c) + c * (b * rz - ry * c)) / det;
        double z = (a * (d * rz - ry * e) - b * (b * rz - ry * c) + rx * (b * e - d * c)) / det;
        return new Vector3d(x, y, z);
    }

    // ── Read-only telemetry getters (consumed by the HUD) ─────────────────
    // These never mutate the sim; they report what the engines of the CURRENT stage are
    // doing at the given ambient pressure (Pa). Pass the live atmospheric pressure to get
    // pressure-corrected figures, or 0 for the vacuum case. The HUD must not have to touch
    // Part internals or duplicate the thrust equation — it just reads these.

    /// <summary>Number of engines in the current stage that are lit (firing).</summary>
    public int ActiveEngineCount
    {
        get
        {
            int count = 0;
            foreach (var engine in ActiveEngines)
            {
                if (!engine.HasEngineRuntime)
                {
                    if (engine.ThrottleLevel > 1e-3)
                        count += engine.SelectedEngineCount;
                    continue;
                }

                foreach (var state in engine.EngineStates)
                    if (state.ChamberPressureFraction > 1e-3)
                        count++;
            }
            return count;
        }
    }

    /// <summary>Total pressure-corrected thrust magnitude (N) of the current stage now.</summary>
    public double GetCurrentThrust(double ambientPressure)
    {
        double thrust = 0.0;
        foreach (var engine in ActiveEngines)
            thrust += engine.GetThrustMagnitude(ambientPressure);
        return thrust;
    }

    /// <summary>Pressure-corrected current-stage thrust available at full throttle.</summary>
    public double GetMaximumThrust(double ambientPressure)
    {
        double thrust = 0.0;
        foreach (var engine in ActiveEngines)
            thrust += engine.GetFullThrottleThrustMagnitude(ambientPressure);
        return thrust;
    }

    /// <summary>Total propellant mass flow of the current stage (kg/s) at this pressure.</summary>
    public double GetCurrentMassFlow(double ambientPressure)
    {
        double massFlow = 0.0;
        foreach (var engine in ActiveEngines)
            massFlow += engine.GetMassFlow(ambientPressure);
        return massFlow;
    }

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

    /// <summary>
    /// Per-engine snapshot for the current stage (one row per engine part).
    /// This compatibility enumerable remains convenient for simulation callers, while the
    /// presentation layer should prefer <see cref="FillEngineReadouts"/> to reuse its buffer.
    /// </summary>
    public IEnumerable<EngineReadout> GetEngineReadouts(double ambientPressure)
    {
        foreach (var engine in ActiveEngines)
        {
            if (engine.HasEngineRuntime)
            {
                foreach (var telemetry in engine.GetEngineTelemetry(ambientPressure))
                {
                    yield return new EngineReadout(
                        telemetry.InstanceId,
                        engine.Definition.Name,
                        telemetry.ChamberPressureFraction,
                        telemetry.ThrustN,
                        telemetry.MassFlowKgS,
                        telemetry.State,
                        telemetry.FailureCode);
                }

                continue;
            }

            yield return BuildStaticEngineReadout(engine, ambientPressure);
        }
    }

    /// <summary>
    /// Fills a caller-owned buffer with the current-stage engine telemetry.
    /// The buffer is cleared and never replaced, allowing HUD/render consumers to sample
    /// telemetry without allocating a list or copying the compatibility enumerable.
    /// </summary>
    public void FillEngineReadouts(double ambientPressure, List<EngineReadout> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();

        foreach (var engine in ActiveEngines)
        {
            if (engine.HasEngineRuntime)
            {
                foreach (var telemetry in engine.GetEngineTelemetry(ambientPressure))
                {
                    destination.Add(new EngineReadout(
                        telemetry.InstanceId,
                        engine.Definition.Name,
                        telemetry.ChamberPressureFraction,
                        telemetry.ThrustN,
                        telemetry.MassFlowKgS,
                        telemetry.State,
                        telemetry.FailureCode));
                }

                continue;
            }

            destination.Add(BuildStaticEngineReadout(engine, ambientPressure));
        }
    }

    private static EngineReadout BuildStaticEngineReadout(Part engine, double ambientPressure) =>
        new(
            engine.InstanceId,
            engine.Definition.Name,
            engine.ThrottleLevel,
            engine.GetThrustMagnitude(ambientPressure),
            engine.GetMassFlow(ambientPressure),
            engine.ThrottleLevel > 1e-3
                ? EngineLifecycleState.Running
                : EngineLifecycleState.Off,
            engine.IsBroken ? "PART_BROKEN" : null);

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

    private void ConsumePropellantFromPool(
        IReadOnlyList<Part> enginePool,
        IReadOnlyList<Part> tankPool,
        double dt,
        double ambientPressure)
    {
        var engines = _physicsTickActive ? _tickEngineScratch : new List<Part>();
        engines.Clear();
        for (int i = 0; i < enginePool.Count; i++)
        {
            var part = enginePool[i];
            if (part.Definition.Category == PartCategory.Engine
                && part.IsStagingActive && !part.IsBroken)
                engines.Add(part);
        }
        if (engines.Count == 0) return;

        // Calcular flujo de masa total de todos los motores activos
        double totalSolidRate = 0, totalMonoRate = 0;
        _liquidDemands.Clear();
        foreach (var engine in engines)
        {
            var def = engine.Definition;
            double pf  = System.Math.Max(0.0, ambientPressure / 101325.0);
            double isp = System.Math.Max(0.0, def.IspVac + (def.IspSL - def.IspVac) * pf);
            if (isp < 1.0) continue;

            // ṁ = F(p)/(Isp·g₀) con el empuje corregido por presión (coherente con
            // GetThrustMagnitude), no el empuje de vacío bruto.
            var fuelType = def.FuelTypeStr;

            if (fuelType.Contains("liquidfuel", StringComparison.OrdinalIgnoreCase)
                || fuelType.Contains("liquid_fuel", StringComparison.OrdinalIgnoreCase))
            {
                if (engine.HasEngineRuntime)
                {
                    int engineIndex = 0;
                    foreach (var row in engine.GetEngineTelemetry(ambientPressure))
                    {
                        if (row.MassFlowKgS <= 1e-12)
                        {
                            engineIndex++;
                            continue;
                        }
                        if (row.MassFlowKgS
                            > engine.GetEngineFeedLimitKgS(engineIndex) + 1e-9)
                        {
                            engine.FailEngine(
                                row.InstanceId, "FEED_BRANCH_FLOW_LIMIT");
                            _tickActiveEngineCacheValid = false;
                            engineIndex++;
                            continue;
                        }
                        _liquidDemands.Add(new LiquidEngineDemand(
                            engine,
                            row.InstanceId,
                            row.MassFlowKgS,
                            def.MixtureRatio));
                        engineIndex++;
                    }
                }
                else
                    _liquidDemands.Add(new LiquidEngineDemand(
                        engine,
                        engine.InstanceId,
                        engine.GetMassFlow(ambientPressure),
                        def.MixtureRatio));
            }
            else if (fuelType.Contains("solid", StringComparison.OrdinalIgnoreCase))
                totalSolidRate += engine.GetMassFlow(ambientPressure);
            else if (fuelType.Contains("mono", StringComparison.OrdinalIgnoreCase))
                totalMonoRate += engine.GetMassFlow(ambientPressure);
        }

        // Consumir de los tanques de la etapa activa (cross-feed dentro de la etapa)
        bool nonLiquidFlameOut = false;

        if (_liquidDemands.Count > 0)
        {
            double totalLF = 0.0;
            double totalOx = 0.0;
            for (int i = 0; i < tankPool.Count; i++)
            {
                totalLF += tankPool[i].LiquidFuel;
                totalOx += tankPool[i].Oxidizer;
            }
            double remainingLF = totalLF;
            double remainingOx = totalOx;
            double fundedLF = 0.0;
            double fundedOx = 0.0;
            double loadedTotal = totalLF + totalOx;
            double fallbackFuelFraction = loadedTotal > 1e-9
                ? totalLF / loadedTotal
                : 0.45;

            _liquidDemands.Sort(static (left, right) =>
                StringComparer.Ordinal.Compare(
                    left.EngineInstanceId, right.EngineInstanceId));
            foreach (var demand in _liquidDemands)
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
                {
                    demand.EnginePart.FailEngine(
                        demand.EngineInstanceId, "PROPELLANT_STARVATION");
                    _tickActiveEngineCacheValid = false;
                }
                else
                {
                    demand.EnginePart.IsStagingActive = false;
                    _tickActiveEngineCacheValid = false;
                }
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
            double totalSolid = 0.0;
            for (int i = 0; i < tankPool.Count; i++)
                totalSolid += tankPool[i].SolidFuel;
            if (totalSolid < solidNeeded) nonLiquidFlameOut = true;
            else
            {
                for (int i = 0; i < tankPool.Count; i++)
                {
                    var part = tankPool[i];
                    if (part.SolidFuel > 0)
                        part.SolidFuel -= solidNeeded * (part.SolidFuel / totalSolid);
                }
            }
        }

        if (totalMonoRate > 0)
        {
            double monoNeeded = totalMonoRate * dt;
            double totalMono = 0.0;
            for (int i = 0; i < tankPool.Count; i++)
                totalMono += tankPool[i].Monopropellant;
            if (totalMono < monoNeeded) nonLiquidFlameOut = true;
            else
            {
                for (int i = 0; i < tankPool.Count; i++)
                {
                    var part = tankPool[i];
                    if (part.Monopropellant > 0)
                        part.Monopropellant -= monoNeeded * (part.Monopropellant / totalMono);
                }
            }
        }

        if (nonLiquidFlameOut)
        {
            foreach (var engine in engines)
            {
                if (engine.HasEngineRuntime)
                    engine.FailAllEngines("PROPELLANT_STARVATION");
                else
                    engine.IsStagingActive = false;
                _tickActiveEngineCacheValid = false;
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
        InvalidateTopologyCaches();

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
            InvalidateTopologyCaches();
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
        InvalidateTopologyCaches();
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
