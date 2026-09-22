namespace Exosphere.Simulation.Tests;

using Exosphere.Simulation.Integrators;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;

public sealed class RigidBody6DofIntegratorTests
{
    [Fact]
    public void ConstantForceProducesAnalyticTranslation()
    {
        var state = new RigidBody6DofState(
            Vector3d.Zero,
            Vector3d.Zero,
            Quaterniond.Identity,
            Vector3d.Zero);
        var mass = new RigidBodyMassProperties(10.0, Matrix3x3d.Diagonal(2.0, 3.0, 4.0));

        RigidBody6DofState result = RigidBody6DofIntegrator.Step(
            state,
            0.0,
            0.1,
            mass,
            (_, _) => new RigidBodyForces(new Vector3d(20.0, 0.0, 0.0), Vector3d.Zero));

        Assert.Equal(0.2, result.Velocity.X, precision: 12);
        Assert.Equal(0.01, result.Position.X, precision: 12);
        Assert.Equal(Vector3d.Zero, result.Velocity - new Vector3d(0.2, 0.0, 0.0));
    }

    [Fact]
    public void PrincipalAxisTorqueProducesAngularAccelerationAndRotation()
    {
        var state = new RigidBody6DofState(
            Vector3d.Zero,
            Vector3d.Zero,
            Quaterniond.Identity,
            Vector3d.Zero);
        var mass = new RigidBodyMassProperties(5.0, Matrix3x3d.Diagonal(2.0, 4.0, 6.0));

        RigidBody6DofState result = RigidBody6DofIntegrator.Step(
            state,
            0.0,
            0.1,
            mass,
            (_, _) => new RigidBodyForces(Vector3d.Zero, new Vector3d(0.0, 8.0, 0.0)));

        Assert.Equal(0.2, result.AngularVelocityBody.Y, precision: 11);
        Assert.Equal(0.01, result.Orientation.ToEuler().yaw * MathUtils.DEG_TO_RAD, precision: 7);
        Assert.Equal(1.0, result.Orientation.Norm, precision: 14);
    }

    [Fact]
    public void TorqueFreeAsymmetricBodyConservesEnergyAndWorldAngularMomentum()
    {
        var mass = new RigidBodyMassProperties(8.0, Matrix3x3d.Diagonal(2.0, 3.0, 5.0));
        var state = new RigidBody6DofState(
            Vector3d.Zero,
            Vector3d.Zero,
            Quaterniond.FromEuler(17.0, -23.0, 31.0),
            new Vector3d(0.4, 0.7, 1.1));
        Vector3d initialMomentum = state.Orientation.Rotate(
            mass.InertiaBody.Multiply(state.AngularVelocityBody));
        double initialEnergy = 0.5 * state.AngularVelocityBody.Dot(
            mass.InertiaBody.Multiply(state.AngularVelocityBody));

        for (int i = 0; i < 1000; i++)
        {
            state = RigidBody6DofIntegrator.Step(
                state,
                i * 0.002,
                0.002,
                mass,
                (_, _) => RigidBodyForces.Zero);
        }

        Vector3d finalMomentum = state.Orientation.Rotate(
            mass.InertiaBody.Multiply(state.AngularVelocityBody));
        double finalEnergy = 0.5 * state.AngularVelocityBody.Dot(
            mass.InertiaBody.Multiply(state.AngularVelocityBody));

        Assert.Equal(1.0, state.Orientation.Norm, precision: 13);
        Assert.InRange((finalMomentum - initialMomentum).Magnitude, 0.0, 2e-9);
        Assert.InRange(System.Math.Abs(finalEnergy - initialEnergy), 0.0, 2e-9);
    }

    [Fact]
    public void ForceEvaluatorSeesIntermediateOrientationStages()
    {
        var state = new RigidBody6DofState(
            Vector3d.Zero,
            Vector3d.Zero,
            Quaterniond.Identity,
            new Vector3d(0.0, 0.0, 1.0));
        var mass = new RigidBodyMassProperties(1.0, Matrix3x3d.Diagonal(1.0, 1.0, 1.0));
        var orientations = new List<Quaterniond>();

        RigidBody6DofState result = RigidBody6DofIntegrator.Step(
            state,
            0.0,
            0.2,
            mass,
            (candidate, _) =>
            {
                orientations.Add(candidate.Orientation);
                return new RigidBodyForces(candidate.Orientation.Rotate(Vector3d.Right), Vector3d.Zero);
            });

        Assert.Equal(4, orientations.Count);
        Assert.True(result.Velocity.X > 0.19 && result.Velocity.X < 0.21);
        Assert.True(result.Velocity.Y > 0.0);
        Assert.Equal(0.0, result.Velocity.Z, precision: 12);
        Assert.Equal(1.0, result.Orientation.Norm, precision: 14);
    }
}
