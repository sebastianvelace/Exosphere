namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Physics;
using Xunit;

public sealed class SloshParameterScheduleTests
{
    [Theory]
    [InlineData(20)]
    [InlineData(200)]
    [InlineData(2000)]
    public void ValuesMatchTheFixedFillModelAndDerivativesMatchIndependentTimeDifferences(double mass)
    {
        const double radius = 2, lambda = 100, rate = -3, acceleration = 0.2, dt = 0.01;
        var mode = PropellantSlosh.FirstMode(radius, mass / lambda, mass, 10);
        var jet = PropellantSlosh.SampleParameters(radius, lambda, mass, rate, acceleration);
        Close(mode.SloshMassKg, jet.ModalMassKg.Value, 1e-12);
        Close(1 / mode.PendulumLengthM, jet.CurvaturePerM.Value, 1e-12);
        Close(mode.SloshMassHeightM, jet.HeightAboveFloorM.Value, 1e-12);
        var before = PropellantSlosh.SampleParameters(radius, lambda, mass - rate * dt + acceleration * dt * dt / 2);
        var after = PropellantSlosh.SampleParameters(radius, lambda, mass + rate * dt + acceleration * dt * dt / 2);
        SloshScalarJet[] values = [jet.ModalMassKg, jet.CurvaturePerM, jet.HeightAboveFloorM];
        SloshScalarJet[] left = [before.ModalMassKg, before.CurvaturePerM, before.HeightAboveFloorM];
        SloshScalarJet[] right = [after.ModalMassKg, after.CurvaturePerM, after.HeightAboveFloorM];
        for (int i = 0; i < values.Length; i++)
        {
            Close(values[i].Rate, (right[i].Value - left[i].Value) / (2 * dt), 1e-7);
            Close(values[i].Acceleration, (right[i].Value - 2 * values[i].Value + left[i].Value) / (dt * dt), 1e-6);
        }
    }

    [Fact]
    public void ExhaustionHasAFiniteOneSidedLimitWithoutAnInfinitePendulumLength()
    {
        const double radius = 2, lambda = 100, rate = -3, xi = PropellantSlosh.FirstModeRoot;
        var left = PropellantSlosh.SampleParameters(radius, lambda, 0, rate);
        Assert.Equal(0, left.ModalMassKg.Value);
        Assert.Equal(0, left.CurvaturePerM.Value);
        Assert.Equal(0, left.HeightAboveFloorM.Value);
        Close(2 / (xi * xi - 1) * rate, left.ModalMassKg.Rate, 1e-14);
        Close(xi * xi / (lambda * radius * radius) * rate, left.CurvaturePerM.Rate, 1e-14);
        Assert.Equal(0, left.HeightAboveFloorM.Rate);
        Assert.Equal(0, left.ModalMassKg.Acceleration);
        Assert.Equal(0, left.CurvaturePerM.Acceleration);
        Assert.Equal(0, left.HeightAboveFloorM.Acceleration);
        var stopped = PropellantSlosh.SampleParameters(radius, lambda, 0);
        Assert.Equal(default, stopped);
    }

    [Fact]
    public void DeepTankDerivativesAreNotLostBySubtractingUnitHyperbolicTangents()
    {
        var value = PropellantSlosh.SampleParameters(1, 1, 20, -1);
        Assert.True(value.ModalMassKg.Rate < 0);
        Assert.True(value.CurvaturePerM.Rate < 0);
        Assert.True(double.IsFinite(value.ModalMassKg.Rate));
    }

    [Theory]
    [InlineData(0, 100, 1, 0)]
    [InlineData(1, 0, 1, 0)]
    [InlineData(1, 100, -1, 0)]
    [InlineData(1, 100, 1, double.NaN)]
    public void InvalidSchedulesAreRejected(double radius, double lambda, double mass, double rate) =>
        Assert.ThrowsAny<ArgumentException>(() => PropellantSlosh.SampleParameters(radius, lambda, mass, rate));

    private static void Close(double expected, double actual, double tolerance) =>
        Assert.InRange(System.Math.Abs(expected - actual), 0, tolerance * System.Math.Max(1, System.Math.Abs(expected)));
}
