namespace Exosphere.Simulation.Environment;

using Exosphere.Simulation.Math;

/// <summary>
/// Dated thermosphere (environment plan E4.3): NRLMSIS 2.1 tabulated for the days a mission spends
/// in the air by envdata (<c>data/environment/atmosphere/msis_*.bin</c>) on universal time × local
/// solar time × geodetic latitude × altitude (UT is an axis because NRLMSIS varies up to 17 % with
/// it at fixed local time), interpolated with Catmull-Rom cubics in time, local time, latitude and
/// altitude (on ln ρ, T and ln n). Below <see cref="BlendBottomM"/> the body's altitude profile (USSA-76) answers;
/// between it and <see cref="BlendTopM"/> the two blend linearly in ln ρ, T and ln p; above
/// 1000 km the density follows the last scale height. Local time comes from the geodetic
/// longitude of the date, so the field belongs with the dated IERS orientation. Simulation time
/// is TDB seconds since J2000; between non-contiguous samples, or outside them, the nearest
/// sample holds.
/// </summary>
public sealed class NrlmsisAtmosphereField : IAtmosphereField
{
    public const double BlendBottomM = 80_000.0;
    public const double BlendTopM = 120_000.0;
    private const double Boltzmann = 1.380649e-23;

    private readonly TimeScales _scales;
    private readonly double[] _dateUtcSeconds; // 0 UT of each listed date, UTC seconds since J2000
    private readonly int _utSamples;
    private readonly double _utStepHours;
    private readonly double[] _lst;
    private readonly double[] _latitude;
    private readonly double[] _altitude;
    private readonly double[] _lnDensity;
    private readonly double[] _temperature;
    private readonly double[] _lnNumber;

    public string Id { get; }

    public NrlmsisAtmosphereField(EnvironmentProductFile product, TimeScales scales)
    {
        Id = product.Product;
        _scales = scales;
        var header = product.Header;
        var utHours = header.GetProperty("ut_hours").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        if (utHours[0] != 0 || utHours[^1] != 24)
            throw new InvalidDataException($"{Id}: each date must span 0..24 UT.");
        _utSamples = utHours.Length;
        _utStepHours = utHours[1] - utHours[0];
        _dateUtcSeconds = header.GetProperty("dates").EnumerateArray()
            .Select(e => DateOnly.Parse(e.GetString()!))
            .Select(date => TimeScales.UtcSecondsFromCalendar(date.Year, date.Month, date.Day, 0, 0, 0.0))
            .ToArray();
        _lst = header.GetProperty("lst_hours").EnumerateArray().Select(e => e.GetDouble()).ToArray();
        _latitude = header.GetProperty("latitudes_deg").EnumerateArray().Select(e => e.GetDouble()).ToArray();
        _altitude = header.GetProperty("altitudes_km").EnumerateArray().Select(e => e.GetDouble() * 1000.0).ToArray();
        int cells = _dateUtcSeconds.Length * _utSamples * _lst.Length * _latitude.Length * _altitude.Length;
        if (product.Rows != cells)
            throw new InvalidDataException($"{Id}: {product.Rows} rows, grid needs {cells}.");
        _lnDensity = new double[cells];
        _temperature = new double[cells];
        _lnNumber = new double[cells];
        int d = product.ColumnIndex("ln_density"), t = product.ColumnIndex("temperature_k"), m = product.ColumnIndex("ln_number_density");
        for (int i = 0; i < cells; i++)
        {
            _lnDensity[i] = product[i, d];
            _temperature[i] = product[i, t];
            _lnNumber[i] = product[i, m];
        }
    }

    public static NrlmsisAtmosphereField LoadFromDataDirectory(string dataDirectory, string productId, TimeScales? scales = null) =>
        new(EnvironmentProductFile.Load(Path.Combine(dataDirectory, "environment", "atmosphere", productId + ".bin")),
            scales ?? TimeScales.LoadFromDataDirectory(dataDirectory));

    public double Density(CelestialBody body, Vector3d worldPosition, double altitude) =>
        Evaluate(body, worldPosition, altitude, out _, out _);

    public double Temperature(CelestialBody body, Vector3d worldPosition, double altitude)
    {
        Evaluate(body, worldPosition, altitude, out double temperature, out _);
        return temperature;
    }

    public double Pressure(CelestialBody body, Vector3d worldPosition, double altitude)
    {
        Evaluate(body, worldPosition, altitude, out _, out double pressure);
        return pressure;
    }

    /// <summary>NRLMSIS state at an instant, place and altitude, without the low-altitude blend.</summary>
    public void Sample(double utcSeconds, double latitudeDeg, double longitudeDeg, double altitudeM,
        out double density, out double temperature, out double pressure)
    {
        // Time: the listed date containing the instant (else the nearest date, held at its end),
        // then linear in UT inside that date only: NRLMSIS steps at midnight with its daily indices.
        int date = Array.BinarySearch(_dateUtcSeconds, utcSeconds);
        if (date < 0) date = ~date - 1;
        double hoursIntoDate;
        if (date >= 0 && utcSeconds - _dateUtcSeconds[date] < TimeScales.SecondsPerDay)
            hoursIntoDate = (utcSeconds - _dateUtcSeconds[date]) / 3600.0;
        else
        {
            int next = date + 1;
            bool useNext = next < _dateUtcSeconds.Length
                && (date < 0 || _dateUtcSeconds[next] - utcSeconds < utcSeconds - (_dateUtcSeconds[date] + TimeScales.SecondsPerDay));
            date = useNext ? next : System.Math.Max(date, 0);
            hoursIntoDate = useNext ? 0.0 : 24.0;
        }
        double utPosition = System.Math.Clamp(hoursIntoDate / _utStepHours, 0.0, _utSamples - 1);
        int u0 = System.Math.Min((int)System.Math.Floor(utPosition), _utSamples - 2);
        double dayFraction = utPosition - u0;
        double utcHours = ((utcSeconds / 3600.0 + 12.0) % 24.0 + 24.0) % 24.0; // J2000 epoch is 12 UT
        double lst = ((utcHours + longitudeDeg / 15.0) % 24.0 + 24.0) % 24.0;
        double step = _lst[1] - _lst[0];
        int l0 = (int)System.Math.Floor(lst / step) % _lst.Length;
        double lstFraction = (lst - _lst[l0]) / step;
        int a0 = Bracket(_latitude, latitudeDeg, out double latFraction);
        int h0 = Bracket(_altitude, altitudeM, out double altFraction, extrapolateAbove: true);

        // Catmull-Rom over four nodes in UT (within the date), local time (periodic) and latitude
        // (clamped at the poles): the diurnal swing at 600 km is a factor of ~2 and its UT and
        // latitude structure is curved, which linear 3 h / 15° steps clip.
        Span<double> utWeights = stackalloc double[4];
        Span<double> lstWeights = stackalloc double[4];
        Span<double> latWeights = stackalloc double[4];
        CatmullRom(dayFraction, utWeights);
        CatmullRom(lstFraction, lstWeights);
        CatmullRom(latFraction, latWeights);
        // Altitude: Catmull-Rom where the four nodes are evenly spaced (inside each grid region),
        // linear at region joins and outside the grid.
        Span<double> altWeights = stackalloc double[4];
        int altFirst = h0 - 1;
        bool cubicAltitude = altFraction >= 0.0 && altFraction <= 1.0 && altFirst >= 0
            && h0 + 2 < _altitude.Length
            && System.Math.Abs((_altitude[h0] - _altitude[altFirst]) - (_altitude[h0 + 1] - _altitude[h0])) < 1.0
            && System.Math.Abs((_altitude[h0 + 2] - _altitude[h0 + 1]) - (_altitude[h0 + 1] - _altitude[h0])) < 1.0;
        if (cubicAltitude) CatmullRom(altFraction, altWeights);
        else
        {
            altFirst = h0;
            altWeights[0] = 1 - altFraction;
            altWeights[1] = altFraction;
            altWeights[2] = altWeights[3] = 0.0;
        }
        int n = _lst.Length, lastLatitude = _latitude.Length - 1;
        int dateBase = date * _utSamples;
        double lnRho = 0, temp = 0, lnNumber = 0;
        for (int dd = 0; dd < 4; dd++)
        {
            int utNode = dateBase + System.Math.Clamp(u0 - 1 + dd, 0, _utSamples - 1);
            for (int ll = 0; ll < 4; ll++)
            {
                int lstNode = ((l0 - 1 + ll) % n + n) % n;
                for (int aa = 0; aa < 4; aa++)
                {
                    int latNode = System.Math.Clamp(a0 - 1 + aa, 0, lastLatitude);
                    double wTime = utWeights[dd] * lstWeights[ll] * latWeights[aa];
                    if (wTime == 0.0) continue;
                    for (int hh = 0; hh < 4; hh++)
                    {
                        double w = wTime * altWeights[hh];
                        if (w == 0.0) continue;
                        int index = Index(utNode, lstNode, latNode, altFirst + hh);
                        lnRho += w * _lnDensity[index];
                        temp += w * _temperature[index];
                        lnNumber += w * _lnNumber[index];
                    }
                }
            }
        }
        density = System.Math.Exp(lnRho);
        temperature = temp;
        pressure = System.Math.Exp(lnNumber) * Boltzmann * temp;
    }

    private double Evaluate(CelestialBody body, Vector3d worldPosition, double altitude, out double temperature, out double pressure)
    {
        var profile = body.Atmosphere!;
        if (altitude <= BlendBottomM)
        {
            temperature = profile.GetTemperature(altitude);
            pressure = profile.GetPressure(altitude);
            return profile.GetDensity(altitude);
        }
        body.GetGeodeticCoordinatesAtTime(worldPosition, body.StateTime, out double latitude, out double longitude, out _);
        double utc = _scales.TdbToUtc(body.StateTime);
        Sample(utc, latitude, longitude, altitude, out double density, out temperature, out pressure);
        if (altitude >= BlendTopM) return density;
        double w = (altitude - BlendBottomM) / (BlendTopM - BlendBottomM);
        double profileDensity = profile.GetDensity(altitude), profilePressure = profile.GetPressure(altitude);
        temperature = (1 - w) * profile.GetTemperature(altitude) + w * temperature;
        pressure = profilePressure > 0 ? System.Math.Exp((1 - w) * System.Math.Log(profilePressure) + w * System.Math.Log(pressure)) : pressure;
        return profileDensity > 0 ? System.Math.Exp((1 - w) * System.Math.Log(profileDensity) + w * System.Math.Log(density)) : density;
    }

    private static void CatmullRom(double t, Span<double> weights)
    {
        double t2 = t * t, t3 = t2 * t;
        weights[0] = 0.5 * (-t3 + 2 * t2 - t);
        weights[1] = 0.5 * (3 * t3 - 5 * t2 + 2);
        weights[2] = 0.5 * (-3 * t3 + 4 * t2 + t);
        weights[3] = 0.5 * (t3 - t2);
    }

    private int Index(int day, int lst, int latitude, int altitude) =>
        ((day * _lst.Length + lst) * _latitude.Length + latitude) * _altitude.Length + altitude;

    private static int Bracket(double[] grid, double value, out double fraction, bool extrapolateAbove = false)
    {
        int last = grid.Length - 1;
        if (value <= grid[0]) { fraction = 0.0; return 0; }
        if (value >= grid[last])
        {
            // Above the grid: continue the top interval linearly (log-density: the last scale height).
            fraction = extrapolateAbove ? 1.0 + (value - grid[last]) / (grid[last] - grid[last - 1]) : 1.0;
            return last - 1;
        }
        int i = Array.BinarySearch(grid, value);
        if (i >= 0)
        {
            if (i == last) { fraction = 1.0; return last - 1; }
            fraction = 0.0;
            return i;
        }
        i = ~i - 1;
        fraction = (value - grid[i]) / (grid[i + 1] - grid[i]);
        return i;
    }
}
