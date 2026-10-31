namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;
using Xunit;

public sealed class CompositeMassPropertiesTests
{
    // Uniform solid cylinder about its centre, axis along body +Y.
    private static RigidBodyMassProperties Cylinder(double mass, double radius, double length, double centreY) =>
        new(mass, new Vector3d(0, centreY, 0), Matrix3x3d.Diagonal(
            mass * (3 * radius * radius + length * length) / 12.0,
            0.5 * mass * radius * radius,
            mass * (3 * radius * radius + length * length) / 12.0));

    [Fact]
    public void TwoCylindersEndToEndEqualOneCylinderOfTwiceTheLength()
    {
        // Two identical 1 t cylinders (r = 1 m, L = 4 m) stacked nose-to-tail along +Y must
        // equal one 2 t cylinder of length 8 m about its centre (exact, parallel-axis theorem).
        var lower = Cylinder(1_000, 1, 4, centreY: 2);
        var upperOwn = Cylinder(1_000, 1, 4, centreY: 2);
        var composite = CompositeMassProperties.Combine(lower,
            [new RigidlyAttachedMember(upperOwn, new Vector3d(0, 4, 0), Quaterniond.Identity)]);
        var whole = Cylinder(2_000, 1, 8, centreY: 4);

        Assert.Equal(2_000, composite.Mass, 9);
        Assert.Equal(4.0, composite.CenterOfMassBody.Y, 9);
        Assert.Equal(whole.InertiaBody.M11, composite.InertiaBody.M11, 6);
        Assert.Equal(whole.InertiaBody.M22, composite.InertiaBody.M22, 6);
        Assert.Equal(whole.InertiaBody.M33, composite.InertiaBody.M33, 6);
        Assert.Equal(0, composite.InertiaBody.M12, 9);
    }

    [Fact]
    public void MemberTensorIsRotatedIntoThePrimaryFrame()
    {
        // A slender rod docked broadside (rotated 90° about Z) through the primary's centre:
        // its long-axis inertia must appear on the primary's X axis, not on Y.
        var primary = new RigidBodyMassProperties(1e-9, Vector3d.Zero, Matrix3x3d.Diagonal(1e-3, 1e-3, 1e-3));
        var rod = Cylinder(600, 0.01, 10, centreY: 0);
        var quarterTurn = Quaterniond.FromAxisAngle(new Vector3d(0, 0, 1), System.Math.PI / 2);
        var composite = CompositeMassProperties.Combine(primary,
            [new RigidlyAttachedMember(rod, Vector3d.Zero, quarterTurn)]);

        Assert.Equal(rod.InertiaBody.M22, composite.InertiaBody.M11, 2); // axial now along X
        Assert.Equal(rod.InertiaBody.M11, composite.InertiaBody.M22, 2); // transverse now along Y
        Assert.Equal(rod.InertiaBody.M33, composite.InertiaBody.M33, 2);
        Assert.Equal(0, composite.InertiaBody.M12, 6);
    }

    [Fact]
    public void OffsetMemberAddsProductsOfInertia()
    {
        // Point-like member displaced diagonally in X and Y produces I_xy = −m·dx·dy about the
        // combined centre (both bodies equal mass, so each sits at ±d/2).
        var tiny = Matrix3x3d.Diagonal(1e-3, 1e-3, 1e-3);
        var a = new RigidBodyMassProperties(100, Vector3d.Zero, tiny);
        var b = new RigidBodyMassProperties(100, Vector3d.Zero, tiny);
        var composite = CompositeMassProperties.Combine(a,
            [new RigidlyAttachedMember(b, new Vector3d(2, 4, 0), Quaterniond.Identity)]);

        Assert.Equal(new Vector3d(1, 2, 0).X, composite.CenterOfMassBody.X, 9);
        Assert.Equal(new Vector3d(1, 2, 0).Y, composite.CenterOfMassBody.Y, 9);
        // Σ −m·dx·dy with (dx,dy) = (∓1, ∓2) for each 100 kg body: −2·100·2 = −400.
        Assert.Equal(-400, composite.InertiaBody.M12, 6);
        Assert.Equal(composite.InertiaBody.M12, composite.InertiaBody.M21, 9);
    }
}
