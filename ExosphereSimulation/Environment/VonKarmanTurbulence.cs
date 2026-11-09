namespace Exosphere.Simulation.Environment;

using Exosphere.Simulation.Math;

public enum TurbulenceIntensity
{
    Light,
    Moderate,
    Severe,
}

/// <summary>
/// Continuous random turbulence (environment plan E4.4): the von Kármán multimission model of
/// NASA/TM-2008-215633 sec. 2.3.13.5, with the standard deviations and length scales of its
/// Tables 2-70 (10–1000 m) and 2-71 (1–200 km) from the envdata product
/// <c>turbulence/turbulence_msfc2008.bin</c>. The handbook prefers von Kármán to Dryden when the
/// capability exists (sec. 2.3.13); a frozen spatial field realises it directly, where Dryden's
/// rational form only matters for white-noise filters.
/// <para>
/// Each table level carries its own realisation: a sum of <see cref="ModesPerLevel"/> random
/// Fourier modes (Kraichnan's method) in nondimensional coordinates ξ = (e/L_h, n/L_h, z/L_w),
/// wavenumbers log-spaced over [<see cref="MinWavenumber"/>, <see cref="MaxWavenumber"/>] with
/// amplitudes from the von Kármán energy spectrum E(k) ∝ (ak)⁴/(1+(ak)²)^(17/6), a = 1.339, so
/// the longitudinal integral scale is 1 in ξ. Every mode is transverse to its wavevector
/// (divergence-free in ξ), and each component's variance is normalised to exactly 1 per
/// realisation. The gust is then dimensionalised as in the handbook's eq. 2.129: σ_h on the two
/// horizontal components and σ_w on the vertical one. Between levels the two realisations blend
/// with cos/sin weights (unit variance preserved) and σ interpolates linearly; below 10 m and
/// above 200 km the end level holds.
/// </para>
/// <para>
/// The field is frozen in the body-fixed frame (Taylor's hypothesis: the vehicle crosses it far
/// faster than it evolves) and laid out on the orthographic tangent plane of an anchor (the launch
/// site): in full within <see cref="FullRadiusM"/> of it, faded by a cosine taper to zero at
/// <see cref="FadeRadiusM"/>, where the projection's scale distortion is 5 %. The same seed gives
/// the same air, bit for bit. A mean wind (a measured profile) may sit underneath.
/// </para>
/// </summary>
public sealed class VonKarmanTurbulence : IWindField
{
    public const int ModesPerLevel = 128;
    public const double MinWavenumber = 1e-2;
    public const double MaxWavenumber = 1e3;
    public const double VonKarmanConstant = 1.339;
    public const double FullRadiusM = 1_000_000.0;
    public const double FadeRadiusM = 2_000_000.0;

    private readonly double[] _altitude;
    private readonly double[] _sigmaH;
    private readonly double[] _sigmaW;
    private readonly double[] _lengthH;
    private readonly double[] _lengthW;
    private readonly Realisation[] _levels;
    private readonly double _anchorLatitudeDeg;
    private readonly double _anchorLongitudeDeg;
    private readonly Vector3d _anchorEast;
    private readonly Vector3d _anchorNorth;

    public string Id { get; }
    public TurbulenceIntensity Intensity { get; }
    public ulong Seed { get; }
    public IWindField? Mean { get; }

    public VonKarmanTurbulence(EnvironmentProductFile product, TurbulenceIntensity intensity, ulong seed,
        double anchorLatitudeDeg, double anchorLongitudeDeg, IWindField? mean = null)
    {
        Intensity = intensity;
        Seed = seed;
        Mean = mean;
        Id = $"{product.Product}:{intensity.ToString().ToLowerInvariant()}:{seed}";
        string suffix = intensity.ToString().ToLowerInvariant();
        int a = product.ColumnIndex("altitude_m");
        int sh = product.ColumnIndex("sigma_h_" + suffix), sw = product.ColumnIndex("sigma_w_" + suffix);
        int lh = product.ColumnIndex("length_h_m"), lw = product.ColumnIndex("length_w_m");
        int n = product.Rows;
        _altitude = new double[n];
        _sigmaH = new double[n];
        _sigmaW = new double[n];
        _lengthH = new double[n];
        _lengthW = new double[n];
        _levels = new Realisation[n];
        for (int i = 0; i < n; i++)
        {
            _altitude[i] = product[i, a];
            if (i > 0 && _altitude[i] <= _altitude[i - 1])
                throw new InvalidDataException($"{product.Product}: levels are not in increasing altitude.");
            _sigmaH[i] = product[i, sh];
            _sigmaW[i] = product[i, sw];
            _lengthH[i] = product[i, lh];
            _lengthW[i] = product[i, lw];
            _levels[i] = new Realisation(seed, (ulong)i);
        }
        _anchorLatitudeDeg = anchorLatitudeDeg;
        _anchorLongitudeDeg = anchorLongitudeDeg;
        double phi = anchorLatitudeDeg * System.Math.PI / 180.0, lambda = anchorLongitudeDeg * System.Math.PI / 180.0;
        _anchorEast = new Vector3d(-System.Math.Sin(lambda), System.Math.Cos(lambda), 0.0);
        _anchorNorth = new Vector3d(-System.Math.Sin(phi) * System.Math.Cos(lambda),
            -System.Math.Sin(phi) * System.Math.Sin(lambda), System.Math.Cos(phi));
    }

    public static VonKarmanTurbulence LoadFromDataDirectory(string dataDirectory, TurbulenceIntensity intensity, ulong seed,
        double anchorLatitudeDeg, double anchorLongitudeDeg, IWindField? mean = null,
        string productId = "turbulence_msfc2008") =>
        new(EnvironmentProductFile.Load(Path.Combine(dataDirectory, "environment", "turbulence", productId + ".bin")),
            intensity, seed, anchorLatitudeDeg, anchorLongitudeDeg, mean);

    /// <summary>Table standard deviations (horizontal, vertical) at an altitude, linear between levels.</summary>
    public (double Horizontal, double Vertical) SigmaAt(double altitudeM)
    {
        int i = Bracket(altitudeM, out double f);
        return (_sigmaH[i] + (_sigmaH[i + 1] - _sigmaH[i]) * f, _sigmaW[i] + (_sigmaW[i + 1] - _sigmaW[i]) * f);
    }

    /// <summary>Table length scales (horizontal, vertical) of the level at or below an altitude.</summary>
    public (double Horizontal, double Vertical) LengthAt(double altitudeM)
    {
        int i = Bracket(altitudeM, out double f);
        return f < 1.0 ? (_lengthH[i], _lengthW[i]) : (_lengthH[i + 1], _lengthW[i + 1]);
    }

    /// <summary>
    /// The gust (east, north, up; m/s) at a point of the anchor's tangent plane: horizontal
    /// coordinates in metres, geodetic altitude in metres. No horizontal taper.
    /// </summary>
    public (double East, double North, double Up) GustAt(double eastM, double northM, double altitudeM)
    {
        int i = Bracket(altitudeM, out double f);
        double z = System.Math.Clamp(altitudeM, _altitude[0], _altitude[^1]);
        double c = System.Math.Cos(0.5 * System.Math.PI * f), s = System.Math.Sin(0.5 * System.Math.PI * f);
        double ue = 0, un = 0, uz = 0;
        if (c > 0.0)
        {
            _levels[i].Evaluate(eastM / _lengthH[i], northM / _lengthH[i], z / _lengthW[i], out double x, out double y, out double w);
            ue += c * x;
            un += c * y;
            uz += c * w;
        }
        if (s > 0.0)
        {
            int j = i + 1;
            _levels[j].Evaluate(eastM / _lengthH[j], northM / _lengthH[j], z / _lengthW[j], out double x, out double y, out double w);
            ue += s * x;
            un += s * y;
            uz += s * w;
        }
        var (sigmaH, sigmaW) = SigmaAt(altitudeM);
        return (sigmaH * ue, sigmaH * un, sigmaW * uz);
    }

    public Vector3d WindAt(CelestialBody body, Vector3d worldPosition)
    {
        var mean = Mean?.WindAt(body, worldPosition) ?? Vector3d.Zero;
        body.GetGeodeticCoordinatesAtTime(worldPosition, body.StateTime, out double latitude, out double longitude, out double altitude);
        double phi = latitude * System.Math.PI / 180.0, lambda = longitude * System.Math.PI / 180.0;
        var radial = new Vector3d(System.Math.Cos(phi) * System.Math.Cos(lambda),
            System.Math.Cos(phi) * System.Math.Sin(lambda), System.Math.Sin(phi));
        double phi0 = _anchorLatitudeDeg * System.Math.PI / 180.0, lambda0 = _anchorLongitudeDeg * System.Math.PI / 180.0;
        var anchorRadial = new Vector3d(System.Math.Cos(phi0) * System.Math.Cos(lambda0),
            System.Math.Cos(phi0) * System.Math.Sin(lambda0), System.Math.Sin(phi0));
        double central = System.Math.Acos(System.Math.Clamp(radial.Dot(anchorRadial), -1.0, 1.0));
        double ground = central * body.MinimumRadius;
        if (ground >= FadeRadiusM) return mean;
        double taper = ground <= FullRadiusM ? 1.0
            : 0.5 * (1.0 + System.Math.Cos(System.Math.PI * (ground - FullRadiusM) / (FadeRadiusM - FullRadiusM)));
        var (ge, gn, gu) = GustAt(body.MinimumRadius * radial.Dot(_anchorEast),
            body.MinimumRadius * radial.Dot(_anchorNorth), altitude);
        var up = body.GetGeodeticUp(worldPosition);
        var east = body.GetEastDirection(worldPosition);
        var north = up.Cross(east).Normalized;
        east = north.Cross(up).Normalized;
        return mean + (east * ge + north * gn + up * gu) * taper;
    }

    private int Bracket(double altitudeM, out double fraction)
    {
        int last = _altitude.Length - 1;
        if (altitudeM <= _altitude[0]) { fraction = 0.0; return 0; }
        if (altitudeM >= _altitude[last]) { fraction = 1.0; return last - 1; }
        int i = Array.BinarySearch(_altitude, altitudeM);
        if (i >= 0) { fraction = 0.0; return i; } // i < last: the top level is caught above
        i = ~i - 1;
        fraction = (altitudeM - _altitude[i]) / (_altitude[i + 1] - _altitude[i]);
        return i;
    }

    /// <summary>One unit-variance, divergence-free von Kármán realisation in ξ (scale 1).</summary>
    private sealed class Realisation
    {
        private readonly double[] _kx, _ky, _kz, _ax, _ay, _az, _phase;

        public Realisation(ulong seed, ulong level)
        {
            var random = new SplitMix64(seed * 0x9E3779B97F4A7C15UL ^ (level + 1) * 0xD1B54A32D192ED03UL);
            int n = ModesPerLevel;
            _kx = new double[n]; _ky = new double[n]; _kz = new double[n];
            _ax = new double[n]; _ay = new double[n]; _az = new double[n];
            _phase = new double[n];
            double logMin = System.Math.Log(MinWavenumber), logStep = (System.Math.Log(MaxWavenumber) - logMin) / n;
            double sum = 0.0;
            var energy = new double[n];
            for (int m = 0; m < n; m++)
            {
                double low = System.Math.Exp(logMin + m * logStep), high = System.Math.Exp(logMin + (m + 1) * logStep);
                double k = System.Math.Exp(logMin + (m + random.NextDouble()) * logStep);
                double ak = VonKarmanConstant * k;
                energy[m] = System.Math.Pow(ak, 4) / System.Math.Pow(1.0 + ak * ak, 17.0 / 6.0) * (high - low);
                sum += energy[m];
                // Uniform direction on the sphere and a uniform transverse polarisation.
                double cz = 2.0 * random.NextDouble() - 1.0, azimuth = 2.0 * System.Math.PI * random.NextDouble();
                double sz = System.Math.Sqrt(1.0 - cz * cz);
                var direction = new Vector3d(sz * System.Math.Cos(azimuth), sz * System.Math.Sin(azimuth), cz);
                var helper = System.Math.Abs(direction.Z) < 0.9 ? new Vector3d(0, 0, 1) : new Vector3d(1, 0, 0);
                var t1 = direction.Cross(helper).Normalized;
                var t2 = direction.Cross(t1);
                double angle = 2.0 * System.Math.PI * random.NextDouble();
                var polarisation = t1 * System.Math.Cos(angle) + t2 * System.Math.Sin(angle);
                _kx[m] = k * direction.X; _ky[m] = k * direction.Y; _kz[m] = k * direction.Z;
                _ax[m] = polarisation.X; _ay[m] = polarisation.Y; _az[m] = polarisation.Z;
                _phase[m] = 2.0 * System.Math.PI * random.NextDouble();
            }
            // Amplitudes from the spectrum (Σ a² = 6 ⇒ unit variance per component on average),
            // then each component normalised to exactly unit spatial variance (Σ a_i²/2 = 1).
            double vx = 0, vy = 0, vz = 0;
            for (int m = 0; m < n; m++)
            {
                double amplitude = System.Math.Sqrt(6.0 * energy[m] / sum);
                _ax[m] *= amplitude; _ay[m] *= amplitude; _az[m] *= amplitude;
                vx += 0.5 * _ax[m] * _ax[m]; vy += 0.5 * _ay[m] * _ay[m]; vz += 0.5 * _az[m] * _az[m];
            }
            double sx = 1.0 / System.Math.Sqrt(vx), sy = 1.0 / System.Math.Sqrt(vy), szn = 1.0 / System.Math.Sqrt(vz);
            for (int m = 0; m < n; m++)
            {
                _ax[m] *= sx; _ay[m] *= sy; _az[m] *= szn;
            }
        }

        public void Evaluate(double x, double y, double z, out double u, out double v, out double w)
        {
            u = v = w = 0.0;
            for (int m = 0; m < _kx.Length; m++)
            {
                double c = System.Math.Cos(_kx[m] * x + _ky[m] * y + _kz[m] * z + _phase[m]);
                u += _ax[m] * c;
                v += _ay[m] * c;
                w += _az[m] * c;
            }
        }
    }

    /// <summary>SplitMix64 (Steele, Lea and Flood 2014): a fixed, platform-independent stream.</summary>
    private struct SplitMix64(ulong state)
    {
        private ulong _state = state;

        public double NextDouble()
        {
            ulong z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (z >> 11) * (1.0 / 9007199254740992.0);
        }
    }
}
