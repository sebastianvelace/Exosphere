namespace ExosphereSimulation.Tests;

using System.Text.Json;
using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Persistence;
using Xunit;

/// <summary>
/// Replays the game's own single-burn ignition state (SaveGameV2 written by the EDL under
/// <c>EXOSPHERE_CATCH_IGNITION_SAVE</c>, with the universe switches beside it) on the bench, at
/// the frame rate it was captured at. The capture comes after a glide that lines the nose up
/// with the tower, so the flip throws the ship along the approach and the burn catches.
/// </summary>
public sealed class Flight15CatchIgnitionReplayTests
{
    [Theory]
    [InlineData("ignition_60hz", 1.0 / 60.0)]
    [InlineData("ignition_50hz", 1.0 / 50.0)]
    public void GameIgnitionStateIsCaughtInOneBurn(string capture, double interval)
    {
        var (universe, ship, earth, site) = Restore(capture);
        Assert.InRange(System.Math.Abs(TowerCatchBurn.BellyRollFromVerticalRad(ship.Orientation, ship.CatchTargetUpWorld)),
            0.0, 5.0 * MathUtils.DEG_TO_RAD);
        Assert.True(NoseTowardTower(ship) > 0.99, $"nose toward tower {NoseTowardTower(ship):F3}");

        var burn = new TowerCatchBurn(ship, earth, TowerCatchBurnTests.Bench.Definition(), "raptor3-starship-sl-flight12");
        for (int i = 0; i < (int)(80.0 / interval)
            && burn.Phase is not (TowerCatchBurnPhase.Caught or TowerCatchBurnPhase.Blocked); i++)
        {
            Refresh(ship, earth, site, universe.CurrentTime);
            burn.Advance(universe, interval);
            universe.Tick(interval);
        }

        string steps = string.Join(" ", burn.EngineSteps.Select(s => $"{s.EngineCount}@{s.TimeS:F1}"));
        Assert.True(burn.Phase == TowerCatchBurnPhase.Caught, $"{burn.Phase} {burn.BlockReason} steps={steps}");
        // The game caught these states in 39–41 s; the bench loop steps control and physics
        // once per frame and lands a few seconds later. The bound guards against the 78–98 s
        // catches of the closing-law guidance this replaced.
        Assert.InRange(burn.CaughtTimeS, 0.0, 50.0);
        Assert.Equal(0, burn.Relights);
        var counts = burn.EngineSteps.Select(s => s.EngineCount).ToArray();
        Assert.Equal(3, counts[0]);
        Assert.True(counts.Zip(counts.Skip(1)).All(p => p.Second < p.First), $"steps={steps}");
    }

    private static double NoseTowardTower(Vessel ship)
    {
        var up = ship.CatchTargetUpWorld.Normalized;
        var axis = ship.Orientation.Rotate(Vector3d.Up).Normalized;
        var nose = axis - up * axis.Dot(up);
        var toTower = ship.CatchTargetPositionWorld - ship.Position;
        toTower -= up * toTower.Dot(up);
        return nose.Normalized.Dot(toTower.Normalized);
    }

    private static (Universe, Vessel, CelestialBody, LaunchSite) Restore(string capture)
    {
        string data = DataDirectory();
        string fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "flight15_catch");
        if (!Directory.Exists(fixtures))
            fixtures = Path.Combine(data, "..", "ExosphereSimulation.Tests", "Fixtures", "flight15_catch");
        var env = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures, capture + ".env.json"))).RootElement;
        // The game flies a heliocentric Earth; the bench holds it at the origin and moves the
        // vessels into its frame, which leaves every body-relative quantity unchanged.
        var earth = CelestialBody.LoadFromJson(Path.Combine(data, "bodies/earth.json"));
        earth.Position = Vector3d.Zero; earth.Velocity = Vector3d.Zero; earth.OrbitalElements = null;
        var site = LaunchSite.LoadFromJson(Path.Combine(data, "launch_sites/starbase_pad2.json"));
        var universe = new Universe();
        universe.AddBody(earth);
        var save = SaveGameV2Json.DeserializeOrMigrate(File.ReadAllText(Path.Combine(fixtures, capture + ".save.json")));
        SaveGameV2Codec.Restore(universe, save, PartCatalog.LoadFromDirectory(Path.Combine(data, "parts")));
        universe.SchedulerBudgetEnabled = env.GetProperty("SchedulerBudgetEnabled").GetBoolean();
        universe.DeferredPhysicsCandidateEnabled = env.GetProperty("DeferredPhysicsCandidateEnabled").GetBoolean();
        universe.DistributedAerodynamicsEnabled = env.GetProperty("DistributedAerodynamicsEnabled").GetBoolean();
        universe.PhysicalBodyFlapsEnabled = env.GetProperty("PhysicalBodyFlapsEnabled").GetBoolean();
        universe.GravityGradientEnabled = env.GetProperty("GravityGradientEnabled").GetBoolean();
        universe.VariableMassTransportEnabled = env.GetProperty("VariableMassTransportEnabled").GetBoolean();
        universe.PropellantSloshEnabled = env.GetProperty("PropellantSloshEnabled").GetBoolean();
        var p = env.GetProperty("bodyPosition"); var v = env.GetProperty("bodyVelocity");
        var bodyPosition = new Vector3d(p[0].GetDouble(), p[1].GetDouble(), p[2].GetDouble());
        var bodyVelocity = new Vector3d(v[0].GetDouble(), v[1].GetDouble(), v[2].GetDouble());
        foreach (var vessel in universe.Vessels) { vessel.Position -= bodyPosition; vessel.Velocity -= bodyVelocity; }
        var ship = universe.ActiveVessel!;
        ship.ConfigureCatchContactsFromParts();
        ship.IsAttemptingTowerCatch = true;
        Refresh(ship, earth, site, universe.CurrentTime);
        return (universe, ship, earth, site);
    }

    private static void Refresh(Vessel ship, CelestialBody earth, LaunchSite site, double time)
    {
        var spec = LaunchComplexSpec.StarbasePostDeluge;
        var cradle = spec.GetCatchCradlePosition(site, earth, time, ship);
        ship.CatchTargetPositionWorld = cradle;
        ship.CatchTargetUpWorld = site.GetLocalFrame(earth, time).Up;
        ship.CatchTargetVelocityWorld = earth.Velocity + earth.GetSurfaceVelocity(cradle);
        ship.CatchTargetEpochSeconds = time;
        ship.CatchTargetRails = spec.GetCatchArmRails(site, earth, time);
    }

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data"))) directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }
}
