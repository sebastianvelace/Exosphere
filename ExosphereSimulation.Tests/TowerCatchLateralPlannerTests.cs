namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Xunit;
using Xunit.Abstractions;

public sealed class TowerCatchLateralPlannerTests(ITestOutputHelper output)
{
    /// <summary>Two Raptors, identified on the full-physics bench.</summary>
    private static readonly LateralPlantModel TwoEngines = new(1.0, 1.7, 0.9, 0.9);

    [Theory]
    [InlineData(500.0, -77.0, 13.0)]
    [InlineData(250.0, -40.0, 12.0)]
    [InlineData(-30.0, 2.0, 8.0)]
    public void RecedingPlanBringsTheIdentifiedPlantToRestOverTheCradle(double offset, double velocity, double horizon)
    {
        var planner = new TowerCatchLateralPlanner(TwoEngines);
        var w = 1.0 / TwoEngines.PeriodS;
        // Truth plant integrated independently of the planner's internal model.
        double r = offset, v = velocity, z1 = 0, z2 = 0, peak = 0, minReverse = 0;
        const double dt = 0.02;
        for (double t = 0; t < horizon; t += dt)
        {
            double u = planner.Command(new Vector3d(r, 0, 0), new Vector3d(v, 0, 0), System.Math.Max(3.0, horizon - t)).X;
            planner.Advance(new Vector3d(u, 0, 0), dt);
            for (int i = 0; i < 4; i++)
            {
                double h = dt / 4, a = TwoEngines.Gain * w * w * (z1 - TwoEngines.ZeroTimeS * z2);
                double z2d = u - w * w * z1 - 2 * TwoEngines.Damping * w * z2;
                r += v * h; v += a * h; z1 += z2 * h; z2 += z2d * h;
                peak = System.Math.Max(peak, System.Math.Abs(a));
                minReverse = System.Math.Min(minReverse, a * System.Math.Sign(-velocity == 0 ? -offset : -velocity));
            }
        }
        output.WriteLine($"r={r:F2} v={v:F2} peak={peak:F2}");
        Assert.InRange(r, -1.0, 1.0);
        Assert.InRange(v, -0.5, 0.5);
    }

    [Fact]
    public void FirstCommandMovesTowardTheCradleAgainstAnInboundVelocityShortfall()
    {
        // At rest 100 m out, the plan's first command must accelerate toward the cradle.
        var gains = TowerCatchLateralPlanner.FirstCommandGains(TwoEngines, 10.0, 1.0);
        Assert.True(gains[0] > 0.0);
    }
}
