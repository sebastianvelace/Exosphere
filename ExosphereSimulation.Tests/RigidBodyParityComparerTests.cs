namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;
using Xunit;

public sealed class RigidBodyParityComparerTests
{
    [Fact]
    public void IdenticalStatesHaveZeroParityError()
    {
        var state = new RigidBodyStateSnapshot(
            new Vector3d(10.0, 20.0, 30.0),
            new Vector3d(1.0, 2.0, 3.0),
            Quaterniond.FromEuler(5.0, 10.0, 15.0),
            new Vector3d(0.01, 0.02, 0.03));

        var result = RigidBodyParityComparer.Compare(
            state,
            state,
            new RigidBodyParityTolerance(0.001, 0.001, 1e-6, 1e-6));

        Assert.True(result.IsFinite);
        Assert.True(result.IsWithinTolerance);
        Assert.Equal(0.0, result.PositionErrorMeters, precision: 12);
        Assert.Equal(0.0, result.VelocityErrorMetersPerSecond, precision: 12);
        Assert.Equal(0.0, result.AttitudeErrorRadians, precision: 12);
        Assert.Equal(0.0, result.AngularVelocityErrorRadiansPerSecond, precision: 12);
    }

    [Fact]
    public void QuaternionSignAmbiguityDoesNotCreateAttitudeError()
    {
        var baseline = new RigidBodyStateSnapshot(
            Vector3d.Zero,
            Vector3d.Zero,
            Quaterniond.Identity,
            Vector3d.Zero);
        var equivalent = baseline with
        {
            Orientation = new Quaterniond(-1.0, 0.0, 0.0, 0.0),
        };

        var result = RigidBodyParityComparer.Compare(
            baseline,
            equivalent,
            new RigidBodyParityTolerance(0.0, 0.0, 1e-12, 0.0));

        Assert.True(result.IsWithinTolerance);
        Assert.Equal(0.0, result.AttitudeErrorRadians, precision: 12);
    }

    [Fact]
    public void DivergenceOutsideToleranceIsReported()
    {
        var baseline = new RigidBodyStateSnapshot(
            Vector3d.Zero,
            Vector3d.Zero,
            Quaterniond.Identity,
            Vector3d.Zero);
        var candidate = baseline with
        {
            CenterOfMassPositionWorld = new Vector3d(2.0, 0.0, 0.0),
        };

        var result = RigidBodyParityComparer.Compare(
            baseline,
            candidate,
            new RigidBodyParityTolerance(1.0, 1.0, 1.0, 1.0));

        Assert.False(result.IsWithinTolerance);
        Assert.Equal(2.0, result.PositionErrorMeters, precision: 12);
    }
}
