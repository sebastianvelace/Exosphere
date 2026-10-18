namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Xunit;

/// <summary>
/// Integrated descent scenarios: each historical capsule falls through the real Earth
/// atmosphere, arms its parachutes through <see cref="Vessel.DeployParachutes"/> and must
/// reach the surface below its declared splashdown limit. Evidence fixtures that merely
/// assert a splashdown flag cannot catch a canopy that never opens.
/// </summary>
public sealed class CapsuleSplashdownPhysicsTests
{
    [Theory]
    [InlineData("mercury_capsule_freedom7")]
    [InlineData("mercury_capsule_friendship7")]
    [InlineData("gemini8_reentry_module")]
    [InlineData("apollo8_command_module_csm103")]
    [InlineData("apollo11_command_module_csm107")]
    public void CapsuleDeploysParachutesAndSplashesDownBelowItsLimit(string partId)
    {
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        var earth = universe.GetBody("earth")!;
        var definition = PartDefinition.LoadFromJson(
            Path.Combine(DataDirectory(), "parts", partId + ".json"));
        var vessel = new Vessel(partId) { SASEnabled = false, ReferenceBodyId = earth.Id };
        var capsule = new Part(definition);
        vessel.Parts.SetRoot(capsule);
        vessel.Parts.AddPart(capsule);

        var start = earth.GetSurfacePosition(20.0, -60.0, 15_000.0);
        var up = earth.GetGeodeticUp(start);
        vessel.Position = start;
        vessel.Velocity = earth.Velocity + earth.GetSurfaceVelocity(start) - up * 150.0;
        vessel.Orientation = Quaterniond.FromTo(Vector3d.Up, up);
        universe.AddVessel(vessel);
        universe.ActiveVessel = vessel;

        int deployed = 0;
        for (int step = 0; step < 50 * 1_200 && !vessel.IsDestroyed && !vessel.IsSurfaceSettled; step++)
        {
            if (deployed == 0 && vessel.GetAltitude(earth) < 7_300.0)
                deployed = vessel.DeployParachutes(earth);
            universe.Tick(0.02);
        }

        Assert.True(deployed > 0, $"{partId} never deployed its parachutes.");
        Assert.False(vessel.IsDestroyed,
            $"{partId} was destroyed at {vessel.CrashImpactSpeed:F1} m/s.");
        Assert.True(vessel.IsSurfaceSettled);
    }

    [Fact]
    public void EveryParachuteDeclaresPressureAsSeaLevelFraction()
    {
        var catalog = PartDefinition.LoadAllFromDirectory(Path.Combine(DataDirectory(), "parts"));
        Assert.All(catalog.Values.Where(part => part.DragChute > 0.0),
            part => Assert.InRange(part.MinPressureDeploy, 0.0, 1.0));
    }

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data", "bodies")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }
}
