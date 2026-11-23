namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Math;
using Xunit;

/// <summary>
/// The Flight 15 terminal belly flop flown physically from the game's 4 km glide state. The
/// game's catch chapter writes the glide attitude kinematically (tower-catch demonstration);
/// here flaps and torques must hold it. The physical Flight 14/15 descent law limits the angle
/// of attack to what the flaps can trim (the 70° entry angle saturates them on trim alone at
/// catch mass) and keeps the reference within reach, so the belly flop stays controlled.
/// </summary>
public sealed class Flight15PhysicalGlideTests
{
    [Fact]
    public void PhysicalDescentLawHoldsTheBellyFlopToIgnition()
    {
        var bench = Flight15CatchBench.Restore("glide_4km");
        bench.PhysicalDescent = Flight15CatchBench.Flight15Descent();
        var ship = bench.Ship;
        var earth = bench.Universe.Bodies.First();
        double start = bench.Universe.CurrentTime, maximumRate = 0.0, deviation = 0.0;
        int samples = 0;
        bench.OnControlStep = b =>
        {
            if (b.Burn != null || b.Universe.CurrentTime - start < 10.0) return;
            var air = earth.GetAirRelativeVelocity(ship.CenterOfMass, ship.CenterOfMassVelocity).Normalized;
            var axis = ship.Orientation.Rotate(Vector3d.Up).Normalized;
            double angle = System.Math.Acos(System.Math.Clamp(axis.Dot(air), -1.0, 1.0)) * MathUtils.RAD_TO_DEG;
            deviation += System.Math.Abs(angle - b.LastGlideAngleOfAttackDeg);
            samples++;
            maximumRate = System.Math.Max(maximumRate, ship.AngularVelocity.Magnitude);
        };

        bench.Run(40.0);

        Assert.True(bench.Burn != null, "the glide never reached ignition");
        Assert.True(samples > 0);
        Assert.InRange(maximumRate, 0.0, 0.5);
        Assert.InRange(deviation / samples, 0.0, 6.0);
    }
}
