namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;

/// <summary>
/// Two physical modal coordinates and rates represented by constrained body vectors.
/// Displacement is relative to the scheduled equilibrium point, not the hinge. Unlike
/// pendulum angles this representation has a finite lower-hemisphere empty-store limit.
/// </summary>
public readonly record struct SloshDisplacementState(Vector3d DisplacementBodyM, Vector3d RateBodyMps)
{
    /// <summary>
    /// Retracts an integration candidate onto k*d²=2*d_y and its time derivative.
    /// Both hemispheres are retained; the zero-curvature limit is a plane. This is a
    /// coordinate constraint, not a model of unconstrained fluid motion in low gravity.
    /// </summary>
    public SloshDisplacementKinematics Sample(SloshScalarJet curvature)
    {
        DynamicsInputs.RequireFinite(DisplacementBodyM, nameof(DisplacementBodyM));
        DynamicsInputs.RequireFinite(RateBodyMps, nameof(RateBodyMps));
        if (!double.IsFinite(curvature.Value) || curvature.Value < 0
            || !double.IsFinite(curvature.Rate) || !double.IsFinite(curvature.Acceleration))
            throw new ArgumentException("Curvature must be nonnegative with finite derivatives.", nameof(curvature));
        double k = curvature.Value;
        Vector3d d, n;
        if (k == 0)
        {
            d = new(DisplacementBodyM.X, 0, DisplacementBodyM.Z);
            n = -Vector3d.Up;
        }
        else
        {
            var rawNormal = DisplacementBodyM * k - Vector3d.Up;
            double norm = rawNormal.Magnitude;
            if (!(norm > 0) || !double.IsFinite(norm))
                throw new ArgumentException("The modal candidate has no finite constraint normal.", nameof(DisplacementBodyM));
            n = rawNormal / norm;
            double x = DisplacementBodyM.X / norm, z = DisplacementBodyM.Z / norm;
            double y = n.Y < 0 ? k * (x * x + z * z) / (1 - n.Y) : (1 + n.Y) / k;
            d = new(x, y, z);
        }
        double normalRate = -0.5 * curvature.Rate * d.MagnitudeSquared;
        var v = RateBodyMps - n * n.Dot(RateBodyMps) + n * normalRate;
        double normalAcceleration = -k * v.MagnitudeSquared - 2 * curvature.Rate * d.Dot(v)
            - 0.5 * curvature.Acceleration * d.MagnitudeSquared;
        var bias = n * normalAcceleration;
        // A least-aligned seed avoids a pole; basis changes do not change the state.
        var seed = System.Math.Abs(n.X) <= System.Math.Abs(n.Y)
            && System.Math.Abs(n.X) <= System.Math.Abs(n.Z) ? Vector3d.Right
            : System.Math.Abs(n.Y) <= System.Math.Abs(n.Z) ? Vector3d.Up : new Vector3d(0, 0, 1);
        var ex = (seed - n * n.Dot(seed)).Normalized;
        var ez = n.Cross(ex);
        DynamicsInputs.RequireFinite(d, nameof(DisplacementBodyM));
        DynamicsInputs.RequireFinite(v, nameof(RateBodyMps));
        DynamicsInputs.RequireFinite(bias, nameof(curvature));
        return new(new(d, v), n, ex, ez, bias);
    }
}

public readonly record struct SloshDisplacementKinematics(SloshDisplacementState State,
    Vector3d Normal, Vector3d TangentX, Vector3d TangentZ, Vector3d AccelerationBiasBodyMps2)
{
    public Vector3d Acceleration(SloshModalAcceleration tangentAcceleration) =>
        AccelerationBiasBodyMps2 + TangentX * tangentAcceleration.X + TangentZ * tangentAcceleration.Z;
}
