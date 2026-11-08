namespace Exosphere.Simulation.Environment;

using Exosphere.Simulation.Integrators;
using Exosphere.Simulation.Math;

/// <summary>
/// Two-body Keplerian ephemeris from each body's <see cref="CelestialBody.OrbitalElements"/>
/// (data/bodies/*.json), chained to its reference body; a body without elements stays at its
/// stored state (the Sun at the origin). This is the behaviour the simulation has always had,
/// kept bit for bit, and the fallback for sandbox systems without dated data.
/// </summary>
public sealed class KeplerEphemeris : IEphemerisProvider
{
    private readonly KeplerPropagator.BodyPropagationWorkspace _workspace = new();

    public string Id => "kepler";

    public void PropagateAll(IReadOnlyList<CelestialBody> bodies, double time)
    {
        KeplerPropagator.PropagateAllBodies(bodies, time, _workspace);
        for (int i = 0; i < bodies.Count; i++) bodies[i].StateTime = time;
    }

    public (Vector3d Position, Vector3d Velocity) StateAt(
        CelestialBody body, double time, Func<string, CelestialBody?> resolve)
    {
        if (body.OrbitalElements is null)
            return (body.Position, body.Velocity);

        var reference = resolve(body.OrbitalElements.ReferenceBodyId);
        if (reference is null)
            return (body.Position, body.Velocity);

        var (referencePosition, referenceVelocity) = StateAt(reference, time, resolve);
        var (relativePosition, relativeVelocity) = body.OrbitalElements.GetStateAtTime(time, reference.GM);
        return (referencePosition + relativePosition, referenceVelocity + relativeVelocity);
    }
}
