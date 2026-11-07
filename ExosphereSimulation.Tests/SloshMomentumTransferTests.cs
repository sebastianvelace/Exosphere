namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;
using Xunit;

public sealed class SloshMomentumTransferTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("FidelityGate", "F5")]
    [Trait("GateKind", "analytic")]
    public void PrescribedFlowCutsConserveAllSixInertialMomentaWithoutMovingTheHull(bool changeGeometry)
    {
        var before = new RigidBodyMassProperties(5000, new(0.2, 4, -0.1), Matrix3x3d.Diagonal(9000, 5000, 10000));
        var after = changeGeometry ? new RigidBodyMassProperties(5000, new(-0.3, 3.8, 0.1),
            new(10000, 30, -10, 30, 6000, 20, -10, 20, 11000)) : before;
        var oldInternal = new SloshInternalMomenta(new(30, -20, 10), new(-100, 10, 120));
        var newInternal = new SloshInternalMomenta(new(5, 6, -4), new(20, -12, 8));
        var initial = new RigidBody6DofState(new(10, -20, 5), new(2, -1, 0.3),
            Quaterniond.FromEuler(20, -35, 13), new(0.1, -0.2, 0.3));
        var final = SloshMomentumTransfer.Conserve(initial, before, oldInternal, after, newInternal);
        var p0 = Momenta(initial, before, oldInternal); var p1 = Momenta(final, after, newInternal);
        Near(p0.P, p1.P, 1e-10); Near(p0.H, p1.H, 1e-9);
        Assert.Equal(initial.Position, final.Position);
        Assert.InRange(System.Math.Abs(initial.Orientation.W * final.Orientation.W + initial.Orientation.X * final.Orientation.X
            + initial.Orientation.Y * final.Orientation.Y + initial.Orientation.Z * final.Orientation.Z), 1 - 1e-14, 1 + 1e-14);
    }

    [Fact]
    public void UnchangedDynamicModesAreNotMistakenForStoppedFeedMomentum()
    {
        var mass = new RigidBodyMassProperties(5000, new(0.2, 4, -0.1), Matrix3x3d.Diagonal(9000, 5000, 10000));
        var mode = SloshTransportCorrection.SampleDisplacement(new(new(400, 0, 0), new(0.5, 0, 0), default),
            new(0, 5, 0), new(new(0.1, 0, -0.05), new(0.2, 0, -0.1)));
        var baseline = new BodyMassSample(mass.Mass, 0, mass.CenterOfMassBody, default, default, mass.InertiaBody, default);
        var internalMomenta = SloshInternalMomenta.Sample(baseline, default, [mode]);
        Assert.True(internalMomenta.LinearBodyKgMps.Magnitude > 1);
        var initial = new RigidBody6DofState(default, new(2, -1, 0.3), Quaterniond.Identity, new(0.1, -0.2, 0.3));
        var final = SloshMomentumTransfer.Conserve(initial, mass, internalMomenta, mass, internalMomenta);
        Near(initial.Velocity, final.Velocity, 1e-14);
        Near(initial.AngularVelocityBody, final.AngularVelocityBody, 1e-14);
    }

    [Fact]
    public void AnInternalTransitionCannotHideAnExternalMassDebit()
    {
        var before = new RigidBodyMassProperties(5000, Matrix3x3d.Diagonal(9000, 5000, 10000));
        var after = new RigidBodyMassProperties(4999, before.InertiaBody);
        Assert.Throws<ArgumentException>(() => SloshMomentumTransfer.Conserve(
            new(default, default, Quaterniond.Identity, default), before, default, after, default));
    }

    private static (Vector3d P, Vector3d H) Momenta(RigidBody6DofState state,
        RigidBodyMassProperties mass, SloshInternalMomenta internalMomenta)
    {
        var center = state.Position + state.Orientation.Rotate(mass.CenterOfMassBody);
        var centerVelocity = state.Velocity + state.Orientation.Rotate(state.AngularVelocityBody.Cross(mass.CenterOfMassBody));
        var rigidP = centerVelocity * mass.Mass;
        var relativeP = state.Orientation.Rotate(internalMomenta.LinearBodyKgMps);
        var h = center.Cross(rigidP) + state.Orientation.Rotate(mass.InertiaBody.Multiply(state.AngularVelocityBody))
            + state.Position.Cross(relativeP) + state.Orientation.Rotate(internalMomenta.AngularDatumBodyKgM2PerS);
        return (rigidP + relativeP, h);
    }

    private static void Near(Vector3d expected, Vector3d actual, double tolerance) =>
        Assert.InRange((expected - actual).Magnitude, 0, tolerance);
}
