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

    /// <summary>
    /// AS-506 on its own flown schedule, S-IC burned to its 32 t cutoff residual and the
    /// flight-reconstructed J-2 performance. Flight (MPR-SAT-FE-69-9): insertion at
    /// T+709 s with 75.7 t in the S-IVB and 135.7 t of vehicle. On the AS-503 clock the
    /// S-IVB arrived at TLI with 30 t and ran dry halfway through the injection.
    /// </summary>
    [Fact]
    public void As506ReachesParkingOrbitWithTheTranslunarPropellant()
    {
        var flight = SaturnVAscentFlight.Fly(variantFile: "apollo11_saturn5_as506_1969.json");
        output.WriteLine($"inserted={flight.Inserted} at T+{flight.InsertionElapsedSeconds:F0}s, "
            + $"S-IVB {flight.SivbPropellantKg / 1000:F1} t, vehicle {flight.Vessel.TotalMass / 1000:F1} t");
        Assert.True(flight.Inserted);
        Assert.InRange(flight.InsertionElapsedSeconds, 680.0, 740.0);
        Assert.True(flight.SivbPropellantKg > 66_000.0, $"Only {flight.SivbPropellantKg / 1000:F1} t left for TLI.");
        // Apollo 11's TLI was 3,182 m/s; the S-IVB must carry it with margin.
        double tliCapacity = 428.7 * 9.80665 * System.Math.Log(flight.Vessel.TotalMass
            / (flight.Vessel.TotalMass - flight.SivbPropellantKg));
        output.WriteLine($"S-IVB TLI capacity {tliCapacity:F0} m/s");
        Assert.True(tliCapacity > 3_250.0, $"S-IVB can give only {tliCapacity:F0} m/s for TLI.");
    }
}
