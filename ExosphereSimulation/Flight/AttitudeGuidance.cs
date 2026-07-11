namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;

/// <summary>
/// Pure attitude-error controller. It never mutates orientation: it converts a desired
/// quaternion into semantic pitch/yaw/roll actuator commands in [-1,1].
/// </summary>
public static class AttitudeGuidance
{
    /// <summary>
    /// Points one body-fixed axis at a world-space target without commanding roll about that
    /// axis. This is the correct controller for engine-axis alignment: roll is unobservable to
    /// thrust and must not contaminate the pitch/yaw error.
    /// </summary>
    public static Vector3d ComputeAxisPointingCommand(
        Quaterniond current,
        Vector3d localAxis,
        Vector3d targetWorld,
        Vector3d angularVelocityWorld,
        double proportionalGain = 2.4,
        double dampingGain = 1.1)
    {
        var bodyAxis = localAxis.Normalized;
        var currentWorld = current.Rotate(bodyAxis).Normalized;
        var target = targetWorld.Normalized;
        var cross = currentWorld.Cross(target);
        double sin = cross.Magnitude;
        double dot = System.Math.Clamp(currentWorld.Dot(target), -1.0, 1.0);

        Vector3d errorWorld;
        if (sin > 1e-8)
            errorWorld = cross / sin * System.Math.Atan2(sin, dot);
        else if (dot < 0.0)
        {
            var reference = System.Math.Abs(currentWorld.Dot(Vector3d.Up)) < 0.9
                ? Vector3d.Up : Vector3d.Right;
            errorWorld = currentWorld.Cross(reference).Normalized * System.Math.PI;
        }
        else
            errorWorld = Vector3d.Zero;

        var errorLocal = current.Inverse().Rotate(errorWorld);
        var rateLocal = current.Inverse().Rotate(angularVelocityWorld);
        rateLocal -= bodyAxis * rateLocal.Dot(bodyAxis); // roll is deliberately unconstrained
        var torqueLocal = errorLocal * proportionalGain - rateLocal * dampingGain;

        return new Vector3d(
            System.Math.Clamp(torqueLocal.X, -1.0, 1.0),
            System.Math.Clamp(torqueLocal.Z, -1.0, 1.0),
            0.0);
    }

    public static Vector3d ComputeCommand(
        Quaterniond current,
        Quaterniond desired,
        Vector3d angularVelocityWorld,
        double proportionalGain = 2.4,
        double dampingGain = 1.1,
        bool allowRoll = true)
    {
        var q = (desired * current.Inverse()).Normalize();
        if (q.W < 0.0)
            q = new Quaterniond(-q.W, -q.X, -q.Y, -q.Z);

        double w = System.Math.Clamp(q.W, -1.0, 1.0);
        double angle = 2.0 * System.Math.Acos(w);
        double sinHalf = System.Math.Sqrt(System.Math.Max(0.0, 1.0 - w * w));

        Vector3d errorLocal = Vector3d.Zero;
        if (angle > 1e-6 && sinHalf > 1e-8)
        {
            var axisWorld = new Vector3d(q.X, q.Y, q.Z) / sinHalf;
            errorLocal = current.Inverse().Rotate(axisWorld) * angle;
        }

        Vector3d rateLocal = current.Inverse().Rotate(angularVelocityWorld);
        Vector3d torqueLocal = errorLocal * proportionalGain - rateLocal * dampingGain;

        // Vehicle long axis is local +Y: local X=pitch, local Z=yaw, local Y=roll.
        return new Vector3d(
            System.Math.Clamp(torqueLocal.X, -1.0, 1.0),
            System.Math.Clamp(torqueLocal.Z, -1.0, 1.0),
            allowRoll ? System.Math.Clamp(torqueLocal.Y, -1.0, 1.0) : 0.0);
    }

    public static double ErrorAngleRadians(Quaterniond current, Quaterniond desired)
    {
        var q = (desired * current.Inverse()).Normalize();
        return 2.0 * System.Math.Acos(System.Math.Clamp(System.Math.Abs(q.W), 0.0, 1.0));
    }
}
