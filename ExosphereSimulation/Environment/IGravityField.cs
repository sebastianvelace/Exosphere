namespace Exosphere.Simulation.Environment;

using Exosphere.Simulation.Math;

/// <summary>
/// Gravitational field of one body (environment plan E0.3). <see cref="ZonalJ2Gravity"/> is
/// today's point mass plus first-order J2; spherical-harmonic fields (EGM / GRGM1200A, plan E3)
/// implement the same contract per body.
/// </summary>
public interface IGravityField
{
    /// <summary>Short identifier recorded in saves and telemetry, e.g. "zonal-j2".</summary>
    string Id { get; }

    /// <summary>
    /// Gravitational acceleration (m/s², inertial axes) of <paramref name="body"/> at
    /// <paramref name="worldPosition"/>; zero within 1 m of the body centre.
    /// </summary>
    Vector3d AccelerationAt(CelestialBody body, Vector3d worldPosition);
}

/// <summary>
/// Point mass GM/r² plus, when <see cref="CelestialBody.J2"/> is set, the first-order zonal
/// term in the body equatorial frame (+Z the spin axis). Zonal, so the frame's rotation about
/// the axis does not enter. Bit for bit the field the simulation has always used.
/// </summary>
public sealed class ZonalJ2Gravity : IGravityField
{
    public static readonly ZonalJ2Gravity Instance = new();

    public string Id => "zonal-j2";

    public Vector3d AccelerationAt(CelestialBody body, Vector3d worldPosition)
    {
        var rVec = worldPosition - body.Position;
        double distSq = rVec.MagnitudeSquared;
        if (distSq < 1.0) return Vector3d.Zero;

        if (body.J2 == 0.0)
            return rVec.Normalized * (-body.GM / distSq);

        double r = System.Math.Sqrt(distSq);
        body.GetBodyFixedEquatorialBasis(out var eastOfPrime, out var ninetyEast, out var north);
        double x = rVec.Dot(eastOfPrime);
        double y = rVec.Dot(ninetyEast);
        double z = rVec.Dot(north);
        double muOverR3 = body.GM / (distSq * r);
        double reOverR = body.EquatorialRadius / r;
        double j2Scale = 1.5 * body.J2 * reOverR * reOverR;
        double zr2 = (z / r) * (z / r);
        double ax = -muOverR3 * x * (1.0 - j2Scale * (5.0 * zr2 - 1.0));
        double ay = -muOverR3 * y * (1.0 - j2Scale * (5.0 * zr2 - 1.0));
        double az = -muOverR3 * z * (1.0 - j2Scale * (5.0 * zr2 - 3.0));
        return eastOfPrime * ax + ninetyEast * ay + north * az;
    }
}
