namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;

public readonly record struct SloshMassSnapshot(RigidBodyMassProperties Properties,
    Vector3d CenterRateBodyMps, Vector3d CenterAccelerationKnownBodyMps2);

/// <summary>
/// Candidate-stage Reynolds/Newton balance for scheduled first modes over an F4 funded
/// mass sample. Source-to-mode internal redistribution is explicit; ordinary thrust and
/// F4 outflow are not debited or applied again. No Universe state is changed by this solve.
/// </summary>
public static class SloshVariableMassDynamics
{
    public static SloshMassSnapshot MassProperties(BodyMassSample baseline,
        IReadOnlyList<SloshTransportCorrection> corrections)
    {
        var first = Vector3d.Zero; var firstRate = Vector3d.Zero; var firstAcceleration = Vector3d.Zero;
        var inertia = baseline.InertiaBodyKgM2;
        foreach (var mode in corrections)
        {
            first += mode.FirstMomentDeltaBodyKgM;
            firstRate += mode.RelativeLinearMomentumBodyKgMps;
            firstAcceleration += mode.RelativeLinearMomentumRateKnownBodyN;
            inertia = Slosh6DofDynamics.Add(inertia, mode.InertiaDeltaDatumBodyKgM2);
        }
        var deltaCenter = first / baseline.MassKg;
        inertia = Slosh6DofDynamics.Add(inertia,
            SloshTransportCorrection.ParallelAxisRate(baseline.CenterOfMassBodyM, first, -1));
        inertia = Slosh6DofDynamics.Add(inertia, Slosh6DofDynamics.ParallelAxis(deltaCenter, -baseline.MassKg));
        var mass = new RigidBodyMassProperties(baseline.MassKg, baseline.CenterOfMassBodyM + deltaCenter, inertia);
        var deltaRate = (firstRate - deltaCenter * baseline.MassRateKgPerS) / baseline.MassKg;
        var deltaAcceleration = (firstAcceleration - deltaRate * (2 * baseline.MassRateKgPerS)) / baseline.MassKg;
        return new(mass, baseline.CenterOfMassRateBodyMps + deltaRate,
            baseline.CenterOfMassAccelerationBodyMps2 + deltaAcceleration);
    }

    /// <summary>
    /// Applied torque is about the datum and excludes the separately supplied uniform
    /// gravity moment. Modal acceleration loads are internal forces divided by modal
    /// mass; the zero-mass endpoint uses their finite kinematic limit, not fictitious mass.
    /// </summary>
    public static Slosh6DofAcceleration Evaluate(BodyMassSample baseline, OpenSystemTransportSample feed,
        Quaterniond orientation, Vector3d angularVelocityBody,
        IReadOnlyList<SloshTransportCorrection> corrections, Slosh6DofLoads loads,
        IReadOnlyList<Vector3d>? internalModalAccelerationBody = null)
    {
        if (loads.GeneralizedModalForces != null)
            throw new ArgumentException("Scheduled modes use physical acceleration loads, not chart forces.", nameof(loads));
        if (internalModalAccelerationBody != null && internalModalAccelerationBody.Count != corrections.Count)
            throw new ArgumentException("One internal acceleration load is required per mode.", nameof(internalModalAccelerationBody));
        if (!(orientation.NormSquared > 0) || !double.IsFinite(orientation.NormSquared))
            throw new ArgumentException("Orientation must be finite and nonzero.", nameof(orientation));
        var q = orientation.Normalize();
        var w = angularVelocityBody;
        DynamicsInputs.RequireFinite(w, nameof(angularVelocityBody));
        DynamicsInputs.RequireFinite(loads.AppliedForceWorld, nameof(loads));
        DynamicsInputs.RequireFinite(loads.AppliedTorqueDatumBody, nameof(loads));
        DynamicsInputs.RequireFinite(loads.GravityWorld, nameof(loads));
        var actual = MassProperties(baseline, corrections).Properties;
        var firstMoment = actual.CenterOfMassBody * actual.Mass;
        var inertiaDatum = Slosh6DofDynamics.Add(actual.InertiaBody,
            Slosh6DofDynamics.ParallelAxis(actual.CenterOfMassBody, actual.Mass));
        var g = q.Inverse().Rotate(loads.GravityWorld);
        var feedForceDatum = feed.ForceBodyN - (baseline.CenterOfMassAccelerationBodyMps2
            + w.Cross(baseline.CenterOfMassRateBodyMps) * 2) * baseline.MassKg;
        var feedTorqueDatum = feed.TorqueBodyNm + baseline.CenterOfMassBodyM.Cross(feedForceDatum);
        var forceRight = q.Inverse().Rotate(loads.AppliedForceWorld) + g * actual.Mass + feedForceDatum
            - w.Cross(w.Cross(firstMoment));
        var torqueRight = loads.AppliedTorqueDatumBody + firstMoment.Cross(g) + feedTorqueDatum
            - w.Cross(inertiaDatum.Multiply(w));
        var modes = new SloshKineticMode[corrections.Count];
        var right = new SloshModalAcceleration[corrections.Count];
        for (int i = 0; i < corrections.Count; i++)
        {
            var mode = corrections[i];
            forceRight -= mode.RelativeLinearMomentumRateKnownBodyN
                + w.Cross(mode.RelativeLinearMomentumBodyKgMps) * 2;
            torqueRight -= mode.InertiaRateDeltaDatumBodyKgM2PerS.Multiply(w)
                + w.Cross(mode.RelativeAngularMomentumDatumBodyKgM2PerS)
                + mode.RelativeAngularMomentumRateKnownDatumBodyNm;
            var drive = g - w.Cross(w.Cross(mode.PositionBodyM)) - w.Cross(mode.VelocityBodyMps) * 2
                - mode.AccelerationBiasBodyMps2 + (internalModalAccelerationBody?[i] ?? Vector3d.Zero);
            DynamicsInputs.RequireFinite(drive, nameof(corrections));
            modes[i] = new(mode.ModalMassKg, mode.PositionBodyM, mode.TangentX, mode.TangentZ);
            right[i] = new(mode.TangentX.Dot(drive), mode.TangentZ.Dot(drive));
        }
        return SloshKineticSystem.Solve(actual, modes, right, forceRight, torqueRight, q);
    }
}
