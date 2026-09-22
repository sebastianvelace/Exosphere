namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;

/// <summary>
/// Complete rigid-body state used by the isolated 6-DoF integrator.
/// Position and velocity are inertial/world-frame values. Angular velocity is
/// body-frame so Euler's rigid-body equation can use the body inertia tensor.
/// </summary>
public readonly struct RigidBody6DofState : IEquatable<RigidBody6DofState>
{
    public Vector3d Position { get; }
    public Vector3d Velocity { get; }
    public Quaterniond Orientation { get; }
    public Vector3d AngularVelocityBody { get; }

    public RigidBody6DofState(
        Vector3d position,
        Vector3d velocity,
        Quaterniond orientation,
        Vector3d angularVelocityBody)
    {
        Position = position;
        Velocity = velocity;
        Orientation = orientation.Normalize();
        AngularVelocityBody = angularVelocityBody;
    }

    public Vector3d AngularVelocityWorld => Orientation.Rotate(AngularVelocityBody);

    public bool Equals(RigidBody6DofState other) =>
        Position == other.Position
        && Velocity == other.Velocity
        && Orientation == other.Orientation
        && AngularVelocityBody == other.AngularVelocityBody;

    public override bool Equals(object? obj) => obj is RigidBody6DofState other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(
        Position, Velocity, Orientation, AngularVelocityBody);
}

/// <summary>
/// Constant mass properties during one integration step.
/// The tensor is expressed in the body's principal/body frame.
/// </summary>
public readonly struct RigidBodyMassProperties
{
    public double Mass { get; }
    public Vector3d CenterOfMassBody { get; }
    public Matrix3x3d InertiaBody { get; }
    public Matrix3x3d InverseInertiaBody { get; }

    public RigidBodyMassProperties(double mass, Matrix3x3d inertiaBody)
        : this(mass, Vector3d.Zero, inertiaBody)
    {
    }

    public RigidBodyMassProperties(
        double mass,
        Vector3d centerOfMassBody,
        Matrix3x3d inertiaBody)
    {
        if (!double.IsFinite(mass) || mass <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(mass), "Mass must be finite and positive.");

        Mass = mass;
        CenterOfMassBody = centerOfMassBody;
        InertiaBody = inertiaBody;
        InverseInertiaBody = inertiaBody.Inverse();
    }
}

/// <summary>
/// Net external load at one RK4 stage. Force is world-frame; torque is body-frame.
/// </summary>
public readonly struct RigidBodyForces
{
    public Vector3d ForceWorld { get; }
    public Vector3d TorqueBody { get; }

    public RigidBodyForces(Vector3d forceWorld, Vector3d torqueBody)
    {
        ForceWorld = forceWorld;
        TorqueBody = torqueBody;
    }

    public static RigidBodyForces Zero => new(Vector3d.Zero, Vector3d.Zero);
}
