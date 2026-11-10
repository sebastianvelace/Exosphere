namespace Exosphere.Simulation;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Exosphere.Simulation.Integrators;

public enum VesselDestructionCause
{
    None,
    GroundImpact,
    ThermalBreakup,
    StructuralBreakup,
    FlightTermination,
}

public class Vessel
{
    /// <summary>
    /// Fallback relative separation opening rate (m/s) used when a decoupler part does not
    /// declare <see cref="Parts.PartDefinition.SeparationImpulseNs"/>. Reproduces the legacy
    /// 1.0 m/s stage-opening behaviour.
    /// </summary>
    public const double DefaultSeparationOpeningMs = 1.0;

    public string Id   { get; }
    public string Name { get; set; } = "Unnamed Vessel";

    public PartGraph Parts { get; } = new();

    internal List<SloshTankRuntime> SloshStates { get; } = [];
    public IReadOnlyList<SloshTankRuntime> PropellantSloshStates => SloshStates;
    public string? LastSloshStatus { get; internal set; }

    /// <summary>Remaining-body mass properties with liquid modal displacement corrections.</summary>
    public RigidBodyMassProperties GetMassProperties() => SloshStates.Count == 0
        ? Parts.GetMassProperties() : SloshVesselState.Sample(this).Mass;

    /// <summary>Sets a declared liquid store's modal coordinates without altering its inventory.</summary>
    public void SetPropellantSloshState(ResourceStoreId store, SloshDisplacementState state)
    {
        var part = Parts.Parts.SingleOrDefault(p => p.InstanceId == store.PartInstanceId)
            ?? throw new ArgumentException("The modal source part is not owned by this vessel.", nameof(store));
        var tank = part.Definition.PropellantTanks.SingleOrDefault(t => t.Kind != PartMassComponentKind.SolidFuel
            && SloshStepModel.Resource(t.Kind) == store.Resource);
        if (store.HeaderIndex != null || tank == null)
            throw new ArgumentException("A mode requires a declared main liquid tank.", nameof(store));
        var parameters = SloshStepModel.Parameters(tank, SloshStepModel.StoreMass(part, store.Resource));
        state = state.Sample(parameters.CurvaturePerM).State;
        var runtime = new SloshTankRuntime(store, state, parameters);
        int index = SloshStates.FindIndex(s => s.Store == store);
        if (index < 0) SloshStates.Add(runtime); else SloshStates[index] = runtime;
    }


    public Vessel(string? id = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString() : id;
    }

    /// <summary>Creates a vessel with a stable identity for save/load roundtrips.</summary>
    public static Vessel CreateWithId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Vessel id must be non-empty.", nameof(id));
        return new Vessel(id);
    }

    // ── Estado cinemático (marco inercial, doble precisión) ───────────────
    public Vector3d    Position        { get; set; }
    public Vector3d    Velocity        { get; set; }
    public Quaterniond Orientation     { get; set; } = Quaterniond.Identity;
    public Vector3d    AngularVelocity { get; set; }  // rad/s, world space

    // ── Modo de física ────────────────────────────────────────────────────
    public bool            IsOnRails     { get; set; }
    public OrbitalElements? OrbitalState { get; set; }  // válido cuando IsOnRails = true
    public string?         ReferenceBodyId { get; set; } = "earth";

    // ── Controles ─────────────────────────────────────────────────────────
    public double    Throttle      { get; set; }           // [0, 1]
    /// <summary>
    /// Commanded main-tank vent flow (kg/s). The physics step removes it from the main tanks
    /// of every part in proportion to their loads (never from header tanks) until they are
    /// empty. The vented gas leaves at low speed; its reaction impulse is neglected.
    /// </summary>
    public double    PropellantVentRateKgPerS { get; set; }
    /// <summary>Total propellant vented overboard so far (kg), for mass bookkeeping.</summary>
    public double    VentedPropellantKg { get; set; }
    public Vector3d  PitchYawRoll  { get; set; }           // [-1, 1] por eje
    public bool      SASEnabled    { get; set; } = true;

    /// <summary>
    /// Optional body-flap command used by guidance modes that deliberately suppress the
    /// coupled TVC/RCS command while still driving the aerodynamic control surfaces. A null
    /// value keeps the normal pilot/autopilot command path shared with the engines.
    /// </summary>
    public Vector3d? FlapCommandOverride { get; set; }

    /// <summary>
    /// True when the last coupled step's flight control inverted the predicted aerodynamic
    /// moment (distributed model): guidance must not add its own static-moment trim on top.
    /// </summary>
    public bool FlightControlInvertsAerodynamics { get; private set; }

    /// <summary>Actual normalized body-flap deflection consumed by both integrators.</summary>
    public FlapActuatorState FlapActuators { get; private set; } = FlapActuatorState.Zero;

    /// <summary>0..1 attitude authority after structural damage (see <see cref="Flight.ControlAuthority"/>).</summary>
    public double ControlAuthorityFactor => Flight.ControlAuthority.Evaluate(this);

    /// <summary>True when structural damage left the vehicle without a command path.</summary>
    public bool StructuralControlLost => Flight.ControlAuthority.IsLost(ControlAuthorityFactor);

    // ── Hot-stage overlap (Ship lit while booster still attached) ─────────
    /// <summary>Sim seconds remaining in the dual-thrust window. Zero when inactive.</summary>
    public double HotStageOverlapRemaining { get; private set; }

    /// <summary>True while both stage clusters may produce thrust on one attached stack.</summary>
    public bool IsHotStageOverlapping => HotStageOverlapRemaining > 0.0 || Parts.HotStageOverlapActive;

    /// <summary>
    /// Set by <see cref="Tick"/> when the overlap timer expires; the game layer should then
    /// call mechanical stage and clear the flag.
    /// </summary>
    public bool HotStageOverlapCompletedPending { get; set; }

    /// <summary>Opt-in water entry; only a declared open-water region bypasses the rigid surface fallback.</summary>
    public WaterContactDefinition? WaterContact { get; set; }
    public WaterContactWrench? LastWaterContact { get; internal set; }
    /// <summary>Opt-in mass-centred water motion, activated only by accepted physical contact.</summary>
    public bool WaterMotionEnabled { get; set; }
    public Coupled6DofTelemetry? LastWaterMotionTelemetry { get; internal set; }

    // ── Physical landing contact ─────────────────────────────────────────
    private ContactPointDefinition[] _landingContactPoints = [];
    public IReadOnlyList<ContactPointDefinition> LandingContactPoints => _landingContactPoints;
    public ContactWrench? LastSurfaceContact { get; internal set; }
    public Vector3d LastContactForceWorld { get; internal set; } = Vector3d.Zero;
    public Vector3d LastContactTorqueWorld { get; internal set; } = Vector3d.Zero;
    public double SurfaceSettledDuration { get; internal set; }
    public bool IsSurfaceSettled { get; internal set; }
    public bool HasSurfaceContact => LastSurfaceContact?.ContactCount > 0;
    public bool HasDeployedLandingGear => _landingContactPoints.Length > 0
        && Parts.Parts.Any(p => p.Definition.Category == PartCategory.Landing && p.IsDeployed);

    // ── Tower catch (Mechazilla chopstick pins) ───────────────────────────
    private ContactPointDefinition[] _catchContactPoints = [];
    public IReadOnlyList<ContactPointDefinition> CatchContactPoints => _catchContactPoints;
    /// <summary>True when this vessel carries tower catch-pin hardpoints at all — a
    /// vehicle without them (every part but the V3 ship's catch-equipped nose) can never
    /// be caught, regardless of mission mode or approach quality.</summary>
    public bool HasCatchPins => _catchContactPoints.Length > 0;
    public ContactWrench? LastCatchContact { get; internal set; }
    /// <summary>Last tower-contact gate result, retained for flight diagnostics.</summary>
    public double LastCatchEvaluationRangeM { get; internal set; } = double.NaN;
    public bool LastCatchEvaluationPassedGate { get; internal set; }
    public double CatchSettledDuration { get; internal set; }
    public bool IsCaught { get; internal set; }
    /// <summary>Set by the game layer when this vessel is flying a return-to-launch-site
    /// catch approach. Gates both the EDL guidance's position-error term and the (cheap)
    /// per-frame catch-contact evaluation, so an ordinary reentry never pays for either.</summary>
    public bool IsAttemptingTowerCatch { get; set; }
    /// <summary>
    /// Marks the deterministic scripted reentry demonstration. It permits the game-layer EDL
    /// director to hold the broadside presentation while validating the tower path; manual and
    /// ordinary flight reentries never use this presentation-only stabilizer.
    /// </summary>
    public bool IsTowerCatchDemonstration { get; set; }
    /// <summary>Inertial position of the catch cradle right now, refreshed each frame by the
    /// game layer from <c>LaunchComplexSpec.GetCatchCradlePosition</c> — <see cref="Universe"/>
    /// stays free of any launch-site/JSON lookup this way, matching the boundary already
    /// drawn for every other sim/game-layer interaction.</summary>
    public Vector3d CatchTargetPositionWorld { get; set; }
    public Vector3d CatchTargetUpWorld { get; set; } = Vector3d.Up;
    public Vector3d CatchTargetVelocityWorld { get; set; }
    /// <summary>Simulation epoch at which <see cref="CatchTargetPositionWorld"/> was sampled.</summary>
    public double CatchTargetEpochSeconds { get; set; } = double.NaN;

    /// <summary>
    /// Predicts the inertial cradle position at a physics substep. The game layer refreshes
    /// the target once per frame; the pure simulation must still account for the rotating
    /// launch site's motion while a warp frame is split into several integration steps.
    /// </summary>
    public Vector3d GetCatchTargetPositionAt(double simulationTime)
    {
        if (!double.IsFinite(CatchTargetEpochSeconds)
            || !double.IsFinite(simulationTime))
            return CatchTargetPositionWorld;
        return CatchTargetPositionWorld
            + CatchTargetVelocityWorld * (simulationTime - CatchTargetEpochSeconds);
    }
    /// <summary>True when the vessel carries at least one parachute part.</summary>
    public bool HasParachutes => Parts.Parts.Any(p => p.Definition.DragChute > 0.0);

    /// <summary>
    /// Pilot arming of the parachute sequence. While armed and descending, the simulation
    /// opens each canopy as soon as its declared altitude and pressure envelope is met,
    /// exactly as a staged chute does; arming never deploys outside that envelope.
    /// </summary>
    public bool ParachutesArmed { get; set; }

    public bool HasDeployedParachute => Parts.Parts.Any(
        p => p.IsDeployed && p.Definition.DragChute > 0.0);
    public double MaximumSplashdownSpeedMps => Parts.Parts
        .Where(p => p.Definition.SplashdownCapable)
        .Select(p => p.Definition.MaxSplashdownSpeedMps)
        .DefaultIfEmpty(0.0)
        .Max();

    // ── Ground hold (pre-launch hold-down) ────────────────────────────────
    public bool     IsGroundHeld          { get; set; }
    public Vector3d GroundNormal          { get; set; }  // unit vector from body centre → spawn point
    public double   GroundOffset          { get; set; }  // height above body surface at spawn (m)

    // ── Crash / destruction state ─────────────────────────────────────────
    public bool     IsDestroyed           { get; set; } = false;
    public VesselDestructionCause DestructionCause { get; set; } = VesselDestructionCause.None;
    public double   CrashImpactSpeed      { get; set; } = 0.0;   // m/s relative to surface
    public Vector3d CrashSimPosition      { get; set; } = Vector3d.Zero; // sim position of impact

    public void ReleaseGroundHold() => IsGroundHeld = false;

    /// <summary>
    /// Clears transient flight state before a debug/navigation teleport. A circular orbit
    /// jump supplies a new position and velocity, so retaining an old conic epoch or angular
    /// momentum would make the next tick propagate from the wrong body and visibly spin the
    /// vehicle even when the pilot has released the controls.
    /// </summary>
    public void PrepareForTeleport()
    {
        IsGroundHeld = false;
        IsSurfaceSettled = false;
        SurfaceSettledDuration = 0.0;
        IsOnRails = false;
        OrbitalState = null;
        // A navigation jump always starts as an unpowered coast. The caller may arm
        // a new burn explicitly after the destination state has been installed, but
        // an old throttle command must never survive the discontinuity for one tick.
        Throttle = 0.0;
        foreach (var part in Parts.Parts)
            if (part.HasEngineRuntime)
                part.ResetEngineRuntimeForTeleport();
        AngularVelocity = Vector3d.Zero;
        PitchYawRoll = Vector3d.Zero;
        LastSurfaceContact = null;
        LastContactForceWorld = Vector3d.Zero;
        LastContactTorqueWorld = Vector3d.Zero;
        LastCatchContact = null;
        LastCatchEvaluationRangeM = double.NaN;
        LastCatchEvaluationPassedGate = false;
        CatchTargetEpochSeconds = double.NaN;
        CatchSettledDuration = 0.0;
        IsCaught = false;
        IsAttemptingTowerCatch = false;
        IsTowerCatchDemonstration = false;
    }

    /// <summary>
    /// Opens the dual-thrust hot-stage window: upper engines join <see cref="Parts.ActiveEngines"/>
    /// and drain their own tanks while the booster remains attached.
    /// </summary>
    public void BeginHotStageOverlap(double durationSeconds)
    {
        if (durationSeconds <= 0.0) return;
        HotStageOverlapRemaining = durationSeconds;
        Parts.HotStageOverlapActive = true;
        HotStageOverlapCompletedPending = false;
    }

    /// <summary>Advances the overlap timer. Returns true the first frame the window just ended.</summary>
    public bool AdvanceHotStageOverlap(double dt)
    {
        if (!Parts.HotStageOverlapActive && HotStageOverlapRemaining <= 0.0)
            return false;

        HotStageOverlapRemaining -= dt;
        if (HotStageOverlapRemaining > 0.0) return false;

        HotStageOverlapRemaining = 0.0;
        Parts.HotStageOverlapActive = false;
        HotStageOverlapCompletedPending = true;
        return true;
    }

    /// <summary>
    /// Builds the aggregate foot ring declared by the installed landing-gear part. The current
    /// Starship renderer uses a skirt datum rather than the part-graph root, so the data stores
    /// both the visible point offset and its physical moment arm from the CoM explicitly.
    /// </summary>
    public void ConfigureLandingContactsFromParts()
    {
        var gear = Parts.Parts.FirstOrDefault(p =>
            p.Definition.Category == PartCategory.Landing
            && p.Definition.ContactPointCount > 0);
        if (gear == null)
        {
            _landingContactPoints = [];
            return;
        }

        var def = gear.Definition;
        int count = System.Math.Max(1, def.ContactPointCount);
        double ring = System.Math.Max(0.0, def.ContactRingRadiusM);
        // Lateral damping must be much softer than axial suspension damping: applying the
        // axial coefficient at a ~29 m CoM arm creates an artificial overturning impulse.
        // Coulomb friction still caps the force at the declared dynamic coefficient.
        double tangentialDamping = System.Math.Max(0.0, def.DamperStrength * 0.05);
        double friction = def.DynamicFriction > 0.0
            ? def.DynamicFriction
            : System.Math.Max(0.0, def.StaticFriction);
        _landingContactPoints = Enumerable.Range(0, count).Select(i =>
        {
            double angle = i * 2.0 * System.Math.PI / count;
            return new ContactPointDefinition(
                Name: $"{def.Id}-foot-{i}",
                LocalPositionFromDatum: new Vector3d(
                    ring * System.Math.Cos(angle),
                    def.ContactOffsetYM,
                    ring * System.Math.Sin(angle)),
                ContactRadiusM: System.Math.Max(0.0, def.ContactRadiusM),
                SpringStiffnessNPerM: System.Math.Max(0.0, def.SpringStrength),
                DampingNsPerM: System.Math.Max(0.0, def.DamperStrength),
                TangentialDampingNsPerM: tangentialDamping,
                FrictionCoefficient: friction,
                MaxCompressionM: System.Math.Max(0.0, def.SuspensionTravelM),
                MaxLoadN: System.Math.Max(0.0, def.MaxLoad));
        }).ToArray();
    }

    // Contact tuning for a mechanical tower catch: much stiffer/less compliant than a
    // landing leg's suspension, because the arms are a rigid grab, not a spring strut.
    // These are first-order constants (not per-part JSON) since the compliance being
    // modelled belongs to the tower's catch mechanism, not the vessel.
    private const double CatchPinSpringStiffnessNPerM = 1.2e6;
    private const double CatchPinDampingNsPerM = 5.0e5;
    private const double CatchPinTangentialDampingNsPerM = 1.0e5;
    private const double CatchPinFrictionCoefficient = 0.6;
    private const double CatchPinMaxCompressionM = 0.6;
    private const double CatchPinMaxLoadN = 3.0e6;

    /// <summary>
    /// Builds the two catch-pin contact points from whichever part declares
    /// <see cref="PartDefinition.CatchPinLateralOffsetM"/> &gt; 0 (today, only the V3 ship's
    /// nose section). Reuses <see cref="PartGraph.ComputePartLocalPositions"/> — the same
    /// vessel-datum-relative positions already used for CoM and rendering — so the pins
    /// track that part's real position in the assembled stack instead of a hardcoded offset
    /// that would break the moment a different vehicle configuration is flown.
    /// </summary>
    public void ConfigureCatchContactsFromParts()
    {
        var pinPart = Parts.Parts.FirstOrDefault(p => p.Definition.CatchPinLateralOffsetM > 0.0);
        if (pinPart == null)
        {
            _catchContactPoints = [];
            return;
        }

        var positions = Parts.ComputePartLocalPositions();
        if (!positions.TryGetValue(pinPart, out var partPosition))
        {
            _catchContactPoints = [];
            return;
        }

        var def = pinPart.Definition;
        double y = partPosition.Y + def.CatchPinOffsetYM;
        double lateral = def.CatchPinLateralOffsetM;
        double radius = System.Math.Max(0.0, def.CatchPinRadiusM);
        _catchContactPoints =
        [
            new ContactPointDefinition(
                Name: $"{def.Id}-catch-pin-left",
                LocalPositionFromDatum: new Vector3d(0.0, y, -lateral),
                ContactRadiusM: radius,
                SpringStiffnessNPerM: CatchPinSpringStiffnessNPerM,
                DampingNsPerM: CatchPinDampingNsPerM,
                TangentialDampingNsPerM: CatchPinTangentialDampingNsPerM,
                FrictionCoefficient: CatchPinFrictionCoefficient,
                MaxCompressionM: CatchPinMaxCompressionM,
                MaxLoadN: CatchPinMaxLoadN),
            new ContactPointDefinition(
                Name: $"{def.Id}-catch-pin-right",
                LocalPositionFromDatum: new Vector3d(0.0, y, lateral),
                ContactRadiusM: radius,
                SpringStiffnessNPerM: CatchPinSpringStiffnessNPerM,
                DampingNsPerM: CatchPinDampingNsPerM,
                TangentialDampingNsPerM: CatchPinTangentialDampingNsPerM,
                FrictionCoefficient: CatchPinFrictionCoefficient,
                MaxCompressionM: CatchPinMaxCompressionM,
                MaxLoadN: CatchPinMaxLoadN),
        ];
    }

    public RigidBodyContactInput GetContactInput(Vector3d position, Vector3d velocity)
    {
        // The part frame and the contact datum share one origin, so the real centre of mass
        // gives the feet's moment arms (it replaces the gear's declared CoM estimate).
        var centerOfMassOffset = Orientation.Rotate(CenterOfMassOffsetLocal);
        return new(
            DatumPositionWorld: position,
            CenterOfMassPositionWorld: position + centerOfMassOffset,
            // The integrator advances the vessel datum with velocity. Transport that
            // velocity to the CoM before the solver transports it back to each foot.
            CenterOfMassVelocityWorld: velocity + AngularVelocity.Cross(centerOfMassOffset),
            Orientation: Orientation,
            AngularVelocityWorld: AngularVelocity);
    }

    /// <summary>
    /// Builds contact input from the coupled integrator's centre-of-mass state. The returned
    /// datum is reconstructed from the PartGraph centre of mass, so contact witnesses remain
    /// attached to the vessel while RK4 evaluates intermediate orientations.
    /// </summary>
    public RigidBodyContactInput GetContactInput(in RigidBody6DofState state)
    {
        var comFromDatumWorld = state.Orientation.Rotate(Parts.CenterOfMass);
        var datumPosition = state.Position - comFromDatumWorld;
        var angularVelocityWorld = state.AngularVelocityWorld;
        var datumVelocity = state.Velocity
            - angularVelocityWorld.Cross(comFromDatumWorld);
        return new RigidBodyContactInput(
            DatumPositionWorld: datumPosition,
            CenterOfMassPositionWorld: state.Position,
            CenterOfMassVelocityWorld: state.Velocity,
            Orientation: state.Orientation,
            AngularVelocityWorld: angularVelocityWorld);
    }

    // ── Tripulación ───────────────────────────────────────────────────────
    public List<CrewMember> Crew { get; } = new();

    // ── Propiedades calculadas ─────────────────────────────────────────────
    public double    TotalMass     => Parts.TotalMass;

    /// <summary>
    /// Mass (kg) of vessels hard-docked to this one as secondaries. <see cref="Universe"/>
    /// refreshes it before each integration step; docked secondaries are slaved to this
    /// vessel, so its thrust, drag and contact forces must accelerate the whole stack.
    /// </summary>
    public double DockedSecondaryMassKg { get; internal set; }

    /// <summary>Mass the translational forces of this vessel accelerate (kg).</summary>
    public double TranslationalMassKg => TotalMass + DockedSecondaryMassKg;

    /// <summary>
    /// Transverse moment of inertia (kg·m²) of the whole docked stack about its combined
    /// centre of mass, or 0 when nothing is docked. <see cref="Universe"/> refreshes it with
    /// <see cref="DockedSecondaryMassKg"/>.
    /// </summary>
    public double DockedStackTransverseInertiaKgM2 { get; internal set; }

    /// <summary>
    /// Centre of mass of the whole docked stack in this vessel's frame, or null when nothing is
    /// docked. RCS torques act about it: a jet aft of the primary's own centre can sit forward
    /// of the stack's (CSM-107 quads at X_A 959 in, CSM CG 934 in, CSM+LM CG 1038 in), which
    /// reverses its moment. <see cref="Universe"/> refreshes it with <see cref="DockedSecondaryMassKg"/>.
    /// </summary>
    public Vector3d? DockedStackCenterOfMassLocal { get; internal set; }

    /// <summary>Axial (roll) moment of inertia of the docked stack (kg·m²), or 0 when undocked.</summary>
    public double DockedStackAxialInertiaKgM2 { get; internal set; }

    /// <summary>Torque reference for RCS allocation: the docked stack's centre, else this vessel's.</summary>
    private Vector3d RcsTorqueReferenceLocal => DockedStackCenterOfMassLocal ?? CenterOfMassOffsetLocal;

    /// <summary>Allocation weights (inverse-inertia ratios) of the body that the jets actually turn.</summary>
    private (double Transverse, double Axial) RcsInertia =>
        DockedStackCenterOfMassLocal != null && DockedStackTransverseInertiaKgM2 > 0.0 && DockedStackAxialInertiaKgM2 > 0.0
            ? (DockedStackTransverseInertiaKgM2, DockedStackAxialInertiaKgM2)
            : (Parts.TransverseMomentOfInertia, Parts.AxialMomentOfInertia);

    /// <summary>
    /// Factor that turns this vessel's own torque/inertia angular acceleration into the
    /// docked stack's: I_vessel / I_stack (1 when undocked). The same factor is applied to
    /// every axis, a documented approximation for the slaved secondary.
    /// </summary>
    private double DockedInertiaScale()
    {
        double own = Parts.TransverseMomentOfInertia;
        double stack = DockedStackTransverseInertiaKgM2;
        return own > 0.0 && stack > own ? own / stack : 1.0;
    }
    public Vector3d  CenterOfMass  => Position + Orientation.Rotate(CenterOfMassOffsetLocal);
    /// <summary>Material velocity at the instantaneous centroid; excludes relative liquid flow.</summary>
    public Vector3d  CenterOfMassVelocity => Velocity + AngularVelocity.Cross(Orientation.Rotate(CenterOfMassOffsetLocal));
    /// <summary>Mean velocity of a closed body's moving mass, for the next unforced conic.</summary>
    public Vector3d CenterOfMassMeanVelocity => CenterOfMassVelocity + (SloshStates.Count == 0 ? Vector3d.Zero
        : Orientation.Rotate(SloshVesselState.Sample(this).Internal.LinearBodyKgMps / TotalMass));
    /// <summary>Centre of mass in the vessel frame (zero for a part-less point mass).</summary>
    public Vector3d  CenterOfMassOffsetLocal => Parts.TotalMass > 0.0
        // Preserve the established summation order for a vessel without modal geometry.
        ? SloshStates.Count == 0 ? Parts.CenterOfMass : GetMassProperties().CenterOfMassBody
        : Vector3d.Zero;

    /// <summary>
    /// Conic of this vessel's centre of mass about <paramref name="body"/> at
    /// <paramref name="epoch"/>: the state <see cref="OrbitalState"/> must describe.
    /// </summary>
    public OrbitalElements ComputeCenterOfMassOrbit(CelestialBody body, double epoch) =>
        OrbitalElements.FromStateVector(CenterOfMass - body.Position,
            CenterOfMassMeanVelocity - body.Velocity, body.GM, body.Id, epoch);
    public double    VehicleLength => Parts.VehicleLength;
    public double    MaximumDiameter => Parts.MaximumDiameter;
    public double    NoseRadius      => Parts.NoseRadius;

    public double GetAltitude(CelestialBody body) =>
        body.GetAltitude(Position);

    /// <summary>Local gravitational acceleration from one body at the vessel position.</summary>
    public double GetLocalGravity(CelestialBody body) =>
        body.GetGravityAt(Position).Magnitude;

    /// <summary>
    /// Weight force (N) in the selected body's local gravity field. Mass remains invariant;
    /// weight changes with body and altitude.
    /// </summary>
    public double GetWeightNewtons(CelestialBody body) =>
        TotalMass * GetLocalGravity(body);

    /// <summary>Pressure- and altitude-corrected thrust-to-local-weight ratio.</summary>
    public double GetThrustToWeightRatio(CelestialBody body)
    {
        double weight = GetWeightNewtons(body);
        return weight > 0.0 ? GetCurrentThrust(body) / weight : 0.0;
    }

    // Velocidad relativa a la superficie (para aerodinámica)
    /// <summary>
    /// Velocity of the vehicle relative to the rotating surface: the centre-of-mass velocity
    /// (what navigation estimates and what loads the whole airframe), not the datum's, which
    /// adds ω×r while the vehicle rotates. Altitude queries keep using the datum (the skirt).
    /// </summary>
    public Vector3d GetSurfaceVelocity(CelestialBody body)
    {
        var centerOfMass = CenterOfMass;
        return CenterOfMassVelocity - body.Velocity - body.GetSurfaceVelocity(centerOfMass);
    }

    /// <summary>
    /// Centre-of-mass velocity relative to the local air (<see cref="CelestialBody.GetAirVelocity"/>):
    /// what drag, lift, dynamic pressure, Mach, heating and air data use. Equal to
    /// <see cref="GetSurfaceVelocity(CelestialBody)"/> until the environment adds wind.
    /// </summary>
    public Vector3d GetAirRelativeVelocity(CelestialBody body) =>
        body.GetAirRelativeVelocity(CenterOfMass, CenterOfMassVelocity);

    // Presión dinámica q = ½·ρ·v² (Pa) respecto a la atmósfera en rotación. Es la carga
    // aerodinámica que define el "Max-Q" del ascenso y escala el arrastre y los momentos.
    public double GetDynamicPressure(CelestialBody body)
    {
        if (body.Atmosphere == null) return 0.0;
        double density = body.AtmosphericDensityAt(Position, GetAltitude(body));
        if (density <= 0.0) return 0.0;
        double speed = GetAirRelativeVelocity(body).Magnitude;
        return 0.5 * density * speed * speed;
    }

    /// <summary>
    /// Proper acceleration felt by crew and structure (m/s²), excluding gravity because
    /// free fall is weightless. While held on a surface, the support reaction balances local
    /// gravity, so a stationary astronaut correctly feels approximately 1 g.
    /// </summary>
    public Vector3d GetProperAcceleration(CelestialBody body)
    {
        if (IsGroundHeld)
            return -body.GetGravityAt(Position);
        if (TotalMass <= 0.0) return Vector3d.Zero;
        return (ComputeThrust(body) + ComputeDrag(body) + LastContactForceWorld) / TotalMass;
    }

    // ── Read-only engine telemetry for the HUD ────────────────────────────
    // Thin wrappers that resolve the live ambient pressure from the reference body and defer
    // to PartGraph, so the HUD reads one obvious call and never touches the sim or the thrust
    // equation. All are pressure-corrected for the vessel's current altitude.

    public double GetAmbientPressure(CelestialBody? body) =>
        body?.GetAtmosphericPressure(Position) ?? 0.0;

    /// <summary>Engines of the current stage that are lit right now.</summary>
    public int ActiveEngineCount => Parts.ActiveEngineCount;

    /// <summary>True when the current stage has at least one healthy active engine part.</summary>
    public bool HasActiveEngineParts => Parts.HasActiveEngineParts;

    /// <summary>Total pressure-corrected thrust (N) at the vessel's current altitude.</summary>
    public double GetCurrentThrust(CelestialBody? body) =>
        Parts.GetCurrentThrust(GetAmbientPressure(body));

    /// <summary>Maximum pressure-corrected thrust available from the current stage.</summary>
    public double GetMaximumThrust(CelestialBody? body) =>
        Parts.GetMaximumThrust(GetAmbientPressure(body));

    /// <summary>Effective cluster specific impulse (s) right now.</summary>
    public double GetCurrentIsp(CelestialBody? body) =>
        Parts.GetCurrentIsp(GetAmbientPressure(body));

    /// <summary>Current propellant mass flow in tonnes per second (HUD-friendly units).</summary>
    public double GetCurrentMassFlowTps(CelestialBody? body) =>
        Parts.GetCurrentMassFlow(GetAmbientPressure(body)) / 1000.0;

    /// <summary>Per-engine telemetry rows (throttle, thrust N, mass flow kg/s).</summary>
    public IEnumerable<EngineReadout> GetEngineReadouts(CelestialBody? body) =>
        Parts.GetEngineReadouts(GetAmbientPressure(body));

    /// <summary>
    /// Fills a caller-owned engine telemetry buffer without allocating a new collection.
    /// Presentation consumers use this when sampling at a fixed visual cadence.
    /// </summary>
    public void FillEngineReadouts(CelestialBody? body, List<EngineReadout> destination) =>
        Parts.FillEngineReadouts(GetAmbientPressure(body), destination);

    /// <summary>
    /// Fills presentation telemetry using a caller-owned ambient-pressure sample. This is
    /// intentionally separate from the live-body overload: renderers may reuse a bounded
    /// visual sample without making the physical pressure query part of their VFX cadence.
    /// It does not mutate vessel or engine state.
    /// </summary>
    public void FillEngineReadoutsAtPressure(
        List<EngineReadout> destination,
        double ambientPressurePa) =>
        Parts.FillEngineReadouts(ambientPressurePa, destination);

    /// <summary>
    /// Fills engine telemetry and returns the aggregate values computed during that same
    /// pass. This avoids repeating the active-stage thrust/mass-flow queries in HUD code.
    /// </summary>
    public void FillEngineReadouts(
        CelestialBody? body,
        List<EngineReadout> destination,
        out EngineTelemetrySummary summary) =>
        Parts.FillEngineReadouts(GetAmbientPressure(body), destination, out summary);

    /// <summary>Δv (m/s) of the current stage as loaded, at the current effective Isp.</summary>
    public double GetCurrentStageDeltaV(CelestialBody? body) =>
        Parts.GetCurrentStageDeltaV(GetAmbientPressure(body));

    /// <summary>Δv (m/s) for an arbitrary wet/dry mass pair at the current effective Isp.</summary>
    public double GetStageDeltaV(double wetMass, double dryMass, CelestialBody? body) =>
        Parts.GetStageDeltaV(wetMass, dryMass, GetAmbientPressure(body));

    // ── Fuerzas ───────────────────────────────────────────────────────────

    // Avanza el spool de cada motor activo hacia el throttle comandado (Vessel.Throttle).
    // Se llama UNA VEZ por tick de física (en Tick()), antes de que RK4 muestree las fuerzas.
    // RK4 luego lee ThrottleLevel tal cual está — sin avanzar el spool de nuevo — para que
    // los cuatro subpasos k₁…k₄ usen el mismo nivel de empuje dentro de un mismo tick.
    private void ApplyThrottle(double dt)
    {
        foreach (var engine in Parts.ActiveEngineList)
            engine.AdvanceEngineRuntime(Throttle, dt);
    }

    // Empuje total en world space (N) — empuje de vacío (compatibilidad).
    public Vector3d ComputeThrust()
    {
        return Orientation.Rotate(Parts.GetTotalThrust() + RcsThrusterForceBody);
    }

    // Empuje total en world space (N), corregido por presión ambiente del cuerpo.
    public Vector3d ComputeThrust(CelestialBody? refBody)
    {
        double pressure = refBody?.GetAtmosphericPressure(Position) ?? 0.0;
        return Orientation.Rotate(Parts.GetTotalThrust(pressure) + RcsThrusterForceBody);
    }

    /// <summary>
    /// Translation reaction-control command per body axis in [−1, 1] (local X, Y = nose, Z),
    /// a fraction of the declared thrusters' force along that axis. Independent of the
    /// engines: it is how a spacecraft brakes toward a target it points at.
    /// </summary>
    public Vector3d TranslationCommand { get; set; }

    /// <summary>
    /// External torque (N·m, body axes) from a failure, such as a thruster stuck on. It
    /// bypasses the flight-control law and lasts until cleared or its propellant runs out.
    /// </summary>
    public Vector3d DisturbanceTorqueBody { get; set; }
    /// <summary>
    /// Where the disturbance flow (a stuck thruster) leaves the vehicle, skirt-datum body frame,
    /// and its outward exhaust direction. Required for variable-mass transport of that flow.
    /// </summary>
    public Vector3d? DisturbanceOutletBodyM { get; set; }
    public Vector3d? DisturbanceExhaustDirectionBody { get; set; }

    /// <summary>Monopropellant (kg/s) the disturbance source burns; it stops when the supply is gone.</summary>
    public double DisturbanceMassFlowKgPerS { get; set; }

    /// <summary>True once the RCS thrusters could not be fed (propellant exhausted).</summary>
    public bool RcsPropellantDepleted { get; private set; }

    /// <summary>
    /// Body-frame force (N) the translation command asks of the declared thrusters: the
    /// clamped command times the thrusters' force authority along each axis.
    /// </summary>
    private Vector3d DesiredRcsTranslationForceBody()
    {
        var command = TranslationCommand;
        if (command.MagnitudeSquared < 1e-12 || IsDestroyed || StructuralControlLost)
            return Vector3d.Zero;
        var thrusters = Parts.GetRcsThrusters();
        if (thrusters.Count == 0) return Vector3d.Zero;
        var authority = Physics.RcsAllocation.TranslationAuthority(thrusters);
        return new Vector3d(
            System.Math.Clamp(command.X, -1.0, 1.0) * authority.X,
            System.Math.Clamp(command.Y, -1.0, 1.0) * authority.Y,
            System.Math.Clamp(command.Z, -1.0, 1.0) * authority.Z);
    }

    /// <summary>
    /// Practice assist: tanks never run down. The engines still go through their normal
    /// consumption step (flameout, feed and hot-stage logic stay identical); the propellant
    /// is put back afterwards.
    /// </summary>
    public bool InfinitePropellant { get; set; }

    /// <summary>Capture the complete gross resource inventory and attributed preparation debits.</summary>
    public bool CaptureResourceFunding { get; set; }
    public Physics.ResourceFundingWindow? LastResourceFunding { get; private set; }

    /// <summary>
    /// Set by the coupled integrator for the step it is about to prepare: the window then feeds
    /// variable-mass transport (F4) even when the public debug capture is off.
    /// </summary>
    internal bool CaptureTransportWindow { get; set; }
    internal Physics.ResourceFundingWindow? TransportWindow { get; private set; }
    /// <summary>"active" when the last coupled step integrated variable-mass transport, else why not.</summary>
    public string? LastVariableMassStatus { get; internal set; }
    /// <summary>End-of-step open-system transport (relative momenta, force, moment) of that step.</summary>
    public Physics.OpenSystemTransportSample? LastTransportSample { get; internal set; }
    /// <summary>
    /// Feed-flow relative momenta (body frame) at the end of the last coupled step, carried to
    /// the next step so a flow change conserves total momentum. Null: unknown (no impulse applied).
    /// A new vessel starts with no flow (zero). Save V2 also carries per-store datum moments
    /// so fragmentation can preserve each feed's momentum and its reference point.
    /// </summary>
    internal Vector3d? TransportRelativeMomentumBody { get; set; } = Vector3d.Zero;
    internal Vector3d? TransportRelativeAngularMomentumBody { get; set; } = Vector3d.Zero;
    internal List<Physics.TransportMomentumShare>? TransportMomentumShares { get; set; }
    internal Vector3d[] TransportLinearScratch = [];
    internal Vector3d[] TransportAngularScratch = [];

    internal void EnsureLegacyTransportMomentumShares(Vector3d centerBody)
    {
        if (TransportMomentumShares != null || TransportRelativeMomentumBody is not { } linear
            || TransportRelativeAngularMomentumBody is not { } angular) return;
        TransportMomentumShares = [];
        if (linear != Vector3d.Zero || angular != Vector3d.Zero)
            TransportMomentumShares.Add(new(null, linear, angular + centerBody.Cross(linear)));
    }

    internal bool TryGetTransportMomentum(Vector3d centerBody, out Vector3d linear, out Vector3d angular)
    {
        if (TransportMomentumShares is { } shares)
        {
            linear = Vector3d.Zero; angular = Vector3d.Zero;
            foreach (var share in shares)
            {
                linear += share.LinearBodyKgMps;
                angular += share.AngularAboutDatumBodyKgM2PerS;
            }
            angular -= centerBody.Cross(linear);
            return true;
        }
        linear = TransportRelativeMomentumBody ?? Vector3d.Zero;
        angular = TransportRelativeAngularMomentumBody ?? Vector3d.Zero;
        return TransportRelativeMomentumBody.HasValue && TransportRelativeAngularMomentumBody.HasValue;
    }

    private void BeginResourceFunding(double dt)
    {
        LastResourceFunding = null;
        TransportWindow = null;
        Parts.ResourceFundingCapture = CaptureResourceFunding || CaptureTransportWindow
            ? new Physics.ResourceFundingCapture(Parts.PartList, dt, InfinitePropellant, Parts.TopologyVersion) : null;
    }

    private void CompleteResourceFunding()
    {
        var window = Parts.ResourceFundingCapture?.Complete(Parts.PartList, Parts.TopologyVersion);
        if (CaptureResourceFunding) LastResourceFunding = window;
        if (CaptureTransportWindow) TransportWindow = window;
        Parts.ResourceFundingCapture = null;
    }

    private void ConsumePropellants(double dt, double pressure)
    {
        var snapshot = InfinitePropellant ? Parts.CapturePropellant() : null;
        Parts.ConsumePropellant(dt, pressure, InfinitePropellant);
        if (PropellantVentRateKgPerS > 0.0 && dt > 0.0)
            VentedPropellantKg += Parts.VentMainPropellant(PropellantVentRateKgPerS * dt, dt, InfinitePropellant);
        if (DisturbanceMassFlowKgPerS > 0.0
            && !Parts.DrawMonopropellant(DisturbanceMassFlowKgPerS * dt, primaryStoresOnly: true))
        {
            // A stuck thruster stops when its feed runs dry.
            DisturbanceTorqueBody = Vector3d.Zero;
            DisturbanceMassFlowKgPerS = 0.0;
        }
        Parts.ResourceFundingCapture?.RecordCurrentStores(Parts.PartList,
            Physics.ResourceWithdrawalSource.Disturbance, Physics.PropellantResourceKind.Monopropellant);
        _propellantSnapshot = snapshot;
    }

    /// <summary>Tank loads to put back after this step's RCS draw (infinite-propellant assist).</summary>
    private double[]? _propellantSnapshot;

    private void RestoreInfinitePropellant()
    {
        if (_propellantSnapshot != null) Parts.RestorePropellant(_propellantSnapshot);
        _propellantSnapshot = null;
    }

    // Fuerza aerodinámica total (drag + lift de cuerpo) en world space (N) — estado actual.
    public Vector3d ComputeDrag(CelestialBody body) =>
        ComputeDragAt(Position, Velocity, Orientation, body);

    // Fuerza aerodinámica evaluada en un estado (pos, vel) arbitrario (para subpasos RK4).
    // Delega en AerodynamicsModel: drag orientación-dependiente (cilindro de 9 m, Cd y área
    // blend axial↔broadside, pico transónico) más la sustentación de cuerpo CL=CLmax·sin(2α).
    /// <summary>
    /// Static aerodynamic moment (body frame, N·m) on the current state from the distributed
    /// model, evaluated with ω = 0 so the flight-control inversion leaves the natural
    /// aerodynamic rate damping in place. Zero outside the atmosphere or for vehicles that
    /// keep a capsule/standalone aero model.
    /// </summary>
    internal Vector3d PredictStaticAerodynamicTorqueBody(CelestialBody body, bool includeBodyFlaps = true)
    {
        if (body.Atmosphere == null || !Parts.UsesDistributedAerodynamics || Parts.TotalMass <= 0.0)
            return Vector3d.Zero;
        var com = CenterOfMass;
        double altitude = body.GetAltitude(com);
        double density = body.AtmosphericDensityAt(com, altitude);
        if (density <= 0.0) return Vector3d.Zero;
        var air = body.GetAirRelativeVelocity(com, CenterOfMassVelocity);
        double temperature = System.Math.Max(1.0, body.AtmosphericTemperatureAt(com, altitude));
        var torque = Physics.DistributedAerodynamics.Evaluate(
            Parts.GetAeroElements(), Orientation, Vector3d.Zero, CenterOfMassOffsetLocal, air, density,
            temperature, MaximumDiameter, Parts.AxialDragCoefficient, body.Atmosphere.SoundSpeedConstant,
            body.Atmosphere.GetDynamicViscosityAtTemperature(temperature)).TorqueBody;
        if (includeBodyFlaps && _physicalBodyFlapsActive && Parts.GetBodyFlaps().Count > 0)
            torque += Physics.BodyFlapAerodynamics.Evaluate(Parts.GetBodyFlaps(), BodyFlapDeflectionsRad,
                Orientation, Vector3d.Zero, CenterOfMassOffsetLocal, air, density, temperature,
                body.Atmosphere.SoundSpeedConstant).TorqueBody;
        return torque;
    }

    // ── Body flaps (F2) ───────────────────────────────────────────────────
    private bool _physicalBodyFlapsActive;

    /// <summary>True when the last step flew the physical body flaps.</summary>
    public bool PhysicalBodyFlapsActive => _physicalBodyFlapsActive;
    private double[] _bodyFlapDeflections = [];
    private double[] _bodyFlapTargets = [];
    private double[] _bodyFlapProbe = [];

    /// <summary>
    /// Physical deflection (rad) of each surface in <see cref="PartGraph.GetBodyFlaps"/>; a
    /// surface without state yet reads as its neutral deflection.
    /// </summary>
    public IReadOnlyList<double> BodyFlapDeflectionsRad
    {
        get
        {
            EnsureBodyFlapState();
            return _bodyFlapDeflections;
        }
    }

    /// <summary>
    /// Restores saved flap state: the aggregate channels and, when the save matches this
    /// vessel's surfaces, each physical deflection (clamped to its travel).
    /// </summary>
    internal void RestoreFlapState(FlapActuatorState actuators, IReadOnlyList<double> bodyFlapDeflectionsRad,
        bool physicalBodyFlapsActive = false, bool flightControlInvertsAerodynamics = false)
    {
        FlapActuators = actuators.Clamp();
        _physicalBodyFlapsActive = physicalBodyFlapsActive;
        FlightControlInvertsAerodynamics = flightControlInvertsAerodynamics;
        EnsureBodyFlapState();
        var flaps = Parts.GetBodyFlaps();
        if (bodyFlapDeflectionsRad.Count != flaps.Count) return;
        for (int i = 0; i < flaps.Count; i++)
            _bodyFlapDeflections[i] = System.Math.Clamp(bodyFlapDeflectionsRad[i],
                flaps[i].MinDeflectionRad, flaps[i].MaxDeflectionRad);
    }

    private void EnsureBodyFlapState()
    {
        var flaps = Parts.GetBodyFlaps();
        if (_bodyFlapDeflections.Length == flaps.Count) return;
        _bodyFlapDeflections = new double[flaps.Count];
        _bodyFlapTargets = new double[flaps.Count];
        _bodyFlapProbe = new double[flaps.Count];
        for (int i = 0; i < flaps.Count; i++)
            _bodyFlapDeflections[i] = flaps[i].NeutralDeflectionRad;
    }

    /// <summary>Flaps are locked at neutral while the ship rides a booster (ascent).</summary>
    private bool BodyFlapsLocked()
    {
        foreach (var part in Parts.PartList)
            if (part.Definition.HasVehicleRole("booster")) return true;
        return false;
    }

    /// <summary>
    /// Flight-control allocation for the body flaps: the torque each surface adds per radian
    /// about its neutral deflection (finite difference on the plate model) gives a 3×N
    /// effectiveness matrix; the requested pitch/yaw/roll channels ask for that fraction of
    /// each axis's full authority, minus — when <paramref name="invertStaticMoment"/> — the
    /// predicted static moment of hull and neutral flaps. A least-squares allocation, clipped
    /// to each surface's travel, sets the targets; each servo slews at its own rate.
    /// </summary>
    private void AdvanceBodyFlaps(double dt, CelestialBody? body, Vector3d command, bool invertStaticMoment)
    {
        var flaps = Parts.GetBodyFlaps();
        EnsureBodyFlapState();
        if (flaps.Count == 0) return;
        for (int i = 0; i < flaps.Count; i++)
            _bodyFlapTargets[i] = flaps[i].NeutralDeflectionRad;

        if (body?.Atmosphere != null && !BodyFlapsLocked())
            AllocateBodyFlaps(flaps, body, command, invertStaticMoment);

        for (int i = 0; i < flaps.Count; i++)
        {
            if (flaps[i].Owner.IsBroken) continue;
            double step = flaps[i].RateRadPerS * System.Math.Max(0.0, dt);
            double error = _bodyFlapTargets[i] - _bodyFlapDeflections[i];
            _bodyFlapDeflections[i] = System.Math.Abs(error) <= step
                ? _bodyFlapTargets[i]
                : _bodyFlapDeflections[i] + System.Math.CopySign(step, error);
        }
    }

    private void AllocateBodyFlaps(IReadOnlyList<Physics.BodyFlapGeometry> flaps, CelestialBody body,
        Vector3d command, bool invertStaticMoment)
    {
        var com = CenterOfMass;
        double altitude = body.GetAltitude(com);
        double density = body.AtmosphericDensityAt(com, altitude);
        if (density <= 0.0) return;
        double temperature = System.Math.Max(1.0, body.AtmosphericTemperatureAt(com, altitude));
        var air = body.GetAirRelativeVelocity(com, CenterOfMassVelocity);
        var comLocal = CenterOfMassOffsetLocal;

        for (int i = 0; i < flaps.Count; i++) _bodyFlapProbe[i] = flaps[i].NeutralDeflectionRad;
        Vector3d Torque() => Physics.BodyFlapAerodynamics.Evaluate(flaps, _bodyFlapProbe, Orientation,
            Vector3d.Zero, comLocal, air, density, temperature, body.Atmosphere!.SoundSpeedConstant).TorqueBody;

        const double probe = 1.0 * MathUtils.DEG_TO_RAD;
        Span<Vector3d> effectiveness = flaps.Count <= 16 ? stackalloc Vector3d[flaps.Count] : new Vector3d[flaps.Count];
        Span<double> point = flaps.Count <= 16 ? stackalloc double[flaps.Count] : new double[flaps.Count];
        Span<double> delta = flaps.Count <= 16 ? stackalloc double[flaps.Count] : new double[flaps.Count];
        Span<bool> fixedAtStop = flaps.Count <= 16 ? stackalloc bool[flaps.Count] : new bool[flaps.Count];
        for (int i = 0; i < flaps.Count; i++) point[i] = flaps[i].NeutralDeflectionRad;
        var desired = Vector3d.Zero;
        // Gauss–Newton on the nonlinear plate moments: the first pass linearises about neutral,
        // later passes re-linearise about the clipped targets so the commanded moment is met
        // where the plates actually sit.
        for (int pass = 0; pass < BodyFlapAllocationPasses; pass++)
        {
            for (int i = 0; i < flaps.Count; i++) _bodyFlapProbe[i] = point[i];
            var baseTorque = Torque();
            var authority = Vector3d.Zero;
            double m00 = 0, m01 = 0, m02 = 0, m11 = 0, m12 = 0, m22 = 0;
            for (int i = 0; i < flaps.Count; i++)
            {
                var flap = flaps[i];
                double h = point[i] + probe <= flap.MaxDeflectionRad ? probe : -probe;
                _bodyFlapProbe[i] = point[i] + h;
                var column = flap.Owner.IsBroken ? Vector3d.Zero : (Torque() - baseTorque) / h;
                _bodyFlapProbe[i] = point[i];
                effectiveness[i] = column;
                if (pass == 0)
                {
                    double travel = System.Math.Max(flap.MaxDeflectionRad - flap.NeutralDeflectionRad,
                        flap.NeutralDeflectionRad - flap.MinDeflectionRad);
                    authority += new Vector3d(System.Math.Abs(column.X), System.Math.Abs(column.Y),
                        System.Math.Abs(column.Z)) * travel;
                }
                m00 += column.X * column.X; m01 += column.X * column.Y; m02 += column.X * column.Z;
                m11 += column.Y * column.Y; m12 += column.Y * column.Z; m22 += column.Z * column.Z;
            }
            if (pass == 0)
            {
                // Semantic channels: pitch → body X, yaw → body Z, roll → body Y. A normalised
                // command is a fraction of each axis's full flap authority (the convention every
                // actuator follows); the flap moment supplies it less the hull's static moment.
                desired = new Vector3d(command.X * authority.X, command.Z * authority.Y, command.Y * authority.Z);
                if (invertStaticMoment)
                    desired -= PredictStaticAerodynamicTorqueBody(body, includeBodyFlaps: false);
                else
                    desired += baseTorque;
            }
            double epsilon = 1e-6 * System.Math.Max(1e-12, m00 + m11 + m22);
            // Redistributed pseudo-inverse (Virnig & Bodden): a surface that would pass its
            // stop is fixed there and the remaining moment is re-solved over the free ones.
            for (int i = 0; i < flaps.Count; i++) { fixedAtStop[i] = false; delta[i] = 0.0; }
            for (int iteration = 0; iteration <= flaps.Count; iteration++)
            {
                var residual = desired - baseTorque;
                double f00 = epsilon, f01 = 0, f02 = 0, f11 = epsilon, f12 = 0, f22 = epsilon;
                for (int i = 0; i < flaps.Count; i++)
                {
                    var column = effectiveness[i];
                    if (fixedAtStop[i]) { residual -= column * delta[i]; continue; }
                    f00 += column.X * column.X; f01 += column.X * column.Y; f02 += column.X * column.Z;
                    f11 += column.Y * column.Y; f12 += column.Y * column.Z; f22 += column.Z * column.Z;
                }
                var lambda = Physics.BodyFlapAerodynamics.SolveSymmetric(f00, f01, f02, f11, f12, f22, residual);
                bool saturated = false;
                for (int i = 0; i < flaps.Count; i++)
                {
                    if (fixedAtStop[i]) continue;
                    delta[i] = effectiveness[i].Dot(lambda);
                    double next = point[i] + delta[i];
                    if (next > flaps[i].MaxDeflectionRad || next < flaps[i].MinDeflectionRad)
                    {
                        delta[i] = System.Math.Clamp(next, flaps[i].MinDeflectionRad, flaps[i].MaxDeflectionRad) - point[i];
                        fixedAtStop[i] = true;
                        saturated = true;
                    }
                }
                if (!saturated) break;
            }
            for (int i = 0; i < flaps.Count; i++)
                point[i] = System.Math.Clamp(point[i] + delta[i], flaps[i].MinDeflectionRad, flaps[i].MaxDeflectionRad);
        }
        for (int i = 0; i < flaps.Count; i++) _bodyFlapTargets[i] = point[i];
    }

    /// <summary>
    /// Full-command angular acceleration of the body flaps about body X (pitch), Y (roll) and
    /// Z (yaw) at the current state (rad/s²), from the same neutral-point effectiveness the
    /// allocation sizes its channels with; zero when the flaps are not flying. Lets a controller
    /// schedule its normalised gains, as gimbal controllers do with their authority. Pure: it
    /// reads only the current state, so a restored vessel reproduces it exactly.
    /// </summary>
    public Vector3d ComputeBodyFlapAngularAuthority(CelestialBody body)
    {
        var flaps = Parts.GetBodyFlaps();
        if (flaps.Count == 0 || body.Atmosphere == null || BodyFlapsLocked() || !_physicalBodyFlapsActive)
            return Vector3d.Zero;
        var com = CenterOfMass;
        double altitude = body.GetAltitude(com);
        double density = body.AtmosphericDensityAt(com, altitude);
        if (density <= 0.0) return Vector3d.Zero;
        double temperature = System.Math.Max(1.0, body.AtmosphericTemperatureAt(com, altitude));
        var air = body.GetAirRelativeVelocity(com, CenterOfMassVelocity);
        if (_flapAuthorityProbe.Length != flaps.Count) _flapAuthorityProbe = new double[flaps.Count];
        var probe = _flapAuthorityProbe;
        for (int i = 0; i < flaps.Count; i++) probe[i] = flaps[i].NeutralDeflectionRad;
        Vector3d Torque() => Physics.BodyFlapAerodynamics.Evaluate(flaps, probe, Orientation, Vector3d.Zero,
            CenterOfMassOffsetLocal, air, density, temperature, body.Atmosphere.SoundSpeedConstant).TorqueBody;
        var neutral = Torque();
        var authority = Vector3d.Zero;
        const double step = 1.0 * MathUtils.DEG_TO_RAD;
        for (int i = 0; i < flaps.Count; i++)
        {
            var flap = flaps[i];
            if (flap.Owner.IsBroken) continue;
            double h = flap.NeutralDeflectionRad + step <= flap.MaxDeflectionRad ? step : -step;
            probe[i] = flap.NeutralDeflectionRad + h;
            var column = (Torque() - neutral) / h;
            probe[i] = flap.NeutralDeflectionRad;
            double travel = System.Math.Max(flap.MaxDeflectionRad - flap.NeutralDeflectionRad,
                flap.NeutralDeflectionRad - flap.MinDeflectionRad);
            authority += new Vector3d(System.Math.Abs(column.X), System.Math.Abs(column.Y), System.Math.Abs(column.Z)) * travel;
        }
        var inertia = GetMassProperties().InertiaBody;
        return new Vector3d(
            inertia.M11 > 0.0 ? authority.X / inertia.M11 : 0.0,
            inertia.M22 > 0.0 ? authority.Y / inertia.M22 : 0.0,
            inertia.M33 > 0.0 ? authority.Z / inertia.M33 : 0.0);
    }

    private double[] _flapAuthorityProbe = [];

    /// <summary>Gauss–Newton passes of the body-flap allocation (one is the neutral linearisation).</summary>
    private const int BodyFlapAllocationPasses = 3;

    /// <summary>Drag of deployed parachutes only (N, world), for the distributed aero path.</summary>
    public Vector3d ComputeParachuteDragAt(Vector3d pos, Vector3d vel, CelestialBody body)
    {
        if (body.Atmosphere == null) return Vector3d.Zero;
        double density = body.AtmosphericDensityAt(pos, body.GetAltitude(pos));
        var surfVel = body.GetAirRelativeVelocity(pos, vel);
        double speed = surfVel.Magnitude;
        double area = GetDeployedParachuteDragArea(body, pos);
        return density <= 0.0 || speed < 0.001 || area <= 0.0
            ? Vector3d.Zero
            : -surfVel.Normalized * (0.5 * density * speed * speed * area);
    }

    public Vector3d ComputeDragAt(Vector3d pos, Vector3d vel, CelestialBody body) =>
        ComputeDragAt(pos, vel, Orientation, body);

    /// <summary>
    /// Evaluates aerodynamic force at an arbitrary translational and attitude state without
    /// mutating the vessel. This is the force-side contract used by the coupled RK4 stages.
    /// </summary>
    public Vector3d ComputeDragAt(
        Vector3d pos,
        Vector3d vel,
        Quaterniond orientation,
        CelestialBody body)
    {
        if (body.Atmosphere == null) return Vector3d.Zero;
        double alt     = body.GetAltitude(pos);
        double density = body.AtmosphericDensityAt(pos, alt);
        if (density <= 0.0) return Vector3d.Zero;

        // Velocidad relativa a la atmósfera en rotación (resta la velocidad de la
        // superficie del cuerpo y su traslación), no inercial.
        var    surfVel = body.GetAirRelativeVelocity(pos, vel);
        double speed   = surfVel.Magnitude;
        if (speed < 0.001 || double.IsNaN(speed)) return Vector3d.Zero;

        Vector3d axis = orientation.Rotate(Vector3d.Up);   // eje longitudinal en mundo
        double   temp = System.Math.Max(1.0, body.AtmosphericTemperatureAt(pos, alt));
        double parachuteArea = GetDeployedParachuteDragArea(body, pos);
        var parachuteDrag = parachuteArea > 0.0
            ? -surfVel.Normalized
                * (0.5 * density * speed * speed * parachuteArea)
            : Vector3d.Zero;
        if (Parts.CapsuleAerodynamics is { } capsule)
            return AerodynamicsModel.ComputeCapsuleForce(density, surfVel, axis, MaximumDiameter,
                capsule.AxialForceCoefficient) + parachuteDrag;

        var drag = AerodynamicsModel.ComputeReentryDrag(
            density,
            surfVel,
            axis,
            VehicleLength,
            MaximumDiameter,
            temp,
            Parts.AxialDragCoefficient,
            body.Atmosphere.SoundSpeedConstant);
        var lift = AerodynamicsModel.ComputeLift(
            density, surfVel, axis, VehicleLength, MaximumDiameter);
        return drag + lift + parachuteDrag;
    }

    /// <summary>
    /// Convective stagnation-point heat flux (W/m²) for this vessel at the given local
    /// atmospheric density and surface-relative velocity, using the Sutton-Graves
    /// correlation with an attitude-dependent effective nose radius: sharp (this vessel's
    /// declared <see cref="NoseRadius"/>) nose/tail-on, blunt (the hull radius) broadside.
    /// Every existing call site computed the broadside-only version of this by hand
    /// (<c>ComputeHeatFlux(density, speed, MaximumDiameter * 0.5)</c>); this centralises the
    /// attitude blend so callers do not each duplicate the cosAlpha computation.
    /// </summary>
    public double ComputeStagnationHeatFlux(double density, Vector3d surfaceVelocity)
    {
        double speed = surfaceVelocity.Magnitude;
        double hullRadius = System.Math.Max(0.1, MaximumDiameter * 0.5);
        double cosAlpha = speed > 1e-6
            ? System.Math.Abs(Orientation.Rotate(Vector3d.Up).Normalized.Dot(surfaceVelocity.Normalized))
            : 0.0;
        double effectiveNoseRadius = ThermalModel.EffectiveNoseRadius(hullRadius, NoseRadius, cosAlpha);
        return ThermalModel.ComputeHeatFlux(density, speed, effectiveNoseRadius);
    }

    /// <summary>
    /// Arms every installed parachute whose declared atmospheric and altitude envelope is
    /// currently satisfied. Deployment state is persisted on the part instance.
    /// </summary>
    public int DeployParachutes(CelestialBody body)
    {
        if (body.Atmosphere == null) return 0;
        double altitude = GetAltitude(body);
        double pressureFraction = body.AtmosphericPressureAt(Position, altitude) / 101_325.0;
        int deployed = 0;
        foreach (var part in Parts.Parts.Where(p =>
                     p.Definition.DragChute > 0.0 && !p.IsDeployed))
        {
            var definition = part.Definition;
            double armAltitude = definition.SemiDeployAltitude > 0.0
                ? definition.SemiDeployAltitude
                : definition.DeployAltitude;
            if (armAltitude > 0.0 && altitude > armAltitude) continue;
            if (pressureFraction + 1e-12 < definition.MinPressureDeploy) continue;
            part.IsDeployed = true;
            deployed++;
        }
        return deployed;
    }

    /// <summary>Combined effective CdA (m²) of deployed reefed/full parachutes.</summary>
    public double GetDeployedParachuteDragArea(
        CelestialBody body,
        Vector3d? position = null)
    {
        double altitude = body.GetAltitude(position ?? Position);
        double totalArea = 0.0;
        foreach (var part in Parts.PartList)
        {
            if (!part.IsDeployed) continue;
            var definition = part.Definition;
            if (definition.DragChute <= 0.0) continue;
            if (definition.DeployAltitude > 0.0
                && definition.SemiDeployDrag > 0.0)
            {
                if (altitude > definition.DeployAltitude)
                {
                    totalArea += definition.SemiDeployDrag;
                    continue;
                }
                // Reefing lines do not release a full canopy instantaneously. Use a
                // one-kilometre inflation corridor below the declared main-deploy event;
                // this avoids a non-physical impulse while preserving the published event.
                double inflation = System.Math.Clamp(
                    (definition.DeployAltitude - altitude) / 1_000.0,
                    0.0,
                    1.0);
                double smooth = inflation * inflation * (3.0 - 2.0 * inflation);
                totalArea += definition.SemiDeployDrag
                    + (definition.DragChute - definition.SemiDeployDrag) * smooth;
                continue;
            }
            totalArea += definition.DragChute;
        }
        return totalArea;
    }

    // Aceleración gravitacional total de todos los cuerpos (m/s²)
    public Vector3d ComputeGravity(IEnumerable<CelestialBody> bodies) =>
        ComputeGravityAt(Position, bodies);

    // Suma N-cuerpos evaluada en una posición arbitraria (para subpasos RK4).
    public Vector3d ComputeGravityAt(Vector3d pos, IEnumerable<CelestialBody> bodies)
    {
        var accel = Vector3d.Zero;
        foreach (var body in bodies)
            accel = accel + body.GetGravityAt(pos);
        return accel;
    }

    /// <summary>
    /// Allocation-free list overload used by <see cref="Universe"/> during RK4. The
    /// enumerable overload remains for callers that provide a general sequence.
    /// </summary>
    public Vector3d ComputeGravityAt(Vector3d pos, IReadOnlyList<CelestialBody> bodies)
    {
        var accel = Vector3d.Zero;
        for (int i = 0; i < bodies.Count; i++)
            accel = accel + bodies[i].GetGravityAt(pos);
        return accel;
    }

    // Aceleración neta para el integrador RK4 (m/s²) — estado actual.
    public Vector3d ComputeNetAcceleration(IEnumerable<CelestialBody> bodies, CelestialBody? refBody) =>
        ComputeNetAccelerationAt(Position, Velocity, bodies, refBody);

    /// <summary>
    /// Aceleración neta (gravedad N-cuerpos + empuje + arrastre) evaluada en un
    /// estado (pos, vel) arbitrario. Esencial para que RK4 muestree las fuerzas en
    /// los estados intermedios k₂…k₄ en lugar de reutilizar el estado actual del vessel.
    /// </summary>
    public Vector3d ComputeNetAccelerationAt(
        Vector3d pos, Vector3d vel, IEnumerable<CelestialBody> bodies, CelestialBody? refBody)
    {
        var gravity = ComputeGravityAt(pos, bodies);
        if (TotalMass <= 0.0) return gravity;

        // Empuje: dirección fija por la orientación durante el subpaso; magnitud
        // corregida por la presión a la altitud del estado evaluado.
        // ThrottleLevel ya fue avanzado por ApplyThrottle(dt) en Tick() antes de este subpaso,
        // así que aquí solo leemos el valor spooled sin modificarlo.
        double pressure = refBody?.GetAtmosphericPressure(pos) ?? 0.0;
        var thrust = Orientation.Rotate(Parts.GetTotalThrust(pressure) + RcsThrusterForceBody)
            / TranslationalMassKg;

        var drag = refBody != null
            ? ComputeDragAt(pos, vel, refBody) / TranslationalMassKg
            : Vector3d.Zero;

        return gravity + thrust + drag;
    }

    /// <summary>List-specialized RK4 force evaluation without interface enumeration.</summary>
    public Vector3d ComputeNetAccelerationAt(
        Vector3d pos,
        Vector3d vel,
        IReadOnlyList<CelestialBody> bodies,
        CelestialBody? refBody)
    {
        var gravity = ComputeGravityAt(pos, bodies);
        if (TotalMass <= 0.0) return gravity;

        double pressure = refBody?.GetAtmosphericPressure(pos) ?? 0.0;
        var thrust = Orientation.Rotate(Parts.GetTotalThrust(pressure) + RcsThrusterForceBody)
            / TranslationalMassKg;
        var drag = refBody != null
            ? ComputeDragAt(pos, vel, refBody) / TranslationalMassKg
            : Vector3d.Zero;
        return gravity + thrust + drag;
    }

    // Overload accepting IReadOnlyList for compatibility with Universe.cs
    public Vector3d ComputeNetAcceleration(IReadOnlyList<CelestialBody> bodies, CelestialBody refBody) =>
        ComputeNetAccelerationAt(Position, Velocity, bodies, refBody);

    public Vector3d ComputeGravity(IReadOnlyList<CelestialBody> bodies) =>
        ComputeGravityAt(Position, bodies);

    // ── Control de actitud (F3): todo momento sale de un actuador ─────────
    // Flight-control laws (pilot/guidance command, SAS rate damping, the pilot rate envelope)
    // only decide a desired body angular acceleration. The gimbals, the declared RCS
    // thrusters and the body flaps deliver whatever part of it they physically can; nothing
    // writes the angular velocity directly. A vehicle without thrusters has no RCS authority.

    /// <summary>Rate the pilot envelope holds a commanded rotation to (20°/s).</summary>
    private const double PilotRateEnvelopeRadPerS = 0.35;

    /// <summary>
    /// The 20°/s pilot-authority envelope on held commands (a handling choice for human
    /// pilots, not a physical limit), applied to the command: the control law never asks the
    /// actuators to turn faster. Autonomous mission controllers that reconstruct real flights
    /// clear it each control step so the airframe turns at its actuator-limited rate.
    /// </summary>
    public bool PilotRateEnvelopeEnabled { get; set; } = true;

    /// <summary>Fraction of the body rate SAS asks to keep per second (rate-damping law).</summary>
    private const double SasRateRetentionPerSecond = 0.005;

    /// <summary>Pitch/yaw angular acceleration (rad/s²) of the declared, enabled RCS thrusters.</summary>
    private double PitchYawRcsAuthority
    {
        get
        {
            var thrusters = RcsThrusterAuthorityTorqueBody;
            return System.Math.Min(thrusters.X, thrusters.Z) / System.Math.Max(1.0, Parts.TransverseMomentOfInertia);
        }
    }

    private double RollRcsAuthority =>
        RcsThrusterAuthorityTorqueBody.Y / System.Math.Max(1.0, Parts.AxialMomentOfInertia);

    /// <summary>
    /// Pure moment the declared, enabled RCS thrusters deliver about each body axis (N·m).
    /// A pure function of the thruster set, the centre of mass on a 25 cm grid and the
    /// roll/pitch inertia ratio to three significant figures, cached on those keys so a
    /// resumed run computes exactly the values a continuous one does.
    /// </summary>
    private Vector3d RcsThrusterAuthorityTorqueBody
    {
        get
        {
            // A separation since the last step may have armed a standby or staged system.
            Parts.ArmStagedReactionControl();
            var thrusters = Parts.GetRcsThrusters();
            if (thrusters.Count == 0) return Vector3d.Zero;
            const double grid = 0.25;
            var com = RcsTorqueReferenceLocal;
            com = new Vector3d(System.Math.Round(com.X / grid) * grid, System.Math.Round(com.Y / grid) * grid,
                System.Math.Round(com.Z / grid) * grid);
            var (rcsTransverse, rcsAxial) = RcsInertia;
            double ratio = System.Math.Max(1.0, rcsTransverse) / System.Math.Max(1.0, rcsAxial);
            double magnitude = System.Math.Pow(10.0, System.Math.Floor(System.Math.Log10(ratio)) - 2.0);
            ratio = System.Math.Round(ratio / magnitude) * magnitude;
            ulong mask = 0;
            for (int i = 0; i < thrusters.Count; i++)
                if (thrusters[i].Available) mask ^= 1UL << (i % 64) ^ (ulong)(i / 64) * 0x9E3779B97F4A7C15UL;
            if (_rcsAuthorityValid && _rcsAuthorityVersion == Parts.TopologyVersion && _rcsAuthorityMask == mask
                && _rcsAuthorityCom == com && _rcsAuthorityRatio == ratio)
                return _rcsAuthority;
            _rcsAuthority = Physics.RcsAllocation.Authority(thrusters, com, new Vector3d(1.0, ratio, 1.0));
            _rcsAuthorityValid = true;
            _rcsAuthorityVersion = Parts.TopologyVersion;
            _rcsAuthorityMask = mask;
            _rcsAuthorityCom = com;
            _rcsAuthorityRatio = ratio;
            return _rcsAuthority;
        }
    }

    private bool _rcsAuthorityValid;
    private int _rcsAuthorityVersion;
    private ulong _rcsAuthorityMask;
    private Vector3d _rcsAuthorityCom;
    private double _rcsAuthorityRatio;
    private Vector3d _rcsAuthority;

    private double[] _rcsDuty = [];

    /// <summary>Force the RCS thrusters fired this step (body frame, N), held across RK4 stages.</summary>
    public Vector3d RcsThrusterForceBody { get; private set; }

    /// <summary>Moment the RCS thrusters fired this step (body frame, N·m), held across RK4 stages.</summary>
    public Vector3d RcsThrusterTorqueBody { get; private set; }

    // Allocation precedes propellant consumption; retain its exact moment reference.
    internal Vector3d RcsTorqueReferenceBody { get; private set; }

    /// <summary>Last step's propellant flow of the RCS thrusters (kg/s).</summary>
    public double RcsThrusterMassFlowKgPerS { get; private set; }

    /// <summary>Opt-in immutable funding evidence for the next variable-mass adapter.</summary>
    public bool CaptureRcsFunding { get; set; }
    public Physics.RcsPropellantFundingSnapshot? LastRcsFunding { get; private set; }

    /// <summary>Opt-in gross liquid-engine and main-vent funding for each physical preparation.</summary>
    public bool CapturePropellantFunding
    {
        get => Parts.CapturePropellantFunding;
        set => Parts.CapturePropellantFunding = value;
    }
    public Physics.LiquidEngineFundingSnapshot? LastLiquidEngineFunding => Parts.LastLiquidEngineFunding;
    public Physics.MainPropellantVentSnapshot? LastMainVentFunding => Parts.LastMainVentFunding;

    /// <summary>
    /// Fires the declared RCS thrusters toward the requested moment and force for one control
    /// step and draws their propellant from the system's store. Without
    /// propellant for the whole step they do not fire.
    /// </summary>
    private void FireRcsThrusters(Vector3d desiredTorqueBody, Vector3d desiredForceBody, double dt)
    {
        RcsThrusterForceBody = Vector3d.Zero;
        RcsThrusterTorqueBody = Vector3d.Zero;
        RcsThrusterMassFlowKgPerS = 0.0;
        LastRcsFunding = null;
        var thrusters = Parts.GetRcsThrusters();
        if (thrusters.Count == 0 || !(dt > 0.0)) return;
        // Sized on the first step after a topology change, not on the first firing.
        if (_rcsDuty.Length != thrusters.Count) _rcsDuty = new double[thrusters.Count];
        if (desiredTorqueBody.MagnitudeSquared < 1e-18 && desiredForceBody.MagnitudeSquared < 1e-18)
            return;
        // Weight each axis by its inverse inertia: the allocation minimises the error in
        // angular acceleration, not in N·m.
        var (rcsTransverse, rcsAxial) = RcsInertia;
        double transverse = System.Math.Max(1.0, rcsTransverse);
        double axial = System.Math.Max(1.0, rcsAxial);
        var weights = new Vector3d(1.0, transverse / axial, 1.0);
        double forceWeight = desiredForceBody.MagnitudeSquared > 0.0 ? 1.0 : Physics.RcsAllocation.AttitudeOnlyForceWeight;
        var torqueReference = RcsTorqueReferenceLocal;
        var result = Physics.RcsAllocation.Allocate(thrusters, torqueReference,
            desiredTorqueBody, desiredForceBody, _rcsDuty, weights, forceWeight);
        if (!DrawRcsPropellant(thrusters, result, dt))
        {
            RcsPropellantDepleted = true;
            return;
        }
        RcsTorqueReferenceBody = torqueReference;
        RcsThrusterForceBody = result.ForceBody;
        RcsThrusterTorqueBody = result.TorqueBody;
        RcsThrusterMassFlowKgPerS = result.MassFlowKgPerS;
    }

    private bool DrawRcsPropellant(IReadOnlyList<Physics.RcsThrusterGeometry> thrusters,
        Physics.RcsAllocationResult result, double dt)
    {
        // Each RCS system burns its own store: a part that carries one feeds its own thrusters,
        // and thrusters on a part without a store draw on the vessel's shared feed.
        if (result.MassFlowKgPerS <= 0.0) return true;
        _rcsDemand.Clear();
        double shared = 0.0;
        for (int i = 0; i < thrusters.Count; i++)
        {
            if (_rcsDuty[i] <= 0.0) continue;
            var thruster = thrusters[i];
            double kg = thruster.MassFlowKgPerS * _rcsDuty[i] * dt;
            if (thruster.Owner.Definition.FuelCapacityMono > 0.0)
                _rcsDemand[thruster.Owner] = _rcsDemand.GetValueOrDefault(thruster.Owner) + kg;
            else
                shared += kg;
        }
        foreach (var (owner, kg) in _rcsDemand)
            if (owner.Monopropellant < kg) return false;
        double[]? before = CaptureRcsFunding || Parts.ResourceFundingCapture != null
            ? Parts.PartList.Select(part => part.Monopropellant).ToArray() : null;
        if (shared > 0.0 && !Parts.DrawMonopropellant(shared, primaryStoresOnly: false, _rcsDemand)) return false;
        foreach (var (owner, kg) in _rcsDemand)
            owner.Monopropellant -= kg;
        if (before != null)
        {
            var receipt = Physics.RcsPropellantFundingSnapshot.Capture(
                Parts.PartList, before, thrusters, _rcsDuty, dt, InfinitePropellant);
            if (CaptureRcsFunding) LastRcsFunding = receipt;
            if (Parts.ResourceFundingCapture is { } capture)
            {
                capture.ReactionControl = receipt;
                capture.RecordCurrentStores(Parts.PartList, Physics.ResourceWithdrawalSource.ReactionControl,
                    Physics.PropellantResourceKind.Monopropellant);
            }
        }
        return true;
    }

    private readonly Dictionary<Part, double> _rcsDemand = new(16);

    /// <summary>
    /// Pitch/yaw angular acceleration (rad/s²) a full attitude command produces at the
    /// current state: the largest of the live TVC envelope, the RCS thrusters and the flaps.
    /// </summary>
    public double GetPitchYawAngularAuthority(CelestialBody body) => GetAttitudeAuthorityBody(body).X;

    /// <summary>
    /// Angular acceleration (rad/s²) a full attitude command produces about body X (pitch),
    /// Y (roll) and Z (yaw): per axis the largest of the live TVC envelope, the declared RCS
    /// thrusters and the body flaps. A command is a fraction of this; controllers normalise
    /// their angular-acceleration demand by it.
    /// </summary>
    public Vector3d GetAttitudeAuthorityBody(CelestialBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        double pressure = body.GetAtmosphericPressure(Position);
        return AttitudeAuthorityLocal(pressure, FlapAngularAuthorityLocal(body));
    }

    private Vector3d AttitudeAuthorityLocal(double pressure, Vector3d flapAuthority)
    {
        var envelope = Parts.GetDifferentialTVCAngularAccelerationEnvelope(pressure);
        double pitchYaw = PitchYawRcsAuthority;
        double roll = RollRcsAuthority;
        return new Vector3d(
            System.Math.Max(System.Math.Max(pitchYaw, envelope.X), flapAuthority.X),
            System.Math.Max(System.Math.Max(roll, envelope.Y), flapAuthority.Y),
            System.Math.Max(System.Math.Max(pitchYaw, envelope.Z), flapAuthority.Z));
    }

    /// <summary>
    /// Desired body angular acceleration (rad/s², local X pitch, Y roll, Z yaw) of the flight
    /// control law. A held command asks for its fraction of the actuators' authority, limited
    /// by the pilot rate envelope when that is on; with no command SAS asks to remove the body
    /// rate at its damping law. The actuators then deliver only what they physically can.
    /// </summary>
    private Vector3d DesiredControlAngularAcceleration(Vector3d command, bool hasInput, double auth,
        double pressure, double dt)
    {
        if (!(dt > 0.0)) return Vector3d.Zero;
        double inertiaScale = DockedInertiaScale();
        var rate = Orientation.Inverse().Rotate(AngularVelocity);
        // Gimbals and jets are asked for the command's fraction of their own authority; the
        // flaps add theirs and the jets only fill above it, so the whole vehicle delivers the
        // command's fraction of the largest of the three (GetAttitudeAuthorityBody).
        if (hasInput)
        {
            var authority = AttitudeAuthorityLocal(pressure, Vector3d.Zero) * auth;
            var alpha = new Vector3d(command.X * authority.X, command.Z * authority.Y, command.Y * authority.Z);
            if (!PilotRateEnvelopeEnabled) return alpha;
            // Command-level envelope: the command never asks for a rate above 20°/s. Only the
            // part of it that would spin the body faster along its current rate is removed; it
            // is never turned into braking, and actuator lag may carry the rate a little past.
            double rateMagnitude = rate.Magnitude;
            if (rateMagnitude < 1e-12) return alpha;
            var rateAxis = rate / rateMagnitude;
            double along = alpha.Dot(rateAxis);
            if (along <= 0.0) return alpha;
            double allowed = System.Math.Max(0.0, (PilotRateEnvelopeRadPerS - rateMagnitude) / (dt * inertiaScale));
            return along <= allowed ? alpha : alpha - rateAxis * (along - allowed);
        }
        if (SASEnabled && auth > 1e-6)
        {
            // Rate damping asks for no more than the actuators can deliver on each axis.
            var authority = AttitudeAuthorityLocal(pressure, Vector3d.Zero) * auth;
            var damping = -rate * ((1.0 - System.Math.Pow(SasRateRetentionPerSecond, dt)) / (dt * inertiaScale));
            return new Vector3d(System.Math.Clamp(damping.X, -authority.X, authority.X),
                System.Math.Clamp(damping.Y, -authority.Y, authority.Y),
                System.Math.Clamp(damping.Z, -authority.Z, authority.Z));
        }
        return Vector3d.Zero;
    }

    /// <summary>Own-body moment (N·m) for a desired angular acceleration (principal axes).</summary>
    private Vector3d ControlTorqueFor(Vector3d angularAcceleration) => new(
        angularAcceleration.X * Parts.TransverseMomentOfInertia,
        angularAcceleration.Y * Parts.AxialMomentOfInertia,
        angularAcceleration.Z * Parts.TransverseMomentOfInertia);

    /// <summary>
    /// Full-deflection angular acceleration of the body flaps about body X (pitch), Y (roll)
    /// and Z (yaw), rad/s²: the physical plates when they fly, else the aggregate flap model
    /// of the split tick; zero without functional flaps or air.
    /// </summary>
    private Vector3d FlapAngularAuthorityLocal(CelestialBody body)
    {
        if (_physicalBodyFlapsActive) return ComputeBodyFlapAngularAuthority(body);
        if (body.Atmosphere == null || !HasFunctionalBodyFlaps()) return Vector3d.Zero;
        double density = body.AtmosphericDensityAt(Position, GetAltitude(body));
        var air = GetAirRelativeVelocity(body);
        var full = AerodynamicsModel.ComputeFlapControlAngularAcceleration(density, air, Quaterniond.Identity,
            new FlapActuatorState(1.0, 1.0, 1.0), VehicleLength, MaximumDiameter, Parts.TransverseMomentOfInertia);
        return new Vector3d(System.Math.Abs(full.X), System.Math.Abs(full.Y), System.Math.Abs(full.Z));
    }

    /// <summary>
    /// Advances stateful propulsion/control preparation exactly once while leaving attitude
    /// integration to the coupled 6-DoF path. The PartGraph physics cache remains open until
    /// <see cref="EndCoupledPhysicsStep"/> so every RK4 stage reads the same prepared engine
    /// state and mass snapshot.
    /// </summary>
    internal void BeginCoupledPhysicsStep(double dt, CelestialBody refBody, bool distributedAerodynamics = false,
        bool physicalBodyFlaps = false)
    {
        Parts.BeginPhysicsTick();
        BeginResourceFunding(dt);
        try
        {
            RcsThrusterForceBody = Vector3d.Zero;
            RcsThrusterTorqueBody = Vector3d.Zero;
            RcsThrusterMassFlowKgPerS = 0.0;
            LastRcsFunding = null;
            Parts.ArmStagedReactionControl();

            ApplyThrottle(dt);
            Parts.InvalidateTickActiveEngineCache();

            double pressure = refBody?.GetAtmosphericPressure(Position) ?? 0.0;
            ConsumePropellants(dt, pressure);
            AdvanceHotStageOverlap(dt);

            foreach (var crew in Crew)
                crew.TickEVA(dt);

            double auth = Flight.ControlAuthority.Evaluate(this);
            if (Flight.ControlAuthority.IsLost(auth))
            {
                PitchYawRoll = Vector3d.Zero;
                SASEnabled = false;
            }

            var command = PitchYawRoll * auth;
            var flapCommand = (FlapCommandOverride ?? PitchYawRoll) * auth;
            AdvanceFlapActuators(dt, flapCommand);
            _physicalBodyFlapsActive = distributedAerodynamics && physicalBodyFlaps;
            if (_physicalBodyFlapsActive && auth > 1e-6)
                AdvanceBodyFlaps(dt, refBody, flapCommand,
                    invertStaticMoment: flapCommand.Magnitude > 1e-6 || SASEnabled);
            else
                AdvanceBodyFlaps(dt, null, Vector3d.Zero, invertStaticMoment: false);
            bool hasInput = command.Magnitude > 1e-6;
            FlightControlInvertsAerodynamics = _physicalBodyFlapsActive && Parts.UsesDistributedAerodynamics;
            foreach (var engine in Parts.ActiveEngineList)
            {
                engine.GimbalOffset = hasInput
                    ? new Vector3d(command.Y, 0.0, -command.X)
                    : Vector3d.Zero;
                engine.ClearGimbalCommandOverrides();
            }

            // Flight control inverts the predicted static aerodynamic moment, the same way it
            // already inverts the engines' zero-gimbal moment: a statically unstable airframe
            // (every finless launcher) is held by its gimbals, within their real range, instead
            // of by guidance gains tuned against a stable aggregate model.
            var aeroTorque = distributedAerodynamics && refBody != null && auth > 1e-6
                ? PredictStaticAerodynamicTorqueBody(refBody)
                : Vector3d.Zero;

            var flapAuthority = refBody != null ? FlapAngularAuthorityLocal(refBody) : Vector3d.Zero;
            var alpha = DesiredControlAngularAcceleration(command, hasInput, auth, pressure, dt);
            ApplyAttitudeActuators(alpha, hasInput || (SASEnabled && auth > 1e-6), aeroTorque, pressure, dt,
                flapCommand, flapAuthority);
            CompleteResourceFunding();
            RestoreInfinitePropellant();
        }
        catch
        {
            Parts.ResourceFundingCapture = null;
            Parts.EndPhysicsTick();
            throw;
        }
    }

    /// <summary>
    /// Sends the desired angular acceleration to the actuators: the gimbals are allocated the
    /// total moment (inverting the engines' zero-gimbal moment and, when given, the predicted
    /// static aerodynamic moment), and the RCS thrusters are asked for what the engines leave,
    /// together with the translation command.
    /// </summary>
    private void ApplyAttitudeActuators(Vector3d alpha, bool controlling, Vector3d aeroTorque,
        double pressure, double dt, Vector3d flapCommand, Vector3d flapAuthority)
    {
        if (Parts.TotalMass <= 0.0) return;
        var desiredTorque = ControlTorqueFor(alpha);
        // Staged engines remain in ActiveEngineList while shut down. Only delivered
        // thrust lets TVC own the aerodynamic trim; otherwise the flaps already own it.
        bool enginesLit = Parts.GetCurrentThrust(pressure) > 0.0;
        if (controlling && enginesLit)
        {
            // The engines turn the docked stack about its centre, as the jets do.
            var reference = DockedStackCenterOfMassLocal;
            Parts.SolveDifferentialGimbal(
                desiredTorque - Parts.GetZeroGimbalEngineTorque(pressure, reference) - aeroTorque,
                pressure, reference);
        }

        if (Parts.GetRcsThrusters().Count == 0) return;
        var desiredForce = DesiredRcsTranslationForceBody();
        if (!controlling && desiredForce.MagnitudeSquared < 1e-18)
        {
            FireRcsThrusters(Vector3d.Zero, Vector3d.Zero, dt);
            return;
        }
        var rcsTorque = Vector3d.Zero;
        if (controlling)
        {
            // The engines' moment after allocation (and the aerodynamic moment the gimbals
            // were asked to cancel) is already being delivered; the thrusters fill the rest.
            var engineTorque = ControlTorqueFor(Parts.GetPitchYawRollAngularAcceleration(pressure));
            var remainder = desiredTorque - engineTorque - (enginesLit ? aeroTorque : Vector3d.Zero);
            // Blended control: the body flaps are credited with what they were commanded (the
            // command's fraction of their present authority), never more than the request, and
            // the jets only fill what is left, never against it.
            double transverse = System.Math.Max(1.0, Parts.TransverseMomentOfInertia);
            double axial = System.Math.Max(1.0, Parts.AxialMomentOfInertia);
            var flapShare = ControlTorqueFor(new Vector3d(
                FlapCredit(remainder.X / transverse, flapCommand.X * flapAuthority.X),
                FlapCredit(remainder.Y / axial, flapCommand.Z * flapAuthority.Y),
                FlapCredit(remainder.Z / transverse, flapCommand.Y * flapAuthority.Z)));
            rcsTorque = remainder - flapShare;
            if (enginesLit && Parts.GimbalsSlewForAttitudeControl())
            {
                // Under thrust a slewing gimbal cluster owns every axis it can cover: the jets
                // are not used to chase servo lag, only where the cluster lacks authority (a
                // single engine's roll, as the S-IVB APS flies it during the J-2 burns). A trim
                // gimbal (the LM DPS at 0.2°/s) only trims, and the jets fly the attitude.
                var envelope = Parts.GetDifferentialTVCAngularAccelerationEnvelope(pressure);
                rcsTorque = new Vector3d(
                    System.Math.Abs(alpha.X) <= envelope.X ? 0.0 : rcsTorque.X,
                    System.Math.Abs(alpha.Y) <= envelope.Y ? 0.0 : rcsTorque.Y,
                    System.Math.Abs(alpha.Z) <= envelope.Z ? 0.0 : rcsTorque.Z);
            }
            // The thrusters are asked for no more than their pure moment on each axis.
            var reach = RcsThrusterAuthorityTorqueBody;
            rcsTorque = new Vector3d(System.Math.Clamp(rcsTorque.X, -reach.X, reach.X),
                System.Math.Clamp(rcsTorque.Y, -reach.Y, reach.Y), System.Math.Clamp(rcsTorque.Z, -reach.Z, reach.Z));
        }
        FireRcsThrusters(rcsTorque, desiredForce, dt);
    }

    /// <summary>The flaps' share of a request: what they were commanded, capped at the request.</summary>
    private static double FlapCredit(double request, double commanded) =>
        request * commanded <= 0.0 ? 0.0
            : System.Math.CopySign(System.Math.Min(System.Math.Abs(request), System.Math.Abs(commanded)), request);

    internal void EndCoupledPhysicsStep() => Parts.EndPhysicsTick();

    internal bool HasFunctionalBodyFlaps()
    {
        foreach (var part in Parts.PartList)
        {
            if (part.Definition.IsStarshipFamily
                && part.Definition.HasVehicleRole("command")
                && !part.IsBroken)
                return true;
        }

        return false;
    }

    public void Tick(double dt, CelestialBody refBody, Vector3d externalContactTorqueWorld = default)
    {
        BeginLegacyPhysicsStep(dt, refBody, externalContactTorqueWorld);
        Parts.EndPhysicsTick();
    }

    internal void BeginLegacyPhysicsStep(double dt, CelestialBody refBody, Vector3d externalContactTorqueWorld)
    {
        Parts.BeginPhysicsTick();
        BeginResourceFunding(dt);
        try { TickPhysics(dt, refBody, externalContactTorqueWorld); }
        catch
        {
            Parts.ResourceFundingCapture = null;
            Parts.EndPhysicsTick();
            throw;
        }
    }

    /// <summary>
    /// Advances the vessel attitude during an analytic translational coast. This uses the
    /// existing world-space constant-angular-velocity approximation; it does not integrate
    /// gravity-gradient torque or full torque-free Euler motion.
    /// </summary>
    internal void AdvanceCoastingAttitude(double dt)
    {
        if (!double.IsFinite(dt) || dt <= 0.0)
            return;

        double angularRate = AngularVelocity.Magnitude;
        if (!double.IsFinite(angularRate) || angularRate <= 1e-12)
            return;

        var deltaRotation = Quaterniond.FromAxisAngle(
            AngularVelocity / angularRate, angularRate * dt);
        Orientation = (deltaRotation * Orientation).Normalize();
    }

    /// <summary>
    /// Legacy split tick (comparison baseline): the same control law and actuators as the
    /// coupled path, integrated with explicit Euler on the angular rate.
    /// </summary>
    private void TickPhysics(
        double dt,
        CelestialBody refBody,
        Vector3d externalContactTorqueWorld)
    {
        double pressure = refBody?.GetAtmosphericPressure(Position) ?? 0.0;
        Parts.ArmStagedReactionControl();

        // Avanzar el spool de motores UNA VEZ por tick (antes del consumo de propelante
        // y antes de que RK4 muestree las fuerzas), para que los subpasos k₁…k₄ usen
        // el mismo ThrottleLevel spooled dentro de un mismo paso de física.
        ApplyThrottle(dt);
        // AdvanceEngineRuntime can fail an engine on an over-temperature transition. Do not
        // let the active-engine snapshot taken by ApplyThrottle survive that state change.
        Parts.InvalidateTickActiveEngineCache();

        ConsumePropellants(dt, pressure);
        AdvanceHotStageOverlap(dt);

        foreach (var crew in Crew)
            crew.TickEVA(dt);

        // Structural control authority: scale commanded rates after breakup / lost command.
        double auth = Flight.ControlAuthority.Evaluate(this);
        if (Flight.ControlAuthority.IsLost(auth))
        {
            PitchYawRoll = Vector3d.Zero;
            SASEnabled = false;
        }

        var command = PitchYawRoll * auth;
        var flapCommand = (FlapCommandOverride ?? PitchYawRoll) * auth;
        AdvanceFlapActuators(dt, flapCommand);
        // Aplicar input de rotación (en espacio local del vessel). El eje longitudinal
        // de la nave es +Y, por lo tanto los controles semánticos se mezclan así:
        // pitch → giro local X, yaw → giro local Z, roll → giro local Y.
        bool hasInput = command.Magnitude > 1e-6;
        // GimbalOffset stays the shared fallback target for any engine SolveDifferentialGimbal
        // does not reach (legacy parts with no engine runtime); GimbalCommandOverride is
        // cleared every tick so a stale per-instance command never lingers.
        foreach (var engine in Parts.ActiveEngineList)
        {
            engine.GimbalOffset = hasInput
                ? new Vector3d(command.Y, 0.0, -command.X)
                : Vector3d.Zero;
            engine.ClearGimbalCommandOverrides();
        }

        var flapAuthority = refBody != null ? FlapAngularAuthorityLocal(refBody) : Vector3d.Zero;
        var alpha = DesiredControlAngularAcceleration(command, hasInput, auth, pressure, dt);
        RcsThrusterForceBody = Vector3d.Zero;
        RcsThrusterTorqueBody = Vector3d.Zero;
        RcsThrusterMassFlowKgPerS = 0.0;
        LastRcsFunding = null;
        ApplyAttitudeActuators(alpha, hasInput || (SASEnabled && auth > 1e-6), Vector3d.Zero, pressure, dt,
            flapCommand, flapAuthority);
        CompleteResourceFunding();
        RestoreInfinitePropellant();

        // ── Real per-mount torque, from genuine engine-mount geometry (R5/R5b/R5c) ──────
        // τ = Σ r×F over every live engine instance's actual gimballed thrust vector, plus
        // the moment the RCS thrusters fired this step.
        var engineAngAccel = Parts.GetFundedAngularAcceleration(pressure, dt);
        var rcs = RcsThrusterTorqueBody;
        var appliedAngAccel = engineAngAccel;
        if (rcs.MagnitudeSquared > 0.0)
        {
            var inertia = GetMassProperties().InertiaBody;
            appliedAngAccel += new Vector3d(
                inertia.M11 > 0.0 ? rcs.X / inertia.M11 : 0.0,
                inertia.M22 > 0.0 ? rcs.Y / inertia.M22 : 0.0,
                inertia.M33 > 0.0 ? rcs.Z / inertia.M33 : 0.0);
        }
        if (appliedAngAccel.MagnitudeSquared > 0.0)
            AngularVelocity = AngularVelocity
                + Orientation.Rotate(appliedAngAccel) * (dt * DockedInertiaScale());

        // ── Aerodinámica rotacional por torque real ─────────────────────────
        // El arrastre actúa en un centro de presión detrás del CoM y crea un momento que se
        // divide por la inercia actual (que cambia al consumir propelente). El aire también
        // amortigua pitch/yaw. Así un stack pesado gira con lentitud, una Ship ligera responde
        // más y el efecto desaparece físicamente al caer q, sin una aceleración artificial fija.
        if (refBody?.Atmosphere != null)
        {
            double altitude = GetAltitude(refBody);
            double density = refBody.AtmosphericDensityAt(Position, altitude);
            var surfVel = GetAirRelativeVelocity(refBody);
            if (density > 0.0 && surfVel.Magnitude > 1.0)
            {
                double temp = System.Math.Max(1.0, refBody.AtmosphericTemperatureAt(Position, altitude));
                double? aerodynamicCenterOffset = Parts.AerodynamicCenterOffsetYM;
                bool hasBodyFlaps = HasFunctionalBodyFlaps();

                var angularAccel = AerodynamicsModel.ComputeAttitudeAngularAcceleration(
                    density,
                    surfVel,
                    Orientation.Rotate(Parts.AerodynamicReferenceAxisLocal),
                    AngularVelocity,
                    VehicleLength,
                    MaximumDiameter,
                    Parts.TransverseMomentOfInertia,
                    temp,
                    aerodynamicCenterOffset,
                    refBody.Atmosphere.SoundSpeedConstant);
                AngularVelocity += angularAccel * (dt * DockedInertiaScale());

                // Starship's four body flaps remain the primary attitude actuators during
                // unpowered entry. Their hinge force scales with q and their physical lever
                // arm; this replaces the impossible assumption that only lit engines can
                // hold a lift-producing angle of attack.
                if (hasBodyFlaps && !FlapActuators.IsZero)
                {
                    AngularVelocity += AerodynamicsModel.ComputeFlapControlAngularAcceleration(
                        density,
                        surfVel,
                        Orientation,
                        FlapActuators,
                        VehicleLength,
                        MaximumDiameter,
                        Parts.TransverseMomentOfInertia) * (dt * DockedInertiaScale());
                }
            }
        }

        // Ground contact and disturbances (a failed-on thruster) are external physical
        // torques, not actuator commands.
        var externalTorqueWorld = externalContactTorqueWorld + Orientation.Rotate(DisturbanceTorqueBody);
        if (externalTorqueWorld.MagnitudeSquared > 1e-12)
        {
            var torqueLocal = Orientation.Inverse().Rotate(externalTorqueWorld);
            double iTrans = System.Math.Max(1.0, Parts.TransverseMomentOfInertia);
            double iAxial = System.Math.Max(1.0, Parts.AxialMomentOfInertia);
            var angularAccelerationLocal = new Vector3d(
                torqueLocal.X / iTrans,
                torqueLocal.Y / iAxial,
                torqueLocal.Z / iTrans);
            AngularVelocity += Orientation.Rotate(angularAccelerationLocal) * (dt * DockedInertiaScale());
        }

        // Integrar velocidad angular → orientación
        double angMag = AngularVelocity.Magnitude;
        if (angMag > 1e-12)
        {
            double angle    = angMag * dt;
            var    deltaRot = Quaterniond.FromAxisAngle(AngularVelocity.Normalized, angle);
            Orientation = (deltaRot * Orientation).Normalize();
        }
    }

    private void AdvanceFlapActuators(double dt, Vector3d command)
    {
        FlapActuators = HasFunctionalBodyFlaps()
            ? FlapActuatorState.Advance(FlapActuators, command, dt)
            : FlapActuatorState.Zero;
    }

    // ── Staging ───────────────────────────────────────────────────────────
    // Retorna el vessel separado (debris) si hubo staging, null si no
    public Vessel? Stage()
    {
        ClearHotStageOverlapState();

        // Capture the declared separation impulse from the decoupler FireNextStage is about
        // to consume — it clears IsStagingActive internally, so this must run first.
        var decoupler = Parts.Parts
            .Where(p => p.Definition.Category == PartCategory.Decoupler && p.IsStagingActive)
            .OrderByDescending(p => p.Definition.StagePriority)
            .FirstOrDefault();
        double impulseNs = decoupler?.Definition.SeparationImpulseNs ?? 0.0;

        var transportCenter = Parts.CenterOfMass;
        var before = Parts.ComputePartLocalPositions();
        var jointsBefore = Parts.Joints.ToArray();
        var detached = Parts.FireNextStage();
        if (detached == null) return null;

        var debris = CreateDebrisVessel(detached, Name + " (debris)");

        // Every part keeps its place at the instant of separation; each fragment's datum
        // moves to its own new lowest point and inherits the rigid-body velocity there.
        // The decoupler impulse then opens them along the stack axis.
        var impulsePoint = SplitImpulsePoint(before, jointsBefore, debris);
        PlaceSplitFragmentsRigidly(before, debris, transportCenter);
        var axis = Orientation.Rotate(Vector3d.Up).Normalized;
        double opening = impulseNs > 0.0
            ? impulseNs * (TotalMass + debris.TotalMass)
                / System.Math.Max(1.0, TotalMass * debris.TotalMass)
            : DefaultSeparationOpeningMs;
        ApplyMassSplitKinematics(debris, axis, 0.0, opening, impulsePoint, impulseNs > 0 ? impulseNs : null);

        // The split changes both position and velocity. Any cached conic now describes
        // the pre-separation stack and must not be reused by the next rails tick.
        IsOnRails = false;
        OrbitalState = null;
        debris.IsOnRails = false;
        debris.OrbitalState = null;

        return debris;
    }

    /// <summary>
    /// Structural split at an overloaded joint: detaches the child subtree into a debris
    /// vessel sharing this vessel's kinematics (plus a small relative push and a
    /// mass-weighted geometric offset). Clears any hot-stage overlap window, same as
    /// <see cref="Stage"/>.
    /// </summary>
    public Vessel? BreakAtJoint(Joint joint)
    {
        ClearHotStageOverlapState();

        var transportCenter = Parts.CenterOfMass;
        var before = Parts.ComputePartLocalPositions();
        var detached = Parts.SplitAtJoint(joint);
        if (detached == null) return null;

        var debris = CreateDebrisVessel(detached, Name + " (structural debris)");

        // Fragments start where their parts were, with a gentle opening along vessel +Y.
        var impulsePoint = SplitImpulsePoint(before, [joint], debris);
        PlaceSplitFragmentsRigidly(before, debris, transportCenter);
        var axis = Orientation.Rotate(Vector3d.Up).Normalized;
        const double relativeOpenMs = 0.5;
        ApplyMassSplitKinematics(debris, axis, 0.0, relativeOpenMs, impulsePoint);

        IsOnRails = false;
        OrbitalState = null;
        debris.IsOnRails = false;
        debris.OrbitalState = null;

        return debris;
    }

    /// <summary>
    /// Splits kinematics between this vessel and <paramref name="other"/> about their shared
    /// centre of mass. Conserves linear momentum (equal-and-opposite impulse), the centre of
    /// mass (complementary, mass-weighted offsets) and angular momentum exactly — the latter
    /// requires transporting each fragment's CoM velocity by ω × r, which a naive "both
    /// inherit ω" split silently drops.
    /// </summary>
    private void ApplyMassSplitKinematics(
        Vessel other, Vector3d separationAxis, double separationDistanceM, double relativeOpeningMs,
        Vector3d impulsePointWorld, double? declaredImpulseNs = null)
    {
        double mThis = System.Math.Max(TotalMass, 1.0);
        double mOther = System.Math.Max(other.TotalMass, 1.0);
        double mTotal = mThis + mOther;

        var rThis = separationAxis * (separationDistanceM * mOther / mTotal);
        var rOther = -separationAxis * (separationDistanceM * mThis / mTotal);
        Position += rThis;
        other.Position += rOther;

        Velocity += AngularVelocity.Cross(rThis);
        other.Velocity += AngularVelocity.Cross(rOther);

        if (SloshStates.Count > 0 || other.SloshStates.Count > 0)
        {
            SloshVesselState.Separate(this, other, impulsePointWorld, separationAxis, relativeOpeningMs, declaredImpulseNs);
            return;
        }
        Velocity += separationAxis * (relativeOpeningMs * mOther / mTotal);
        other.Velocity -= separationAxis * (relativeOpeningMs * mThis / mTotal);
    }

    private Vector3d SplitImpulsePoint(Dictionary<Part, Vector3d> before, IReadOnlyList<Joint> oldJoints, Vessel fragment)
    {
        var cut = oldJoints.FirstOrDefault(j => !Parts.Joints.Contains(j) && !fragment.Parts.Joints.Contains(j));
        if (cut != null)
        {
            var node = cut.Parent.Definition.AttachmentNodes.FirstOrDefault(n => n.Id == cut.ParentNodeId);
            if (node != null && before.TryGetValue(cut.Parent, out var parent))
                return Position + Orientation.Rotate(parent + new Vector3d(node.Position[0], node.Position[1], node.Position[2]));
            if (before.TryGetValue(cut.Parent, out parent) && before.TryGetValue(cut.Child, out var child))
                return Position + Orientation.Rotate((parent + child) / 2);
        }
        throw new InvalidOperationException("Separation requires the original joint geometry.");
    }

    private void ClearHotStageOverlapState()
    {
        HotStageOverlapRemaining = 0.0;
        Parts.HotStageOverlapActive = false;
        HotStageOverlapCompletedPending = false;
    }

    /// <summary>
    /// Re-datums this vessel and a fragment just split from it so that every part keeps the
    /// world position it had in the joined stack (<paramref name="before"/>, the stack's
    /// part positions). Each fragment's datum (its new lowest point) moves by the change of
    /// frame and takes the rigid-body velocity v + ω×Δ there; angular velocity is shared.
    /// Mass, centre of mass, linear and angular momentum are conserved exactly.
    /// </summary>
    private void PlaceSplitFragmentsRigidly(Dictionary<Part, Vector3d> before, Vessel fragment,
        Vector3d previousCenterBody)
    {
        var detachedIds = fragment.Parts.Parts.Select(p => p.InstanceId).ToHashSet(StringComparer.Ordinal);
        for (int i = SloshStates.Count - 1; i >= 0; i--)
            if (detachedIds.Contains(SloshStates[i].Store.PartInstanceId))
            {
                fragment.SloshStates.Add(SloshStates[i]);
                SloshStates.RemoveAt(i);
            }
        EnsureLegacyTransportMomentumShares(previousCenterBody);
        if (TransportMomentumShares is { } shares)
        {
            fragment.TransportMomentumShares = [];
            var fragmentIds = fragment.Parts.Parts.Select(part => part.InstanceId).ToHashSet(StringComparer.Ordinal);
            for (int i = shares.Count - 1; i >= 0; i--)
                if (shares[i].PartInstanceId is { } owner && fragmentIds.Contains(owner))
                {
                    fragment.TransportMomentumShares.Add(shares[i]);
                    shares.RemoveAt(i);
                }
        }
        else
        {
            fragment.TransportRelativeMomentumBody = null;
            fragment.TransportRelativeAngularMomentumBody = null;
        }
        var (position, velocity) = (Position, Velocity);
        Rebase(this);
        Rebase(fragment);
        Refresh(this);
        Refresh(fragment);

        static void Refresh(Vessel vessel)
        {
            if (!vessel.TryGetTransportMomentum(vessel.Parts.CenterOfMass, out var linear, out var angular)) return;
            vessel.TransportRelativeMomentumBody = linear;
            vessel.TransportRelativeAngularMomentumBody = angular;
        }

        void Rebase(Vessel vessel)
        {
            var after = vessel.Parts.ComputePartLocalPositions();
            foreach (var (part, local) in after)
            {
                if (!before.TryGetValue(part, out var original)) continue;
                var shiftWorld = Orientation.Rotate(original - local);
                if (vessel.TransportMomentumShares is { } routed)
                    for (int i = 0; i < routed.Count; i++)
                        routed[i] = routed[i] with { AngularAboutDatumBodyKgM2PerS =
                            routed[i].AngularAboutDatumBodyKgM2PerS - (original - local).Cross(routed[i].LinearBodyKgMps) };
                vessel.Position = position + shiftWorld;
                vessel.Velocity = velocity + AngularVelocity.Cross(shiftWorld);
                return;
            }
        }
    }

    private Vessel CreateDebrisVessel(PartGraph detached, string name)
    {
        var debris = new Vessel
        {
            Name            = name,
            Position        = Position,
            Velocity        = Velocity,
            Orientation     = Orientation,
            AngularVelocity = AngularVelocity,
            ReferenceBodyId = ReferenceBodyId,
            SASEnabled      = SASEnabled,
        };
        if (detached.Root != null) debris.Parts.SetRoot(detached.Root);
        foreach (var p in detached.Parts) debris.Parts.AddPart(p);
        foreach (var j in detached.Joints) debris.Parts.AddJoint(j);
        // Parity with DeployPayload: staged debris (Super Heavy) must inherit catch /
        // landing contact geometry declared on its parts so R12 can arm a tower catch.
        debris.ConfigureLandingContactsFromParts();
        debris.ConfigureCatchContactsFromParts();
        return debris;
    }

    /// <summary>
    /// Deploys a part subtree as a fully independent, controllable vessel. The split uses
    /// equal-and-opposite impulses and complementary position offsets, conserving total
    /// mass, centre of mass, linear momentum and (via <see cref="ApplyMassSplitKinematics"/>)
    /// angular momentum in the aggregate point-mass model. The geometric offset and the
    /// commanded separation velocity are generally not parallel (a radial payload offsets
    /// one way, its eject velocity fires another), so each is split through the shared
    /// helper independently.
    /// </summary>
    public Vessel? DeployPayload(
        string rootPartInstanceId,
        string? payloadName = null,
        Vector3d? separationVelocityLocal = null)
    {
        var part = Parts.Parts.FirstOrDefault(p => p.InstanceId == rootPartInstanceId);
        if (part == null) return null;

        var transportCenter = Parts.CenterOfMass;
        var before = Parts.ComputePartLocalPositions();
        var jointsBefore = Parts.Joints.ToArray();
        var detached = Parts.DetachSubtree(rootPartInstanceId);
        if (detached == null) return null;

        double payloadMass = detached.TotalMass;
        double carrierMass = Parts.TotalMass;
        if (payloadMass <= 0.0 || carrierMass <= 0.0)
            throw new InvalidOperationException("Payload separation requires positive masses.");

        Vector3d relativeVelocity = Orientation.Rotate(
            separationVelocityLocal ?? new Vector3d(0.0, 0.25, 0.0));

        var payload = new Vessel
        {
            Name = payloadName ?? $"{Name} Payload",
            Position = Position,
            Velocity = Velocity,
            Orientation = Orientation,
            AngularVelocity = AngularVelocity,
            ReferenceBodyId = ReferenceBodyId,
            // A payload split changes the carrier and payload kinematics. Recompute
            // analytic rails from the post-separation state on the next scheduler tick.
            IsOnRails = false,
            SASEnabled = true,
        };
        if (detached.Root != null) payload.Parts.SetRoot(detached.Root);
        foreach (var detachedPart in detached.Parts) payload.Parts.AddPart(detachedPart);
        foreach (var joint in detached.Joints) payload.Parts.AddJoint(joint);

        var impulsePoint = SplitImpulsePoint(before, jointsBefore, payload);
        PlaceSplitFragmentsRigidly(before, payload, transportCenter);

        Vector3d velocityAxis = relativeVelocity.Magnitude > 1e-9
            ? -relativeVelocity.Normalized
            : Vector3d.Up;
        ApplyMassSplitKinematics(payload, velocityAxis, 0.0, relativeVelocity.Magnitude, impulsePoint);

        IsOnRails = false;
        OrbitalState = null;
        payload.IsOnRails = false;
        payload.OrbitalState = null;

        payload.ConfigureLandingContactsFromParts();
        payload.ConfigureCatchContactsFromParts();
        return payload;
    }
}
