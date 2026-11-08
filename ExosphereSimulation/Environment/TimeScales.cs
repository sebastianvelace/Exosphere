namespace Exosphere.Simulation.Environment;

/// <summary>
/// Astronomical time scales (environment plan E1.1). Every instant is expressed as seconds
/// since J2000.0 (JD 2451545.0) in its own scale, s_X = 86400 (JD_X − 2451545), which keeps
/// ~0.1 µs resolution across the space age where a double Julian date would keep only ~40 µs.
///
/// <list type="bullet">
/// <item>UTC → TAI from the official TAI − UTC table (<c>data/environment/time/tai_utc.bin</c>,
///   USNO), including the 1961–1971 rate terms: TAI − UTC = offset + (MJD_UTC − MJD_ref) · rate.</item>
/// <item>TT = TAI + 32.184 s (IAU 1991 / 2000).</item>
/// <item>TDB − TT from the truncated Fairhead–Bretagnon series of USNO Circular 179 (Kaplan 2005,
///   eq. 2.6): 9.3 µs from the full ERFA <c>eraDtdb</c> series over 1960–2040 (geocentre).</item>
/// </list>
/// UT1 needs Earth orientation parameters and arrives with E1.2. Instants before
/// 1961-01-01, where UTC is undefined, are rejected; inside a leap second (23:59:60) UTC is
/// not representable in this form, as for any quasi-Julian UTC count.
/// </summary>
public sealed class TimeScales
{
    public const double SecondsPerDay = 86400.0;
    public const double J2000JulianDate = 2451545.0;
    public const double TtMinusTaiSeconds = 32.184;
    private const double MjdOfJ2000 = 51544.5;

    private readonly double[] _startUtcSeconds;
    private readonly double[] _offsetSeconds;
    private readonly double[] _referenceMjd;
    private readonly double[] _rateSecondsPerDay;

    public TimeScales(EnvironmentProductFile taiMinusUtc)
    {
        if (taiMinusUtc.Product != "tai_utc")
            throw new InvalidDataException($"Expected the tai_utc product, got '{taiMinusUtc.Product}'.");
        int start = taiMinusUtc.ColumnIndex("jd_utc_start");
        int offset = taiMinusUtc.ColumnIndex("offset_s");
        int reference = taiMinusUtc.ColumnIndex("mjd_reference");
        int rate = taiMinusUtc.ColumnIndex("rate_s_per_day");
        int rows = taiMinusUtc.Rows;
        _startUtcSeconds = new double[rows];
        _offsetSeconds = new double[rows];
        _referenceMjd = new double[rows];
        _rateSecondsPerDay = new double[rows];
        for (int i = 0; i < rows; i++)
        {
            // Table starts are midnight Julian dates (x.5): exact in seconds.
            _startUtcSeconds[i] = (taiMinusUtc[i, start] - J2000JulianDate) * SecondsPerDay;
            _offsetSeconds[i] = taiMinusUtc[i, offset];
            _referenceMjd[i] = taiMinusUtc[i, reference];
            _rateSecondsPerDay[i] = taiMinusUtc[i, rate];
            if (i > 0 && _startUtcSeconds[i] <= _startUtcSeconds[i - 1])
                throw new InvalidDataException("TAI - UTC rows are not in time order.");
        }
    }

    public static TimeScales LoadFromDataDirectory(string dataDirectory) =>
        new(EnvironmentProductFile.Load(Path.Combine(dataDirectory, "environment", "time", "tai_utc.bin")));

    /// <summary>First UTC instant the table defines (1961-01-01), in UTC seconds since J2000.</summary>
    public double FirstUtcSeconds => _startUtcSeconds[0];

    /// <summary>
    /// UTC seconds since J2000 of a Gregorian calendar instant: exact integer arithmetic for the
    /// date and the whole hours/minutes, so only <paramref name="second"/> carries rounding.
    /// </summary>
    public static double UtcSecondsFromCalendar(int year, int month, int day, int hour, int minute, double second)
    {
        if (month is < 1 or > 12 || day is < 1 or > 31 || hour is < 0 or > 23 || minute is < 0 or > 59
            || !(second >= 0.0 && second < 60.0))
            throw new ArgumentOutOfRangeException(nameof(second), "Invalid UTC calendar instant.");
        long days = JulianDayNumber(year, month, day) - 2451545L; // whole days from J2000 noon
        return days * SecondsPerDay + (hour - 12) * 3600.0 + minute * 60.0 + second;
    }

    /// <summary>Julian day number (the Julian date at noon) of a Gregorian date.</summary>
    public static long JulianDayNumber(int year, int month, int day)
    {
        long a = (14 - month) / 12;
        long y = year + 4800 - a;
        long m = month + 12 * a - 3;
        return day + (153 * m + 2) / 5 + 365 * y + y / 4 - y / 100 + y / 400 - 32045;
    }

    /// <summary>TAI − UTC (s) at a UTC instant.</summary>
    public double TaiMinusUtc(double utcSeconds)
    {
        if (!double.IsFinite(utcSeconds) || utcSeconds < _startUtcSeconds[0])
            throw new ArgumentOutOfRangeException(nameof(utcSeconds), "UTC is defined from 1961-01-01.");
        int row = Array.BinarySearch(_startUtcSeconds, utcSeconds);
        if (row < 0) row = ~row - 1;
        double mjd = MjdOfJ2000 + utcSeconds / SecondsPerDay;
        return _offsetSeconds[row] + (mjd - _referenceMjd[row]) * _rateSecondsPerDay[row];
    }

    public double UtcToTai(double utcSeconds) => utcSeconds + TaiMinusUtc(utcSeconds);

    /// <summary>
    /// Inverse of <see cref="UtcToTai"/>: the fixed point of utc = tai − ΔAT(utc). It contracts
    /// by the rate term (≤ 3·10⁻⁸) within a table row; starting from tai − ΔAT(tai) it settles
    /// on the correct side of a leap step after at most one row change.
    /// </summary>
    public double TaiToUtc(double taiSeconds)
    {
        double first = _startUtcSeconds[0];
        double utc = taiSeconds - TaiMinusUtc(System.Math.Max(first, taiSeconds));
        for (int i = 0; i < 3; i++)
            utc = taiSeconds - TaiMinusUtc(System.Math.Max(first, utc));
        if (utc < first)
            throw new ArgumentOutOfRangeException(nameof(taiSeconds), "UTC is defined from 1961-01-01.");
        return utc;
    }

    public static double TaiToTt(double taiSeconds) => taiSeconds + TtMinusTaiSeconds;
    public static double TtToTai(double ttSeconds) => ttSeconds - TtMinusTaiSeconds;

    /// <summary>
    /// TDB − TT (s) at the geocentre, USNO Circular 179 eq. 2.6 (Fairhead &amp; Bretagnon 1990,
    /// truncated); T in Julian centuries of TT from J2000.
    /// </summary>
    public static double TdbMinusTt(double ttSeconds)
    {
        double t = ttSeconds / (SecondsPerDay * 36525.0);
        return 0.001657 * System.Math.Sin(628.3076 * t + 6.2401)
            + 0.000022 * System.Math.Sin(575.3385 * t + 4.2970)
            + 0.000014 * System.Math.Sin(1256.6152 * t + 6.1969)
            + 0.000005 * System.Math.Sin(606.9777 * t + 4.0212)
            + 0.000005 * System.Math.Sin(52.9691 * t + 0.4444)
            + 0.000002 * System.Math.Sin(21.3299 * t + 5.5431)
            + 0.000010 * t * System.Math.Sin(628.3076 * t + 4.2490);
    }

    public static double TtToTdb(double ttSeconds) => ttSeconds + TdbMinusTt(ttSeconds);

    /// <summary>Inverse of <see cref="TtToTdb"/>; the series' slope is ~10⁻⁸, so two passes are exact.</summary>
    public static double TdbToTt(double tdbSeconds)
    {
        double tt = tdbSeconds - TdbMinusTt(tdbSeconds);
        return tdbSeconds - TdbMinusTt(tt);
    }

    public double UtcToTdb(double utcSeconds) => TtToTdb(TaiToTt(UtcToTai(utcSeconds)));

    public double TdbToUtc(double tdbSeconds) => TaiToUtc(TtToTai(TdbToTt(tdbSeconds)));
}
