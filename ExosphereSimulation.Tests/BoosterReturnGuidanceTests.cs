namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Xunit;

public sealed class BoosterReturnGuidanceTests
{
    private static readonly DirectoryInfo Root = FindRepoRoot();

    [Fact]
    public void BoostbackThrustOpposesDownrangeAndBiasesTowardPad()
    {
        var up = Vector3d.Up;
        var surfVel = new Vector3d(2000.0, 0.0, 0.0); // eastbound downrange
        var vesselPos = new Vector3d(50_000.0, 6_400_000.0, 0.0);
        var padPos = new Vector3d(0.0, 6_400_000.0, 0.0);

        var dir = BoosterReturnGuidance.BoostbackThrustDirection(
            surfVel, up, padPos, vesselPos);

        Assert.True(dir.Magnitude > 0.99);
        // Primary component kills eastbound velocity → thrust has −X.
        Assert.True(dir.X < -0.5, $"expected westward thrust, got {dir}");
        // Bias toward pad (also −X here) keeps the sign; no large vertical demand.
        Assert.True(System.Math.Abs(dir.Y) < 0.5);
    }

    [Fact]
    public void BoostbackCutoffWhenHorizontalSpeedAndFuelCrossThresholds()
    {
        Assert.True(BoosterReturnGuidance.ShouldContinueBoostback(500.0, 0.05));
        Assert.False(BoosterReturnGuidance.ShouldContinueBoostback(
            BoosterReturnGuidance.BoostbackCutoffHorizontalMps - 1.0, 0.05));
        Assert.False(BoosterReturnGuidance.ShouldContinueBoostback(
            500.0, BoosterReturnGuidance.BoostbackMinFuelFraction - 0.001));
    }

    [Fact]
    public void Flight7BoosterDeclaresCatchPinsAndDebrisInheritsThem()
    {
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(Root.FullName, "data", "parts"));
        var boosterPart = new Part(catalog["super_heavy_booster"]);
        Assert.True(boosterPart.Definition.CatchPinLateralOffsetM > 0.0);

        var stack = new Vessel { Name = "stack" };
        var command = new Part(catalog["starship_command"]);
        var tank = new Part(catalog["starship_tank"]);
        var engines = new Part(catalog["starship_engines"]);
        var ring = new Part(catalog["decoupler_heavy"]);
        stack.Parts.SetRoot(command);
        stack.Parts.AddJoint(new Joint(command, tank, "bottom", "top"));
        stack.Parts.AddJoint(new Joint(tank, engines, "bottom", "top"));
        stack.Parts.AddJoint(new Joint(engines, ring, "bottom", "top"));
        stack.Parts.AddJoint(new Joint(ring, boosterPart, "bottom", "top"));

        var debris = stack.Stage();
        Assert.NotNull(debris);
        Assert.True(BoosterReturnGuidance.IsStarshipBooster(debris!));
        Assert.True(debris!.HasCatchPins,
            "staged Super Heavy debris must configure catch pins for R12");
    }

    [Fact]
    public void IsStarshipBoosterRejectsShipOnlyStack()
    {
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(Root.FullName, "data", "parts"));
        var ship = new Vessel { Name = "ship" };
        var command = new Part(catalog["starship_command"]);
        var tank = new Part(catalog["starship_tank"]);
        var engines = new Part(catalog["starship_engines"]);
        ship.Parts.SetRoot(command);
        ship.Parts.AddJoint(new Joint(command, tank, "bottom", "top"));
        ship.Parts.AddJoint(new Joint(tank, engines, "bottom", "top"));

        Assert.False(BoosterReturnGuidance.IsStarshipBooster(ship));
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
                return directory;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }
}
