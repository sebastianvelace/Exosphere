namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;

/// <summary>The target velocity of <see cref="ApsidesTargetBurn"/> lies on the requested conic.</summary>
public sealed class ApsidesTargetBurnTests
{
    private const double Gm = 4.9028e12, Rp = 1_737_400.0 + 99_452.4, Ra = 1_737_400.0 + 121_676.4;

    [Theory]
    [InlineData(1.0)]
    [InlineData(-1.0)]
    public void TargetVelocityLiesOnTheRequestedConicAndBranch(double radialSign)
    {
        var position = new Vector3d(1_737_400.0 + 111_120.0, 0.0, 0.0);
        var velocity = new Vector3d(3.0, 1_650.0, 40.0); // any in-plane state: only its plane matters
        Assert.True(ApsidesTargetBurn.TryTargetVelocity(position, velocity, Gm, Rp, Ra, radialSign, out var target));
        var orbit = OrbitalElements.FromStateVector(position, target, Gm, "moon", 0.0);
        Assert.Equal(Rp, orbit.Periapsis, 3);
        Assert.Equal(Ra, orbit.Apoapsis, 3);
        Assert.Equal(System.Math.Sign(radialSign), System.Math.Sign(target.Dot(position)));
        // The target stays in the plane of the current velocity.
        Assert.InRange(System.Math.Abs(target.Dot(position.Cross(velocity).Normalized)), 0.0, 1e-9);
    }

    [Fact]
    public void RadiiOutsideTheTargetConicHaveNoInPlaneSolution()
    {
        var velocity = new Vector3d(0.0, 1_600.0, 0.0);
        Assert.False(ApsidesTargetBurn.TryTargetVelocity(new Vector3d(Rp - 1_000.0, 0.0, 0.0), velocity, Gm, Rp, Ra, 1.0, out _));
        Assert.False(ApsidesTargetBurn.TryTargetVelocity(new Vector3d(Ra + 1_000.0, 0.0, 0.0), velocity, Gm, Rp, Ra, 1.0, out _));
    }
}
