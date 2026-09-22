namespace Exosphere.Simulation.Tests;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

public sealed class PartGraphMassPropertiesTests
{
    [Fact]
    public void SnapshotUsesCurrentMassAndParallelAxisTheorem()
    {
        var root = new Part(new PartDefinition
        {
            Id = "root",
            CategoryStr = "command",
            MassDry = 2.0,
            LengthM = 2.0,
            DiameterM = 2.0,
            AttachmentNodes =
            [
                new AttachmentNodeDef { Id = "top", Position = [0.0, 1.0, 0.0] },
            ],
        }, "root");
        var child = new Part(new PartDefinition
        {
            Id = "child",
            CategoryStr = "structure",
            MassDry = 1.0,
            LengthM = 2.0,
            DiameterM = 2.0,
            AttachmentNodes =
            [
                new AttachmentNodeDef { Id = "bottom", Position = [0.0, -1.0, 0.0] },
            ],
        }, "child");

        var graph = new PartGraph();
        graph.SetRoot(root);
        graph.AddJoint(new Joint(root, child, "top", "bottom"));

        var properties = graph.GetMassProperties();

        Assert.Equal(3.0, properties.Mass, precision: 12);
        Assert.Equal(2.0 / 3.0, properties.CenterOfMassBody.Y, precision: 12);
        Assert.Equal(0.0, properties.CenterOfMassBody.X, precision: 12);
        Assert.Equal(0.0, properties.CenterOfMassBody.Z, precision: 12);

        // Each 2 m x 2 m cylinder has Ixx = Izz = 7m/12 and Iyy = m/2.
        // The Y offsets add m*d² around the transverse axes.
        Assert.Equal(53.0 / 12.0, properties.InertiaBody.M11, precision: 12);
        Assert.Equal(3.0 / 2.0, properties.InertiaBody.M22, precision: 12);
        Assert.Equal(53.0 / 12.0, properties.InertiaBody.M33, precision: 12);
        Assert.Equal(0.0, properties.InertiaBody.M12, precision: 12);
        Assert.Equal(0.0, properties.InertiaBody.M13, precision: 12);
        Assert.Equal(0.0, properties.InertiaBody.M23, precision: 12);
    }

    [Fact]
    public void SnapshotTracksPropellantMassWithoutMovingPartGeometry()
    {
        var part = new Part(new PartDefinition
        {
            Id = "tank",
            CategoryStr = "fuel_tank",
            MassDry = 100.0,
            FuelCapacityLF = 50.0,
            LengthM = 10.0,
            DiameterM = 4.0,
        }, "tank");
        var graph = new PartGraph();
        graph.SetRoot(part);

        var full = graph.GetMassProperties();
        part.LiquidFuel = 0.0;
        var empty = graph.GetMassProperties();

        Assert.Equal(150.0, full.Mass, precision: 12);
        Assert.Equal(100.0, empty.Mass, precision: 12);
        Assert.Equal(full.CenterOfMassBody, empty.CenterOfMassBody);
        Assert.True(full.InertiaBody.M11 > empty.InertiaBody.M11);
        Assert.True(full.InertiaBody.M22 > empty.InertiaBody.M22);
        Assert.True(full.InertiaBody.M33 > empty.InertiaBody.M33);
    }

    [Fact]
    public void EmptyGraphReportsNoMassProperties()
    {
        var graph = new PartGraph();

        Assert.False(graph.TryGetMassProperties(out var properties));
        Assert.Equal(0.0, properties.Mass);
    }
}
