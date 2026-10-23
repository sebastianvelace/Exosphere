namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Xunit;

/// <summary>SAS hold modes: the directions they hold and that holding one actually points there.</summary>
public sealed class SasHoldTests
{
    private static (Universe universe, Vessel vessel, CelestialBody earth) InLowOrbit()
    {
        var root = SaturnVAscentFlight.DataDirectory();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(root, "parts"));
        var universe = Universe.LoadFromDataDirectory(root);
        var earth = universe.GetBody("earth")!;
        var assembly = new VesselAssembly(catalog);
        var nose = assembly.AddRoot("gemini8_docking_nose");
        var capsule = assembly.AttachPartAutomatically(nose.InstanceId, "gemini8_reentry_module");
        assembly.AttachPartAutomatically(capsule.InstanceId, "gemini8_equipment_adapter");
        var vessel = assembly.ToVessel("Gemini", "gemini");
        double radius = earth.Radius + 300_000.0;
        vessel.Position = earth.Position + Vector3d.Right * radius;
        vessel.Velocity = earth.Velocity + Vector3d.Forward * System.Math.Sqrt(earth.GM / radius);
        vessel.Orientation = Quaterniond.Identity;
        vessel.SASEnabled = false;
        universe.AddVessel(vessel);
        universe.ActiveVessel = vessel;
        return (universe, vessel, earth);
    }

    [Fact]
    public void OrbitalModesFormTheProgradeNormalRadialTriad()
    {
        var (_, vessel, earth) = InLowOrbit();
        var prograde = SasHold.Direction(vessel, earth, SasHoldMode.Prograde)!.Value;
        var normal = SasHold.Direction(vessel, earth, SasHoldMode.Normal)!.Value;
        var radialOut = SasHold.Direction(vessel, earth, SasHoldMode.RadialOut)!.Value;
        Assert.False(SasHold.UsesSurfaceFrame(vessel, earth));
        Assert.Equal(1.0, prograde.Dot(Vector3d.Forward), 6);
        Assert.Equal(1.0, radialOut.Dot(Vector3d.Right), 6);
        Assert.Equal(0.0, normal.Dot(prograde), 6);
        Assert.Equal(0.0, normal.Dot(radialOut), 6);
        Assert.Equal(-1.0, SasHold.Direction(vessel, earth, SasHoldMode.Retrograde)!.Value.Dot(prograde), 6);
        Assert.Null(SasHold.Direction(vessel, earth, SasHoldMode.Stability));
        Assert.Null(SasHold.Direction(vessel, earth, SasHoldMode.Target));
    }

    [Fact]
    public void HoldingRetrogradeTurnsTheNoseAroundOnTheVehiclesOwnAuthority()
    {
        var (universe, vessel, earth) = InLowOrbit();
        // Nose starts prograde-ish (+Y vs velocity +Forward: 90° off); hold retrograde.
        for (int step = 0; step < 50 * 240; step++)
        {
            var direction = SasHold.Direction(vessel, earth, SasHoldMode.Retrograde)!.Value;
            vessel.PitchYawRoll = SasHold.Command(vessel, earth, direction);
            universe.Tick(0.02);
        }
        var nose = vessel.Orientation.Rotate(Vector3d.Up);
        var retrograde = SasHold.Direction(vessel, earth, SasHoldMode.Retrograde)!.Value;
        Assert.True(nose.Dot(retrograde) > System.Math.Cos(3.0 * MathUtils.DEG_TO_RAD),
            $"Nose {System.Math.Acos(System.Math.Clamp(nose.Dot(retrograde), -1, 1)) * MathUtils.RAD_TO_DEG:F1}° off retrograde.");
        Assert.True(vessel.AngularVelocity.Magnitude < 0.01);
    }
}
