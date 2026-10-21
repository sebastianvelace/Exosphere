namespace ExosphereSimulation.Tests;

using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Apollo 8's AS-503 flown from LC-39A to a real parking orbit: an S-IC pitch-over and
/// zero-AoA gravity turn with the historical staging times, then time-to-go insertion
/// steering on the S-II and S-IVB until the integrated periapsis clears the atmosphere.
/// No state is assigned. The disturbed case reproduces the S-II separation transient seen
/// in the game (30° off the aim and ~0.2 rad/s): with booster-tuned damping the low-authority
/// S-IVB fell into a saturated tumble and reached orbit with ~28 t instead of ~67 t.
/// </summary>
public sealed class SaturnVAscentScenarioTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(30.0, 0.3)]
    public void ClosedLoopUpperStagesReachTheParkingOrbit(double separationErrorDeg, double separationRateRadPerS)
    {
        var flight = SaturnVAscentFlight.Fly(separationErrorDeg: separationErrorDeg,
            separationRateRadPerS: separationRateRadPerS);
        var earth = flight.Earth;
        output.WriteLine($"inserted={flight.Inserted} at T+{flight.InsertionElapsedSeconds:F0}s orbit="
            + $"{(flight.Orbit.Periapsis - earth.Radius) / 1000:F0} x {(flight.Orbit.Apoapsis - earth.Radius) / 1000:F0} km "
            + $"i={flight.Orbit.Inclination * 180 / System.Math.PI:F1} deg "
            + $"S-IVB propellant left={flight.SivbPropellantKg / 1000:F1} t destroyed={flight.Vessel.IsDestroyed}");
        Assert.False(flight.Vessel.IsDestroyed);
        Assert.True(flight.Inserted);
        // Historical S-IVB first cutoff: T+685 s with roughly 70 t left for TLI.
        Assert.InRange(flight.InsertionElapsedSeconds, 560.0, 800.0);
        Assert.InRange(flight.Orbit.Apoapsis - earth.Radius, SaturnVAscentFlight.MinimumParkingPeriapsisM, 260_000.0);
        Assert.True(flight.SivbPropellantKg > 60_000.0, $"Only {flight.SivbPropellantKg / 1000:F1} t left for TLI.");
    }
}
