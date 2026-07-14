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
    public string Id   { get; private set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Unnamed Vessel";

    public PartGraph Parts { get; } = new();

    public Vessel() { }

    /// <summary>Creates a vessel with a stable identity for save/load roundtrips.</summary>
    public static Vessel CreateWithId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Vessel id must be non-empty.", nameof(id));
        return new Vessel { Id = id };
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
        foreach (var engine in Parts.ActiveEngines)
            engine.SpoolToward(Throttle, dt);
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
            density, surfVel, axis, VehicleLength, MaximumDiameter, temp);
        var lift = AerodynamicsModel.ComputeLift(
            density, surfVel, axis, VehicleLength, MaximumDiameter);
        return drag + lift;
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

    // Overload accepting IReadOnlyList for compatibility with Universe.cs
    public Vector3d ComputeNetAcceleration(IReadOnlyList<CelestialBody> bodies, CelestialBody refBody) =>
        ComputeNetAcceleration((IEnumerable<CelestialBody>)bodies, refBody);

    public Vector3d ComputeGravity(IReadOnlyList<CelestialBody> bodies) =>
        ComputeGravity((IEnumerable<CelestialBody>)bodies);

    // ── Tick interno (consumo, SAS, rotación) ──────────────────────────────
    // Minimum cold-gas / hot-gas attitude authority when main engines are off. Live Raptor
    // gimbal authority is computed from thrust, lever arm, CoM and moment of inertia.
    private const double ReactionControlAuthority = 0.01;
    public void Tick(double dt, CelestialBody refBody, Vector3d externalContactTorqueWorld = default)
    {
        double pressure = refBody?.Atmosphere?.GetPressure(GetAltitude(refBody)) ?? 0.0;

        // Avanzar el spool de motores UNA VEZ por tick (antes del consumo de propelante
        // y antes de que RK4 muestree las fuerzas), para que los subpasos k₁…k₄ usen
        // el mismo ThrottleLevel spooled dentro de un mismo paso de física.
        ApplyThrottle(dt);

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
        foreach (var engine in Parts.ActiveEngines)
            engine.GimbalOffset = hasInput
                ? new Vector3d(command.Y, 0.0, -command.X)
                : Vector3d.Zero;

        if (hasInput)
        {
            double pitchYawAuthority = System.Math.Max(
                ReactionControlAuthority,
                Parts.GetPitchYawAngularAcceleration(pressure)) * auth;
            double rollAuthority = System.Math.Max(
                ReactionControlAuthority,
                Parts.GetRollAngularAcceleration(pressure)) * auth;
            var localAngAccel = new Vector3d(
                command.X * pitchYawAuthority,
                command.Z * rollAuthority,
                command.Y * pitchYawAuthority);
            // Convertir de espacio local a mundo
            AngularVelocity = AngularVelocity + Orientation.Rotate(localAngAccel) * dt;

            // Limitar velocidad angular máxima (20°/s = 0.35 rad/s)
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
                var angularAccel = AerodynamicsModel.ComputeAttitudeAngularAcceleration(
                    density,
                    surfVel,
                    Orientation.Rotate(Vector3d.Up),
                    AngularVelocity,
                    VehicleLength,
                    MaximumDiameter,
                    Parts.TransverseMomentOfInertia,
                    temp);
                AngularVelocity += angularAccel * dt;

                // Starship's four body flaps remain the primary attitude actuators during
                // unpowered entry. Their hinge force scales with q and their physical lever
                // arm; this replaces the impossible assumption that only lit engines can
                // hold a lift-producing angle of attack.
                bool hasBodyFlaps = Parts.Parts.Any(p =>
                    p.Definition.Id == "starship_command" && !p.IsBroken);
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
        double finalAngularRate = AngularVelocity.Magnitude;
        if (finalAngularRate > maximumAngularRate)
            AngularVelocity *= maximumAngularRate / finalAngularRate;

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

        var detached = Parts.FireNextStage();
        if (detached == null) return null;

        return CreateDebrisVessel(detached, Name + " (debris)");
    }

    /// <summary>
    /// Structural split at an overloaded joint: detaches the child subtree into a debris
    /// vessel sharing this vessel's kinematics (plus a small relative push). Clears any
    /// hot-stage overlap window, same as <see cref="Stage"/>.
    /// </summary>
    public Vessel? BreakAtJoint(Joint joint)
    {
        ClearHotStageOverlapState();

        var detached = Parts.SplitAtJoint(joint);
        if (detached == null) return null;

        var debris = CreateDebrisVessel(detached, Name + " (structural debris)");

        // Gentle separation along vessel +Y so fragments do not occupy the same origin.
        var axis = Orientation.Rotate(Vector3d.Up).Normalized;
        double mainMass = System.Math.Max(TotalMass, 1.0);
        double debrisMass = System.Math.Max(debris.TotalMass, 1.0);
        double totalMass = mainMass + debrisMass;
        const double relativeOpenMs = 0.5;
        Velocity += axis * (relativeOpenMs * debrisMass / totalMass);
        debris.Velocity -= axis * (relativeOpenMs * mainMass / totalMass);

        return debris;
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
        return debris;
    }
}
