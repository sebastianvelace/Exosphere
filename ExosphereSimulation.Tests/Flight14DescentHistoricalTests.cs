namespace ExosphereSimulation.Tests;

using System.Text.Json;
using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Historical gate of fidelity phase F2: the Flight 14 ship, flying its entry on physical body
/// flaps and the measured (Jorgensen) crossflow drag, reproduces the broadcast descent displays
/// and the broadcast belly-flop speed. The displays do not state their altitude datum
/// (ellipsoid vs sphere moves them by ~1–2 km over this track), hence the 3 km band.
/// </summary>
[Trait("FidelityGate", "F2")]
[Trait("GateKind", "historical")]
public sealed class Flight14DescentHistoricalTests(ITestOutputHelper output)
{
    private const double DisplayAltitudeToleranceM = 3_000.0;

    [Fact]
    public void PhysicalFlapEntryReproducesTheBroadcastDescentAndBellyFlopSpeed()
    {
        var run = Flight14LaunchDiagnostic.AttachDescent(Flight14EntryCheckpointFixture.Resume(),
            Flight14EntryCheckpointFixture.DataDirectory());
        var descent = run.DescentController!;
        var anchors = DescentAnchors();
        var residuals = new List<string>();
        int next = 0;
        for (int i = 0; i < 13_000 * 60 && descent.Phase is not
            (Flight14DescentPhase.DescentReached or Flight14DescentPhase.Blocked); i++)
        {
            run.AdvanceFrame(1.0 / 60);
            double elapsed = run.Universe.CurrentTime - run.Controller.LiftoffEpoch;
            while (next < anchors.Length && elapsed >= anchors[next].Time)
            {
                double residual = run.Ship.GetAltitude(run.Earth) - anchors[next].AltitudeM;
                residuals.Add($"T+{anchors[next].Time:F0}: {residual:F0} m");
                Assert.True(System.Math.Abs(residual) <= DisplayAltitudeToleranceM,
                    $"broadcast descent residual {residual:F0} m at T+{anchors[next].Time:F0}");
                next++;
            }
        }
        output.WriteLine($"descent residuals: {string.Join(", ", residuals)}");
        Assert.True(descent.Phase == Flight14DescentPhase.DescentReached, descent.BlockReason);
        Assert.Equal(anchors.Length, next);
        Assert.InRange(descent.MaximumAngularRateRadPerSecond, 0, 0.15);

        // Belly-flop terminal speed: the broadcast shows 363 km/h at 1.2 km. Terminal speed
        // scales as 1/sqrt(density), so the 3 km endpoint is compared at 1.2 km density.
        var end = descent.EndWitness!;
        var atmosphere = run.Earth.Atmosphere!;
        double atBroadcastAltitude = end.AtmosphereRelativeSpeedMps
            * System.Math.Sqrt(atmosphere.GetDensity(end.GeodeticAltitudeM) / atmosphere.GetDensity(1_200.0));
        output.WriteLine($"belly flop {end.AtmosphereRelativeSpeedMps:F1} m/s at {end.GeodeticAltitudeM:F0} m "
            + $"= {atBroadcastAltitude:F1} m/s at 1.2 km (broadcast {363.0 / 3.6:F1} m/s)");
        Assert.InRange(atBroadcastAltitude, 363.0 / 3.6 * 0.9, 363.0 / 3.6 * 1.1);
    }

    private static (double Time, double AltitudeM)[] DescentAnchors()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            Flight14EntryCheckpointFixture.DataDirectory(), "..",
            "docs/research/STARSHIP_FLIGHT14_DESCENT_BROWSER_ANCHORS_2026-10-02.json")));
        return document.RootElement.GetProperty("anchors").EnumerateArray()
            .Select(a => (Time: a.GetProperty("displayed_elapsed_seconds").GetDouble(),
                AltitudeM: a.GetProperty("displayed_altitude_km").GetDouble() * 1000))
            .Where(a => a.Time > 10_100)
            .ToArray();
    }
}
