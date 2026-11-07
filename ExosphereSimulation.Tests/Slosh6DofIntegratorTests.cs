namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;
using Exosphere.Simulation.Integrators;
using Xunit;

public sealed class Slosh6DofIntegratorTests
{
    private static readonly RigidBodyMassProperties Locked = new(5000, new(0.2, 4, -0.1),
        Matrix3x3d.Diagonal(9000, 5000, 10000));
    private static readonly Slosh6DofMode[] Modes =
        [new(400, new(0.5, 6.5, 0.3), 1.5), new(300, new(-0.3, 8.2, 0.2), 1.2)];
    private static readonly Slosh6DofProfile Fixed = new(new(Locked.Mass, 0, Locked.CenterOfMassBody,
        default, default, Locked.InertiaBody, default), Modes.Select(m => new SloshTankParameters(
            new(new(m.MassKg, 0, 0), new(1 / m.LengthM, 0, 0), default),
            m.HingeBodyM - Vector3d.Up * m.LengthM)).ToArray());

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("FidelityGate", "F5")]
    [Trait("GateKind", "analytic")]
    public void CoupledIntegrationConservesIndependentMomentaAcrossTheChartBoundary(bool damped)
    {
        var state = new Slosh6DofState(new(new(10, -20, 4), new(0.2, -0.1, 0.05),
            Quaterniond.FromEuler(15, -30, 10), new(0.1, -0.2, 0.3)),
            [new(default, new(3, 0, 0)), new(default, new(0, 0, 2.04))]);
        var initial = IndependentInvariants(state); double previousEnergy = initial.KineticEnergyJ;
        bool upper = false;
        const double dt = 0.0005;
        for (int i = 0; i < 4000; i++)
        {
            state = Slosh6DofIntegrator.Step(state, i * dt, dt, _ => Fixed,
                (stage, _) => new(default, InternalModalAccelerationBody: damped
                    ? stage.State.Modes.Select(m => -m.RateBodyMps * 0.1).ToArray() : null));
            upper |= state.Modes.Where((m, j) => m.DisplacementBodyM.Y > Modes[j].LengthM).Any();
            var current = IndependentInvariants(state);
            Near(initial.LinearMomentumWorld, current.LinearMomentumWorld, 1e-7);
            Near(initial.AngularMomentumWorld, current.AngularMomentumWorld, 1e-6);
            if (damped) Assert.True(current.KineticEnergyJ <= previousEnergy + 1e-9);
            else Assert.InRange(System.Math.Abs(current.KineticEnergyJ / initial.KineticEnergyJ - 1), 0, 1e-9);
            previousEnergy = current.KineticEnergyJ;
        }
        Assert.True(upper);
        if (damped) Assert.True(previousEnergy < initial.KineticEnergyJ - 1);
    }

    [Fact]
    [Trait("FidelityGate", "F5")]
    [Trait("GateKind", "analytic")]
    public void SmoothIntegrationConvergesAtFourthOrder()
    {
        var initial = new Slosh6DofState(new(default, default, Quaterniond.Identity, new(0.1, -0.2, 0.3)),
            [new(default, new(3, 0, 0)), new(default, new(0, 0, 2.04))]);
        Slosh6DofState Run(double dt)
        {
            var value = initial;
            int count = (int)System.Math.Round(0.8 / dt);
            for (int i = 0; i < count; i++) value = Slosh6DofIntegrator.Step(value, i * dt, dt, _ => Fixed, (_, _) => default);
            return value;
        }
        var reference = Run(0.00125); var coarse = Run(0.04); var fine = Run(0.02);
        double Error(Slosh6DofState s) => (s.Datum.Velocity - reference.Datum.Velocity).Magnitude
            + s.Modes.Select((m, i) => (m.DisplacementBodyM - reference.Modes[i].DisplacementBodyM).Magnitude
                + (m.RateBodyMps - reference.Modes[i].RateBodyMps).Magnitude).Sum();
        Assert.InRange(Error(coarse) / Error(fine), 13, 19);
    }

    [Fact]
    public void AnEmptyTankEndpointRemainsFiniteAndDoesNotChangeTheFundedMass()
    {
        var dry = new FixedShapeMassComponent("dry", new(0, 4, 0), Matrix3x3d.Diagonal(2, 1, 2), [new MassKnot(0, 4000)]);
        var liquid = FixedShapeMassComponent.SettledColumn("liquid", default, Vector3d.Up, 100, 2,
            [new MassKnot(0, 3), new MassKnot(1, 0)]);
        var distribution = new BodyMassDistribution(1, [dry, liquid]);
        var transport = new OpenSystemTransport(distribution,
            [new("drain", [new(1, 3, 0, 1)], [new(new(0.25, -2, 0.1), -Vector3d.Up, 0, 3, 0, 1)])]);
        Slosh6DofProfile Profile(double t) => new(distribution.Sample(t),
            [new(PropellantSlosh.SampleParameters(2, 100, 3 * (1 - t), -3), default)]);
        var state = new Slosh6DofState(new(default, default, Quaterniond.Identity, default),
            [new(new(0.1, 0, 0), new(0.01, 0, 0))]);
        for (int i = 0; i < 100; i++) state = Slosh6DofIntegrator.Step(state, i / 100.0, 0.01, Profile,
            (stage, t) => new(default, transport.Sample(t, t == 1 ? MassBoundarySide.Left : MassBoundarySide.Right, stage.Profile.Baseline,
                stage.State.Datum.AngularVelocityBody)));
        var end = Slosh6DofIntegrator.Sample(state, Profile(1));
        Assert.Equal(4000, end.Mass.Properties.Mass);
        Assert.Equal(0, end.Corrections[0].ModalMassKg);
        Assert.True(double.IsFinite(state.Datum.Velocity.Magnitude));
        Assert.True(double.IsFinite(state.Modes[0].RateBodyMps.Magnitude));
        Assert.Equal(0, state.Modes[0].DisplacementBodyM.Y);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(2000)]
    [Trait("FidelityGate", "F5")]
    [Trait("GateKind", "analytic")]
    public void IntegratedDrainingMomentaEqualOnlyTheOutletFluxIncludingTheEmptyFlowCut(int count)
    {
        var dry = new FixedShapeMassComponent("dry", new(0, 4, 0), Matrix3x3d.Diagonal(2, 1, 2), [new MassKnot(0, 4000)]);
        var liquid = FixedShapeMassComponent.SettledColumn("liquid", default, Vector3d.Up, 100, 2,
            [new MassKnot(0, 3), new MassKnot(1, 0)]);
        var distribution = new BodyMassDistribution(1, [dry, liquid]);
        var exit = new Vector3d(0.25, -2, 0.1);
        var transport = new OpenSystemTransport(distribution,
            [new("drain", [new(1, 3, 0, 1)], [new(exit, -Vector3d.Up, 0, 3, 0, 1)])]);
        Slosh6DofProfile Profile(double t) => new(distribution.Sample(t),
            [new(PropellantSlosh.SampleParameters(2, 100, 3 * (1 - t), -3), default)]);
        var state = new Slosh6DofState(new(new(10, -20, 5), new(2, -1, 0.3),
            Quaterniond.FromEuler(20, -35, 13), new(0.1, -0.2, 0.3)),
            [new(new(0.1, 0, 0.05), new(0.2, 0, -0.1))]);
        var expectedP = Vector3d.Zero; var expectedH = Vector3d.Zero;
        // Independently reconstruct world-space material particles and the prescribed
        // route momenta. No production aggregate moment sampler is used for the oracle.
        (Vector3d P, Vector3d H) Momenta(Slosh6DofState value, double t)
        {
            var mass = distribution.Sample(t); var parameters = Profile(t).Tanks[0].Parameters;
            var modal = value.Modes[0].Sample(parameters.CurvaturePerM).State;
            var datum = value.Datum; var q = datum.Orientation; var w = datum.AngularVelocityBody;
            var feed = transport.Sample(t, t == 1 ? MassBoundarySide.Left : MassBoundarySide.Right, mass, w);
            var cm = datum.Position + q.Rotate(mass.CenterOfMassBodyM);
            var cmV = datum.Velocity + q.Rotate(w.Cross(mass.CenterOfMassBodyM));
            var p = cmV * mass.MassKg + q.Rotate(feed.RelativeLinearMomentumBodyKgMps);
            var h = cm.Cross(cmV * mass.MassKg) + q.Rotate(mass.InertiaBodyKgM2.Multiply(w))
                + datum.Position.Cross(q.Rotate(feed.RelativeLinearMomentumBodyKgMps))
                + q.Rotate(feed.RelativeAngularMomentumBodyKgM2PerS
                    + mass.CenterOfMassBodyM.Cross(feed.RelativeLinearMomentumBodyKgMps));
            var r0 = Vector3d.Up * parameters.HeightAboveFloorM.Value;
            var r0Rate = Vector3d.Up * parameters.HeightAboveFloorM.Rate;
            var d = modal.DisplacementBodyM; var v = modal.RateBodyMps; var r = r0 + d;
            var v0 = datum.Velocity + q.Rotate(w.Cross(r0));
            var v1 = datum.Velocity + q.Rotate(w.Cross(r));
            var relativeP = d * parameters.ModalMassKg.Rate + v * parameters.ModalMassKg.Value;
            var relativeH = (r.Cross(v) + d.Cross(r0Rate)) * parameters.ModalMassKg.Value
                + r0.Cross(d) * parameters.ModalMassKg.Rate;
            p += (v1 - v0) * parameters.ModalMassKg.Value + q.Rotate(relativeP);
            h += ((datum.Position + q.Rotate(r)).Cross(v1) - (datum.Position + q.Rotate(r0)).Cross(v0))
                * parameters.ModalMassKg.Value + datum.Position.Cross(q.Rotate(relativeP)) + q.Rotate(relativeH);
            return (p, h);
        }
        var initial = Momenta(state, 0);
        double dt = 1.0 / count;
        for (int i = 0; i < count; i++)
        {
            int candidate = 0;
            state = Slosh6DofIntegrator.Step(state, i * dt, dt, Profile, (stage, t) =>
            {
                var datum = stage.State.Datum; var q = datum.Orientation;
                var exitV = datum.Velocity + q.Rotate(datum.AngularVelocityBody.Cross(exit));
                double weight = candidate++ is 0 or 3 ? dt / 6 : dt / 3;
                expectedP -= exitV * (3 * weight);
                expectedH -= (datum.Position + q.Rotate(exit)).Cross(exitV) * (3 * weight);
                return new(default, transport.Sample(t, t == 1 ? MassBoundarySide.Left : MassBoundarySide.Right,
                    stage.Profile.Baseline, datum.AngularVelocityBody));
            });
        }
        var final = Momenta(state, 1);
        Near(initial.P + expectedP, final.P, 1e-7);
        Near(initial.H + expectedH, final.H, 1e-6);
        var end = Slosh6DofIntegrator.Sample(state, Profile(1));
        var feedEnd = transport.Sample(1, MassBoundarySide.Left, end.Profile.Baseline, state.Datum.AngularVelocityBody);
        var internalEnd = SloshInternalMomenta.Sample(end.Profile.Baseline, feedEnd, end.Corrections);
        var stopped = SloshMomentumTransfer.Conserve(state.Datum, end.Mass.Properties, internalEnd,
            end.Profile.Baseline.ToRigidBodyMassProperties(), default);
        var dryP = (stopped.Velocity + stopped.Orientation.Rotate(stopped.AngularVelocityBody.Cross(
            end.Profile.Baseline.CenterOfMassBodyM))) * 4000;
        var dryH = (stopped.Position + stopped.Orientation.Rotate(end.Profile.Baseline.CenterOfMassBodyM)).Cross(dryP)
            + stopped.Orientation.Rotate(end.Profile.Baseline.InertiaBodyKgM2.Multiply(stopped.AngularVelocityBody));
        Near(final.P, dryP, 1e-8); Near(final.H, dryH, 1e-7);
    }

    [Fact]
    public void CandidateLoadsSeeTheirOwnProjectedStateAndMassWithoutMutatingTheInput()
    {
        var initial = new Slosh6DofState(new(default, default, Quaterniond.Identity, default),
            [new(new(0.1, 0, 0), new(0.2, 0, 0)), default]);
        var copy = initial.Modes.ToArray(); var times = new List<double>();
        Slosh6DofIntegrator.Step(initial, 10, 0.02, _ => Fixed, (stage, t) =>
        {
            times.Add(t);
            var mode = stage.State.Modes[0]; double k = Fixed.Tanks[0].Parameters.CurvaturePerM.Value;
            Assert.InRange(System.Math.Abs(k * mode.DisplacementBodyM.MagnitudeSquared - 2 * mode.DisplacementBodyM.Y), 0, 1e-14);
            Assert.Equal(Locked.Mass, stage.Mass.Properties.Mass);
            Assert.NotEqual(Locked.CenterOfMassBody, stage.Mass.Properties.CenterOfMassBody);
            return default;
        });
        Assert.Equal(new[] { 10, 10.01, 10.01, 10.02 }, times);
        Assert.Equal(copy, initial.Modes);
    }

    // World-space point replacement, independent of the production moment sampler.
    private static Slosh6DofInvariants IndependentInvariants(Slosh6DofState state)
    {
        var d = state.Datum; var q = d.Orientation; var w = d.AngularVelocityBody;
        var centerVelocity = d.Velocity + q.Rotate(w.Cross(Locked.CenterOfMassBody));
        var p = centerVelocity * Locked.Mass;
        var h = (d.Position + q.Rotate(Locked.CenterOfMassBody)).Cross(p) + q.Rotate(Locked.InertiaBody.Multiply(w));
        double energy = 0.5 * Locked.Mass * centerVelocity.MagnitudeSquared + 0.5 * w.Dot(Locked.InertiaBody.Multiply(w));
        for (int i = 0; i < Modes.Length; i++)
        {
            var mode = Modes[i]; var modal = state.Modes[i];
            var r0 = mode.HingeBodyM - Vector3d.Up * mode.LengthM; var r = r0 + modal.DisplacementBodyM;
            var v0 = d.Velocity + q.Rotate(w.Cross(r0));
            var v = d.Velocity + q.Rotate(w.Cross(r) + modal.RateBodyMps);
            p += (v - v0) * mode.MassKg;
            h += ((d.Position + q.Rotate(r)).Cross(v) - (d.Position + q.Rotate(r0)).Cross(v0)) * mode.MassKg;
            energy += 0.5 * mode.MassKg * (v.MagnitudeSquared - v0.MagnitudeSquared);
        }
        return new(p, h, energy);
    }

    private static void Near(Vector3d expected, Vector3d actual, double tolerance) =>
        Assert.InRange((expected - actual).Magnitude, 0, tolerance);
}
