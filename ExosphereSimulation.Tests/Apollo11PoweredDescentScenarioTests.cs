namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Eagle's descent with no state assignment: from the circular parking orbit DOI lowers the
/// perilune to 15 km, the DPS fires again at that perilune (PDI), brakes on guidance to the
/// low gate and lets down vertically to a soft contact. Mission reference: PDI to touchdown took 12 min 36 s and left ~45 s of
/// hover propellant.
/// </summary>
public sealed class Apollo11PoweredDescentScenarioTests(ITestOutputHelper output)
{
    [Fact]
    public void EagleFliesDoiAndPoweredDescentFromLowLunarOrbitToSoftTouchdown()
    {
        var root = SaturnVAscentFlight.DataDirectory();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(root, "parts"));
        var universe = Universe.LoadFromDataDirectory(root);
        var moon = universe.GetBody("moon")!;
        var eagle = VehicleVariantDefinition.LoadFromJson(Path.Combine(root, "vehicles",
            "apollo11_lm5_eagle_1969.json")).Build(catalog).ToVessel("Eagle", Apollo11FlightProfile.EagleVesselId);

        double radius = moon.Radius + Apollo11FlightProfile.CircularLunarOrbitAltitudeM;
        var tangent = Vector3d.Forward;
        double epoch = universe.CurrentTime;
        eagle.Position = moon.Position + Vector3d.Right * radius;
        eagle.Velocity = moon.Velocity + tangent * System.Math.Sqrt(moon.GM / radius);
        eagle.Orientation = Quaterniond.FromTo(Vector3d.Up, -tangent);
        eagle.ReferenceBodyId = moon.Id;
        eagle.SASEnabled = false;
        universe.AddVessel(eagle);
        universe.SetActiveVessel(eagle.Id);

        var descentStage = eagle.Parts.Parts.Single(part => part.Definition.HasVehicleRole("lunar_module_descent_stage"));
        double dpsPropellantBefore = descentStage.LiquidFuel + descentStage.Oxidizer;
        var descent = new LunarPoweredDescent();
        double peakTiltDeg = 0.0;
        double ignition = double.NaN, gate = double.NaN, doiSeconds = 0.0;
        while (!descent.IsFinished && universe.CurrentTime - epoch < 12_000.0)
        {
            descent.Advance(eagle, moon, 0.02, universe.CurrentTime);
            if (descent.Status == LunarPoweredDescentStatus.DescentOrbitInsertion && eagle.Throttle > 0.0) doiSeconds += 0.02;
            // The half-orbit coast to perilune is unpowered: integrate it in coarser steps.
            universe.TimeScale = descent.Status == LunarPoweredDescentStatus.Coasting ? 50.0 : 1.0;
            if (double.IsNaN(ignition) && descent.Status == LunarPoweredDescentStatus.Braking) ignition = universe.CurrentTime;
            if (double.IsNaN(gate) && descent.Status == LunarPoweredDescentStatus.Terminal)
            {
                gate = universe.CurrentTime;
                output.WriteLine($"low gate: alt {descent.AltitudeM:F0} m, surface speed "
                    + $"{eagle.GetSurfaceVelocity(moon).Magnitude:F1} m/s, {gate - ignition:F0} s after PDI");
            }
            // Touching down upright is what matters: the final 30 m, after the gate pitch-up.
            if (descent.Status == LunarPoweredDescentStatus.Terminal && descent.AltitudeM < 30.0)
            {
                var up = (eagle.Position - moon.Position).Normalized;
                double tilt = System.Math.Acos(System.Math.Clamp(eagle.Orientation.Rotate(Vector3d.Up).Dot(up), -1.0, 1.0));
                peakTiltDeg = System.Math.Max(peakTiltDeg, tilt * MathUtils.RAD_TO_DEG);
            }
            universe.Tick(0.02);
        }

        double remaining = descentStage.LiquidFuel + descentStage.Oxidizer;
        output.WriteLine($"DOI {doiSeconds:F0} s at {100 * LunarPoweredDescent.DescentOrbitInsertionThrottle:F0} %");
        output.WriteLine($"status={descent.Status} {descent.BlockReason} destroyed={eagle.IsDestroyed} "
            + $"touchdown {descent.TouchdownSpeedMps:F2} m/s, PDI→contact {universe.CurrentTime - ignition:F0} s, "
            + $"DPS used {dpsPropellantBefore - remaining:F0} kg, left {remaining:F0} kg, tilt below 30 m ≤ {peakTiltDeg:F1}°");
        Assert.Equal(LunarPoweredDescentStatus.Landed, descent.Status);
        Assert.False(eagle.IsDestroyed);
        Assert.True(descent.TouchdownSpeedMps < AscentStagingPolicy.SoftLandingSpeedMps,
            $"Touchdown at {descent.TouchdownSpeedMps:F2} m/s.");
        Assert.InRange(universe.CurrentTime - ignition, 500.0, 950.0);
        Assert.True(remaining > 0.0, "The DPS ran dry before contact.");
        Assert.InRange(doiSeconds, 20.0, 120.0);
        Assert.True(peakTiltDeg < 10.0, $"Final let-down tilted {peakTiltDeg:F1}°.");
    }
}
