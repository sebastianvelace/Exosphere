namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Gemini 8's emergency return with no state assignment: the solid retrorockets fired with
/// the thrust axis held retrograde, the retrograde section jettisoned at burnout to expose the
/// heat shield (it sits ahead of the shield and would burn through), then a heat-shield-first
/// entry and the parachute sequence to splashdown.
/// </summary>
public sealed class Gemini8ReturnScenarioTests(ITestOutputHelper output)
{
    [Fact]
    public void SolidRetrofireJettisonAndEntryReachSplashdown()
    {
        var root = SaturnVAscentFlight.DataDirectory();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(root, "parts"));
        var assembly = new VesselAssembly(catalog);
        var current = assembly.AddRoot("gemini8_docking_nose");
        foreach (var id in new[] { "gemini8_reentry_module", "gemini8_retro_module" })
            current = assembly.AttachPartAutomatically(current.InstanceId, id);
        var gemini = assembly.ToVessel("Spacecraft 8", "gemini8");
        var universe = Universe.LoadFromDataDirectory(root);
        var earth = universe.GetBody("earth")!;
        double radius = earth.Radius + 290_000.0;
        var tangent = new Vector3d(0.0, 0.0, -1.0);
        gemini.Position = earth.Position + Vector3d.Right * radius;
        gemini.Velocity = earth.Velocity + tangent * System.Math.Sqrt(earth.GM / radius);
        gemini.Orientation = Quaterniond.FromTo(Vector3d.Up, -tangent);
        gemini.ReferenceBodyId = earth.Id;
        gemini.SASEnabled = false;
        foreach (var part in gemini.Parts.Parts) part.RcsAttitudeEnabled = true;
        universe.AddVessel(gemini);
        universe.ActiveVessel = gemini;

        var retro = gemini.Parts.Parts.First(p => p.Definition.HasVehicleRole("gemini_retro_module"));
        gemini.Throttle = 1.0;
        double ignition = universe.CurrentTime;
        while (universe.CurrentTime - ignition < 2.0 || gemini.ComputeThrust(earth).Magnitude >= 1.0)
        {
            var retrograde = -(gemini.Velocity - earth.Velocity).Normalized;
            gemini.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(gemini.Orientation, Vector3d.Up,
                retrograde, gemini.AngularVelocity, proportionalGain: 2.3,
                dampingGain: AttitudeGuidance.DampingGainFor(2.3, gemini.GetPitchYawAngularAuthority(earth)));
            universe.Tick(0.02);
        }
        gemini.Throttle = 0.0;
        var afterRetro = OrbitalElements.FromStateVector(gemini.Position - earth.Position,
            gemini.Velocity - earth.Velocity, earth.GM, earth.Id, universe.CurrentTime);
        universe.AddVessel(gemini.DeployPayload(retro.InstanceId, "Retrograde Section", new Vector3d(0.0, -0.5, 0.0))!);
        output.WriteLine($"retro burn {universe.CurrentTime - ignition:F1} s, vacuum perigee "
            + $"{(afterRetro.Periapsis - earth.Radius) / 1000:F0} km");

        bool chutes = false;
        universe.TimeScale = 10.0;
        while (earth.GetAltitude(gemini.Position) > 130_000.0)
        {
            gemini.PitchYawRoll = CapsuleEntryGuidance.ComputeCommand(gemini, earth);
            universe.Tick(1.0 / 50.0);
        }
        universe.TimeScale = 1.0;
        double peakG = 0.0, lastSpeed = gemini.GetSurfaceVelocity(earth).Magnitude;
        for (int step = 0; step < 50 * 3_000 && !gemini.IsDestroyed; step++)
        {
            double altitude = earth.GetAltitude(gemini.Position);
            if (altitude <= 2.0) break;
            gemini.PitchYawRoll = CapsuleEntryGuidance.ComputeCommand(gemini, earth);
            if (!chutes && altitude <= 15_000.0) chutes = gemini.DeployParachutes(earth) > 0;
            universe.Tick(0.02);
            double speed = gemini.GetSurfaceVelocity(earth).Magnitude;
            if (altitude > 10_000.0) peakG = System.Math.Max(peakG, System.Math.Abs(lastSpeed - speed) / 0.02 / 9.80665);
            lastSpeed = speed;
        }
        output.WriteLine($"splashdown: destroyed={gemini.IsDestroyed} chutes={chutes} speed={lastSpeed:F1} m/s peak={peakG:F1} g");
        Assert.InRange(afterRetro.Periapsis - earth.Radius, -150_000.0, 60_000.0);
        Assert.False(gemini.IsDestroyed);
        Assert.True(chutes);
        Assert.True(lastSpeed < 12.0, $"Splashdown at {lastSpeed:F1} m/s.");
        Assert.InRange(peakG, 4.0, 12.0); // ballistic-class entry from a −30 km vacuum perigee
    }
}
