namespace Exosphere.Simulation.Environment;

using Exosphere.Simulation.Math;

/// <summary>
/// Gravity field from spherical-harmonic coefficients (environment plan E3.2), evaluated with the
/// Cunningham V/W recursion (Montenbruck &amp; Gill, Satellite Orbits, 2000, §3.2.4-3.2.5): no
/// singularity at the poles, no trigonometric calls, allocation-free. Coefficients arrive 4π
/// fully normalized (EGM2008, GRGM1200A) and are converted once to unnormalized form; the
/// recursion's terms stay inside double range up to degree <see cref="MaximumDegree"/>.
///
/// The field is defined in the body-fixed frame; <see cref="CelestialBody"/> supplies the axes at
/// its state time (the dated IERS or lunar principal-axes frame, or the fixed-axis spin). GM and
/// the reference radius are the model's own, which its coefficients are scaled to.
/// </summary>
public sealed class SphericalHarmonicGravity : IGravityField
{
    public const int MaximumDegree = 120;

    private readonly double _gm;
    private readonly double _radius;
    private readonly int _degree;
    private readonly double[] _c;
    private readonly double[] _s;

    [ThreadStatic] private static double[]? _v;
    [ThreadStatic] private static double[]? _w;

    public string Id { get; }
    public int Degree => _degree;
    public double GravitationalParameter => _gm;
    public double ReferenceRadius => _radius;

    /// <param name="normalizedC">4π-normalized C̄ₙₘ, indexed n(n+1)/2 + m, through <paramref name="degree"/>.</param>
    public SphericalHarmonicGravity(string id, double gm, double referenceRadius, int degree,
        double[] normalizedC, double[] normalizedS)
    {
        if (degree < 0 || degree > MaximumDegree)
            throw new ArgumentOutOfRangeException(nameof(degree), $"Degree must be 0..{MaximumDegree}.");
        int count = Index(degree, degree) + 1;
        if (normalizedC.Length < count || normalizedS.Length < count)
            throw new ArgumentException("Coefficient arrays are shorter than the degree requires.");
        Id = id;
        _gm = gm;
        _radius = referenceRadius;
        _degree = degree;
        _c = new double[count];
        _s = new double[count];
        for (int n = 0; n <= degree; n++)
            for (int m = 0; m <= n; m++)
            {
                double factor = NormalizationFactor(n, m);
                _c[Index(n, m)] = normalizedC[Index(n, m)] * factor;
                _s[Index(n, m)] = normalizedS[Index(n, m)] * factor;
            }
    }

    public static int Index(int n, int m) => n * (n + 1) / 2 + m;

    /// <summary>
    /// Nₙₘ = sqrt((2 − δ₀ₘ)(2n + 1)(n − m)! / (n + m)!), the factor that turns a 4π-normalized
    /// coefficient into its unnormalized value; built as a product to avoid overflowing factorials.
    /// </summary>
    public static double NormalizationFactor(int n, int m)
    {
        double ratio = 1.0; // (n − m)! / (n + m)! = 1 / Π_{k=n−m+1}^{n+m} k
        for (int k = n - m + 1; k <= n + m; k++) ratio /= k;
        return System.Math.Sqrt((m == 0 ? 1.0 : 2.0) * (2 * n + 1) * ratio);
    }

    public Vector3d AccelerationAt(CelestialBody body, Vector3d worldPosition)
    {
        body.GetRotatingBasis(out var xAxis, out var yAxis, out var zAxis);
        var relative = worldPosition - body.Position;
        var fixedFrame = new Vector3d(relative.Dot(xAxis), relative.Dot(yAxis), relative.Dot(zAxis));
        var a = AccelerationBodyFixed(fixedFrame);
        return xAxis * a.X + yAxis * a.Y + zAxis * a.Z;
    }

    /// <summary>Acceleration (m/s², body-fixed axes) at a body-fixed position (m).</summary>
    public Vector3d AccelerationBodyFixed(Vector3d r)
    {
        double r2 = r.MagnitudeSquared;
        if (r2 < 1.0) return Vector3d.Zero;
        int size = _degree + 2;
        int length = Index(size, size) + 1;
        var v = _v;
        if (v == null || v.Length < length) _v = v = new double[length];
        var w = _w;
        if (w == null || w.Length < length) _w = w = new double[length];

        double rho = _radius * _radius / r2;
        double x0 = _radius * r.X / r2, y0 = _radius * r.Y / r2, z0 = _radius * r.Z / r2;

        // Zonal terms and the first sectorial seed: V₀₀ = R/r, W₀₀ = 0.
        v[0] = _radius / System.Math.Sqrt(r2);
        w[0] = 0.0;
        for (int m = 0; m <= size; m++)
        {
            if (m > 0)
            {
                int prev = Index(m - 1, m - 1), here = Index(m, m);
                v[here] = (2 * m - 1) * (x0 * v[prev] - y0 * w[prev]);
                w[here] = (2 * m - 1) * (x0 * w[prev] + y0 * v[prev]);
            }
            if (m + 1 <= size)
            {
                int here = Index(m + 1, m), below = Index(m, m);
                v[here] = (2 * m + 1) * z0 * v[below];
                w[here] = (2 * m + 1) * z0 * w[below];
            }
            for (int n = m + 2; n <= size; n++)
            {
                int here = Index(n, m), one = Index(n - 1, m), two = Index(n - 2, m);
                double a = (2.0 * n - 1.0) / (n - m), b = (n + m - 1.0) / (n - m);
                v[here] = a * z0 * v[one] - b * rho * v[two];
                w[here] = a * z0 * w[one] - b * rho * w[two];
            }
        }

        double ax = 0.0, ay = 0.0, az = 0.0;
        for (int m = 0; m <= _degree; m++)
            for (int n = m; n <= _degree; n++)
            {
                double c = _c[Index(n, m)], s = _s[Index(n, m)];
                if (m == 0)
                {
                    ax -= c * v[Index(n + 1, 1)];
                    ay -= c * w[Index(n + 1, 1)];
                    az += (n + 1) * (-c * v[Index(n + 1, 0)]);
                }
                else
                {
                    double f = (n - m + 1.0) * (n - m + 2.0);
                    int up = Index(n + 1, m + 1), down = Index(n + 1, m - 1), same = Index(n + 1, m);
                    ax += 0.5 * (-c * v[up] - s * w[up] + f * (c * v[down] + s * w[down]));
                    ay += 0.5 * (-c * w[up] + s * v[up] + f * (-c * w[down] + s * v[down]));
                    az += (n - m + 1.0) * (-c * v[same] - s * w[same]);
                }
            }
        double scale = _gm / (_radius * _radius);
        return new Vector3d(ax * scale, ay * scale, az * scale);
    }

    /// <summary>
    /// Potential (m²/s², attractive positive: U = GM/r + …) at a body-fixed position. Allocates;
    /// for verification (the acceleration must be its gradient), not for the physics step.
    /// </summary>
    public double PotentialBodyFixed(Vector3d r)
    {
        double r2 = r.MagnitudeSquared;
        int size = _degree;
        int length = Index(size + 2, size + 2) + 1;
        var v = new double[length];
        var w = new double[length];
        double rho = _radius * _radius / r2;
        double x0 = _radius * r.X / r2, y0 = _radius * r.Y / r2, z0 = _radius * r.Z / r2;
        v[0] = _radius / System.Math.Sqrt(r2);
        for (int m = 0; m <= size; m++)
        {
            if (m > 0)
            {
                int prev = Index(m - 1, m - 1), here = Index(m, m);
                v[here] = (2 * m - 1) * (x0 * v[prev] - y0 * w[prev]);
                w[here] = (2 * m - 1) * (x0 * w[prev] + y0 * v[prev]);
            }
            if (m + 1 <= size)
            {
                v[Index(m + 1, m)] = (2 * m + 1) * z0 * v[Index(m, m)];
                w[Index(m + 1, m)] = (2 * m + 1) * z0 * w[Index(m, m)];
            }
            for (int n = m + 2; n <= size; n++)
            {
                double a = (2.0 * n - 1.0) / (n - m), b = (n + m - 1.0) / (n - m);
                v[Index(n, m)] = a * z0 * v[Index(n - 1, m)] - b * rho * v[Index(n - 2, m)];
                w[Index(n, m)] = a * z0 * w[Index(n - 1, m)] - b * rho * w[Index(n - 2, m)];
            }
        }
        double u = 0.0;
        for (int n = 0; n <= _degree; n++)
            for (int m = 0; m <= n; m++)
                u += _c[Index(n, m)] * v[Index(n, m)] + _s[Index(n, m)] * w[Index(n, m)];
        return _gm / _radius * u;
    }
}
