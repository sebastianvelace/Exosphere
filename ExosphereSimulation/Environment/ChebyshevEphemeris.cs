namespace Exosphere.Simulation.Environment;

using Exosphere.Simulation.Math;

/// <summary>
/// One piecewise-Chebyshev series of a DE440 product (<c>data/environment/ephemeris/*.bin</c>):
/// uniform spans in TDB seconds since J2000, position in metres (ICRF), velocity from the
/// analytic derivative. Evaluation is allocation-free (Clenshaw recurrences).
/// </summary>
public sealed class ChebyshevSeries
{
    private readonly double _first;
    private readonly double _span;
    private readonly int _degree;
    private readonly int _segments;
    private readonly double[] _coefficients; // [segment][axis][k]

    public string Product { get; }
    public double ValidFrom => _first;
    public double ValidTo => _first + _segments * _span;

    public ChebyshevSeries(EnvironmentProductFile product)
    {
        Product = product.Product;
        _degree = product.Header.GetProperty("degree").GetInt32();
        _segments = product.Rows;
        _first = product[0, 0];
        _span = product[0, 1] - product[0, 0];
        int width = 3 * (_degree + 1);
        if (product.Columns.Count != 2 + width)
            throw new InvalidDataException($"{Product}: columns do not match degree {_degree}.");
        _coefficients = new double[_segments * width];
        for (int s = 0; s < _segments; s++)
        {
            if (System.Math.Abs(product[s, 0] - (_first + s * _span)) > 1e-3
                || System.Math.Abs(product[s, 1] - (_first + (s + 1) * _span)) > 1e-3)
                throw new InvalidDataException($"{Product}: spans are not uniform at segment {s}.");
            for (int c = 0; c < width; c++)
                _coefficients[s * width + c] = product[s, 2 + c];
        }
    }

    public bool Covers(double tdbSeconds) => tdbSeconds >= _first && tdbSeconds <= ValidTo;

    /// <summary>Position (m) and velocity (m/s) in the ICRF at a TDB instant.</summary>
    public void Evaluate(double tdbSeconds, out Vector3d position, out Vector3d velocity)
    {
        int segment = (int)System.Math.Floor((tdbSeconds - _first) / _span);
        segment = System.Math.Clamp(segment, 0, _segments - 1);
        double start = _first + segment * _span;
        double x = 2.0 * (tdbSeconds - start) / _span - 1.0;
        int width = 3 * (_degree + 1);
        int baseIndex = segment * width;
        double scale = 2.0 / _span;
        Axis(baseIndex, x, out double px, out double vx);
        Axis(baseIndex + (_degree + 1), x, out double py, out double vy);
        Axis(baseIndex + 2 * (_degree + 1), x, out double pz, out double vz);
        position = new Vector3d(px, py, pz);
        velocity = new Vector3d(vx * scale, vy * scale, vz * scale);
    }

    /// <summary>Clenshaw for Σ cₖ Tₖ(x) and its derivative Σ cₖ Tₖ′(x) = Σ k cₖ Uₖ₋₁(x).</summary>
    private void Axis(int offset, double x, out double value, out double derivative)
    {
        double b1 = 0.0, b2 = 0.0;
        for (int k = _degree; k >= 1; k--)
        {
            double b0 = 2.0 * x * b1 - b2 + _coefficients[offset + k];
            b2 = b1;
            b1 = b0;
        }
        value = x * b1 - b2 + _coefficients[offset];
        // Derivative: Σ_{k≥1} k cₖ U_{k−1}(x) by Clenshaw on U (U recurrence matches T's).
        double d1 = 0.0, d2 = 0.0;
        for (int k = _degree; k >= 1; k--)
        {
            double d0 = 2.0 * x * d1 - d2 + k * _coefficients[offset + k];
            d2 = d1;
            d1 = d0;
        }
        derivative = d1;
    }
}

/// <summary>
/// Dated ephemeris from JPL DE440 (environment plan E2.2): every body's barycentric state from
/// Chebyshev series, rotated from the ICRF into the simulation frame (ICRF about +X by the IAU
/// 2006 obliquity, <see cref="IersEarthOrientation.GcrsToSimulation"/>). Earth is
/// EMB − Moon_geo·μ_M/(μ_E + μ_M) with the DE440 GMs; the Moon is Earth + Moon_geo. The origin is
/// the solar-system barycentre, so the Sun moves (~13 m/s). Simulation time is TDB seconds since
/// J2000. Outside 1960–2051, or for a body without a series, the Kepler model answers.
/// </summary>
public sealed class ChebyshevEphemeris : IEphemerisProvider
{
    private readonly Dictionary<string, ChebyshevSeries> _barycentric;
    private readonly ChebyshevSeries _emb;
    private readonly ChebyshevSeries _moon;
    private readonly double _moonFraction;
    private readonly KeplerEphemeris _fallback = new();
    private readonly List<CelestialBody> _fallbackBodies = new();

    public string Id => "de440";

    public ChebyshevEphemeris(IReadOnlyDictionary<string, EnvironmentProductFile> products)
    {
        _barycentric = new Dictionary<string, ChebyshevSeries>(StringComparer.Ordinal);
        foreach (var (body, product) in new[] { ("sun", "de440_sun"), ("mercury", "de440_mercury"),
                     ("venus", "de440_venus"), ("mars", "de440_mars"), ("jupiter", "de440_jupiter"), ("saturn", "de440_saturn") })
            _barycentric[body] = new ChebyshevSeries(products[product]);
        _emb = new ChebyshevSeries(products["de440_emb"]);
        var moon = products["de440_moon"];
        _moon = new ChebyshevSeries(moon);
        double gmEarth = moon.Header.GetProperty("gm_earth_m3s2").GetDouble();
        double gmMoon = moon.Header.GetProperty("gm_moon_m3s2").GetDouble();
        _moonFraction = gmMoon / (gmEarth + gmMoon);
    }

    public static ChebyshevEphemeris LoadFromDataDirectory(string dataDirectory)
    {
        string folder = Path.Combine(dataDirectory, "environment", "ephemeris");
        var products = new Dictionary<string, EnvironmentProductFile>(StringComparer.Ordinal);
        foreach (var id in new[] { "de440_sun", "de440_mercury", "de440_venus", "de440_mars", "de440_jupiter",
                     "de440_saturn", "de440_emb", "de440_moon" })
            products[id] = EnvironmentProductFile.Load(Path.Combine(folder, id + ".bin"));
        return new ChebyshevEphemeris(products);
    }

    /// <summary>μ_M/(μ_E + μ_M) from the DE440 GMs.</summary>
    public double MoonMassFraction => _moonFraction;

    public bool Covers(double tdbSeconds) => _moon.Covers(tdbSeconds) && _emb.Covers(tdbSeconds);

    /// <summary>Barycentric state of a body in the simulation frame; false if this ephemeris has no series for it.</summary>
    public bool TryState(string bodyId, double tdbSeconds, out Vector3d position, out Vector3d velocity)
    {
        if (!Covers(tdbSeconds))
        {
            position = velocity = Vector3d.Zero;
            return false;
        }
        Vector3d p, v;
        switch (bodyId)
        {
            case "earth":
            case "moon":
                _emb.Evaluate(tdbSeconds, out var embP, out var embV);
                _moon.Evaluate(tdbSeconds, out var moonP, out var moonV);
                p = embP - moonP * _moonFraction;
                v = embV - moonV * _moonFraction;
                if (bodyId == "moon")
                {
                    p += moonP;
                    v += moonV;
                }
                break;
            default:
                if (!_barycentric.TryGetValue(bodyId, out var series))
                {
                    position = velocity = Vector3d.Zero;
                    return false;
                }
                series.Evaluate(tdbSeconds, out p, out v);
                break;
        }
        var frame = IersEarthOrientation.GcrsToSimulation;
        position = frame.Multiply(p);
        velocity = frame.Multiply(v);
        return true;
    }

    public void PropagateAll(IReadOnlyList<CelestialBody> bodies, double time)
    {
        if (!Covers(time))
        {
            // Outside 1960–2051 the whole system rides Kepler, so frames stay consistent.
            _fallback.PropagateAll(bodies, time);
            return;
        }
        _fallbackBodies.Clear();
        for (int i = 0; i < bodies.Count; i++)
        {
            var body = bodies[i];
            if (TryState(body.Id, time, out var p, out var v))
            {
                body.Position = p;
                body.Velocity = v;
                body.StateTime = time;
            }
            else
            {
                _fallbackBodies.Add(body);
            }
        }
        if (_fallbackBodies.Count == 0) return;
        // A body without a series rides its conic on the dated state of its parent.
        CelestialBody? Resolve(string id)
        {
            for (int i = 0; i < bodies.Count; i++)
                if (bodies[i].Id == id) return bodies[i];
            return null;
        }
        foreach (var body in _fallbackBodies)
        {
            var (p, v) = StateAt(body, time, Resolve);
            body.Position = p;
            body.Velocity = v;
            body.StateTime = time;
        }
    }

    public (Vector3d Position, Vector3d Velocity) StateAt(
        CelestialBody body, double time, Func<string, CelestialBody?> resolve)
    {
        if (TryState(body.Id, time, out var position, out var velocity))
            return (position, velocity);
        if (!Covers(time))
            return _fallback.StateAt(body, time, resolve);
        if (body.OrbitalElements is { } elements && resolve(elements.ReferenceBodyId) is { } parent)
        {
            var (parentPosition, parentVelocity) = StateAt(parent, time, resolve);
            var (relativePosition, relativeVelocity) = elements.GetStateAtTime(time, parent.GM);
            return (parentPosition + relativePosition, parentVelocity + relativeVelocity);
        }
        return (body.Position, body.Velocity);
    }
}
