namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;

internal readonly record struct SloshKineticMode(double MassKg, Vector3d PositionBodyM,
    Vector3d TangentX, Vector3d TangentZ);

/// <summary>
/// Symmetric kinetic solve shared by fixed-fill and funded parameter schedules. A zero-mass
/// endpoint is recovered through its acceleration-per-unit-mass limit, without adding
/// fictitious mass or regularizing the body's inertia.
/// </summary>
internal static class SloshKineticSystem
{
    internal static Slosh6DofAcceleration Solve(RigidBodyMassProperties mass,
        IReadOnlyList<SloshKineticMode> modes, IReadOnlyList<SloshModalAcceleration> modalRightPerMass,
        Vector3d forceRightBody, Vector3d torqueRightDatumBody, Quaterniond orientation)
    {
        if (modalRightPerMass.Count != modes.Count) throw new ArgumentException("Modal load count must match.");
        var map = new int[modes.Count];
        int count = 0;
        double movingMass = 0;
        for (int i = 0; i < modes.Count; i++)
        {
            if (modes[i].MassKg < 0 || !double.IsFinite(modes[i].MassKg))
                throw new ArgumentException("Modal mass must be finite and nonnegative.");
            map[i] = modes[i].MassKg > 0 ? count++ : -1;
            movingMass += modes[i].MassKg;
        }
        if (!(mass.Mass > movingMass)) throw new InvalidOperationException("A positive carrier mass is required.");
        int n = 6 + 2 * count;
        var matrix = new double[n, n]; var rhs = new double[n];
        var s = mass.CenterOfMassBody * mass.Mass;
        var inertia = Slosh6DofDynamics.Add(mass.InertiaBody, Slosh6DofDynamics.ParallelAxis(mass.CenterOfMassBody, mass.Mass));
        Vector3d[] axes = [new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)];
        for (int i = 0; i < 3; i++)
        {
            matrix[i, i] = mass.Mass;
            var column = axes[i].Cross(s); var inertiaColumn = inertia.Multiply(axes[i]);
            rhs[i] = Component(forceRightBody, i); rhs[i + 3] = Component(torqueRightDatumBody, i);
            for (int j = 0; j < 3; j++)
            {
                matrix[j, i + 3] = matrix[i + 3, j] = Component(column, j);
                matrix[j + 3, i + 3] = Component(inertiaColumn, j);
            }
        }
        for (int i = 0; i < modes.Count; i++)
        {
            if (map[i] < 0) continue;
            var mode = modes[i]; int start = 6 + 2 * map[i];
            for (int a = 0; a < 2; a++)
            {
                int column = start + a; var tangent = a == 0 ? mode.TangentX : mode.TangentZ;
                var moment = mode.PositionBodyM.Cross(tangent) * mode.MassKg;
                for (int j = 0; j < 3; j++)
                {
                    matrix[j, column] = matrix[column, j] = mode.MassKg * Component(tangent, j);
                    matrix[j + 3, column] = matrix[column, j + 3] = Component(moment, j);
                }
                matrix[column, start] = mode.MassKg * tangent.Dot(mode.TangentX);
                matrix[column, start + 1] = mode.MassKg * tangent.Dot(mode.TangentZ);
                rhs[column] = mode.MassKg * (a == 0 ? modalRightPerMass[i].X : modalRightPerMass[i].Z);
            }
        }
        var solution = SolvePositiveDefinite(matrix, rhs);
        var acceleration = new Vector3d(solution[0], solution[1], solution[2]);
        var angular = new Vector3d(solution[3], solution[4], solution[5]);
        var modal = new SloshModalAcceleration[modes.Count];
        for (int i = 0; i < modes.Count; i++)
        {
            if (map[i] >= 0)
            {
                int j = 6 + 2 * map[i]; modal[i] = new(solution[j], solution[j + 1]);
                continue;
            }
            var mode = modes[i];
            var inertial = acceleration + angular.Cross(mode.PositionBodyM);
            var metric = new double[,] { { mode.TangentX.Dot(mode.TangentX), mode.TangentX.Dot(mode.TangentZ) },
                { mode.TangentZ.Dot(mode.TangentX), mode.TangentZ.Dot(mode.TangentZ) } };
            var limit = SolvePositiveDefinite(metric,
                [modalRightPerMass[i].X - mode.TangentX.Dot(inertial), modalRightPerMass[i].Z - mode.TangentZ.Dot(inertial)]);
            modal[i] = new(limit[0], limit[1]);
        }
        return new(orientation.Rotate(acceleration), angular, modal);
    }

    private static double Component(Vector3d v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;
    private static double[] SolvePositiveDefinite(double[,] a, double[] b)
    {
        int n = b.Length;
        var scale = new double[n]; var l = new double[n, n]; var y = new double[n]; var x = new double[n];
        double budget = Slosh6DofDynamics.NormalizedPivotBudgetPerDimension * n;
        for (int i = 0; i < n; i++)
        {
            if (!(a[i, i] > 0) || !double.IsFinite(a[i, i]))
                throw new InvalidOperationException("The coupled kinetic matrix is not positive definite.");
            scale[i] = System.Math.Sqrt(a[i, i]);
        }
        for (int i = 0; i < n; i++)
        {
            if (!double.IsFinite(b[i])) throw new ArgumentException("Generalized load must be finite.");
            for (int j = 0; j <= i; j++)
            {
                double lower = a[i, j] / scale[i] / scale[j], upper = a[j, i] / scale[i] / scale[j];
                if (!double.IsFinite(lower) || !double.IsFinite(upper) || System.Math.Abs(lower - upper) > budget)
                    throw new InvalidOperationException("The coupled kinetic matrix must be finite and symmetric.");
                double sum = lower * 0.5 + upper * 0.5;
                for (int k = 0; k < j; k++) sum -= l[i, k] * l[j, k];
                if (i == j)
                {
                    if (!(sum > budget)) throw new InvalidOperationException("The coupled kinetic matrix is indefinite or numerically singular.");
                    l[i, j] = System.Math.Sqrt(sum);
                }
                else l[i, j] = sum / l[j, j];
            }
            double right = b[i] / scale[i];
            for (int j = 0; j < i; j++) right -= l[i, j] * y[j];
            y[i] = right / l[i, i];
        }
        for (int i = n - 1; i >= 0; i--)
        {
            double right = y[i];
            for (int j = i + 1; j < n; j++) right -= l[j, i] * x[j];
            x[i] = right / l[i, i];
        }
        for (int i = 0; i < n; i++)
        {
            x[i] /= scale[i];
            if (!double.IsFinite(x[i])) throw new InvalidOperationException("Coupled acceleration is not representable.");
        }
        return x;
    }


}
