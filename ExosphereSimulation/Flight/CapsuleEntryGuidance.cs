namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;

/// <summary>
/// Lifting entry of a trimmed blunt capsule (Apollo-style). Aerodynamics holds the trim angle;
/// reaction control only rolls the lift vector about the airstream and damps pitch/yaw.
/// The vertical share of lift, cos(bank), rises when drag exceeds the reference (climb out of
/// the dense air) and falls while the capsule climbs (dig back in instead of skipping out).
/// Before the air bites, the trim axis is pointed into the airstream. Returns PitchYawRoll.
/// </summary>
public static class CapsuleEntryGuidance
{
    /// <summary>Reference drag deceleration (g) the bank modulation flies toward.</summary>
    public const double ReferenceDragG = 4.0;

    /// <summary>Standard gravity used to express drag in g (m/s²).</summary>
    public const double StandardGravity = 9.80665;

    /// <summary>Below this drag (g) the capsule only holds its trim attitude.</summary>
    public const double AerodynamicOnsetG = 0.05;

    public static Vector3d ComputeCommand(Vessel capsule, CelestialBody body)
    {
        ArgumentNullException.ThrowIfNull(capsule);
        ArgumentNullException.ThrowIfNull(body);
        var air = capsule.GetSurfaceVelocity(body);
        if (air.MagnitudeSquared < 1.0) return Vector3d.Zero;
        var flow = air.Normalized;
        double authority = capsule.GetPitchYawAngularAuthority(body);
        double damping = AttitudeGuidance.DampingGainFor(2.3, authority);
        var trimAxis = capsule.Parts.AerodynamicReferenceAxisLocal;

        double dragG = capsule.ComputeDrag(body).Magnitude / System.Math.Max(1.0, capsule.TotalMass)
            / StandardGravity;
        if (dragG < AerodynamicOnsetG)
            return AttitudeGuidance.ComputeAxisPointingCommand(capsule.Orientation, trimAxis, -flow,
                capsule.AngularVelocity, proportionalGain: 2.3, dampingGain: damping);

        var up = (capsule.Position - body.Position).Normalized;
        double climbRate = air.Dot(up);
        double verticalLift = System.Math.Clamp(
            (dragG - ReferenceDragG) / ReferenceDragG - climbRate / 100.0, -1.0, 1.0);

        // Current lift direction: the body axis component normal to the airstream.
        var axis = capsule.Orientation.Rotate(Vector3d.Up);
        var lift = axis - flow * axis.Dot(flow);
        var upNormal = up - flow * up.Dot(flow);
        if (lift.MagnitudeSquared < 1e-9 || upNormal.MagnitudeSquared < 1e-9) return Vector3d.Zero;
        lift = lift.Normalized;
        upNormal = upNormal.Normalized;
        var side = flow.Cross(upNormal);
        // Bank toward whichever side the lift already leans, so modulation never forces a reversal.
        double sideSign = lift.Dot(side) >= 0.0 ? 1.0 : -1.0;
        var desired = upNormal * verticalLift
            + side * (sideSign * System.Math.Sqrt(1.0 - verticalLift * verticalLift));

        double bankError = System.Math.Atan2(lift.Cross(desired).Dot(axis), lift.Dot(desired));
        var localRate = capsule.Orientation.Inverse().Rotate(capsule.AngularVelocity);
        double roll = System.Math.Clamp(2.3 * bankError - damping * localRate.Y, -1.0, 1.0);
        // Rate damping only on pitch/yaw: the aerodynamic moment owns the trim angle.
        return new Vector3d(
            System.Math.Clamp(-damping * localRate.X, -1.0, 1.0),
            System.Math.Clamp(-damping * localRate.Z, -1.0, 1.0),
            roll);
    }
}
