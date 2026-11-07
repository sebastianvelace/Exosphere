namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Integrators;

/// <summary>Current store-owned modal geometry, without funding or mutation.</summary>
public static class SloshVesselState
{
    public static bool HasDeclaredLiquidTanks(Vessel vessel) => vessel.Parts.Parts.Any(p =>
        p.Definition.PropellantTanks.Any(t => t.Kind != PartMassComponentKind.SolidFuel));

    internal static SloshInternalMomenta FeedMomenta(Vessel vessel)
    {
        if (!vessel.TryGetTransportMomentum(vessel.Parts.CenterOfMass, out var p, out var h)) return default;
        return new(p, h + vessel.Parts.CenterOfMass.Cross(p));
    }

    internal static void InitializeEquilibriumModes(Vessel vessel)
    {
        var existing = vessel.SloshStates.Select(s => s.Store).ToHashSet();
        foreach (var part in vessel.Parts.Parts)
            foreach (var tank in part.Definition.PropellantTanks)
            {
                if (tank.Kind == PartMassComponentKind.SolidFuel) continue;
                var resource = SloshStepModel.Resource(tank.Kind); var store = new ResourceStoreId(part.InstanceId, resource);
                double mass = SloshStepModel.StoreMass(part, resource);
                if (mass > 0 && existing.Add(store)) vessel.SloshStates.Add(new(store, default, SloshStepModel.Parameters(tank, mass)));
            }
    }

    internal static void Separate(Vessel primary, Vessel other, Vector3d impulsePointWorld,
        Vector3d separationAxis, double relativeOpeningMs, double? declaredImpulseNs = null)
    {
        var own = SloshVesselState.Stage(primary); var detached = SloshVesselState.Stage(other);
        Slosh6DofState Response(
            Slosh6DofStage stage, Vector3d impulse)
        {
            var datum = stage.State.Datum;
            return SloshImpulseDynamics.Apply(stage, impulse,
                datum.Orientation.Inverse().Rotate((impulsePointWorld - datum.Position).Cross(impulse)));
        }
        double magnitude = declaredImpulseNs ?? 0;
        if (declaredImpulseNs == null && relativeOpeningMs != 0)
        {
            // Evaluate the linear mobility at zero rigid velocity to avoid subtracting
            // tiny unit-impulse responses from an astronomical translational velocity.
            Slosh6DofStage AtRest(Slosh6DofStage stage) =>
                stage with { State = stage.State with { Datum = new(stage.State.Datum.Position,
                    Vector3d.Zero, stage.State.Datum.Orientation, Vector3d.Zero) } };
            var unitOwn = Response(AtRest(own), separationAxis);
            var unitOther = Response(AtRest(detached), -separationAxis);
            Vector3d PointResponse(Slosh6DofState state) => state.Datum.Velocity
                + state.Datum.AngularVelocityWorld.Cross(impulsePointWorld - state.Datum.Position);
            double mobility = separationAxis.Dot(PointResponse(unitOwn) - PointResponse(unitOther));
            if (!(mobility > 0) || !double.IsFinite(mobility))
                throw new InvalidOperationException("The coupled separation mobility must be positive.");
            magnitude = relativeOpeningMs / mobility;
        }
        if (magnitude != 0)
        {
            SloshVesselState.CommitImpulse(primary, Response(own, separationAxis * magnitude));
            SloshVesselState.CommitImpulse(other, Response(detached, -separationAxis * magnitude));
        }
    }

    public static Slosh6DofStage Stage(Vessel vessel)
    {
        var locked = vessel.Parts.GetMassProperties();
        var baseline = new BodyMassSample(locked.Mass, 0, locked.CenterOfMassBody, default, default, locked.InertiaBody, default);
        var positions = vessel.Parts.ComputePartLocalPositions();
        var tanks = new SloshTankParameters[vessel.SloshStates.Count];
        for (int i = 0; i < tanks.Length; i++)
        {
            var runtime = vessel.SloshStates[i];
            var part = vessel.Parts.Parts.Single(p => p.InstanceId == runtime.Store.PartInstanceId);
            var tank = part.Definition.PropellantTanks.Single(t => t.Kind != PartMassComponentKind.SolidFuel
                && SloshStepModel.Resource(t.Kind) == runtime.Store.Resource);
            var parameters = runtime.PreviousParameters ?? SloshStepModel.Parameters(tank, SloshStepModel.StoreMass(part, runtime.Store.Resource));
            tanks[i] = new(parameters, positions[part] + Vector3d.Up * tank.BottomYM);
        }
        var state = new Slosh6DofState(new(vessel.Position, vessel.Velocity, vessel.Orientation,
            vessel.Orientation.Inverse().Rotate(vessel.AngularVelocity)), vessel.SloshStates.Select(s => s.State).ToArray());
        return Slosh6DofIntegrator.Sample(state, new(baseline, tanks));
    }

    internal static void CommitImpulse(Vessel vessel, Slosh6DofState state)
    {
        vessel.Velocity = state.Datum.Velocity; vessel.AngularVelocity = state.Datum.AngularVelocityWorld;
        for (int i = 0; i < state.Modes.Count; i++) vessel.SloshStates[i] = vessel.SloshStates[i] with { State = state.Modes[i] };
    }

    public static (RigidBodyMassProperties Mass, SloshInternalMomenta Internal) Sample(Vessel vessel)
    {
        var locked = vessel.Parts.GetMassProperties();
        if (vessel.SloshStates.All(s => s.State.DisplacementBodyM == Vector3d.Zero && s.State.RateBodyMps == Vector3d.Zero))
            return (locked, default);
        var baseline = new BodyMassSample(locked.Mass, 0, locked.CenterOfMassBody,
            default, default, locked.InertiaBody, default);
        var corrections = new List<SloshTransportCorrection>();
        if (vessel.SloshStates.Count > 0)
        {
            var positions = vessel.Parts.ComputePartLocalPositions();
            foreach (var runtime in vessel.SloshStates)
            {
                var part = vessel.Parts.Parts.SingleOrDefault(p => p.InstanceId == runtime.Store.PartInstanceId)
                    ?? throw new InvalidOperationException("A slosh state has no source part.");
                var tank = part.Definition.PropellantTanks.SingleOrDefault(t => t.Kind != PartMassComponentKind.SolidFuel
                    && SloshStepModel.Resource(t.Kind) == runtime.Store.Resource)
                    ?? throw new InvalidOperationException("A slosh state has no declared source tank.");
                var parameters = runtime.PreviousParameters ?? SloshStepModel.Parameters(tank,
                    SloshStepModel.StoreMass(part, runtime.Store.Resource));
                var floor = positions[part] + Vector3d.Up * tank.BottomYM;
                corrections.Add(SloshTransportCorrection.SampleDisplacement(parameters, floor, runtime.State));
            }
        }
        var mass = corrections.Count > 0 ? SloshVariableMassDynamics.MassProperties(baseline, corrections).Properties : locked;
        return (mass, SloshInternalMomenta.Sample(baseline, default, corrections));
    }
}
