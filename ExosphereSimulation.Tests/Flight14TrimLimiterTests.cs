namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Xunit;

/// <summary>
/// The descent's angle-of-attack limiter decides trim on the moment that changes the angle of
/// attack (about body axis × flow). It once tested the moment about body X, which a belly-first
/// hull and a symmetric flap pair leave at zero, so its answer followed the sign of rounding
/// noise. These checks run on the game's 4 km Flight 15 glide state.
/// </summary>
public sealed class Flight14TrimLimiterTests
{
    [Fact]
    public void LimiterIsInsensitiveToRoundingLevelChangesOfTheLiftReference()
    {
        var (bench, up, surface) = GlideState();
        var flow = surface.Normalized;
        var lift = (up - flow * up.Dot(flow)).Normalized;
        var nudged = Quaterniond.FromAxisAngle(flow, 1e-9).Rotate(lift);
        for (double scheduled = 50.0; scheduled <= 95.0; scheduled += 5.0)
        {
            double a = Limit(bench, surface, lift, scheduled);
            double b = Limit(bench, surface, nudged, scheduled);
            Assert.Equal(a, b);
        }
    }

    [Fact]
    public void LimitedAngleIsItselfTrimmable()
    {
        var (bench, up, surface) = GlideState();
        var flow = surface.Normalized;
        var lift = (up - flow * up.Dot(flow)).Normalized;
        for (double scheduled = 50.0; scheduled <= 95.0; scheduled += 5.0)
        {
            double limited = Limit(bench, surface, lift, scheduled);
            Assert.InRange(limited, 45.0, 95.0);
            Assert.Equal(limited, Limit(bench, surface, lift, limited));
        }
    }

    private static (Flight15CatchBench Bench, Vector3d Up, Vector3d Surface) GlideState()
    {
        var bench = Flight15CatchBench.Restore("glide_4km");
        var earth = bench.Universe.Bodies.First();
        return (bench, earth.GetGeodeticUp(bench.Ship.Position), bench.Ship.GetSurfaceVelocity(earth));
    }

    private static double Limit(Flight15CatchBench bench, Vector3d surface, Vector3d lift, double scheduled)
    {
        var earth = bench.Universe.Bodies.First();
        var descent = Flight15CatchBench.Flight15Descent();
        return Flight14DescentController.TrimFeasibleAngleOfAttack(bench.Ship, earth,
            bench.Ship.GetAltitude(earth), surface, lift, scheduled,
            descent.TrimMarginDegrees * MathUtils.DEG_TO_RAD);
    }
}
