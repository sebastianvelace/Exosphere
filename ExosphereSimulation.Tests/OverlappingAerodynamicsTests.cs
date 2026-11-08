namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;

public sealed class OverlappingAerodynamicsTests
{
    [Fact]
    public void OverlappingCylindersLoadTheExposedPlanformOnce()
    {
        var a = MakePart("a", 4.0, 2.0);
        var b = MakePart("b", 2.0, 2.0);
        var graph = new PartGraph();
        graph.SetRoot(a);
        graph.AddJoint(new Joint(a, b, "join", "join"));
        var elements = Build(graph, new() { [a] = new(0, 2, 0), [b] = new(0, 3, 0) });

        Assert.Equal(8.0, elements.Sum(e => e.CrossflowAreaM2), 12);
        Assert.Equal(System.Math.PI, elements.Sum(e => e.SlenderAreaChangeM2), 12);
    }

    [Fact]
    public void BuriedEngineDoesNotCreateAnExposedBoatTail()
    {
        var top = MakePart("top", 2.0, 2.0);
        var engine = MakePart("engine", 1.0, 1.0);
        var ring = MakePart("ring", 0.2, 2.0);
        var adapter = MakePart("adapter", 4.0, 4.0);
        var graph = new PartGraph();
        graph.SetRoot(top);
        graph.AddJoint(new Joint(top, engine, "join", "join"));
        graph.AddJoint(new Joint(engine, ring, "join", "join"));
        graph.AddJoint(new Joint(ring, adapter, "join", "join"));
        var elements = Build(graph, new()
        {
            [top] = new(0, 5, 0), [engine] = new(0, 3.5, 0),
            [ring] = new(0, 3.9, 0), [adapter] = new(0, 2, 0),
        });

        Assert.Equal(20.0, elements.Sum(e => e.CrossflowAreaM2), 12);
        Assert.Equal(4.0 * System.Math.PI, elements.Sum(e => e.SlenderAreaChangeM2), 12);
        Assert.All(elements, e => Assert.True(e.SlenderAreaChangeM2 >= 0.0));
        Assert.Contains(elements, e => e.PositionLocal.Y == 4.0
            && System.Math.Abs(e.SlenderAreaChangeM2 - 3.0 * System.Math.PI) < 1e-12);
    }

    [Fact]
    public void HiddenHardwareDoesNotChangeTheTranslatedHullForceOrMoment()
    {
        var hull = MakePart("hull", 6.0, 4.0);
        var hidden = MakePart("hidden", 2.0, 1.0);
        var graph = new PartGraph();
        graph.SetRoot(hull);
        var baseline = Build(graph, new() { [hull] = new(0, 3, 0) });
        graph.AddJoint(new Joint(hull, hidden, "join", "join"));
        var overlapped = Build(graph, new() { [hull] = new(0, 3, 0), [hidden] = new(0, 4, 0) });

        AeroWrench Wrench(List<AeroElement> elements) => DistributedAerodynamics.Evaluate(elements,
            Quaterniond.Identity, Vector3d.Zero, new(0, 2, 0), new(20, 50, 0), 1.0, 280.0, 4.0, 0.3);
        var expected = Wrench(baseline);
        var actual = Wrench(overlapped);
        Assert.InRange((actual.ForceWorld - expected.ForceWorld).Magnitude, 0.0, 1e-8);
        Assert.InRange((actual.TorqueBody - expected.TorqueBody).Magnitude, 0.0, 1e-8);
    }

    [Fact]
    public void OrdinaryJointRoundoffPreservesTheExistingElementsExactly()
    {
        var top = MakePart("top", 2.0, 1.0);
        var bottom = MakePart("bottom", 2.0, 2.0);
        var graph = new PartGraph();
        graph.SetRoot(top);
        graph.AddJoint(new Joint(top, bottom, "join", "join"));
        double topCenter = double.BitDecrement(3.0);
        var elements = Build(graph, new() { [top] = new(0, topCenter, 0), [bottom] = new(0, 1, 0) });

        Assert.Equal(new[]
        {
            new AeroElement(new(0, topCenter, 0), 2.0, 0.0, 1.0),
            new AeroElement(new(0, 1, 0), 4.0, 0.0, 2.0),
            new AeroElement(new(0, topCenter + 1.0, 0), 0.0, System.Math.PI * 0.25),
            new AeroElement(new(0, 2, 0), 0.0, System.Math.PI * 0.75),
        }, elements);
    }

    private static List<AeroElement> Build(PartGraph graph, Dictionary<Part, Vector3d> positions)
    {
        var elements = new List<AeroElement>();
        DistributedAerodynamics.BuildElements(graph, positions, elements);
        return elements;
    }

    private static Part MakePart(string id, double length, double diameter) => new(new PartDefinition
    {
        Id = id, LengthM = length, DiameterM = diameter, MassDry = 1.0,
        AttachmentNodes = [new AttachmentNodeDef { Id = "join", Position = [0.0, 0.0, 0.0] }],
    });
}
