namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;
using Xunit;

public sealed class SloshVariableMassDynamicsTests
{
    [Fact]
    public void ZeroParameterRatesRecoverTheConservationCheckedFixedFillKernel()
    {
        var locked = new RigidBodyMassProperties(5000, new(0.2, 4, -0.1), Matrix3x3d.Diagonal(9000, 5000, 10000));
        Slosh6DofMode[] modes = [new(400, new(0.5, 6.5, 0.3), 1.5), new(300, new(-0.3, 8.2, 0.2), 1.2)];
        SloshModalState[] states = [new(0.15, -0.08, 0.2, 0.12), new(-0.05, 0.11, -0.1, 0.2)];
        var corrections = modes.Select((m, i) => SloshTransportCorrection.Sample(new(new(m.MassKg, 0, 0),
            new(1 / m.LengthM, 0, 0), default), m.HingeBodyM - Vector3d.Up * m.LengthM, states[i])).ToArray();
        var baseline = new BodyMassSample(locked.Mass, 0, locked.CenterOfMassBody, default, default, locked.InertiaBody, default);
        var q = Quaterniond.FromEuler(20, 13, -7); var w = new Vector3d(0.1, -0.2, 0.3);
        var loads = new Slosh6DofLoads(new(2, 20, -1), new(3, -5, 4), new(-1, -2, 0.3));
        var expected = new Slosh6DofDynamics(locked, modes).Evaluate(q, w, states, loads);
        var actual = SloshVariableMassDynamics.Evaluate(baseline, default, q, w, corrections, loads);
        Near(expected.DatumAccelerationWorld, actual.DatumAccelerationWorld, 1e-12);
        Near(expected.AngularAccelerationBody, actual.AngularAccelerationBody, 1e-12);
        for (int i = 0; i < modes.Length; i++)
        {
            Assert.InRange(System.Math.Abs(expected.ModalAccelerations[i].X - actual.ModalAccelerations[i].X), 0, 1e-12);
            Assert.InRange(System.Math.Abs(expected.ModalAccelerations[i].Z - actual.ModalAccelerations[i].Z), 0, 1e-12);
        }
    }

    [Fact]
    public void ZeroModalMassUsesItsKinematicLimitAndDoesNotAlterBodyInertia()
    {
        var mass = new RigidBodyMassProperties(5000, new(0.2, 4, -0.1), Matrix3x3d.Diagonal(9000, 5000, 10000));
        var baseline = new BodyMassSample(mass.Mass, 0, mass.CenterOfMassBody, default, default, mass.InertiaBody, default);
        var correction = SloshTransportCorrection.Sample(PropellantSlosh.SampleParameters(2, 100, 0),
            new(0, 5, 0), new(0.1, -0.05, 0.2, -0.1));
        var w = new Vector3d(0.1, -0.2, 0.3);
        var loads = new Slosh6DofLoads(new(2, 20, -1), new(3, -5, 4), default);
        var result = SloshVariableMassDynamics.Evaluate(baseline, default, Quaterniond.Identity, w, [correction], loads);
        var expected = new Slosh6DofDynamics(mass, []).Evaluate(Quaterniond.Identity, w, [], loads);
        Near(expected.DatumAccelerationWorld, result.DatumAccelerationWorld, 1e-13);
        Near(expected.AngularAccelerationBody, result.AngularAccelerationBody, 1e-13);
        var sampled = SloshVariableMassDynamics.MassProperties(baseline, [correction]);
        Assert.Equal(mass.Mass, sampled.Properties.Mass);
        Assert.Equal(mass.InertiaBody, sampled.Properties.InertiaBody);
        Assert.True(double.IsFinite(result.ModalAccelerations.Single().X));
        Assert.True(double.IsFinite(result.ModalAccelerations.Single().Z));
    }

    [Fact]
    public void CombinedDrainingBalanceMatchesOnlyTheIndependentlyAdvectedOutletMomenta()
    {
        var dry = new FixedShapeMassComponent("dry", new(0, 4, 0), Matrix3x3d.Diagonal(2, 1, 2), [new MassKnot(0, 4000)]);
        var liquid = FixedShapeMassComponent.SettledColumn("liquid", default, Vector3d.Up, 100, 2,
            [new MassKnot(0, 200), new MassKnot(2, 194)]);
        var distribution = new BodyMassDistribution(2, [dry, liquid]);
        var exit = new Vector3d(0.25, -2, 0.1);
        var transport = new OpenSystemTransport(distribution,
            [new("drain", [new(1, 3, 0, 2)], [new(exit, -Vector3d.Up, 0, 3, 0, 2)])]);
        const double time = 1, step = 2e-5;
        var position = new Vector3d(10, -20, 5); var velocity = new Vector3d(2, -1, 0.3);
        var q = Quaterniond.FromEuler(20, -35, 13); var w = new Vector3d(0.1, -0.2, 0.3);
        var state = new SloshModalState(0.1, 0.05, 0.2, -0.1);
        var mass = distribution.Sample(time);
        var parameters = PropellantSlosh.SampleParameters(2, 100, 200 - 3 * time, -3);
        var correction = SloshTransportCorrection.Sample(parameters, default, state);
        var feed = transport.Sample(time, MassBoundarySide.Right, mass, w);
        var acceleration = SloshVariableMassDynamics.Evaluate(mass, feed, q, w, [correction], default);

        (Vector3d P, Vector3d H) At(double dt)
        {
            var currentMass = distribution.Sample(time + dt);
            var currentParameters = PropellantSlosh.SampleParameters(2, 100, 200 - 3 * (time + dt), -3);
            var currentW = w + acceleration.AngularAccelerationBody * dt;
            var sweep = w * dt + acceleration.AngularAccelerationBody * (dt * dt / 2);
            var currentQ = (q * Quaterniond.FromAxisAngle(sweep, sweep.Magnitude)).Normalize();
            var currentV = velocity + acceleration.DatumAccelerationWorld * dt;
            var currentPosition = position + velocity * dt + acceleration.DatumAccelerationWorld * (dt * dt / 2);
            var modalA = acceleration.ModalAccelerations.Single();
            var currentState = new SloshModalState(state.X + state.XRate * dt + modalA.X * dt * dt / 2,
                state.Z + state.ZRate * dt + modalA.Z * dt * dt / 2,
                state.XRate + modalA.X * dt, state.ZRate + modalA.Z * dt);
            var currentFeed = transport.Sample(time + dt, MassBoundarySide.Right, currentMass, currentW);
            var center = currentPosition + currentQ.Rotate(currentMass.CenterOfMassBodyM);
            var centerVelocity = currentV + currentQ.Rotate(currentW.Cross(currentMass.CenterOfMassBodyM));
            var p = centerVelocity * currentMass.MassKg + currentQ.Rotate(currentFeed.RelativeLinearMomentumBodyKgMps);
            var h = center.Cross(centerVelocity * currentMass.MassKg)
                + currentQ.Rotate(currentMass.InertiaBodyKgM2.Multiply(currentW))
                + currentPosition.Cross(currentQ.Rotate(currentFeed.RelativeLinearMomentumBodyKgMps))
                + currentQ.Rotate(currentFeed.RelativeAngularMomentumBodyKgM2PerS
                    + currentMass.CenterOfMassBodyM.Cross(currentFeed.RelativeLinearMomentumBodyKgMps));
            // Independent point replacement and straight-route momentum. Use length
            // geometry rather than the implementation's reciprocal-length derivatives.
            double length = 1 / currentParameters.CurvaturePerM.Value;
            double lengthRate = -currentParameters.CurvaturePerM.Rate * length * length;
            double depth = System.Math.Sqrt(length * length - currentState.X * currentState.X - currentState.Z * currentState.Z);
            var r0 = Vector3d.Up * currentParameters.HeightAboveFloorM.Value;
            var r0Rate = Vector3d.Up * currentParameters.HeightAboveFloorM.Rate;
            var d = new Vector3d(currentState.X, length - depth, currentState.Z);
            var dRate = new Vector3d(currentState.XRate,
                lengthRate - (length * lengthRate - currentState.X * currentState.XRate - currentState.Z * currentState.ZRate) / depth,
                currentState.ZRate);
            var r = r0 + d; double mu = currentParameters.ModalMassKg.Value;
            var v0 = currentV + currentQ.Rotate(currentW.Cross(r0));
            var v1 = currentV + currentQ.Rotate(currentW.Cross(r));
            var relativeP = d * currentParameters.ModalMassKg.Rate + dRate * mu;
            var relativeH = (r.Cross(dRate) + d.Cross(r0Rate)) * mu + r0.Cross(d) * currentParameters.ModalMassKg.Rate;
            p += (v1 - v0) * mu + currentQ.Rotate(relativeP);
            h += ((currentPosition + currentQ.Rotate(r)).Cross(v1)
                - (currentPosition + currentQ.Rotate(r0)).Cross(v0)) * mu
                + currentPosition.Cross(currentQ.Rotate(relativeP)) + currentQ.Rotate(relativeH);
            return (p, h);
        }

        var before = At(-step); var after = At(step);
        var exitVelocity = velocity + q.Rotate(w.Cross(exit));
        Near(-exitVelocity * 3, (after.P - before.P) / (2 * step), 1e-5);
        Near(-(position + q.Rotate(exit)).Cross(exitVelocity) * 3, (after.H - before.H) / (2 * step), 1e-4);
    }

    private static void Near(Vector3d a, Vector3d b, double tolerance) =>
        Assert.InRange((a - b).Magnitude, 0, tolerance);
}
