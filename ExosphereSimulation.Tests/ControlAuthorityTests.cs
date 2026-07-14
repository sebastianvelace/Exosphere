namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Parts;
using Xunit;

public sealed class ControlAuthorityTests
{
    [Fact]
    public void IntactFlight7StackHasFullAuthority()
    {
        var (vessel, _, _, _, _) = BuildFlight7Stack();
        Assert.Equal(ControlAuthority.Full, ControlAuthority.Evaluate(vessel));
        Assert.False(vessel.StructuralControlLost);
        Assert.False(ControlAuthority.IsDegraded(vessel.ControlAuthorityFactor));
    }

    [Fact]
    public void ShipOnlyStackHasFullAuthority()
    {
        var (vessel, _, _, _, _) = BuildFlight7Stack();
        _ = vessel.Stage();
        Assert.Equal(ControlAuthority.Full, ControlAuthority.Evaluate(vessel));
    }

    [Fact]
    public void RemovingCommandLeavesDeadStick()
    {
        var (vessel, _, _, _, _) = BuildFlight7Stack();
        _ = vessel.Stage();
        var command = vessel.Parts.Parts.First(p => p.Definition.Category == PartCategory.Command);
        // Break command in place: Evaluate ignores broken command parts.
        command.IsBroken = true;
        Assert.Equal(ControlAuthority.None, ControlAuthority.Evaluate(vessel));
        Assert.True(vessel.StructuralControlLost);
    }

    [Fact]
    public void EnginesGoneLeavesFlapsOnlyAuthority()
    {
        var (vessel, _, _, shipEngines, _) = BuildFlight7Stack();
        _ = vessel.Stage();
        shipEngines.IsBroken = true;
        shipEngines.IsStagingActive = false;
        Assert.Equal(ControlAuthority.FlapsOnly, ControlAuthority.Evaluate(vessel));
        Assert.True(ControlAuthority.IsDegraded(vessel.ControlAuthorityFactor));
        Assert.False(vessel.StructuralControlLost);
    }

    [Fact]
    public void TickZerosAttitudeWhenStructuralControlLost()
    {
        var vessel = new Vessel();
        vessel.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = "bare_structure",
            CategoryStr = "structure",
            MassDry = 1000.0,
        }));
        vessel.SASEnabled = true;
        vessel.PitchYawRoll = new Exosphere.Simulation.Math.Vector3d(1, 0, 0);
        vessel.Throttle = 0.5;

        var body = LoadBody("earth");
        vessel.Position = body.Position + new Exosphere.Simulation.Math.Vector3d(body.Radius + 200_000.0, 0, 0);
        vessel.Tick(0.02, body);

        Assert.True(vessel.StructuralControlLost);
        Assert.False(vessel.SASEnabled);
        Assert.Equal(0.0, vessel.PitchYawRoll.Magnitude, 6);
    }

    [Fact]
    public void BreakupThatRemovesEnginesDegradesAuthority()
    {
        var (vessel, _, _, _, _) = BuildFlight7Stack();
        // Sever command→tank: keeps command (flaps), detaches engines+booster as debris.
        var command = vessel.Parts.Root!;
        var tank = vessel.Parts.GetChildren(command).First();
        var joint = vessel.Parts.GetJoint(command, tank)!;
        joint.TensileStrength = 1.0;
        joint.ShearStrength = 1.0;
        joint.CurrentTensileLoad = 1e9;

        var debris = vessel.BreakAtJoint(joint);
        Assert.NotNull(debris);
        Assert.Equal(ControlAuthority.FlapsOnly, ControlAuthority.Evaluate(vessel));
        Assert.Contains(debris!.Parts.Parts, p => p.Definition.Id == "starship_engines"
            || p.Definition.Id == "super_heavy_booster");
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

    private static CelestialBody LoadBody(string id) =>
        CelestialBody.LoadFromJson(Path.Combine(FindRepoRoot().FullName, "data", "bodies", $"{id}.json"));

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
