namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;

/// <summary>
/// External inputs that are already known for one force-evaluation stage.
/// Contact solvers and the moving reference frame can provide these without making the
/// rigid-body evaluator responsible for their lifecycle or failure policy.
/// </summary>
public readonly struct RigidBodyForceContext
{
    public IReadOnlyList<CelestialBody> Bodies { get; }
    public CelestialBody? ReferenceBody { get; }
    public Vector3d ExternalForceWorld { get; }
    public Vector3d ExternalTorqueWorld { get; }
    /// <summary>Per-part distributed aerodynamics (default); false selects the aggregate model.</summary>
    public bool DistributedAerodynamics { get; }
    /// <summary>Physical hinged body flaps (F2) on the distributed path; false keeps the aggregate flap torque.</summary>
    public bool PhysicalBodyFlaps { get; }

    public RigidBodyForceContext(
        IReadOnlyList<CelestialBody> bodies,
        CelestialBody? referenceBody = null,
        Vector3d externalForceWorld = default,
        Vector3d externalTorqueWorld = default,
        bool distributedAerodynamics = true,
        bool physicalBodyFlaps = false)
    {
        DistributedAerodynamics = distributedAerodynamics;
        PhysicalBodyFlaps = physicalBodyFlaps;
        Bodies = bodies ?? throw new ArgumentNullException(nameof(bodies));
        ReferenceBody = referenceBody;
        ExternalForceWorld = externalForceWorld;
        ExternalTorqueWorld = externalTorqueWorld;
    }
}

/// <summary>
/// Pure force/torque adapter for one candidate 6-DoF state.
/// It reads vessel and part state but does not advance engines, consume resources, update
/// thermal state, change controls, or write contact state. The returned force is world-frame;
/// the returned torque is body-frame for <see cref="RigidBody6DofIntegrator"/>.
/// </summary>
public static class RigidBodyForceEvaluator
{
    public static RigidBodyForces Evaluate(
        Vessel vessel,
        in RigidBody6DofState state,
        in RigidBodyForceContext context)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        return Evaluate(vessel, state, context, vessel.Parts.GetMassProperties());
    }

    /// <summary>
    /// Evaluates the wrench on a body whose integrated mass properties may include rigidly
    /// attached members (a docked stack). Gravity acts on the whole mass; the vessel's own
    /// propulsion torque, computed about its own centre of mass, is transferred to the
    /// integrated centre of mass.
    /// </summary>
    public static RigidBodyForces Evaluate(
        Vessel vessel,
        in RigidBody6DofState state,
        in RigidBodyForceContext context,
        in RigidBodyMassProperties massProperties)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        var forceWorld = vessel.ComputeGravityAt(state.Position, context.Bodies)
            * massProperties.Mass;

        double pressure = context.ReferenceBody?.Atmosphere?.GetPressure(
            context.ReferenceBody.GetAltitude(state.Position)) ?? 0.0;
        var propulsionBody = vessel.Parts.GetTotalThrust(pressure)
            + vessel.RcsThrusterForceBody;
        forceWorld += state.Orientation.Rotate(propulsionBody);

        Vector3d distributedTorqueBody = Vector3d.Zero;
        bool distributed = context.DistributedAerodynamics && context.ReferenceBody?.Atmosphere != null
            && vessel.Parts.UsesDistributedAerodynamics;
        if (distributed)
        {
            var body = context.ReferenceBody!;
            double altitude = body.GetAltitude(state.Position);
            var air = state.Velocity - body.Velocity - body.GetSurfaceVelocity(state.Position);
            var wrench = DistributedAerodynamics.Evaluate(
                vessel.Parts.GetAeroElements(),
                state.Orientation,
                state.AngularVelocityWorld,
                massProperties.CenterOfMassBody,
                air,
                body.Atmosphere!.GetDensity(altitude),
                System.Math.Max(1.0, body.Atmosphere.GetTemperature(altitude)),
                vessel.MaximumDiameter,
                vessel.Parts.AxialDragCoefficient,
                body.Atmosphere.SoundSpeedConstant,
                body.Atmosphere.GetDynamicViscosity(altitude));
            forceWorld += wrench.ForceWorld + vessel.ComputeParachuteDragAt(state.Position, state.Velocity, body);
            distributedTorqueBody = wrench.TorqueBody;
            var flaps = vessel.Parts.GetBodyFlaps();
            if (context.PhysicalBodyFlaps && flaps.Count > 0)
            {
                var flapWrench = BodyFlapAerodynamics.Evaluate(
                    flaps,
                    vessel.BodyFlapDeflectionsRad,
                    state.Orientation,
                    state.AngularVelocityWorld,
                    massProperties.CenterOfMassBody,
                    air,
                    body.Atmosphere.GetDensity(altitude),
                    System.Math.Max(1.0, body.Atmosphere.GetTemperature(altitude)),
                    body.Atmosphere.SoundSpeedConstant);
                forceWorld += flapWrench.ForceWorld;
                distributedTorqueBody += flapWrench.TorqueBody;
            }
        }
        else if (context.ReferenceBody != null)
        {
            forceWorld += vessel.ComputeDragAt(
                state.Position,
                state.Velocity,
                state.Orientation,
                context.ReferenceBody);
        }

        forceWorld += context.ExternalForceWorld;

        var torqueBody = vessel.Parts.GetTotalTorque(pressure)
            + (vessel.Parts.CenterOfMass - massProperties.CenterOfMassBody).Cross(propulsionBody);
        torqueBody += state.Orientation.Inverse().Rotate(context.ExternalTorqueWorld);
        torqueBody += vessel.DisturbanceTorqueBody;
        torqueBody += vessel.RcsThrusterTorqueBody;
        torqueBody += distributed
            ? distributedTorqueBody + (context.PhysicalBodyFlaps && vessel.Parts.GetBodyFlaps().Count > 0
                ? Vector3d.Zero
                : ComputeFlapTorqueBody(vessel, state, context.ReferenceBody!, massProperties))
            : ComputeAerodynamicTorqueBody(vessel, state, context.ReferenceBody, massProperties);

        return new RigidBodyForces(forceWorld, torqueBody);
    }

    /// <summary>Body-flap control moment (the flaps are modelled as aggregate surfaces until F2).</summary>
    private static Vector3d ComputeFlapTorqueBody(
        Vessel vessel,
        in RigidBody6DofState state,
        CelestialBody body,
        RigidBodyMassProperties massProperties)
    {
        if (vessel.FlapActuators.IsZero || !vessel.HasFunctionalBodyFlaps()) return Vector3d.Zero;
        double altitude = body.GetAltitude(state.Position);
        double density = body.Atmosphere!.GetDensity(altitude);
        var air = state.Velocity - body.Velocity - body.GetSurfaceVelocity(state.Position);
        var angularAccelerationWorld = AerodynamicsModel.ComputeFlapControlAngularAcceleration(
            density, air, state.Orientation, vessel.FlapActuators,
            vessel.VehicleLength, vessel.MaximumDiameter, vessel.Parts.TransverseMomentOfInertia);
        return massProperties.InertiaBody.Multiply(state.Orientation.Inverse().Rotate(angularAccelerationWorld));
    }

    private static Vector3d ComputeAerodynamicTorqueBody(
        Vessel vessel,
        in RigidBody6DofState state,
        CelestialBody? body,
        RigidBodyMassProperties massProperties)
    {
        if (body?.Atmosphere == null) return Vector3d.Zero;

        double altitude = body.GetAltitude(state.Position);
        double density = body.Atmosphere.GetDensity(altitude);
        var surfaceVelocity = state.Velocity
            - body.Velocity
            - body.GetSurfaceVelocity(state.Position);
        double speed = surfaceVelocity.Magnitude;
        if (density <= 0.0 || speed <= 1.0) return Vector3d.Zero;

        double temperature = System.Math.Max(1.0, body.Atmosphere.GetTemperature(altitude));
        double? aerodynamicCenterOffset = vessel.Parts.AerodynamicCenterOffsetYM;

        var angularAccelerationWorld = AerodynamicsModel.ComputeAttitudeAngularAcceleration(
            density,
            surfaceVelocity,
            state.Orientation.Rotate(vessel.Parts.AerodynamicReferenceAxisLocal),
            state.AngularVelocityWorld,
            vessel.VehicleLength,
            vessel.MaximumDiameter,
            vessel.Parts.TransverseMomentOfInertia,
            temperature,
            aerodynamicCenterOffset,
            body.Atmosphere.SoundSpeedConstant);
        if (vessel.FlapActuators.IsZero is false && vessel.HasFunctionalBodyFlaps())
        {
            angularAccelerationWorld += AerodynamicsModel.ComputeFlapControlAngularAcceleration(
                density,
                surfaceVelocity,
                state.Orientation,
                vessel.FlapActuators,
                vessel.VehicleLength,
                vessel.MaximumDiameter,
                vessel.Parts.TransverseMomentOfInertia);
        }
        var angularAccelerationBody = state.Orientation.Inverse().Rotate(angularAccelerationWorld);
        return massProperties.InertiaBody.Multiply(angularAccelerationBody);
    }
}
