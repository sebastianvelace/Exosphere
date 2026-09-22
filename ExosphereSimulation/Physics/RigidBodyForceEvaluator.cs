namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;

/// <summary>
/// External inputs that are already known for one force-evaluation stage.
/// Contact solvers and the moving reference frame can provide these without making the
/// rigid-body evaluator responsible for their lifecycle or failure policy.
/// </summary>
public readonly struct RigidBodyForceContext
{
    public IReadOnlyList<CelestialBody> Bodies { get; }
    public CelestialBody? ReferenceBody { get; }
    public Vector3d ExternalForceWorld { get; }
    public Vector3d ExternalTorqueWorld { get; }

    public RigidBodyForceContext(
        IReadOnlyList<CelestialBody> bodies,
        CelestialBody? referenceBody = null,
        Vector3d externalForceWorld = default,
        Vector3d externalTorqueWorld = default)
    {
        Bodies = bodies ?? throw new ArgumentNullException(nameof(bodies));
        ReferenceBody = referenceBody;
        ExternalForceWorld = externalForceWorld;
        ExternalTorqueWorld = externalTorqueWorld;
    }
}

/// <summary>
/// Pure force/torque adapter for one candidate 6-DoF state.
/// It reads vessel and part state but does not advance engines, consume resources, update
/// thermal state, change controls, or write contact state. The returned force is world-frame;
/// the returned torque is body-frame for <see cref="RigidBody6DofIntegrator"/>.
/// </summary>
public static class RigidBodyForceEvaluator
{
    public static RigidBodyForces Evaluate(
        Vessel vessel,
        in RigidBody6DofState state,
        in RigidBodyForceContext context)
    {
        ArgumentNullException.ThrowIfNull(vessel);

        var massProperties = vessel.Parts.GetMassProperties();
        var forceWorld = vessel.ComputeGravityAt(state.Position, context.Bodies)
            * massProperties.Mass;

        double pressure = context.ReferenceBody?.Atmosphere?.GetPressure(
            context.ReferenceBody.GetAltitude(state.Position)) ?? 0.0;
        forceWorld += state.Orientation.Rotate(
            vessel.Parts.GetTotalThrust(pressure));

        if (context.ReferenceBody != null)
        {
            forceWorld += vessel.ComputeDragAt(
                state.Position,
                state.Velocity,
                state.Orientation,
                context.ReferenceBody);
        }

        forceWorld += context.ExternalForceWorld;

        var torqueBody = vessel.Parts.GetTotalTorque(pressure);
        torqueBody += state.Orientation.Inverse().Rotate(context.ExternalTorqueWorld);
        torqueBody += ComputeAerodynamicTorqueBody(vessel, state, context.ReferenceBody, massProperties);

        return new RigidBodyForces(forceWorld, torqueBody);
    }

    private static Vector3d ComputeAerodynamicTorqueBody(
        Vessel vessel,
        in RigidBody6DofState state,
        CelestialBody? body,
        RigidBodyMassProperties massProperties)
    {
        if (body?.Atmosphere == null) return Vector3d.Zero;

        double altitude = body.GetAltitude(state.Position);
        double density = body.Atmosphere.GetDensity(altitude);
        var surfaceVelocity = state.Velocity
            - body.Velocity
            - body.GetSurfaceVelocity(state.Position);
        double speed = surfaceVelocity.Magnitude;
        if (density <= 0.0 || speed <= 1.0) return Vector3d.Zero;

        double temperature = System.Math.Max(1.0, body.Atmosphere.GetTemperature(altitude));
        double? aerodynamicCenterOffset = null;
        foreach (var part in vessel.Parts.PartList)
        {
            if (part.Definition.AerodynamicCenterOffsetYM.HasValue)
            {
                aerodynamicCenterOffset = part.Definition.AerodynamicCenterOffsetYM;
                break;
            }
        }

        var angularAccelerationWorld = AerodynamicsModel.ComputeAttitudeAngularAcceleration(
            density,
            surfaceVelocity,
            state.Orientation.Rotate(Vector3d.Up),
            state.AngularVelocityWorld,
            vessel.VehicleLength,
            vessel.MaximumDiameter,
            vessel.Parts.TransverseMomentOfInertia,
            temperature,
            aerodynamicCenterOffset);
        var angularAccelerationBody = state.Orientation.Inverse().Rotate(angularAccelerationWorld);
        return massProperties.InertiaBody.Multiply(angularAccelerationBody);
    }
}
