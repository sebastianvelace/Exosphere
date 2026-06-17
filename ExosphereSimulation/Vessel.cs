namespace Exosphere.Simulation;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

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

    public void ReleaseGroundHold() => IsGroundHeld = false;

    // ── Tripulación ───────────────────────────────────────────────────────
    public List<CrewMember> Crew { get; } = new();

    // ── Propiedades calculadas ─────────────────────────────────────────────
    public double    TotalMass     => Parts.TotalMass;
    public Vector3d  CenterOfMass  => Position + Orientation.Rotate(Parts.CenterOfMass);

    public double GetAltitude(CelestialBody body) =>
        (Position - body.Position).Magnitude - body.Radius;

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

    // Aplica el throttle actual a todos los motores activos
    private void ApplyThrottle()
    {
        foreach (var engine in Parts.ActiveEngines)
            engine.ThrottleLevel = Throttle;
    }

    // Empuje total en world space (N) — empuje de vacío (compatibilidad).
    public Vector3d ComputeThrust()
    {
        ApplyThrottle();
        return Orientation.Rotate(Parts.GetTotalThrust());
    }

    // Empuje total en world space (N), corregido por presión ambiente del cuerpo.
    public Vector3d ComputeThrust(CelestialBody? refBody)
    {
        ApplyThrottle();
        double pressure = refBody?.Atmosphere?.GetPressure(GetAltitude(refBody)) ?? 0.0;
        return Orientation.Rotate(Parts.GetTotalThrust(pressure));
    }

    // Arrastre aerodinámico en world space (N) — estado actual.
    public Vector3d ComputeDrag(CelestialBody body) =>
        ComputeDragAt(Position, Velocity, body);

    // Arrastre evaluado en un estado (pos, vel) arbitrario (para subpasos RK4).
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

        // ── Área de referencia y Cd dependientes de la orientación ──────────────
        // El vehículo se modela como un cilindro de diámetro de núcleo fijo. El área
        // presentada y la "romería" (bluffness) dependen del ángulo entre su eje
        // longitudinal (local +Y) y el flujo de aire: de morro/cola (axial) ofrece un
        // área frontal pequeña y aerodinámica; de costado (belly-flop) ofrece un área
        // lateral grande y roma. Esto reproduce la actitud de alta resistencia en la
        // reentrada de Starship y la baja resistencia del flip-and-burn vertical.
        const double diameter = 9.0;                                  // núcleo Starship/SH (m)
        double radius      = diameter * 0.5;
        double length      = System.Math.Max(diameter, Parts.Parts.Count * 12.0);  // altura del stack
        double axialArea   = System.Math.PI * radius * radius;        // de morro/cola
        double lateralArea = diameter * length;                       // de costado (broadside)

        Vector3d axis = Orientation.Rotate(Vector3d.Up);              // eje longitudinal en mundo
        double cosA = System.Math.Abs(axis.Dot(surfVel.Normalized));  // 1=axial, 0=broadside
        double aa   = cosA * cosA;
        double area = lateralArea + (axialArea - lateralArea) * aa;
        double cd   = 1.5 + (0.6 - 1.5) * aa;                         // romo 1.5 ↔ aerodinámico 0.6

        // Número de Mach usando la temperatura ISA real del modelo atmosférico,
        // a = √(γ·R_specific·T), γ=1.4, R_specific=287 J/(kg·K) para aire.
        double temp    = System.Math.Max(1.0, body.Atmosphere.GetTemperature(alt));
        double mach    = speed / System.Math.Sqrt(1.4 * 287.0 * temp);
        double machMul = mach < 0.8 ? 1.0
                       : mach < 1.0 ? 1.0 + (mach - 0.8) * 5.0
                       : mach < 1.2 ? 2.0
                       : mach < 5.0 ? 2.0 - (mach - 1.2) * 0.25
                       : 1.0;

        // Arrastre = ½·ρ·v²·Cd·A, opuesto a la velocidad relativa a la atmósfera.
        double drag = 0.5 * density * speed * speed * cd * area * machMul;
        return surfVel.Normalized * (-drag);
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
        double pressure = refBody?.Atmosphere?.GetPressure(refBody.GetAltitude(pos)) ?? 0.0;
        ApplyThrottle();
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
    // Autoridad de control: cuántos rad/s² aplica el input máximo (±1)
    private const double ControlAuthority = 0.6;
    // Aerodinámica rotacional: tendencia a alinear el eje largo (+Y) con el flujo de aire
    // (weathervaning, como un dardo estable) y amortiguación angular, ambas escaladas por q.
    private const double AeroStability = 0.85;   // rad/s² a q≈30 kPa y ángulo de ataque pequeño
    private const double AeroDamping   = 2.0;    // amortiguación angular por q (1/s)
    private const double AeroQRef      = 30_000.0;

    public void Tick(double dt, CelestialBody refBody)
    {
        double pressure = refBody?.Atmosphere?.GetPressure(GetAltitude(refBody)) ?? 0.0;
        Parts.ConsumePropellant(dt, pressure);

        foreach (var crew in Crew)
            crew.TickEVA(dt);

        // Aplicar input de rotación (en espacio local del vessel)
        // PitchYawRoll: X=pitch (nariz arriba/abajo), Y=yaw (nariz izq/der), Z=roll (giro)
        bool hasInput = PitchYawRoll.Magnitude > 0.01;
        if (hasInput)
        {
            var localAngAccel = new Vector3d(
                PitchYawRoll.X * ControlAuthority,
                PitchYawRoll.Y * ControlAuthority,
                PitchYawRoll.Z * ControlAuthority);
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
