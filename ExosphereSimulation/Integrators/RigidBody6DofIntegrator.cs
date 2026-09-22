namespace Exosphere.Simulation.Integrators;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;

/// <summary>
/// Allocation-free fourth-order integrator for a coupled rigid body.
/// The evaluator must be pure: it may inspect the candidate state, but must not
/// mutate engines, propellant, vessel state, or the simulation clock.
/// </summary>
public static class RigidBody6DofIntegrator
{
    public static RigidBody6DofState Step(
        RigidBody6DofState state,
        double time,
        double dt,
        RigidBodyMassProperties massProperties,
        Func<RigidBody6DofState, double, RigidBodyForces> evaluateForces)
    {
        ArgumentNullException.ThrowIfNull(evaluateForces);
        if (!double.IsFinite(time))
            throw new ArgumentOutOfRangeException(nameof(time), "Time must be finite.");
        if (!double.IsFinite(dt) || dt <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(dt), "Step must be finite and positive.");

        Derivative k1 = Evaluate(state, time, massProperties, evaluateForces);
        RigidBody6DofState s2 = Add(state, k1, dt * 0.5);
        Derivative k2 = Evaluate(s2, time + dt * 0.5, massProperties, evaluateForces);
        RigidBody6DofState s3 = Add(state, k2, dt * 0.5);
        Derivative k3 = Evaluate(s3, time + dt * 0.5, massProperties, evaluateForces);
        RigidBody6DofState s4 = Add(state, k3, dt);
        Derivative k4 = Evaluate(s4, time + dt, massProperties, evaluateForces);

        double dtOver6 = dt / 6.0;
        return new RigidBody6DofState(
            state.Position + (k1.PositionRate + 2.0 * k2.PositionRate
                + 2.0 * k3.PositionRate + k4.PositionRate) * dtOver6,
            state.Velocity + (k1.VelocityRate + 2.0 * k2.VelocityRate
                + 2.0 * k3.VelocityRate + k4.VelocityRate) * dtOver6,
            AddQuaternion(state.Orientation,
                k1.OrientationRate, k2.OrientationRate,
                k3.OrientationRate, k4.OrientationRate, dtOver6),
            state.AngularVelocityBody + (k1.AngularAccelerationBody
                + 2.0 * k2.AngularAccelerationBody
                + 2.0 * k3.AngularAccelerationBody
                + k4.AngularAccelerationBody) * dtOver6);
    }

    private static Derivative Evaluate(
        RigidBody6DofState state,
        double time,
        RigidBodyMassProperties massProperties,
        Func<RigidBody6DofState, double, RigidBodyForces> evaluateForces)
    {
        RigidBodyForces forces = evaluateForces(state, time);
        Vector3d angularMomentumBody = massProperties.InertiaBody.Multiply(
            state.AngularVelocityBody);
        Vector3d gyroscopicTerm = state.AngularVelocityBody.Cross(angularMomentumBody);
        Vector3d angularAccelerationBody = massProperties.InverseInertiaBody.Multiply(
            forces.TorqueBody - gyroscopicTerm);

        Quaterniond omegaQuaternion = new(
            0.0,
            state.AngularVelocityBody.X,
            state.AngularVelocityBody.Y,
            state.AngularVelocityBody.Z);
        Quaterniond orientationRate = state.Orientation * omegaQuaternion;
        orientationRate = new Quaterniond(
            orientationRate.W * 0.5,
            orientationRate.X * 0.5,
            orientationRate.Y * 0.5,
            orientationRate.Z * 0.5);

        return new Derivative(
            state.Velocity,
            forces.ForceWorld / massProperties.Mass,
            orientationRate,
            angularAccelerationBody);
    }

    private static RigidBody6DofState Add(
        RigidBody6DofState state,
        Derivative derivative,
        double scale)
    {
        return new RigidBody6DofState(
            state.Position + derivative.PositionRate * scale,
            state.Velocity + derivative.VelocityRate * scale,
            AddQuaternion(state.Orientation, ScaleQuaternion(derivative.OrientationRate, scale), 1.0),
            state.AngularVelocityBody + derivative.AngularAccelerationBody * scale);
    }

    private static Quaterniond AddQuaternion(
        Quaterniond state,
        Quaterniond k1,
        Quaterniond k2,
        Quaterniond k3,
        Quaterniond k4,
        double scale)
    {
        return new Quaterniond(
            state.W + (k1.W + 2.0 * k2.W + 2.0 * k3.W + k4.W) * scale,
            state.X + (k1.X + 2.0 * k2.X + 2.0 * k3.X + k4.X) * scale,
            state.Y + (k1.Y + 2.0 * k2.Y + 2.0 * k3.Y + k4.Y) * scale,
            state.Z + (k1.Z + 2.0 * k2.Z + 2.0 * k3.Z + k4.Z) * scale).Normalize();
    }

    private static Quaterniond AddQuaternion(
        Quaterniond state,
        Quaterniond derivative,
        double scale)
    {
        return new Quaterniond(
            state.W + derivative.W * scale,
            state.X + derivative.X * scale,
            state.Y + derivative.Y * scale,
            state.Z + derivative.Z * scale).Normalize();
    }

    private static Quaterniond ScaleQuaternion(Quaterniond value, double scale) => new(
        value.W * scale,
        value.X * scale,
        value.Y * scale,
        value.Z * scale);

    private readonly struct Derivative
    {
        public Vector3d PositionRate { get; }
        public Vector3d VelocityRate { get; }
        public Quaterniond OrientationRate { get; }
        public Vector3d AngularAccelerationBody { get; }

        public Derivative(
            Vector3d positionRate,
            Vector3d velocityRate,
            Quaterniond orientationRate,
            Vector3d angularAccelerationBody)
        {
            PositionRate = positionRate;
            VelocityRate = velocityRate;
            OrientationRate = orientationRate;
            AngularAccelerationBody = angularAccelerationBody;
        }
    }
}
