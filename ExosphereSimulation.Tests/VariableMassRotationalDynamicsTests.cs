namespace Exosphere.Simulation.Tests;

using Exosphere.Simulation.Integrators;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;

public sealed class VariableMassRotationalDynamicsTests
{
    [Fact]
    public void PointOutflowCarriesTangentialMomentumAndAlwaysRemovesRotationalEnergy()
    {
        var arm = new Vector3d(1, -2, 3);
        var omega = new Vector3d(0.2, 0.3, -0.4);
        var jet = new MassOutflow(4, arm);
        var torque = jet.JetDampingTorqueBody(omega);
        var tangential = omega.Cross(arm);
        Assert.Equal(-4 * tangential.MagnitudeSquared, torque.Dot(omega), 13);
        Assert.InRange(torque.Dot(omega), double.NegativeInfinity, 0);
        Assert.Equal(Vector3d.Zero, jet.JetDampingTorqueBody(arm));
    }

    [Fact]
    public void FiniteExitDiskTransportsAxialSpinThatAPointExitCannot()
    {
        var omega = Vector3d.Up * 0.2;
        var point = new MassOutflow(4, new Vector3d(0, -3, 0));
        var disk = new MassOutflow(4, new Vector3d(0, -3, 0), -Vector3d.Up * 7, 2);
        Assert.Equal(Vector3d.Zero, point.JetDampingTorqueBody(omega));
        Assert.Equal(-1.6, disk.JetDampingTorqueBody(omega).Y, 14);
    }

    [Fact]
    public void InertiaVariationCancelsFluxWhenCorotatingMassLeavesAtItsOwnStation()
    {
        var omega = new Vector3d(0.1, 0.2, 0.3);
        var jets = new[] { new MassOutflow(2, new Vector3d(0, -3, 0)) };
        var inertiaRate = Matrix3x3d.Diagonal(-18, 0, -18);
        var torque = VariableMassRotationalDynamics.ComputeTransportTorqueBody(omega, inertiaRate, jets);
        Assert.InRange(torque.Magnitude, 0, 1e-14);
        // Adding the jet term alone would spuriously damp a co-rotating point loss.
        Assert.True(jets[0].JetDampingTorqueBody(omega).Magnitude > 1);
    }

    [Fact]
    public void RelativeFlowMomentumAndItsBodyDerivativeUseTheGyrostatTransportSigns()
    {
        var torque = VariableMassRotationalDynamics.ComputeTransportTorqueBody(new Vector3d(0.2, -0.4, 0.3),
            default, Array.Empty<MassOutflow>(), new Vector3d(1, 2, 3), new Vector3d(0.1, -0.2, 0.4));
        Assert.InRange((torque - new Vector3d(1.7, 0.5, -1.2)).Magnitude, 0, 1e-14);
    }

    [Fact]
    public void InternalMomentumExchangeConservesTheTotalWorldAngularMomentum()
    {
        var mass = new RigidBodyMassProperties(8, Matrix3x3d.Diagonal(2, 3, 5));
        var initialRelative = new Vector3d(0.1, -0.2, 0.3);
        var relativeRate = new Vector3d(0.03, 0.04, -0.01);
        var state = new RigidBody6DofState(Vector3d.Zero, Vector3d.Zero,
            Quaterniond.FromEuler(17, -23, 31), new Vector3d(0.4, 0.7, 1.1));
        var initial = state.Orientation.Rotate(mass.InertiaBody.Multiply(state.AngularVelocityBody) + initialRelative);
        const double dt = 0.002;
        for (int i = 0; i < 1000; i++)
            state = RigidBody6DofIntegrator.Step(state, i * dt, dt, mass, (candidate, t) =>
                new RigidBodyForces(Vector3d.Zero, VariableMassRotationalDynamics.ComputeTransportTorqueBody(
                    candidate.AngularVelocityBody, default, Array.Empty<MassOutflow>(),
                    initialRelative + relativeRate * t, relativeRate)));
        var final = state.Orientation.Rotate(mass.InertiaBody.Multiply(state.AngularVelocityBody)
            + initialRelative + relativeRate * 2);
        Assert.InRange((final - initial).Magnitude, 0, 2e-9);
    }

    [Fact]
    public void FullInertiaDerivativeRetainsTheProductsOfInertia()
    {
        var rate = new Matrix3x3d(-1, 0.2, 0.3, 0.2, -2, 0.4, 0.3, 0.4, -3);
        var torque = VariableMassRotationalDynamics.ComputeTransportTorqueBody(new Vector3d(1, 2, 3),
            rate, Array.Empty<MassOutflow>());
        Assert.InRange((torque - new Vector3d(-0.3, 2.6, 7.9)).Magnitude, 0, 2e-15);
    }

    [Fact]
    public void MovingCentralOriginMatchesReynoldsAngularMomentumBalance()
    {
        // Instantaneously stationary material frame, CoM at its origin, no spin.
        // Stored fluid carries P=(0,2,0); one kg/s leaves at r=(1,0,0), u=(0,0,1).
        // d(m C)/dt=P-mdot*r gives the moving centroid's body-frame velocity.
        const double mass = 10, flow = 1;
        var momentum = new Vector3d(0, 2, 0);
        var outlet = new Vector3d(1, 0, 0);
        var exitVelocity = new Vector3d(0, 0, 1);
        var centerRate = (momentum - outlet * flow) / mass;
        var thrustMoment = outlet.Cross(exitVelocity * -flow);
        var transport = VariableMassRotationalDynamics.ComputeTransportTorqueBody(Vector3d.Zero,
            default, Array.Empty<MassOutflow>(), centerOfMassRateBodyMps: centerRate,
            relativeLinearMomentumBodyKgMps: momentum);
        // Direct moving-origin Reynolds balance: -sum(mdot*r x v_exit) - C_dot x P.
        var expected = new Vector3d(0, 1, 0.2);
        Assert.InRange((thrustMoment + transport - expected).Magnitude, 0, 1e-15);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("FidelityGate", "F4")]
    [Trait("GateKind", "analytic")]
    public void UniformBurnCylinderMatchesTheNasaClosedForm(bool axialSpin)
    {
        // NASA/CR-1998-208246, sec. 4.3.1: uniform density loss and a uniform,
        // full-radius exit. The axial rate is constant; transverse rate decays as m^p.
        const double initialMass = 100, flow = 0.25, radius = 2, halfLength = 3, end = 40, dt = 0.05;
        double transversePerKg = radius * radius / 4 + halfLength * halfLength / 3;
        double axialPerKg = radius * radius / 2;
        RigidBodyMassProperties Mass(double t) => new(initialMass - flow * t,
            Matrix3x3d.Diagonal((initialMass - flow * t) * transversePerKg,
                (initialMass - flow * t) * axialPerKg, (initialMass - flow * t) * transversePerKg));
        var inertiaRate = Matrix3x3d.Diagonal(-flow * transversePerKg, -flow * axialPerKg, -flow * transversePerKg);
        var exit = new[] { new MassOutflow(flow, new Vector3d(0, -halfLength, 0), -Vector3d.Up, radius) };
        var spinAxis = axialSpin ? Vector3d.Up : Vector3d.Right;
        var state = new RigidBody6DofState(Vector3d.Zero, Vector3d.Zero, Quaterniond.Identity, spinAxis * 0.1);
        for (int i = 0; i < (int)(end / dt); i++)
            state = RigidBody6DofIntegrator.Step(state, i * dt, dt, Mass, (candidate, _, _) =>
                new RigidBodyForces(Vector3d.Zero, VariableMassRotationalDynamics.ComputeTransportTorqueBody(
                    candidate.AngularVelocityBody, inertiaRate, exit)));
        double exponent = (2 * halfLength * halfLength / 3) / transversePerKg;
        double expected = axialSpin ? 0.1 : 0.1 * System.Math.Pow((initialMass - flow * end) / initialMass, exponent);
        Assert.Equal(expected, state.AngularVelocityBody.Dot(spinAxis), 12);
        Assert.InRange((state.AngularVelocityBody - spinAxis * expected).Magnitude, 0, 1e-12);
    }

    [Fact]
    public void NegativeFlowAndUndefinedFiniteAreaExitsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MassOutflow(-1, Vector3d.Zero));
        Assert.Throws<ArgumentException>(() => new MassOutflow(1, Vector3d.Zero, exitRadiusM: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MassOutflow(1, Vector3d.Zero, Vector3d.Up, double.NaN));
        Assert.Equal(Vector3d.Zero, default(MassOutflow).JetDampingTorqueBody(Vector3d.Up));
    }
}
