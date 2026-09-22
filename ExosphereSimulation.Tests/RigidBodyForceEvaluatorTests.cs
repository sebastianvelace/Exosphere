namespace Exosphere.Simulation.Tests;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;
using Exosphere.Simulation.Parts;

public sealed class RigidBodyForceEvaluatorTests
{
    [Fact]
    public void EvaluatorUsesCandidateOrientationForThrust()
    {
        var engine = new Part(new PartDefinition
        {
            Id = "test_engine",
            CategoryStr = "engine",
            MassDry = 10.0,
            ThrustVac = 100.0,
            ThrustSL = 100.0,
            IspVac = 300.0,
            IspSL = 300.0,
            GimbalRange = 0.0,
            LengthM = 4.0,
            DiameterM = 2.0,
        }, "engine")
        {
            ThrottleLevel = 1.0,
        };
        var vessel = new Vessel("force-evaluator") { Orientation = Quaterniond.Identity };
        vessel.Parts.SetRoot(engine);
        var context = new RigidBodyForceContext(Array.Empty<CelestialBody>());

        var rotatedState = new RigidBody6DofState(
            Vector3d.Zero,
            Vector3d.Zero,
            Quaterniond.FromAxisAngle(new Vector3d(0.0, 0.0, 1.0), System.Math.PI * 0.5),
            Vector3d.Zero);
        var forces = RigidBodyForceEvaluator.Evaluate(vessel, rotatedState, context);

        Assert.Equal(-100.0, forces.ForceWorld.X, precision: 10);
        Assert.Equal(0.0, forces.ForceWorld.Y, precision: 10);
        Assert.Equal(0.0, forces.ForceWorld.Z, precision: 10);
    }

    [Fact]
    public void EvaluatorCombinesEngineAndExternalTorqueInBodyFrame()
    {
        var engine = new Part(new PartDefinition
        {
            Id = "gimballed_engine",
            CategoryStr = "engine",
            MassDry = 10.0,
            ThrustVac = 100.0,
            ThrustSL = 100.0,
            IspVac = 300.0,
            IspSL = 300.0,
            GimbalRange = 10.0,
            ThrustPositionYM = -2.0,
            LengthM = 4.0,
            DiameterM = 2.0,
        }, "engine")
        {
            ThrottleLevel = 1.0,
            GimbalOffset = new Vector3d(1.0, 0.0, 0.0),
        };
        var vessel = new Vessel("torque-evaluator");
        vessel.Parts.SetRoot(engine);
        var state = new RigidBody6DofState(
            Vector3d.Zero,
            Vector3d.Zero,
            Quaterniond.Identity,
            Vector3d.Zero);
        var expectedEngineTorque = vessel.Parts.GetTotalTorque(0.0);
        var context = new RigidBodyForceContext(
            Array.Empty<CelestialBody>(),
            externalTorqueWorld: new Vector3d(1.0, 2.0, 3.0));

        var beforeFuel = engine.LiquidFuel;
        var beforeOrientation = vessel.Orientation;
        var forces = RigidBodyForceEvaluator.Evaluate(vessel, state, context);

        Assert.Equal(expectedEngineTorque.X + 1.0, forces.TorqueBody.X, precision: 10);
        Assert.Equal(expectedEngineTorque.Y + 2.0, forces.TorqueBody.Y, precision: 10);
        Assert.Equal(expectedEngineTorque.Z + 3.0, forces.TorqueBody.Z, precision: 10);
        Assert.Equal(beforeFuel, engine.LiquidFuel, precision: 12);
        Assert.Equal(beforeOrientation, vessel.Orientation);
    }

    [Fact]
    public void EvaluatorAddsExternalForceWithoutMutatingVessel()
    {
        var part = new Part(new PartDefinition
        {
            Id = "payload",
            CategoryStr = "structure",
            MassDry = 20.0,
            LengthM = 2.0,
            DiameterM = 2.0,
        }, "payload");
        var vessel = new Vessel("force-only");
        vessel.Parts.SetRoot(part);
        var state = new RigidBody6DofState(
            new Vector3d(10.0, 20.0, 30.0),
            new Vector3d(1.0, 2.0, 3.0),
            Quaterniond.Identity,
            Vector3d.Zero);
        var context = new RigidBodyForceContext(
            Array.Empty<CelestialBody>(),
            externalForceWorld: new Vector3d(4.0, 5.0, 6.0));

        var forces = RigidBodyForceEvaluator.Evaluate(vessel, state, context);

        Assert.Equal(new Vector3d(4.0, 5.0, 6.0), forces.ForceWorld);
        Assert.Equal(Vector3d.Zero, forces.TorqueBody);
        Assert.Equal(new Vector3d(10.0, 20.0, 30.0), state.Position);
        Assert.Equal(new Vector3d(1.0, 2.0, 3.0), state.Velocity);
    }
}
