namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Xunit;

public sealed class Slosh6DofDynamicsTests
{
    private static readonly RigidBodyMassProperties Locked = new(5000, new(0.2, 4, -0.1),
        Matrix3x3d.Diagonal(9000, 5000, 10000));
    private static readonly Slosh6DofMode[] Modes =
        [new(400, new(0.5, 6.5, 0.3), 1.5), new(300, new(-0.3, 8.2, 0.2), 1.2)];

    [Fact]
    [Trait("FidelityGate", "F5")]
    [Trait("GateKind", "analytic")]
    public void ZeroDisplacementRetainsTheLockedMassCentroidAndFullTensorExactly()
    {
        var model = new Slosh6DofDynamics(Locked, Modes);
        var actual = model.MassProperties(new SloshModalState[2]);
        Assert.Equal(Locked.Mass, actual.Mass);
        Assert.Equal(Locked.CenterOfMassBody, actual.CenterOfMassBody);
        Assert.Equal(Locked.InertiaBody, actual.InertiaBody);
    }

    [Fact]
    public void RemovingTheModesRecoversRigidBodyEulerDynamicsAboutTheActualCenter()
    {
        var model = new Slosh6DofDynamics(Locked, []);
        var w = new Vector3d(0.1, -0.2, 0.3);
        var force = new Vector3d(200, 400, -100);
        var torque = new Vector3d(20, -30, 40);
        var result = model.Evaluate(Quaterniond.Identity, w, [], new(force, torque, default));
        var expectedAngular = Locked.InverseInertiaBody.Multiply(torque
            - Locked.CenterOfMassBody.Cross(force) - w.Cross(Locked.InertiaBody.Multiply(w)));
        var expectedDatum = force / Locked.Mass - expectedAngular.Cross(Locked.CenterOfMassBody)
            - w.Cross(w.Cross(Locked.CenterOfMassBody));
        Near(expectedAngular, result.AngularAccelerationBody, 1e-13);
        Near(expectedDatum, result.DatumAccelerationWorld, 1e-13);
    }

    [Fact]
    [Trait("FidelityGate", "F5")]
    [Trait("GateKind", "analytic")]
    public void AUniformGravitationalFieldDoesNotRestoreSloshInFreeFall()
    {
        var model = new Slosh6DofDynamics(Locked, Modes);
        var q = Quaterniond.FromEuler(20, 30, -12);
        var g = new Vector3d(1, -9, 0.5);
        SloshModalState[] states = [new(0.15, -0.08, 0, 0), new(-0.05, 0.11, 0, 0)];
        var result = model.Evaluate(q, default, states, new(default, default, g));
        Near(g, result.DatumAccelerationWorld, 1e-12);
        Near(default, result.AngularAccelerationBody, 1e-12);
        foreach (var a in result.ModalAccelerations)
        {
            Assert.InRange(System.Math.Abs(a.X), 0, 1e-12);
            Assert.InRange(System.Math.Abs(a.Z), 0, 1e-12);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoMovingModesConserveInertialMomentaAndObeyInternalDamping(bool damped)
    {
        var model = new Slosh6DofDynamics(Locked, Modes);
        var state = State(new(10, -20, 4), new(0.2, -0.1, 0.05), Quaterniond.FromEuler(15, -30, 10),
            new(0.1, -0.2, 0.3), [new(0.15, -0.08, 0.2, 0.12), new(-0.05, 0.11, -0.1, 0.2)]);
        var initial = IndependentInvariants(state);
        var reported = Invariants(model, state);
        Near(initial.LinearMomentumWorld, reported.LinearMomentumWorld, 1e-9);
        Near(initial.AngularMomentumWorld, reported.AngularMomentumWorld, 1e-8);
        Assert.InRange(System.Math.Abs(initial.KineticEnergyJ - reported.KineticEnergyJ), 0, 1e-9);
        double previousEnergy = initial.KineticEnergyJ;
        for (int i = 0; i < 2000; i++)
        {
            state = Step(model, state, 0.0005, value => new(default, default, default,
                damped ? Modal(value).Select(s => new SloshModalForce(-30 * s.XRate, -30 * s.ZRate)).ToArray() : null));
            var current = IndependentInvariants(state);
            Near(initial.LinearMomentumWorld, current.LinearMomentumWorld, 1e-7);
            Near(initial.AngularMomentumWorld, current.AngularMomentumWorld, 1e-6);
            if (damped) Assert.True(current.KineticEnergyJ <= previousEnergy + 1e-9);
            else Assert.InRange(System.Math.Abs(current.KineticEnergyJ / initial.KineticEnergyJ - 1), 0, 1e-10);
            previousEnergy = current.KineticEnergyJ;
        }
        if (damped) Assert.True(previousEnergy < initial.KineticEnergyJ - 0.1);
    }

    [Fact]
    public void TheSixDofSolveReproducesTheIndependentFreeCarrierFrequency()
    {
        const double carrier = 3000, bob = 600, length = 1.5, acceleration = 12, dt = 0.001;
        // Large finite rotational inertia isolates the independently known translational
        // free-carrier mode without replacing the kernel's angular solve.
        var mass = new RigidBodyMassProperties(carrier + bob, Matrix3x3d.Diagonal(1e12, 1e12, 1e12));
        var model = new Slosh6DofDynamics(mass, [new(bob, Vector3d.Up * length, length)]);
        var state = State(default, default, Quaterniond.Identity, default, [new(1e-4, 0, 0, 0)]);
        var crossings = new List<double>();
        for (int i = 0; i < 6000 && crossings.Count < 2; i++)
        {
            var next = Step(model, state, dt, _ => new(Vector3d.Up * ((carrier + bob) * acceleration), default, default));
            if (state[13] > 0 && next[13] <= 0)
                crossings.Add((i + state[13] / (state[13] - next[13])) * dt);
            state = next;
        }
        Assert.Equal(2, crossings.Count);
        double expected = 2 * System.Math.PI / System.Math.Sqrt(acceleration / length * (1 + bob / carrier));
        Assert.InRange(System.Math.Abs((crossings[1] - crossings[0]) / expected - 1), 0, 1e-6);
    }

    [Fact]
    public void SphericalAndCartesianCoordinatesGiveTheSamePhysicalAcceleration()
    {
        var model = new Slosh6DofDynamics(Locked, Modes);
        SloshModalState[] cartesian = [new(0.15, -0.08, 0.2, 0.12), new(-0.05, 0.11, -0.1, 0.2)];
        var spherical = cartesian.Select((state, i) =>
        {
            double length = Modes[i].LengthM;
            double depth = System.Math.Sqrt(length * length - state.X * state.X - state.Z * state.Z);
            return new SloshSphericalState(new Vector3d(state.X, -depth, state.Z) / length,
                new Vector3d(state.XRate, (state.X * state.XRate + state.Z * state.ZRate) / depth, state.ZRate) / length);
        }).ToArray();
        var q = Quaterniond.FromEuler(20, 13, -7); var w = new Vector3d(0.1, -0.2, 0.3);
        var loads = new Slosh6DofLoads(new(2, 20, -1), new(3, -5, 4), new(-1, -2, 0.3));
        var a = model.Evaluate(q, w, cartesian, loads);
        var b = model.EvaluateSpherical(q, w, spherical, loads);
        Near(a.DatumAccelerationWorld, b.DatumAccelerationWorld, 1e-12);
        Near(a.AngularAccelerationBody, b.AngularAccelerationBody, 1e-12);
        for (int i = 0; i < Modes.Length; i++)
        {
            var acceleration = b.DirectionAccelerationBodyPerS2[i] * Modes[i].LengthM;
            Assert.InRange(System.Math.Abs(a.ModalAccelerations[i].X - acceleration.X), 0, 1e-12);
            Assert.InRange(System.Math.Abs(a.ModalAccelerations[i].Z - acceleration.Z), 0, 1e-12);
        }
        var equilibrium = model.SphericalMassProperties(
            [new(-Vector3d.Up, default), new(-Vector3d.Up, default)]);
        Assert.Equal(Locked.CenterOfMassBody, equilibrium.CenterOfMassBody);
        Assert.Equal(Locked.InertiaBody, equilibrium.InertiaBody);
    }

    [Fact]
    public void ConservativeSphericalContinuationCrossesTheOldChartBoundaryWithoutReset()
    {
        var model = new Slosh6DofDynamics(Locked, Modes);
        var state = new double[25]; state[6] = 1;
        state[14] = state[20] = -1; // Both directions initially point down.
        state[16] = 2; state[24] = 1.7;
        var initial = IndependentSphericalInvariants(state);
        bool crossedUpperHemisphere = false;
        for (int i = 0; i < 4000; i++)
        {
            state = SphericalStep(model, state, 0.0005);
            crossedUpperHemisphere |= state[14] > 0 || state[20] > 0;
            var value = IndependentSphericalInvariants(state);
            Near(initial.LinearMomentumWorld, value.LinearMomentumWorld, 1e-7);
            Near(initial.AngularMomentumWorld, value.AngularMomentumWorld, 1e-6);
            Assert.InRange(System.Math.Abs(value.KineticEnergyJ / initial.KineticEnergyJ - 1), 0, 1e-9);
        }
        Assert.True(crossedUpperHemisphere);
    }

    [Theory]
    [InlineData("apollo11_saturn5_as506_1969.json", 1.0)]
    [InlineData("apollo11_saturn5_as506_1969.json", 0.5)]
    [InlineData("apollo11_saturn5_as506_1969.json", 0.01)]
    public void DeclaredCatalogTanksHavePositiveEquilibriumCouplingAcrossFill(string variant, double fill)
    {
        string data = SaturnVAscentFlight.DataDirectory();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(data, "parts"));
        var ship = VehicleVariantDefinition.LoadFromJson(Path.Combine(data, "vehicles", variant))
            .Build(catalog).ToVessel("slosh equilibrium domain");
        foreach (var part in ship.Parts.Parts)
        {
            part.LiquidFuel *= fill;
            part.Oxidizer *= fill;
            part.Monopropellant *= fill;
        }
        var positions = ship.Parts.ComputePartLocalPositions();
        var modes = new List<Slosh6DofMode>();
        foreach (var part in ship.Parts.Parts)
            foreach (var tank in part.Definition.PropellantTanks)
            {
                double mass = tank.Kind switch
                {
                    PartMassComponentKind.LiquidFuel => part.LiquidFuel,
                    PartMassComponentKind.Oxidizer => part.Oxidizer,
                    PartMassComponentKind.Monopropellant => part.Monopropellant,
                    _ => 0,
                };
                if (mass <= 0) continue;
                double depth = mass / (tank.DensityKgPerM3 * System.Math.PI * tank.RadiusM * tank.RadiusM);
                var mode = PropellantSlosh.FirstMode(tank.RadiusM, depth, mass, 1);
                modes.Add(new(mode.SloshMassKg,
                    positions[part] + Vector3d.Up * (tank.BottomYM + mode.HingeHeightM), mode.PendulumLengthM));
            }
        Assert.NotEmpty(modes);
        var properties = ship.Parts.GetMassProperties();
        var model = new Slosh6DofDynamics(properties, modes);
        var sampled = model.MassProperties(new SloshModalState[modes.Count]);
        Assert.Equal(properties.Mass, sampled.Mass);
        Assert.Equal(properties.CenterOfMassBody, sampled.CenterOfMassBody);
        Assert.Equal(properties.InertiaBody, sampled.InertiaBody);
    }

    [Fact]
    [Trait("FidelityGate", "F5")]
    [Trait("GateKind", "analytic")]
    public void PositiveLockedInertiaDoesNotHideAnIndefiniteCoupledKineticMatrix()
    {
        var inadequate = new RigidBodyMassProperties(3000, Matrix3x3d.Identity);
        Assert.Throws<InvalidOperationException>(() => new Slosh6DofDynamics(inadequate,
            [new(600, new(0, 11, 0), 1)]));
    }

    [Theory]
    [InlineData(1.5, 0, 0)]
    [InlineData(double.NaN, 0, 0)]
    [InlineData(0, 0, double.PositiveInfinity)]
    public void InvalidStatesCannotProduceAccelerations(double x, double z, double rate)
    {
        var model = new Slosh6DofDynamics(Locked, [Modes[0]]);
        Assert.ThrowsAny<ArgumentException>(() => model.Evaluate(Quaterniond.Identity, default,
            [new(x, z, rate, 0)], default));
    }

    private static void Near(Vector3d expected, Vector3d actual, double tolerance) =>
        Assert.InRange((expected - actual).Magnitude, 0, tolerance);
    private static double[] State(Vector3d p, Vector3d v, Quaterniond q, Vector3d w, SloshModalState[] modes) =>
        new[] { p.X, p.Y, p.Z, v.X, v.Y, v.Z, q.W, q.X, q.Y, q.Z, w.X, w.Y, w.Z }
            .Concat(modes.SelectMany(s => new[] { s.X, s.Z, s.XRate, s.ZRate })).ToArray();
    private static Quaterniond Orientation(double[] s) => new Quaterniond(s[6], s[7], s[8], s[9]).Normalize();
    private static SloshModalState[] Modal(double[] s) => Enumerable.Range(0, (s.Length - 13) / 4)
        .Select(i => new SloshModalState(s[13 + 4 * i], s[14 + 4 * i], s[15 + 4 * i], s[16 + 4 * i])).ToArray();
    private static Slosh6DofInvariants Invariants(Slosh6DofDynamics model, double[] s) => model.Invariants(
        new(s[0], s[1], s[2]), new(s[3], s[4], s[5]), Orientation(s), new(s[10], s[11], s[12]), Modal(s), default);

    // Independent world-particle reconstruction: remove each locked bob and insert
    // the actual moving one. This does not use the kernel's inertia or momentum sampler.
    private static Slosh6DofInvariants IndependentInvariants(double[] s)
    {
        var q = Orientation(s); var w = new Vector3d(s[10], s[11], s[12]);
        var datum = new Vector3d(s[0], s[1], s[2]); var velocity = new Vector3d(s[3], s[4], s[5]);
        var center = datum + q.Rotate(Locked.CenterOfMassBody);
        var centerVelocity = velocity + q.Rotate(w.Cross(Locked.CenterOfMassBody));
        var p = centerVelocity * Locked.Mass;
        var h = center.Cross(p) + q.Rotate(Locked.InertiaBody.Multiply(w));
        double energy = 0.5 * Locked.Mass * centerVelocity.MagnitudeSquared
            + 0.5 * w.Dot(Locked.InertiaBody.Multiply(w));
        var states = Modal(s);
        for (int i = 0; i < Modes.Length; i++)
        {
            var mode = Modes[i]; var state = states[i];
            double depth = System.Math.Sqrt(mode.LengthM * mode.LengthM - state.X * state.X - state.Z * state.Z);
            var r0 = mode.HingeBodyM - Vector3d.Up * mode.LengthM;
            var r = mode.HingeBodyM + new Vector3d(state.X, -depth, state.Z);
            var relative = new Vector3d(state.XRate,
                (state.X * state.XRate + state.Z * state.ZRate) / depth, state.ZRate);
            var v0 = velocity + q.Rotate(w.Cross(r0));
            var v = velocity + q.Rotate(w.Cross(r) + relative);
            p += (v - v0) * mode.MassKg;
            h += ((datum + q.Rotate(r)).Cross(v) - (datum + q.Rotate(r0)).Cross(v0)) * mode.MassKg;
            energy += 0.5 * mode.MassKg * (v.MagnitudeSquared - v0.MagnitudeSquared);
        }
        return new(p, h, energy);
    }

    private static SloshSphericalState[] Sphere(double[] s) => Enumerable.Range(0, (s.Length - 13) / 6)
        .Select(i =>
        {
            int j = 13 + 6 * i;
            var n = new Vector3d(s[j], s[j + 1], s[j + 2]).Normalized;
            var rate = new Vector3d(s[j + 3], s[j + 4], s[j + 5]);
            return new SloshSphericalState(n, rate - n * n.Dot(rate));
        }).ToArray();

    private static Slosh6DofInvariants IndependentSphericalInvariants(double[] s)
    {
        var q = Orientation(s); var w = new Vector3d(s[10], s[11], s[12]);
        var datum = new Vector3d(s[0], s[1], s[2]); var velocity = new Vector3d(s[3], s[4], s[5]);
        var centerVelocity = velocity + q.Rotate(w.Cross(Locked.CenterOfMassBody));
        var p = centerVelocity * Locked.Mass;
        var h = (datum + q.Rotate(Locked.CenterOfMassBody)).Cross(p) + q.Rotate(Locked.InertiaBody.Multiply(w));
        double energy = 0.5 * Locked.Mass * centerVelocity.MagnitudeSquared
            + 0.5 * w.Dot(Locked.InertiaBody.Multiply(w));
        var states = Sphere(s);
        for (int i = 0; i < Modes.Length; i++)
        {
            var mode = Modes[i]; var state = states[i];
            var r0 = mode.HingeBodyM - Vector3d.Up * mode.LengthM;
            var r = mode.HingeBodyM + state.DirectionBody * mode.LengthM;
            var relative = state.DirectionRateBodyPerS * mode.LengthM;
            var v0 = velocity + q.Rotate(w.Cross(r0)); var v = velocity + q.Rotate(w.Cross(r) + relative);
            p += (v - v0) * mode.MassKg;
            h += ((datum + q.Rotate(r)).Cross(v) - (datum + q.Rotate(r0)).Cross(v0)) * mode.MassKg;
            energy += 0.5 * mode.MassKg * (v.MagnitudeSquared - v0.MagnitudeSquared);
        }
        return new(p, h, energy);
    }

    private static double[] SphericalStep(Slosh6DofDynamics model, double[] s, double dt)
    {
        double[] F(double[] a)
        {
            var q = Orientation(a); var w = new Vector3d(a[10], a[11], a[12]); var states = Sphere(a);
            var result = model.EvaluateSpherical(q, w, states, default);
            var dq = q * new Quaterniond(0, w.X, w.Y, w.Z);
            var d = new double[a.Length];
            d[0] = a[3]; d[1] = a[4]; d[2] = a[5];
            d[3] = result.DatumAccelerationWorld.X; d[4] = result.DatumAccelerationWorld.Y; d[5] = result.DatumAccelerationWorld.Z;
            d[6] = dq.W / 2; d[7] = dq.X / 2; d[8] = dq.Y / 2; d[9] = dq.Z / 2;
            d[10] = result.AngularAccelerationBody.X; d[11] = result.AngularAccelerationBody.Y; d[12] = result.AngularAccelerationBody.Z;
            for (int i = 0; i < states.Length; i++)
            {
                int j = 13 + 6 * i; var rate = states[i].DirectionRateBodyPerS; var accel = result.DirectionAccelerationBodyPerS2[i];
                d[j] = rate.X; d[j + 1] = rate.Y; d[j + 2] = rate.Z;
                d[j + 3] = accel.X; d[j + 4] = accel.Y; d[j + 5] = accel.Z;
            }
            return d;
        }
        double[] Add(double[] a, double[] k, double scale) => a.Zip(k, (x, y) => x + scale * y).ToArray();
        var k1 = F(s); var k2 = F(Add(s, k1, dt / 2)); var k3 = F(Add(s, k2, dt / 2)); var k4 = F(Add(s, k3, dt));
        var next = Enumerable.Range(0, s.Length).Select(i => s[i] + dt / 6 * (k1[i] + 2 * k2[i] + 2 * k3[i] + k4[i])).ToArray();
        var q = Orientation(next); next[6] = q.W; next[7] = q.X; next[8] = q.Y; next[9] = q.Z;
        var states = Sphere(next);
        for (int i = 0; i < states.Length; i++)
        {
            int j = 13 + 6 * i; var n = states[i].DirectionBody; var rate = states[i].DirectionRateBodyPerS;
            next[j] = n.X; next[j + 1] = n.Y; next[j + 2] = n.Z;
            next[j + 3] = rate.X; next[j + 4] = rate.Y; next[j + 5] = rate.Z;
        }
        return next;
    }

    // Independent test integrator for the Newton/Euler kernel; it does not call a
    // production body/slosh integrator or use its conserved-quantity calculations.
    private static double[] Step(Slosh6DofDynamics model, double[] s, double dt, Func<double[], Slosh6DofLoads> load)
    {
        double[] F(double[] a)
        {
            var q = Orientation(a); var w = new Vector3d(a[10], a[11], a[12]);
            var result = model.Evaluate(q, w, Modal(a), load(a));
            var dq = q * new Quaterniond(0, w.X, w.Y, w.Z);
            var derivative = new double[a.Length];
            for (int i = 0; i < 3; i++) derivative[i] = a[i + 3];
            derivative[3] = result.DatumAccelerationWorld.X; derivative[4] = result.DatumAccelerationWorld.Y;
            derivative[5] = result.DatumAccelerationWorld.Z;
            derivative[6] = dq.W / 2; derivative[7] = dq.X / 2; derivative[8] = dq.Y / 2; derivative[9] = dq.Z / 2;
            derivative[10] = result.AngularAccelerationBody.X; derivative[11] = result.AngularAccelerationBody.Y;
            derivative[12] = result.AngularAccelerationBody.Z;
            for (int i = 0; i < result.ModalAccelerations.Count; i++)
            {
                derivative[13 + 4 * i] = a[15 + 4 * i]; derivative[14 + 4 * i] = a[16 + 4 * i];
                derivative[15 + 4 * i] = result.ModalAccelerations[i].X;
                derivative[16 + 4 * i] = result.ModalAccelerations[i].Z;
            }
            return derivative;
        }
        double[] Add(double[] a, double[] k, double factor) => a.Zip(k, (x, y) => x + factor * y).ToArray();
        var k1 = F(s); var k2 = F(Add(s, k1, dt / 2)); var k3 = F(Add(s, k2, dt / 2)); var k4 = F(Add(s, k3, dt));
        var next = Enumerable.Range(0, s.Length).Select(i => s[i] + dt / 6 * (k1[i] + 2 * k2[i] + 2 * k3[i] + k4[i])).ToArray();
        var q = Orientation(next); next[6] = q.W; next[7] = q.X; next[8] = q.Y; next[9] = q.Z;
        return next;
    }
}
