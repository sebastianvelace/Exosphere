namespace Exosphere.Simulation;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;

public enum VesselDestructionCause
{
    None,
    GroundImpact,
    ThermalBreakup,
}

public class Vessel
{
    public string Id   { get; }    = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Unnamed Vessel";

    public PartGraph Parts { get; } = new();

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
    // Aerodinámica rotacional: tendencia a alinear el eje largo (+Y) con el flujo de aire
    // (weathervaning, como un dardo estable) y amortiguación angular, ambas escaladas por q.
    private const double AeroStability = 0.85;   // rad/s² a q≈30 kPa y ángulo de ataque pequeño
    private const double AeroDamping   = 2.0;    // amortiguación angular por q (1/s)
    private const double AeroQRef      = 30_000.0;

    public void Tick(double dt, CelestialBody refBody)
    {
        double pressure = refBody?.Atmosphere?.GetPressure(GetAltitude(refBody)) ?? 0.0;

        // Avanzar el spool de motores UNA VEZ por tick (antes del consumo de propelante
        // y antes de que RK4 muestree las fuerzas), para que los subpasos k₁…k₄ usen
        // el mismo ThrottleLevel spooled dentro de un mismo paso de física.
        ApplyThrottle(dt);

        Parts.ConsumePropellant(dt, pressure);

        foreach (var crew in Crew)
            crew.TickEVA(dt);

        // Aplicar input de rotación (en espacio local del vessel)
        // PitchYawRoll: X=pitch (nariz arriba/abajo), Y=yaw (nariz izq/der), Z=roll (giro)
        bool hasInput = PitchYawRoll.Magnitude > 0.01;
        if (hasInput)
        {
            double pitchYawAuthority = System.Math.Max(
                ReactionControlAuthority,
                Parts.GetPitchYawAngularAcceleration(pressure));
            double rollAuthority = System.Math.Max(
                ReactionControlAuthority,
                Parts.GetRollAngularAcceleration(pressure));
            var localAngAccel = new Vector3d(
                PitchYawRoll.X * pitchYawAuthority,
                PitchYawRoll.Y * pitchYawAuthority,
                PitchYawRoll.Z * rollAuthority);
            // Convertir de espacio local a mundo
            AngularVelocity = AngularVelocity + Orientation.Rotate(localAngAccel) * dt;

            // Limitar velocidad angular máxima (20°/s = 0.35 rad/s)
            double maxAngVel = 0.35;
            double mag = AngularVelocity.Magnitude;
            if (mag > maxAngVel)
                AngularVelocity = AngularVelocity * (maxAngVel / mag);
        }

        // SAS: solo amortigua cuando el jugador no está dando input
        if (SASEnabled && !hasInput)
            AngularVelocity = AngularVelocity * System.Math.Pow(0.005, dt);

        // ── Aerodinámica rotacional (weathervaning) ─────────────────────────
        // La presión dinámica tiende a alinear el eje largo del cohete con el flujo de aire,
        // como una flecha estable: produce el giro gravitacional natural y resiste las
        // perturbaciones en la baja atmósfera. El término se desvanece cerca de 90° de ángulo
        // de ataque (max(cosA,0)) para no luchar contra una actitud broadside deliberada
        // (belly-flop). El piloto automático y la EDL fijan la orientación directamente, así
        // que esto moldea sobre todo el vuelo manual en el ascenso inicial.
        if (refBody?.Atmosphere != null)
        {
            double q = GetDynamicPressure(refBody);
            if (q > 50.0)
            {
                var surfVel = GetSurfaceVelocity(refBody);
                if (surfVel.Magnitude > 5.0)
                {
                    var    flow = surfVel.Normalized;
                    var    axis = Orientation.Rotate(Vector3d.Up);
                    double cosA = System.Math.Clamp(axis.Dot(flow), -1.0, 1.0);
                    var    rotAxis = axis.Cross(flow);
                    double sinA = rotAxis.Magnitude;
                    double qn = System.Math.Min(q / AeroQRef, 1.5);
                    if (sinA > 1e-4)
                    {
                        double restore = AeroStability * qn * sinA * System.Math.Max(cosA, 0.0);
                        AngularVelocity += (rotAxis / sinA) * (restore * dt);
                    }
                    double damp = System.Math.Min(AeroDamping * qn * dt, 0.9);
                    AngularVelocity *= (1.0 - damp);
                }
            }
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
        var detached = Parts.FireNextStage();
        if (detached == null) return null;

        var debris = new Vessel
        {
            Name        = Name + " (debris)",
            Position    = Position,
            Velocity    = Velocity,
            Orientation = Orientation
        };
        if (detached.Root != null) debris.Parts.SetRoot(detached.Root);
        foreach (var p in detached.Parts) debris.Parts.AddPart(p);
        foreach (var j in detached.Joints) debris.Parts.AddJoint(j);
        return debris;
    }
}
