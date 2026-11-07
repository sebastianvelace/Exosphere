namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// F5 first lateral slosh mode per tank (NASA SP-8009 / SP-106 cylinder model): analytic limits,
/// conservation of the column's own mass and centre, the coupled pendulum's momentum and
/// frequency, and the AS-501 measured S-IC slosh frequencies.
/// </summary>
public sealed class PropellantSloshTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("FidelityGate", "F5")]
    [Trait("GateKind", "analytic")]
    public void TheModeRootIsTheFirstZeroOfTheBesselDerivative()
    {
        // J₁′(x) = J₀(x) − J₁(x)/x, by series.
        static double J(int n, double x)
        {
            double sum = 0, term = System.Math.Pow(x / 2, n);
            for (int k = 1; k <= n; k++) term /= k;
            for (int k = 0; k < 60; k++)
            {
                sum += term;
                term *= -(x * x / 4) / ((k + 1) * (k + 1 + n));
            }
            return sum;
        }
        double x = PropellantSlosh.FirstModeRoot;
        Assert.InRange(System.Math.Abs(J(0, x) - J(1, x) / x), 0, 1e-12);
    }

    [Theory]
    [Trait("FidelityGate", "F5")]
    [Trait("GateKind", "analytic")]
    [InlineData(0.05)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(3.0)]
    public void TheModelKeepsTheColumnsMassAndCentre(double depthRatio)
    {
        const double radius = 5.0, mass = 1.0e6, acceleration = 20.0;
        var mode = PropellantSlosh.FirstMode(radius, depthRatio * radius, mass, acceleration);
        Assert.Equal(mass, mode.SloshMassKg + mode.FixedMassKg, 6);
        Assert.Equal(mass * depthRatio * radius / 2, mode.SloshMassKg * mode.SloshMassHeightM + mode.FixedMassKg * mode.FixedMassHeightM, 3);
        // The pendulum is the mode: ω² = a / L.
        Assert.Equal(acceleration / mode.PendulumLengthM, mode.FrequencyRadPerS * mode.FrequencyRadPerS, 9);
        Assert.InRange(mode.SloshMassKg, 0, mass);
        Assert.True(mode.SloshMassHeightM <= depthRatio * radius);
    }

    [Fact]
    [Trait("FidelityGate", "F5")]
    [Trait("GateKind", "analytic")]
    public void DeepAndShallowLimitsMatchTheirClosedForms()
    {
        const double radius = 2.0, mass = 1000, acceleration = 9.80665, xi = PropellantSlosh.FirstModeRoot;
        var deep = PropellantSlosh.FirstMode(radius, 50 * radius, mass, acceleration);
        Assert.Equal(System.Math.Sqrt(xi * acceleration / radius), deep.FrequencyRadPerS, 9);
        Assert.Equal(0.4547 / 50, deep.SloshMassKg / mass, 4);       // SP-8009 0.4547 (R/h)
        Assert.Equal(radius / xi, deep.PendulumLengthM, 9);
        Assert.InRange(System.Math.Abs(2 * radius / xi - (50 * radius - deep.SloshMassHeightM)),
            0, 1e-12); // Exact 2R/xi limit, rather than a truncated decimal coefficient.
        var shallow = PropellantSlosh.FirstMode(radius, 1e-4 * radius, mass, acceleration);
        // Shallow water: ω = ξ₁ √(a h) / R, and m₁/m → 2/(ξ₁² − 1).
        Assert.Equal(xi * System.Math.Sqrt(acceleration * 1e-4 * radius) / radius, shallow.FrequencyRadPerS, 6);
        Assert.Equal(2 / (xi * xi - 1), shallow.SloshMassKg / mass, 6);
    }

    [Fact]
    [Trait("FidelityGate", "F5")]
    [Trait("GateKind", "analytic")]
    public void ACoupledPendulumConservesMomentumAndEnergy()
    {
        const double carrier = 3000, slosh = 600, length = 1.5, acceleration = 12, dt = 1e-3;
        var state = (V: 0.2, Angle: 0.1, Rate: -0.15);
        double momentum = SloshPendulumCoupling.LateralMomentum(carrier, slosh, length, state.V, state.Angle, state.Rate);
        double initialEnergy = Energy(state);
        for (int i = 0; i < 20000; i++)
        {
            state = Step(state, dt, carrier, slosh, length, acceleration);
            Assert.InRange(System.Math.Abs(SloshPendulumCoupling.LateralMomentum(carrier, slosh,
                length, state.V, state.Angle, state.Rate) - momentum), 0, 1e-8);
            Assert.InRange(System.Math.Abs(Energy(state) / initialEnergy - 1), 0, 1e-10);
        }
        double Energy((double V, double Angle, double Rate) q) =>
            0.5 * carrier * q.V * q.V + 0.5 * slosh *
                (System.Math.Pow(q.V + length * q.Rate * System.Math.Cos(q.Angle), 2)
                + System.Math.Pow(length * q.Rate * System.Math.Sin(q.Angle), 2))
            + slosh * acceleration * length * (1 - System.Math.Cos(q.Angle));
    }

    [Fact]
    [Trait("FidelityGate", "F5")]
    [Trait("GateKind", "analytic")]
    public void ACoupledPendulumRunsAtTheFreeFreeFrequency()
    {
        const double carrier = 3000, slosh = 600, length = 1.5, acceleration = 12, dt = 1e-3;
        var state = (V: 0.0, Angle: 1e-4, Rate: 0.0);
        var crossings = new List<double>();
        for (int i = 0; i < 10000 && crossings.Count < 2; i++)
        {
            var next = Step(state, dt, carrier, slosh, length, acceleration);
            if (state.Angle > 0 && next.Angle <= 0)
                crossings.Add((i + state.Angle / (state.Angle - next.Angle)) * dt);
            state = next;
        }
        Assert.Equal(2, crossings.Count);
        double expected = 2 * System.Math.PI / System.Math.Sqrt(acceleration / length * (1 + slosh / carrier));
        Assert.InRange(System.Math.Abs((crossings[1] - crossings[0]) / expected - 1), 0, 1e-7);
    }

    [Fact]
    [Trait("FidelityGate", "F5")]
    [Trait("GateKind", "analytic")]
    public void InternalDampingRemovesEnergyWithoutChangingMomentum()
    {
        const double carrier = 3000, slosh = 600, length = 1.5, acceleration = 12, dt = 1e-3;
        var state = (V: 0.0, Angle: 0.05, Rate: 0.0);
        double initialEnergy = Energy(state), previousEnergy = initialEnergy;
        for (int i = 0; i < 10000; i++)
        {
            state = Step(state, dt, carrier, slosh, length, acceleration, 0.02);
            double energy = Energy(state);
            Assert.True(energy <= previousEnergy + 1e-11);
            Assert.InRange(System.Math.Abs(SloshPendulumCoupling.LateralMomentum(carrier, slosh,
                length, state.V, state.Angle, state.Rate)), 0, 1e-8);
            previousEnergy = energy;
        }
        Assert.InRange(previousEnergy / initialEnergy, 0, 0.5);
        double Energy((double V, double Angle, double Rate) q) =>
            0.5 * carrier * q.V * q.V + 0.5 * slosh *
                (System.Math.Pow(q.V + length * q.Rate * System.Math.Cos(q.Angle), 2)
                + System.Math.Pow(length * q.Rate * System.Math.Sin(q.Angle), 2))
            + slosh * acceleration * length * (1 - System.Math.Cos(q.Angle));
    }

    [Fact]
    public void ShallowStationRetainsItsSmallPositiveHeight()
    {
        const double xi = PropellantSlosh.FirstModeRoot;
        var shallow = PropellantSlosh.FirstMode(1, 1e-8, 1000, 9.80665);
        double expectedHeight = xi * xi * 1e-24 / 12;
        Assert.InRange(System.Math.Abs(shallow.SloshMassHeightM / expectedHeight - 1), 0, 1e-12);
    }

    [Fact]
    public void FiniteHighFrequencyDoesNotOverflowBeforeTheSquareRoot()
    {
        const double xi = PropellantSlosh.FirstModeRoot;
        var fast = PropellantSlosh.FirstMode(1e-100, 1e-100, 1, 1e300);
        double expectedFrequency = System.Math.Exp((System.Math.Log(1e300) + System.Math.Log(xi)
            - System.Math.Log(1e-100) + System.Math.Log(System.Math.Tanh(xi))) / 2);
        Assert.InRange(System.Math.Abs(fast.FrequencyRadPerS / expectedFrequency - 1), 0, 1e-12);
    }

    [Fact]
    public void LargeMassDepthMomentDoesNotOverflowBeforeNormalization()
    {
        var massive = PropellantSlosh.FirstMode(1e10, 1e10, 1e300, 10);
        Assert.True(double.IsFinite(massive.FixedMassHeightM));
        Assert.InRange(System.Math.Abs((massive.FixedMassKg / 1e300) * massive.FixedMassHeightM / 1e10
            + (massive.SloshMassKg / 1e300) * massive.SloshMassHeightM / 1e10 - 0.5), 0, 1e-15);
    }

    [Fact]
    public void EquilibriumWithATinyCarrierDoesNotSubtractEqualBulkMasses()
    {
        var equilibrium = SloshPendulumCoupling.Derivatives(1e-250, 1e250, 1, 10, 0, 0);
        Assert.Equal((0.0, 0.0), equilibrium);
        Assert.Equal(0, SloshPendulumCoupling.LateralMomentum(1e300, 1e299, 1e10, 0, 0, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-9.80665)]
    public void NonSettlingSpecificForceDoesNotInventARestoringFrequency(double acceleration)
    {
        var mode = PropellantSlosh.FirstMode(2, 3, 1000, acceleration);
        Assert.Equal(0, mode.FrequencyRadPerS);
        Assert.True(mode.PendulumLengthM > 0);
    }

    [Theory]
    [InlineData(0, 1, 1, 1)]
    [InlineData(1, 0, 1, 1)]
    [InlineData(1, 1, 0, 1)]
    [InlineData(double.NaN, 1, 1, 1)]
    [InlineData(1, double.PositiveInfinity, 1, 1)]
    [InlineData(1, 1, 1, double.NaN)]
    public void InvalidModeInputsAreRejected(double radius, double depth, double mass, double acceleration) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PropellantSlosh.FirstMode(radius, depth, mass, acceleration));

    [Theory]
    [InlineData(1e308, 1e-300, 1, 1)]
    [InlineData(1e-300, 1e308, 1, 1)]
    [InlineData(1, 1, double.Epsilon, 1)]
    public void UnrepresentableModesAreRejected(double radius, double depth, double mass, double acceleration) =>
        Assert.Throws<ArgumentException>(() => PropellantSlosh.FirstMode(radius, depth, mass, acceleration));

    [Theory]
    [InlineData(0, 1, 1, 1, 0, 0, 0)]
    [InlineData(1, 0, 1, 1, 0, 0, 0)]
    [InlineData(1, 1, 0, 1, 0, 0, 0)]
    [InlineData(1, 1, 1, -1, 0, 0, 0)]
    [InlineData(1, 1, 1, 1, double.NaN, 0, 0)]
    [InlineData(1, 1, 1, 1, 0, double.PositiveInfinity, 0)]
    [InlineData(1, 1, 1, 1, 0, 0, -1)]
    public void InvalidCouplingInputsAreRejected(double carrier, double slosh, double length, double acceleration,
        double angle, double rate, double damping) => Assert.Throws<ArgumentOutOfRangeException>(() =>
            SloshPendulumCoupling.Derivatives(carrier, slosh, length, acceleration, angle, rate, damping));

    private static (double V, double Angle, double Rate) Step((double V, double Angle, double Rate) state,
        double dt, double carrier, double slosh, double length, double acceleration, double damping = 0)
    {
        (double V, double Angle, double Rate) F((double V, double Angle, double Rate) q)
        {
            var (v, rate) = SloshPendulumCoupling.Derivatives(carrier, slosh, length, acceleration,
                q.Angle, q.Rate, damping);
            return (v, q.Rate, rate);
        }
        (double V, double Angle, double Rate) Add((double V, double Angle, double Rate) q,
            (double V, double Angle, double Rate) k, double scale) =>
            (q.V + k.V * scale, q.Angle + k.Angle * scale, q.Rate + k.Rate * scale);
        var k1 = F(state); var k2 = F(Add(state, k1, dt / 2));
        var k3 = F(Add(state, k2, dt / 2)); var k4 = F(Add(state, k3, dt));
        return (state.V + dt / 6 * (k1.V + 2 * k2.V + 2 * k3.V + k4.V),
            state.Angle + dt / 6 * (k1.Angle + 2 * k2.Angle + 2 * k3.Angle + k4.Angle),
            state.Rate + dt / 6 * (k1.Rate + 2 * k2.Rate + 2 * k3.Rate + k4.Rate));
    }

    /// <summary>Cross-variant AS-506 frequencies against the AS-501 measured dotted probe curves.
    /// The comparison band includes graph reading and variant/model differences; it does not
    /// validate slosh amplitude, damping or production coupling.</summary>
    [Fact]
    [Trait("FidelityGate", "F5")]
    [Trait("GateKind", "historical")]
    public void As506SicSloshFollowsTheAs501MeasuredFrequencies()
    {
        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(SaturnVAscentFlight.DataDirectory(),
            "flight_profiles", "saturn_as501_slosh_reference.json")));
        var measured = reference.RootElement.GetProperty("samples").EnumerateArray().Select(row =>
            (Time: row.GetProperty("range_time_s").GetDouble(), Lox: row.GetProperty("lox_hz").GetDouble(),
                Fuel: row.GetProperty("fuel_hz").GetDouble())).ToArray();
        double band = reference.RootElement.GetProperty("relative_comparison_tolerance").GetDouble();
        var samples = new List<(double Time, double Lox, double Fuel)>();
        int next = 0;
        SaturnVAscentFlight.Fly(variantFile: "apollo11_saturn5_as506_1969.json",
            untilElapsedSeconds: measured[^1].Time + 0.02,
            afterTickWithBody: (vessel, earth, elapsed, sicAttached) =>
        {
            if (!sicAttached || next >= measured.Length || elapsed < measured[next].Time) return;
            var tank = vessel.Parts.Parts.First(p => p.Definition.HasVehicleRole("sic_tank"));
            double specificForce = vessel.GetProperAcceleration(earth).Dot(vessel.Orientation.Rotate(Vector3d.Up));
            Assert.True(specificForce > 0);
            double Frequency(PartMassComponentKind kind, double mass)
            {
                var definition = tank.Definition.FindPropellantTank(kind)!;
                double lambda = definition.DensityKgPerM3 * System.Math.PI * definition.RadiusM * definition.RadiusM;
                return PropellantSlosh.FirstMode(definition.RadiusM, mass / lambda, mass, specificForce).FrequencyHz;
            }
            samples.Add((elapsed, Frequency(PartMassComponentKind.Oxidizer, tank.Oxidizer),
                Frequency(PartMassComponentKind.LiquidFuel, tank.LiquidFuel)));
            next++;
        });
        Assert.Equal(measured.Length, samples.Count);
        for (int i = 0; i < measured.Length; i++)
        {
            output.WriteLine($"t={samples[i].Time:F2}s LOX {samples[i].Lox:F6} Hz (probe {measured[i].Lox}), "
                + $"fuel {samples[i].Fuel:F6} Hz (probe {measured[i].Fuel})");
            Assert.InRange(samples[i].Lox / measured[i].Lox, 1 - band, 1 + band);
            Assert.InRange(samples[i].Fuel / measured[i].Fuel, 1 - band, 1 + band);
        }
    }
}
