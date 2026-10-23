namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Navigation;
using Xunit;

public sealed class CapturePlannerTests
{
    private const double MoonGm = 4.9028e12;
    private const double MoonRadius = 1_737_400.0;

    [Fact]
    public void HyperbolicArrivalNeedsAnApolloSizedLunarOrbitInsertion()
    {
        // Apollo 8: ~0.9 km/s hyperbolic excess, 111 km perilune, captured into 111 x 312 km.
        double rp = MoonRadius + 111_000.0;
        double vInfinity = 900.0;
        double a = -MoonGm / (vInfinity * vInfinity);
        double dv = CapturePlanner.PeriapsisDeltaV(MoonGm, rp, a, MoonRadius + 312_000.0);
        Assert.InRange(dv, -1_000.0, -800.0); // flight: 913 m/s
    }

    [Fact]
    public void TargetAtThePeriapsisCircularizesAndATargetBelowItIsClamped()
    {
        double rp = MoonRadius + 111_000.0, ra = MoonRadius + 312_000.0;
        double a = 0.5 * (rp + ra);
        double circularize = CapturePlanner.PeriapsisDeltaV(MoonGm, rp, a, rp);
        double vp = System.Math.Sqrt(MoonGm * (2.0 / rp - 1.0 / a));
        Assert.Equal(System.Math.Sqrt(MoonGm / rp) - vp, circularize, 6);
        Assert.Equal(circularize, CapturePlanner.PeriapsisDeltaV(MoonGm, rp, a, MoonRadius), 9);
        Assert.Equal(0.0, CapturePlanner.PeriapsisDeltaV(MoonGm, rp, a, ra), 6);
    }

    [Fact]
    public void LoweringFromAHighCircularOrbitIsTheFirstHohmannBurn()
    {
        double r = MoonRadius + 300_000.0, low = MoonRadius + 110_000.0;
        double dv = CapturePlanner.ApsisDeltaV(MoonGm, r, r, low);
        double transfer = System.Math.Sqrt(MoonGm * (2.0 / r - 2.0 / (r + low)));
        Assert.Equal(transfer - System.Math.Sqrt(MoonGm / r), dv, 9);
        Assert.InRange(dv, -45.0, -30.0); // 300 → 110 km: ~38 m/s
    }
}
