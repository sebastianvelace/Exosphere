namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Visual;
using Xunit;

public sealed class CameraSmoothingTests
{
    [Theory]
    [InlineData(3.0)]
    [InlineData(6.0)]
    [InlineData(8.0)]
    [InlineData(1.0 / 0.14)]
    [InlineData(16.0)]
    [InlineData(18.0)]
    public void HeldTargetHasSameResponseAtDifferentFrameRatesAndAfterHitch(double rate)
    {
        // A 120 ms interval must not snap an 8/s camera to its target, and splitting
        // the same wall time across frames must not change the result.
        double expected = Follow(12.0, -4.0, rate, new[] { 0.12 });
        foreach (int frames in new[] { 2, 4, 8, 16 })
        {
            double actual = Follow(12.0, -4.0, rate, Enumerable.Repeat(0.12 / frames, frames));
            Assert.Equal(expected, actual, 11);
        }
        Assert.InRange(expected, -3.999999, 11.999999);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(144)]
    public void CockpitRecenteringRetainsSameFractionAfterOneSecond(int fps)
    {
        double yaw = Follow(60.0, 0.0, 3.0, Enumerable.Repeat(1.0 / fps, fps));
        Assert.Equal(60.0 * System.Math.Exp(-3.0), yaw, 10);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NoValidElapsedTimePreservesExistingFrame(double delta)
    {
        Assert.Equal(12.0, Follow(12.0, -4.0, 8.0, new[] { delta }));
        var smoother = new CameraOrientationSmoother();
        var initial = Quaterniond.FromAxisAngle(Vector3d.Right, 0.4);
        smoother.Update(initial, 0.0);
        AssertSameRotation(initial, smoother.Update(Quaterniond.Identity, delta));
    }

    [Fact]
    public void FirstCockpitFrameUsesActualAttitudeWithoutSweepFromIdentity()
    {
        var attitude = Quaterniond.FromAxisAngle(Vector3d.Right, 2.0);
        AssertSameRotation(attitude, new CameraOrientationSmoother().Update(attitude, 1.0 / 60));
    }

    [Fact]
    public void ReturningToCockpitReseedsFromCurrentAttitude()
    {
        var smoother = new CameraOrientationSmoother();
        smoother.Update(Quaterniond.FromAxisAngle(Vector3d.Up, -1.0), 0.016);
        smoother.Reset();
        var changedAttitude = Quaterniond.FromAxisAngle(Vector3d.Right, 2.0);
        AssertSameRotation(changedAttitude, smoother.Update(changedAttitude, 0.016));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(144)]
    public void AttitudeStepHasSameAngularResponseAcrossFrameRates(int fps)
    {
        var smoother = new CameraOrientationSmoother();
        smoother.Update(Quaterniond.Identity, 0.0);
        var target = Quaterniond.FromAxisAngle(Vector3d.Right, 2.0);
        Quaterniond result = Quaterniond.Identity;
        // Stay outside Quaterniond's existing near-equal nlerp approximation.
        for (int frame = 0; frame < fps / 6; frame++)
            result = smoother.Update(target, 1.0 / fps);
        var expected = Quaterniond.FromAxisAngle(Vector3d.Right,
            2.0 * (1.0 - System.Math.Exp(-8.0 / 6.0)));
        AssertSameRotation(expected, result);
    }

    [Fact]
    public void QuaternionSignChangeDoesNotCreateCameraRotation()
    {
        var smoother = new CameraOrientationSmoother();
        var attitude = Quaterniond.FromAxisAngle(Vector3d.Up, 1.0);
        smoother.Update(attitude, 0.0);
        var same = new Quaterniond(-attitude.W, -attitude.X, -attitude.Y, -attitude.Z);
        AssertSameRotation(attitude, smoother.Update(same, 0.016));
    }

    [Fact]
    public void SingleFrameAttitudeSpikeIsAttenuatedAndRecovers()
    {
        var smoother = new CameraOrientationSmoother();
        smoother.Update(Quaterniond.Identity, 0.0);
        var result = smoother.Update(Quaterniond.FromAxisAngle(Vector3d.Right, 0.5), 1.0 / 60);
        Assert.InRange(2.0 * System.Math.Acos(result.W), 0.0, 0.07);
        for (int i = 0; i < 120; i++) result = smoother.Update(Quaterniond.Identity, 1.0 / 60);
        AssertSameRotation(Quaterniond.Identity, result);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(144)]
    public void ShakePhaseStillAdvancesAtWallClockRateAfterADay(int fps)
    {
        double elapsed = 86400.0;
        for (int i = 0; i < fps; i++) elapsed += 1.0 / fps;
        double expected = CameraSmoothing.Oscillator(86401.0, 10.4, 17.13);
        Assert.InRange(System.Math.Abs(
            CameraSmoothing.Oscillator(elapsed, 10.4, 17.13) - expected), 0.0, 2e-8);
    }

    private static double Follow(double value, double target, double rate, IEnumerable<double> frames)
    {
        foreach (double delta in frames)
            value += (target - value) * CameraSmoothing.Blend(delta, rate);
        return value;
    }

    private static void AssertSameRotation(Quaterniond expected, Quaterniond actual)
    {
        // Compare rotated axes, so q and -q correctly represent the same view.
        Assert.InRange((expected.Rotate(Vector3d.Up) - actual.Rotate(Vector3d.Up)).Magnitude, 0, 1e-7);
        Assert.InRange((expected.Rotate(Vector3d.Right) - actual.Rotate(Vector3d.Right)).Magnitude, 0, 1e-7);
    }
}
