namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Persistence;
using Xunit;

/// <summary>
/// Header tanks embedded in a part: their propellant sits at its own station (centre of
/// mass and inertia), feeds the engines only once the main tanks are empty, conserves mass,
/// and survives a save. Analytic gates of fidelity phase F2.
/// </summary>
[Trait("FidelityGate", "F2")]
[Trait("GateKind", "analytic")]
public sealed class HeaderTankTests
{
    private const double HeaderKg = 2_000.0;
    private const double HeaderStation = 4.0;

    private static PartGraph Stack(out Part nose, out Part tank, out Part engine, double mainFuel = 1_000.0, double mainOx = 3_000.0)
    {
        nose = new Part(new PartDefinition
        {
            Id = "nose", CategoryStr = "command", MassDry = 1_000.0, LengthM = 10.0, DiameterM = 3.0,
            HeaderTanks = [new HeaderTankDef { Id = "lox", Propellant = "oxidizer", CapacityKg = HeaderKg, CenterYM = HeaderStation }],
            AttachmentNodes =
            [
                new AttachmentNodeDef { Id = "bottom", Position = [0.0, -5.0, 0.0], Size = 2 },
            ],
        }, "nose");
        tank = new Part(new PartDefinition
        {
            Id = "tank", CategoryStr = "fuel_tank", MassDry = 500.0, LengthM = 10.0, DiameterM = 3.0,
            FuelCapacityLF = mainFuel, FuelCapacityOx = mainOx,
            HeaderTanks = [new HeaderTankDef { Id = "ch4", Propellant = "liquid_fuel", CapacityKg = HeaderKg / 4.0, CenterYM = 0.0 }],
            AttachmentNodes =
            [
                new AttachmentNodeDef { Id = "top", Position = [0.0, 5.0, 0.0], Size = 2 },
                new AttachmentNodeDef { Id = "bottom", Position = [0.0, -5.0, 0.0], Size = 2 },
            ],
        }, "tank");
        engine = new Part(new PartDefinition
        {
            Id = "engine", CategoryStr = "engine", MassDry = 300.0, LengthM = 2.0, DiameterM = 3.0,
            ThrustSL = 100_000.0, ThrustVac = 100_000.0, IspSL = 300.0, IspVac = 300.0,
            FuelTypeStr = "liquid_fuel", MixtureRatio = 4.0,
            AttachmentNodes = [new AttachmentNodeDef { Id = "top", Position = [0.0, 1.0, 0.0], Size = 2 }],
        }, "engine");
        var graph = new PartGraph();
        graph.SetRoot(nose);
        graph.AddJoint(new Joint(nose, tank, "bottom", "top"));
        graph.AddJoint(new Joint(tank, engine, "bottom", "top"));
        engine.ThrottleLevel = 1.0;
        return graph;
    }

    [Fact]
    public void HeaderMassSitsAtItsStation()
    {
        var graph = Stack(out var nose, out var tank, out var engine);
        Assert.True(graph.TryGetPartLocalPosition(nose, out var nosePosition));
        Assert.True(graph.TryGetPartLocalPosition(tank, out var tankPosition));
        Assert.True(graph.TryGetPartLocalPosition(engine, out var enginePosition));
        double expected = (1_000.0 * nosePosition.Y + HeaderKg * (nosePosition.Y + HeaderStation)
            + (500.0 + 4_000.0 + HeaderKg / 4.0) * tankPosition.Y + 300.0 * enginePosition.Y)
            / (1_000.0 + HeaderKg + 500.0 + 4_000.0 + HeaderKg / 4.0 + 300.0);
        Assert.Equal(expected, graph.CenterOfMass.Y, 9);
        Assert.Equal(expected, graph.GetMassProperties().CenterOfMassBody.Y, 9);
        Assert.Equal(HeaderKg * HeaderStation / nose.CurrentMass, nose.MassCenterOffsetYM, 12);
    }

    [Fact]
    public void HeaderPointMassAddsItsParallelAxisInertia()
    {
        var with = Stack(out var nose, out _, out _).GetMassProperties();
        nose.SetHeaderPropellant([0.0]);
        var graph = Stack(out var emptyNose, out _, out _);
        emptyNose.SetHeaderPropellant([0.0]);
        // Same structure with the header empty, plus the header as a point mass at its station.
        var without = graph.GetMassProperties();
        Assert.True(graph.TryGetPartLocalPosition(emptyNose, out var nosePosition));
        var station = new Vector3d(0.0, nosePosition.Y + HeaderStation, 0.0);
        double m0 = without.Mass;
        var c = (without.CenterOfMassBody * m0 + station * HeaderKg) / (m0 + HeaderKg);
        double d0 = (without.CenterOfMassBody - c).Y;
        double d1 = (station - c).Y;
        double expected = without.InertiaBody.M11 + m0 * d0 * d0 + HeaderKg * d1 * d1;
        Assert.Equal(expected, with.InertiaBody.M11, 6);
    }

    [Fact]
    public void EnginesDrainTheMainTanksBeforeTheHeaders()
    {
        var graph = Stack(out var nose, out var tank, out _, mainFuel: 50.0, mainOx: 200.0);
        double startMass = graph.TotalMass;
        graph.ConsumePropellant(0.5, 0.0);
        Assert.Equal(HeaderKg, nose.HeaderOxidizer, 12);
        Assert.Equal(HeaderKg / 4.0, tank.HeaderLiquidFuel, 12);
        double consumedMain = startMass - graph.TotalMass;
        Assert.True(consumedMain > 0.0 && tank.LiquidFuel < 50.0);

        for (int i = 0; i < 40; i++) graph.ConsumePropellant(0.5, 0.0);
        Assert.Equal(0.0, tank.LiquidFuel, 9);
        Assert.Equal(0.0, tank.Oxidizer, 9);
        Assert.True(nose.HeaderOxidizer < HeaderKg, "headers must feed once the main tanks are empty");
        Assert.True(tank.HeaderLiquidFuel < HeaderKg / 4.0);
        // Mass is conserved: what left the vehicle is exactly what the engine burned.
        double burned = (50.0 + 200.0 + HeaderKg + HeaderKg / 4.0)
            - (tank.LiquidFuel + tank.Oxidizer + nose.HeaderOxidizer + tank.HeaderLiquidFuel);
        Assert.Equal(startMass - burned, graph.TotalMass, 9);
    }

    [Fact]
    public void HeaderLoadsSurviveASave()
    {
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(FindRepoRoot().FullName, "data", "parts"));
        var vessel = new Vessel { Name = "HeaderSave" };
        var command = new Part(catalog["starship_v3_command_flight12"]);
        vessel.Parts.SetRoot(command);
        vessel.Parts.AddPart(command);
        Assert.Single(command.HeaderPropellantKg);
        command.SetHeaderPropellant([1_234.5]);
        var universe = new Universe();
        universe.AddVessel(vessel);
        var restored = new Universe();
        SaveGameV2Codec.Restore(restored, SaveGameV2Json.DeserializeOrMigrate(
            SaveGameV2Json.Serialize(SaveGameV2Codec.Capture(universe))), catalog);
        var part = restored.Vessels.Single().Parts.Parts.Single();
        Assert.Equal(1_234.5, part.HeaderOxidizer, 12);
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
