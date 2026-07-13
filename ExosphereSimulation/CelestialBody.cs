namespace Exosphere.Simulation;

using Exosphere.Simulation.Math;

/// <summary>
/// Represents a planet, moon, star, or any gravitational body in the simulation.
/// Static properties are loaded from JSON; dynamic state (Position, Velocity) is
/// updated at runtime by the integrator.
/// </summary>
public class CelestialBody
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

    /// <summary>Surface gravitational acceleration (m/s²).</summary>
    public double GetSurfaceGravity() => GM / (Radius * Radius);

    /// <summary>
    /// Altitude of <paramref name="worldPos"/> above the surface (m).
    /// Negative values mean the point is below the surface.
    /// </summary>
    public double GetAltitude(Vector3d worldPos) =>
        (worldPos - Position).Magnitude - Radius;

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
    /// Points toward the body's centre with magnitude GM/r².
    /// Returns zero if the position coincides with the body's centre.
    /// </summary>
    public Vector3d GetGravityAt(Vector3d worldPos)
    {
        var    r      = worldPos - Position;
        double distSq = r.MagnitudeSquared;
        if (distSq < 1.0) return Vector3d.Zero;

        return r.Normalized * (-GM / distSq);
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
    /// equatorial plane normal to <see cref="RotationAxis"/>.
    /// </summary>
    public double GetLatitude(Vector3d worldPos)
    {
        var relPos = worldPos - Position;
        if (relPos.MagnitudeSquared < 1.0) return 0.0;

        double sinLat = System.Math.Clamp(relPos.Normalized.Dot(RotationAxis), -1.0, 1.0);
        return System.Math.Asin(sinLat) * MathUtils.RAD_TO_DEG;
    }

    /// <summary>
    /// Inertial position of the surface point at the given geodetic coordinates.
    ///
    /// The body-fixed basis is built from <see cref="RotationAxis"/>, so the co-latitude —
    /// and therefore the rotational boost a launch site inherits — is physically correct:
    /// a site at latitude φ is carried east at ω·R·cos φ.
    ///
    /// Longitude is measured about the spin axis from an arbitrary but fixed prime
    /// meridian. The simulation does not track a sidereal spin phase, so longitude only
    /// fixes where sites sit relative to each other, not where they sit at an epoch.
    /// </summary>
    /// <param name="latitudeDeg">Geodetic latitude, +N (degrees).</param>
    /// <param name="longitudeDeg">Longitude, +E (degrees).</param>
    /// <param name="altitudeM">Height above mean radius (m).</param>
    public Vector3d GetSurfacePosition(double latitudeDeg, double longitudeDeg, double altitudeM = 0.0)
    {
        double lat = latitudeDeg  * MathUtils.DEG_TO_RAD;
        double lon = longitudeDeg * MathUtils.DEG_TO_RAD;

        // Orthonormal body-fixed basis: axis (north) + two equatorial vectors.
        var north = RotationAxis;
        var seed  = System.Math.Abs(north.Z) < 0.9 ? new Vector3d(0, 0, 1) : new Vector3d(1, 0, 0);
        var primeMeridian = seed.Cross(north).Normalized;      // in the equatorial plane
        var ninetyEast    = north.Cross(primeMeridian).Normalized;

        var equatorial = primeMeridian * System.Math.Cos(lon) + ninetyEast * System.Math.Sin(lon);
        var up         = equatorial * System.Math.Cos(lat) + north * System.Math.Sin(lat);

        return Position + up.Normalized * (Radius + altitudeM);
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

        return new CelestialBody
        {
            Id                = root.GetProperty("id").GetString()   ?? "",
            Name              = root.GetProperty("name").GetString() ?? "",
            Mass              = root.GetProperty("mass").GetDouble(),
            Radius            = root.GetProperty("radius").GetDouble(),
            GM                = root.GetProperty("gm").GetDouble(),
            SphereOfInfluence = root.GetProperty("soi").GetDouble(),
            RotationalPeriod  = root.GetProperty("rotational_period").GetDouble(),
            AxialTilt         = root.GetProperty("axial_tilt").GetDouble(),
            Atmosphere        = atmo,
            OrbitalElements   = orbEl,
        };
    }

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
