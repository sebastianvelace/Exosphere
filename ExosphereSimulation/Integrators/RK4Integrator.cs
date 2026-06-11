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
        double[] state = [pos.X, pos.Y, pos.Z, vel.X, vel.Y, vel.Z];

        double[] result = Step(state, t, dt, (s, time) =>
        {
            var p = new Vector3d(s[0], s[1], s[2]);
            var v = new Vector3d(s[3], s[4], s[5]);
            var a = acceleration(p, v, time);
            return [v.X, v.Y, v.Z, a.X, a.Y, a.Z];
        });

        return (
            new Vector3d(result[0], result[1], result[2]),
            new Vector3d(result[3], result[4], result[5])
        );
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
