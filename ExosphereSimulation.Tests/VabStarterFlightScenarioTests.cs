namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Played scenario: the VAB "Starter Rocket" template launched by hand from LC-39A.
/// The pilot throttles up, the hold-downs release under the shared policy, and the
/// vehicle flies vertically to burnout, apogee and impact under integrated physics.
/// </summary>
public sealed class VabStarterFlightScenarioTests(ITestOutputHelper output)
{
    [Fact]
    public void StarterTemplateFliesAPlausibleVerticalHop()
    {
        var root = DataDirectory();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(root, "parts"));
        var assembly = new VesselAssembly(catalog);
        var current = assembly.AddRoot("command_pod_mk1");
        current = assembly.AttachPartAutomatically(current.InstanceId, "fuel_tank_small");
        assembly.AttachPartAutomatically(current.InstanceId, "engine_liquid_small");
        var vessel = assembly.ToVessel("Starter Rocket", "starter");

        var universe = Universe.LoadFromDataDirectory(root);
        var earth = universe.GetBody("earth")!;
        var pad = earth.GetSurfacePosition(28.608389, -80.604333, 3.0);
        var up = earth.GetGeodeticUp(pad);
        vessel.Position = pad;
        vessel.Velocity = earth.Velocity + earth.GetSurfaceVelocity(pad);
        vessel.Orientation = Quaterniond.FromTo(Vector3d.Up, up);
        vessel.ReferenceBodyId = earth.Id;
        vessel.IsGroundHeld = true;
        vessel.GroundNormal = (pad - earth.Position).Normalized;
        vessel.GroundOffset = 3.0;
        universe.AddVessel(vessel);
        universe.ActiveVessel = vessel;

        double wetMass = vessel.TotalMass;
        double liftoffTime = double.NaN, burnoutAltitude = double.NaN, burnoutSpeed = double.NaN;
        double apogee = 0.0, maxAcceleration = 0.0;
        for (int step = 0; step < 50 * 3_600 && !vessel.IsDestroyed && !vessel.IsSurfaceSettled; step++)
        {
            vessel.Throttle = System.Math.Min(1.0, vessel.Throttle + 0.02);
            if (vessel.IsGroundHeld
                && HoldDownReleasePolicy.CanRelease(vessel.GetThrustToWeightRatio(earth), vessel.Throttle))
            {
                vessel.ReleaseGroundHold();
                liftoffTime = universe.CurrentTime;
            }
            universe.Tick(0.02);
            double altitude = vessel.GetAltitude(earth);
            apogee = System.Math.Max(apogee, altitude);
            if (!vessel.IsGroundHeld)
                maxAcceleration = System.Math.Max(maxAcceleration, vessel.GetProperAcceleration(earth).Magnitude);
            if (double.IsNaN(burnoutAltitude) && !vessel.IsGroundHeld && vessel.GetCurrentThrust(earth) < 1.0)
            {
                burnoutAltitude = altitude;
                burnoutSpeed = vessel.GetSurfaceVelocity(earth).Magnitude;
            }
        }

        output.WriteLine($"wet={wetMass:F0} kg dry={vessel.TotalMass:F0} kg liftoff t={liftoffTime:F2} s "
            + $"burnout alt={burnoutAltitude:F0} m v={burnoutSpeed:F0} m/s apogee={apogee:F0} m "
            + $"peak accel={maxAcceleration / 9.80665:F1} g impact={vessel.CrashImpactSpeed:F0} m/s destroyed={vessel.IsDestroyed}");

        Assert.True(double.IsFinite(liftoffTime), "The hold-downs never released.");
        Assert.True(double.IsFinite(burnoutAltitude), "The engine never burned out.");
        Assert.True(wetMass > vessel.TotalMass, "No propellant was consumed.");
        // Ideal vacuum rocket-equation bound on the burnout speed.
        double idealDeltaV = 300.0 * 9.80665 * System.Math.Log(wetMass / vessel.TotalMass);
        Assert.True(burnoutSpeed < idealDeltaV, $"Burnout speed {burnoutSpeed:F0} exceeds ideal {idealDeltaV:F0} m/s.");
        // A starter rocket must be a flyable hop to space, not a high-g firecracker.
        Assert.True(apogee > 100_000.0, $"The starter rocket only reached {apogee:F0} m.");
        Assert.True(maxAcceleration < 8.0 * 9.80665, $"Peak load {maxAcceleration / 9.80665:F1} g.");
        Assert.True(vessel.IsDestroyed || vessel.IsSurfaceSettled, "The flight never ended on the surface.");
    }

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data", "bodies")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }
}
