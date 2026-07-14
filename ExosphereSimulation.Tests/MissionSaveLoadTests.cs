namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Persistence;

public class MissionSaveLoadTests
{
    [Fact]
    public void CreateWithId_PreservesStableIdentity()
    {
        var vessel = Vessel.CreateWithId("stable-vessel-id");
        Assert.Equal("stable-vessel-id", vessel.Id);
    }

    [Fact]
    public void SetCurrentTime_RestoresEpochAndPropagatesBodies()
    {
        var universe = Universe.LoadFromDataDirectory(Path.Combine(FindRepoRoot(), "data"));
        universe.SetCurrentTime(3600.0);
        Assert.Equal(3600.0, universe.CurrentTime);

        var earth = universe.GetBody("earth");
        Assert.NotNull(earth);
        // Earth has moved on its heliocentric orbit since t=0.
        Assert.True(earth!.Position.Magnitude > 1e10);
    }

    [Fact]
    public void MidOrbitRoundtrip_RestoresKinematicsFuelTimeAndPhase()
    {
        string dataRoot = Path.Combine(FindRepoRoot(), "data");
        string partsDir = Path.Combine(dataRoot, "parts");
        var defs = PartDefinition.LoadAllFromDirectory(partsDir);

        var universe = Universe.LoadFromDataDirectory(dataRoot);
        universe.SetCurrentTime(12_345.678);

        var vessel = BuildFlight7OrbitVessel(defs, universe);
        string originalId = vessel.Id;
        double fuelBefore = vessel.Parts.TotalLiquidFuel;
        double oxBefore = vessel.Parts.TotalOxidizer;
        var posBefore = vessel.Position;
        var velBefore = vessel.Velocity;
        var oriBefore = vessel.Orientation;
        double timeBefore = universe.CurrentTime;

        // Partially burn propellant so restore must not reload full tanks.
        foreach (var tank in vessel.Parts.Parts.Where(p => p.Definition.FuelCapacityLF > 0))
        {
            tank.LiquidFuel *= 0.37;
            tank.Oxidizer *= 0.37;
        }
        fuelBefore = vessel.Parts.TotalLiquidFuel;
        oxBefore = vessel.Parts.TotalOxidizer;

        var saved = MissionSaveSerializer.Capture(universe, missionPhase: "ORBIT", warpIndex: 4);

        // Mutate live universe to prove restore overwrites.
        universe.SetCurrentTime(1.0);
        vessel.Position = Vector3d.Zero;
        vessel.Velocity = Vector3d.Zero;
        vessel.Throttle = 1.0;
        foreach (var p in vessel.Parts.Parts)
        {
            p.LiquidFuel = 0.0;
            p.Oxidizer = 0.0;
        }

        string json = System.Text.Json.JsonSerializer.Serialize(saved);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<MissionSaveState>(json);
        Assert.NotNull(loaded);

        MissionSaveSerializer.Restore(universe, loaded!, defs);

        Assert.Equal(timeBefore, universe.CurrentTime, precision: 9);
        Assert.Equal("ORBIT", loaded!.MissionPhase);
        Assert.Equal(4, loaded.WarpIndex);

        var restored = universe.ActiveVessel;
        Assert.NotNull(restored);
        Assert.Equal(originalId, restored!.Id);
        Assert.Equal(originalId, universe.ActiveVessel?.Id);
        Assert.False(restored.IsGroundHeld);
        Assert.Equal(0.0, restored.Throttle);
        Assert.True(restored.SASEnabled);
        Assert.Equal("earth", restored.ReferenceBodyId);
        Assert.Equal(5, restored.Parts.Parts.Count); // Flight7 without landing gear in helper
        Assert.Equal(4, restored.Parts.Joints.Count);

        Assert.Equal(posBefore.X, restored.Position.X, precision: 6);
        Assert.Equal(posBefore.Y, restored.Position.Y, precision: 6);
        Assert.Equal(posBefore.Z, restored.Position.Z, precision: 6);
        Assert.Equal(velBefore.X, restored.Velocity.X, precision: 6);
        Assert.Equal(velBefore.Y, restored.Velocity.Y, precision: 6);
        Assert.Equal(velBefore.Z, restored.Velocity.Z, precision: 6);
        Assert.Equal(oriBefore.W, restored.Orientation.W, precision: 9);
        Assert.Equal(oriBefore.X, restored.Orientation.X, precision: 9);
        Assert.Equal(oriBefore.Y, restored.Orientation.Y, precision: 9);
        Assert.Equal(oriBefore.Z, restored.Orientation.Z, precision: 9);

        Assert.Equal(fuelBefore, restored.Parts.TotalLiquidFuel, precision: 6);
        Assert.Equal(oxBefore, restored.Parts.TotalOxidizer, precision: 6);

        // Orbit shape: apoapsis/periapsis within ~1 m/s circular-velocity tolerance.
        var earth = universe.GetBody("earth")!;
        var relPos = restored.Position - earth.Position;
        var relVel = restored.Velocity - earth.Velocity;
        var oe = OrbitalElements.FromStateVector(
            relPos, relVel, earth.GM, earth.Id, universe.CurrentTime);
        Assert.InRange(oe.Eccentricity, 0.0, 0.02);
        Assert.InRange(oe.Periapsis - earth.Radius, 190_000.0, 210_000.0);
        Assert.InRange(oe.Apoapsis - earth.Radius, 190_000.0, 210_000.0);
    }

    [Fact]
    public void Roundtrip_PreservesStagingFlagsAndGroundHold()
    {
        string dataRoot = Path.Combine(FindRepoRoot(), "data");
        string partsDir = Path.Combine(dataRoot, "parts");
        var defs = PartDefinition.LoadAllFromDirectory(partsDir);
        var universe = Universe.LoadFromDataDirectory(dataRoot);

        var vessel = Vessel.CreateWithId("pad-held");
        vessel.Name = "Pad Stack";
        var command = new Part(defs["starship_command"]);
        var tank = new Part(defs["starship_tank"]);
        var engines = new Part(defs["starship_engines"]);
        var ring = new Part(defs["decoupler_heavy"]);
        var booster = new Part(defs["super_heavy_booster"]);
        ring.IsStagingActive = false;
        engines.Temperature = 412.5;

        vessel.Parts.SetRoot(command);
        vessel.Parts.AddJoint(new Joint(command, tank, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(tank, engines, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(engines, ring, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(ring, booster, "bottom", "top"));
        vessel.IsGroundHeld = true;
        var groundNormal = new Vector3d(0.1, 0.9, 0.1).Normalized;
        vessel.GroundNormal = groundNormal;
        vessel.GroundOffset = 42.0;
        vessel.Throttle = 0.25;
        vessel.SASEnabled = false;

        universe.AddVessel(vessel);
        universe.ActiveVessel = vessel;

        var state = MissionSaveSerializer.Capture(universe, "PRE_LAUNCH", warpIndex: 0);
        MissionSaveSerializer.Restore(universe, state, defs);

        var restored = universe.ActiveVessel!;
        Assert.True(restored.IsGroundHeld);
        Assert.Equal(42.0, restored.GroundOffset);
        Assert.Equal(0.25, restored.Throttle);
        Assert.False(restored.SASEnabled);
        Assert.Equal(groundNormal.X, restored.GroundNormal.X, precision: 6);
        Assert.Equal(groundNormal.Y, restored.GroundNormal.Y, precision: 6);
        Assert.Equal(groundNormal.Z, restored.GroundNormal.Z, precision: 6);

        var restoredRing = restored.Parts.Parts.First(p => p.Definition.Id == "decoupler_heavy");
        Assert.False(restoredRing.IsStagingActive);
        var restoredEngines = restored.Parts.Parts.First(p => p.Definition.Id == "starship_engines");
        Assert.Equal(412.5, restoredEngines.Temperature);
    }

    private static Vessel BuildFlight7OrbitVessel(
        IReadOnlyDictionary<string, PartDefinition> defs,
        Universe universe)
    {
        var earth = universe.GetBody("earth")
            ?? throw new InvalidOperationException("Earth missing from universe.");

        var vessel = Vessel.CreateWithId("flight7-orbit");
        vessel.Name = "Starship Flight 7";

        var command = new Part(defs["starship_command"]);
        var tank = new Part(defs["starship_tank"]);
        var engines = new Part(defs["starship_engines"]);
        var ring = new Part(defs["decoupler_heavy"]);
        var booster = new Part(defs["super_heavy_booster"]);

        vessel.Parts.SetRoot(command);
        vessel.Parts.AddJoint(new Joint(command, tank, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(tank, engines, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(engines, ring, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(ring, booster, "bottom", "top"));

        double altitude = 200_000.0;
        var up = new Vector3d(0, 1, 0);
        double r = earth.Radius + altitude;
        vessel.Position = earth.Position + up * r;
        var tangent = new Vector3d(1, 0, 0);
        double vCirc = System.Math.Sqrt(earth.GM / r);
        vessel.Velocity = earth.Velocity + tangent * vCirc;
        vessel.Orientation = Quaterniond.FromTo(Vector3d.Up, up);
        vessel.IsGroundHeld = false;
        vessel.IsOnRails = false;
        vessel.ReferenceBodyId = earth.Id;
        vessel.Throttle = 0.0;
        vessel.SASEnabled = true;

        universe.AddVessel(vessel);
        universe.ActiveVessel = vessel;
        return vessel;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data"))
                && File.Exists(Path.Combine(dir.FullName, "ExosphereSimulation.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
