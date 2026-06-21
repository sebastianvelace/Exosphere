namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Navigation;
using Xunit;

public sealed class NavigationRegressionTests
{
    private const double SunGm = 1.32712440018e20;
    private const double Au = 1.495978707e11;

    [Fact]
    public void EarthToMarsHohmannHasExpectedSignAndFlightTime()
    {
        var plan = HohmannTransfer.Compute(SunGm, Au, 1.523679 * Au);

        Assert.True(plan.FirstBurnDeltaV > 0.0);
        Assert.True(plan.SecondBurnDeltaV > 0.0);
        Assert.InRange(plan.TimeOfFlight / 86400.0, 250.0, 270.0);
        Assert.InRange(plan.RequiredPhaseAngle, 0.0, 2.0 * System.Math.PI);
    }

    [Fact]
    public void EarthToVenusHohmannStartsWithRetrogradeBurn()
    {
        var plan = HohmannTransfer.Compute(SunGm, Au, 0.723332 * Au);

        Assert.True(plan.FirstBurnDeltaV < 0.0);
        Assert.True(plan.SecondBurnDeltaV < 0.0);
        Assert.InRange(plan.TimeOfFlight / 86400.0, 140.0, 160.0);
        Assert.InRange(plan.RequiredPhaseAngle, 0.0, 2.0 * System.Math.PI);
    }

    [Fact]
    public void HohmannRejectsDegenerateRadii()
    {
        Assert.Throws<ArgumentException>(() => HohmannTransfer.Compute(SunGm, Au, Au));
        Assert.Throws<ArgumentOutOfRangeException>(() => HohmannTransfer.Compute(SunGm, -1.0, Au));
    }
}
