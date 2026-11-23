namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;

/// <summary>
/// The two chopstick arms as catch surfaces. Each arm is a horizontal rail along
/// <see cref="ArmAxisWorld"/>. When the arms close they stop against the hull, so each rail's
/// inner edge sits one hull radius either side of the vehicle axis, wherever that axis is
/// between the open arms. A catch pin rests on a rail when it stands proud of the hull over
/// that rail and lies within the catch zone along the arm. That is how the flown catch takes
/// up a sideways offset: the arms close onto the ship, the ship does not have to hit a point.
/// </summary>
public readonly record struct CatchArmRails(
    Vector3d ArmAxisWorld, double CatchZoneHalfLengthM, double OpenHalfGapM, double RailWidthM)
{
    /// <summary>Rails are usable when the arm axis is a real horizontal direction.</summary>
    public bool IsDefined => ArmAxisWorld.MagnitudeSquared > 1e-12;

    /// <summary>
    /// Smallest slack (m) of the conditions that put the pin at <paramref name="pinWorld"/> on a
    /// rail: inside the catch zone along the arm, axis within reach of the open arms, and the
    /// pin overlapping the rail across. Negative when the pin is off its rail.
    /// </summary>
    public double PinSlackM(Vector3d cradleWorld, Vector3d upWorld, Vector3d axisPointWorld,
        double hullRadiusM, double pinRadiusM, Vector3d pinWorld)
    {
        var (along, across) = Frame(upWorld);
        var d = pinWorld - cradleWorld;
        var axis = axisPointWorld - cradleWorld;
        double outward = System.Math.Abs(d.Dot(across) - axis.Dot(across));
        double alongSlack = CatchZoneHalfLengthM - System.Math.Abs(d.Dot(along));
        double axisAlongSlack = CatchZoneHalfLengthM - System.Math.Abs(axis.Dot(along));
        double reachSlack = OpenHalfGapM - hullRadiusM - System.Math.Abs(axis.Dot(across));
        double innerSlack = outward + pinRadiusM - hullRadiusM;
        double outerSlack = hullRadiusM + RailWidthM - (outward - pinRadiusM);
        return System.Math.Min(System.Math.Min(alongSlack, axisAlongSlack),
            System.Math.Min(reachSlack, System.Math.Min(innerSlack, outerSlack)));
    }

    /// <summary>Rail surface under a pin, or a point far below when the pin is off both rails.</summary>
    public SurfaceSample Sample(Vector3d cradleWorld, Vector3d upWorld, Vector3d cradleVelocityWorld,
        Vector3d axisPointWorld, double hullRadiusM, double pinRadiusM, Vector3d queryWorld)
    {
        var up = upWorld.Normalized;
        bool onRail = PinSlackM(cradleWorld, up, axisPointWorld, hullRadiusM, pinRadiusM, queryWorld) >= 0.0;
        var offset = queryWorld - cradleWorld;
        var point = onRail
            ? cradleWorld + offset - up * offset.Dot(up)
            : queryWorld - up * 1.0e6;
        return new SurfaceSample(point, up, cradleVelocityWorld);
    }

    private (Vector3d Along, Vector3d Across) Frame(Vector3d upWorld)
    {
        var up = upWorld.Normalized;
        var along = (ArmAxisWorld - up * ArmAxisWorld.Dot(up)).Normalized;
        return (along, up.Cross(along).Normalized);
    }
}
