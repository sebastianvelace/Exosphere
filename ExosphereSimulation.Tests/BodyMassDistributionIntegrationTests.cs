namespace Exosphere.Simulation.Tests;

using Exosphere.Simulation.Integrators;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;

public sealed class BodyMassDistributionIntegrationTests
{
    [Fact]
    public void SampledPartialBurnReproducesTheRocketEquationAndBurnThenCoastDistance()
    {
        const double duration = 3, burn = 2, dry = 100, propellant = 10, flow = 1, exhaustSpeed = 1000;
        var distribution = new BodyMassDistribution(duration,
        [
            new("dry", Vector3d.Zero, Matrix3x3d.Identity, [new(0, dry)]),
            new("propellant", Vector3d.Zero, default, [new(0, propellant), new(burn, propellant - flow * burn)]),
        ]);
        var state = new RigidBody6DofState(default, default, Quaterniond.Identity, default);
        var boundaries = distribution.BoundariesSeconds;
        for (int interval = 0; interval < boundaries.Count - 1; interval++)
        {
            double start = boundaries[interval], end = boundaries[interval + 1];
            // A full RK4 substep has one live-engine set, including its endpoint sample.
            var force = start < burn ? Vector3d.Up * (flow * exhaustSpeed) : Vector3d.Zero;
            const int steps = 200;
            for (int i = 0; i < steps; i++)
            {
                double time = start + (end - start) * i / steps;
                double next = start + (end - start) * (i + 1) / steps;
                state = RigidBody6DofIntegrator.Step(state, time, next - time,
                    time => distribution.Sample(time).ToRigidBodyMassProperties(),
                    (_, _, _) => new RigidBodyForces(force, default));
            }
        }
        double initialMass = dry + propellant, finalMass = initialMass - flow * burn;
        double logarithm = System.Math.Log(initialMass / finalMass);
        double expectedVelocity = exhaustSpeed * logarithm;
        double burnDistance = exhaustSpeed * (burn - finalMass / flow * logarithm);
        Assert.Equal(expectedVelocity, state.Velocity.Y, 9);
        Assert.Equal(burnDistance + expectedVelocity * (duration - burn), state.Position.Y, 9);
        Assert.Equal(Vector3d.Zero, state.AngularVelocityBody);
    }

    [Fact]
    public void ConstantComponentSamplingMatchesTheFrozenMassIntegratorExactly()
    {
        var distribution = new BodyMassDistribution(2,
        [new("body", Vector3d.Zero, Matrix3x3d.Diagonal(2, 1, 2), [new(0, 2)])]);
        var fixedMass = new RigidBodyMassProperties(2, Matrix3x3d.Diagonal(4, 2, 4));
        var expected = new RigidBody6DofState(default, new(2, 3, 4), Quaterniond.Identity, new(0.1, 0.2, 0.3));
        var actual = expected;
        static RigidBodyForces Forces(RigidBody6DofState state, double time) =>
            new(Vector3d.Right * 25, new(0.1, -0.02, 0.03));
        for (int i = 0; i < 100; i++)
        {
            double time = i * 0.01;
            expected = RigidBody6DofIntegrator.Step(expected, time, 0.01, fixedMass, Forces);
            actual = RigidBody6DofIntegrator.Step(actual, time, 0.01,
                t => distribution.Sample(t).ToRigidBodyMassProperties(),
                (candidate, t, _) => Forces(candidate, t));
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void CoRotatingPointRemovalCancelsInertiaRateAgainstOutletFlux()
    {
        // The synthetic outlet is the removed point itself; there is no relative flow.
        var station = Vector3d.Right * 4;
        var distribution = new BodyMassDistribution(2,
        [
            new("dry", Vector3d.Zero, Matrix3x3d.Identity, [new(0, 2)]),
            new("point", station, default, [new(0, 3), new(2, 1)]),
        ]);
        var sample = distribution.Sample(1);
        var exit = station - sample.CenterOfMassBodyM;
        var relativeMomentum = sample.CenterOfMassRateBodyMps * sample.MassKg + exit;
        Assert.Equal(Vector3d.Zero, relativeMomentum);
        var torque = VariableMassRotationalDynamics.ComputeTransportTorqueBody(new(0.2, -0.3, 0.5),
            sample.InertiaRateBodyKgM2PerS, [new MassOutflow(1, exit)],
            centerOfMassRateBodyMps: sample.CenterOfMassRateBodyMps,
            relativeLinearMomentumBodyKgMps: relativeMomentum);
        Assert.InRange(torque.Magnitude, 0, 1e-14);
    }

    [Fact]
    public void SymmetricInternalRedistributionConservesSpinMomentumAcrossTheFlowCut()
    {
        // Radial transfers are symmetric: the centre and relative angular momentum stay
        // zero, no mass leaves, and the rotational transport is exactly -I_dot*omega.
        var distribution = new BodyMassDistribution(2,
        [
            new("dry", Vector3d.Zero, Matrix3x3d.Identity, [new(0, 1)]),
            new("outer-left", -Vector3d.Right, default, [new(0, 1), new(1, 0.5)]),
            new("outer-right", Vector3d.Right, default, [new(0, 1), new(1, 0.5)]),
            new("inner-left", -Vector3d.Right * 0.5, default, [new(0, 0), new(1, 0.5)]),
            new("inner-right", Vector3d.Right * 0.5, default, [new(0, 0), new(1, 0.5)]),
        ]);
        var state = new RigidBody6DofState(default, default, Quaterniond.Identity, new(0, 0, 0.3));
        for (int interval = 0; interval < distribution.BoundariesSeconds.Count - 1; interval++)
        {
            double start = distribution.BoundariesSeconds[interval];
            double end = distribution.BoundariesSeconds[interval + 1];
            // k4 at the cut belongs to the ending interval, k1 to the following interval.
            BodyMassSample Sample(double t) => distribution.Sample(t,
                t == end ? MassBoundarySide.Left : MassBoundarySide.Right);
            for (int i = 0; i < 100; i++)
            {
                double time = start + (end - start) * i / 100;
                double next = start + (end - start) * (i + 1) / 100;
                state = RigidBody6DofIntegrator.Step(state, time, next - time,
                    t => Sample(t).ToRigidBodyMassProperties(),
                    (candidate, t, _) => new RigidBodyForces(default,
                        VariableMassRotationalDynamics.ComputeTransportTorqueBody(
                            candidate.AngularVelocityBody, Sample(t).InertiaRateBodyKgM2PerS,
                            Array.Empty<MassOutflow>())));
            }
            double momentum = distribution.Sample(end).InertiaBodyKgM2.M33 * state.AngularVelocityBody.Z;
            Assert.Equal(3 * 0.3, momentum, 10);
        }
        Assert.Equal(0.4, state.AngularVelocityBody.Z, 10);
        Assert.Equal(Vector3d.Zero, state.Position);
        Assert.Equal(Vector3d.Zero, state.Velocity);
    }
}
