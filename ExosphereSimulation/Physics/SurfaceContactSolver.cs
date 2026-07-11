namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;

/// <summary>
/// Local surface plane sampled at a prospective contact point. <see cref="PointWorld"/>
/// lies on the surface, <see cref="NormalWorld"/> points out of the body and
/// <see cref="VelocityWorld"/> is the inertial velocity of that material point.
/// </summary>
public readonly record struct SurfaceSample(
    Vector3d PointWorld,
    Vector3d NormalWorld,
    Vector3d VelocityWorld)
{
    /// <summary>Samples the mean-radius sphere of a rotating celestial body.</summary>
    public static SurfaceSample FromSphere(CelestialBody body, Vector3d queryWorld)
    {
        var radial = queryWorld - body.Position;
        var normal = radial.Magnitude > 1e-9 ? radial.Normalized : Vector3d.Up;
        var point = body.Position + normal * body.Radius;
        var velocity = body.Velocity + body.GetSurfaceVelocity(point);
        return new SurfaceSample(point, normal, velocity);
    }
}

/// <summary>
/// SI contact parameters for one foot, skid or hull witness point. The point is declared
/// from the renderer/vehicle datum; the solver independently receives the world-space CoM,
/// so visual geometry and torque arms do not have to share an origin.
/// </summary>
public readonly record struct ContactPointDefinition(
    string Name,
    Vector3d LocalPositionFromDatum,
    double ContactRadiusM,
    double SpringStiffnessNPerM,
    double DampingNsPerM,
    double TangentialDampingNsPerM,
    double FrictionCoefficient,
    double MaxCompressionM,
    double MaxLoadN,
    bool Enabled = true);

/// <summary>
/// Rigid-body state needed by the pure contact solver. Linear velocity belongs to the CoM;
/// point velocity is therefore <c>vCOM + omega x rCOM</c>.
/// </summary>
public readonly record struct RigidBodyContactInput(
    Vector3d DatumPositionWorld,
    Vector3d CenterOfMassPositionWorld,
    Vector3d CenterOfMassVelocityWorld,
    Quaterniond Orientation,
    Vector3d AngularVelocityWorld);

/// <summary>Diagnostic result for one contact definition.</summary>
public readonly record struct ContactPointResult(
    string Name,
    Vector3d PointWorld,
    Vector3d LeverArmFromCenterOfMassWorld,
    double SignedGapM,
    double PenetrationM,
    double NormalVelocityMps,
    Vector3d NormalForceWorld,
    Vector3d FrictionForceWorld,
    Vector3d TorqueWorld,
    double TravelExcessM,
    bool IsOverTravel,
    bool IsOverloaded)
{
    public bool IsGeometricallyContacting => PenetrationM > 0.0;
    public double NormalLoadN => NormalForceWorld.Magnitude;
    public Vector3d TotalForceWorld => NormalForceWorld + FrictionForceWorld;
}

/// <summary>Net force/torque and per-point diagnostics for one solver evaluation.</summary>
public sealed class ContactWrench
{
    public Vector3d ForceWorld { get; }
    public Vector3d TorqueWorld { get; }
    public IReadOnlyList<ContactPointResult> Points { get; }
    public int ContactCount { get; }
    public bool HasOverTravel { get; }
    public bool HasOverload { get; }
    public double MaxTravelExcessM { get; }

    internal ContactWrench(
        Vector3d forceWorld,
        Vector3d torqueWorld,
        IReadOnlyList<ContactPointResult> points)
    {
        ForceWorld = forceWorld;
        TorqueWorld = torqueWorld;
        Points = points;
        ContactCount = points.Count(p => p.IsGeometricallyContacting);
        HasOverTravel = points.Any(p => p.IsOverTravel);
        HasOverload = points.Any(p => p.IsOverloaded);
        MaxTravelExcessM = points.Count > 0 ? points.Max(p => p.TravelExcessM) : 0.0;
    }
}

/// <summary>
/// Pure multipoint penalty-contact model. Normal support follows
/// <c>Fn=max(0, k*penetration-c*normalVelocity)</c>. Tangential force is viscous near
/// rest and capped by Coulomb friction, so it cannot exceed <c>mu*Fn</c> or create adhesion.
/// No state is mutated; runtime integration and failure policy remain the caller's concern.
/// </summary>
public static class SurfaceContactSolver
{
    public static ContactWrench Evaluate(
        in RigidBodyContactInput body,
        IEnumerable<ContactPointDefinition> definitions,
        Func<Vector3d, SurfaceSample> sampleSurface)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(sampleSurface);

        var results = new List<ContactPointResult>();
        var totalForce = Vector3d.Zero;
        var totalTorque = Vector3d.Zero;

        foreach (var definition in definitions)
        {
            Validate(definition);
            if (!definition.Enabled) continue;

            var pointWorld = body.DatumPositionWorld
                + body.Orientation.Rotate(definition.LocalPositionFromDatum);
            var leverWorld = pointWorld - body.CenterOfMassPositionWorld;
            var pointVelocity = body.CenterOfMassVelocityWorld
                + body.AngularVelocityWorld.Cross(leverWorld);

            var surface = sampleSurface(pointWorld);
            var normal = surface.NormalWorld.Normalized;
            if (normal.MagnitudeSquared < 0.5)
                throw new ArgumentException("Surface normal must be non-zero.", nameof(sampleSurface));

            double signedGap = (pointWorld - surface.PointWorld).Dot(normal)
                - definition.ContactRadiusM;
            double penetration = System.Math.Max(0.0, -signedGap);
            var relativeVelocity = pointVelocity - surface.VelocityWorld;
            double normalVelocity = relativeVelocity.Dot(normal);

            var normalForce = Vector3d.Zero;
            var frictionForce = Vector3d.Zero;
            if (penetration > 0.0)
            {
                double normalLoad = System.Math.Max(0.0,
                    definition.SpringStiffnessNPerM * penetration
                    - definition.DampingNsPerM * normalVelocity);
                normalForce = normal * normalLoad;

                var tangentialVelocity = relativeVelocity - normal * normalVelocity;
                double tangentSpeed = tangentialVelocity.Magnitude;
                if (normalLoad > 0.0 && tangentSpeed > 1e-12)
                {
                    double viscousLoad = definition.TangentialDampingNsPerM * tangentSpeed;
                    double coulombLimit = definition.FrictionCoefficient * normalLoad;
                    double frictionLoad = System.Math.Min(viscousLoad, coulombLimit);
                    frictionForce = tangentialVelocity * (-frictionLoad / tangentSpeed);
                }
            }

            var force = normalForce + frictionForce;
            var torque = leverWorld.Cross(force);
            double travelExcess = definition.MaxCompressionM > 0.0
                ? System.Math.Max(0.0, penetration - definition.MaxCompressionM)
                : 0.0;
            bool overTravel = travelExcess > 0.0;
            bool overloaded = definition.MaxLoadN > 0.0
                && normalForce.Magnitude > definition.MaxLoadN;

            results.Add(new ContactPointResult(
                definition.Name,
                pointWorld,
                leverWorld,
                signedGap,
                penetration,
                normalVelocity,
                normalForce,
                frictionForce,
                torque,
                travelExcess,
                overTravel,
                overloaded));
            totalForce += force;
            totalTorque += torque;
        }

        return new ContactWrench(totalForce, totalTorque, results);
    }

    public static ContactWrench EvaluateSphere(
        in RigidBodyContactInput vessel,
        IEnumerable<ContactPointDefinition> definitions,
        CelestialBody body) =>
        Evaluate(vessel, definitions, point => SurfaceSample.FromSphere(body, point));

    private static void Validate(in ContactPointDefinition definition)
    {
        if (definition.ContactRadiusM < 0.0)
            throw new ArgumentOutOfRangeException(nameof(definition), "Contact radius cannot be negative.");
        if (definition.SpringStiffnessNPerM < 0.0
            || definition.DampingNsPerM < 0.0
            || definition.TangentialDampingNsPerM < 0.0)
            throw new ArgumentOutOfRangeException(nameof(definition), "Contact coefficients cannot be negative.");
        if (definition.FrictionCoefficient < 0.0)
            throw new ArgumentOutOfRangeException(nameof(definition), "Friction cannot be negative.");
    }
}
