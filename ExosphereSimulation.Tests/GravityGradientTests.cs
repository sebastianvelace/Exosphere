namespace Exosphere.Simulation.Tests;

using Exosphere.Simulation.Integrators;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;

public sealed class GravityGradientTests
{
    [Fact]
    public void SphericalInertiaAndPrincipalAxisAlignmentHaveNoTidalMoment()
    {
        Assert.Equal(Vector3d.Zero, GravityGradient.ComputeTorqueBody(100,
            new Vector3d(3, 4, 0), Matrix3x3d.Diagonal(2, 2, 2)));
        Assert.Equal(Vector3d.Zero, GravityGradient.ComputeTorqueBody(100,
            new Vector3d(0, 5, 0), Matrix3x3d.Diagonal(2, 3, 5)));
    }

    [Fact]
    public void TiltedDumbbellMatchesDirectGravityOnItsTwoMasses()
    {
        const double mu = 100_000, mass = 2, halfLength = 0.1;
        var com = new Vector3d(800, 600, 0);
        var orientation = Quaterniond.FromEuler(17, -23, 31);
        var offset = orientation.Rotate(Vector3d.Right * halfLength);
        Vector3d Force(Vector3d p) => p * (-mu * mass / System.Math.Pow(p.Magnitude, 3));
        var direct = offset.Cross(Force(com + offset)) + (-offset).Cross(Force(com - offset));
        var inertia = Matrix3x3d.Diagonal(0, 2 * mass * halfLength * halfLength,
            2 * mass * halfLength * halfLength);
        var predicted = orientation.Rotate(GravityGradient.ComputeTorqueBody(mu,
            orientation.Inverse().Rotate(com), inertia));
        Assert.True(direct.Magnitude > 0);
        Assert.InRange((predicted - direct).Magnitude / direct.Magnitude, 0, 1e-6);
    }

    [Fact]
    public void TidalMomentHasTheRestoringSignAndInverseCubeDistanceScaling()
    {
        var inertia = Matrix3x3d.Diagonal(2, 10, 10);
        var radial = new Vector3d(1, 1, 0).Normalized * 100;
        var torque = GravityGradient.ComputeTorqueBody(1_000, radial, inertia);
        Assert.Equal(0.012, torque.Z, 14); // 3 mu/R^3 (Iy-Ix) sin(45) cos(45).
        var farther = GravityGradient.ComputeTorqueBody(1_000, radial * 2, inertia);
        Assert.InRange((farther * 8 - torque).Magnitude, 0, 1e-15);
    }

    [Fact]
    [Trait("FidelityGate", "F4")]
    [Trait("GateKind", "analytic")]
    public void CircularOrbitDumbbellLibratesAtTheAnalyticSmallAngleFrequency()
    {
        const double meanMotion = 0.02, radius = 100, angle0 = 0.001, step = 0.01;
        double mu = meanMotion * meanMotion * radius * radius * radius;
        var mass = new RigidBodyMassProperties(1, Matrix3x3d.Diagonal(2, 10, 10));
        double frequency = meanMotion * System.Math.Sqrt(3.0 * (10 - 2) / 10);
        double end = System.Math.PI / (2 * frequency);
        // Vector3d.Forward is -Z; this prescribed orbit rotates about +Z.
        var orbitNormal = new Vector3d(0, 0, 1);
        var state = new RigidBody6DofState(Vector3d.Zero, Vector3d.Zero,
            Quaterniond.FromAxisAngle(orbitNormal, angle0), orbitNormal * meanMotion);
        double time = 0;
        while (time < end)
        {
            double dt = System.Math.Min(step, end - time);
            state = RigidBody6DofIntegrator.Step(state, time, dt, mass, (candidate, t) =>
            {
                var radial = new Vector3d(System.Math.Cos(meanMotion * t), System.Math.Sin(meanMotion * t), 0) * radius;
                return new RigidBodyForces(Vector3d.Zero, GravityGradient.ComputeTorqueBody(mu,
                    candidate.Orientation.Inverse().Rotate(radial), mass.InertiaBody));
            });
            time += dt;
        }
        var axis = state.Orientation.Rotate(Vector3d.Right);
        double relativeAngle = System.Math.Atan2(axis.Y, axis.X) - meanMotion * time;
        Assert.InRange(System.Math.Abs(relativeAngle), 0, 2e-9);
        Assert.InRange(System.Math.Abs(state.AngularVelocityBody.Z - meanMotion + angle0 * frequency), 0, 2e-11);
    }

    [Fact]
    public void SingularPositionsAndNonfiniteInputsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GravityGradient.ComputeTorqueBody(1, Vector3d.Zero, Matrix3x3d.Identity));
        Assert.Throws<ArgumentOutOfRangeException>(() => GravityGradient.ComputeTorqueBody(-1, Vector3d.Up, Matrix3x3d.Identity));
        Assert.Throws<ArgumentException>(() => GravityGradient.ComputeTorqueBody(1,
            new Vector3d(double.NaN, 0, 0), Matrix3x3d.Identity));
    }
}
