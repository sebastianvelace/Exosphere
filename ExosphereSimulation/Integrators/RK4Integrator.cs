namespace Exosphere.Simulation.Integrators;

using Exosphere.Simulation.Math;

/// <summary>
/// Classic fourth-order Runge-Kutta integrator.
/// All methods are stateless and pure; thread-safe for concurrent use.
/// </summary>
public static class RK4Integrator
{
    // ── Generic array API ─────────────────────────────────────────────────

    /// <summary>
    /// Advances an arbitrary state vector <paramref name="state"/> by one time step
    /// <paramref name="dt"/> using the RK4 method.
    /// </summary>
    /// <param name="state">
    ///   Current state as a flat array of doubles.
    ///   For 6-DoF orbital mechanics this is [x, y, z, vx, vy, vz].
    /// </param>
    /// <param name="t">Current simulation time (s).</param>
    /// <param name="dt">Time step (s).</param>
    /// <param name="derivative">
    ///   Function that takes (state, t) and returns the time derivative of each element.
    ///   For orbital mechanics: returns [vx, vy, vz, ax, ay, az].
    /// </param>
    /// <returns>New state after <paramref name="dt"/> seconds.</returns>
    public static double[] Step(
        double[] state,
        double t,
        double dt,
        Func<double[], double, double[]> derivative)
    {
        int n = state.Length;

        double[] k1 = derivative(state, t);

        double[] s2 = Add(state, Scale(k1, dt * 0.5));
        double[] k2 = derivative(s2, t + dt * 0.5);

        double[] s3 = Add(state, Scale(k2, dt * 0.5));
        double[] k3 = derivative(s3, t + dt * 0.5);

        double[] s4 = Add(state, Scale(k3, dt));
        double[] k4 = derivative(s4, t + dt);

        var result = new double[n];
        double dtOver6 = dt / 6.0;
        for (int i = 0; i < n; i++)
            result[i] = state[i] + (k1[i] + 2.0 * k2[i] + 2.0 * k3[i] + k4[i]) * dtOver6;

        return result;
    }

    // ── Convenience: position + velocity API ─────────────────────────────

    /// <summary>
    /// Advances a (position, velocity) pair by <paramref name="dt"/> seconds.
    /// </summary>
    /// <param name="pos">Current position (m), inertial frame.</param>
    /// <param name="vel">Current velocity (m/s), inertial frame.</param>
    /// <param name="t">Current simulation time (s).</param>
    /// <param name="dt">Time step (s).</param>
    /// <param name="acceleration">
    ///   Function that returns the net acceleration (m/s²) given (position, velocity, time).
    ///   Must be deterministic for the same inputs.
    /// </param>
    /// <returns>New (position, velocity) after <paramref name="dt"/> seconds.</returns>
    public static (Vector3d newPos, Vector3d newVel) StepPosVel(
        Vector3d pos,
        Vector3d vel,
        double t,
        double dt,
        Func<Vector3d, Vector3d, double, Vector3d> acceleration)
    {
        // Keep the generic array API above for callers that integrate arbitrary state
        // vectors, but use scalar/vector stages for the simulation's 6-DoF path. The old
        // implementation allocated the state, four derivative arrays, four intermediate
        // arrays and the result on every vessel sub-step. Vector3d is a value type, so the
        // equivalent formulation is allocation-free apart from a caller-owned closure.
        double halfDt = dt * 0.5;
        double dtOver6 = dt / 6.0;

        Vector3d k1Pos = vel;
        Vector3d k1Vel = acceleration(pos, vel, t);

        Vector3d p2 = pos + k1Pos * halfDt;
        Vector3d v2 = vel + k1Vel * halfDt;
        Vector3d k2Pos = v2;
        Vector3d k2Vel = acceleration(p2, v2, t + halfDt);

        Vector3d p3 = pos + k2Pos * halfDt;
        Vector3d v3 = vel + k2Vel * halfDt;
        Vector3d k3Pos = v3;
        Vector3d k3Vel = acceleration(p3, v3, t + halfDt);

        Vector3d p4 = pos + k3Pos * dt;
        Vector3d v4 = vel + k3Vel * dt;
        Vector3d k4Pos = v4;
        Vector3d k4Vel = acceleration(p4, v4, t + dt);

        Vector3d weightedPosition = k1Pos
            + 2.0 * k2Pos
            + 2.0 * k3Pos
            + k4Pos;
        Vector3d weightedVelocity = k1Vel
            + 2.0 * k2Vel
            + 2.0 * k3Vel
            + k4Vel;
        return (
            pos + weightedPosition * dtOver6,
            vel + weightedVelocity * dtOver6);
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private static double[] Add(double[] a, double[] b)
    {
        var r = new double[a.Length];
        for (int i = 0; i < a.Length; i++)
            r[i] = a[i] + b[i];
        return r;
    }

    private static double[] Scale(double[] a, double s)
    {
        var r = new double[a.Length];
        for (int i = 0; i < a.Length; i++)
            r[i] = a[i] * s;
        return r;
    }
}
