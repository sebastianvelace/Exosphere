namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Xunit;

public sealed class TeleportRegressionTests
{
    [Fact]
    public void PrepareForTeleportClearsStaleOrbitSpinAndContactState()
    {
        var vessel = new Vessel("jump-regression")
        {
            IsGroundHeld = true,
            IsOnRails = true,
            OrbitalState = new OrbitalElements
            {
                ReferenceBodyId = "earth",
                SemiMajorAxis = 7_000_000.0,
                Eccentricity = 0.01,
            },
            AngularVelocity = new Vector3d(0.2, 0.1, -0.3),
            PitchYawRoll = new Vector3d(1.0, -1.0, 0.5),
            Throttle = 1.0,
            IsAttemptingTowerCatch = true,
        };

        vessel.PrepareForTeleport();

        Assert.False(vessel.IsGroundHeld);
        Assert.False(vessel.IsSurfaceSettled);
        Assert.False(vessel.IsOnRails);
        Assert.Null(vessel.OrbitalState);
        Assert.Equal(0.0, vessel.Throttle);
        Assert.Equal(Vector3d.Zero, vessel.AngularVelocity);
        Assert.Equal(Vector3d.Zero, vessel.PitchYawRoll);
        Assert.False(vessel.IsCaught);
        Assert.False(vessel.IsAttemptingTowerCatch);
        Assert.False(vessel.IsTowerCatchDemonstration);
        Assert.Equal(0.0, vessel.CatchSettledDuration);
        Assert.True(double.IsNaN(vessel.LastCatchEvaluationRangeM));
        Assert.False(vessel.LastCatchEvaluationPassedGate);
        Assert.True(double.IsNaN(vessel.CatchTargetEpochSeconds));
    }
}
