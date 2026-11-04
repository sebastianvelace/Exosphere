namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;

/// <summary>
/// A member rigidly attached to a primary body: its own mass properties (about its own
/// datum frame), plus the pose of its datum in the primary's body frame.
/// </summary>
public readonly record struct RigidlyAttachedMember(
    RigidBodyMassProperties MassProperties,
    Vector3d DatumPositionPrimaryLocal,
    Quaterniond OrientationPrimaryLocal);

/// <summary>
/// Mass properties of several rigidly joined bodies (a docked stack) expressed in the
/// primary's body frame: total mass, combined centre of mass, and the inertia tensor about
/// that centre, each member's tensor rotated into the primary frame (R·I·Rᵀ) and shifted
/// with the parallel-axis theorem I + m((d·d)E − d⊗d).
/// </summary>
public static class CompositeMassProperties
{
    public static RigidBodyMassProperties Combine(
        in RigidBodyMassProperties primary, IReadOnlyList<RigidlyAttachedMember> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (members.Count == 0) return primary;

        double totalMass = primary.Mass;
        Vector3d weighted = primary.CenterOfMassBody * primary.Mass;
        foreach (var member in members)
        {
            totalMass += member.MassProperties.Mass;
            weighted += MemberCenter(member) * member.MassProperties.Mass;
        }
        var center = weighted / totalMass;

        var inertia = Shift(primary.InertiaBody, primary.Mass, primary.CenterOfMassBody - center);
        foreach (var member in members)
        {
            var rotated = Rotate(member.MassProperties.InertiaBody, member.OrientationPrimaryLocal);
            inertia = Add(inertia, Shift(rotated, member.MassProperties.Mass, MemberCenter(member) - center));
        }
        return new RigidBodyMassProperties(totalMass, center, inertia);
    }

    private static Vector3d MemberCenter(in RigidlyAttachedMember member) =>
        member.DatumPositionPrimaryLocal
        + member.OrientationPrimaryLocal.Rotate(member.MassProperties.CenterOfMassBody);

    /// <summary>R·I·Rᵀ for the rotation that maps member-frame vectors to the primary frame.</summary>
    private static Matrix3x3d Rotate(in Matrix3x3d inertia, Quaterniond rotation)
    {
        var c1 = rotation.Rotate(Vector3d.Right);
        var c2 = rotation.Rotate(Vector3d.Up);
        var c3 = rotation.Rotate(new Vector3d(0, 0, 1));
        // Columns of R are the rotated basis vectors; (R I Rᵀ)ij = Σ R_ik I_kl R_jl.
        double[,] r = { { c1.X, c2.X, c3.X }, { c1.Y, c2.Y, c3.Y }, { c1.Z, c2.Z, c3.Z } };
        double[,] m = { { inertia.M11, inertia.M12, inertia.M13 },
                        { inertia.M21, inertia.M22, inertia.M23 },
                        { inertia.M31, inertia.M32, inertia.M33 } };
        var o = new double[3, 3];
        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
        {
            double sum = 0.0;
            for (int k = 0; k < 3; k++)
            for (int l = 0; l < 3; l++)
                sum += r[i, k] * m[k, l] * r[j, l];
            o[i, j] = sum;
        }
        return new Matrix3x3d(o[0, 0], o[0, 1], o[0, 2], o[1, 0], o[1, 1], o[1, 2], o[2, 0], o[2, 1], o[2, 2]);
    }

    private static Matrix3x3d Shift(in Matrix3x3d inertia, double mass, Vector3d d)
    {
        double dd = d.Dot(d);
        return new Matrix3x3d(
            inertia.M11 + mass * (dd - d.X * d.X), inertia.M12 - mass * d.X * d.Y, inertia.M13 - mass * d.X * d.Z,
            inertia.M21 - mass * d.Y * d.X, inertia.M22 + mass * (dd - d.Y * d.Y), inertia.M23 - mass * d.Y * d.Z,
            inertia.M31 - mass * d.Z * d.X, inertia.M32 - mass * d.Z * d.Y, inertia.M33 + mass * (dd - d.Z * d.Z));
    }

    private static Matrix3x3d Add(in Matrix3x3d a, in Matrix3x3d b) => new(
        a.M11 + b.M11, a.M12 + b.M12, a.M13 + b.M13,
        a.M21 + b.M21, a.M22 + b.M22, a.M23 + b.M23,
        a.M31 + b.M31, a.M32 + b.M32, a.M33 + b.M33);
}
