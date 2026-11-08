namespace Exosphere.Simulation;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;
using Exosphere.Simulation.Integrators;

public partial class Universe
{
    /// <summary>Coupled first modes for declared main liquid tanks; fallback shapes have no inferred surface.</summary>
    public bool PropellantSloshEnabled { get; set; } = true;

    private bool HasRetainedSloshSchedule(Vessel vessel)
    {
        if (!PropellantSloshEnabled) return false;
        return Pending(vessel) || SloshMembers(vessel)?.Any(m => Pending(m.Vessel)) == true;
        static bool Pending(Vessel owner) => owner.SloshStates.Any(s => s.PreviousParameters is { } p
            && (p.ModalMassKg.Rate != 0 || p.ModalMassKg.Acceleration != 0 || p.CurvaturePerM.Rate != 0
                || p.CurvaturePerM.Acceleration != 0 || p.HeightAboveFloorM.Rate != 0 || p.HeightAboveFloorM.Acceleration != 0));
    }

    private DeferredRailAnchor CaptureRailAnchor(Vessel vessel, double time)
    {
        var owners = new List<Vessel> { vessel };
        if (SloshMembers(vessel) is { } members) owners.AddRange(members.Select(m => m.Vessel));
        var slosh = owners.Any(o => o.SloshStates.Count > 0 || SloshVesselState.HasDeclaredLiquidTanks(o))
            ? owners.Select(o => new DeferredSloshAnchor(o.Id, o.SloshStates.ToArray())).ToArray() : null;
        return new(time, vessel.Orientation, vessel.AngularVelocity, slosh);
    }

    private void RestoreRailAnchor(Vessel vessel, DeferredRailAnchor anchor)
    {
        vessel.Orientation = anchor.Orientation; vessel.AngularVelocity = anchor.AngularVelocity;
        if (anchor.Slosh == null) return;
        foreach (var saved in anchor.Slosh)
        {
            var owner = _vessels.First(v => v.Id == saved.VesselId);
            owner.SloshStates.Clear(); owner.SloshStates.AddRange(saved.States);
        }
    }

    internal void SynchronizeSloshSaveSnapshot()
    {
        // A save becomes a new committed modal epoch. Both the continued world and a
        // restored world must begin the next coast from that same state and partition.
        foreach (var vessel in _vessels)
        {
            if (IsDockedSecondary(vessel) || !HasSloshTanks(vessel)) continue;
            CatchUpDeferredRailVessel(vessel, CurrentTime);
            _deferredRailAnchors.Remove(vessel.Id); _nextDeferredRailDeadline.Remove(vessel.Id);
        }
    }

    private bool HasSloshTanks(Vessel primary) => SloshVesselState.HasDeclaredLiquidTanks(primary)
        || SloshMembers(primary)?.Any(m => SloshVesselState.HasDeclaredLiquidTanks(m.Vessel)) == true;

    private List<SloshAttachedVessel>? SloshMembers(Vessel primary)
    {
        List<SloshAttachedVessel>? members = null;
        foreach (var connection in _dockingConnections)
        {
            if (connection.PrimaryVesselId != primary.Id) continue;
            var secondary = _vessels.FirstOrDefault(v => v.Id == connection.SecondaryVesselId && !v.IsDestroyed);
            if (secondary != null) (members ??= []).Add(new(secondary,
                connection.SecondaryPositionPrimaryLocal, connection.SecondaryOrientationPrimaryLocal));
        }
        return members;
    }

    private void IntegrateVesselSlosh(Vessel vessel, CelestialBody reference, double dt,
        VariableMassStepModel funded, SloshStepModel model, Vector3d referenceStartPosition,
        Vector3d referenceStartVelocity, Vector3d frameAcceleration, double evaluationTime)
    {
        try
        {
            var distribution = funded.Distribution; var transport = funded.Transport;
            var boundaries = new List<double>(distribution.BoundariesSeconds);
            foreach (var group in transport.GroupArray)
            {
                foreach (var debit in group._debits) { boundaries.Add(debit.StartSeconds); boundaries.Add(debit.EndSeconds); }
                foreach (var exit in group._outlets) { boundaries.Add(exit.StartSeconds); boundaries.Add(exit.EndSeconds); }
            }
            var initial = new Slosh6DofState(new(vessel.Position - referenceStartPosition,
                vessel.Velocity - referenceStartVelocity, vessel.Orientation,
                vessel.Orientation.Inverse().Rotate(vessel.AngularVelocity)), model.InitialStates());
            var opening = model.Sample(0, MassBoundarySide.Right);
            var previous = Slosh6DofIntegrator.Sample(initial, model.PreviousProfile(opening));
            var start = Slosh6DofIntegrator.Sample(initial, opening);
            var oldFeed = PreviousFeedMomenta(vessel);
            var oldInternal = Add(oldFeed, SloshInternalMomenta.Sample(previous.Profile.Baseline, default, previous.Corrections));
            var nextFeed = transport.Sample(0, MassBoundarySide.Right, opening.Baseline, initial.Datum.AngularVelocityBody);
            var nextInternal = SloshInternalMomenta.Sample(opening.Baseline, nextFeed, start.Corrections);
            initial = start.State with { Datum = SloshMomentumTransfer.Conserve(initial.Datum,
                previous.Mass.Properties, oldInternal, start.Mass.Properties, nextInternal) };
            var initialAbsoluteCenter = referenceStartPosition + initial.Datum.Position
                + initial.Datum.Orientation.Rotate(start.Mass.Properties.CenterOfMassBody);
            var initialAbsoluteVelocity = referenceStartVelocity + initial.Datum.Velocity
                + initial.Datum.Orientation.Rotate(initial.Datum.AngularVelocityBody.Cross(start.Mass.Properties.CenterOfMassBody));
            double subStart = 0, subEnd = dt;
            double localTime = 0; var side = MassBoundarySide.Right;
            var lastProperBody = Vector3d.Zero;
            string lastRegime = "conservative-unsettled";
            Slosh6DofProfile Profile(double local)
            {
                if (local == subStart) side = MassBoundarySide.Right;
                else if (local >= subEnd - 1e-12 * dt) { local = subEnd; side = MassBoundarySide.Left; }
                else { local = System.Math.Max(subStart, local); side = MassBoundarySide.Right; }
                localTime = local;
                return model.Sample(local, side);
            }
            Slosh6DofStageLoads Loads(Slosh6DofStage stage, double epoch)
            {
                var datum = stage.State.Datum;
                var (refP, refV) = BodyStateAt(reference, epoch);
                var offset = datum.Orientation.Rotate(stage.Mass.Properties.CenterOfMassBody);
                var material = new RigidBody6DofState(refP + datum.Position + offset,
                    refV + datum.Velocity + datum.AngularVelocityWorld.Cross(offset), datum.Orientation, datum.AngularVelocityBody);
                // Existing contact geometry is defined from the primary's locked part centroid.
                // Evaluate its material state there, then move that wrench to the actual composite centroid.
                var contactOffset = datum.Orientation.Rotate(vessel.Parts.CenterOfMass);
                var contactState = new RigidBody6DofState(refP + datum.Position + contactOffset,
                    refV + datum.Velocity + datum.AngularVelocityWorld.Cross(contactOffset), datum.Orientation, datum.AngularVelocityBody);
                var landing = EvaluateLandingContact(vessel, reference, contactState);
                var catchContact = EvaluateCatchContact(vessel, contactState, epoch);
                var water = EvaluateCoupledWaterContact(vessel, reference, contactState, epoch);
                var force = (landing?.ForceWorld ?? default) + (catchContact?.ForceWorld ?? default) + (water?.ForceWorld ?? default);
                var torque = (landing?.TorqueWorld ?? default) + (catchContact?.TorqueWorld ?? default)
                    + (water?.TorqueWorld ?? default) + (contactState.Position - material.Position).Cross(force);
                var context = new RigidBodyForceContext(_bodies, reference, force, torque,
                    DistributedAerodynamicsEnabled, PhysicalBodyFlapsEnabled, GravityGradientEnabled);
                var evaluated = RigidBodyForceEvaluator.Evaluate(vessel, material, context, stage.Mass.Properties);
                var gravity = vessel.ComputeGravityAt(material.Position, _bodies);
                var appliedForce = evaluated.ForceWorld - gravity * stage.Mass.Properties.Mass;
                var worldToBody = datum.Orientation.Inverse();
                var applied = new Slosh6DofLoads(appliedForce,
                    evaluated.TorqueBody + stage.Mass.Properties.CenterOfMassBody.Cross(worldToBody.Rotate(appliedForce)),
                    gravity - frameAcceleration);
                Vector3d DifferentialGravity(Vector3d point)
                {
                    var result = Vector3d.Zero;
                    if (!GravityGradientEnabled) return result;
                    foreach (var attractor in _bodies)
                        if (attractor.GM > 0) result += GravityGradient.DifferentialAccelerationBody(attractor.GM,
                            worldToBody.Rotate(material.Position - attractor.Position), point - stage.Mass.Properties.CenterOfMassBody);
                    return result;
                }
                lastProperBody = worldToBody.Rotate(appliedForce / stage.Mass.Properties.Mass);
                lastRegime = model.DescribeRegime(stage, lastProperBody);
                var internalAcceleration = model.InternalAccelerations(stage, lastProperBody, DifferentialGravity);
                return new(applied, transport.Sample(localTime, side, stage.Profile.Baseline, datum.AngularVelocityBody), internalAcceleration);
            }
            Slosh6DofState CrossCut(Slosh6DofState state, double time)
            {
                var left = Slosh6DofIntegrator.Sample(state, model.Sample(time, MassBoundarySide.Left));
                var right = Slosh6DofIntegrator.Sample(state, model.Sample(time, MassBoundarySide.Right));
                var leftFeed = transport.Sample(time, MassBoundarySide.Left, left.Profile.Baseline, state.Datum.AngularVelocityBody);
                var rightFeed = transport.Sample(time, MassBoundarySide.Right, right.Profile.Baseline, state.Datum.AngularVelocityBody);
                var transferred = SloshMomentumTransfer.Conserve(state.Datum, left.Mass.Properties,
                    SloshInternalMomenta.Sample(left.Profile.Baseline, leftFeed, left.Corrections), right.Mass.Properties,
                    SloshInternalMomenta.Sample(right.Profile.Baseline, rightFeed, right.Corrections));
                return right.State with { Datum = transferred };
            }
            var final = vessel.Parts.IntegrateEngineIntervals(initial, dt, boundaries, (state, offset, interval) =>
            {
                if (offset > 0) state = CrossCut(state, offset);
                subStart = offset; subEnd = System.Math.Min(dt, offset + interval);
                double knotDistance = 1e-9 * dt;
                foreach (double knot in boundaries)
                {
                    double distance = System.Math.Abs(knot - (offset + interval));
                    if (knot > offset && distance <= knotDistance) { subEnd = knot; knotDistance = distance; }
                }
                double intervalEnd = subEnd;
                localTime = offset; side = MassBoundarySide.Right;
                var beforePolicy = Slosh6DofIntegrator.Sample(state, model.Sample(offset, MassBoundarySide.Right));
                Loads(beforePolicy, CurrentTime + offset); // Pure opening force at this selected engine interval.
                model.ConfigureContinuations(offset, intervalEnd, lastProperBody);
                var afterPolicy = Slosh6DofIntegrator.Sample(state, model.Sample(offset, MassBoundarySide.Right));
                if (!beforePolicy.Profile.Tanks.Select(t => t.Parameters).SequenceEqual(afterPolicy.Profile.Tanks.Select(t => t.Parameters)))
                {
                    var feed = transport.Sample(offset, MassBoundarySide.Right, beforePolicy.Profile.Baseline, state.Datum.AngularVelocityBody);
                    var transferred = SloshMomentumTransfer.Conserve(state.Datum, beforePolicy.Mass.Properties,
                        SloshInternalMomenta.Sample(beforePolicy.Profile.Baseline, feed, beforePolicy.Corrections),
                        afterPolicy.Mass.Properties, SloshInternalMomenta.Sample(afterPolicy.Profile.Baseline, feed, afterPolicy.Corrections));
                    state = afterPolicy.State with { Datum = transferred };
                }
                var cuts = model.ContinuationCuts(offset, intervalEnd).Append(intervalEnd);
                double cursor = offset;
                foreach (double cut in cuts)
                {
                    subStart = cursor; subEnd = cut;
                    state = Slosh6DofIntegrator.Step(state, cursor, cut - cursor, Profile,
                        (stage, local) => Loads(stage, CurrentTime + local));
                    if (cut < intervalEnd) state = CrossCut(state, cut);
                    cursor = cut;
                }
                return state;
            });
            var ending = model.Sample(dt, MassBoundarySide.Left);
            var end = Slosh6DofIntegrator.Sample(final, ending);
            model.Commit(end.State, ending);
            var datumFinal = ToAbsoluteState(reference, final.Datum);
            vessel.Position = datumFinal.Position; vessel.Velocity = datumFinal.Velocity;
            vessel.Orientation = datumFinal.Orientation; vessel.AngularVelocity = datumFinal.AngularVelocityWorld;
            vessel.LastTransportSample = transport.Sample(dt, MassBoundarySide.Left, ending.Baseline, final.Datum.AngularVelocityBody);
            vessel.TransportRelativeMomentumBody = vessel.LastTransportSample.Value.RelativeLinearMomentumBodyKgMps;
            vessel.TransportRelativeAngularMomentumBody = vessel.LastTransportSample.Value.RelativeAngularMomentumBodyKgM2PerS;
            funded.CaptureMomentumShares(dt, MassBoundarySide.Left, vessel);
            vessel.LastSloshStatus = lastRegime;
            foreach (var member in SloshMembers(vessel) ?? [])
            {
                member.Vessel.TransportRelativeMomentumBody = member.Vessel.TransportRelativeAngularMomentumBody = Vector3d.Zero;
                (member.Vessel.TransportMomentumShares ??= []).Clear();
            }
            var finalOffset = datumFinal.Orientation.Rotate(end.Mass.Properties.CenterOfMassBody);
            LastCoupled6DofTelemetry = new(true, evaluationTime, dt, end.Mass.Properties.Mass,
                initialAbsoluteCenter, datumFinal.Position + finalOffset, initialAbsoluteVelocity,
                datumFinal.Velocity + datumFinal.AngularVelocityWorld.Cross(finalOffset), datumFinal.Orientation, datumFinal.AngularVelocityWorld);
            var contactFinalOffset = datumFinal.Orientation.Rotate(vessel.Parts.CenterOfMass);
            var contactFinal = new RigidBody6DofState(datumFinal.Position + contactFinalOffset,
                datumFinal.Velocity + datumFinal.AngularVelocityWorld.Cross(contactFinalOffset), datumFinal.Orientation, datumFinal.AngularVelocityBody);
            var contact = EvaluateLandingContact(vessel, reference, contactFinal);
            vessel.LastSurfaceContact = contact;
            vessel.LastContactForceWorld = contact?.ForceWorld ?? default;
            vessel.LastContactTorqueWorld = contact?.TorqueWorld ?? default;
            var catchFinal = EvaluateCatchContact(vessel, contactFinal, evaluationTime);
            vessel.LastCatchContact = catchFinal;
            UpdateLandingContactState(vessel, reference, contact, dt); UpdateCatchContactState(vessel, catchFinal, dt);
            vessel.LastWaterContact = EvaluateCoupledWaterContact(vessel, reference, contactFinal, evaluationTime);
            if (vessel.LastWaterContact is { } waterAfter)
            {
                vessel.LastContactForceWorld += waterAfter.ForceWorld;
                vessel.LastContactTorqueWorld += waterAfter.TorqueWorld;
            }
        }
        finally { vessel.EndCoupledPhysicsStep(); }
    }

    private void AdvanceCoastingSlosh(Vessel vessel, CelestialBody reference, double startTime, double dt)
    {
        var capture = new ResourceFundingCapture(vessel.Parts.PartList, dt, false, vessel.Parts.TopologyVersion);
        var window = capture.Complete(vessel.Parts.PartList, vessel.Parts.TopologyVersion);
        if (!VariableMassStepModel.TryBuild(vessel.Parts, window, vessel, DockedMembers(vessel, includeSlosh: false),
            out var funded, out var reason, includeConstantStores: true) || funded == null)
            throw new InvalidOperationException("A constant slosh coast could not be sampled: " + reason);
        var model = new SloshStepModel(vessel, funded, SloshMembers(vessel));
        var profile = model.Sample(0, MassBoundarySide.Right);
        var initial = new Slosh6DofState(new(default, default, vessel.Orientation,
            vessel.Orientation.Inverse().Rotate(vessel.AngularVelocity)), model.InitialStates());
        var stage = Slosh6DofIntegrator.Sample(initial, profile);
        var relative = SloshInternalMomenta.Sample(profile.Baseline, default, stage.Corrections);
        var c = stage.Mass.Properties.CenterOfMassBody;
        initial = stage.State with { Datum = new(-vessel.Orientation.Rotate(c),
            -vessel.Orientation.Rotate(initial.Datum.AngularVelocityBody.Cross(c)
                + relative.LinearBodyKgMps / stage.Mass.Properties.Mass),
            vessel.Orientation, initial.Datum.AngularVelocityBody) };
        Slosh6DofStageLoads Loads(Slosh6DofStage candidate, double local)
        {
            if (!GravityGradientEnabled) return default;
            double epoch = startTime + local;
            var (relativePosition, _) = KeplerPropagator.PropagateToTime(vessel.OrbitalState!, epoch, reference.GM);
            var (refPosition, _) = BodyStateAt(reference, epoch);
            var center = refPosition + relativePosition;
            var q = candidate.State.Datum.Orientation; var inverse = q.Inverse();
            var torque = Vector3d.Zero;
            var fields = new List<(double GM, Vector3d R)>();
            foreach (var attractor in _bodies)
            {
                if (attractor.GM == 0) continue;
                var (attractorPosition, _) = BodyStateAt(attractor, epoch);
                var r = inverse.Rotate(center - attractorPosition);
                torque += GravityGradient.ComputeTorqueBody(attractor.GM, r, candidate.Mass.Properties.InertiaBody);
                fields.Add((attractor.GM, r));
            }
            Vector3d Differential(Vector3d point)
            {
                var acceleration = Vector3d.Zero;
                foreach (var field in fields) acceleration += GravityGradient.DifferentialAccelerationBody(field.GM,
                    field.R, point - candidate.Mass.Properties.CenterOfMassBody);
                return acceleration;
            }
            return new(new(default, torque, default), InternalModalAccelerationBody:
                model.InternalAccelerations(candidate, default, Differential));
        }
        var final = CoastingSloshIntegrator.Advance(initial, 0, dt, profile, Loads);
        model.Commit(final, profile);
        vessel.Orientation = final.Datum.Orientation; vessel.AngularVelocity = final.Datum.AngularVelocityWorld;
        vessel.LastSloshStatus = "conservative-coast";
    }

    private Vector3d SloshMeanVelocityCorrection(Vessel vessel)
    {
        if (vessel.SloshStates.Count == 0 && SloshMembers(vessel)?.Any(m => m.Vessel.SloshStates.Count > 0) != true)
            return Vector3d.Zero;
        var own = SloshVesselState.Sample(vessel); double mass = own.Mass.Mass;
        var p = own.Internal.LinearBodyKgMps;
        foreach (var member in SloshMembers(vessel) ?? [])
        {
            var secondary = SloshVesselState.Sample(member.Vessel);
            mass += secondary.Mass.Mass; p += member.OrientationPrimaryBody.Rotate(secondary.Internal.LinearBodyKgMps);
        }
        return vessel.Orientation.Rotate(p / mass);
    }

    private SloshInternalMomenta PreviousFeedMomenta(Vessel primary)
    {
        var result = From(primary, default, Quaterniond.Identity);
        foreach (var member in SloshMembers(primary) ?? [])
            result = Add(result, From(member.Vessel, member.DatumPrimaryBodyM, member.OrientationPrimaryBody));
        return result;
        static SloshInternalMomenta From(Vessel owner, Vector3d datum, Quaterniond frame)
        {
            if (!owner.TryGetTransportMomentum(owner.Parts.CenterOfMass, out var p, out var h)) return default;
            var linear = frame.Rotate(p);
            return new(linear, frame.Rotate(h + owner.Parts.CenterOfMass.Cross(p)) + datum.Cross(linear));
        }
    }

    private static SloshInternalMomenta Add(SloshInternalMomenta a, SloshInternalMomenta b) =>
        new(a.LinearBodyKgMps + b.LinearBodyKgMps, a.AngularDatumBodyKgM2PerS + b.AngularDatumBodyKgM2PerS);
}
