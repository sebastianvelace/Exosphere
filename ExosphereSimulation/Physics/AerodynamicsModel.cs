namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

/// <summary>
/// Re-entry aerodynamics helpers: dynamic pressure, Mach effects, and an
/// orientation-dependent drag model. A blunt body (broadside / belly-flop) presents
/// a large area and a high drag coefficient and brakes hard in the atmosphere; a
/// streamlined nose-/tail-first attitude presents a small area and a low coefficient.
/// Pure SI (kg/m³, m/s, m², N). Sin Godot.
/// </summary>
public static class AerodynamicsModel
{
    /// <summary>Drag force (world space, N), opposing the surface-relative velocity.</summary>
    public static Vector3d ComputeDrag(
        double density,
        Vector3d surfaceVelocity,
        double dragCoefficient,
        double referenceArea)
    {
        double speed = surfaceVelocity.Magnitude;
        if (speed < 1e-3) return Vector3d.Zero;
        double magnitude = 0.5 * density * speed * speed * dragCoefficient * referenceArea;
        return surfaceVelocity.Normalized * (-magnitude);
    }

    /// <summary>Dynamic pressure q = ½·ρ·v² (Pa).</summary>
    public static double ComputeDynamicPressure(double density, double speed) =>
        0.5 * density * speed * speed;

    /// <summary>Mach number from airspeed (m/s) and local temperature (K): M = v / √(γ·R·T).</summary>
    public static double ComputeMach(double speed, double temperature)
    {
        const double gamma = 1.4;
        const double R     = 287.0;  // J/(kg·K) gas específico del aire
        double sos = System.Math.Sqrt(gamma * R * System.Math.Max(1.0, temperature));
        return speed / sos;
    }

    /// <summary>Cd multiplier across the transonic drag-rise peak (≈2× near Mach 1).</summary>
    public static double GetMachDragMultiplier(double mach) =>
        mach < 0.8 ? 1.0
        : mach < 1.0 ? 1.0 + (mach - 0.8) * 5.0
        : mach < 1.2 ? 2.0
        : mach < 5.0 ? 2.0 - (mach - 1.2) * 0.25
        : 1.0;

    /// <summary>Axial reference area (m²) from the declared physical envelope.</summary>
    public static double EstimateReferenceArea(PartGraph graph)
    {
        double radius = graph.MaximumDiameter * 0.5;
        return System.Math.PI * radius * radius;
    }

    // ── Orientation-dependent re-entry aero (cilindro Starship/SH) ─────────────
    //
    // El vehículo se modela como un cilindro de núcleo de 9 m. El área presentada y
    // la "romería" (bluffness) dependen del ángulo entre su eje longitudinal (local
    // +Y) y el flujo: de morro/cola (axial) ⇒ área pequeña y Cd bajo; de costado
    // (belly-flop) ⇒ área lateral grande y Cd alto. Reproduce la reentrada de alta
    // resistencia de Starship y el flip-and-burn vertical de baja resistencia.

    /// <summary>Starship/Super-Heavy core diameter (m).</summary>
    public const double CoreDiameter = 9.0;

    /// <summary>
    /// Effective frontal area (m²) for a stack of <paramref name="partCount"/> parts at an
    /// axial alignment <paramref name="cosAlpha"/> = |axis·flow| (1 = nose/tail-on, 0 = broadside).
    /// Interpolates between the axial cross-section (πr²) and the broadside slab (D·L).
    /// </summary>
    public static double EffectiveArea(int partCount, double cosAlpha)
        => EffectiveArea(
            System.Math.Max(CoreDiameter, partCount * 12.0),
            CoreDiameter,
            cosAlpha);

    /// <summary>
    /// Effective projected area for a cylindrical vehicle with an explicit length and
    /// diameter. This is the authoritative path for Starship; the part-count overload only
    /// remains for legacy craft whose JSON does not yet declare dimensions.
    /// </summary>
    public static double EffectiveArea(double length, double diameter, double cosAlpha)
    {
        diameter = System.Math.Max(0.1, diameter);
        length = System.Math.Max(diameter, length);
        double radius      = diameter * 0.5;
        double axialArea   = System.Math.PI * radius * radius;
        double lateralArea = diameter * length;
        double aa          = System.Math.Clamp(cosAlpha * cosAlpha, 0.0, 1.0);
        return lateralArea + (axialArea - lateralArea) * aa;
    }

    /// <summary>
    /// Effective drag coefficient blended between a blunt broadside body (≈1.5) and a
    /// streamlined axial body (≈0.6) by the axial alignment <paramref name="cosAlpha"/>.
    /// </summary>
    public static double EffectiveDragCoefficient(double cosAlpha)
    {
        double aa = System.Math.Clamp(cosAlpha * cosAlpha, 0.0, 1.0);
        return 1.5 + (0.6 - 1.5) * aa;
    }

    // ── Body lift (R6) ─────────────────────────────────────────────────────────
    //
    // Un cilindro simétrico volando con ángulo de ataque genera sustentación de
    // cuerpo perpendicular al flujo, en el plano eje-flujo, hacia el lado del morro.
    // Modelo Newtoniano simplificado CL = CLmax·sin(2α): cero de morro (α=0) y de
    // costado puro (α=90°), máximo a 45°. Con CLmax 0.7 y el Cd/área combinados del
    // modelo de drag, a α≈70° da L/D ≈ 0.3 — el régimen real de la Starship en EDL.

    /// <summary>Peak body-lift coefficient of the 9 m cylinder (at 45° angle of attack).</summary>
    public const double MaxLiftCoefficient = 0.7;

    /// <summary>Nominal Starship entry angle of attack: high drag with positive body lift.</summary>
    public const double NominalEntryAngleOfAttackDegrees = 70.0;

    /// <summary>
    /// Builds the longitudinal-axis target for a lift-up atmospheric entry. The resulting
    /// axis is <paramref name="angleOfAttackDegrees"/> away from prograde and lies in the
    /// velocity/local-up plane, so body lift points away from the planet instead of merely
    /// accepting a ballistic, zero-lift broadside descent.
    /// </summary>
    public static Vector3d ComputeLiftUpEntryAxis(
        Vector3d localUp,
        Vector3d velocityDirection,
        double angleOfAttackDegrees = NominalEntryAngleOfAttackDegrees)
    {
        var flow = velocityDirection.Normalized;
        var liftUp = localUp - flow * localUp.Dot(flow);
        if (liftUp.Magnitude < 1e-6)
            liftUp = System.Math.Abs(flow.Dot(Vector3d.Up)) < 0.9
                ? (Vector3d.Up - flow * Vector3d.Up.Dot(flow)).Normalized
                : (Vector3d.Right - flow * Vector3d.Right.Dot(flow)).Normalized;
        else
            liftUp = liftUp.Normalized;

        double alpha = System.Math.Clamp(angleOfAttackDegrees, 0.0, 90.0)
            * MathUtils.DEG_TO_RAD;
        return (flow * System.Math.Cos(alpha) + liftUp * System.Math.Sin(alpha)).Normalized;
    }

    /// <summary>
    /// Body-lift coefficient CL = CLmax·sin(2α) from the signed axial alignment
    /// <paramref name="cosAlpha"/> = axis·flow. Zero nose-/tail-on and at exact broadside;
    /// negative when flying tail-first so the lift flips to the correct side.
    /// </summary>
    public static double EffectiveLiftCoefficient(double cosAlpha)
    {
        double c = System.Math.Clamp(cosAlpha, -1.0, 1.0);
        return MaxLiftCoefficient * 2.0 * c * System.Math.Sqrt(1.0 - c * c);
    }

    /// <summary>
    /// Body-lift force (world space, N): perpendicular to the surface-relative flow, in the
    /// axis-flow plane, toward the side the nose points. <paramref name="longitudinalAxis"/>
    /// is the vessel's long axis (local +Y, nose direction) expressed in world space.
    /// </summary>
    public static Vector3d ComputeLift(
        double density,
        Vector3d surfaceVelocity,
        Vector3d longitudinalAxis,
        int partCount)
        => ComputeLift(
            density,
            surfaceVelocity,
            longitudinalAxis,
            System.Math.Max(CoreDiameter, partCount * 12.0),
            CoreDiameter);

    public static Vector3d ComputeLift(
        double density,
        Vector3d surfaceVelocity,
        Vector3d longitudinalAxis,
        double vehicleLength,
        double vehicleDiameter)
    {
        double speed = surfaceVelocity.Magnitude;
        if (density <= 0.0 || speed < 1e-3) return Vector3d.Zero;

        var flowDir     = surfaceVelocity.Normalized;
        var axis        = longitudinalAxis.Normalized;
        double cosAlpha = axis.Dot(flowDir);

        var perp = axis - flowDir * cosAlpha;
        double perpLen = perp.Magnitude;
        if (perpLen < 1e-6) return Vector3d.Zero;   // axial flight: no lift plane defined

        double cl   = EffectiveLiftCoefficient(cosAlpha);
        double area = EffectiveArea(vehicleLength, vehicleDiameter, System.Math.Abs(cosAlpha));
        double magnitude = 0.5 * density * speed * speed * cl * area;
        return (perp / perpLen) * magnitude;
    }

    /// <summary>
    /// Full re-entry drag force (world space, N) for a vessel: orientation-dependent
    /// area and Cd, with the transonic Mach multiplier folded in. <paramref name="surfaceVelocity"/>
    /// is the velocity relative to the rotating atmosphere; <paramref name="longitudinalAxis"/> is
    /// the vessel's long axis (local +Y) expressed in world space.
    /// </summary>
    public static Vector3d ComputeReentryDrag(
        double density,
        Vector3d surfaceVelocity,
        Vector3d longitudinalAxis,
        int partCount,
        double temperature)
        => ComputeReentryDrag(
            density,
            surfaceVelocity,
            longitudinalAxis,
            System.Math.Max(CoreDiameter, partCount * 12.0),
            CoreDiameter,
            temperature);

    public static Vector3d ComputeReentryDrag(
        double density,
        Vector3d surfaceVelocity,
        Vector3d longitudinalAxis,
        double vehicleLength,
        double vehicleDiameter,
        double temperature)
    {
        double speed = surfaceVelocity.Magnitude;
        if (density <= 0.0 || speed < 1e-3) return Vector3d.Zero;

        double cosAlpha = System.Math.Abs(longitudinalAxis.Normalized.Dot(surfaceVelocity.Normalized));
        double area     = EffectiveArea(vehicleLength, vehicleDiameter, cosAlpha);
        double cd       = EffectiveDragCoefficient(cosAlpha);
        double machMul  = GetMachDragMultiplier(ComputeMach(speed, temperature));

        double magnitude = 0.5 * density * speed * speed * cd * area * machMul;
        return surfaceVelocity.Normalized * (-magnitude);
    }

    // ── Aerodynamic attitude dynamics ────────────────────────────────────────

    /// <summary>
    /// Angular acceleration caused by the aerodynamic force acting behind the centre of
    /// mass, plus rate damping from the surrounding air. This turns the vehicle through a
    /// real torque/inertia relationship rather than applying a fixed "weathervane" rate.
    /// The returned vector is in world space (rad/s²).
    /// </summary>
    public static Vector3d ComputeAttitudeAngularAcceleration(
        double density,
        Vector3d surfaceVelocity,
        Vector3d longitudinalAxis,
        Vector3d angularVelocity,
        double vehicleLength,
        double vehicleDiameter,
        double transverseMomentOfInertia,
        double temperature)
    {
        double speed = surfaceVelocity.Magnitude;
        if (density <= 0.0 || speed < 1.0 || transverseMomentOfInertia <= 0.0)
            return Vector3d.Zero;

        var axis = longitudinalAxis.Normalized;
        var drag = ComputeReentryDrag(
            density, surfaceVelocity, axis, vehicleLength, vehicleDiameter, temperature);

        // A modest static margin. Long launch stacks place their aerodynamic centre farther
        // behind the CoM than the shorter Ship; limiting it avoids an unrealistically violent
        // snap at Max-Q while propellant shifts the actual inertia continuously.
        double cpOffset = System.Math.Clamp(vehicleLength * 0.08,
            vehicleDiameter * 0.35, vehicleDiameter * 1.35);
        var momentArm = axis * (-cpOffset);
        var torqueAcceleration = momentArm.Cross(drag) / transverseMomentOfInertia;

        // Air damps pitch/yaw rates, but does not directly erase roll about the nearly
        // axisymmetric hull. Scale smoothly with q and cap the decay rate for numerical
        // stability during dense-atmosphere descent.
        double q = ComputeDynamicPressure(density, speed);
        double dampingRate = System.Math.Min(1.25, 0.55 * q / 30_000.0);
        var rollRate = axis * angularVelocity.Dot(axis);
        var pitchYawRate = angularVelocity - rollRate;
        var dampingAcceleration = pitchYawRate * (-dampingRate);

        return torqueAcceleration + dampingAcceleration;
    }

    /// <summary>
    /// Approximate flap-control angular acceleration from aerodynamic hinge forces. The four
    /// flaps are represented as a combined control area and a lever arm about the CoM; q creates
    /// the force, so authority fades naturally in thin air and does not require engine thrust.
    /// The command uses semantic fields X=pitch, Y=yaw, Z=roll; these are mapped to the
    /// vehicle's physical local axes X=pitch, Y=roll, Z=yaw.
    /// </summary>
    public static Vector3d ComputeFlapControlAngularAcceleration(
        double density,
        Vector3d surfaceVelocity,
        Quaterniond orientation,
        Vector3d pitchYawRollCommand,
        double vehicleLength,
        double vehicleDiameter,
        double transverseMomentOfInertia)
    {
        if (density <= 0.0 || surfaceVelocity.Magnitude < 1.0
            || transverseMomentOfInertia <= 0.0 || pitchYawRollCommand.Magnitude < 1e-6)
            return Vector3d.Zero;

        double q = ComputeDynamicPressure(density, surfaceVelocity.Magnitude);
        // Four large surfaces: combined projected planform is approximately one-and-a-half
        // 9 m hull cross-sections, with fore/aft pairs acting far from the mass centre.
        double combinedFlapArea = 1.40 * vehicleDiameter * vehicleDiameter;
        double leverArm = 0.45 * vehicleLength;
        const double controlCoefficient = 1.00;
        double pitchYawAuthority = System.Math.Min(
            1.20,
            q * combinedFlapArea * leverArm * controlCoefficient / transverseMomentOfInertia);
        double rollAuthority = pitchYawAuthority * 0.55;

        var localAcceleration = new Vector3d(
            System.Math.Clamp(pitchYawRollCommand.X, -1.0, 1.0) * pitchYawAuthority,
            System.Math.Clamp(pitchYawRollCommand.Z, -1.0, 1.0) * rollAuthority,
            System.Math.Clamp(pitchYawRollCommand.Y, -1.0, 1.0) * pitchYawAuthority);
        return orientation.Rotate(localAcceleration);
    }

    /// <summary>
    /// Builds a full attitude for broadside entry: local +Y follows the commanded long
    /// axis while the tiled local -X belly faces the direction of travel through the air.
    /// </summary>
    public static Quaterniond ComputeBellyFirstOrientation(
        Vector3d longitudinalAxis, Vector3d velocityDirection)
    {
        var axis = longitudinalAxis.Normalized;
        var qAxis = ShortestArc(Vector3d.Up, axis);
        var currentBelly = qAxis.Rotate(-Vector3d.Right).Normalized;
        var desiredBelly = velocityDirection - axis * velocityDirection.Dot(axis);
        if (desiredBelly.Magnitude < 1e-6) return qAxis;
        desiredBelly = desiredBelly.Normalized;

        double sin = axis.Dot(currentBelly.Cross(desiredBelly));
        double cos = System.Math.Clamp(currentBelly.Dot(desiredBelly), -1.0, 1.0);
        var qRoll = Quaterniond.FromAxisAngle(axis, System.Math.Atan2(sin, cos));
        return (qRoll * qAxis).Normalize();
    }

    private static Quaterniond ShortestArc(Vector3d from, Vector3d to)
    {
        var f = from.Normalized;
        var t = to.Normalized;
        double dot = f.Dot(t);
        if (dot > 0.99999) return Quaterniond.Identity;
        if (dot < -0.99999)
        {
            Vector3d perpendicular = System.Math.Abs(f.X) < 0.9
                ? f.Cross(Vector3d.Right)
                : f.Cross(Vector3d.Up);
            return Quaterniond.FromAxisAngle(perpendicular.Normalized, System.Math.PI);
        }
        return Quaterniond.FromAxisAngle(f.Cross(t).Normalized,
            System.Math.Acos(System.Math.Clamp(dot, -1.0, 1.0)));
    }
}
