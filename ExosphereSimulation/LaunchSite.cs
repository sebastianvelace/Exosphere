namespace Exosphere.Simulation;

using System;
using System.Collections.Generic;
using System.Text.Json;
using Exosphere.Simulation.Math;

/// <summary>
/// A launch site on a celestial body, loaded from <c>data/launch_sites/*.json</c>.
///
/// Latitude is the physically load-bearing field: it fixes the site's co-latitude from
/// the body's spin axis, and therefore the eastward velocity the pad hands a vehicle for
/// free (ω·R·cos φ). Kennedy at 28.6° N inherits ≈408 m/s; a polar pad inherits nothing.
/// </summary>
public class LaunchSite
{
    /// <summary>Unique lower-case identifier (e.g. "kennedy").</summary>
    public string Id   { get; init; } = "";

    /// <summary>Display name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Id of the <see cref="CelestialBody"/> the site sits on.</summary>
    public string BodyId { get; init; } = "";

    /// <summary>Geodetic latitude, +N (degrees).</summary>
    public double Latitude  { get; init; }

    /// <summary>Longitude, +E (degrees).</summary>
    public double Longitude { get; init; }

    /// <summary>Pad height above mean radius (m).</summary>
    public double Altitude  { get; init; }

    /// <summary>Launch azimuth (degrees clockwise from north). 90 ⇒ due east.</summary>
    public double Heading   { get; init; }

    /// <summary>Inertial position of the pad surface on <paramref name="body"/>.</summary>
    public Vector3d GetPosition(CelestialBody body) =>
        body.GetSurfacePosition(Latitude, Longitude, Altitude);

    /// <summary>Local vertical (radial up) at the pad.</summary>
    public Vector3d GetUpDirection(CelestialBody body) =>
        (GetPosition(body) - body.Position).Normalized;

    /// <summary>
    /// Inertial velocity of the pad itself: the free ride the body's rotation gives a
    /// vehicle still sitting on the ground. Includes the body's own orbital velocity.
    /// </summary>
    public Vector3d GetVelocity(CelestialBody body) =>
        body.Velocity + body.GetSurfaceVelocity(GetPosition(body));

    /// <summary>
    /// Speed of the eastward boost alone (m/s), excluding the body's orbital motion.
    /// This is ω·R·cos(latitude).
    /// </summary>
    public double GetRotationalBoost(CelestialBody body) =>
        body.GetSurfaceVelocity(GetPosition(body)).Magnitude;

    // ── JSON loading ───────────────────────────────────────────────────────

    public static LaunchSite LoadFromJson(string path)
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        var root = doc.RootElement;

        return new LaunchSite
        {
            Id        = root.GetProperty("id").GetString()   ?? "",
            Name      = root.GetProperty("name").GetString() ?? "",
            BodyId    = root.GetProperty("body").GetString() ?? "",
            Latitude  = root.GetProperty("latitude").GetDouble(),
            Longitude = root.GetProperty("longitude").GetDouble(),
            Altitude  = root.TryGetProperty("altitude", out var alt) ? alt.GetDouble() : 0.0,
            Heading   = root.TryGetProperty("heading",  out var hdg) ? hdg.GetDouble() : 90.0,
        };
    }

    public static Dictionary<string, LaunchSite> LoadAllFromDirectory(string dirPath)
    {
        var result = new Dictionary<string, LaunchSite>(StringComparer.Ordinal);
        if (!System.IO.Directory.Exists(dirPath)) return result;

        foreach (var file in System.IO.Directory.GetFiles(dirPath, "*.json"))
        {
            var site = LoadFromJson(file);
            result[site.Id] = site;
        }
        return result;
    }
}
