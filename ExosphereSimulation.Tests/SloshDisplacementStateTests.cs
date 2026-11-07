namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;
using Xunit;

public sealed class SloshDisplacementStateTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void GlobalAndCartesianCoordinatesRecoverTheSamePhysicalAcceleration(double massRate)
    {
        var parameters = PropellantSlosh.SampleParameters(2, 100, 200, massRate, 0.2);
        var floor = new Vector3d(-0.3, 5, 0.2);
        var chart = new SloshModalState(0.15, -0.08, 0.2, 0.12);
        var reference = SloshTransportCorrection.Sample(parameters, floor, chart);
        var d = reference.PositionBodyM - floor - Vector3d.Up * parameters.HeightAboveFloorM.Value;
        var v = reference.VelocityBodyMps - Vector3d.Up * parameters.HeightAboveFloorM.Rate;
        var state = new SloshDisplacementState(d, v);
        var actual = SloshTransportCorrection.SampleDisplacement(parameters, floor, state);
        Near(reference.PositionBodyM, actual.PositionBodyM, 1e-14);
        Near(reference.RelativeLinearMomentumBodyKgMps, actual.RelativeLinearMomentumBodyKgMps, 1e-13);
        Near(reference.RelativeAngularMomentumDatumBodyKgM2PerS,
            actual.RelativeAngularMomentumDatumBodyKgM2PerS, 1e-12);
        var baseline = new BodyMassSample(5000, massRate, new(0.2, 4, -0.1), default, default,
            Matrix3x3d.Diagonal(9000, 5000, 10000), default);
        var q = Quaterniond.FromEuler(20, 13, -7); var w = new Vector3d(0.1, -0.2, 0.3);
        var loads = new Slosh6DofLoads(new(2, 20, -1), new(3, -5, 4), new(-1, -2, 0.3));
        var a = SloshVariableMassDynamics.Evaluate(baseline, default, q, w, [reference], loads);
        var b = SloshVariableMassDynamics.Evaluate(baseline, default, q, w, [actual], loads);
        Near(a.DatumAccelerationWorld, b.DatumAccelerationWorld, 1e-12);
        Near(a.AngularAccelerationBody, b.AngularAccelerationBody, 1e-12);
        Near(reference.AccelerationBiasBodyMps2 + reference.TangentX * a.ModalAccelerations[0].X
                + reference.TangentZ * a.ModalAccelerations[0].Z,
            actual.AccelerationBiasBodyMps2 + actual.TangentX * b.ModalAccelerations[0].X
                + actual.TangentZ * b.ModalAccelerations[0].Z, 1e-12);
    }

    [Theory]
    [InlineData(-0.8)]
    [InlineData(0)]
    [InlineData(0.8)]
    [InlineData(1)]
    public void RetractionPreservesBothHemispheresAndScheduledConstraintDerivatives(double normalY)
    {
        const double k = 0.7, rate = -0.1, acceleration = 0.03;
        var n = new Vector3d(System.Math.Sqrt(1 - normalY * normalY), normalY, 0);
        var d = (n + Vector3d.Up) / k;
        var sample = new SloshDisplacementState(d, new(0.2, -0.1, 0.3)).Sample(new(k, rate, acceleration));
        var projected = sample.State.DisplacementBodyM; var v = sample.State.RateBodyMps;
        Near(d, projected, 1e-14); Near(n, sample.Normal, 1e-14);
        Assert.InRange(System.Math.Abs(k * projected.MagnitudeSquared - 2 * projected.Y), 0, 1e-14);
        Assert.InRange(System.Math.Abs(sample.Normal.Dot(v) + rate * projected.MagnitudeSquared / 2), 0, 1e-14);
        var a = sample.Acceleration(new(0.4, -0.2));
        Assert.InRange(System.Math.Abs(sample.Normal.Dot(a) + k * v.MagnitudeSquared
            + 2 * rate * projected.Dot(v) + acceleration * projected.MagnitudeSquared / 2), 0, 1e-14);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1e-100)]
    [InlineData(0.7)]
    public void LowerHemisphereHasAFiniteEmptyLimitWithoutCancellation(double k)
    {
        var sample = new SloshDisplacementState(new(0.1, 0, -0.05), new(0.2, 0, -0.1))
            .Sample(new(k, -0.1, 0.03));
        Assert.InRange(sample.State.DisplacementBodyM.Magnitude, 0.1, 0.12);
        Assert.InRange(System.Math.Abs(sample.Normal.Dot(sample.State.RateBodyMps)
            - 0.05 * sample.State.DisplacementBodyM.MagnitudeSquared), 0, 1e-16);
        Assert.True(double.IsFinite(sample.Acceleration(new(0.4, -0.2)).Magnitude));
        if (k == 0) Assert.Equal(0, sample.State.DisplacementBodyM.Y);
        if (k == 1e-100) Assert.InRange(sample.State.DisplacementBodyM.Y, 6e-103, 7e-103);
    }

    [Fact]
    public void SingularCandidatesAreRejectedInsteadOfInventingADirection()
    {
        Assert.Throws<ArgumentException>(() => new SloshDisplacementState(Vector3d.Up, default).Sample(new(1, 0, 0)));
        Assert.Throws<ArgumentException>(() => default(SloshDisplacementState).Sample(new(-1, 0, 0)));
    }

    private static void Near(Vector3d expected, Vector3d actual, double tolerance) =>
        Assert.InRange((expected - actual).Magnitude, 0, tolerance);
}
