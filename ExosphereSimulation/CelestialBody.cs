namespace Exosphere.Simulation;

using Exosphere.Simulation.Math;

/// <summary>
/// Represents a planet, moon, star, or any gravitational body in the simulation.
/// Static properties are loaded from JSON; dynamic state (Position, Velocity) is
/// updated at runtime by the integrator.
/// </summary>
public partial class CelestialBody
{
    // ── Static properties (loaded from JSON) ──────────────────────────────

    /// <summary>Unique lower-case identifier (e.g. "earth", "moon").</summary>
    public string Id   { get; init; } = "";

    /// <summary>Display name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Mass (kg).</summary>
    public double Mass              { get; init; }

    /// <summary>Mean radius (m).</summary>
    public double Radius            { get; init; }

    /// <summary>
    /// Equatorial radius (m) used by the J2 term and the WGS-style ellipsoid.
    /// Distinct from <see cref="Radius"/> (mean radius). Required when <see cref="J2"/>
    /// is non-zero.
    /// </summary>
    public double EquatorialRadius  { get; init; }

    /// <summary>
    /// Polar radius (m) of the reference ellipsoid. Required with
    /// <see cref="EquatorialRadius"/> when <see cref="J2"/> is non-zero so surface
    /// gravity, altitude and pad placement share the same datum as the field.
    /// </summary>
    public double PolarRadius       { get; init; }

    /// <summary>
    /// Unnormalised J2 zonal harmonic (dimensionless). Zero (the default) keeps a
    /// point-mass field. First-order oblateness only; Kepler-on-rails remains two-body
    /// (same class as no third body). Active/RK4 vessels feel J2 through
    /// <see cref="GetGravityAt"/>.
    /// </summary>
    public double J2                { get; init; }

    /// <summary>Standard gravitational parameter μ = GM (m³/s²).</summary>
    public double GM                { get; init; }

    /// <summary>Radius of the sphere of influence (m).</summary>
    public double SphereOfInfluence { get; init; }

    /// <summary>Sidereal rotational period (s). Negative means retrograde rotation.</summary>
    public double RotationalPeriod  { get; init; }

    /// <summary>Axial tilt relative to the ecliptic (degrees).</summary>
    public double AxialTilt         { get; init; }

    /// <summary>Optional atmospheric model. Null for airless bodies.</summary>
    public AtmosphereModel? Atmosphere { get; init; }

    /// <summary>
    /// Keplerian orbital elements relative to the parent body.
    /// Null for the root body (e.g. the Sun).
    /// </summary>
    public OrbitalElements? OrbitalElements { get; set; }

    // ── Runtime state (updated by integrator) ─────────────────────────────

    /// <summary>Position in the inertial simulation frame (m).</summary>
    public Vector3d Position { get; set; }

    /// <summary>Velocity in the inertial simulation frame (m/s).</summary>
    public Vector3d Velocity { get; set; }

    // ── Computed helpers ──────────────────────────────────────────────────

    /// <summary>
    /// True when this body has a distinct equatorial/polar radius. Surface geometry,
    /// geodetic altitude and pad frames then use the ellipsoid instead of the mean sphere.
    /// </summary>
    public bool IsOblate =>
        EquatorialRadius > PolarRadius + 1.0 && PolarRadius > 0.0;

    /// <summary>Largest body radius (m): equatorial if oblate, otherwise mean.</summary>
    public double MaximumRadius => IsOblate ? EquatorialRadius : Radius;

    /// <summary>Smallest body radius (m): polar if oblate, otherwise mean.</summary>
    public double MinimumRadius => IsOblate ? PolarRadius : Radius;

    /// <summary>
    /// Gravitational acceleration (m/s²) at the equatorial surface. Includes J2 when
    /// armed; does not include the centrifugal term of the rotating frame.
    /// </summary>
    public double GetSurfaceGravity() =>
        GetGravityAt(GetSurfacePosition(0.0, 0.0, 0.0)).Magnitude;

    /// <summary>
    /// Height of <paramref name="worldPos"/> above the reference surface (m).
    /// Geodetic height on an oblate ellipsoid; |r| − R on a sphere. Negative values
    /// mean the point is below the surface.
    /// </summary>
    public double GetAltitude(Vector3d worldPos)
    {
        GetGeodeticCoordinates(worldPos, out _, out _, out double height);
        return height;
    }

    /// <summary>Returns true if <paramref name="worldPos"/> is inside the atmosphere.</summary>
    public bool IsInAtmosphere(Vector3d worldPos) =>
        Atmosphere != null && GetAltitude(worldPos) < Atmosphere.MaxAltitude;

    /// <summary>
    /// Atmospheric density (kg/m³) at <paramref name="worldPos"/>.
    /// Returns 0 if the body has no atmosphere.
    /// </summary>
    public double GetAtmosphericDensity(Vector3d worldPos) =>
        Atmosphere?.GetDensity(GetAltitude(worldPos)) ?? 0.0;

    /// <summary>
    /// Atmospheric pressure (Pa) at <paramref name="worldPos"/>.
    /// Returns 0 if the body has no atmosphere.
    /// </summary>
    public double GetAtmosphericPressure(Vector3d worldPos) =>
        Atmosphere?.GetPressure(GetAltitude(worldPos)) ?? 0.0;

    /// <summary>
    /// Gravitational acceleration vector (m/s²) at <paramref name="worldPos"/>.
    /// Point-mass GM/r² plus, when <see cref="J2"/> is set, the first-order zonal
    /// perturbation in the body equatorial frame (+Z = <see cref="RotationAxis"/>).
    /// Returns zero if the position coincides with the body's centre.
    /// </summary>
    public Vector3d GetGravityAt(Vector3d worldPos)
    {
        var    rVec   = worldPos - Position;
        double distSq = rVec.MagnitudeSquared;
        if (distSq < 1.0) return Vector3d.Zero;

        if (J2 == 0.0)
            return rVec.Normalized * (-GM / distSq);

        double r = System.Math.Sqrt(distSq);
        GetBodyFixedEquatorialBasis(out var eastOfPrime, out var ninetyEast, out var north);
        double x = rVec.Dot(eastOfPrime);
        double y = rVec.Dot(ninetyEast);
        double z = rVec.Dot(north);
        double muOverR3 = GM / (distSq * r);
        double reOverR = EquatorialRadius / r;
        double j2Scale = 1.5 * J2 * reOverR * reOverR;
        double zr2 = (z / r) * (z / r);
        double ax = -muOverR3 * x * (1.0 - j2Scale * (5.0 * zr2 - 1.0));
        double ay = -muOverR3 * y * (1.0 - j2Scale * (5.0 * zr2 - 1.0));
        double az = -muOverR3 * z * (1.0 - j2Scale * (5.0 * zr2 - 3.0));
        return eastOfPrime * ax + ninetyEast * ay + north * az;
    }

    /// <summary>
    /// Body-fixed equatorial basis: prime meridian, 90° east, and spin axis (north).
    /// Same construction as <see cref="GetSurfacePosition"/> so gravity latitude and
    /// geodetic latitude cannot drift apart.
    /// </summary>
    private void GetBodyFixedEquatorialBasis(
        out Vector3d primeMeridian, out Vector3d ninetyEast, out Vector3d north)
    {
        north = RotationAxis;
        var seed = System.Math.Abs(north.Z) < 0.9 ? new Vector3d(0, 0, 1) : new Vector3d(1, 0, 0);
        primeMeridian = seed.Cross(north).Normalized;
        ninetyEast = north.Cross(primeMeridian).Normalized;
    }

    /// <summary>
    /// Unit rotation axis in the inertial frame: +Y tilted toward +X by the axial tilt.
    /// This is the single definition of the body's spin axis — surface velocity, latitude
    /// and the local east direction are all derived from it, so they cannot drift apart.
    /// </summary>
    public Vector3d RotationAxis
    {
        get
        {
            double tiltRad = AxialTilt * MathUtils.DEG_TO_RAD;
            return new Vector3d(
                System.Math.Sin(tiltRad),
                System.Math.Cos(tiltRad),
                0.0).Normalized;
        }
    }

    /// <summary>Angular velocity ω (rad/s). Sign preserved: negative ⇒ retrograde.</summary>
    public double AngularSpeed =>
        System.Math.Abs(RotationalPeriod) < 1.0
            ? 0.0
            : 2.0 * System.Math.PI / RotationalPeriod;

    /// <summary>
    /// Surface velocity at <paramref name="worldPos"/> due to the body's rotation (m/s):
    /// v = ω⃗ × r⃗. This is what a launch site hands the vehicle for free, and what
    /// airspeed is measured against.
    /// </summary>
    public Vector3d GetSurfaceVelocity(Vector3d worldPos)
    {
        if (AngularSpeed == 0.0) return Vector3d.Zero;

        var omegaVec = RotationAxis * AngularSpeed;
        return omegaVec.Cross(worldPos - Position);
    }

    /// <summary>
    /// Local east (the direction the surface is carried by rotation) at
    /// <paramref name="worldPos"/>: east = ω̂ × r̂. Degenerate at the poles, where the
    /// radial and the spin axis are parallel; returns zero there.
    /// </summary>
    public Vector3d GetEastDirection(Vector3d worldPos)
    {
        var relPos = worldPos - Position;
        if (relPos.MagnitudeSquared < 1.0) return Vector3d.Zero;

        var east = RotationAxis.Cross(relPos.Normalized);
        return east.Magnitude > 1e-9 ? east.Normalized : Vector3d.Zero;
    }

    /// <summary>
    /// Geodetic latitude (degrees, +N) of <paramref name="worldPos"/>, measured from the
    /// equatorial plane normal to <see cref="RotationAxis"/>. On a sphere this equals
    /// geocentric latitude; on an oblate ellipsoid it is the WGS-style geodetic latitude.
    /// </summary>
    public double GetLatitude(Vector3d worldPos)
    {
        GetGeodeticCoordinates(worldPos, out double latitudeDeg, out _, out _);
        return latitudeDeg;
    }

    /// <summary>
    /// Outward geodetic unit normal at <paramref name="worldPos"/> (local vertical).
    /// On a sphere this is the geocentric radial.
    /// </summary>
    public Vector3d GetGeodeticUp(Vector3d worldPos)
    {
        GetGeodeticCoordinates(worldPos, out double latitudeDeg, out double longitudeDeg, out _);
        return GeodeticUp(latitudeDeg, longitudeDeg);
    }

    /// <summary>
    /// Projects <paramref name="worldPos"/> onto the reference surface plus
    /// <paramref name="heightAboveSurface"/> along the local geodetic vertical.
    /// </summary>
    public Vector3d GetSurfacePoint(Vector3d worldPos, double heightAboveSurface)
    {
        GetGeodeticCoordinates(worldPos, out double latitudeDeg, out double longitudeDeg, out _);
        return GetSurfacePosition(latitudeDeg, longitudeDeg, heightAboveSurface);
    }

    /// <summary>
    /// Geodetic (φ, λ, h) of <paramref name="worldPos"/> in the body equatorial frame.
    /// Longitude is measured from the inertial prime meridian (seed × spin axis); it is
    /// not Greenwich. Height is metres above the ellipsoid or mean sphere.
    /// </summary>
    public void GetGeodeticCoordinates(
        Vector3d worldPos, out double latitudeDeg, out double longitudeDeg, out double heightM)
    {
        GetBodyFixedEquatorialBasis(out var primeMeridian, out var ninetyEast, out var north);
        var rel = worldPos - Position;
        double x = rel.Dot(primeMeridian);
        double y = rel.Dot(ninetyEast);
        double z = rel.Dot(north);
        double p = System.Math.Sqrt(x * x + y * y);
        longitudeDeg = System.Math.Atan2(y, x) * MathUtils.RAD_TO_DEG;

        if (!IsOblate)
        {
            double r = System.Math.Sqrt(p * p + z * z);
            if (r < 1.0)
            {
                latitudeDeg = 0.0;
                heightM = -Radius;
                return;
            }

            latitudeDeg = System.Math.Atan2(z, p) * MathUtils.RAD_TO_DEG;
            heightM = r - Radius;
            return;
        }

        if (p < 1e-12 * System.Math.Max(EquatorialRadius, System.Math.Sqrt(p * p + z * z)))
        {
            latitudeDeg = z >= 0.0 ? 90.0 : -90.0;
            heightM = System.Math.Abs(z) - PolarRadius;
            return;
        }

        double a = EquatorialRadius;
        double b = PolarRadius;
        double e2 = 1.0 - (b * b) / (a * a);
        double ep2 = (a * a) / (b * b) - 1.0;
        double theta = System.Math.Atan2(z * a, p * b);
        double sinTheta = System.Math.Sin(theta);
        double cosTheta = System.Math.Cos(theta);
        double lat = System.Math.Atan2(
            z + ep2 * b * sinTheta * sinTheta * sinTheta,
            p - e2 * a * cosTheta * cosTheta * cosTheta);
        double sinLat = System.Math.Sin(lat);
        double n = a / System.Math.Sqrt(1.0 - e2 * sinLat * sinLat);
        latitudeDeg = lat * MathUtils.RAD_TO_DEG;
        heightM = p / System.Math.Cos(lat) - n;
    }

    /// <summary>
    /// Inertial position of the surface point at the given geodetic coordinates.
    ///
    /// The body-fixed basis is built from <see cref="RotationAxis"/>, so the co-latitude —
    /// and therefore the rotational boost a launch site inherits — is physically correct:
    /// a site at latitude φ is carried east at ω·ρ, with ρ the distance from the spin axis
    /// on the ellipsoid (ω·a·cos φ at the equator).
    ///
    /// Longitude is measured about the spin axis from an arbitrary but fixed prime
    /// meridian. The simulation does not track a Greenwich sidereal phase at J2000, so
    /// longitude only fixes where sites sit relative to each other, not where they sit
    /// against the stars at an epoch. <see cref="GetSurfacePositionAtTime"/> still
    /// advances that meridian at the sidereal angular speed.
    /// </summary>
    /// <param name="latitudeDeg">Geodetic latitude, +N (degrees).</param>
    /// <param name="longitudeDeg">Longitude, +E (degrees).</param>
    /// <param name="altitudeM">Height above the reference ellipsoid or mean sphere (m).</param>
    public Vector3d GetSurfacePosition(double latitudeDeg, double longitudeDeg, double altitudeM = 0.0)
    {
        double lat = latitudeDeg  * MathUtils.DEG_TO_RAD;
        double lon = longitudeDeg * MathUtils.DEG_TO_RAD;
        GetBodyFixedEquatorialBasis(out var primeMeridian, out var ninetyEast, out var north);
        var equatorial = primeMeridian * System.Math.Cos(lon) + ninetyEast * System.Math.Sin(lon);

        if (!IsOblate)
        {
            var up = equatorial * System.Math.Cos(lat) + north * System.Math.Sin(lat);
            return Position + up.Normalized * (Radius + altitudeM);
        }

        double sinLat = System.Math.Sin(lat);
        double cosLat = System.Math.Cos(lat);
        double a = EquatorialRadius;
        double e2 = 1.0 - (PolarRadius * PolarRadius) / (a * a);
        double n = a / System.Math.Sqrt(1.0 - e2 * sinLat * sinLat);
        double rho = (n + altitudeM) * cosLat;
        double z = (n * (1.0 - e2) + altitudeM) * sinLat;
        return Position + equatorial * rho + north * z;
    }

    /// <summary>
    /// Point at geodetic height <paramref name="heightAboveSurface"/> along
    /// <paramref name="inertialDirectionFromCentre"/>. Tests and probes that used
    /// <c>centre + R̂·(R + h)</c> on a sphere should call this so they are not buried
    /// inside an oblate Earth.
    ///
    /// The seed sits on the ellipsoid along the geocentric ray, not at the equatorial
    /// radius. Geodetic latitude is not constant along a ray from the centre; seeding
    /// at <see cref="MaximumRadius"/> would walk a mid-latitude pad tens of metres
    /// equatorward on every hold snap.
    /// </summary>
    public Vector3d GetPositionAlongDirection(
        Vector3d inertialDirectionFromCentre, double heightAboveSurface)
    {
        var dir = inertialDirectionFromCentre.MagnitudeSquared > 1e-24
            ? inertialDirectionFromCentre.Normalized
            : Vector3d.Up;
        double radius = GeocentricRadiusOfEllipsoid(dir);
        var guess = Position + dir * radius;
        return GetSurfacePoint(guess, heightAboveSurface);
    }

    /// <summary>
    /// Radial distance (m) from the body centre to the reference ellipsoid along
    /// <paramref name="inertialDirectionFromCentre"/>. Equals <see cref="Radius"/>
    /// on a sphere.
    /// </summary>
    public double GeocentricRadiusOfEllipsoid(Vector3d inertialDirectionFromCentre)
    {
        if (!IsOblate) return Radius;
        var dir = inertialDirectionFromCentre.MagnitudeSquared > 1e-24
            ? inertialDirectionFromCentre.Normalized
            : Vector3d.Up;
        GetBodyFixedEquatorialBasis(out _, out _, out var north);
        double sinLatc = System.Math.Clamp(dir.Dot(north), -1.0, 1.0);
        double cosLatcSq = System.Math.Max(0.0, 1.0 - sinLatc * sinLatc);
        double a = EquatorialRadius;
        double b = PolarRadius;
        return (a * b) / System.Math.Sqrt(b * b * cosLatcSq + a * a * sinLatc * sinLatc);
    }

    /// <summary>
    /// Surface point at <paramref name="heightAboveSurface"/> whose geodetic up is
    /// <paramref name="geodeticUp"/>. The geodetic normal does not pass through the
    /// centre, so this must not be implemented as centre + R·n̂.
    /// </summary>
    public Vector3d GetSurfacePositionFromGeodeticUp(Vector3d geodeticUp, double heightAboveSurface)
    {
        var n = geodeticUp.MagnitudeSquared > 1e-24 ? geodeticUp.Normalized : Vector3d.Up;
        GetBodyFixedEquatorialBasis(out var primeMeridian, out var ninetyEast, out var north);
        double sinLat = System.Math.Clamp(n.Dot(north), -1.0, 1.0);
        double latitudeDeg = System.Math.Asin(sinLat) * MathUtils.RAD_TO_DEG;
        var equatorial = n - north * sinLat;
        double longitudeDeg = equatorial.MagnitudeSquared < 1e-24
            ? 0.0
            : System.Math.Atan2(equatorial.Dot(ninetyEast), equatorial.Dot(primeMeridian))
                * MathUtils.RAD_TO_DEG;
        return GetSurfacePosition(latitudeDeg, longitudeDeg, heightAboveSurface);
    }

    private Vector3d GeodeticUp(double latitudeDeg, double longitudeDeg)
    {
        double lat = latitudeDeg * MathUtils.DEG_TO_RAD;
        double lon = longitudeDeg * MathUtils.DEG_TO_RAD;
        GetBodyFixedEquatorialBasis(out var primeMeridian, out var ninetyEast, out var north);
        var equatorial = primeMeridian * System.Math.Cos(lon) + ninetyEast * System.Math.Sin(lon);
        return (equatorial * System.Math.Cos(lat) + north * System.Math.Sin(lat)).Normalized;
    }

    /// <summary>
    /// Inertial position of a body-fixed surface coordinate at simulation time. Longitude
    /// advances by the body's sidereal angular speed, making the derivative exactly the
    /// rotational surface velocity used by atmosphere and launch physics.
    /// </summary>
    public Vector3d GetSurfacePositionAtTime(
        double latitudeDeg, double longitudeDeg, double simulationTime, double altitudeM = 0.0)
    {
        double rotationDegrees = AngularSpeed * simulationTime * MathUtils.RAD_TO_DEG;
        return GetSurfacePosition(latitudeDeg, longitudeDeg + rotationDegrees, altitudeM);
    }

    /// <summary>Transforms an inertial direction into the body's rotating frame.</summary>
    public Vector3d ToBodyFixedDirection(Vector3d inertialDirection, double simulationTime)
    {
        if (AngularSpeed == 0.0) return inertialDirection;
        var inverseSpin = Math.Quaterniond.FromAxisAngle(
            RotationAxis, -AngularSpeed * simulationTime);
        return inverseSpin.Rotate(inertialDirection);
    }

    // ── JSON loading ───────────────────────────────────────────────────────

    /// <summary>
    /// Loads a <see cref="CelestialBody"/> from a JSON file.
    /// Expected format matches /data/bodies/*.json.
    /// </summary>
    public static CelestialBody LoadFromJson(string jsonPath)
    {
        var  text = System.IO.File.ReadAllText(jsonPath);
        using var doc  = System.Text.Json.JsonDocument.Parse(text);
        var  root = doc.RootElement;

        // ── Atmosphere ────────────────────────────────────────────────────
        AtmosphereModel? atmo = null;
        if (root.TryGetProperty("has_atmosphere", out var hasAtmo) && hasAtmo.GetBoolean()
            && root.TryGetProperty("atmosphere", out var atmoEl)
            && atmoEl.ValueKind != System.Text.Json.JsonValueKind.Null)
        {
            atmo = AtmosphereModel.FromJson(atmoEl);
        }

        // ── Orbital elements ──────────────────────────────────────────────
        OrbitalElements? orbEl = null;
        if (root.TryGetProperty("orbital_elements", out var oeEl)
            && oeEl.ValueKind != System.Text.Json.JsonValueKind.Null)
        {
            orbEl = new OrbitalElements
            {
                SemiMajorAxis            = oeEl.GetProperty("semi_major_axis").GetDouble(),
                Eccentricity             = oeEl.GetProperty("eccentricity").GetDouble(),
                Inclination              = oeEl.GetProperty("inclination").GetDouble()           * MathUtils.DEG_TO_RAD,
                LongitudeOfAscendingNode = oeEl.GetProperty("longitude_of_node").GetDouble()    * MathUtils.DEG_TO_RAD,
                ArgumentOfPeriapsis      = oeEl.GetProperty("argument_of_periapsis").GetDouble() * MathUtils.DEG_TO_RAD,
                MeanAnomalyAtEpoch       = oeEl.GetProperty("mean_anomaly_at_epoch").GetDouble() * MathUtils.DEG_TO_RAD,
                Epoch                    = oeEl.TryGetProperty("epoch", out var ep) ? ep.GetDouble() : 0.0,
                ReferenceBodyId          = oeEl.GetProperty("reference_body").GetString() ?? "",
            };
        }

        double j2 = 0.0;
        if (root.TryGetProperty("j2", out var j2El) && j2El.ValueKind != System.Text.Json.JsonValueKind.Null)
            j2 = j2El.GetDouble();
        if (!double.IsFinite(j2) || j2 < 0.0)
            throw new InvalidDataException($"Body '{root.GetProperty("id").GetString()}' has invalid j2.");

        double equatorialRadius = 0.0;
        if (root.TryGetProperty("equatorial_radius", out var eqEl)
            && eqEl.ValueKind != System.Text.Json.JsonValueKind.Null)
            equatorialRadius = eqEl.GetDouble();
        double polarRadius = 0.0;
        if (root.TryGetProperty("polar_radius", out var polEl)
            && polEl.ValueKind != System.Text.Json.JsonValueKind.Null)
            polarRadius = polEl.GetDouble();
        string bodyId = root.GetProperty("id").GetString() ?? "";
        if (j2 > 0.0 && !(equatorialRadius > 0.0 && double.IsFinite(equatorialRadius)))
            throw new InvalidDataException(
                $"Body '{bodyId}' sets j2 but is missing a positive equatorial_radius.");
        if (j2 > 0.0 && !(polarRadius > 0.0 && polarRadius < equatorialRadius && double.IsFinite(polarRadius)))
            throw new InvalidDataException(
                $"Body '{bodyId}' sets j2 but is missing a polar_radius smaller than equatorial_radius.");

        return new CelestialBody
        {
            Id                = bodyId,
            Name              = root.GetProperty("name").GetString() ?? "",
            Mass              = root.GetProperty("mass").GetDouble(),
            Radius            = root.GetProperty("radius").GetDouble(),
            GM                = root.GetProperty("gm").GetDouble(),
            SphereOfInfluence = root.GetProperty("soi").GetDouble(),
            RotationalPeriod  = root.GetProperty("rotational_period").GetDouble(),
            AxialTilt         = root.GetProperty("axial_tilt").GetDouble(),
            J2                = j2,
            EquatorialRadius  = equatorialRadius,
            PolarRadius       = polarRadius,
            Atmosphere        = atmo,
            OrbitalElements   = orbEl,
        };
    }

    /// <summary>
    /// Copy with J2 and ellipsoid radii cleared. Scheduler tests that prove Kepler ≡ RK4
    /// need a spherical point-mass Earth so oblateness cannot masquerade as a warp bug.
    /// </summary>
    public CelestialBody WithoutOblateness() => new()
    {
        Id = Id,
        Name = Name,
        Mass = Mass,
        Radius = Radius,
        GM = GM,
        SphereOfInfluence = SphereOfInfluence,
        RotationalPeriod = RotationalPeriod,
        AxialTilt = AxialTilt,
        J2 = 0.0,
        EquatorialRadius = 0.0,
        PolarRadius = 0.0,
        Atmosphere = Atmosphere,
        OrbitalElements = OrbitalElements,
        Position = Position,
        Velocity = Velocity,
    };

    /// <summary>
    /// Loads all <c>*.json</c> files in <paramref name="dirPath"/> as celestial bodies,
    /// keyed by their <see cref="Id"/>.
    /// </summary>
    public static Dictionary<string, CelestialBody> LoadAllFromDirectory(string dirPath)
    {
        var result = new Dictionary<string, CelestialBody>(StringComparer.Ordinal);
        foreach (var file in System.IO.Directory.GetFiles(dirPath, "*.json"))
        {
            var body = LoadFromJson(file);
            result[body.Id] = body;
        }
        return result;
    }
}
