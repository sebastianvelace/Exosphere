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
    public static void PropagateAllBodies(IEnumerable<CelestialBody> bodies, double targetTime)
    {
        var bodyMap = bodies.ToDictionary(b => b.Id, StringComparer.Ordinal);
        var completed = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        foreach (var body in bodyMap.Values)
            PropagateHierarchy(body, bodyMap, targetTime, completed, visiting);
    }

    private static void PropagateHierarchy(
        CelestialBody body,
        IReadOnlyDictionary<string, CelestialBody> bodyMap,
        double targetTime,
        HashSet<string> completed,
        HashSet<string> visiting)
    {
        if (completed.Contains(body.Id)) return;
        if (!visiting.Add(body.Id))
            throw new InvalidOperationException($"Orbital reference cycle detected at '{body.Id}'.");

        if (body.OrbitalElements is { } elements)
        {
            if (!bodyMap.TryGetValue(elements.ReferenceBodyId, out var refBody))
            {
                // Small isolated test/sandbox universes may intentionally omit the parent.
                // Preserve the body's supplied inertial state, matching the legacy behavior.
                visiting.Remove(body.Id);
                completed.Add(body.Id);
                return;
            }

            PropagateHierarchy(refBody, bodyMap, targetTime, completed, visiting);

            var (relPos, relVel) = elements.GetStateAtTime(targetTime, refBody.GM);

            body.Position = refBody.Position + relPos;
            body.Velocity = refBody.Velocity + relVel;
        }

        visiting.Remove(body.Id);
        completed.Add(body.Id);
    }
}
