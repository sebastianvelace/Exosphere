namespace Exosphere.Simulation.Environment;

using Exosphere.Simulation.Math;

/// <summary>
/// Wind (environment plan E4): the velocity of the air relative to the body's rotating surface,
/// in the simulation's inertial axes, at a world position. It enters every aerodynamic and air
/// data consumer through the single air point <see cref="CelestialBody.GetAirVelocity"/>; the
/// ground, contact and navigation keep the surface velocity. Time-dependent fields read
/// <see cref="CelestialBody.StateTime"/>.
/// </summary>
public interface IWindField
{
    string Id { get; }

    Vector3d WindAt(CelestialBody body, Vector3d worldPosition);
}

/// <summary>
/// A measured wind profile around a launch site (environment plan E4.1), from an envdata
/// sounding product (<c>data/environment/winds/*.bin</c>): east and north components interpolated
/// linearly in geodetic altitude, held at the lowest level below it, faded to zero over 5 km
/// above the highest. Horizontally it applies in full within <see cref="FullRadiusM"/> of the
/// station and fades to zero by <see cref="FadeRadiusM"/> (cosine taper), so a profile measured
/// at one place does not blow over the whole planet.
/// </summary>
public sealed class ProfileWind : IWindField
{
    public const double FullRadiusM = 300_000.0;
    public const double FadeRadiusM = 600_000.0;
    public const double TopFadeM = 5_000.0;

    private readonly double[] _altitude;
    private readonly double[] _east;
    private readonly double[] _north;
    private readonly double _latitudeDeg;
    private readonly double _longitudeDeg;
    private ProfileWind? _upper;

    public string Id { get; }

    public ProfileWind(EnvironmentProductFile product)
    {
        Id = product.Product;
        int altitude = product.ColumnIndex("altitude_m");
        int from = product.ColumnIndex("wind_from_deg");
        int speed = product.ColumnIndex("wind_speed_mps");
        _altitude = new double[product.Rows];
        _east = new double[product.Rows];
        _north = new double[product.Rows];
        for (int i = 0; i < product.Rows; i++)
        {
            _altitude[i] = product[i, altitude];
            if (i > 0 && _altitude[i] <= _altitude[i - 1])
                throw new InvalidDataException($"{Id}: levels are not in increasing altitude.");
            // Meteorological convention: the direction the wind blows FROM, clockwise from north.
            double fromRad = product[i, from] * System.Math.PI / 180.0;
            _east[i] = -product[i, speed] * System.Math.Sin(fromRad);
            _north[i] = -product[i, speed] * System.Math.Cos(fromRad);
        }
        _latitudeDeg = product.Header.GetProperty("station_latitude_deg").GetDouble();
        _longitudeDeg = product.Header.GetProperty("station_longitude_deg").GetDouble();
    }

    public static ProfileWind LoadFromDataDirectory(string dataDirectory, string productId) =>
        new(EnvironmentProductFile.Load(Path.Combine(dataDirectory, "environment", "winds", productId + ".bin")));

    /// <summary>
    /// Stacks a profile above this one (environment plan E4.2: HWM14 above a sounding). Above this
    /// profile's top the two blend linearly over <see cref="TopFadeM"/>; higher up the upper one
    /// answers. Returns this profile.
    /// </summary>
    public ProfileWind WithUpper(ProfileWind upper)
    {
        _upper = upper;
        return this;
    }

    /// <summary>East and north wind (m/s) at a geodetic altitude, before the horizontal taper.</summary>
    public (double East, double North) ProfileAt(double altitudeM)
    {
        int last = _altitude.Length - 1;
        if (altitudeM <= _altitude[0]) return (_east[0], _north[0]);
        if (altitudeM >= _altitude[last] && _upper is { } upper)
        {
            var (ue, un) = upper.ProfileAt(altitudeM);
            double w = System.Math.Min(1.0, (altitudeM - _altitude[last]) / TopFadeM);
            return (_east[last] + (ue - _east[last]) * w, _north[last] + (un - _north[last]) * w);
        }
        if (altitudeM >= _altitude[last])
        {
            double fade = System.Math.Max(0.0, 1.0 - (altitudeM - _altitude[last]) / TopFadeM);
            return (_east[last] * fade, _north[last] * fade);
        }
        int i = Array.BinarySearch(_altitude, altitudeM);
        if (i < 0) i = ~i - 1;
        double f = (altitudeM - _altitude[i]) / (_altitude[i + 1] - _altitude[i]);
        return (_east[i] + (_east[i + 1] - _east[i]) * f, _north[i] + (_north[i + 1] - _north[i]) * f);
    }

    public Vector3d WindAt(CelestialBody body, Vector3d worldPosition)
    {
        var site = body.GetSurfacePositionAtTime(_latitudeDeg, _longitudeDeg, body.StateTime);
        var upHere = body.GetGeodeticUp(worldPosition);
        var upSite = body.GetGeodeticUp(site);
        double central = System.Math.Acos(System.Math.Clamp(upHere.Dot(upSite), -1.0, 1.0));
        double ground = central * body.MinimumRadius;
        if (ground >= FadeRadiusM) return Vector3d.Zero;
        double taper = ground <= FullRadiusM ? 1.0
            : 0.5 * (1.0 + System.Math.Cos(System.Math.PI * (ground - FullRadiusM) / (FadeRadiusM - FullRadiusM)));
        var (eastSpeed, northSpeed) = ProfileAt(body.GetAltitude(worldPosition));
        var east = body.GetEastDirection(worldPosition);
        var north = upHere.Cross(east).Normalized;
        east = north.Cross(upHere).Normalized;
        return (east * eastSpeed + north * northSpeed) * taper;
    }
}
