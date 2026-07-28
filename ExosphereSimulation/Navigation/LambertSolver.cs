namespace Exosphere.Simulation.Navigation;

using Exosphere.Simulation.Math;

/// <summary>
/// The two-body velocity boundary condition returned by a zero-revolution Lambert solve.
/// Positions and velocities share one inertial frame and use SI units.
/// </summary>
public readonly record struct LambertSolution(
    Vector3d DepartureVelocity,
    Vector3d ArrivalVelocity,
    double   TransferAngle,
    double   TimeOfFlight);

/// <summary>
/// Universal-variable, zero-revolution Lambert solver.
///
/// This is deliberately kept in the Godot-free simulation assembly: mission guidance,
/// headless tests and the map UI must all consume the same physical solution. The solver
/// uses Stumpff functions and supports elliptic and hyperbolic transfer arcs.
/// </summary>
public static class LambertSolver
{
    private const double TwoPi = 2.0 * System.Math.PI;

    /// <summary>
    /// Solves the short- or long-way, zero-revolution Lambert boundary-value problem.
    /// </summary>
    /// <param name="centralGm">Central gravitational parameter μ (m³/s²).</param>
    /// <param name="departurePosition">Initial position relative to the central body (m).</param>
    /// <param name="arrivalPosition">Required final position in the same frame (m).</param>
    /// <param name="timeOfFlight">Elapsed transfer time (s).</param>
    /// <param name="planeNormal">
    /// Preferred positive angular-momentum direction. The sign of
    /// (r₁ × r₂)·normal selects the short-way orientation.
    /// </param>
    /// <param name="longWay">Selects an arc whose transfer angle exceeds π.</param>
    public static LambertSolution Solve(
        double   centralGm,
        Vector3d departurePosition,
        Vector3d arrivalPosition,
        double   timeOfFlight,
        Vector3d planeNormal,
        bool     longWay = false)
    {
        if (!double.IsFinite(centralGm) || centralGm <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(centralGm));
        if (!double.IsFinite(timeOfFlight) || timeOfFlight <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(timeOfFlight));
        if (!IsFinite(departurePosition) || departurePosition.Magnitude < 1.0)
            throw new ArgumentOutOfRangeException(nameof(departurePosition));
        if (!IsFinite(arrivalPosition) || arrivalPosition.Magnitude < 1.0)
            throw new ArgumentOutOfRangeException(nameof(arrivalPosition));
        if (!IsFinite(planeNormal) || planeNormal.Magnitude < 1e-12)
            throw new ArgumentOutOfRangeException(nameof(planeNormal));

        double r1 = departurePosition.Magnitude;
        double r2 = arrivalPosition.Magnitude;
        double cosAngle = System.Math.Clamp(
            departurePosition.Dot(arrivalPosition) / (r1 * r2), -1.0, 1.0);

        double unsignedAngle = System.Math.Acos(cosAngle);
        double orientation = System.Math.Sign(
            departurePosition.Cross(arrivalPosition).Dot(planeNormal));
        if (orientation == 0.0) orientation = 1.0;

        double transferAngle = orientation > 0.0 ? unsignedAngle : TwoPi - unsignedAngle;
        if (longWay == (transferAngle < System.Math.PI))
            transferAngle = TwoPi - transferAngle;

        double sinAngle = System.Math.Sin(transferAngle);
        double oneMinusCos = 1.0 - cosAngle;
        if (System.Math.Abs(sinAngle) < 1e-12 || oneMinusCos < 1e-14)
            throw new ArgumentException("Lambert geometry is singular for collinear boundary positions.");

        double a = sinAngle * System.Math.Sqrt(r1 * r2 / oneMinusCos);
        if (System.Math.Abs(a) < 1e-9)
            throw new ArgumentException("Lambert geometry produced a degenerate transfer parameter.");

        double sqrtMuTime = System.Math.Sqrt(centralGm) * timeOfFlight;

        double TimeResidual(double z)
        {
            (double c, double s) = Stumpff(z);
            if (c <= 0.0 || !double.IsFinite(c) || !double.IsFinite(s))
                return double.NaN;

            double y = r1 + r2 + a * (z * s - 1.0) / System.Math.Sqrt(c);
            if (y < 0.0)
                return double.NaN;

            double x = System.Math.Sqrt(y / c);
            return x * x * x * s + a * System.Math.Sqrt(y) - sqrtMuTime;
        }

        // The physical zero-revolution root is monotonic over its valid branch. Scan a
        // broad universal-anomaly range first, then bisect the first finite sign change.
        // Sampling instead of assuming z=0 brackets both fast hyperbolic and slow
        // elliptic transfers without a fragile Newton starter.
        // 1024 samples bracket the single physical branch comfortably; the subsequent
        // bisection provides the precision. The former 8192-point scan made interactive
        // lunar-window searches spend most of their time evaluating already-bracketed
        // residuals without improving the terminal state.
        const int samples = 1024;
        double zMin = -4.0 * System.Math.PI * System.Math.PI;
        double zMax =  4.0 * System.Math.PI * System.Math.PI;
        double previousZ = zMin;
        double previousF = TimeResidual(previousZ);
        double lower = double.NaN;
        double upper = double.NaN;

        for (int i = 1; i <= samples; i++)
        {
            double z = zMin + (zMax - zMin) * i / samples;
            double f = TimeResidual(z);

            if (double.IsFinite(f))
            {
                if (System.Math.Abs(f) < 1e-8)
                {
                    lower = upper = z;
                    break;
                }

                if (double.IsFinite(previousF) && System.Math.Sign(f) != System.Math.Sign(previousF))
                {
                    lower = previousZ;
                    upper = z;
                    break;
                }
            }

            previousZ = z;
            previousF = f;
        }

        if (!double.IsFinite(lower))
            throw new InvalidOperationException("No zero-revolution Lambert solution exists for the requested boundary conditions.");

        double root = lower;
        if (lower != upper)
        {
            double fLower = TimeResidual(lower);
            for (int i = 0; i < 100; i++)
            {
                root = 0.5 * (lower + upper);
                double fMid = TimeResidual(root);
                if (!double.IsFinite(fMid))
                {
                    lower = root;
                    continue;
                }

                if (System.Math.Abs(fMid) < 1e-8 || upper - lower < 1e-12)
                    break;

                if (System.Math.Sign(fMid) == System.Math.Sign(fLower))
                {
                    lower = root;
                    fLower = fMid;
                }
                else
                {
                    upper = root;
                }
            }
        }

        (double cRoot, double sRoot) = Stumpff(root);
        double yRoot = r1 + r2 + a * (root * sRoot - 1.0) / System.Math.Sqrt(cRoot);
        double fLagrange = 1.0 - yRoot / r1;
        double gLagrange = a * System.Math.Sqrt(yRoot / centralGm);
        double gDot = 1.0 - yRoot / r2;

        if (!double.IsFinite(gLagrange) || System.Math.Abs(gLagrange) < 1e-12)
            throw new InvalidOperationException("Lambert solution has a singular Lagrange g coefficient.");

        Vector3d departureVelocity =
            (arrivalPosition - departurePosition * fLagrange) / gLagrange;
        Vector3d arrivalVelocity =
            (arrivalPosition * gDot - departurePosition) / gLagrange;

        if (!IsFinite(departureVelocity) || !IsFinite(arrivalVelocity))
            throw new InvalidOperationException("Lambert solution produced a non-finite state.");

        return new LambertSolution(
            departureVelocity,
            arrivalVelocity,
            transferAngle,
            timeOfFlight);
    }

    private static (double c, double s) Stumpff(double z)
    {
        if (z > 1e-8)
        {
            double root = System.Math.Sqrt(z);
            return (
                (1.0 - System.Math.Cos(root)) / z,
                (root - System.Math.Sin(root)) / (root * root * root));
        }

        if (z < -1e-8)
        {
            double root = System.Math.Sqrt(-z);
            return (
                (System.Math.Cosh(root) - 1.0) / (-z),
                (System.Math.Sinh(root) - root) / (root * root * root));
        }

        // Series around z=0 avoids cancellation in the trigonometric definitions.
        double z2 = z * z;
        double z3 = z2 * z;
        return (
            0.5 - z / 24.0 + z2 / 720.0 - z3 / 40_320.0,
            1.0 / 6.0 - z / 120.0 + z2 / 5_040.0 - z3 / 362_880.0);
    }

    private static bool IsFinite(Vector3d value) =>
        double.IsFinite(value.X) &&
        double.IsFinite(value.Y) &&
        double.IsFinite(value.Z);
}
