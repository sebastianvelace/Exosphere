using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;

namespace ExosphereSimulation.Tests;

public sealed class StructuralBreakupTests
{
    [Fact]
    public void FindBreakingJoints_EmptyUnderMildAcceleration()
    {
        var (vessel, _) = BuildTwoStageStack();
        int jointsBefore = vessel.Parts.Joints.Count;

        // ~3 g proper accel — within Flight 7 / Max-Q envelope for size-3 stack joints.
        StressSolver.ComputeLoads(vessel.Parts, new Vector3d(0.0, 30.0, 0.0), Quaterniond.Identity);
        Assert.Empty(StressSolver.FindBreakingJoints(vessel.Parts));
        Assert.Equal(jointsBefore, vessel.Parts.Joints.Count);
    }

    [Fact]
    public void FindBreakingJoints_OverloadThenBreakAtJoint_DetachesSubtree()
    {
        var (vessel, boosterJoint) = BuildTwoStageStack();
        int jointsBefore = vessel.Parts.Joints.Count;
        int partsBefore = vessel.Parts.Parts.Count;

        // Absurd lateral accel forces shear overload regardless of Flight 7 margins.
        StressSolver.ComputeLoads(vessel.Parts, new Vector3d(1e6, 0.0, 0.0), Quaterniond.Identity);
        var breaking = StressSolver.FindBreakingJoints(vessel.Parts).ToList();
        Assert.NotEmpty(breaking);
        Assert.Contains(boosterJoint, breaking);

        vessel.BeginHotStageOverlap(1.5);
        Assert.True(vessel.IsHotStageOverlapping);

        var debris = vessel.BreakAtJoint(boosterJoint);
        Assert.NotNull(debris);
        Assert.Contains(debris!.Parts.Parts, p => p.Definition.Id == "super_heavy_booster");
        Assert.DoesNotContain(vessel.Parts.Parts, p => p.Definition.Id == "super_heavy_booster");
        Assert.True(vessel.Parts.Joints.Count < jointsBefore);
        Assert.True(vessel.Parts.Parts.Count < partsBefore);
        Assert.False(vessel.IsDestroyed);
        Assert.Equal(VesselDestructionCause.None, vessel.DestructionCause);
        Assert.False(vessel.Parts.HotStageOverlapActive);
        Assert.Equal(0.0, vessel.HotStageOverlapRemaining);
        Assert.False(vessel.HotStageOverlapCompletedPending);
    }

    [Fact]
    public void UniverseTick_OverloadedJoint_SpawnsStructuralDebris()
    {
        var earth = LoadEarth();
        var (vessel, _) = BuildTwoStageStack();
        // Weaken one joint so a single physics step with thrust breaks it without
        // needing an impossible whole-vehicle acceleration from thrust alone.
        var weak = vessel.Parts.Joints.First(j => j.Child.Definition.Id == "super_heavy_booster");
        weak.TensileStrength = 1.0;
        weak.ShearStrength = 1.0;

        vessel.Position = earth.Position + Vector3d.Right * (earth.Radius + 120_000.0);
        vessel.Velocity = earth.Velocity + earth.GetSurfaceVelocity(vessel.Position);
        vessel.Orientation = Quaterniond.Identity;
        vessel.Throttle = 1.0;
        foreach (var engine in vessel.Parts.Parts.Where(p => p.Definition.Category == PartCategory.Engine))
            engine.ThrottleLevel = 1.0;

        var universe = new Universe();
        universe.AddBody(earth);
        universe.AddVessel(vessel);
        universe.ActiveVessel = vessel;
        universe.TimeScale = 1.0;

        int jointsBefore = vessel.Parts.Joints.Count;
        for (int i = 0; i < 5; i++)
            universe.Tick(0.02);

        var debris = universe.DrainPendingStructuralDebris();
        Assert.NotEmpty(debris);
        Assert.Contains(universe.Vessels, v => v != vessel);
        Assert.True(vessel.Parts.Joints.Count < jointsBefore);
        Assert.DoesNotContain(vessel.Parts.Parts, p => p.Definition.Id == "super_heavy_booster");
        Assert.False(vessel.IsDestroyed);
    }

    [Fact]
    public void SplitAtJoint_RejectsUnknownOrRootJoint()
    {
        var (vessel, boosterJoint) = BuildTwoStageStack();
        var foreign = new Joint(
            new Part(LoadPart("starship_command")),
            new Part(LoadPart("starship_tank")),
            "bottom", "top");

        Assert.Null(vessel.Parts.SplitAtJoint(foreign));

        // Cannot detach the root via a synthetic parent→root edge.
        var root = vessel.Parts.Root!;
        var orphanParent = new Part(LoadPart("starship_tank"));
        var badRootJoint = new Joint(orphanParent, root, "bottom", "top");
        vessel.Parts.AddJoint(badRootJoint);
        Assert.Null(vessel.Parts.SplitAtJoint(badRootJoint));

        // Valid booster joint still works.
        Assert.NotNull(vessel.Parts.SplitAtJoint(boosterJoint));
    }

    [Fact]
    public void NominalFlight7Loads_DoNotBreakJoints()
    {
        var (vessel, _, _, _, _) = BuildFlight7Stack();
        // Proper accel envelope roughly covering liftoff (~1.5 g) through near-MECO (~3–4 g)
        // and mild aero lateral (~few m/s²). Documented: nominal EDL / [G] ascent must not split.
        StressSolver.ComputeLoads(
            vessel.Parts, new Vector3d(5.0, 40.0, 0.0), Quaterniond.Identity);
        Assert.Empty(StressSolver.FindBreakingJoints(vessel.Parts));
    }

    [Fact]
    public void NominalEdlLikeAeroAccel_DoesNotBreakShipOnlyStack()
    {
        // Ship-only stack (post-stage) under belly-flop class decelerations (~2–3 g aero).
        // Optional regression: R13 belly-flop must NOT tear joints.
        var defs = LoadDefs();
        var command = new Part(defs["starship_command"]);
        var tank = new Part(defs["starship_tank"]);
        var engines = new Part(defs["starship_engines"]);
        var vessel = new Vessel { Name = "Ship EDL" };
        vessel.Parts.SetRoot(command);
        vessel.Parts.AddJoint(new Joint(command, tank, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(tank, engines, "bottom", "top"));

        StressSolver.ComputeLoads(
            vessel.Parts, new Vector3d(25.0, 5.0, 10.0), Quaterniond.Identity);
        Assert.Empty(StressSolver.FindBreakingJoints(vessel.Parts));
    }

    private static (Vessel vessel, Joint boosterJoint) BuildTwoStageStack()
    {
        var (vessel, _, _, _, _) = BuildFlight7Stack();
        var boosterJoint = vessel.Parts.Joints.First(
            j => j.Child.Definition.Id == "super_heavy_booster");
        return (vessel, boosterJoint);
    }

    private static (Vessel vessel, Part booster, Part ring, Part shipEngines, Part shipTank)
        BuildFlight7Stack()
    {
        var defs = LoadDefs();

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

    private static Dictionary<string, PartDefinition> LoadDefs() =>
        PartDefinition.LoadAllFromDirectory(
            Path.Combine(FindRepoRoot().FullName, "data", "parts"));

    private static PartDefinition LoadPart(string id) =>
        PartDefinition.LoadFromJson(
            Path.Combine(FindRepoRoot().FullName, "data", "parts", $"{id}.json"));

    private static CelestialBody LoadEarth() =>
        CelestialBody.LoadFromJson(
            Path.Combine(FindRepoRoot().FullName, "data", "bodies", "earth.json"));

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
