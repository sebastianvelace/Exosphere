namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Integrators;

/// <summary>
/// Inelastic rigid-carrier capture with free modal tangential velocities. Preserves the
/// complete linear/angular momentum and imposes no tangential impulse on a liquid bob.
/// The captured relative pose is the original pose; this solver performs no geometric snap.
/// </summary>
public static class SloshDockingDynamics
{
    public static (Slosh6DofState Primary, Slosh6DofState Secondary) Capture(
        Slosh6DofStage primary, Slosh6DofStage secondary,
        SloshInternalMomenta primaryFeed = default, SloshInternalMomenta secondaryFeed = default)
    {
        var origin = primary.State.Datum.Position;
        var q = primary.State.Datum.Orientation; var inverse = q.Inverse();
        var offset = inverse.Rotate(secondary.State.Datum.Position - origin);
        var frame = (inverse * secondary.State.Datum.Orientation).Normalize();
        var mass = CompositeMassProperties.Combine(primary.Mass.Properties,
            [new RigidlyAttachedMember(secondary.Mass.Properties, offset, frame)]);
        var modes = new List<SloshKineticMode>(); var modalRight = new List<SloshModalAcceleration>();
        var momentumWorld = Vector3d.Zero; var angularWorld = Vector3d.Zero;
        var knownLinear = Vector3d.Zero; var knownAngular = Vector3d.Zero;
        AddMember(primary, primaryFeed, Vector3d.Zero, Quaterniond.Identity);
        AddMember(secondary, secondaryFeed, offset, frame);
        var solution = SloshKineticSystem.Solve(mass, modes, modalRight,
            inverse.Rotate(momentumWorld) - knownLinear, inverse.Rotate(angularWorld) - knownAngular, q);
        var angularWorldAfter = q.Rotate(solution.AngularAccelerationBody);
        var primaryModes = RestoreRates(primary, 0);
        var secondaryModes = RestoreRates(secondary, primary.Corrections.Count);
        var p = primary.State.Datum; var s = secondary.State.Datum;
        return (new(new(p.Position, solution.DatumAccelerationWorld, p.Orientation,
                    p.Orientation.Inverse().Rotate(angularWorldAfter)), primaryModes),
            new(new(s.Position, solution.DatumAccelerationWorld + angularWorldAfter.Cross(s.Position - origin),
                    s.Orientation, s.Orientation.Inverse().Rotate(angularWorldAfter)), secondaryModes));

        void AddMember(Slosh6DofStage member, SloshInternalMomenta feed, Vector3d datumPrimaryBody, Quaterniond axes)
        {
            var datum = member.State.Datum; var properties = member.Mass.Properties;
            var relative = SloshInternalMomenta.Sample(member.Profile.Baseline, default, member.Corrections);
            relative = new(relative.LinearBodyKgMps + feed.LinearBodyKgMps,
                relative.AngularDatumBodyKgM2PerS + feed.AngularDatumBodyKgM2PerS);
            var centerOffset = datum.Orientation.Rotate(properties.CenterOfMassBody);
            var centerVelocity = datum.Velocity + datum.AngularVelocityWorld.Cross(centerOffset);
            var rigidP = centerVelocity * properties.Mass;
            var relativeP = datum.Orientation.Rotate(relative.LinearBodyKgMps);
            momentumWorld += rigidP + relativeP;
            angularWorld += (datum.Position - origin + centerOffset).Cross(rigidP)
                + datum.Orientation.Rotate(properties.InertiaBody.Multiply(datum.AngularVelocityBody))
                + (datum.Position - origin).Cross(relativeP)
                + datum.Orientation.Rotate(relative.AngularDatumBodyKgM2PerS);
            var knownP = relative.LinearBodyKgMps; var knownH = relative.AngularDatumBodyKgM2PerS;
            for (int i = 0; i < member.Corrections.Count; i++)
            {
                var correction = member.Corrections[i];
                var tankFrame = member.Profile.Tanks[i].TankToBody ?? Quaterniond.Identity;
                var displacementRate = tankFrame.Rotate(member.State.Modes[i].RateBodyMps);
                var tangentRate = correction.TangentX * correction.TangentX.Dot(displacementRate)
                    + correction.TangentZ * correction.TangentZ.Dot(displacementRate);
                knownP -= tangentRate * correction.ModalMassKg;
                knownH -= correction.PositionBodyM.Cross(tangentRate) * correction.ModalMassKg;
                var position = datumPrimaryBody + axes.Rotate(correction.PositionBodyM);
                var ex = axes.Rotate(correction.TangentX); var ez = axes.Rotate(correction.TangentZ);
                modes.Add(new(correction.ModalMassKg, position, ex, ez));
                var oldBobVelocity = inverse.Rotate(datum.Velocity + datum.Orientation.Rotate(
                    datum.AngularVelocityBody.Cross(correction.PositionBodyM) + correction.VelocityBodyMps));
                var prescribedVelocity = axes.Rotate(correction.VelocityBodyMps - tangentRate);
                modalRight.Add(new(ex.Dot(oldBobVelocity - prescribedVelocity), ez.Dot(oldBobVelocity - prescribedVelocity)));
            }
            var transformedP = axes.Rotate(knownP);
            knownLinear += transformedP;
            knownAngular += axes.Rotate(knownH) + datumPrimaryBody.Cross(transformedP);
        }

        SloshDisplacementState[] RestoreRates(Slosh6DofStage member, int first)
        {
            var result = new SloshDisplacementState[member.State.Modes.Count];
            for (int i = 0; i < result.Length; i++)
            {
                var correction = member.Corrections[i];
                var tankFrame = member.Profile.Tanks[i].TankToBody ?? Quaterniond.Identity;
                var oldRate = tankFrame.Rotate(member.State.Modes[i].RateBodyMps);
                var normalRate = oldRate - correction.TangentX * correction.TangentX.Dot(oldRate)
                    - correction.TangentZ * correction.TangentZ.Dot(oldRate);
                var u = solution.ModalAccelerations[first + i];
                result[i] = member.State.Modes[i] with { RateBodyMps = tankFrame.Inverse().Rotate(normalRate
                    + correction.TangentX * u.X + correction.TangentZ * u.Z) };
            }
            return result;
        }
    }
}
