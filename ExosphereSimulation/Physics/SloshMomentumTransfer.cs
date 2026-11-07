namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;

/// <summary>Internal relative momenta about the construction datum, in body axes.</summary>
public readonly record struct SloshInternalMomenta(Vector3d LinearBodyKgMps, Vector3d AngularDatumBodyKgM2PerS)
{
    public static SloshInternalMomenta Sample(BodyMassSample baseline, OpenSystemTransportSample feed,
        IReadOnlyList<SloshTransportCorrection> corrections)
    {
        var linear = feed.RelativeLinearMomentumBodyKgMps;
        var angular = feed.RelativeAngularMomentumBodyKgM2PerS + baseline.CenterOfMassBodyM.Cross(linear);
        foreach (var correction in corrections)
        {
            linear += correction.RelativeLinearMomentumBodyKgMps;
            angular += correction.RelativeAngularMomentumDatumBodyKgM2PerS;
        }
        return new(linear, angular);
    }
}

/// <summary>Conservative handoff between prescribed internal-flow states at a fixed datum pose.</summary>
public static class SloshMomentumTransfer
{
    /// <summary>
    /// Retains all six inertial momenta at an instantaneous internal transition. This
    /// operation cannot represent an external impulse, actual mass removal or a pose jump.
    /// Modal dynamic momentum must be supplied on both sides, never cleared as stopped feed.
    /// </summary>
    public static RigidBody6DofState Conserve(RigidBody6DofState datum,
        RigidBodyMassProperties previousMass, SloshInternalMomenta previousInternal,
        RigidBodyMassProperties nextMass, SloshInternalMomenta nextInternal)
    {
        if (previousMass.Mass != nextMass.Mass)
            throw new ArgumentException("An internal handoff cannot change total mass.", nameof(nextMass));
        DynamicsInputs.RequireFinite(previousInternal.LinearBodyKgMps, nameof(previousInternal));
        DynamicsInputs.RequireFinite(previousInternal.AngularDatumBodyKgM2PerS, nameof(previousInternal));
        DynamicsInputs.RequireFinite(nextInternal.LinearBodyKgMps, nameof(nextInternal));
        DynamicsInputs.RequireFinite(nextInternal.AngularDatumBodyKgM2PerS, nameof(nextInternal));
        if (!(datum.Orientation.NormSquared > 0) || !double.IsFinite(datum.Orientation.NormSquared))
            throw new ArgumentException("Orientation must be finite and nonzero.", nameof(datum));
        DynamicsInputs.RequireFinite(datum.Position, nameof(datum));
        DynamicsInputs.RequireFinite(datum.Velocity, nameof(datum));
        DynamicsInputs.RequireFinite(datum.AngularVelocityBody, nameof(datum));
        var q = datum.Orientation.Normalize();
        var v = q.Inverse().Rotate(datum.Velocity); var w = datum.AngularVelocityBody;
        var previousFirst = previousMass.CenterOfMassBody * previousMass.Mass;
        var previousDatumInertia = Slosh6DofDynamics.Add(previousMass.InertiaBody,
            Slosh6DofDynamics.ParallelAxis(previousMass.CenterOfMassBody, previousMass.Mass));
        var p = v * previousMass.Mass + w.Cross(previousFirst) + previousInternal.LinearBodyKgMps;
        var hDatum = previousFirst.Cross(v) + previousDatumInertia.Multiply(w)
            + previousInternal.AngularDatumBodyKgM2PerS;
        var materialP = p - nextInternal.LinearBodyKgMps;
        var nextW = nextMass.InverseInertiaBody.Multiply(hDatum - nextInternal.AngularDatumBodyKgM2PerS
            - nextMass.CenterOfMassBody.Cross(materialP));
        var nextV = materialP / nextMass.Mass - nextW.Cross(nextMass.CenterOfMassBody);
        DynamicsInputs.RequireFinite(nextW, nameof(nextMass)); DynamicsInputs.RequireFinite(nextV, nameof(nextMass));
        return new(datum.Position, q.Rotate(nextV), q, nextW);
    }
}
