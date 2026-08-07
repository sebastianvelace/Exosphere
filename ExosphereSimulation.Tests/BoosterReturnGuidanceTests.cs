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
    public void OutboundHorizontalSpeedIsPositiveWhenFleeingPad()
    {
        var up = Vector3d.Up;
        var pad = new Vector3d(0.0, 6_400_000.0, 0.0);
        var vessel = new Vector3d(50_000.0, 6_400_000.0, 0.0);
        var eastbound = new Vector3d(2000.0, 0.0, 0.0);
        var inbound = new Vector3d(-500.0, 0.0, 0.0);

        double outbound = BoosterReturnGuidance.OutboundHorizontalSpeedMps(
            eastbound, up, pad, vessel);
        double returning = BoosterReturnGuidance.OutboundHorizontalSpeedMps(
            inbound, up, pad, vessel);

        Assert.True(outbound > 1500.0, $"expected strong outbound, got {outbound}");
        Assert.True(returning < 0.0, $"expected inbound (negative), got {returning}");
    }

    [Fact]
    public void BoostbackCutoffWhenOutboundAndFuelCrossThresholds()
    {
        Assert.True(BoosterReturnGuidance.ShouldContinueBoostback(500.0, 0.05));
        Assert.False(BoosterReturnGuidance.ShouldContinueBoostback(
            BoosterReturnGuidance.BoostbackCutoffOutboundMps - 1.0, 0.05));
        Assert.False(BoosterReturnGuidance.ShouldContinueBoostback(
            500.0, BoosterReturnGuidance.BoostbackMinFuelFraction - 0.001));
    }

    [Fact]
    public void EntryBurnArmsBelowThresholdAndHandsOffToCatchAltitude()
    {
        Assert.False(BoosterReturnGuidance.ShouldArmEntryBurn(
            BoosterReturnGuidance.EntryBurnArmAltitudeM + 100.0, hasCatchPins: true));
        Assert.True(BoosterReturnGuidance.ShouldArmEntryBurn(
            BoosterReturnGuidance.EntryBurnArmAltitudeM - 1.0, hasCatchPins: true));
        Assert.False(BoosterReturnGuidance.ShouldArmEntryBurn(1000.0, hasCatchPins: false));

        Assert.True(BoosterReturnGuidance.ShouldContinueEntryBurn(
            BoosterReturnGuidance.CatchBurnAltitudeM + 10.0));
        Assert.False(BoosterReturnGuidance.ShouldContinueEntryBurn(
            BoosterReturnGuidance.CatchBurnAltitudeM - 1.0));
    }

    [Fact]
    public void Flight7BoosterBoostbackBudgetMatchesIftDeltaVBand()
    {
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(Root.FullName, "data", "parts"));
        var def = catalog["super_heavy_booster"];
        double propellant = def.FuelCapacityLF + def.FuelCapacityOx;

        double dv = BoosterReturnGuidance.EstimateBoostbackBudgetDeltaVMps(
            def.MassDry, propellant, def.IspVac);

        Assert.InRange(dv,
            BoosterReturnGuidance.IftBoostbackDeltaVMinMps,
            BoosterReturnGuidance.IftBoostbackDeltaVMaxMps);
        // Sanity: MECO reserve window must leave catch propellant (end < start).
        Assert.True(BoosterReturnGuidance.BoostbackMinFuelFraction
            < AscentStagingPolicy.BoosterReserveFraction);
    }

    [Fact]
    public void EntryBurnUsesThirteenEnginesThenCatchUsesThree()
    {
        Assert.Equal(13, BoosterReturnGuidance.EntryBurnEngineCount);
        Assert.Equal(13, BoosterReturnGuidance.BoostbackEngineCount);
        Assert.Equal(3, BoosterReturnGuidance.CatchEngineCount);
        Assert.True(BoosterReturnGuidance.EntryBurnArmAltitudeM
            > BoosterReturnGuidance.CatchBurnAltitudeM);
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
