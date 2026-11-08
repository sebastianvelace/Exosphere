namespace Exosphere.Simulation.Environment;

using Exosphere.Simulation.Math;

/// <summary>
/// The Moon's dated orientation (environment plan E3.1): the DE440 principal-axes frame from
/// the integrated librations (<c>moon/lunar_pa.bin</c>, 3-1-3 Euler angles of ICRF → PA,
/// R = R3(ψ) R1(θ) R3(φ)), rotated into the simulation frame. This is the frame GRGM1200A's
/// coefficients are defined in (DE430 PA, within ~1e-6 rad; see the product's provenance). The
/// spin axis and rate are those of the instantaneous angular velocity from the Euler rates.
/// Simulation time is TDB seconds since J2000.
/// </summary>
public sealed class LunarPrincipalAxesOrientation : IDatedBodyOrientation
{
    private readonly ChebyshevSeries _angles;
    private double _cachedTime = double.NaN;
    private Matrix3x3d _cachedMatrix;
    private Vector3d _cachedOmega;

    public LunarPrincipalAxesOrientation(EnvironmentProductFile product)
    {
        if (product.Product != "lunar_pa")
            throw new InvalidDataException($"Expected the lunar_pa product, got '{product.Product}'.");
        _angles = new ChebyshevSeries(product);
    }

    public static LunarPrincipalAxesOrientation LoadFromDataDirectory(string dataDirectory) =>
        new(EnvironmentProductFile.Load(Path.Combine(dataDirectory, "environment", "moon", "lunar_pa.bin")));

    public string Id => "de440-principal-axes";

    public Matrix3x3d BodyFixedToInertial(CelestialBody body, double time)
    {
        Refresh(time);
        return _cachedMatrix;
    }

    public Vector3d SpinAxis(CelestialBody body)
    {
        Refresh(body.StateTime);
        return _cachedOmega.Normalized;
    }

    public double AngularSpeed(CelestialBody body)
    {
        Refresh(body.StateTime);
        return _cachedOmega.Magnitude;
    }

    public double RotationAngleAt(CelestialBody body, double time)
    {
        _angles.Evaluate(time, out var angles, out _);
        return angles.Z; // ψ, the spin angle about the principal z axis
    }

    /// <summary>ICRF → principal axes rotation (frame rotation, SOFA convention) at a TDB instant.</summary>
    public Matrix3x3d IcrfToPrincipalAxes(double time)
    {
        _angles.Evaluate(time, out var angles, out _);
        return Euler313(angles.X, angles.Y, angles.Z);
    }

    private void Refresh(double time)
    {
        if (time == _cachedTime) return;
        _angles.Evaluate(time, out var angles, out var rates);
        double phi = angles.X, theta = angles.Y, psi = angles.Z;
        var icrfToPa = Euler313(phi, theta, psi);
        var paToSim = IersEarthOrientation.Multiply(IersEarthOrientation.GcrsToSimulation,
            IersEarthOrientation.Transpose(icrfToPa));
        // Angular velocity in body axes for a 3-1-3 sequence, then into the simulation frame.
        double sinT = System.Math.Sin(theta), cosT = System.Math.Cos(theta);
        double sinP = System.Math.Sin(psi), cosP = System.Math.Cos(psi);
        var omegaBody = new Vector3d(
            rates.X * sinT * sinP + rates.Y * cosP,
            rates.X * sinT * cosP - rates.Y * sinP,
            rates.X * cosT + rates.Z);
        _cachedMatrix = paToSim;
        _cachedOmega = paToSim.Multiply(omegaBody);
        _cachedTime = time;
    }

    private static Matrix3x3d Euler313(double phi, double theta, double psi) =>
        IersEarthOrientation.Multiply(IersEarthOrientation.RotationZ(psi),
            IersEarthOrientation.Multiply(IersEarthOrientation.RotationX(theta), IersEarthOrientation.RotationZ(phi)));
}
