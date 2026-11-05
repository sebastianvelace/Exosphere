namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;

/// <summary>
/// A positive outward mass flux through a point or a uniformly loaded circular exit plane.
/// Geometry is measured from the instantaneous CoM in the same body frame as angular velocity.
/// </summary>
public readonly struct MassOutflow
{
    public const double UniformDiskSecondMomentFactor = 0.25;

    public double MassFlowKgPerS { get; }
    public Vector3d PositionFromCenterOfMassBodyM { get; }
    public Vector3d ExitNormalBody { get; }
    public double ExitRadiusM { get; }

    public MassOutflow(double massFlowKgPerS, Vector3d positionFromCenterOfMassBodyM,
        Vector3d exitNormalBody = default, double exitRadiusM = 0)
    {
        if (!double.IsFinite(massFlowKgPerS) || massFlowKgPerS < 0)
            throw new ArgumentOutOfRangeException(nameof(massFlowKgPerS));
        if (!double.IsFinite(exitRadiusM) || exitRadiusM < 0)
            throw new ArgumentOutOfRangeException(nameof(exitRadiusM));
        DynamicsInputs.RequireFinite(positionFromCenterOfMassBodyM, nameof(positionFromCenterOfMassBodyM));
        DynamicsInputs.RequireFinite(exitNormalBody, nameof(exitNormalBody));
        double normalLength = exitNormalBody.Magnitude;
        if (exitRadiusM > 0 && (!double.IsFinite(normalLength) || normalLength <= 0))
            throw new ArgumentException("A finite-area exit needs a nonzero normal.", nameof(exitNormalBody));
        MassFlowKgPerS = massFlowKgPerS;
        PositionFromCenterOfMassBodyM = positionFromCenterOfMassBodyM;
        ExitRadiusM = exitRadiusM;
        ExitNormalBody = exitRadiusM > 0 ? exitNormalBody / normalLength : Vector3d.Zero;
    }

    /// <summary>
    /// Negative angular-momentum flux: -mdot integral r x (omega x r) over the exit.
    /// Finite exit area transports axial spin even when its centroid is on the spin axis.
    /// The ordinary r x thrust moment is deliberately not included here.
    /// </summary>
    public Vector3d JetDampingTorqueBody(Vector3d angularVelocityBody)
    {
        DynamicsInputs.RequireFinite(angularVelocityBody, nameof(angularVelocityBody));
        if (MassFlowKgPerS == 0) return Vector3d.Zero;
        var arm = PositionFromCenterOfMassBodyM;
        var flux = arm.Cross(angularVelocityBody.Cross(arm));
        if (ExitRadiusM > 0)
            flux += (angularVelocityBody + ExitNormalBody * angularVelocityBody.Dot(ExitNormalBody))
                * (UniformDiskSecondMomentFactor * ExitRadiusM * ExitRadiusM);
        return flux * -MassFlowKgPerS;
    }
}

/// <summary>Open-system rotational transport, separate from external and thrust moments.</summary>
public static class VariableMassRotationalDynamics
{
    /// <summary>
    /// Returns -I_dot omega - J_out omega - omega x h_rel - (dh_rel/dt)_body
    /// - c_dot x p_rel. Relative-flow velocities are measured in the material body frame;
    /// c_dot is the central mass station's velocity in that frame, not inertial CoM velocity.
    /// Euler's omega x (I omega) remains the integrator's responsibility. I_dot is a
    /// body-frame derivative of the instantaneous central inertia, in kg m^2/s.
    /// Setting the relative-flow terms to zero requires an explicit negligible-flow or
    /// axisymmetric/no-swirl assumption; this method does not infer one from a vehicle name.
    /// </summary>
    public static Vector3d ComputeTransportTorqueBody(Vector3d angularVelocityBody,
        Matrix3x3d inertiaRateBody, IReadOnlyList<MassOutflow> outflows,
        Vector3d relativeAngularMomentumBody = default,
        Vector3d relativeAngularMomentumRateBody = default,
        Vector3d centerOfMassRateBodyMps = default,
        Vector3d relativeLinearMomentumBodyKgMps = default)
    {
        ArgumentNullException.ThrowIfNull(outflows);
        DynamicsInputs.RequireFinite(angularVelocityBody, nameof(angularVelocityBody));
        DynamicsInputs.RequireFinite(inertiaRateBody, nameof(inertiaRateBody));
        DynamicsInputs.RequireFinite(relativeAngularMomentumBody, nameof(relativeAngularMomentumBody));
        DynamicsInputs.RequireFinite(relativeAngularMomentumRateBody, nameof(relativeAngularMomentumRateBody));
        DynamicsInputs.RequireFinite(centerOfMassRateBodyMps, nameof(centerOfMassRateBodyMps));
        DynamicsInputs.RequireFinite(relativeLinearMomentumBodyKgMps, nameof(relativeLinearMomentumBodyKgMps));
        var torque = -inertiaRateBody.Multiply(angularVelocityBody)
            - angularVelocityBody.Cross(relativeAngularMomentumBody) - relativeAngularMomentumRateBody
            - centerOfMassRateBodyMps.Cross(relativeLinearMomentumBodyKgMps);
        for (int i = 0; i < outflows.Count; i++)
            torque += outflows[i].JetDampingTorqueBody(angularVelocityBody);
        return torque;
    }
}
