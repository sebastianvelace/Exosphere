namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Xunit;

/// <summary>
/// B2 — dual-thrust hot-stage: Ship lights while Super Heavy is still attached, then separates.
/// </summary>
public sealed class HotStageOverlapTests
{
    [Fact]
    public void OverlapAddsUpperStageThrustWhileBoosterStillAttached()
    {
        var (vessel, booster, _, shipEngines, _) = BuildFlight7Stack();
        booster.ThrottleLevel = 1.0;
        shipEngines.ThrottleLevel = 1.0;

        double boosterOnly = vessel.Parts.GetCurrentThrust(0.0);
        Assert.Equal(33, vessel.ActiveEngineCount);

        vessel.BeginHotStageOverlap(AscentStagingPolicy.HotStageOverlapSeconds);

        Assert.True(vessel.IsHotStageOverlapping);
        Assert.True(vessel.Parts.HotStageOverlapActive);
        Assert.Equal(39, vessel.ActiveEngineCount); // 33 + 6 selected bells as engine-count sum
        Assert.Contains(shipEngines, vessel.Parts.ActiveEngines);

        double combined = vessel.Parts.GetCurrentThrust(0.0);
        Assert.True(combined > boosterOnly * 1.05,
            $"overlap must add Ship thrust (booster={boosterOnly:F0} N, combined={combined:F0} N)");
        Assert.True(shipEngines.GetThrustMagnitude(0.0) > 0.0);
    }

    [Fact]
    public void OverlapDrainsBothStageTanksIndependently()
    {
        var (vessel, booster, _, shipEngines, shipTank) = BuildFlight7Stack();
        booster.ThrottleLevel = 1.0;
        shipEngines.ThrottleLevel = 1.0;

        double boosterLf0 = booster.LiquidFuel;
        double shipLf0 = shipTank.LiquidFuel;

        vessel.BeginHotStageOverlap(1.0);
        vessel.Parts.ConsumePropellant(1.0, 0.0);

        Assert.True(booster.LiquidFuel < boosterLf0, "booster must burn its own tanks");
        Assert.True(shipTank.LiquidFuel < shipLf0, "Ship tank must burn during overlap");
        // No cross-feed: Ship tanks must not feed the booster burn ratio alone.
        Assert.True(booster.LiquidFuel > 0.0);
        Assert.True(shipTank.LiquidFuel > 0.0);
    }

    [Fact]
    public void OverlapEndsWithSingleActiveStageAfterMechanicalSeparation()
    {
        var (vessel, booster, _, shipEngines, _) = BuildFlight7Stack();
        vessel.Throttle = 1.0;
        booster.ThrottleLevel = 1.0;
        shipEngines.ThrottleLevel = 1.0;

        vessel.BeginHotStageOverlap(0.5);
        // Advance past the window without calling Stage yet.
        for (int i = 0; i < 30; i++)
            vessel.AdvanceHotStageOverlap(0.02);

        Assert.True(vessel.HotStageOverlapCompletedPending);
        Assert.False(vessel.Parts.HotStageOverlapActive);

        var debris = vessel.Stage();
        Assert.NotNull(debris);
        Assert.Contains(debris!.Parts.Parts, p => p.Definition.Id == "super_heavy_booster");
        Assert.DoesNotContain(vessel.Parts.Parts, p => p.Definition.Id == "super_heavy_booster");
        Assert.False(vessel.Parts.HotStageOverlapActive);
        Assert.Equal(0.0, vessel.HotStageOverlapRemaining);

        // After separation only Ship engines are active.
        shipEngines.ThrottleLevel = 1.0;
        Assert.Equal(6, vessel.ActiveEngineCount);
        Assert.DoesNotContain(vessel.Parts.ActiveEngines, e => e.Definition.Id == "super_heavy_booster");
    }

    [Fact]
    public void HotStagePolicyStillGatesAtFlight7SpeedBand()
    {
        Assert.True(AscentStagingPolicy.ShouldHotStageSuperHeavy(
            alreadyStaged: false,
            boosterStillAttached: true,
            surfaceSpeedMps: 2308.0,
            altitudeMeters: 65_000.0,
            remainingFuelFraction: 0.10));
        Assert.InRange(AscentStagingPolicy.HotStageOverlapSeconds, 0.5, 2.5);
    }

    private static (Vessel vessel, Part booster, Part ring, Part shipEngines, Part shipTank)
        BuildFlight7Stack()
    {
        var defs = PartDefinition.LoadAllFromDirectory(
            Path.Combine(FindRepoRoot().FullName, "data", "parts"));

        var command = new Part(defs["starship_command"]);
        var tank = new Part(defs["starship_tank"]);
        var engines = new Part(defs["starship_engines"]);
        var ring = new Part(defs["decoupler_heavy"]);
        var booster = new Part(defs["super_heavy_booster"]);

        var vessel = new Vessel { Name = "Starship Flight 7" };
        vessel.Parts.SetRoot(command);
        vessel.Parts.AddJoint(new Joint(command, tank, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(tank, engines, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(engines, ring, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(ring, booster, "bottom", "top"));
        return (vessel, booster, ring, engines, tank);
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data"))
                && File.Exists(Path.Combine(dir.FullName, "ExosphereSimulation.sln")))
                return dir;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
