namespace Exosphere.Simulation.Environment;

using Exosphere.Simulation.Math;

/// <summary>
/// Earth orientation by the IERS Conventions 2010 CIO-based chain (environment plan E1.2):
///
///   [ITRS] = W(t) · R3(ERA) · Q(t)ᵀ [GCRS],   W = R1(−y_p) R2(−x_p) R3(s′)  (ERFA order)
///
/// with the CIP coordinates X, Y and CIO locator s of the IAU 2006/2000A model tabulated daily
/// by ERFA (<c>earth/cip_xys.bin</c>, 8-point Lagrange) plus the observed pole offsets dX, dY;
/// the Earth rotation angle of UT1; polar motion x_p, y_p and s′ = −47 µas·T. Observed parameters
/// come from IERS EOP 20 C04 (<c>earth/eop_c04.bin</c>, 4-point Lagrange as in the IERS
/// interpolation routine, UT1 interpolated as UT1 − TAI so leap steps do not enter). After the
/// last EOP row the parameters hold their last values; before the first (1962-01-01) UT1 = UTC
/// and the pole is at the origin. <see cref="Sample"/> reports the published 1σ of the epoch.
///
/// The simulation's inertial frame is the J2000 ecliptic (+Z ecliptic north, +X equinox): it is
/// GCRS rotated about +X by the IAU 2006 obliquity ε₀ = 84381.406″, which is how the original
/// fixed-axis Earth already places its pole at (0, sin ε, cos ε).
/// </summary>
public sealed class IersEarthOrientation
{
    public const double ArcsecondToRadian = System.Math.PI / (180.0 * 3600.0);
    public const double J2000ObliquityArcsec = 84381.406;
    private const double MjdOfJ2000 = 51544.5;

    private readonly TimeScales _scales;
    private readonly double _eopFirstMjd;
    private readonly double[] _xp, _yp, _ut1MinusTai, _dx, _dy, _lod, _xpSigma, _ypSigma, _ut1Sigma;
    private readonly double _xysFirstMjd;
    private readonly double[] _x, _y, _s;

    /// <summary>GCRS → simulation inertial (J2000 ecliptic): rotation about +X by ε₀.</summary>
    public static readonly Matrix3x3d GcrsToSimulation = RotationX(J2000ObliquityArcsec * ArcsecondToRadian);

    public IersEarthOrientation(TimeScales scales, EnvironmentProductFile eop, EnvironmentProductFile xys)
    {
        _scales = scales;
        if (eop.Product != "eop_c04" || xys.Product != "cip_xys")
            throw new InvalidDataException("Expected the eop_c04 and cip_xys products.");
        int rows = eop.Rows;
        _eopFirstMjd = eop[0, eop.ColumnIndex("mjd_utc")];
        _xp = Column(eop, "xp_arcsec");
        _yp = Column(eop, "yp_arcsec");
        _dx = Column(eop, "dx_arcsec");
        _dy = Column(eop, "dy_arcsec");
        _lod = Column(eop, "lod_s");
        _xpSigma = Column(eop, "xp_sigma_arcsec");
        _ypSigma = Column(eop, "yp_sigma_arcsec");
        _ut1Sigma = Column(eop, "ut1_sigma_s");
        _ut1MinusTai = new double[rows];
        int mjdColumn = eop.ColumnIndex("mjd_utc");
        int ut1Column = eop.ColumnIndex("ut1_minus_utc_s");
        for (int i = 0; i < rows; i++)
        {
            double mjd = eop[i, mjdColumn];
            if (mjd != _eopFirstMjd + i)
                throw new InvalidDataException("EOP rows must be a continuous daily series.");
            double nodeUtc = (mjd - MjdOfJ2000) * TimeScales.SecondsPerDay;
            _ut1MinusTai[i] = eop[i, ut1Column] - scales.TaiMinusUtc(nodeUtc);
        }
        _xysFirstMjd = xys[0, xys.ColumnIndex("mjd_tt")];
        _x = Column(xys, "x_rad");
        _y = Column(xys, "y_rad");
        _s = Column(xys, "s_rad");
    }

    public static IersEarthOrientation LoadFromDataDirectory(string dataDirectory, TimeScales? scales = null)
    {
        string earth = Path.Combine(dataDirectory, "environment", "earth");
        return new IersEarthOrientation(scales ?? TimeScales.LoadFromDataDirectory(dataDirectory),
            EnvironmentProductFile.Load(Path.Combine(earth, "eop_c04.bin")),
            EnvironmentProductFile.Load(Path.Combine(earth, "cip_xys.bin")));
    }

    public TimeScales Scales => _scales;

    /// <summary>Every quantity of the orientation chain at one instant.</summary>
    public readonly record struct EarthOrientationSample(
        double XpRad, double YpRad, double Ut1MinusTaiSeconds, double DxRad, double DyRad,
        double XRad, double YRad, double SRad, double SPrimeRad, double EarthRotationAngleRad,
        double PoleSigmaRad, double Ut1SigmaSeconds, bool WithinObservations,
        double LengthOfDayExcessSeconds = 0.0);

    /// <summary>The orientation chain at <paramref name="ttSeconds"/> (TT seconds since J2000).</summary>
    public EarthOrientationSample Sample(double ttSeconds)
    {
        double tai = TimeScales.TtToTai(ttSeconds);
        double utc = _scales.TaiToUtc(tai);
        double mjdUtc = MjdOfJ2000 + utc / TimeScales.SecondsPerDay;
        double position = mjdUtc - _eopFirstMjd;
        bool within = position >= 0.0 && position <= _xp.Length - 1;
        double xp, yp, ut1MinusTai, dx, dy, poleSigma, ut1Sigma, lod;
        if (position < 0.0)
        {
            // Before C04 (1962-01-01): UT1 = UTC, pole at the origin, uncertainty of the first row.
            xp = yp = dx = dy = 0.0;
            lod = _lod[0];
            ut1MinusTai = -_scales.TaiMinusUtc(utc);
            poleSigma = System.Math.Abs(_xp[0]) + System.Math.Abs(_yp[0]) + _xpSigma[0];
            ut1Sigma = 1.0;
        }
        else
        {
            double p = System.Math.Min(position, _xp.Length - 1);
            xp = Lagrange(_xp, p, 4);
            yp = Lagrange(_yp, p, 4);
            ut1MinusTai = Lagrange(_ut1MinusTai, p, 4);
            dx = Lagrange(_dx, p, 4);
            dy = Lagrange(_dy, p, 4);
            lod = Lagrange(_lod, p, 4);
            int nearest = (int)System.Math.Round(p);
            poleSigma = System.Math.Sqrt(_xpSigma[nearest] * _xpSigma[nearest] + _ypSigma[nearest] * _ypSigma[nearest]);
            ut1Sigma = _ut1Sigma[nearest];
        }
        double mjdTt = MjdOfJ2000 + ttSeconds / TimeScales.SecondsPerDay;
        double q = mjdTt - _xysFirstMjd;
        if (q < 3.0 || q > _x.Length - 5)
            throw new ArgumentOutOfRangeException(nameof(ttSeconds), "Outside the tabulated IAU 2006/2000A model (1960-2051).");
        double x = Lagrange(_x, q, 8) + dx * ArcsecondToRadian;
        double y = Lagrange(_y, q, 8) + dy * ArcsecondToRadian;
        double s = Lagrange(_s, q, 8);
        double centuries = ttSeconds / (TimeScales.SecondsPerDay * 36525.0);
        double sPrime = -47e-6 * ArcsecondToRadian * centuries;
        double ut1Days = (tai + ut1MinusTai) / TimeScales.SecondsPerDay;
        return new EarthOrientationSample(xp * ArcsecondToRadian, yp * ArcsecondToRadian, ut1MinusTai,
            dx * ArcsecondToRadian, dy * ArcsecondToRadian, x, y, s, sPrime, EarthRotationAngle(ut1Days),
            poleSigma * ArcsecondToRadian, ut1Sigma, within, lod);
    }

    /// <summary>
    /// Earth rotation angle (IERS 2010 eq. 5.15) of UT1 days since J2000 (JD_UT1 − 2451545),
    /// written as ERFA eraEra00 does to keep the fractional day exact.
    /// </summary>
    public static double EarthRotationAngle(double ut1DaysSinceJ2000)
    {
        double fraction = ut1DaysSinceJ2000 - System.Math.Floor(ut1DaysSinceJ2000);
        double turns = fraction + 0.7790572732640 + 0.00273781191135448 * ut1DaysSinceJ2000;
        double angle = 2.0 * System.Math.PI * (turns - System.Math.Floor(turns));
        return angle;
    }

    /// <summary>GCRS → ITRS rotation matrix (IERS 2010 eq. 5.1, ERFA eraC2tcio order).</summary>
    public Matrix3x3d CelestialToTerrestrial(double ttSeconds) => CelestialToTerrestrial(Sample(ttSeconds));

    public static Matrix3x3d CelestialToTerrestrial(in EarthOrientationSample sample)
    {
        var celestialToIntermediate = CelestialToIntermediate(sample.XRad, sample.YRad, sample.SRad);
        var polarMotion = Multiply(RotationX(-sample.YpRad),
            Multiply(RotationY(-sample.XpRad), RotationZ(sample.SPrimeRad)));
        return Multiply(polarMotion, Multiply(RotationZ(sample.EarthRotationAngleRad), celestialToIntermediate));
    }

    /// <summary>ITRS (body-fixed) → simulation inertial frame.</summary>
    public Matrix3x3d TerrestrialToSimulation(double ttSeconds) =>
        Multiply(GcrsToSimulation, Transpose(CelestialToTerrestrial(ttSeconds)));

    /// <summary>GCRS → CIRS from X, Y, s (ERFA eraC2ixys).</summary>
    public static Matrix3x3d CelestialToIntermediate(double x, double y, double s)
    {
        double r2 = x * x + y * y;
        double e = r2 > 0.0 ? System.Math.Atan2(y, x) : 0.0;
        double d = System.Math.Atan(System.Math.Sqrt(r2 / (1.0 - r2)));
        return Multiply(RotationZ(-(e + s)), Multiply(RotationY(d), RotationZ(e)));
    }

    // Frame rotations in the SOFA/ERFA convention: R·v gives v's components in the rotated frame.
    public static Matrix3x3d RotationX(double angle)
    {
        double c = System.Math.Cos(angle), s = System.Math.Sin(angle);
        return new Matrix3x3d(1, 0, 0, 0, c, s, 0, -s, c);
    }

    public static Matrix3x3d RotationY(double angle)
    {
        double c = System.Math.Cos(angle), s = System.Math.Sin(angle);
        return new Matrix3x3d(c, 0, -s, 0, 1, 0, s, 0, c);
    }

    public static Matrix3x3d RotationZ(double angle)
    {
        double c = System.Math.Cos(angle), s = System.Math.Sin(angle);
        return new Matrix3x3d(c, s, 0, -s, c, 0, 0, 0, 1);
    }

    public static Matrix3x3d Multiply(Matrix3x3d a, Matrix3x3d b) => new(
        a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31,
        a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32,
        a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33,
        a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31,
        a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32,
        a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33,
        a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31,
        a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32,
        a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33);

    public static Matrix3x3d Transpose(Matrix3x3d m) => new(
        m.M11, m.M21, m.M31, m.M12, m.M22, m.M32, m.M13, m.M23, m.M33);

    /// <summary>
    /// Lagrange interpolation on a unit-spaced grid at fractional index <paramref name="p"/> with
    /// <paramref name="points"/> nodes centred on it (shifted inward at the ends).
    /// </summary>
    private static double Lagrange(double[] values, double p, int points)
    {
        int first = (int)System.Math.Floor(p) - (points / 2 - 1);
        first = System.Math.Clamp(first, 0, values.Length - points);
        double total = 0.0;
        for (int i = 0; i < points; i++)
        {
            double term = values[first + i];
            double xi = first + i;
            for (int j = 0; j < points; j++)
                if (j != i) term *= (p - (first + j)) / (xi - (first + j));
            total += term;
        }
        return total;
    }

    private static double[] Column(EnvironmentProductFile product, string name)
    {
        int column = product.ColumnIndex(name);
        var values = new double[product.Rows];
        for (int i = 0; i < values.Length; i++) values[i] = product[i, column];
        return values;
    }
}
