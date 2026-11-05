namespace Exosphere.Simulation.Tests;

using Exosphere.Simulation.Integrators;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;

public sealed class CoastingAttitudeIntegratorTests
{
    [Fact]
    public void AsymmetricFreeBodyConservesWorldMomentumAndKineticEnergy()
    {
        var inertia = Matrix3x3d.Diagonal(2, 3, 5);
        var mass = new RigidBodyMassProperties(1, inertia);
        var q = Quaterniond.Identity;
        var omega = new Vector3d(0.3, 0.2, 0.1);
        var initialMomentum = inertia.Multiply(omega);
        double initialEnergy = 0.5 * omega.Dot(initialMomentum);
        for (int i = 0; i < 5000; i++)
            (q, omega) = CoastingAttitudeIntegrator.Advance(q, omega, mass, i * 2.0, 2, (_, _) => Vector3d.Zero);
        Assert.InRange((q.Rotate(inertia.Multiply(omega)) - initialMomentum).Magnitude, 0, 1e-8);
        Assert.InRange(System.Math.Abs(0.5 * omega.Dot(inertia.Multiply(omega)) - initialEnergy), 0, 1e-8);
        Assert.InRange(System.Math.Abs(q.Norm - 1), 0, 1e-14);
        // Constant world omega would violate these invariants for this asymmetric body.
        Assert.True((q.Rotate(omega) - new Vector3d(0.3, 0.2, 0.1)).Magnitude > 0.01);
    }

    [Fact]
    public void PrincipalAxisConstantTorqueMatchesTheAnalyticRotation()
    {
        var mass = new RigidBodyMassProperties(1, Matrix3x3d.Diagonal(2, 3, 5));
        const double elapsed = 100;
        var result = CoastingAttitudeIntegrator.Advance(Quaterniond.Identity, Vector3d.Zero,
            mass, 800_000_000, elapsed, (_, _) => new Vector3d(0, 0, 0.01));
        Assert.InRange((result.AngularVelocityBody - new Vector3d(0, 0, 0.2)).Magnitude, 0, 1e-13);
        var analytic = Quaterniond.FromAxisAngle(new Vector3d(0, 0, 1), 10);
        Assert.InRange((result.Orientation.Rotate(Vector3d.Right) - analytic.Rotate(Vector3d.Right)).Magnitude, 0, 1e-8);
    }

    [Fact]
    public void CircularOrbitTidalLibrationMatchesItsSmallAngleSolution()
    {
        var mass = new RigidBodyMassProperties(1, Matrix3x3d.Diagonal(2, 5, 9));
        const double n = 0.001;
        const double amplitude = 0.0001;
        var z = new Vector3d(0, 0, 1);
        var q = Quaterniond.FromAxisAngle(z, amplitude);
        var omega = z * n;
        for (int i = 0; i < 3000; i++)
        {
            (q, omega) = CoastingAttitudeIntegrator.Advance(q, omega, mass, i * 2.0, 2, (orientation, time) =>
            {
                var radial = new Vector3d(System.Math.Cos(n * time), System.Math.Sin(n * time), 0) * 1000;
                return GravityGradient.ComputeTorqueBody(1000, orientation.Inverse().Rotate(radial), mass.InertiaBody);
            });
        }
        // sqrt(3*n^2*(Iyy-Ixx)/Izz) = n for this synthetic dumbbell-like tensor.
        var expected = Quaterniond.FromAxisAngle(z, n * 6000 + amplitude * System.Math.Cos(n * 6000));
        Assert.InRange((q.Rotate(Vector3d.Right) - expected.Rotate(Vector3d.Right)).Magnitude, 0, 1e-10);
        Assert.InRange(System.Math.Abs(omega.Z - (n - amplitude * n * System.Math.Sin(n * 6000))), 0, 1e-13);
    }

    [Fact]
    public void FastAsymmetricSpinAgreesAcrossDifferentWarpPartitions()
    {
        var mass = new RigidBodyMassProperties(1, Matrix3x3d.Diagonal(2, 3, 5));
        var initialOmega = new Vector3d(1.5, 0.3, 0.5);
        var whole = CoastingAttitudeIntegrator.Advance(Quaterniond.Identity, initialOmega, mass, 0, 20, (_, _) => Vector3d.Zero);
        var q = Quaterniond.Identity;
        var omega = initialOmega;
        for (int i = 0; i < 1000; i++)
            (q, omega) = CoastingAttitudeIntegrator.Advance(q, omega, mass, i * 0.02, 0.02, (_, _) => Vector3d.Zero);
        Assert.InRange((q.Rotate(Vector3d.Up) - whole.Orientation.Rotate(Vector3d.Up)).Magnitude, 0, 1e-8);
        Assert.InRange((omega - whole.AngularVelocityBody).Magnitude, 0, 1e-8);
    }

    [Fact]
    public void LargeTotalSpinCannotAliasTheLocalErrorEstimate()
    {
        var mass = new RigidBodyMassProperties(1, Matrix3x3d.Diagonal(1, 1, 1));
        var omega = new Vector3d(0, 0, 10_000);
        var result = CoastingAttitudeIntegrator.Advance(Quaterniond.Identity, omega, mass,
            0, 0.001, (_, _) => Vector3d.Zero);
        var expected = Quaterniond.FromAxisAngle(new Vector3d(0, 0, 1), 10);
        Assert.InRange((result.Orientation.Rotate(Vector3d.Right) - expected.Rotate(Vector3d.Right)).Magnitude, 0, 1e-9);
        Assert.Equal(omega, result.AngularVelocityBody);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidDurationIsRejected(double dt)
    {
        var mass = new RigidBodyMassProperties(1, Matrix3x3d.Diagonal(1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CoastingAttitudeIntegrator.Advance(
            Quaterniond.Identity, Vector3d.Zero, mass, 0, dt, (_, _) => Vector3d.Zero));
    }

    [Fact]
    public void InvalidStateAndNonfiniteLoadsFailWithoutAdvancingTheCaller()
    {
        var mass = new RigidBodyMassProperties(1, Matrix3x3d.Diagonal(1, 1, 1));
        Assert.Throws<ArgumentException>(() => CoastingAttitudeIntegrator.Advance(default,
            Vector3d.Zero, mass, 0, 1, (_, _) => Vector3d.Zero));
        Assert.Throws<ArgumentException>(() => CoastingAttitudeIntegrator.Advance(Quaterniond.Identity,
            new Vector3d(double.NaN, 0, 0), mass, 0, 1, (_, _) => Vector3d.Zero));
        Assert.Throws<ArgumentException>(() => CoastingAttitudeIntegrator.Advance(Quaterniond.Identity,
            Vector3d.Zero, default, 0, 1, (_, _) => Vector3d.Zero));
        Assert.Throws<ArgumentException>(() => CoastingAttitudeIntegrator.Advance(Quaterniond.Identity,
            Vector3d.Zero, mass, 0, 1, (_, _) => new Vector3d(double.NaN, 0, 0)));
    }
}
