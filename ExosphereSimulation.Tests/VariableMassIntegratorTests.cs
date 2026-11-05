namespace Exosphere.Simulation.Tests;

using Exosphere.Simulation.Integrators;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;

public sealed class VariableMassIntegratorTests
{
    [Fact]
    public void ConstantMassSamplerMatchesTheProductionKernelExactly()
    {
        var mass = new RigidBodyMassProperties(8, Matrix3x3d.Diagonal(2, 3, 5));
        var state = new RigidBody6DofState(Vector3d.Right, Vector3d.Up,
            Quaterniond.FromEuler(17, -23, 31), new Vector3d(0.4, 0.7, 1.1));
        RigidBodyForces Force(RigidBody6DofState candidate, double t) =>
            new(candidate.Orientation.Rotate(Vector3d.Up) * (1 + t), new Vector3d(0.2, -0.3, 0.1));
        var baseline = RigidBody6DofIntegrator.Step(state, 3, 0.02, mass, Force);
        var sampled = RigidBody6DofIntegrator.Step(state, 3, 0.02, _ => mass, (candidate, t, _) => Force(candidate, t));
        Assert.Equal(baseline, sampled);
    }

    [Fact]
    public void ForceStagesReceiveTheirOwnInstantaneousMassSnapshot()
    {
        var samples = new List<(double Time, double Mass)>();
        var initial = new RigidBody6DofState(Vector3d.Zero, Vector3d.Zero, Quaterniond.Identity, Vector3d.Zero);
        RigidBody6DofIntegrator.Step(initial, 3, 0.2,
            t => new RigidBodyMassProperties(20 - t, Matrix3x3d.Identity),
            (_, t, mass) => { samples.Add((t, mass.Mass)); return RigidBodyForces.Zero; });
        Assert.Equal(new[] { (3.0, 17.0), (3.1, 16.9), (3.1, 16.9), (3.2, 16.8) }, samples);
    }

    [Fact]
    public void ConstantExhaustVelocityBurnFollowsTheRocketEquationAndIntegratedDistance()
    {
        const double m0 = 100, flow = 0.5, exhaustSpeed = 2000, end = 10, dt = 0.02;
        var state = new RigidBody6DofState(Vector3d.Zero, Vector3d.Zero, Quaterniond.Identity, Vector3d.Zero);
        for (int i = 0; i < (int)(end / dt); i++)
            state = RigidBody6DofIntegrator.Step(state, i * dt, dt,
                t => new RigidBodyMassProperties(m0 - flow * t, Matrix3x3d.Identity),
                (_, _, _) => new RigidBodyForces(Vector3d.Right * (flow * exhaustSpeed), Vector3d.Zero));
        double m1 = m0 - flow * end;
        double speed = exhaustSpeed * System.Math.Log(m0 / m1);
        double distance = exhaustSpeed * (end + m1 / flow * System.Math.Log(m1 / m0));
        Assert.InRange(System.Math.Abs(state.Velocity.X - speed), 0, 2e-9);
        Assert.InRange(System.Math.Abs(state.Position.X - distance), 0, 2e-8);
    }

    [Fact]
    public void InternalInertiaChangeConservesWorldAngularMomentumWithoutAnOutflow()
    {
        const double end = 10, dt = 0.02;
        var state = new RigidBody6DofState(Vector3d.Zero, Vector3d.Zero, Quaterniond.Identity, Vector3d.Up * 0.2);
        for (int i = 0; i < (int)(end / dt); i++)
            state = RigidBody6DofIntegrator.Step(state, i * dt, dt,
                t => new RigidBodyMassProperties(10, Matrix3x3d.Diagonal(10, 10 - 0.2 * t, 10)),
                (candidate, _, _) => new RigidBodyForces(Vector3d.Zero,
                    VariableMassRotationalDynamics.ComputeTransportTorqueBody(candidate.AngularVelocityBody,
                        Matrix3x3d.Diagonal(0, -0.2, 0), Array.Empty<MassOutflow>())));
        Assert.Equal(2, 8 * state.AngularVelocityBody.Y, 12);
        var expected = Quaterniond.FromAxisAngle(Vector3d.Up, 10 * System.Math.Log(1.25));
        Assert.InRange((state.Orientation.Rotate(Vector3d.Forward) - expected.Rotate(Vector3d.Forward)).Magnitude, 0, 1e-8);
    }

    [Fact]
    public void InvalidSampleMassAndAnOverflowingStageTimeAreRejected()
    {
        var state = new RigidBody6DofState(Vector3d.Zero, Vector3d.Zero, Quaterniond.Identity, Vector3d.Zero);
        Assert.Throws<ArgumentException>(() => RigidBody6DofIntegrator.Step(state, 0, 0.1,
            _ => default, (_, _, _) => RigidBodyForces.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => RigidBody6DofIntegrator.Step(state, double.MaxValue,
            double.MaxValue, _ => new RigidBodyMassProperties(1, Matrix3x3d.Identity), (_, _, _) => RigidBodyForces.Zero));
    }
}
