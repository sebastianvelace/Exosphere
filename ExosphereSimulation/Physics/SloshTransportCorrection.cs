namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;

/// <summary>
/// Smooth modal kinematics and momentum corrections about the fixed body datum.
/// Acceleration-rate fields exclude the unknown two modal accelerations. Internal
/// redistribution follows the declared straight equilibrium-to-mode route.
/// </summary>
public readonly record struct SloshTransportCorrection(
    double ModalMassKg, Vector3d PositionBodyM, Vector3d VelocityBodyMps,
    Vector3d AccelerationBiasBodyMps2, Vector3d TangentX, Vector3d TangentZ,
    Vector3d FirstMomentDeltaBodyKgM, Matrix3x3d InertiaDeltaDatumBodyKgM2,
    Matrix3x3d InertiaRateDeltaDatumBodyKgM2PerS,
    Vector3d RelativeLinearMomentumBodyKgMps, Vector3d RelativeLinearMomentumRateKnownBodyN,
    Vector3d RelativeAngularMomentumDatumBodyKgM2PerS, Vector3d RelativeAngularMomentumRateKnownDatumBodyNm)
{
    /// <summary>
    /// Samples a settled lower-chart displacement in metres using inverse length.
    /// All coefficients, including their one-sided rates, belong to the same funded
    /// mass sample. This computes no debit and does not itself advance a modal state.
    /// </summary>
    public static SloshTransportCorrection Sample(CylindricalSloshParameters parameters,
        Vector3d floorBodyM, SloshModalState state)
    {
        DynamicsInputs.RequireFinite(floorBodyM, nameof(floorBodyM));
        var mu = parameters.ModalMassKg; var curvature = parameters.CurvaturePerM;
        var height = parameters.HeightAboveFloorM;
        foreach (var jet in new[] { mu, curvature, height })
            if (!double.IsFinite(jet.Value) || !double.IsFinite(jet.Rate) || !double.IsFinite(jet.Acceleration))
                throw new ArgumentException("Modal coefficients and derivatives must be finite.", nameof(parameters));
        if (mu.Value < 0 || curvature.Value < 0)
            throw new ArgumentException("Modal mass and inverse length must be nonnegative.", nameof(parameters));
        DynamicsInputs.RequireFinite(new Vector3d(state.X, state.Z, state.XRate), nameof(state));
        if (!double.IsFinite(state.ZRate)) throw new ArgumentException("Modal rates must be finite.", nameof(state));
        double k = curvature.Value;
        double radiusSquared = state.X * state.X + state.Z * state.Z;
        double cx = k * state.X, cz = k * state.Z;
        double chartRadiusSquared = cx * cx + cz * cz;
        if (!(chartRadiusSquared < 1)) throw new ArgumentException("Modal displacement leaves its lower chart.", nameof(state));
        double s = System.Math.Sqrt(1 - chartRadiusSquared), s3 = s * s * s;
        double dot = state.X * state.XRate + state.Z * state.ZRate;
        double speedSquared = state.XRate * state.XRate + state.ZRate * state.ZRate;
        double heightK = radiusSquared / (s * (1 + s));
        double heightKK = k * radiusSquared * radiusSquared * (1 + 2 * s)
            / (s3 * (1 + s) * (1 + s));
        var displacement = new Vector3d(state.X, k * radiusSquared / (1 + s), state.Z);
        var ex = new Vector3d(1, cx / s, 0); var ez = new Vector3d(0, cz / s, 1);
        var displacementRate = ex * state.XRate + ez * state.ZRate
            + Vector3d.Up * (heightK * curvature.Rate);
        double biasY = k * speedSquared / s + k * k * k * dot * dot / s3
            + 2 * curvature.Rate * dot / s3 + heightKK * curvature.Rate * curvature.Rate
            + heightK * curvature.Acceleration;
        var displacementBias = Vector3d.Up * biasY;
        return FromKinematics(parameters, floorBodyM, displacement, displacementRate, displacementBias, ex, ez, Vector3d.Up);
    }

    /// <summary>Global displacement form, with tangent accelerations in an orthonormal basis.</summary>
    public static SloshTransportCorrection SampleDisplacement(CylindricalSloshParameters parameters,
        Vector3d floorBodyM, SloshDisplacementState state, Quaterniond? tankToBody = null)
    {
        var kinematics = state.Sample(parameters.CurvaturePerM);
        var q = tankToBody ?? Quaterniond.Identity;
        if (!(q.NormSquared > 0) || !double.IsFinite(q.NormSquared))
            throw new ArgumentException("Tank frame must be finite and nonzero.", nameof(tankToBody));
        q = q.Normalize();
        return FromKinematics(parameters, floorBodyM, q.Rotate(kinematics.State.DisplacementBodyM),
            q.Rotate(kinematics.State.RateBodyMps), q.Rotate(kinematics.AccelerationBiasBodyMps2),
            q.Rotate(kinematics.TangentX), q.Rotate(kinematics.TangentZ), q.Rotate(Vector3d.Up));
    }

    private static SloshTransportCorrection FromKinematics(CylindricalSloshParameters parameters,
        Vector3d floorBodyM, Vector3d displacement, Vector3d displacementRate,
        Vector3d displacementBias, Vector3d ex, Vector3d ez, Vector3d axis)
    {
        DynamicsInputs.RequireFinite(floorBodyM, nameof(floorBodyM));
        var mu = parameters.ModalMassKg; var height = parameters.HeightAboveFloorM;
        if (mu.Value < 0) throw new ArgumentException("Modal mass must be nonnegative.", nameof(parameters));
        foreach (var jet in new[] { mu, height })
            if (!double.IsFinite(jet.Value) || !double.IsFinite(jet.Rate) || !double.IsFinite(jet.Acceleration))
                throw new ArgumentException("Modal coefficients and derivatives must be finite.", nameof(parameters));
        var r0 = floorBodyM + axis * height.Value;
        var r0Rate = axis * height.Rate; var r0Acceleration = axis * height.Acceleration;
        var r = r0 + displacement;
        var relativeLinear = displacement * mu.Rate + displacementRate * mu.Value;
        var linearRate = displacement * mu.Acceleration + displacementRate * (2 * mu.Rate)
            + displacementBias * mu.Value;
        var relativeAngular = (r.Cross(displacementRate) + displacement.Cross(r0Rate)) * mu.Value
            + r0.Cross(displacement) * mu.Rate;
        var angularRate = (r.Cross(displacementBias) + displacement.Cross(r0Acceleration)) * mu.Value
            + (r0 * 2 + displacement).Cross(displacementRate) * mu.Rate
            + r0.Cross(displacement) * mu.Acceleration;
        var deltaInertia = Slosh6DofDynamics.DeltaParallelAxis(r0, displacement, mu.Value);
        var deltaInertiaRate = Slosh6DofDynamics.Add(
            Slosh6DofDynamics.DeltaParallelAxis(r0, displacement, mu.Rate),
            Slosh6DofDynamics.Add(ParallelAxisRate(r, displacementRate, mu.Value),
                ParallelAxisRate(displacement, r0Rate, mu.Value)));
        DynamicsInputs.RequireFinite(r, nameof(parameters));
        DynamicsInputs.RequireFinite(r0Rate + displacementRate, nameof(parameters));
        DynamicsInputs.RequireFinite(r0Acceleration + displacementBias, nameof(parameters));
        DynamicsInputs.RequireFinite(relativeLinear, nameof(parameters)); DynamicsInputs.RequireFinite(linearRate, nameof(parameters));
        DynamicsInputs.RequireFinite(relativeAngular, nameof(parameters)); DynamicsInputs.RequireFinite(angularRate, nameof(parameters));
        DynamicsInputs.RequireFinite(deltaInertia, nameof(parameters)); DynamicsInputs.RequireFinite(deltaInertiaRate, nameof(parameters));
        return new(mu.Value, r, r0Rate + displacementRate, r0Acceleration + displacementBias, ex, ez,
            displacement * mu.Value, deltaInertia, deltaInertiaRate,
            relativeLinear, linearRate, relativeAngular, angularRate);
    }

    internal static Matrix3x3d ParallelAxisRate(Vector3d r, Vector3d v, double m)
    {
        double xx = 2 * m * (r.Y * v.Y + r.Z * v.Z), yy = 2 * m * (r.X * v.X + r.Z * v.Z);
        double zz = 2 * m * (r.X * v.X + r.Y * v.Y);
        double xy = -m * (r.X * v.Y + v.X * r.Y), xz = -m * (r.X * v.Z + v.X * r.Z);
        double yz = -m * (r.Y * v.Z + v.Y * r.Z);
        return new(xx, xy, xz, xy, yy, yz, xz, yz, zz);
    }
}
