namespace Exosphere.Simulation.Environment;

using Exosphere.Simulation.Math;

/// <summary>
/// Source of the inertial state of every celestial body (environment plan E0.3).
/// <see cref="KeplerEphemeris"/> is today's two-body model; dated Chebyshev ephemerides (DE440,
/// plan E2) implement the same contract, so <see cref="Universe"/> asks one place for body
/// states and never knows which model answers.
/// </summary>
public interface IEphemerisProvider
{
    /// <summary>Short identifier recorded in saves and telemetry, e.g. "kepler".</summary>
    string Id { get; }

    /// <summary>
    /// Writes every body's inertial <see cref="CelestialBody.Position"/> and
    /// <see cref="CelestialBody.Velocity"/> at <paramref name="time"/> (seconds since J2000).
    /// Parents are resolved before children whatever the list order.
    /// </summary>
    void PropagateAll(IReadOnlyList<CelestialBody> bodies, double time);

    /// <summary>
    /// Inertial state of <paramref name="body"/> at <paramref name="time"/> without mutating
    /// any body, so sub-step events (SOI crossings, slosh epochs) read the frame at their own
    /// instant. <paramref name="resolve"/> maps a body id to its body.
    /// </summary>
    (Vector3d Position, Vector3d Velocity) StateAt(
        CelestialBody body, double time, Func<string, CelestialBody?> resolve);
}
