namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Persistence;
using Xunit;

/// <summary>
/// Free-flight parachute scenarios: the pilot only arms the sequence, and the integrated
/// simulation opens each canopy inside its declared envelope.
/// </summary>
public sealed class ArmedParachuteScenarioTests
{
    [Theory]
    [InlineData("mercury_capsule_freedom7")]
    [InlineData("gemini8_reentry_module")]
    [InlineData("apollo11_command_module_csm107")]
    public void ArmedCapsuleOpensItsCanopyAndSplashesDownIntact(string partId)
    {
        var (universe, earth, vessel) = Capsule(partId, altitude: 15_000.0, verticalSpeed: -150.0);
        vessel.ParachutesArmed = true;

        for (int step = 0; step < 50 * 1_200 && !vessel.IsDestroyed && !vessel.IsSurfaceSettled; step++)
            universe.Tick(0.02);

        Assert.True(vessel.HasDeployedParachute);
        Assert.False(vessel.IsDestroyed, $"{partId} struck at {vessel.CrashImpactSpeed:F1} m/s.");
        Assert.True(vessel.IsSurfaceSettled);
    }

    [Fact]
    public void UnarmedCapsuleStillStrikesTheSurface()
    {
        var (universe, _, vessel) = Capsule("mercury_capsule_freedom7", altitude: 3_000.0, verticalSpeed: -150.0);
        for (int step = 0; step < 50 * 300 && !vessel.IsDestroyed && !vessel.IsSurfaceSettled; step++)
            universe.Tick(0.02);

        Assert.False(vessel.HasDeployedParachute);
        Assert.True(vessel.IsDestroyed);
    }

    [Fact]
    public void ArmedCanopyDoesNotOpenWhileClimbing()
    {
        var (universe, _, vessel) = Capsule("mercury_capsule_freedom7", altitude: 2_000.0, verticalSpeed: 80.0);
        vessel.ParachutesArmed = true;
        for (int step = 0; step < 50; step++)
            universe.Tick(0.02);

        Assert.False(vessel.HasDeployedParachute);
    }

    [Fact]
    public void ArmingSurvivesSaveAndLoad()
    {
        var (universe, _, vessel) = Capsule("mercury_capsule_freedom7", altitude: 20_000.0, verticalSpeed: -100.0);
        vessel.ParachutesArmed = true;

        var decoded = SaveGameV2Json.DeserializeOrMigrate(
            SaveGameV2Json.Serialize(SaveGameV2Codec.Capture(universe)));
        var restored = Universe.LoadFromDataDirectory(DataDirectory());
        SaveGameV2Codec.Restore(restored, decoded,
            PartCatalog.LoadFromDirectory(Path.Combine(DataDirectory(), "parts")));

        Assert.True(Assert.Single(restored.Vessels).ParachutesArmed);
    }

    private static (Universe Universe, CelestialBody Earth, Vessel Vessel) Capsule(
        string partId, double altitude, double verticalSpeed)
    {
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        var earth = universe.GetBody("earth")!;
        var definition = PartDefinition.LoadFromJson(
            Path.Combine(DataDirectory(), "parts", partId + ".json"));
        var vessel = new Vessel(partId) { SASEnabled = false, ReferenceBodyId = earth.Id };
        var capsule = new Part(definition);
        vessel.Parts.SetRoot(capsule);
        vessel.Parts.AddPart(capsule);
        var start = earth.GetSurfacePosition(20.0, -60.0, altitude);
        var up = earth.GetGeodeticUp(start);
        vessel.Position = start;
        vessel.Velocity = earth.Velocity + earth.GetSurfaceVelocity(start) + up * verticalSpeed;
        vessel.Orientation = Quaterniond.FromTo(Vector3d.Up, up);
        universe.AddVessel(vessel);
        universe.ActiveVessel = vessel;
        return (universe, earth, vessel);
    }

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data", "bodies")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }
}
