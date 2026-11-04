namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Xunit;

[Trait("FidelityGate", "F1")]
[Trait("GateKind", "analytic")]
public sealed class DistributedAerodynamicsTests
{
    private const double Density = 1.2;
    private const double Temperature = 288.15; // 100 m/s is Mach 0.29.

    private static List<AttachmentNodeDef> StackNodes(double length) =>
    [
        new AttachmentNodeDef { Id = "top", Position = [0.0, length * 0.5, 0.0] },
        new AttachmentNodeDef { Id = "bottom", Position = [0.0, -length * 0.5, 0.0] },
    ];

    private static Part Segment(string id, double length, double diameter, double nose = 0.0) => new(new PartDefinition
    {
        Id = id, CategoryStr = "structure", MassDry = 1_000.0, LengthM = length, DiameterM = diameter,
        NoseLengthM = nose, AttachmentNodes = StackNodes(length),
    }, id);

    private static PartGraph Stack(params Part[] topToBottom)
    {
        var graph = new PartGraph();
        graph.SetRoot(topToBottom[0]);
        for (int i = 1; i < topToBottom.Length; i++)
            graph.AddJoint(new Joint(topToBottom[i - 1], topToBottom[i], "bottom", "top"));
        return graph;
    }

    private static AeroWrench Evaluate(PartGraph graph, Vector3d airVelocity, Vector3d? omega = null,
        double axialCd = 0.6, Vector3d? centerOfMass = null) =>
        DistributedAerodynamics.Evaluate(graph.GetAeroElements(), Quaterniond.Identity,
            omega ?? Vector3d.Zero, centerOfMass ?? graph.CenterOfMass, airVelocity, Density, Temperature,
            graph.MaximumDiameter, axialCd);

    private static Vector3d Flow(double angleOfAttackDeg, double speed = 100.0)
    {
        double a = angleOfAttackDeg * MathUtils.DEG_TO_RAD;
        // Vehicle moving nose-first (+Y) with its velocity tilted toward −X: the nose is
        // pitched toward +X relative to the flight path.
        return new Vector3d(-System.Math.Sin(a), System.Math.Cos(a), 0.0) * speed;
    }

    [Fact]
    public void UniformCylinderBroadsideIsCrossflowDragAtItsCentre()
    {
        var graph = Stack(Segment("body", 40.0, 4.0));
        var wrench = Evaluate(graph, new Vector3d(100.0, 0.0, 0.0));
        var air = new Exosphere.Simulation.AtmosphereModel();
        double mach = 100.0 / System.Math.Sqrt(AerodynamicsModel.EarthAirSoundSpeedConstant * Temperature);
        double reynolds = Density * 100.0 * 4.0 / air.GetDynamicViscosityAtTemperature(Temperature);
        double expected = 0.5 * Density * 100.0 * 100.0 * CrossflowDrag.Coefficient(mach, reynolds, 10.0) * 4.0 * 40.0;
        Assert.Equal(-expected, wrench.ForceWorld.X, 6);
        Assert.Equal(0.0, wrench.ForceWorld.Y, 6);
        Assert.InRange(wrench.TorqueBody.Magnitude, 0.0, 1e-6 * expected);
    }

    [Fact]
    public void NoseOnFlightIsPureAxialDrag()
    {
        var graph = Stack(Segment("body", 40.0, 4.0, nose: 8.0));
        var wrench = Evaluate(graph, new Vector3d(0.0, 100.0, 0.0), axialCd: 0.4);
        double expected = 0.5 * Density * 100.0 * 100.0 * 0.4 * System.Math.PI * 4.0;
        Assert.Equal(-expected, wrench.ForceWorld.Y, 6);
        Assert.InRange(new Vector3d(wrench.ForceWorld.X, 0, wrench.ForceWorld.Z).Magnitude, 0.0, 1e-9);
        Assert.InRange(wrench.TorqueBody.Magnitude, 0.0, 1e-9);
    }

    [Fact]
    public void SmallAngleNormalForceSlopeIsTwoBaseAreas()
    {
        // Munk: dC_N/dα = 2 at α → 0 for any nose; crossflow adds only at second order.
        var graph = Stack(Segment("body", 40.0, 4.0, nose: 8.0));
        double alpha = 0.5 * MathUtils.DEG_TO_RAD;
        var wrench = Evaluate(graph, Flow(0.5));
        double q = 0.5 * Density * 100.0 * 100.0;
        double area = System.Math.PI * 4.0;
        double normal = wrench.ForceWorld.X; // perpendicular to the axis
        Assert.InRange(normal / (q * area * alpha), 2.0, 2.0 * 1.10);
    }

    [Fact]
    public void BareBodyOfRevolutionIsStaticallyUnstableAndAFlareStabilisesIt()
    {
        // Centre of mass at mid-length. The nose lift ahead of it pitches the nose further
        // into the wind (Munk moment); a wide aft flare puts lift behind it and restores.
        var bare = Stack(Segment("body", 40.0, 4.0, nose: 8.0));
        var flared = Stack(Segment("body", 40.0, 4.0, nose: 8.0), Segment("skirt", 4.0, 7.0));
        var centre = new Vector3d(0.0, 22.0, 0.0);
        var bareTorque = Evaluate(bare, Flow(2.0), centerOfMass: centre).TorqueBody;
        var flaredTorque = Evaluate(flared, Flow(2.0), centerOfMass: centre).TorqueBody;
        // The nose is pitched toward +X; a torque about −Z turns +Y further toward +X, i.e.
        // increases α (unstable), and one about +Z restores it.
        Assert.True(bareTorque.Z < 0.0, $"bare body must be unstable: {bareTorque}");
        Assert.True(flaredTorque.Z > bareTorque.Z, $"flare must move the CP aft: {flaredTorque} vs {bareTorque}");
        Assert.True(flaredTorque.Z > 0.0, $"a large flare must make it stable: {flaredTorque}");
    }

    [Fact]
    public void PitchRateIsDampedByTheLocalFlow()
    {
        var graph = Stack(Segment("body", 40.0, 4.0, nose: 8.0));
        var omega = new Vector3d(0.0, 0.0, 0.2);
        var still = Evaluate(graph, Flow(0.0)).TorqueBody;
        var rotating = Evaluate(graph, Flow(0.0), omega).TorqueBody;
        Assert.InRange(still.Magnitude, 0.0, 1e-9);
        Assert.True(rotating.Z < 0.0, $"aerodynamic moment must oppose the pitch rate: {rotating}");
    }

    [Fact]
    public void TailFirstFlightReversesTheNoseLiftContribution()
    {
        // Falling engines-first, the nose trails: its slender-body lift reverses and the
        // body weathervanes with the heavy aft end leading (booster return, belly-flop flip).
        var graph = Stack(Segment("body", 40.0, 4.0, nose: 8.0));
        var forward = Evaluate(graph, Flow(5.0), centerOfMass: new Vector3d(0, 20, 0)).TorqueBody;
        var backward = Evaluate(graph, -Flow(5.0), centerOfMass: new Vector3d(0, 20, 0)).TorqueBody;
        Assert.NotEqual(0, System.Math.Sign(forward.Z));
        Assert.True(System.Math.Abs(forward.Z - backward.Z) > 1e-6, $"{forward} vs {backward}");
    }
}

[Trait("FidelityGate", "F1")]
[Trait("GateKind", "analytic")]
public sealed class DistributedAerodynamicsControlTests
{
    private static Part Engine(double thrust, double gimbalDeg) => new(new PartDefinition
    {
        Id = "engine", CategoryStr = "engine", MassDry = 500.0, LengthM = 1.0, DiameterM = 1.0,
        ThrustSL = thrust, ThrustVac = thrust, IspSL = 300.0, IspVac = 300.0, GimbalRange = gimbalDeg,
        AttachmentNodes = [new AttachmentNodeDef { Id = "top", Position = [0.0, 0.5, 0.0] }],
    }, "engine");

    [Fact]
    public void SingleEngineWithoutRuntimeIsAllocatedThroughItsPartGimbal()
    {
        var tank = new Part(new PartDefinition
        {
            Id = "tank", CategoryStr = "structure", MassDry = 5_000.0, LengthM = 10.0, DiameterM = 1.0,
            AttachmentNodes = [new AttachmentNodeDef { Id = "bottom", Position = [0.0, -5.0, 0.0] }],
        }, "tank");
        var engine = Engine(60_000.0, 5.0);
        var graph = new PartGraph();
        graph.SetRoot(tank);
        graph.AddJoint(new Joint(tank, engine, "bottom", "top"));
        engine.IsStagingActive = true;
        engine.ThrottleLevel = 1.0;
        Assert.False(engine.HasEngineRuntime);

        var desired = new Vector3d(-20_000.0, 0.0, 0.0);
        graph.SolveDifferentialGimbal(desired, 0.0);
        var torque = graph.GetTotalTorque(0.0);

        Assert.NotEqual(Vector3d.Zero, engine.GimbalOffset);
        Assert.True(torque.X * desired.X > 0.0, $"gimbal torque {torque} must follow the request {desired}");
        Assert.InRange(System.Math.Abs(torque.X), 0.8 * 20_000.0, 1.2 * 20_000.0);
    }

    [Fact]
    public void BluntBodiesKeepTheBluntModel()
    {
        static PartGraph Body(double length, double diameter)
        {
            var graph = new PartGraph();
            var part = new Part(new PartDefinition
            {
                Id = "b", CategoryStr = "structure", MassDry = 1_000.0, LengthM = length, DiameterM = diameter,
            }, "b");
            graph.SetRoot(part);
            graph.AddPart(part);
            return graph;
        }
        Assert.False(Body(3.3, 1.9).UsesDistributedAerodynamics); // Mercury-like capsule, L/D 1.7
        Assert.True(Body(30.0, 3.0).UsesDistributedAerodynamics);  // slender launcher, L/D 10
    }
}
