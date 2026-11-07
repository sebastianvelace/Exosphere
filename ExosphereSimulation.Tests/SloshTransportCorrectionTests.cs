namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;
using Xunit;

public sealed class SloshTransportCorrectionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ScheduledMomentaAndInertiaRatesMatchIndependentTimeDifferences(double massRate)
    {
        const double dt = 1e-4, massAcceleration = 0.2;
        var floor = new Vector3d(-0.3, 5, 0.2);
        var initial = new SloshModalState(0.1, 0.05, 0.2, -0.1);
        const double ax = 0.4, az = -0.3;
        SloshTransportCorrection At(double t) => SloshTransportCorrection.Sample(
            PropellantSlosh.SampleParameters(2, 100, 200 + massRate * t + massAcceleration * t * t / 2,
                massRate + massAcceleration * t, massAcceleration), floor,
            new(initial.X + initial.XRate * t + ax * t * t / 2,
                initial.Z + initial.ZRate * t + az * t * t / 2,
                initial.XRate + ax * t, initial.ZRate + az * t));
        var value = At(0); var left = At(-dt); var right = At(dt);
        Near(value.RelativeLinearMomentumBodyKgMps,
            (right.FirstMomentDeltaBodyKgM - left.FirstMomentDeltaBodyKgM) / (2 * dt), 1e-7);
        var unknownAcceleration = value.TangentX * ax + value.TangentZ * az;
        Near(value.RelativeLinearMomentumRateKnownBodyN + unknownAcceleration * value.ModalMassKg,
            (right.RelativeLinearMomentumBodyKgMps - left.RelativeLinearMomentumBodyKgMps) / (2 * dt), 1e-7);
        Near(value.RelativeAngularMomentumRateKnownDatumBodyNm
                + value.PositionBodyM.Cross(unknownAcceleration) * value.ModalMassKg,
            (right.RelativeAngularMomentumDatumBodyKgM2PerS - left.RelativeAngularMomentumDatumBodyKgM2PerS) / (2 * dt), 1e-6);
        var actual = Entries(value.InertiaRateDeltaDatumBodyKgM2PerS).ToArray();
        var minus = Entries(left.InertiaDeltaDatumBodyKgM2).ToArray();
        var plus = Entries(right.InertiaDeltaDatumBodyKgM2).ToArray();
        for (int i = 0; i < actual.Length; i++)
            Assert.InRange(System.Math.Abs(actual[i] - (plus[i] - minus[i]) / (2 * dt)), 0, 1e-6);
    }

    [Fact]
    public void EmptyStoreLeftLimitRetainsTheFlowCutMomentumWithoutAddingModalMass()
    {
        var floor = new Vector3d(-0.3, 5, 0.2);
        var state = new SloshModalState(0.1, -0.05, 0.2, -0.1);
        var parameters = PropellantSlosh.SampleParameters(2, 100, 0, -3);
        var value = SloshTransportCorrection.Sample(parameters, floor, state);
        var expected = new Vector3d(state.X, 0, state.Z) * parameters.ModalMassKg.Rate;
        Assert.Equal(0, value.ModalMassKg);
        Assert.Equal(Vector3d.Zero, value.FirstMomentDeltaBodyKgM);
        Assert.All(Entries(value.InertiaDeltaDatumBodyKgM2), x => Assert.Equal(0, x));
        Near(expected, value.RelativeLinearMomentumBodyKgMps, 1e-14);
        Near(floor.Cross(expected), value.RelativeAngularMomentumDatumBodyKgM2PerS, 1e-14);
        var stopped = SloshTransportCorrection.Sample(PropellantSlosh.SampleParameters(2, 100, 0), floor, state);
        Assert.Equal(Vector3d.Zero, stopped.RelativeLinearMomentumBodyKgMps);
        Assert.Equal(Vector3d.Zero, stopped.RelativeAngularMomentumDatumBodyKgM2PerS);
    }

    [Fact]
    public void UndisplacedUnexcitedModeAddsNoMassMomentOrFeedMomentumWhileDraining()
    {
        var value = SloshTransportCorrection.Sample(PropellantSlosh.SampleParameters(2, 100, 200, -3),
            new(0, 5, 0), default);
        Assert.Equal(Vector3d.Zero, value.FirstMomentDeltaBodyKgM);
        Assert.All(Entries(value.InertiaDeltaDatumBodyKgM2), x => Assert.Equal(0, x));
        Assert.All(Entries(value.InertiaRateDeltaDatumBodyKgM2PerS), x => Assert.Equal(0, x));
        Assert.Equal(Vector3d.Zero, value.RelativeLinearMomentumBodyKgMps);
        Assert.Equal(Vector3d.Zero, value.RelativeLinearMomentumRateKnownBodyN);
        Assert.Equal(Vector3d.Zero, value.RelativeAngularMomentumDatumBodyKgM2PerS);
        Assert.Equal(Vector3d.Zero, value.RelativeAngularMomentumRateKnownDatumBodyNm);
    }

    private static void Near(Vector3d a, Vector3d b, double tolerance) =>
        Assert.InRange((a - b).Magnitude, 0, tolerance);
    private static IEnumerable<double> Entries(Matrix3x3d a) =>
        [a.M11, a.M12, a.M13, a.M21, a.M22, a.M23, a.M31, a.M32, a.M33];
}
