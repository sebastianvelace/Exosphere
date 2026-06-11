namespace Exosphere.Simulation.Integrators;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;

/// <summary>
/// Keplerian (analytic, force-free) propagator for vessels and celestial bodies
/// that are placed "on rails" — i.e. their orbit is not perturbed by active forces.
/// </summary>
public static class KeplerPropagator
{
    /// <summary>
    /// Returns the inertial position and velocity of an orbit described by
    /// <paramref name="elements"/> at simulation time <paramref name="targetTime"/>.
    /// The result is relative to the reference body's centre.
    /// </summary>
    /// <param name="elements">Keplerian orbital elements.</param>
    /// <param name="targetTime">Target simulation time (s).</param>
    /// <param name="gm">Gravitational parameter of the reference body (m³/s²).</param>
    public static (Vector3d position, Vector3d velocity) PropagateToTime(
        OrbitalElements elements,
        double targetTime,
        double gm)
    {
        return elements.GetStateAtTime(targetTime, gm);
    }

    /// <summary>
    /// Converts a Cartesian state vector (position + velocity relative to the
    /// reference body) into <see cref="OrbitalElements"/>.
    /// </summary>
    /// <param name="relativePos">Position relative to reference body (m).</param>
    /// <param name="relativeVel">Velocity relative to reference body (m/s).</param>
    /// <param name="gm">Gravitational parameter of the reference body (m³/s²).</param>
    /// <param name="referenceBodyId">Id of the reference body.</param>
    /// <param name="epoch">Simulation time at which the state vector was measured (s).</param>
    public static OrbitalElements ComputeElements(
        Vector3d relativePos,
        Vector3d relativeVel,
        double gm,
        string referenceBodyId,
        double epoch)
    {
        return OrbitalElements.FromStateVector(relativePos, relativeVel, gm, referenceBodyId, epoch);
    }

    /// <summary>
    /// Propagates every <see cref="CelestialBody"/> in <paramref name="bodies"/> that has
    /// <see cref="CelestialBody.OrbitalElements"/> to <paramref name="targetTime"/> using
    /// Keplerian mechanics.  Updates <see cref="CelestialBody.Position"/> and
    /// <see cref="CelestialBody.Velocity"/> in the inertial simulation frame.
    /// Bodies without orbital elements (e.g. the Sun) are left untouched.
    /// </summary>
    /// <remarks>
    /// The propagation order matters for multi-level hierarchies (Sun → planet → moon).
    /// Callers should ensure that parent bodies are propagated before their children,
    /// or pass a list already sorted by hierarchy depth.  When all parent positions are
    /// updated first this method converges in a single pass.
    /// </remarks>
    public static void PropagateAllBodies(IEnumerable<CelestialBody> bodies, double targetTime)
    {
        // Build a fast lookup map so we can resolve parent positions.
        var bodyMap = bodies.ToDictionary(b => b.Id, StringComparer.Ordinal);

        foreach (var body in bodyMap.Values)
        {
            if (body.OrbitalElements is null) continue;   // root body — fixed at origin

            var refId = body.OrbitalElements.ReferenceBodyId;
            if (!bodyMap.TryGetValue(refId, out var refBody)) continue;

            var (relPos, relVel) = body.OrbitalElements.GetStateAtTime(targetTime, refBody.GM);

            body.Position = refBody.Position + relPos;
            body.Velocity = refBody.Velocity + relVel;
        }
    }
}
