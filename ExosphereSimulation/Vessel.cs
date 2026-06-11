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

    // ── Fuerzas ───────────────────────────────────────────────────────────

    // Aplica el throttle actual a todos los motores activos
    private void ApplyThrottle()
    {
        foreach (var engine in Parts.ActiveEngines)
            engine.ThrottleLevel = Throttle;
    }

    // Empuje total en world space (N)
    public Vector3d ComputeThrust()
    {
        ApplyThrottle();
        return Orientation.Rotate(Parts.GetTotalThrust());
    }

    // Arrastre aerodinámico en world space (N)
    public Vector3d ComputeDrag(CelestialBody body)
    {
        if (body.Atmosphere == null) return Vector3d.Zero;
        double alt     = GetAltitude(body);
        double density = body.Atmosphere.GetDensity(alt);
        if (density <= 0.0) return Vector3d.Zero;

        var    surfVel = GetSurfaceVelocity(body);
        double speed   = surfVel.Magnitude;
        if (speed < 0.001) return Vector3d.Zero;

        // Estimar área de referencia proporcional al número de piezas
        double radius  = System.Math.Max(0.5, System.Math.Sqrt(Parts.Parts.Count * 0.3));
        double area    = System.Math.PI * radius * radius;
        double cd      = 0.3;

        // Factor de Mach (resistencia transónica)
        double temp    = density > 0
            ? 288.15 * (density / body.Atmosphere.SeaLevelDensity)
            : 288.15;
        double mach    = speed / System.Math.Sqrt(1.4 * 287.0 * System.Math.Max(1.0, temp));
        double machMul = mach < 0.8 ? 1.0
                       : mach < 1.0 ? 1.0 + (mach - 0.8) * 5.0
                       : mach < 1.2 ? 2.0
                       : mach < 5.0 ? 2.0 - (mach - 1.2) * 0.25
                       : 1.0;

        double drag = 0.5 * density * speed * speed * cd * area * machMul;
        return surfVel.Normalized * (-drag);
    }

    // Aceleración gravitacional total de todos los cuerpos (m/s²)
    public Vector3d ComputeGravity(IEnumerable<CelestialBody> bodies)
    {
        var accel = Vector3d.Zero;
        foreach (var body in bodies)
            accel = accel + body.GetGravityAt(Position);
        return accel;
    }

    // Aceleración neta para el integrador RK4 (m/s²)
    public Vector3d ComputeNetAcceleration(IEnumerable<CelestialBody> bodies, CelestialBody? refBody)
    {
        if (TotalMass <= 0.0) return ComputeGravity(bodies);
        var gravity = ComputeGravity(bodies);
        var thrust  = ComputeThrust() / TotalMass;
        var drag    = refBody != null ? ComputeDrag(refBody) / TotalMass : Vector3d.Zero;
        return gravity + thrust + drag;
    }

    // Overload accepting IReadOnlyList for compatibility with Universe.cs
    public Vector3d ComputeNetAcceleration(IReadOnlyList<CelestialBody> bodies, CelestialBody refBody) =>
        ComputeNetAcceleration((IEnumerable<CelestialBody>)bodies, refBody);

    public Vector3d ComputeGravity(IReadOnlyList<CelestialBody> bodies) =>
        ComputeGravity((IEnumerable<CelestialBody>)bodies);

    // ── Tick interno (consumo, SAS, rotación) ──────────────────────────────
    public void Tick(double dt, CelestialBody refBody)
    {
        double pressure = refBody?.Atmosphere?.GetPressure(GetAltitude(refBody)) ?? 0.0;
        Parts.ConsumePropellant(dt, pressure);

        // Tick EVA crew
        foreach (var crew in Crew)
            crew.TickEVA(dt);

        // SAS: amortigua velocidad angular
        if (SASEnabled)
            AngularVelocity = AngularVelocity * System.Math.Pow(0.001, dt);

        // Aplicar velocidad angular a la orientación
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
