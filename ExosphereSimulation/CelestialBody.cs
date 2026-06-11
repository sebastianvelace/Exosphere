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
    /// Surface velocity at <paramref name="worldPos"/> due to the body's rotation (m/s).
    /// Used to compute airspeed relative to the atmosphere.
    /// </summary>
    public Vector3d GetSurfaceVelocity(Vector3d worldPos)
    {
        if (System.Math.Abs(RotationalPeriod) < 1.0) return Vector3d.Zero;

        double omega    = 2.0 * System.Math.PI / RotationalPeriod;   // rad/s (sign preserved)
        double tiltRad  = AxialTilt * MathUtils.DEG_TO_RAD;

        // Rotation axis: +Y tilted toward +X by the axial tilt
        var rotAxis = new Vector3d(
            System.Math.Sin(tiltRad),
            System.Math.Cos(tiltRad),
            0.0).Normalized;

        // ω⃗ = omega * rotAxis
        var omegaVec = rotAxis * omega;

        // v = ω⃗ × r⃗
        var relPos = worldPos - Position;
        return omegaVec.Cross(relPos);
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
