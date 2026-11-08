namespace Exosphere.Simulation.Environment;

using Exosphere.Simulation.Math;

/// <summary>
/// Earth's dated orientation (environment plan E1.2b): the body-fixed frame is the ITRS of
/// <see cref="IersEarthOrientation"/>, so longitude is Greenwich longitude and launch sites from
/// <c>data/launch_sites</c> sit where they are on the date. Simulation time is read as TDB seconds
/// since J2000 (the ephemeris time of the dated environment). The spin axis is the CIP and the
/// rate the Earth rotation angle's, scaled by 1 − LOD/86400 s to SI seconds.
/// </summary>
public sealed class IersOrientationProvider : IDatedBodyOrientation
{
    private readonly IersEarthOrientation _iers;
    private double _cachedTime = double.NaN;
    private IersEarthOrientation.EarthOrientationSample _cachedSample;
    private Matrix3x3d _cachedMatrix;

    public IersOrientationProvider(IersEarthOrientation iers) => _iers = iers;

    public string Id => "iers-2010";

    public IersEarthOrientation Model => _iers;

    public Matrix3x3d BodyFixedToInertial(CelestialBody body, double time)
    {
        Refresh(time);
        return _cachedMatrix;
    }

    public Vector3d SpinAxis(CelestialBody body)
    {
        Refresh(body.StateTime);
        double x = _cachedSample.XRad, y = _cachedSample.YRad;
        var cipGcrs = new Vector3d(x, y, System.Math.Sqrt(1.0 - x * x - y * y));
        return IersEarthOrientation.GcrsToSimulation.Multiply(cipGcrs).Normalized;
    }

    public double AngularSpeed(CelestialBody body)
    {
        Refresh(body.StateTime);
        return 2.0 * System.Math.PI * 1.00273781191135448 / TimeScales.SecondsPerDay
            * (1.0 - _cachedSample.LengthOfDayExcessSeconds / TimeScales.SecondsPerDay);
    }

    /// <summary>The Earth rotation angle at <paramref name="time"/>, for diagnostics.</summary>
    public double RotationAngleAt(CelestialBody body, double time)
    {
        Refresh(time);
        return _cachedSample.EarthRotationAngleRad;
    }

    /// <summary>The orientation chain (and its published uncertainty) at a simulation time.</summary>
    public IersEarthOrientation.EarthOrientationSample SampleAt(double time)
    {
        Refresh(time);
        return _cachedSample;
    }

    private void Refresh(double time)
    {
        if (time == _cachedTime) return;
        _cachedSample = _iers.Sample(TimeScales.TdbToTt(time));
        _cachedMatrix = IersEarthOrientation.Multiply(IersEarthOrientation.GcrsToSimulation,
            IersEarthOrientation.Transpose(IersEarthOrientation.CelestialToTerrestrial(_cachedSample)));
        _cachedTime = time;
    }
}
