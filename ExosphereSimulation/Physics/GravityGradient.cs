namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;

/// <summary>First-order Newtonian tidal moment about the spacecraft centre of mass.</summary>
public static class GravityGradient
{
    public const double TidalCoefficient = 3.0;

    /// <summary>Linearized point-attractor acceleration difference G*displacement, in body axes.</summary>
    public static Vector3d DifferentialAccelerationBody(double gravitationalParameterM3PerS2,
        Vector3d positionFromAttractorBodyM, Vector3d displacementFromCenterBodyM)
    {
        if (!double.IsFinite(gravitationalParameterM3PerS2) || gravitationalParameterM3PerS2 < 0)
            throw new ArgumentOutOfRangeException(nameof(gravitationalParameterM3PerS2));
        DynamicsInputs.RequireFinite(positionFromAttractorBodyM, nameof(positionFromAttractorBodyM));
        DynamicsInputs.RequireFinite(displacementFromCenterBodyM, nameof(displacementFromCenterBodyM));
        double radius = positionFromAttractorBodyM.Magnitude;
        if (!(radius > 0) || !double.IsFinite(radius)) throw new ArgumentOutOfRangeException(nameof(positionFromAttractorBodyM));
        var n = positionFromAttractorBodyM / radius;
        double scale = (gravitationalParameterM3PerS2 / radius) / radius / radius;
        return (n * (TidalCoefficient * n.Dot(displacementFromCenterBodyM)) - displacementFromCenterBodyM) * scale;
    }

    /// <summary>
    /// Returns 3 mu/r^3 n x (I n), in N m. The attractor-to-CoM vector and inertia
    /// tensor must use the same body frame. This is a far-field, point-attractor model;
    /// it is neither a geodetic-up approximation nor an empirical damping term.
    /// </summary>
    public static Vector3d ComputeTorqueBody(double gravitationalParameterM3PerS2,
        Vector3d positionFromAttractorBodyM, Matrix3x3d inertiaBody)
    {
        if (!double.IsFinite(gravitationalParameterM3PerS2) || gravitationalParameterM3PerS2 < 0)
            throw new ArgumentOutOfRangeException(nameof(gravitationalParameterM3PerS2));
        DynamicsInputs.RequireFinite(positionFromAttractorBodyM, nameof(positionFromAttractorBodyM));
        DynamicsInputs.RequireFinite(inertiaBody, nameof(inertiaBody));
        double radius = positionFromAttractorBodyM.Magnitude;
        if (!double.IsFinite(radius) || radius <= 0)
            throw new ArgumentOutOfRangeException(nameof(positionFromAttractorBodyM));
        var direction = positionFromAttractorBodyM / radius;
        double scale = TidalCoefficient * (gravitationalParameterM3PerS2 / radius) / radius / radius;
        return direction.Cross(inertiaBody.Multiply(direction)) * scale;
    }
}

internal static class DynamicsInputs
{
    internal static void RequireFinite(Vector3d value, string name)
    {
        if (!double.IsFinite(value.X) || !double.IsFinite(value.Y) || !double.IsFinite(value.Z))
            throw new ArgumentException("Vector must be finite.", name);
    }

    internal static void RequireFinite(Matrix3x3d value, string name)
    {
        if (!double.IsFinite(value.M11) || !double.IsFinite(value.M12) || !double.IsFinite(value.M13)
            || !double.IsFinite(value.M21) || !double.IsFinite(value.M22) || !double.IsFinite(value.M23)
            || !double.IsFinite(value.M31) || !double.IsFinite(value.M32) || !double.IsFinite(value.M33))
            throw new ArgumentException("Tensor must be finite.", name);
    }
}
