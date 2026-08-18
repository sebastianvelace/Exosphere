namespace Exosphere.Simulation;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;

public enum VesselDestructionCause
{
    None,
    GroundImpact,
    ThermalBreakup,
    StructuralBreakup,
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
    public Vector3d  PitchYawRoll  { get; set; }           // [-1, 1] por eje
    public bool      SASEnabled    { get; set; } = true;

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

    // ── Physical landing contact ─────────────────────────────────────────
    private ContactPointDefinition[] _landingContactPoints = [];
    private Vector3d _landingCenterOfMassFromDatumLocal = Vector3d.Zero;
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
            _landingCenterOfMassFromDatumLocal = Vector3d.Zero;
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

        // pointFromDatum - pointFromCom = comFromDatum
        _landingCenterOfMassFromDatumLocal = new Vector3d(
            0.0,
            def.ContactOffsetYM - def.ContactComOffsetYM,
            0.0);
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

    public RigidBodyContactInput GetContactInput(Vector3d position, Vector3d velocity) => new(
        DatumPositionWorld: position,
        CenterOfMassPositionWorld: position + Orientation.Rotate(_landingCenterOfMassFromDatumLocal),
        CenterOfMassVelocityWorld: velocity,
        Orientation: Orientation,
        AngularVelocityWorld: AngularVelocity);

    // ── Tripulación ───────────────────────────────────────────────────────
    public List<CrewMember> Crew { get; } = new();

    // ── Propiedades calculadas ─────────────────────────────────────────────
    public double    TotalMass     => Parts.TotalMass;
    public Vector3d  CenterOfMass  => Position + Orientation.Rotate(Parts.CenterOfMass);
    public double    VehicleLength => Parts.VehicleLength;
    public double    MaximumDiameter => Parts.MaximumDiameter;
    public double    NoseRadius      => Parts.NoseRadius;

    public double GetAltitude(CelestialBody body) =>
        (Position - body.Position).Magnitude - body.Radius;

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
    public Vector3d GetSurfaceVelocity(CelestialBody body) =>
        Velocity - body.Velocity - body.GetSurfaceVelocity(Position);

    // Presión dinámica q = ½·ρ·v² (Pa) respecto a la atmósfera en rotación. Es la carga
    // aerodinámica que define el "Max-Q" del ascenso y escala el arrastre y los momentos.
    public double GetDynamicPressure(CelestialBody body)
    {
        if (body.Atmosphere == null) return 0.0;
        double density = body.Atmosphere.GetDensity(GetAltitude(body));
        if (density <= 0.0) return 0.0;
        double speed = GetSurfaceVelocity(body).Magnitude;
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
        body?.Atmosphere?.GetPressure(GetAltitude(body)) ?? 0.0;

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
    /// Fills engine telemetry and returns the aggregate values computed during that same
    /// pass. This avoids repeating the active-stage thrust/mass-flow queries in HUD code.
    /// </summary>
    public void FillEngineReadouts(
        CelestialBody? body,
        List<EngineReadout> destination,
        out EngineTelemetrySummary summary) =>
        Parts.FillEngineReadouts(GetAmbientPressure(body), destination, out summary);

    public bool InjectEngineFailure(string engineInstanceId, string failureCode)
    {
        foreach (var part in Parts.Parts.Where(p => p.HasEngineRuntime))
            if (part.FailEngine(engineInstanceId, failureCode))
                return true;
        return false;
    }

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
        return Orientation.Rotate(Parts.GetTotalThrust());
    }

    // Empuje total en world space (N), corregido por presión ambiente del cuerpo.
    public Vector3d ComputeThrust(CelestialBody? refBody)
    {
        double pressure = refBody?.Atmosphere?.GetPressure(GetAltitude(refBody)) ?? 0.0;
        return Orientation.Rotate(Parts.GetTotalThrust(pressure));
    }

    // Fuerza aerodinámica total (drag + lift de cuerpo) en world space (N) — estado actual.
    public Vector3d ComputeDrag(CelestialBody body) =>
        ComputeDragAt(Position, Velocity, body);

    // Fuerza aerodinámica evaluada en un estado (pos, vel) arbitrario (para subpasos RK4).
    // Delega en AerodynamicsModel: drag orientación-dependiente (cilindro de 9 m, Cd y área
    // blend axial↔broadside, pico transónico) más la sustentación de cuerpo CL=CLmax·sin(2α).
    public Vector3d ComputeDragAt(Vector3d pos, Vector3d vel, CelestialBody body)
    {
        if (body.Atmosphere == null) return Vector3d.Zero;
        double alt     = body.GetAltitude(pos);
        double density = body.Atmosphere.GetDensity(alt);
        if (density <= 0.0) return Vector3d.Zero;

        // Velocidad relativa a la atmósfera en rotación (resta la velocidad de la
        // superficie del cuerpo y su traslación), no inercial.
        var    surfVel = vel - body.Velocity - body.GetSurfaceVelocity(pos);
        double speed   = surfVel.Magnitude;
        if (speed < 0.001 || double.IsNaN(speed)) return Vector3d.Zero;

        Vector3d axis = Orientation.Rotate(Vector3d.Up);   // eje longitudinal en mundo
        double   temp = System.Math.Max(1.0, body.Atmosphere.GetTemperature(alt));

        var drag = AerodynamicsModel.ComputeReentryDrag(
            density,
            surfVel,
            axis,
            VehicleLength,
            MaximumDiameter,
            temp,
            Parts.AxialDragCoefficient);
        var lift = AerodynamicsModel.ComputeLift(
            density, surfVel, axis, VehicleLength, MaximumDiameter);
        double parachuteArea = GetDeployedParachuteDragArea(body, pos);
        var parachuteDrag = parachuteArea > 0.0
            ? -surfVel.Normalized
                * (0.5 * density * speed * speed * parachuteArea)
            : Vector3d.Zero;
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
        double pressureFraction = body.Atmosphere.GetPressure(altitude) / 101_325.0;
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
        double pressure = refBody?.Atmosphere?.GetPressure(refBody.GetAltitude(pos)) ?? 0.0;
        var thrust = Orientation.Rotate(Parts.GetTotalThrust(pressure)) / TotalMass;

        var drag = refBody != null
            ? ComputeDragAt(pos, vel, refBody) / TotalMass
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

        double pressure = refBody?.Atmosphere?.GetPressure(refBody.GetAltitude(pos)) ?? 0.0;
        var thrust = Orientation.Rotate(Parts.GetTotalThrust(pressure)) / TotalMass;
        var drag = refBody != null
            ? ComputeDragAt(pos, vel, refBody) / TotalMass
            : Vector3d.Zero;
        return gravity + thrust + drag;
    }

    // Overload accepting IReadOnlyList for compatibility with Universe.cs
    public Vector3d ComputeNetAcceleration(IReadOnlyList<CelestialBody> bodies, CelestialBody refBody) =>
        ComputeNetAccelerationAt(Position, Velocity, bodies, refBody);

    public Vector3d ComputeGravity(IReadOnlyList<CelestialBody> bodies) =>
        ComputeGravityAt(Position, bodies);

    // ── Tick interno (consumo, SAS, rotación) ──────────────────────────────
    // Minimum cold-gas / hot-gas attitude authority when main engines are off. Live Raptor
    // gimbal authority is computed from thrust, lever arm, CoM and moment of inertia.
    private const double ReactionControlAuthority = 0.01;

    /// <summary>
    /// Returns whichever of <paramref name="value"/>/<paramref name="floor"/> has the larger
    /// magnitude — a component-wise Math.Max-by-magnitude used to apply
    /// <see cref="ReactionControlAuthority"/> as a genuine floor under real engine authority
    /// rather than an unconditional addition on top of it.
    /// </summary>
    private static double FloorByMagnitude(double value, double floor) =>
        System.Math.Abs(value) >= System.Math.Abs(floor) ? value : floor;

    public void Tick(double dt, CelestialBody refBody, Vector3d externalContactTorqueWorld = default)
    {
        Parts.BeginPhysicsTick();
        try
        {
            TickPhysics(dt, refBody, externalContactTorqueWorld);
        }
        finally
        {
            Parts.EndPhysicsTick();
        }
    }

    private void TickPhysics(
        double dt,
        CelestialBody refBody,
        Vector3d externalContactTorqueWorld)
    {
        // A failure or an impact can legitimately leave the rigid body above the normal
        // actuator envelope. Preserve that incoming rate while ordinary controls/aero
        // work it down; the 20°/s envelope must prevent commanded acceleration, not
        // erase externally generated angular momentum on the next tick.
        double incomingAngularRate = AngularVelocity.Magnitude;
        double pressure = refBody?.Atmosphere?.GetPressure(GetAltitude(refBody)) ?? 0.0;

        // Avanzar el spool de motores UNA VEZ por tick (antes del consumo de propelante
        // y antes de que RK4 muestree las fuerzas), para que los subpasos k₁…k₄ usen
        // el mismo ThrottleLevel spooled dentro de un mismo paso de física.
        ApplyThrottle(dt);
        // AdvanceEngineRuntime can fail an engine on an over-temperature transition. Do not
        // let the active-engine snapshot taken by ApplyThrottle survive that state change.
        Parts.InvalidateTickActiveEngineCache();

        Parts.ConsumePropellant(dt, pressure);
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
        // Aplicar input de rotación (en espacio local del vessel). El eje longitudinal
        // de la nave es +Y, por lo tanto los controles semánticos se mezclan así:
        // pitch → giro local X, yaw → giro local Z, roll → giro local Y.
        bool hasInput = command.Magnitude > 0.01;
        // Couple the commanded attitude torque to the actual thrust vector. Engines sit
        // below the CoM: +pitch needs -Z deflection; +yaw needs +X deflection. Roll remains
        // differential-cluster torque and has no net lateral force in this aggregate model.
        // GimbalOffset stays the shared fallback target for any engine SolveDifferentialGimbal
        // below does not reach (legacy parts with no engine runtime, still averaged directly
        // in Part.GetThrustVector); GimbalCommandOverride is cleared every tick so a stale
        // per-instance command from a previous tick — or from a tick where the pilot let go of
        // the stick — never lingers on an instance the current tick doesn't recompute.
        foreach (var engine in Parts.ActiveEngineList)
        {
            engine.GimbalOffset = hasInput
                ? new Vector3d(command.Y, 0.0, -command.X)
                : Vector3d.Zero;
            engine.ClearGimbalCommandOverrides();
        }

        if (hasInput)
        {
            // R5b — differential per-mount TVC: size a desired torque from the envelope
            // SolveDifferentialGimbal can actually deliver (only live, selected, gimballed
            // instances — see GetDifferentialTVCAngularAccelerationEnvelope), not the legacy
            // GetPitchYawAngularAcceleration/GetRollAngularAcceleration estimate, which scales
            // every active engine's full thrust by its part-wide GimbalRange regardless of
            // whether that specific mount can gimbal (e.g. Super Heavy's ungimballed outer
            // ring) — that over-counting chronically saturated the allocator's request.
            // ReactionControlAuthority stays as a floor so a command still sizes a viable
            // target even when the live cluster's own authority momentarily reads near zero.
            var envelope = Parts.GetDifferentialTVCAngularAccelerationEnvelope(pressure);
            double pitchYawAuthority =
                System.Math.Max(ReactionControlAuthority, envelope.X) * auth;
            double rollAuthority =
                System.Math.Max(ReactionControlAuthority, envelope.Y) * auth;
            var desiredLocalAngAccel = new Vector3d(
                command.X * pitchYawAuthority,
                command.Z * rollAuthority,
                command.Y * pitchYawAuthority);
            var desiredTorque = new Vector3d(
                desiredLocalAngAccel.X * Parts.TransverseMomentOfInertia,
                desiredLocalAngAccel.Y * Parts.AxialMomentOfInertia,
                desiredLocalAngAccel.Z * Parts.TransverseMomentOfInertia);
            Parts.SolveDifferentialGimbal(desiredTorque, pressure);
        }

        // ── Real per-mount torque, from genuine engine-mount geometry (R5/R5b/R5c) ──────
        // τ = Σ r×F over every live engine instance's actual gimballed thrust vector — the
        // instance's real EngineInstanceState.GimbalDeg, whatever Part.AdvanceGimbal has
        // servoed it toward: SolveDifferentialGimbal's per-mount command above when the
        // pilot is commanding attitude, or its resting/asymmetric state (engine-out, mount
        // asymmetry) when nobody is. This used to be split into a `hasInput` branch that
        // applied an idealized full-authority estimate (engine's maximum GimbalRange, not
        // live servo state) and a separate `!hasInput` branch that read this same real
        // geometry, restricted to !hasInput specifically to avoid double-counting the
        // pilot's own commanded deflection through two different models. Now that R5b
        // routes the pilot's command through this exact real per-mount geometry instead of
        // an idealized shortcut, both branches are the same physical quantity, so there is
        // only one term, applied unconditionally.
        var engineAngAccel = Parts.GetPitchYawRollAngularAcceleration(pressure);

        // RCS floor: idealized attitude-thruster authority (ReactionControlAuthority),
        // independent of engine gimbal — real spacecraft carry separate RCS jets that fire
        // whether or not the main engines do. This must stay a FLOOR (component-wise
        // Math.Max by magnitude), exactly as the pre-R5b idealized estimate's
        // `Math.Max(ReactionControlAuthority, ...)` did — simply adding it on top of the
        // real per-mount torque would make it the dominant actuator on a large vehicle
        // (ReactionControlAuthority is a fixed angular acceleration, so summing it scales
        // the equivalent RCS torque with the vessel's own moment of inertia, easily
        // exceeding what a real gimballed cluster delivers on something the size of a
        // Super Heavy). Only fills the gap when the real engine term reads below the floor.
        var rcsFloorAngAccel = hasInput
            ? new Vector3d(command.X, command.Z, command.Y) * ReactionControlAuthority * auth
            : Vector3d.Zero;

        var appliedAngAccel = new Vector3d(
            FloorByMagnitude(engineAngAccel.X, rcsFloorAngAccel.X),
            FloorByMagnitude(engineAngAccel.Y, rcsFloorAngAccel.Y),
            FloorByMagnitude(engineAngAccel.Z, rcsFloorAngAccel.Z));
        if (appliedAngAccel.MagnitudeSquared > 0.0)
            AngularVelocity = AngularVelocity + Orientation.Rotate(appliedAngAccel) * dt;

        // Limitar velocidad angular máxima (20°/s = 0.35 rad/s) — solo mientras se pilota
        // activamente; un disturbio no comandado (motor caído) no está sujeto a este límite
        // de autoridad de control.
        if (hasInput)
        {
            double maxAngVel = 0.35;
            double mag = AngularVelocity.Magnitude;
            if (mag > maxAngVel)
                AngularVelocity = AngularVelocity * (maxAngVel / mag);
        }

        // SAS: solo amortigua cuando el jugador no está dando input
        if (SASEnabled && !hasInput && auth > 1e-6)
            AngularVelocity = AngularVelocity * System.Math.Pow(0.005, dt);

        // ── Aerodinámica rotacional por torque real ─────────────────────────
        // El arrastre actúa en un centro de presión detrás del CoM y crea un momento que se
        // divide por la inercia actual (que cambia al consumir propelente). El aire también
        // amortigua pitch/yaw. Así un stack pesado gira con lentitud, una Ship ligera responde
        // más y el efecto desaparece físicamente al caer q, sin una aceleración artificial fija.
        if (refBody?.Atmosphere != null)
        {
            double altitude = GetAltitude(refBody);
            double density = refBody.Atmosphere.GetDensity(altitude);
            var surfVel = GetSurfaceVelocity(refBody);
            if (density > 0.0 && surfVel.Magnitude > 1.0)
            {
                double temp = System.Math.Max(1.0, refBody.Atmosphere.GetTemperature(altitude));
                double? aerodynamicCenterOffset = null;
                bool hasBodyFlaps = false;
                foreach (var part in Parts.PartList)
                {
                    if (!aerodynamicCenterOffset.HasValue
                        && part.Definition.AerodynamicCenterOffsetYM.HasValue)
                        aerodynamicCenterOffset = part.Definition.AerodynamicCenterOffsetYM;
                    if (part.Definition.IsStarshipFamily
                        && part.Definition.HasVehicleRole("command")
                        && !part.IsBroken)
                        hasBodyFlaps = true;
                }

                var angularAccel = AerodynamicsModel.ComputeAttitudeAngularAcceleration(
                    density,
                    surfVel,
                    Orientation.Rotate(Vector3d.Up),
                    AngularVelocity,
                    VehicleLength,
                    MaximumDiameter,
                    Parts.TransverseMomentOfInertia,
                    temp,
                    aerodynamicCenterOffset);
                AngularVelocity += angularAccel * dt;

                // Starship's four body flaps remain the primary attitude actuators during
                // unpowered entry. Their hinge force scales with q and their physical lever
                // arm; this replaces the impossible assumption that only lit engines can
                // hold a lift-producing angle of attack.
                if (hasBodyFlaps && hasInput)
                {
                    AngularVelocity += AerodynamicsModel.ComputeFlapControlAngularAcceleration(
                        density,
                        surfVel,
                        Orientation,
                        command,
                        VehicleLength,
                        MaximumDiameter,
                        Parts.TransverseMomentOfInertia) * dt;
                }
            }
        }

        // Apply the physical angular-rate envelope after every torque source, including aero.
        // Clamping earlier allowed a high-q aerodynamic moment to bypass the limit in the same
        // integration step and produce a numerically explosive snap.
        const double maximumAngularRate = 0.35;
        double controlledRateEnvelope = System.Math.Max(
            maximumAngularRate, incomingAngularRate);
        double finalAngularRate = AngularVelocity.Magnitude;
        if (finalAngularRate > controlledRateEnvelope)
            AngularVelocity *= controlledRateEnvelope / finalAngularRate;

        // Ground contact is an external physical torque, not an actuator command. Apply it
        // after the vehicle-control rate envelope so an impact can genuinely rotate/tip the
        // rigid body instead of being silently clipped to the autopilot's 20°/s limit.
        if (externalContactTorqueWorld.MagnitudeSquared > 1e-12)
        {
            var torqueLocal = Orientation.Inverse().Rotate(externalContactTorqueWorld);
            double iTrans = System.Math.Max(1.0, Parts.TransverseMomentOfInertia);
            double iAxial = System.Math.Max(1.0, Parts.AxialMomentOfInertia);
            var angularAccelerationLocal = new Vector3d(
                torqueLocal.X / iTrans,
                torqueLocal.Y / iAxial,
                torqueLocal.Z / iTrans);
            AngularVelocity += Orientation.Rotate(angularAccelerationLocal) * dt;
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

        var detached = Parts.FireNextStage();
        if (detached == null) return null;

        var debris = CreateDebrisVessel(detached, Name + " (debris)");

        // Rebase the remaining vessel from the old stack-base datum to the physical
        // separation plane, split by mass ratio so both fragments move (not just this one),
        // conserving the combined centre of mass. Without this, both vessels occupy the same
        // world point while their renderers use different local origins, producing total
        // interpenetration.
        var axis = Orientation.Rotate(Vector3d.Up).Normalized;
        double gap = System.Math.Max(1.0, debris.VehicleLength);
        double opening = impulseNs > 0.0
            ? impulseNs * (TotalMass + debris.TotalMass)
                / System.Math.Max(1.0, TotalMass * debris.TotalMass)
            : DefaultSeparationOpeningMs;
        ApplyMassSplitKinematics(debris, axis, gap, opening);

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

        var detached = Parts.SplitAtJoint(joint);
        if (detached == null) return null;

        var debris = CreateDebrisVessel(detached, Name + " (structural debris)");

        // Gentle separation along vessel +Y so fragments do not occupy the same origin.
        var axis = Orientation.Rotate(Vector3d.Up).Normalized;
        double gap = System.Math.Max(1.0, debris.VehicleLength);
        const double relativeOpenMs = 0.5;
        ApplyMassSplitKinematics(debris, axis, gap, relativeOpenMs);

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
        Vessel other, Vector3d separationAxis, double separationDistanceM, double relativeOpeningMs)
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

        Velocity += separationAxis * (relativeOpeningMs * mOther / mTotal);
        other.Velocity -= separationAxis * (relativeOpeningMs * mThis / mTotal);
    }

    private void ClearHotStageOverlapState()
    {
        HotStageOverlapRemaining = 0.0;
        Parts.HotStageOverlapActive = false;
        HotStageOverlapCompletedPending = false;
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

        var positions = Parts.ComputePartLocalPositions();
        Vector3d localOffset = positions.TryGetValue(part, out var offset)
            ? offset - Parts.CenterOfMass
            : Vector3d.Zero;
        var detached = Parts.DetachSubtree(rootPartInstanceId);
        if (detached == null) return null;

        double payloadMass = detached.TotalMass;
        double carrierMass = Parts.TotalMass;
        if (payloadMass <= 0.0 || carrierMass <= 0.0)
            throw new InvalidOperationException("Payload separation requires positive masses.");

        Vector3d relativePosition = Orientation.Rotate(localOffset);
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

        Vector3d positionAxis = relativePosition.Magnitude > 1e-9
            ? -relativePosition.Normalized
            : Vector3d.Up;
        ApplyMassSplitKinematics(payload, positionAxis, relativePosition.Magnitude, 0.0);

        Vector3d velocityAxis = relativeVelocity.Magnitude > 1e-9
            ? -relativeVelocity.Normalized
            : Vector3d.Up;
        ApplyMassSplitKinematics(payload, velocityAxis, 0.0, relativeVelocity.Magnitude);

        IsOnRails = false;
        OrbitalState = null;
        payload.IsOnRails = false;
        payload.OrbitalState = null;

        payload.ConfigureLandingContactsFromParts();
        payload.ConfigureCatchContactsFromParts();
        return payload;
    }
}
