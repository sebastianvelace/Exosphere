namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Xunit;

/// <summary>
/// Load-path scenarios for the quasi-static joint model: thrust applied at the engines
/// compresses every interface by the mass that interface pushes.
/// </summary>
public sealed class StructuralLoadPathTests
{
    [Fact]
    public void BoosterThrustCompressesEachInterfaceByTheMassAboveIt()
    {
        var vessel = BuildFlight7();
        var booster = vessel.Parts.Parts.Single(p => p.Definition.Id == "super_heavy_booster");
        for (int step = 0; step < 300; step++)
            booster.AdvanceEngineRuntime(1.0, 0.02);
        double thrust = booster.GetThrustVector(0.0).Y;
        Assert.True(thrust > 1e7);
        double acceleration = thrust / vessel.TotalMass;

        StressSolver.ComputeLoads(vessel.Parts, new Vector3d(0.0, acceleration, 0.0),
            Quaterniond.Identity);

        foreach (var joint in vessel.Parts.Joints)
        {
            double massAbove = MassOfSideWithout(vessel.Parts, joint, booster);
            Assert.Equal(massAbove * acceleration, joint.CurrentCompressiveLoad,
                massAbove * acceleration * 1e-9 + 1e-3);
            Assert.Equal(0.0, joint.CurrentTensileLoad, 6);
            Assert.Equal(0.0, joint.CurrentShearLoad, 6);
        }

        var noseJoint = vessel.Parts.Joints.Single(j => j.Parent == vessel.Parts.Root);
        Assert.True(noseJoint.CurrentCompressiveLoad < 0.02 * thrust,
            "The nose interface must only push the nose, not the whole stack.");
    }

    [Fact]
    public void CompressiveOverloadIsAFailureMode()
    {
        var vessel = BuildFlight7();
        var booster = vessel.Parts.Parts.Single(p => p.Definition.Id == "super_heavy_booster");
        for (int step = 0; step < 300; step++)
            booster.AdvanceEngineRuntime(1.0, 0.02);
        var interstage = vessel.Parts.Joints.Single(j => j.Child == booster);
        interstage.CompressiveStrength = 1.0;
        interstage.TensileStrength = double.MaxValue;

        StressSolver.ComputeLoads(vessel.Parts,
            new Vector3d(0.0, booster.GetThrustVector(0.0).Y / vessel.TotalMass, 0.0),
            Quaterniond.Identity);

        Assert.Contains(interstage, StressSolver.FindBreakingJoints(vessel.Parts));
    }

    private static double MassOfSideWithout(PartGraph graph, Joint joint, Part engine)
    {
        var childSide = new HashSet<Part>();
        void Collect(Part part)
        {
            childSide.Add(part);
            foreach (var child in graph.GetChildren(part)) Collect(child);
        }
        Collect(joint.Child);
        var pushed = childSide.Contains(engine)
            ? graph.Parts.Where(p => !childSide.Contains(p))
            : childSide;
        return pushed.Sum(p => p.CurrentMass);
    }

    private static Vessel BuildFlight7()
    {
        var root = DataDirectory();
        var variant = VehicleVariantDefinition.LoadFromJson(
            Path.Combine(root, "vehicles", "starship_flight7_block2_2025.json"));
        return variant.Build(PartCatalog.LoadFromDirectory(Path.Combine(root, "parts")))
            .ToVessel("load-path", "load-path");
    }

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data", "bodies")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }
}
