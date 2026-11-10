namespace Exosphere.Simulation.Navigation;

using Exosphere.Simulation.Math;

/// <summary>
/// A point-mass attractor at one instant: gravitational parameter, inertial state and sphere of
/// influence (non-positive for a root body whose influence is unbounded).
/// </summary>
public readonly record struct PointMassAttractor(double Gm, Vector3d Position, Vector3d Velocity,
    double SphereOfInfluence);

/// <summary>
/// Coast of a massless point through the point-mass field of moving bodies, for planners that
/// cannot trust a patched conic (a lunar return aimed at an entry corridor a few kilometres
/// wide). It integrates the way the simulation flies an off-rails vessel: relative to the
/// dominant body (the smallest sphere of influence that contains the point), whose frame
/// accelerates with the pull of every other body at its centre, so the point feels that body
/// and the others' tides. On a dated ephemeris this is the inertial motion; on Kepler rails it
/// is what the simulation does rather than what the rails do. RK4 with a step that is a fixed
/// fraction of the shortest dynamical time √(d³/μ), so a perigee or perilune pass is resolved
/// as finely as the cruise between them is not.
/// </summary>
public static class PointMassPropagator
{
    /// <summary>Step as a fraction of the shortest dynamical time √(d³/μ).</summary>
    public const double StepFraction = 0.01;

    /// <param name="attractorsAt">Every attractor at a time, in a fixed order.</param>
    /// <param name="position">Inertial position at <paramref name="startTime"/> (m).</param>
    /// <param name="velocity">Inertial velocity at <paramref name="startTime"/> (m/s).</param>
    /// <returns>Inertial state at <paramref name="endTime"/>.</returns>
    public static (Vector3d Position, Vector3d Velocity) Propagate(
        Func<double, IReadOnlyList<PointMassAttractor>> attractorsAt,
        Vector3d position, Vector3d velocity, double startTime, double endTime, double maximumStepSeconds = 600.0)
    {
        ArgumentNullException.ThrowIfNull(attractorsAt);
        if (!double.IsFinite(startTime) || !double.IsFinite(endTime) || !(endTime >= startTime)
            || !(maximumStepSeconds > 0.0))
            throw new ArgumentOutOfRangeException(nameof(endTime));
        double time = startTime;
        while (endTime - time > 1e-9)
        {
            var bodies = attractorsAt(time);
            int reference = Dominant(bodies, position);
            double step = System.Math.Min(maximumStepSeconds,
                System.Math.Min(StepFraction * DynamicalTime(bodies, position), endTime - time));

            var r0 = position - bodies[reference].Position;
            var v0 = velocity - bodies[reference].Velocity;
            var middle = attractorsAt(time + 0.5 * step);
            var end = attractorsAt(time + step);
            var a1 = Relative(bodies, reference, r0);
            var r2 = r0 + v0 * (0.5 * step); var v2 = v0 + a1 * (0.5 * step);
            var a2 = Relative(middle, reference, r2);
            var r3 = r0 + v2 * (0.5 * step); var v3 = v0 + a2 * (0.5 * step);
            var a3 = Relative(middle, reference, r3);
            var r4 = r0 + v3 * step; var v4 = v0 + a3 * step;
            var a4 = Relative(end, reference, r4);
            var r1 = r0 + (v0 + (v2 + v3) * 2.0 + v4) * (step / 6.0);
            var v1 = v0 + (a1 + (a2 + a3) * 2.0 + a4) * (step / 6.0);
            position = end[reference].Position + r1;
            velocity = end[reference].Velocity + v1;
            time += step;
        }
        return (position, velocity);
    }

    /// <summary>Index of the attractor with the smallest sphere of influence that contains the point.</summary>
    public static int Dominant(IReadOnlyList<PointMassAttractor> bodies, Vector3d position)
    {
        int best = -1;
        double bestSphere = double.PositiveInfinity;
        for (int i = 0; i < bodies.Count; i++)
        {
            double sphere = bodies[i].SphereOfInfluence > 0.0 ? bodies[i].SphereOfInfluence : double.PositiveInfinity;
            if (sphere < bestSphere && (position - bodies[i].Position).Magnitude < sphere)
            {
                best = i;
                bestSphere = sphere;
            }
            else if (best < 0 && double.IsPositiveInfinity(sphere)) best = i;
        }
        return best >= 0 ? best : 0;
    }

    /// <summary>Acceleration relative to attractor <paramref name="reference"/> of a point at <paramref name="relative"/>.</summary>
    private static Vector3d Relative(IReadOnlyList<PointMassAttractor> bodies, int reference, Vector3d relative)
    {
        var origin = bodies[reference].Position;
        var acceleration = Vector3d.Zero;
        for (int i = 0; i < bodies.Count; i++)
        {
            acceleration += Pull(bodies[i], origin + relative);
            if (i != reference) acceleration -= Pull(bodies[i], origin);
        }
        return acceleration;
    }

    private static Vector3d Pull(PointMassAttractor body, Vector3d at)
    {
        var offset = body.Position - at;
        double distanceSquared = offset.MagnitudeSquared;
        return offset * (body.Gm / (distanceSquared * System.Math.Sqrt(distanceSquared)));
    }

    private static double DynamicalTime(IReadOnlyList<PointMassAttractor> bodies, Vector3d position)
    {
        double shortest = double.PositiveInfinity;
        for (int i = 0; i < bodies.Count; i++)
        {
            double distance = (bodies[i].Position - position).Magnitude;
            shortest = System.Math.Min(shortest, System.Math.Sqrt(distance * distance * distance / bodies[i].Gm));
        }
        return shortest;
    }
}
