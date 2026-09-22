namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;

/// <summary>
/// COM-normalized snapshot used to compare two rigid-body integration paths.
/// </summary>
public readonly record struct RigidBodyStateSnapshot(
    Vector3d CenterOfMassPositionWorld,
    Vector3d CenterOfMassVelocityWorld,
    Quaterniond Orientation,
    Vector3d AngularVelocityWorld)
{
    public static RigidBodyStateSnapshot FromVessel(Vessel vessel) => new(
        vessel.CenterOfMass,
        vessel.Velocity
            + vessel.AngularVelocity.Cross(vessel.Orientation.Rotate(vessel.Parts.CenterOfMass)),
        vessel.Orientation,
        vessel.AngularVelocity);
}

/// <summary>Maximum acceptable error for a parity comparison.</summary>
public readonly record struct RigidBodyParityTolerance(
    double PositionMeters,
    double VelocityMetersPerSecond,
    double AttitudeRadians,
    double AngularVelocityRadiansPerSecond);

/// <summary>Measured state errors between two rigid-body integration paths.</summary>
public readonly record struct RigidBodyParityResult(
    double PositionErrorMeters,
    double VelocityErrorMetersPerSecond,
    double AttitudeErrorRadians,
    double AngularVelocityErrorRadiansPerSecond,
    bool IsWithinTolerance)
{
    public bool IsFinite =>
        double.IsFinite(PositionErrorMeters)
        && double.IsFinite(VelocityErrorMetersPerSecond)
        && double.IsFinite(AttitudeErrorRadians)
        && double.IsFinite(AngularVelocityErrorRadiansPerSecond);
}

public static class RigidBodyParityComparer
{
    public static RigidBodyParityResult Compare(
        in RigidBodyStateSnapshot baseline,
        in RigidBodyStateSnapshot candidate,
        in RigidBodyParityTolerance tolerance)
    {
        double positionError =
            (candidate.CenterOfMassPositionWorld - baseline.CenterOfMassPositionWorld).Magnitude;
        double velocityError =
            (candidate.CenterOfMassVelocityWorld - baseline.CenterOfMassVelocityWorld).Magnitude;
        double attitudeError = QuaternionDistanceRadians(
            baseline.Orientation, candidate.Orientation);
        double angularVelocityError =
            (candidate.AngularVelocityWorld - baseline.AngularVelocityWorld).Magnitude;

        bool withinTolerance =
            positionError <= tolerance.PositionMeters
            && velocityError <= tolerance.VelocityMetersPerSecond
            && attitudeError <= tolerance.AttitudeRadians
            && angularVelocityError <= tolerance.AngularVelocityRadiansPerSecond;

        return new(
            positionError,
            velocityError,
            attitudeError,
            angularVelocityError,
            withinTolerance);
    }

    private static double QuaternionDistanceRadians(
        Quaterniond baseline,
        Quaterniond candidate)
    {
        Quaterniond relative = (baseline.Inverse() * candidate).Normalize();
        double shortestW = System.Math.Abs(relative.W);
        shortestW = System.Math.Clamp(shortestW, 0.0, 1.0);
        return 2.0 * System.Math.Acos(shortestW);
    }
}
