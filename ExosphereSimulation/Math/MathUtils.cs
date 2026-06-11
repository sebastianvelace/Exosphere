namespace Exosphere.Simulation.Math;

public static class MathUtils
{
    // Physical / astronomical constants
    public const double G          = 6.674e-11;                  // gravitational constant  (m³ kg⁻¹ s⁻²)
    public const double AU         = 1.496e11;                   // astronomical unit       (m)
    public const double DEG_TO_RAD = System.Math.PI / 180.0;
    public const double RAD_TO_DEG = 180.0 / System.Math.PI;

    /// <summary>Wraps an angle in degrees to [0, 360).</summary>
    public static double ClampAngle(double angleDeg)
    {
        angleDeg %= 360.0;
        if (angleDeg < 0.0) angleDeg += 360.0;
        return angleDeg;
    }

    /// <summary>Wraps an angle in radians to [−π, π].</summary>
    public static double WrapAngle(double angleRad)
    {
        angleRad %= (2.0 * System.Math.PI);
        if (angleRad > System.Math.PI)  angleRad -= 2.0 * System.Math.PI;
        if (angleRad < -System.Math.PI) angleRad += 2.0 * System.Math.PI;
        return angleRad;
    }

    /// <summary>Clamps <paramref name="v"/> to [<paramref name="min"/>, <paramref name="max"/>].</summary>
    public static double Clamp(double v, double min, double max) =>
        v < min ? min : v > max ? max : v;

    /// <summary>Linear interpolation between <paramref name="a"/> and <paramref name="b"/> by factor <paramref name="t"/>.</summary>
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>
    /// Solves Kepler's equation  M = E − e·sin(E)  for eccentric anomaly E using Newton-Raphson.
    /// </summary>
    /// <param name="M">Mean anomaly (radians).</param>
    /// <param name="e">Eccentricity (0 ≤ e &lt; 1).</param>
    /// <param name="tolerance">Convergence tolerance (default 1e-10).</param>
    /// <returns>Eccentric anomaly E (radians).</returns>
    public static double SolveKeplerEquation(double M, double e, double tolerance = 1e-10)
    {
        // Wrap M to [0, 2π) for numerical stability
        M = M % (2.0 * System.Math.PI);
        if (M < 0.0) M += 2.0 * System.Math.PI;

        // Initial guess: Danby (1988) starter
        double E = M + e * System.Math.Sin(M) * (1.0 + e * System.Math.Cos(M));

        for (int i = 0; i < 100; i++)
        {
            double sinE  = System.Math.Sin(E);
            double cosE  = System.Math.Cos(E);
            double f     = E - e * sinE - M;
            double fPrime = 1.0 - e * cosE;

            double delta = f / fPrime;
            E -= delta;

            if (System.Math.Abs(delta) < tolerance)
                break;
        }

        return E;
    }

    /// <summary>
    /// Converts Keplerian orbital elements to a 3-D position vector (inertial frame, m).
    /// </summary>
    /// <param name="a">Semi-major axis (m).</param>
    /// <param name="e">Eccentricity.</param>
    /// <param name="trueAnomaly">True anomaly (radians).</param>
    /// <param name="i">Inclination (radians).</param>
    /// <param name="lan">Longitude of ascending node Ω (radians).</param>
    /// <param name="aop">Argument of periapsis ω (radians).</param>
    /// <returns>Position vector relative to parent body (m).</returns>
    public static Vector3d OrbitalToInertial(
        double a, double e, double trueAnomaly,
        double i, double lan, double aop)
    {
        // Radius in orbital plane
        double p = a * (1.0 - e * e);          // semi-latus rectum
        double cosV = System.Math.Cos(trueAnomaly);
        double sinV = System.Math.Sin(trueAnomaly);
        double r    = p / (1.0 + e * cosV);

        // Position in perifocal frame (x̂ toward periapsis, ŷ 90° in orbital plane)
        double xOrb = r * cosV;
        double yOrb = r * sinV;

        // Rotation angles
        double cosO = System.Math.Cos(lan);
        double sinO = System.Math.Sin(lan);
        double cosW = System.Math.Cos(aop);
        double sinW = System.Math.Sin(aop);
        double cosI = System.Math.Cos(i);
        double sinI = System.Math.Sin(i);

        // Rotation matrix columns (perifocal → inertial)
        // Rx = Q column for x-direction
        double Qxx = cosO * cosW - sinO * sinW * cosI;
        double Qyx = sinO * cosW + cosO * sinW * cosI;
        double Qzx = sinW * sinI;

        double Qxy = -cosO * sinW - sinO * cosW * cosI;
        double Qyy = -sinO * sinW + cosO * cosW * cosI;
        double Qzy =  cosW * sinI;

        double x = Qxx * xOrb + Qxy * yOrb;
        double y = Qyx * xOrb + Qyy * yOrb;
        double z = Qzx * xOrb + Qzy * yOrb;

        return new Vector3d(x, y, z);
    }

    /// <summary>
    /// Converts Keplerian orbital elements to position AND velocity in the inertial frame.
    /// </summary>
    public static (Vector3d position, Vector3d velocity) OrbitalToInertialStateVector(
        double a, double e, double trueAnomaly,
        double i, double lan, double aop,
        double gm)
    {
        double p = a * (1.0 - e * e);
        double cosV = System.Math.Cos(trueAnomaly);
        double sinV = System.Math.Sin(trueAnomaly);
        double r    = p / (1.0 + e * cosV);

        // Position in perifocal frame
        double xOrb = r * cosV;
        double yOrb = r * sinV;

        // Velocity in perifocal frame
        double sqrtGMp = System.Math.Sqrt(gm / p);
        double vxOrb = -sqrtGMp * sinV;
        double vyOrb =  sqrtGMp * (e + cosV);

        // Rotation matrix (perifocal → inertial)
        double cosO = System.Math.Cos(lan);
        double sinO = System.Math.Sin(lan);
        double cosW = System.Math.Cos(aop);
        double sinW = System.Math.Sin(aop);
        double cosI = System.Math.Cos(i);
        double sinI = System.Math.Sin(i);

        double Qxx = cosO * cosW - sinO * sinW * cosI;
        double Qyx = sinO * cosW + cosO * sinW * cosI;
        double Qzx = sinW * sinI;

        double Qxy = -cosO * sinW - sinO * cosW * cosI;
        double Qyy = -sinO * sinW + cosO * cosW * cosI;
        double Qzy =  cosW * sinI;

        Vector3d pos = new(
            Qxx * xOrb + Qxy * yOrb,
            Qyx * xOrb + Qyy * yOrb,
            Qzx * xOrb + Qzy * yOrb);

        Vector3d vel = new(
            Qxx * vxOrb + Qxy * vyOrb,
            Qyx * vxOrb + Qyy * vyOrb,
            Qzx * vxOrb + Qzy * vyOrb);

        return (pos, vel);
    }
}
