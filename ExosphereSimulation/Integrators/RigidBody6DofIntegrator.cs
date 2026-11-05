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

        return Combine(state, dt, k1, k2, k3, k4);
    }

    /// <summary>
    /// RK4 with pure, time-dependent mass properties sampled at every candidate stage.
    /// The force callback receives that same mass snapshot. It must include the required
    /// open-system transport moments; varying I alone does not conserve angular momentum.
    /// Resource mutation and construction-datum/CoM transport remain outside this kernel.
    /// </summary>
    public static RigidBody6DofState Step(
        RigidBody6DofState state, double time, double dt,
        Func<double, RigidBodyMassProperties> sampleMassProperties,
        Func<RigidBody6DofState, double, RigidBodyMassProperties, RigidBodyForces> evaluateForces)
    {
        ArgumentNullException.ThrowIfNull(sampleMassProperties);
        ArgumentNullException.ThrowIfNull(evaluateForces);
        if (!double.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
        if (!double.IsFinite(dt) || dt <= 0 || !double.IsFinite(time + dt))
            throw new ArgumentOutOfRangeException(nameof(dt));
        var k1 = EvaluateVariableMass(state, time, sampleMassProperties, evaluateForces);
        var k2 = EvaluateVariableMass(Add(state, k1, dt * 0.5), time + dt * 0.5,
            sampleMassProperties, evaluateForces);
        var k3 = EvaluateVariableMass(Add(state, k2, dt * 0.5), time + dt * 0.5,
            sampleMassProperties, evaluateForces);
        var k4 = EvaluateVariableMass(Add(state, k3, dt), time + dt,
            sampleMassProperties, evaluateForces);
        return Combine(state, dt, k1, k2, k3, k4);
    }

    private static RigidBody6DofState Combine(RigidBody6DofState state, double dt,
        Derivative k1, Derivative k2, Derivative k3, Derivative k4)
    {
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
        return Evaluate(state, massProperties, forces);
    }

    private static Derivative EvaluateVariableMass(RigidBody6DofState state, double time,
        Func<double, RigidBodyMassProperties> sampleMassProperties,
        Func<RigidBody6DofState, double, RigidBodyMassProperties, RigidBodyForces> evaluateForces)
    {
        var mass = sampleMassProperties(time);
        if (!double.IsFinite(mass.Mass) || mass.Mass <= 0)
            throw new ArgumentException("Mass sampler returned a nonpositive or nonfinite mass.", nameof(sampleMassProperties));
        return Evaluate(state, mass, evaluateForces(state, time, mass));
    }

    private static Derivative Evaluate(RigidBody6DofState state,
        RigidBodyMassProperties massProperties, RigidBodyForces forces)
    {
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
