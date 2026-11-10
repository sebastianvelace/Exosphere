namespace Exosphere.Simulation.Parts;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;
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

/// <summary>
/// Aggregate values produced together with a per-engine telemetry fill. Keeping these
/// values beside the rows prevents presentation callers from rebuilding the active-stage
/// query and re-evaluating every engine several times for one HUD sample.
/// </summary>
public readonly record struct EngineTelemetrySummary(
    int NominalEngineCount,
    int ReadoutEngineCount,
    double ThrustN,
    double MassFlowKgS)
{
    public double EffectiveIspSeconds => MassFlowKgS > 1e-9
        ? ThrustN / (MassFlowKgS * 9.80665)
        : 0.0;
}

public partial class PartGraph
{
    private readonly record struct LiquidEngineDemand(
        Part EnginePart,
        string EngineInstanceId,
        double MassFlowKgS,
        double MixtureRatio);

    private readonly List<Part>  _parts  = new();
    private readonly List<Joint> _joints = new();
    private readonly IReadOnlyList<Part> _partsView;
    private readonly IReadOnlyList<Joint> _jointsView;
    private readonly List<Part> _tickStageParts = new();
    private readonly List<Part> _tickActiveEngines = new();
    private readonly List<Part> _queryStageParts = new();
    private readonly List<Part> _queryActiveEngines = new();
    private readonly List<Part> _stageSubtreeScratch = new();
    private readonly List<Part> _stageTraversalScratch = new();
    private readonly Queue<Part> _stageQueueScratch = new();
    private readonly List<Part> _engineReadoutCacheParts = new();
    private readonly List<EngineInstanceState?> _engineReadoutCacheStates = new();
    private readonly List<EngineReadout> _engineReadoutCacheRows = new();
    private readonly List<Part> _tickEngineScratch = new();
    private readonly List<Part> _tickSubtreeScratch = new();
    private readonly List<LiquidEngineDemand> _liquidDemands = new();
    private readonly record struct SingleResourceEngineDemand(Part Part, string EngineId,
        PropellantResourceKind Resource, double MassFlowKgPerS);
    private readonly List<SingleResourceEngineDemand> _singleResourceDemands = new();
    private readonly HashSet<Part> _hotStageBottomSet = new();
    private readonly List<Part> _hotStageUpperParts = new();
    private readonly List<(EngineInstanceState? State, Part Engine, Vector3d Jx, Vector3d Jz)>
        _gimbalContributions = new();
    private Part? _root;
    // Part positions depend only on topology and part geometry, not on mass, so this copy
    // survives across ticks and is rebuilt only by InvalidateTopologyCaches.
    private readonly Dictionary<Part, Vector3d> _topologyPartLocalPositions = new();
    private bool _topologyPartLocalPositionsValid;
    private double? _topologyVehicleLength;
    private bool _physicsTickActive;
    private bool _tickStageCacheValid;
    private bool _tickActiveEngineCacheValid;
    private bool _tickMassPropertiesValid;
    private bool _engineReadoutCacheValid;
    private double _engineReadoutCacheAmbientPressure;
    private EngineTelemetrySummary _engineReadoutCacheSummary;
    private double _tickTotalMass;
    private Vector3d _tickCenterOfMass;
    private double _tickTransverseMomentOfInertia;
    private double _tickAxialMomentOfInertia;
    private RigidBodyMassProperties _tickMassProperties;

    public PartGraph()
    {
        // A read-only facade is itself an allocation. Keep one stable view per graph so
        // physics/HUD queries do not create garbage merely by reading the topology.
        _partsView = _parts.AsReadOnly();
        _jointsView = _joints.AsReadOnly();
    }

    public IReadOnlyList<Part>  Parts  => _partsView;
    public IReadOnlyList<Joint> Joints => _jointsView;
    public Part? Root => _root;
    public double? AerodynamicCenterOffsetYM
    {
        get
        {
            if (_root?.Definition.StandaloneAerodynamicCenterOffsetYM is { } standalone) return standalone;
            foreach (var part in _parts)
                if (part.Definition.AerodynamicCenterOffsetYM is { } offset) return offset;
            return null;
        }
    }

    /// <summary>
    /// Blunt-capsule aerodynamics when the root is a trimmed capsule flying alone: trim angle
    /// (rad) and axial-force coefficient. Null for any stack, which keeps the slender-body model.
    /// </summary>
    public (double TrimAngleRad, double AxialForceCoefficient)? CapsuleAerodynamics =>
        _parts.Count == 1 && _root?.Definition.CapsuleTrimAngleOfAttackDeg is { } trimDeg
            ? (trimDeg * Math.MathUtils.DEG_TO_RAD,
                _root.Definition.CapsuleAxialForceCoefficient > 0.0 ? _root.Definition.CapsuleAxialForceCoefficient : 1.3)
            : null;

    /// <summary>
    /// Body axis the aerodynamic restoring moment aligns against the flow: local +Y, or for a
    /// trimmed capsule +Y tilted by the trim angle about local +X (the offset-CG equilibrium).
    /// </summary>
    public Math.Vector3d AerodynamicReferenceAxisLocal => CapsuleAerodynamics is { } capsule
        ? new Math.Vector3d(0.0, System.Math.Cos(capsule.TrimAngleRad), System.Math.Sin(capsule.TrimAngleRad))
        : Math.Vector3d.Up;

    /// <summary>Per-axis translation RCS thrust (N) of the intact parts, and its specific impulse (s).</summary>
    public (double ThrustN, double IspSeconds) RcsTranslation
    {
        get
        {
            // Force of the declared, enabled thrusters along their weakest translation axis,
            // and their thrust-weighted specific impulse.
            var thrusters = GetRcsThrusters();
            var authority = Physics.RcsAllocation.TranslationAuthority(thrusters);
            double weakest = System.Math.Min(authority.X, System.Math.Min(authority.Y, authority.Z));
            double thrust = 0.0, impulseWeight = 0.0;
            for (int i = 0; i < thrusters.Count; i++)
            {
                var thruster = thrusters[i];
                if (!thruster.Available) continue;
                thrust += thruster.ThrustN;
                impulseWeight += thruster.ThrustN * thruster.IspS;
            }
            return (weakest, thrust > 0.0 ? impulseWeight / thrust : 0.0);
        }
    }


    /// <summary>Monopropellant left in all parts (kg).</summary>
    public double TotalMonopropellant
    {
        get
        {
            double total = 0.0;
            foreach (var part in _parts) total += part.Monopropellant;
            return total;
        }
    }

    /// <summary>Draws monopropellant from every part in proportion to its load; false if short.</summary>
    /// <summary>Every part's propellant load, in part order, for <see cref="RestorePropellant"/>.</summary>
    public double[] CapturePropellant()
    {
        var loads = new double[_parts.Count * 4];
        for (int i = 0; i < _parts.Count; i++)
        {
            var part = _parts[i];
            loads[4 * i] = part.LiquidFuel;
            loads[4 * i + 1] = part.Oxidizer;
            loads[4 * i + 2] = part.SolidFuel;
            loads[4 * i + 3] = part.Monopropellant;
        }
        return loads;
    }

    public void RestorePropellant(double[] loads)
    {
        ArgumentNullException.ThrowIfNull(loads);
        if (loads.Length != _parts.Count * 4) return; // the graph changed (staging) mid-step
        for (int i = 0; i < _parts.Count; i++)
        {
            var part = _parts[i];
            part.LiquidFuel = loads[4 * i];
            part.Oxidizer = loads[4 * i + 1];
            part.SolidFuel = loads[4 * i + 2];
            part.Monopropellant = loads[4 * i + 3];
        }
    }

    public bool DrawMonopropellant(double kilograms) => DrawMonopropellant(kilograms, primaryStoresOnly: false);

    /// <summary>
    /// Draws from the shared monopropellant feed. With <paramref name="primaryStoresOnly"/>
    /// only the primary systems' stores feed (those whose RCS starts enabled or arms with its
    /// stage, not a standby system armed by the crew or at separation): a failed-on
    /// thruster belongs to the primary RCS and keeps draining it after it is switched off,
    /// never a standby system's tanks.
    /// </summary>
    public bool DrawMonopropellant(double kilograms, bool primaryStoresOnly)
        => DrawMonopropellant(kilograms, primaryStoresOnly, reservedByStore: null);

    /// <summary>Shared demand may only use the load left after simultaneous owning-system demands.</summary>
    internal bool DrawMonopropellant(double kilograms, bool primaryStoresOnly,
        IReadOnlyDictionary<Part, double>? reservedByStore)
    {
        if (kilograms <= 0.0) return true;
        double total = 0.0;
        foreach (var part in _parts)
            if (Feeds(part)) total += Available(part);
        if (total < kilograms) return false;
        foreach (var part in _parts)
            if (Feeds(part) && Available(part) > 0.0)
            {
                double reserved = Reserved(part);
                // Exact exhaustion empties the unreserved pool. Otherwise protect the
                // reservation boundary from roundoff in proportional subtraction.
                part.Monopropellant = kilograms == total ? reserved
                    : System.Math.Max(reserved, part.Monopropellant - kilograms * (Available(part) / total));
            }
        return true;

        double Reserved(Part part) => reservedByStore != null && reservedByStore.TryGetValue(part, out double reserved)
            ? reserved : 0.0;
        double Available(Part part) => reservedByStore == null
            ? part.Monopropellant
            : System.Math.Max(0.0, part.Monopropellant - Reserved(part));

        bool Feeds(Part part) => primaryStoresOnly
            ? part.Definition.RcsThrusters.Count == 0 || part.Definition.RcsAttitudeStartsEnabled
                || part.Definition.RcsArmsWithStage
            : FeedsSharedMonopropellant(part);
    }

    /// <summary>
    /// A part's monopropellant joins the shared feed unless it belongs to a thruster system
    /// that is valved off (a disabled RCS, such as Gemini's re-entry rings before arming).
    /// </summary>
    private static bool FeedsSharedMonopropellant(Part part) =>
        part.Definition.RcsThrusters.Count == 0 || part.RcsAttitudeEnabled;

    /// <summary>Enclosed payloads contribute mass/inertia, but not the carrier's outer hull.</summary>
    public bool IsEnclosedPayload(Part part) => part.Definition.InternalPayload && part != _root;

    /// <summary>
    /// Concrete topology buffer for internal simulation loops. The public <see cref="Parts"/>
    /// facade remains read-only; physics callers use this stable list to avoid boxing the
    /// <see cref="List{T}.Enumerator"/> on every tick.
    /// </summary>
    internal List<Part> PartList => _parts;

    /// <summary>
    /// Enables the short-lived physics-tick caches. They are intentionally scoped to one
    /// <see cref="Vessel.Tick"/> so resource consumption and staging remain authoritative
    /// outside the cache window.
    /// </summary>
    internal void BeginPhysicsTick()
    {
        ClearEngineFunding();
        LastLiquidEngineFunding = null;
        LastMainVentFunding = null;
        _physicsTickActive = true;
        _tickStageCacheValid = false;
        _tickActiveEngineCacheValid = false;
        _tickMassPropertiesValid = false;
        _engineReadoutCacheValid = false;
    }

    private bool _stagedReactionControlPending = true;
    internal bool ReactionControlArmingPending => _stagedReactionControlPending;

    /// <summary>
    /// Arms every RCS declared <see cref="PartDefinition.RcsArmsWithStage"/> (the S-IVB APS,
    /// Gemini's OAMS, Starship's cold-gas thrusters) once its part belongs to the current
    /// (lowest) stage: the system is switched on when the stage below separates. Also arms an
    /// isolated standby RCS. Runs once per topology change, at the next physics step, so an
    /// assembly being built part by part never looks isolated.
    /// </summary>
    public void ArmStagedReactionControl()
    {
        if (!_stagedReactionControlPending) return;
        _stagedReactionControlPending = false;
        ArmIsolatedReactionControl();
        bool waiting = false;
        foreach (var part in _parts)
            if (part.Definition.RcsArmsWithStage && !part.RcsStageArmed && !part.IsBroken)
                waiting = true;
        if (!waiting) return;
        foreach (var part in CurrentStageParts())
            if (part.Definition.RcsArmsWithStage && !part.RcsStageArmed && !part.IsBroken)
            {
                // Armed once, when the stage below separates; the crew may switch it off later.
                part.RcsStageArmed = true;
                part.RcsAttitudeEnabled = true;
                _rcsThrustersValid = false;
            }
    }

    /// <summary>
    /// A standby RCS (<see cref="PartDefinition.RcsArmsWhenIsolated"/>: the Apollo CM's entry
    /// thrusters, the Mercury capsule's H2O2 jets) is armed once its vehicle no longer carries
    /// another attitude actuator: a part with thrusters or a liquid-propellant engine (the
    /// carrier stage or service module it separates from). Solid retro motors do not count.
    /// </summary>
    private void ArmIsolatedReactionControl()
    {
        bool standby = false, primary = false;
        foreach (var part in _parts)
        {
            if (part.IsBroken) continue;
            var definition = part.Definition;
            if (definition.RcsThrusters.Count > 0)
            {
                if (definition.RcsArmsWhenIsolated && !part.RcsAttitudeEnabled) standby = true;
                else if (!definition.RcsArmsWhenIsolated && part.RcsAttitudeEnabled) primary = true;
            }
            else if (definition.Category == PartCategory.Engine
                && !string.Equals(definition.FuelTypeStr, "solid", StringComparison.OrdinalIgnoreCase))
                primary = true;
        }
        if (!standby || primary) return;
        foreach (var part in _parts)
            if (part.Definition.RcsArmsWhenIsolated) part.RcsAttitudeEnabled = true;
    }

    internal void EndPhysicsTick()
    {
        CompleteEngineFunding();
        _physicsTickActive = false;
        _tickStageCacheValid = false;
        _tickActiveEngineCacheValid = false;
        _tickMassPropertiesValid = false;
        _engineReadoutCacheValid = false;
    }

    internal void InvalidateTickActiveEngineCache() => _tickActiveEngineCacheValid = false;

    /// <summary>Incremented on every topology change (parts added, removed or detached).</summary>
    public int TopologyVersion { get; private set; }

    private void InvalidateTopologyCaches()
    {
        TopologyVersion++;
        _aeroElementsValid = false;
        _bodyFlapsValid = false;
        _rcsThrustersValid = false;
        _topologyPartLocalPositionsValid = false;
        _topologyVehicleLength = null;
        _tickStageCacheValid = false;
        _tickActiveEngineCacheValid = false;
        _tickMassPropertiesValid = false;
        _engineReadoutCacheValid = false;
        _stagedReactionControlPending = true;
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
            _engineReadoutCacheValid = false;
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

    public IEnumerable<Part>  GetChildren(Part parent) => GetChildList(parent);
    public Joint? GetJoint(Part parent, Part child)
    {
        foreach (var joint in _joints)
            if (joint.Parent == parent && joint.Child == child) return joint;
        return null;
    }

    /// <summary>
    /// Children of <paramref name="parent"/> in joint order, cached until the topology
    /// changes. Per-step solvers iterate it without allocating.
    /// </summary>
    internal List<Part> GetChildList(Part parent)
    {
        if (_childrenCacheVersion != TopologyVersion)
        {
            foreach (var list in _childrenCache.Values) list.Clear();
            foreach (var joint in _joints)
            {
                if (!_childrenCache.TryGetValue(joint.Parent, out var children))
                    _childrenCache[joint.Parent] = children = new List<Part>();
                children.Add(joint.Child);
            }
            _childrenCacheVersion = TopologyVersion;
        }
        return _childrenCache.TryGetValue(parent, out var cached) ? cached : EmptyChildren;
    }
    private readonly Dictionary<Part, List<Part>> _childrenCache = new();
    private int _childrenCacheVersion = -1;
    private static readonly List<Part> EmptyChildren = new();

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
    /// <summary>All liquid fuel aboard, main and header tanks.</summary>
    public double TotalLiquidFuel  => _parts.Sum(p => p.LiquidFuel + p.HeaderLiquidFuel);
    /// <summary>All oxidizer aboard, main and header tanks.</summary>
    public double TotalOxidizer    => _parts.Sum(p => p.Oxidizer + p.HeaderOxidizer);
    public double TotalElectricCharge => _parts.Sum(p => p.ElectricCharge);
    public double VehicleLength
    {
        get
        {
            if (_topologyVehicleLength is { } cached) return cached;
            return (_topologyVehicleLength = ComputeVehicleLength()).Value;
        }
    }

    private double ComputeVehicleLength()
    {
        double specified = 0.0;
        foreach (var part in _parts)
            if (!IsEnclosedPayload(part)) specified += System.Math.Max(0.0, part.Definition.LengthM);
        double estimate = specified > 0.0
            ? specified
            : System.Math.Max(1.0, _parts.Count * 12.0);
        if (!(specified > 0.0) || _root == null || _joints.Count != _parts.Count - 1)
            return estimate;

        bool endToEndChain = true;
        int direction = 0;
        for (int i = 0; i < _joints.Count; i++)
        {
            var joint = _joints[i];
            var parent = FindAttachmentNode(joint.Parent, joint.ParentNodeId);
            var child = FindAttachmentNode(joint.Child, joint.ChildNodeId);
            // Unknown connections do not establish a physical overlap or gap.
            if (parent?.Position is not { Length: >= 3 } p
                || child?.Position is not { Length: >= 3 } c
                || !double.IsFinite(p[0]) || !double.IsFinite(p[1]) || !double.IsFinite(p[2])
                || !double.IsFinite(c[0]) || !double.IsFinite(c[1]) || !double.IsFinite(c[2]))
                return estimate;
            bool oppositeEnds = p[0] == 0.0 && p[2] == 0.0 && c[0] == 0.0 && c[2] == 0.0
                && System.Math.Abs(p[1]) == joint.Parent.Definition.LengthM * 0.5
                && System.Math.Abs(c[1]) == joint.Child.Definition.LengthM * 0.5
                && p[1] * c[1] <= 0.0;
            if (!oppositeEnds) endToEndChain = false;
            int jointDirection = System.Math.Sign(p[1]);
            if (direction != 0 && jointDirection != 0 && jointDirection != direction)
                endToEndChain = false;
            if (jointDirection != 0) direction = jointDirection;
            for (int j = 0; j < i; j++)
                if (ReferenceEquals(_joints[j].Parent, joint.Parent)) endToEndChain = false;
        }
        // Preserve the original summation (including its last bits) for ordinary
        // serial stacks. Only explicit nonstandard connections need an envelope.
        if (endToEndChain) return estimate;

        var positions = GetTopologyPartLocalPositions();
        if (positions.Count != _parts.Count) return estimate;
        double lower = double.PositiveInfinity, upper = double.NegativeInfinity;
        foreach (var part in _parts)
        {
            if (IsEnclosedPayload(part)) continue;
            double center = positions[part].Y;
            double halfLength = part.Definition.LengthM * 0.5;
            lower = System.Math.Min(lower, center - halfLength);
            upper = System.Math.Max(upper, center + halfLength);
        }
        double span = upper - lower;
        return double.IsFinite(span) && span > 0.0 ? span : estimate;
    }
    public double MaximumDiameter
    {
        get
        {
            if (_parts.Count == 0) return 1.0;
            double specified = 0.0;
            foreach (var part in _parts)
                if (!IsEnclosedPayload(part)) specified = System.Math.Max(specified, part.Definition.DiameterM);
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
                if (IsEnclosedPayload(part)) continue;
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
                if (IsEnclosedPayload(part)) continue;
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
        double positionedMass = 0.0;
        if (_root != null)
        {
            var positions = GetCachedPartLocalPositions();
            for (int i = 0; i < _parts.Count; i++)
            {
                var part = _parts[i];
                double mass = part.CurrentMass;
                _tickTotalMass += mass;
                if (!positions.TryGetValue(part, out var position)) continue;
                _tickCenterOfMass += MassCentre(part, position) * mass;
                positionedMass += mass;
            }
            if (positionedMass > 0.0)
                _tickCenterOfMass /= positionedMass;
        }

        if (_tickTotalMass > 0.0)
        {
            _tickMassProperties = BuildMassProperties(GetCachedPartLocalPositions());
            (_tickTransverseMomentOfInertia, _tickAxialMomentOfInertia) =
                PrincipalScalars(_tickMassProperties);
        }
        else
        {
            _tickTransverseMomentOfInertia = 0.0;
            _tickAxialMomentOfInertia = 0.0;
        }
        _tickMassPropertiesValid = true;
    }

    /// <summary>
    /// Returns the current physical mass distribution in vessel-local coordinates.
    /// Each part is approximated as a cylinder aligned with the vessel axes, then shifted
    /// to the aggregate centre of mass with the parallel-axis theorem. This is deliberately
    /// separate from the legacy envelope inertia used by existing guidance authority code.
    /// </summary>
    public RigidBodyMassProperties GetMassProperties()
    {
        if (_physicsTickActive)
        {
            EnsureTickMassProperties();
            if (_tickTotalMass <= 0.0)
                throw new InvalidOperationException("Cannot build mass properties for an empty graph.");
            return _tickMassProperties;
        }

        var positions = ComputePartLocalPositions();
        return BuildMassProperties(positions);
    }

    /// <summary>Non-throwing variant for construction and empty-graph callers.</summary>
    public bool TryGetMassProperties(out RigidBodyMassProperties properties)
    {
        if (_physicsTickActive)
        {
            EnsureTickMassProperties();
            if (_tickTotalMass <= 0.0)
            {
                properties = default;
                return false;
            }

            properties = _tickMassProperties;
            return true;
        }

        var positions = ComputePartLocalPositions();
        double totalMass = _parts.Sum(part => part.CurrentMass);
        if (totalMass <= 0.0)
        {
            properties = default;
            return false;
        }

        properties = BuildMassProperties(positions);
        return true;
    }

    private RigidBodyMassProperties BuildMassProperties(
        IReadOnlyDictionary<Part, Vector3d> positions)
    {
        double fallbackLength = System.Math.Max(
            1.0,
            VehicleLength / System.Math.Max(1, _parts.Count));
        double fallbackDiameter = System.Math.Max(1.0, MaximumDiameter);

        // One shared decomposition (PartMassGeometry): every component keeps its own centroid
        // and central inertia, so the parallel-axis spread between dry structure, tanks and
        // headers is retained instead of shifting one wet cylinder.
        var components = _massComponentScratch;
        double totalMass = 0.0;
        Vector3d weightedPosition = Vector3d.Zero;
        foreach (var part in _parts)
        {
            double mass = part.CurrentMass;
            if (!double.IsFinite(mass) || mass < 0.0)
                throw new InvalidOperationException(
                    $"Part '{part.InstanceId}' has invalid mass {mass:G17}.");
            if (!positions.TryGetValue(part, out var position)) continue;
            components.Clear();
            PartMassGeometry.AppendComponents(part, fallbackLength, fallbackDiameter, components);
            foreach (var component in components)
            {
                if (component.MassKg <= 0.0) continue;
                totalMass += component.MassKg;
                weightedPosition += new Vector3d(position.X, position.Y + component.CenterYM, position.Z)
                    * component.MassKg;
            }
        }

        if (!double.IsFinite(totalMass) || totalMass <= 0.0)
            throw new InvalidOperationException("Cannot build mass properties for an empty graph.");

        Vector3d centerOfMass = weightedPosition / totalMass;

        double i11 = 0.0;
        double i12 = 0.0;
        double i13 = 0.0;
        double i22 = 0.0;
        double i23 = 0.0;
        double i33 = 0.0;
        foreach (var part in _parts)
        {
            if (!positions.TryGetValue(part, out var position)) continue;
            components.Clear();
            PartMassGeometry.AppendComponents(part, fallbackLength, fallbackDiameter, components);
            foreach (var component in components)
            {
                double mass = component.MassKg;
                if (mass <= 0.0) continue;
                // Local axis is vessel +Y; the component's central tensor is diagonal.
                double transverse = mass * component.Shape.TransverseSpecific(mass);
                double axial = mass * component.Shape.AxialSpecific;
                Vector3d d = new Vector3d(position.X, position.Y + component.CenterYM, position.Z) - centerOfMass;
                // Parallel-axis theorem: I_shift = I_local + m((d·d)E - d⊗d).
                i11 += transverse + mass * (d.Y * d.Y + d.Z * d.Z);
                i22 += axial + mass * (d.X * d.X + d.Z * d.Z);
                i33 += transverse + mass * (d.X * d.X + d.Y * d.Y);
                i12 -= mass * d.X * d.Y;
                i13 -= mass * d.X * d.Z;
                i23 -= mass * d.Y * d.Z;
            }
        }

        var inertia = new Matrix3x3d(
            i11, i12, i13,
            i12, i22, i23,
            i13, i23, i33);
        return new RigidBodyMassProperties(totalMass, centerOfMass, inertia);
    }

    private readonly List<PartMassComponent> _massComponentScratch = new(16);

    /// <summary>A part's own centre of mass: its centre, shifted along its axis by its components.</summary>
    private static Vector3d MassCentre(Part part, Vector3d position)
    {
        double offset = part.MassCenterOffsetYM;
        return offset == 0.0 ? position : new Vector3d(position.X, position.Y + offset, position.Z);
    }

    // Positions depend on topology and geometry only, so ticks share the topology cache.
    private Dictionary<Part, Vector3d> GetCachedPartLocalPositions() => GetTopologyPartLocalPositions();

    /// <summary>The stack-frame positions the mass properties use (only placed parts carry mass).</summary>
    internal IReadOnlyDictionary<Part, Vector3d> GetTopologyPartLocalPositionsForMass() => GetTopologyPartLocalPositions();

    /// <summary>
    /// True when this body's aerodynamics come from its part geometry
    /// (<see cref="GetAeroElements"/>). Slender-body and crossflow theory hold only for slender
    /// stacks, so a blunt body (fineness ratio below <see cref="SlenderBodyMinimumFineness"/>:
    /// Mercury, Gemini and Apollo capsules) keeps the blunt/trimmed model. Capsules keep their
    /// trimmed model, and a root flying alone with a declared standalone static margin (Super
    /// Heavy's grid fins, not yet modelled as surfaces) keeps that margin.
    /// </summary>
    public bool UsesDistributedAerodynamics =>
        CapsuleAerodynamics == null && _root?.Definition.StandaloneAerodynamicCenterOffsetYM == null
        && MaximumDiameter > 0.0 && VehicleLength / MaximumDiameter >= SlenderBodyMinimumFineness;

    /// <summary>Length/diameter below which slender-body theory does not describe the flow.</summary>
    public const double SlenderBodyMinimumFineness = 3.0;

    private readonly List<Physics.AeroElement> _aeroElements = new();
    private bool _aeroElementsValid;

    /// <summary>
    /// Aerodynamic elements of the assembled body, derived from part geometry and cached by
    /// topology: one crossflow element per exposed part (planform D·L at its centroid), and
    /// slender-body area changes (the leading nose and every diameter step between stacked
    /// parts). See <see cref="Physics.DistributedAerodynamics"/>.
    /// </summary>
    public IReadOnlyList<Physics.AeroElement> GetAeroElements()
    {
        if (_aeroElementsValid) return _aeroElements;
        _aeroElements.Clear();
        Physics.DistributedAerodynamics.BuildElements(this, GetTopologyPartLocalPositions(), _aeroElements);
        _aeroElementsValid = true;
        return _aeroElements;
    }

    private readonly List<Physics.BodyFlapGeometry> _bodyFlaps = new();
    private bool _bodyFlapsValid;

    /// <summary>Hinged control surfaces of the assembled body in the vessel frame, cached by topology.</summary>
    public IReadOnlyList<Physics.BodyFlapGeometry> GetBodyFlaps()
    {
        if (_bodyFlapsValid) return _bodyFlaps;
        _bodyFlaps.Clear();
        var positions = GetTopologyPartLocalPositions();
        foreach (var part in _parts)
        {
            if (part.Definition.BodyFlaps.Count == 0 || !positions.TryGetValue(part, out var centre)) continue;
            foreach (var flap in part.Definition.BodyFlaps)
                _bodyFlaps.Add(Physics.BodyFlapGeometry.From(part, centre, flap));
        }
        _bodyFlapsValid = true;
        return _bodyFlaps;
    }

    private readonly List<Physics.RcsThrusterGeometry> _rcsThrusters = new();
    private bool _rcsThrustersValid;

    /// <summary>Every declared RCS thruster, placed in the vessel frame (skirt-datum origin).</summary>
    public List<Physics.RcsThrusterGeometry> GetRcsThrusters()
    {
        if (_rcsThrustersValid) return _rcsThrusters;
        _rcsThrusters.Clear();
        var positions = GetTopologyPartLocalPositions();
        foreach (var part in _parts)
        {
            if (part.Definition.RcsThrusters.Count == 0 || !positions.TryGetValue(part, out var centre)) continue;
            foreach (var thruster in part.Definition.RcsThrusters)
                _rcsThrusters.Add(Physics.RcsThrusterGeometry.From(part, centre, thruster));
        }
        _rcsThrustersValid = true;
        return _rcsThrusters;
    }

    /// <summary>Centre of <paramref name="part"/> in the vessel frame (skirt-datum origin).</summary>
    public bool TryGetPartLocalPosition(Part part, out Vector3d position) =>
        GetTopologyPartLocalPositions().TryGetValue(part, out position);

    /// <summary>Allocation-free positions for read-only queries outside a physics tick.</summary>
    private Dictionary<Part, Vector3d> GetTopologyPartLocalPositions()
    {
        if (_topologyPartLocalPositionsValid)
            return _topologyPartLocalPositions;
        _topologyPartLocalPositions.Clear();
        AssignSkirtDatumPositions(_topologyPartLocalPositions);
        _topologyPartLocalPositionsValid = true;
        return _topologyPartLocalPositions;
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
        bool foundActiveDecoupler = false;
        for (int partIndex = 0; partIndex < _parts.Count; partIndex++)
        {
            var decoupler = _parts[partIndex];
            if (decoupler.Definition.Category != PartCategory.Decoupler
                || !decoupler.IsStagingActive)
                continue;

            foundActiveDecoupler = true;
            Part? child = null;
            for (int jointIndex = 0; jointIndex < _joints.Count; jointIndex++)
            {
                var joint = _joints[jointIndex];
                if (ReferenceEquals(joint.Parent, decoupler))
                {
                    child = joint.Child;
                    break;
                }
            }

            if (child == null) continue;

            CollectSubtreeInto(child, _stageSubtreeScratch);
            bool containsActiveDecoupler = false;
            for (int subtreeIndex = 0; subtreeIndex < _stageSubtreeScratch.Count; subtreeIndex++)
            {
                var subtreePart = _stageSubtreeScratch[subtreeIndex];
                if (subtreePart.Definition.Category == PartCategory.Decoupler
                    && subtreePart.IsStagingActive)
                {
                    containsActiveDecoupler = true;
                    break;
                }
            }

            // The bottom stage is the first active-decoupler subtree that contains no
            // further active decoupler. Preserve the previous _parts/_joints order.
            if (!containsActiveDecoupler)
            {
                result.AddRange(_stageSubtreeScratch);
                return;
            }
        }

        if (!foundActiveDecoupler)
        {
            result.AddRange(_parts);
            return;
        }

        result.AddRange(_parts);
    }

    private void CollectSubtreeInto(Part root, List<Part> result)
    {
        result.Clear();
        _stageTraversalScratch.Clear();
        _stageTraversalScratch.Add(root);

        for (int queueIndex = 0; queueIndex < _stageTraversalScratch.Count; queueIndex++)
        {
            var part = _stageTraversalScratch[queueIndex];
            result.Add(part);
            for (int jointIndex = 0; jointIndex < _joints.Count; jointIndex++)
            {
                var joint = _joints[jointIndex];
                if (ReferenceEquals(joint.Parent, part))
                    _stageTraversalScratch.Add(joint.Child);
            }
        }

        _stageTraversalScratch.Clear();
    }

    /// <summary>
    /// Concrete active-engine buffer for simulation callers. Keeping the concrete list
    /// available avoids boxing its struct enumerator when the physics tick iterates it.
    /// </summary>
    internal List<Part> ActiveEngineList
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

    /// <summary>Compatibility enumerable for presentation and external callers.</summary>
    public IEnumerable<Part> ActiveEngines => ActiveEngineList;

    /// <summary>
    /// True when the current stage exposes at least one healthy, staging-active engine part.
    /// This avoids enumerating the compatibility view for presence-only presentation queries.
    /// </summary>
    public bool HasActiveEngineParts => ActiveEngineList.Count > 0;

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

            if (_root == null) return Vector3d.Zero;
            // Same rule as BuildMassProperties: only parts placed in the stack frame carry a
            // position; an unattached part must not drag the centre toward the datum.
            var positions = GetTopologyPartLocalPositions();
            var com = Vector3d.Zero;
            double positionedMass = 0.0;
            foreach (var p in _parts)
            {
                if (!positions.TryGetValue(p, out var pos)) continue;
                com = com + MassCentre(p, pos) * p.CurrentMass;
                positionedMass += p.CurrentMass;
            }
            return positionedMass > 0.0 ? com / positionedMass : Vector3d.Zero;
        }
    }

    /// <summary>
    /// Pitch/yaw inertia (kg·m²) about the current centre of mass, from the per-part
    /// parallel-axis tensor. It changes continuously as propellant mass is consumed.
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
            return TryGetMassProperties(out var properties)
                ? PrincipalScalars(properties).Transverse
                : 0.0;
        }
    }

    /// <summary>Roll-axis inertia (kg·m²) from the same per-part tensor.</summary>
    public double AxialMomentOfInertia
    {
        get
        {
            if (_physicsTickActive)
            {
                EnsureTickMassProperties();
                return _tickAxialMomentOfInertia;
            }
            return TryGetMassProperties(out var properties)
                ? PrincipalScalars(properties).Axial
                : 0.0;
        }
    }

    /// <summary>
    /// Scalar pitch/yaw and roll inertia read from the same per-part, parallel-axis tensor
    /// the coupled rigid-body integrator uses, so both integration paths divide torque by one
    /// inertia model. Pitch and yaw are averaged; for the axisymmetric stacks flown here
    /// I_xx and I_zz agree to within the products of inertia.
    /// </summary>
    private static (double Transverse, double Axial) PrincipalScalars(
        in RigidBodyMassProperties properties) =>
        (0.5 * (properties.InertiaBody.M11 + properties.InertiaBody.M33),
         properties.InertiaBody.M22);

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
        foreach (var engine in ActiveEngineList)
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
        foreach (var engine in ActiveEngineList)
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
    /// <summary>
    /// Slowest gimbal rate (°/s) at which a lit engine still counts as an attitude actuator;
    /// slower gimbals only trim the thrust line through the centre of mass.
    /// </summary>
    public const double AttitudeGimbalRateDegPerS = 1.0;

    /// <summary>
    /// True when every lit, gimballed engine slews at least <see cref="AttitudeGimbalRateDegPerS"/>
    /// (engines without a resolved model count as attitude gimbals).
    /// </summary>
    public bool GimbalsSlewForAttitudeControl()
    {
        foreach (var engine in ActiveEngineList)
        {
            var model = engine.Definition.ResolvedEngineModel;
            if (model == null || model.GimbalRangeDeg <= 0.0) continue;
            if (model.GimbalRateDegPerS < AttitudeGimbalRateDegPerS) return false;
        }
        return true;
    }

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

        foreach (var engine in ActiveEngineList)
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
        foreach (var engine in ActiveEngineList)
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
    /// per part), this sums real per-engine moment arms, so asymmetric engine availability or
    /// gimbal deflection produces genuine, correctly-signed torque instead of only reducing
    /// total thrust proportionally.
    /// </summary>
    public Vector3d GetTotalTorque(double ambientPressure)
    {
        var positions = GetCachedPartLocalPositions();
        var com = CenterOfMass;
        var torque = Vector3d.Zero;
        foreach (var engine in ActiveEngineList)
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
    /// Returns the engine-cluster torque with every engine at zero gimbal. This includes
    /// unavoidable fixed-engine and asymmetric-mount moments, which a TVC allocator must
    /// cancel before solving for the commanded control torque. It intentionally does not
    /// mutate servo state or command overrides.
    /// </summary>
    /// <param name="torqueReference">Point the moment is taken about (part frame); this
    /// graph's own centre of mass when null. A docked primary passes its stack's centre.</param>
    public Vector3d GetZeroGimbalEngineTorque(double ambientPressure, Vector3d? torqueReference = null)
    {
        var positions = GetCachedPartLocalPositions();
        var com = torqueReference ?? CenterOfMass;
        var torque = Vector3d.Zero;
        foreach (var engine in ActiveEngineList)
        {
            if (!positions.TryGetValue(engine, out var partPosition)) continue;
            var geometry = engine.GetEngineInstanceZeroGimbalThrustGeometrySnapshot(ambientPressure);
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
    /// <param name="torqueReference">Point <paramref name="desiredTorque"/> is taken about (part
    /// frame); this graph's own centre of mass when null. The body the engines turn is the
    /// docked stack when there is one, and its centre can sit metres from the primary's own:
    /// lever arms about the wrong point scale the delivered moment by their ratio.</param>
    public void SolveDifferentialGimbal(Vector3d desiredTorque, double ambientPressure,
        Vector3d? torqueReference = null)
    {
        var positions = GetCachedPartLocalPositions();
        var com = torqueReference ?? CenterOfMass;

        var contributions = _physicsTickActive
            ? _gimbalContributions
            : new List<(EngineInstanceState? State, Part Engine, Vector3d Jx, Vector3d Jz)>();
        contributions.Clear();
        double m00 = 0, m01 = 0, m02 = 0, m11 = 0, m12 = 0, m22 = 0;

        void Add(EngineInstanceState? state, Part engine, Vector3d r, double thrustN, double gimbalRangeDeg)
        {
            double mountAuthority = thrustN * (gimbalRangeDeg * MathUtils.DEG_TO_RAD);
            var jx = r.Cross(new Vector3d(mountAuthority, 0.0, 0.0));
            var jz = r.Cross(new Vector3d(0.0, 0.0, mountAuthority));
            contributions.Add((state, engine, jx, jz));

            m00 += jx.X * jx.X + jz.X * jz.X;
            m01 += jx.X * jx.Y + jz.X * jz.Y;
            m02 += jx.X * jx.Z + jz.X * jz.Z;
            m11 += jx.Y * jx.Y + jz.Y * jz.Y;
            m12 += jx.Y * jx.Z + jz.Y * jz.Z;
            m22 += jx.Z * jx.Z + jz.Z * jz.Z;
        }

        foreach (var engine in ActiveEngineList)
        {
            if (!positions.TryGetValue(engine, out var partPosition)) continue;
            if (!engine.HasEngineRuntime)
            {
                // A single engine without a per-instance runtime gimbals as one mount through
                // its part-wide GimbalOffset (the same geometry GetThrustVector/GetTotalTorque use).
                double range = engine.Definition.GimbalRange;
                double thrust = engine.GetThrustMagnitude(ambientPressure);
                if (range > 0.0 && thrust > 0.0)
                    Add(null, engine,
                        partPosition + new Vector3d(0.0, engine.Definition.ThrustPositionYM, 0.0) - com,
                        thrust, range);
                continue;
            }
            var authorityRows = engine.GetEngineInstanceGimbalAuthoritySnapshot(ambientPressure);
            for (int i = 0; i < authorityRows.Count; i++)
            {
                var (state, mountPosition, thrustN, gimbalRangeDeg) = authorityRows[i];
                Add(state, engine, partPosition + mountPosition - com, thrustN, gimbalRangeDeg);
            }
        }

        if (contributions.Count == 0) return;

        // Tikhonov regularization: without it, a Gramian built from fewer than three
        // independent lever-arm directions (e.g. one engine, or engines coincident on an
        // axis) is singular and unsolvable. εI keeps the solve well-posed and damps the
        // allocation toward zero for axes the cluster genuinely cannot influence, instead of
        // failing outright.
        double epsilon = 1e-6 * (m00 + m11 + m22);
        var lambda = SolveSymmetric3x3(
            m00 + epsilon, m01, m02,
            m11 + epsilon, m12,
            m22 + epsilon,
            desiredTorque);

        foreach (var (state, engine, jx, jz) in contributions)
        {
            var command = new Vector3d(
                System.Math.Clamp(jx.Dot(lambda), -1.0, 1.0),
                0.0,
                System.Math.Clamp(jz.Dot(lambda), -1.0, 1.0));
            if (state != null) state.GimbalCommandOverride = command;
            else engine.GimbalOffset = command;
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
        // Normalize before testing the determinant: its units and magnitude depend on
        // engine authority cubed, not on the conditioning of the allocation problem.
        double scale = System.Math.Max(System.Math.Abs(a), System.Math.Max(System.Math.Abs(d), System.Math.Abs(f)));
        if (!(scale > 0.0) || !double.IsFinite(scale)) return Vector3d.Zero;
        a /= scale; b /= scale; c /= scale; d /= scale; e /= scale; f /= scale;
        rhs /= scale;
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
            foreach (var engine in ActiveEngineList)
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
        foreach (var engine in ActiveEngineList)
            thrust += engine.GetThrustMagnitude(ambientPressure);
        return thrust;
    }

    /// <summary>Pressure-corrected current-stage thrust available at full throttle.</summary>
    public double GetMaximumThrust(double ambientPressure)
    {
        double thrust = 0.0;
        foreach (var engine in ActiveEngineList)
            thrust += engine.GetFullThrottleThrustMagnitude(ambientPressure);
        return thrust;
    }

    /// <summary>Total propellant mass flow of the current stage (kg/s) at this pressure.</summary>
    public double GetCurrentMassFlow(double ambientPressure)
    {
        double massFlow = 0.0;
        foreach (var engine in ActiveEngineList)
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
        foreach (var engine in ActiveEngineList)
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

    private List<Part> GetActiveEngineListForReadout()
    {
        if (_physicsTickActive)
        {
            EnsureTickActiveEngineCache();
            return _tickActiveEngines;
        }

        return GetQueryActiveEngineList();
    }

    private bool TryCopyCachedEngineReadouts(
        IReadOnlyList<Part> engines,
        double ambientPressure,
        List<EngineReadout> destination,
        out EngineTelemetrySummary summary)
    {
        summary = default;
        // A non-finite pressure must still reach the evaluator so it preserves its existing
        // argument validation instead of accidentally becoming a cache hit.
        if (!_engineReadoutCacheValid
            || !double.IsFinite(ambientPressure)
            || ambientPressure != _engineReadoutCacheAmbientPressure)
            return false;

        int rowIndex = 0;
        for (int engineIndex = 0; engineIndex < engines.Count; engineIndex++)
        {
            var engine = engines[engineIndex];
            if (engine.HasEngineRuntime)
            {
                var states = engine.EngineStates;
                for (int stateIndex = 0; stateIndex < states.Count; stateIndex++)
                {
                    if (rowIndex >= _engineReadoutCacheRows.Count
                        || _engineReadoutCacheParts[rowIndex] != engine
                        || _engineReadoutCacheStates[rowIndex] != states[stateIndex])
                        return false;

                    var state = states[stateIndex];
                    var row = _engineReadoutCacheRows[rowIndex];
                    if (state.State != row.State
                        || state.ChamberPressureFraction != row.Throttle
                        || !string.Equals(
                            state.FailureCode,
                            row.FailureCode,
                            StringComparison.Ordinal))
                        return false;
                    rowIndex++;
                }
            }
            else
            {
                if (rowIndex >= _engineReadoutCacheRows.Count
                    || _engineReadoutCacheParts[rowIndex] != engine
                    || _engineReadoutCacheStates[rowIndex] != null)
                    return false;

                var row = _engineReadoutCacheRows[rowIndex];
                if (engine.ThrottleLevel != row.Throttle
                    || (engine.IsBroken ? "PART_BROKEN" : null) != row.FailureCode)
                    return false;
                rowIndex++;
            }
        }

        if (rowIndex != _engineReadoutCacheRows.Count) return false;
        destination.AddRange(_engineReadoutCacheRows);
        summary = _engineReadoutCacheSummary;
        return true;
    }

    /// <summary>
    /// Fills a caller-owned buffer with the current-stage engine telemetry.
    /// The buffer is cleared and never replaced, allowing HUD/render consumers to sample
    /// telemetry without allocating a list or copying the compatibility enumerable.
    /// </summary>
    public void FillEngineReadouts(double ambientPressure, List<EngineReadout> destination)
        => FillEngineReadouts(ambientPressure, destination, out _);

    /// <summary>
    /// Fills engine rows and computes their aggregate telemetry in one active-stage pass.
    /// The overload without <paramref name="summary"/> remains the compatibility API for
    /// callers that only need rows.
    /// </summary>
    public void FillEngineReadouts(
        double ambientPressure,
        List<EngineReadout> destination,
        out EngineTelemetrySummary summary)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();

        var engines = GetActiveEngineListForReadout();
        if (TryCopyCachedEngineReadouts(engines, ambientPressure, destination, out summary))
            return;

        _engineReadoutCacheParts.Clear();
        _engineReadoutCacheStates.Clear();
        _engineReadoutCacheRows.Clear();
        _engineReadoutCacheAmbientPressure = ambientPressure;
        _engineReadoutCacheValid = false;
        int nominalEngineCount = 0;
        double thrustN = 0.0;
        double massFlowKgS = 0.0;

        foreach (var engine in engines)
        {
            nominalEngineCount += System.Math.Max(1, engine.Definition.EngineCount);
            if (engine.HasEngineRuntime)
            {
                int stateIndex = 0;
                foreach (var telemetry in engine.GetEngineTelemetry(ambientPressure))
                {
                    var row = new EngineReadout(
                        telemetry.InstanceId,
                        engine.Definition.Name,
                        telemetry.ChamberPressureFraction,
                        telemetry.ThrustN,
                        telemetry.MassFlowKgS,
                        telemetry.State,
                        telemetry.FailureCode);
                    destination.Add(row);
                    thrustN += row.ThrustN;
                    massFlowKgS += row.MassFlowKgS;
                    _engineReadoutCacheParts.Add(engine);
                    _engineReadoutCacheStates.Add(engine.EngineStates[stateIndex++]);
                    _engineReadoutCacheRows.Add(row);
                }

                continue;
            }

            var staticRow = BuildStaticEngineReadout(engine, ambientPressure);
            destination.Add(staticRow);
            thrustN += staticRow.ThrustN;
            massFlowKgS += staticRow.MassFlowKgS;
            _engineReadoutCacheParts.Add(engine);
            _engineReadoutCacheStates.Add(null);
            _engineReadoutCacheRows.Add(staticRow);
        }

        summary = new EngineTelemetrySummary(
            nominalEngineCount,
            destination.Count,
            thrustN,
            massFlowKgS);
        _engineReadoutCacheSummary = summary;
        _engineReadoutCacheValid = true;
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
            p.LiquidFuel + p.Oxidizer + p.HeaderLiquidFuel + p.HeaderOxidizer + p.SolidFuel + p.Monopropellant);
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
        foreach (var e in ActiveEngineList)
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
    /// <summary>
    /// Vents up to <paramref name="kg"/> of liquid propellant from the main tanks (fuel and
    /// oxidizer in proportion to what remains, never from header tanks or a landing reserve).
    /// Returns the mass removed.
    /// </summary>
    public double VentMainPropellant(double kg) => VentMainPropellant(kg, null, false);

    internal double VentMainPropellant(double kg, double? dt, bool assist)
    {
        LastMainVentFunding = null;
        if (!(kg > 0.0)) return 0.0;
        double available = 0.0;
        foreach (var part in _parts) available += part.AvailableLiquidFuel + part.AvailableOxidizer;
        if (available <= 0.0) return 0.0;
        var capture = CapturePropellantFunding || ResourceFundingCapture != null
            ? (_ventFundingScratch ??= new LiquidFundingCapture()).Reset() : null;
        capture?.BeginPool(_parts, includeHeaders: false);
        double vented = System.Math.Min(kg, available);
        double fraction = vented / available;
        foreach (var part in _parts)
        {
            part.LiquidFuel -= part.AvailableLiquidFuel * fraction;
            part.Oxidizer -= part.AvailableOxidizer * fraction;
        }
        if (capture != null)
        {
            capture.EndPool(null);
            var receipt = capture.BuildVent(kg, vented, dt, assist);
            ResourceFundingCapture?.RecordVent(receipt);
            if (CapturePropellantFunding) LastMainVentFunding = receipt;
        }
        return vented;
    }

    public void ConsumePropellant(double dt, double ambientPressure) => ConsumePropellant(dt, ambientPressure, false);

    internal void ConsumePropellant(double dt, double ambientPressure, bool assist)
    {
        LastLiquidEngineFunding = null;
        // One reusable capture per graph: published snapshots copy its contents.
        _liquidFundingCapture = (CapturePropellantFunding || ResourceFundingCapture != null) && dt > 0
            ? (_engineFundingScratch ??= new LiquidFundingCapture()).Reset() : null;
        try
        {
            ConsumePropellantCore(dt, ambientPressure);
            var receipt = _liquidFundingCapture?.BuildEngines(dt, assist);
            ResourceFundingCapture?.RecordLiquid(receipt);
            ResourceFundingCapture?.RecordCurrentStores(_parts, ResourceWithdrawalSource.OtherEngine);
            if (CapturePropellantFunding) LastLiquidEngineFunding = receipt;
        }
        finally { _liquidFundingCapture = null; }
    }

    private void ConsumePropellantCore(double dt, double ambientPressure)
    {
        if (HotStageOverlapActive)
        {
            var bottom = CurrentStageParts();
            _hotStageBottomSet.Clear();
            for (int i = 0; i < bottom.Count; i++)
                _hotStageBottomSet.Add(bottom[i]);

            _hotStageUpperParts.Clear();
            for (int i = 0; i < _parts.Count; i++)
            {
                var part = _parts[i];
                if (!_hotStageBottomSet.Contains(part))
                    _hotStageUpperParts.Add(part);
            }

            ConsumePropellantFromPool(bottom, bottom, dt, ambientPressure);
            ConsumePropellantFromPool(
                _hotStageUpperParts,
                _hotStageUpperParts,
                dt,
                ambientPressure);
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
        int pool = _liquidFundingCapture?.BeginPool(tankPool, includeHeaders: true) ?? 0;

        // Calcular flujo de masa total de todos los motores activos
        _liquidDemands.Clear();
        _singleResourceDemands.Clear();
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
                    var states = engine.EngineStates;
                    for (int engineIndex = 0; engineIndex < states.Count; engineIndex++)
                    {
                        var state = states[engineIndex];
                        var performance = engine.GetEngineInstancePerformance(
                            engineIndex,
                            ambientPressure);
                        if (performance.MassFlowKgS <= 1e-12)
                        {
                            continue;
                        }
                        _liquidDemands.Add(new LiquidEngineDemand(
                            engine,
                            state.InstanceId,
                            performance.MassFlowKgS,
                            def.MixtureRatio));
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
                CollectSingleResourceDemand(engine, PropellantResourceKind.SolidFuel, ambientPressure);
            else if (fuelType.Contains("mono", StringComparison.OrdinalIgnoreCase))
                CollectSingleResourceDemand(engine, PropellantResourceKind.Monopropellant, ambientPressure);
        }

        // Consumir de los tanques de la etapa activa (cross-feed dentro de la etapa)
        if (_liquidDemands.Count > 0)
        {
            double totalLF = 0.0;
            double totalOx = 0.0;
            double headerLF = 0.0;
            double headerOx = 0.0;
            for (int i = 0; i < tankPool.Count; i++)
            {
                totalLF += tankPool[i].AvailableLiquidFuel;
                totalOx += tankPool[i].AvailableOxidizer;
                headerLF += tankPool[i].HeaderLiquidFuel;
                headerOx += tankPool[i].HeaderOxidizer;
            }
            // Header tanks feed once the main tanks cannot fund the demand.
            double remainingLF = totalLF + headerLF;
            double remainingOx = totalOx + headerOx;
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
                fundedLF += demand.MassFlowKgS * fuelFraction * dt;
                fundedOx += demand.MassFlowKgS * (1.0 - fuelFraction) * dt;
            }
            // All branches sharing a pool burn simultaneously until its first limiting
            // reactant. Engine IDs determine summation order, never priority of fuel access.
            double fraction = 1.0;
            if (fundedLF > 0.0) fraction = System.Math.Min(fraction, remainingLF / fundedLF);
            if (fundedOx > 0.0) fraction = System.Math.Min(fraction, remainingOx / fundedOx);
            fraction = System.Math.Clamp(fraction, 0.0, 1.0);
            if (_liquidFundingCapture != null && dt * fraction > 0)
                foreach (var demand in _liquidDemands)
                {
                    if (!(demand.MassFlowKgS > 0)) continue;
                    double fuelFraction = demand.MixtureRatio > 0.0
                        ? 1.0 / (1.0 + demand.MixtureRatio) : fallbackFuelFraction;
                    _liquidFundingCapture.Engines.Add(new(demand.EnginePart.InstanceId,
                        demand.EngineInstanceId, pool, demand.MassFlowKgS * fuelFraction,
                        demand.MassFlowKgS * (1.0 - fuelFraction), dt * fraction));
                }
            if (fraction < 1.0 || (fundedLF > 0 && fundedLF == remainingLF)
                || (fundedOx > 0 && fundedOx == remainingOx))
            {
                foreach (var demand in _liquidDemands)
                    RegisterEngineExhaustion(demand.EnginePart, dt * fraction);
            }
            fundedLF = System.Math.Min(remainingLF, fundedLF * fraction);
            fundedOx = System.Math.Min(remainingOx, fundedOx * fraction);

            if (fundedLF > 0.0 || fundedOx > 0.0)
            {
                double mainLF = System.Math.Min(fundedLF, totalLF);
                double mainOx = System.Math.Min(fundedOx, totalOx);
                double fromHeaderLF = fundedLF - mainLF;
                double fromHeaderOx = fundedOx - mainOx;
                // Describe the existing main-first allocator without changing its debits.
                // Each reactant switches independently; no header draw means main flow
                // lasts the entire funded interval, including exact main exhaustion.
                // Divide by the funded rate rather than multiplying a tiny mass ratio
                // by a long duration: the ratio itself can underflow to zero.
                _liquidFundingCapture?.SetEngineTiming(dt * fraction,
                    fromHeaderLF > 0 ? mainLF / (fundedLF / (dt * fraction)) : dt * fraction,
                    fromHeaderOx > 0 ? mainOx / (fundedOx / (dt * fraction)) : dt * fraction);
                for (int tankIndex = 0; tankIndex < tankPool.Count; tankIndex++)
                {
                    var p = tankPool[tankIndex];
                    double partHeaderLF = p.HeaderLiquidFuel;
                    double partHeaderOx = p.HeaderOxidizer;
                    if (totalLF > 0.0)
                        p.LiquidFuel = System.Math.Max(p.ReservedLiquidFuel, p.LiquidFuel - mainLF * (p.AvailableLiquidFuel / totalLF));
                    if (totalOx > 0.0)
                        p.Oxidizer = System.Math.Max(p.ReservedOxidizer, p.Oxidizer - mainOx * (p.AvailableOxidizer / totalOx));
                    if (fromHeaderLF > 0.0 && headerLF > 0.0)
                        p.DrawHeaderPropellant(oxidizer: false, fromHeaderLF * partHeaderLF / headerLF);
                    if (fromHeaderOx > 0.0 && headerOx > 0.0)
                        p.DrawHeaderPropellant(oxidizer: true, fromHeaderOx * partHeaderOx / headerOx);
                }
            }
        }

        FundSingleResourcePool(tankPool, PropellantResourceKind.SolidFuel, pool, dt);
        FundSingleResourcePool(tankPool, PropellantResourceKind.Monopropellant, pool, dt);
        _liquidFundingCapture?.EndPool(pool);
    }

    private void CollectSingleResourceDemand(Part part, PropellantResourceKind resource, double pressure)
    {
        if (part.HasEngineRuntime)
        {
            for (int i = 0; i < part.EngineStates.Count; i++)
            {
                double rate = part.GetEngineInstancePerformance(i, pressure).MassFlowKgS;
                if (rate > 0) _singleResourceDemands.Add(new(part, part.EngineStates[i].InstanceId, resource, rate));
            }
        }
        else
        {
            double rate = part.GetMassFlow(pressure);
            if (rate > 0) _singleResourceDemands.Add(new(part, part.InstanceId, resource, rate));
        }
    }

    private void FundSingleResourcePool(IReadOnlyList<Part> tanks, PropellantResourceKind resource,
        int pool, double dt)
    {
        if (!(dt > 0)) return;
        _singleResourceDemands.Sort(static (a, b) => StringComparer.Ordinal.Compare(a.EngineId, b.EngineId));
        double rate = 0, available = 0;
        foreach (var demand in _singleResourceDemands)
            if (demand.Resource == resource) rate += demand.MassFlowKgPerS;
        if (!(rate > 0)) return;
        foreach (var part in tanks)
            available += resource == PropellantResourceKind.SolidFuel ? part.SolidFuel : part.Monopropellant;
        double requested = rate * dt;
        double funded = System.Math.Min(available, requested);
        double duration = available < requested ? available / rate : dt;
        foreach (var demand in _singleResourceDemands)
        {
            if (demand.Resource != resource) continue;
            if (duration > 0) ResourceFundingCapture?.OtherEngines.Add(new(demand.Part.InstanceId,
                demand.EngineId, resource, pool, demand.MassFlowKgPerS, duration));
            if (available <= requested) RegisterEngineExhaustion(demand.Part, duration);
        }
        if (!(funded > 0)) return;
        foreach (var part in tanks)
        {
            double before = resource == PropellantResourceKind.SolidFuel ? part.SolidFuel : part.Monopropellant;
            double after = funded == available ? 0 : System.Math.Max(0, before - funded * (before / available));
            if (resource == PropellantResourceKind.SolidFuel) part.SolidFuel = after;
            else part.Monopropellant = after;
            ResourceFundingCapture?.RecordSingleResourceDebit(part, resource, before, after, duration);
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
    /// <summary>
    /// Part centres in the vessel frame. The origin is the lowest point of the assembled
    /// stack (the skirt datum): the same point the renderer draws at the vessel position,
    /// landing gear and water contact measure from, and the pad mount supports. Centre of
    /// mass, inertia tensor, engine mounts and attachment nodes are all expressed in it, so
    /// the physical and visible vehicle share one origin.
    /// </summary>
    public Dictionary<Part, Vector3d> ComputePartLocalPositions()
    {
        var positions = new Dictionary<Part, Vector3d>();
        AssignSkirtDatumPositions(positions);
        return positions;
    }

    /// <summary>
    /// Fills <paramref name="positions"/> with every attached part's centre, origin at the
    /// stack's lowest point. Shared by the cached physics-tick path and the public query so
    /// both report the same frame.
    /// </summary>
    private void AssignSkirtDatumPositions(Dictionary<Part, Vector3d> positions)
    {
        if (_root == null) return;
        AssignPositions(_root, Vector3d.Zero, positions);
        double lowest = double.PositiveInfinity;
        foreach (var (part, position) in positions)
            lowest = System.Math.Min(lowest, position.Y - part.Definition.LengthM * 0.5);
        if (!double.IsFinite(lowest) || lowest == 0.0) return;
        var shift = new Vector3d(0.0, -lowest, 0.0);
        // Iterate the part list, not the dictionary being updated (allocation-free).
        foreach (var part in _parts)
            if (positions.TryGetValue(part, out var position))
                positions[part] = position + shift;
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
        // Preserve joint order without allocating the Where/Select iterator or doing a
        // second joint search for each child.
        for (int jointIndex = 0; jointIndex < _joints.Count; jointIndex++)
        {
            var joint = _joints[jointIndex];
            if (!ReferenceEquals(joint.Parent, part)) continue;
            var child = joint.Child;
            var parentNode = FindAttachmentNode(part, joint.ParentNodeId);
            var childNode = FindAttachmentNode(child, joint.ChildNodeId);

            var pOff = parentNode != null
                ? new Vector3d(parentNode.Position[0], parentNode.Position[1], parentNode.Position[2])
                : Vector3d.Zero;
            var cOff = childNode != null
                ? new Vector3d(childNode.Position[0],  childNode.Position[1],  childNode.Position[2])
                : Vector3d.Zero;

            AssignPositions(child, pos + pOff - cOff, map);
        }
    }

    private static AttachmentNodeDef? FindAttachmentNode(Part part, string? nodeId)
    {
        if (string.IsNullOrEmpty(nodeId)) return null;
        var nodes = part.Definition.AttachmentNodes;
        for (int i = 0; i < nodes.Count; i++)
            if (nodes[i].Id == nodeId) return nodes[i];
        return null;
    }

    private List<Part> CollectSubtree(Part root)
    {
        var result = new List<Part>();
        CollectSubtreeInto(root, result, _stageQueueScratch);
        return result;
    }

    private void CollectSubtreeInto(
        Part root,
        List<Part> result,
        Queue<Part> queue)
    {
        result.Clear();
        queue.Clear();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var part = queue.Dequeue();
            result.Add(part);
            for (int jointIndex = 0; jointIndex < _joints.Count; jointIndex++)
            {
                var joint = _joints[jointIndex];
                if (ReferenceEquals(joint.Parent, part))
                    queue.Enqueue(joint.Child);
            }
        }
        queue.Clear();
    }
}
