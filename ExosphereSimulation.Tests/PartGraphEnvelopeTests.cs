namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Parts;

public sealed class PartGraphEnvelopeTests
{
    [Theory]
    [InlineData(2.0, 1.0, 4.0)] // The short part is entirely inside the long part.
    [InlineData(4.0, -1.0, 8.0)] // A declared attachment gap belongs to the envelope.
    [InlineData(2.0, -1.0, 6.0)] // Ordinary end-to-end construction preserves the sum.
    public void DeclaredConnectionsDetermineThePhysicalAxialSpan(double parentY, double childY, double expected)
    {
        var root = MakePart("root", 4.0, parentY);
        var child = MakePart("child", 2.0, childY);
        var graph = new PartGraph();
        graph.SetRoot(root);
        graph.AddJoint(new Joint(root, child, "join", "join"));

        Assert.Equal(expected, graph.VehicleLength, 12);
    }

    [Fact]
    public void MissingConnectionGeometryKeepsTheLegacyLengthEstimate()
    {
        var root = MakePart("root", 4.0, 2.0);
        var child = MakePart("child", 2.0, 1.0);
        var graph = new PartGraph();
        graph.SetRoot(root);
        graph.AddJoint(new Joint(root, child, "unknown", "unknown"));

        Assert.Equal(6.0, graph.VehicleLength, 12);
    }

    [Fact]
    public void EnclosedPayloadDoesNotExtendTheCarrierEnvelope()
    {
        var root = MakePart("root", 4.0, 2.0);
        var child = MakePart("child", 2.0, 1.0);
        var payload = MakePart("payload", 20.0, 10.0, internalPayload: true);
        var graph = new PartGraph();
        graph.SetRoot(root);
        graph.AddJoint(new Joint(root, child, "join", "join"));
        graph.AddJoint(new Joint(child, payload, "join", "join"));

        Assert.Equal(4.0, graph.VehicleLength, 12);
    }

    [Fact]
    public void TopologyMutationInvalidatesTheEnvelope()
    {
        var root = MakePart("root", 4.0, 2.0);
        var child = MakePart("child", 2.0, 1.0);
        var graph = new PartGraph();
        graph.SetRoot(root);
        graph.AddJoint(new Joint(root, child, "join", "join"));
        Assert.Equal(4.0, graph.VehicleLength, 12);

        var extra = MakePart("extra", 2.0, -1.0);
        graph.AddJoint(new Joint(child, extra, "join", "join"));
        Assert.Equal(6.0, graph.VehicleLength, 12);
    }

    [Fact]
    public void EndConnectionsThatFoldBackOverTheStackUseTheOuterBounds()
    {
        var root = MakePart("root", 4.0, 2.0);
        var child = MakePart("child", 2.0, -1.0);
        var folded = MakePart("folded", 4.0, 2.0);
        var graph = new PartGraph();
        graph.SetRoot(root);
        graph.AddJoint(new Joint(root, child, "join", "join"));
        graph.AddJoint(new Joint(child, folded, "join", "join"));

        Assert.Equal(6.0, graph.VehicleLength, 12);
    }

    [Fact]
    public void ParallelBranchesDoNotAddTheirLengthsTwice()
    {
        var root = MakePart("root", 4.0, 2.0);
        var a = MakePart("a", 2.0, -1.0);
        var b = MakePart("b", 2.0, -1.0);
        var graph = new PartGraph();
        graph.SetRoot(root);
        graph.AddJoint(new Joint(root, a, "join", "join"));
        graph.AddJoint(new Joint(root, b, "join", "join"));

        Assert.Equal(6.0, graph.VehicleLength, 12);
    }

    [Fact]
    public void DetachingASubtreeInvalidatesTheEnvelope()
    {
        var root = MakePart("root", 4.0, 4.0);
        var child = MakePart("child", 2.0, -1.0);
        var graph = new PartGraph();
        graph.SetRoot(root);
        graph.AddJoint(new Joint(root, child, "join", "join"));
        Assert.Equal(8.0, graph.VehicleLength, 12);

        Assert.NotNull(graph.DetachSubtree(child.InstanceId));
        Assert.Equal(4.0, graph.VehicleLength, 12);
    }

    private static Part MakePart(string id, double length, double attachmentY, bool internalPayload = false) =>
        new(new PartDefinition
        {
            Id = id,
            LengthM = length,
            DiameterM = 1.0,
            MassDry = 1.0,
            InternalPayload = internalPayload,
            AttachmentNodes = [new AttachmentNodeDef { Id = "join", Position = [0.0, attachmentY, 0.0] }],
        });
}
