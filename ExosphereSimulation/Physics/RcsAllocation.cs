namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

/// <summary>One reaction-control thruster placed in the vessel frame (skirt-datum origin).</summary>
public readonly record struct RcsThrusterGeometry(
    Part Owner,
    Vector3d PositionLocal,
    Vector3d DirectionLocal,
    double ThrustN,
    double IspS)
{
    public static RcsThrusterGeometry From(Part owner, Vector3d partCentre, RcsThrusterDef thruster)
    {
        var position = thruster.Position.Length == 3
            ? new Vector3d(thruster.Position[0], thruster.Position[1], thruster.Position[2])
            : Vector3d.Zero;
        var direction = thruster.Direction.Length == 3
            ? new Vector3d(thruster.Direction[0], thruster.Direction[1], thruster.Direction[2])
            : Vector3d.Up;
        if (direction.MagnitudeSquared < 1e-12) direction = Vector3d.Up;
        return new RcsThrusterGeometry(owner, partCentre + position, direction.Normalized,
            System.Math.Max(0.0, thruster.ThrustN), System.Math.Max(0.0, thruster.IspS));
    }

    /// <summary>
    /// Firing: the owning part is intact, its RCS is enabled and its own store (when it has
    /// one) is not empty.
    /// </summary>
    public bool Available => !Owner.IsBroken && Owner.RcsAttitudeEnabled && ThrustN > 0.0
        && (Owner.Definition.FuelCapacityMono <= 0.0 || Owner.Monopropellant > 1e-9);

    /// <summary>Force on the vehicle at full firing (body frame, N).</summary>
    public Vector3d Force => DirectionLocal * ThrustN;

    /// <summary>Moment about <paramref name="centerOfMassLocal"/> at full firing (body frame, N·m).</summary>
    public Vector3d Torque(Vector3d centerOfMassLocal) => (PositionLocal - centerOfMassLocal).Cross(Force);

    /// <summary>Propellant flow at full firing (kg/s).</summary>
    public double MassFlowKgPerS => IspS > 0.0 ? ThrustN / (IspS * StandardGravity) : 0.0;

    public const double StandardGravity = 9.80665;
}

/// <summary>Result of one RCS allocation: what the thrusters actually deliver.</summary>
public readonly record struct RcsAllocationResult(Vector3d TorqueBody, Vector3d ForceBody, double MassFlowKgPerS);

/// <summary>
/// Reaction-control allocation: each thruster fires a fraction u ∈ [0, 1] of its thrust (the
/// pulse-width duty over a control step) so that Σ u·(r×F) and Σ u·F best match the requested
/// moment and force — a redistributed pseudo-inverse (minimum-norm duties, clamping and
/// re-solving) polished by projected coordinate descent on the bounded least-squares problem,
/// rows normalised by each channel's scale and a small duty penalty favouring less propellant.
/// Torque and force follow from each thruster's declared position, direction and thrust only.
/// </summary>
public static class RcsAllocation
{
    private const int Sweeps = 200;
    private const double Regularisation = 1e-9;
    private const double DutyPenalty = 1e-6;

    /// <summary>
    /// Moment the thrusters can deliver about each body axis in both senses (N·m): per axis,
    /// the smaller of the positive and negative sums of single-thruster moments.
    /// </summary>
    /// <summary>
    /// Force the thrusters can deliver along each body axis in both senses (N): per axis, the
    /// smaller of the positive and negative sums of single-thruster force components.
    /// </summary>
    public static Vector3d TranslationAuthority(IReadOnlyList<RcsThrusterGeometry> thrusters)
    {
        double px = 0, nx = 0, py = 0, ny = 0, pz = 0, nz = 0;
        for (int i = 0; i < thrusters.Count; i++)
        {
            var thruster = thrusters[i];
            if (!thruster.Available) continue;
            var f = thruster.Force;
            if (f.X > 0) px += f.X; else nx -= f.X;
            if (f.Y > 0) py += f.Y; else ny -= f.Y;
            if (f.Z > 0) pz += f.Z; else nz -= f.Z;
        }
        return new Vector3d(System.Math.Min(px, nx), System.Math.Min(py, ny), System.Math.Min(pz, nz));
    }

    /// <summary>
    /// Moment the thrusters can deliver about each body axis in both senses (N·m): per axis,
    /// the smaller of the positive and negative moments an attitude-only allocation reaches
    /// with no moment on the other axes.
    /// </summary>
    public static Vector3d Authority(IReadOnlyList<RcsThrusterGeometry> thrusters, Vector3d centerOfMassLocal) =>
        Authority(thrusters, centerOfMassLocal, new Vector3d(1.0, 1.0, 1.0));

    public static Vector3d Authority(IReadOnlyList<RcsThrusterGeometry> thrusters, Vector3d centerOfMassLocal,
        Vector3d axisWeights)
    {
        int n = thrusters.Count;
        if (n == 0) return Vector3d.Zero;
        double reach = 0.0;
        for (int i = 0; i < n; i++)
            if (thrusters[i].Available)
                reach += thrusters[i].Torque(centerOfMassLocal).Magnitude;
        if (reach <= 0.0) return Vector3d.Zero;
        Span<double> duty = n <= 64 ? stackalloc double[n] : new double[n];
        Span<double> axisAuthority = stackalloc double[3];
        for (int axis = 0; axis < 3; axis++)
        {
            double smallest = double.PositiveInfinity;
            for (int senseIndex = 0; senseIndex < 2; senseIndex++)
            {
                double sense = senseIndex == 0 ? 1.0 : -1.0;
                var request = new Vector3d(axis == 0 ? sense : 0.0, axis == 1 ? sense : 0.0, axis == 2 ? sense : 0.0)
                    * (2.0 * reach);
                var delivered = Allocate(thrusters, centerOfMassLocal, request, Vector3d.Zero, duty, axisWeights,
                    AttitudeOnlyForceWeight).TorqueBody;
                double component = axis == 0 ? delivered.X : axis == 1 ? delivered.Y : delivered.Z;
                smallest = System.Math.Min(smallest, System.Math.Max(0.0, sense * component));
            }
            axisAuthority[axis] = smallest;
        }
        return new Vector3d(axisAuthority[0], axisAuthority[1], axisAuthority[2]);
    }

    public static RcsAllocationResult Allocate(IReadOnlyList<RcsThrusterGeometry> thrusters,
        Vector3d centerOfMassLocal, Vector3d desiredTorqueBody, Vector3d desiredForceBody, Span<double> duty) =>
        Allocate(thrusters, centerOfMassLocal, desiredTorqueBody, desiredForceBody, duty, new Vector3d(1.0, 1.0, 1.0));

    /// <summary>
    /// Weight of the force rows for an attitude-only request: couples are used wherever the
    /// thrusters offer them, and a system without couples (the Apollo CM's single-sided jets)
    /// still turns the vehicle, accepting the translation that real jet selection accepts.
    /// </summary>
    public const double AttitudeOnlyForceWeight = 1e-3;

    /// <param name="forceWeight">Relative weight of the force error against the moment error.</param>
    /// <param name="axisWeights">
    /// Per-axis weight on the moment error. Passing the inverse principal inertias (scaled to
    /// any common reference) makes the allocation minimise the angular-acceleration error, so a
    /// small roll inertia is not swamped by pitch moments that are large only in N·m.
    /// </param>
    public static RcsAllocationResult Allocate(IReadOnlyList<RcsThrusterGeometry> thrusters,
        Vector3d centerOfMassLocal, Vector3d desiredTorqueBody, Vector3d desiredForceBody, Span<double> duty,
        Vector3d axisWeights, double forceWeight = 1.0)
    {
        // Lexicographic: first the moment from couples alone (force held at its request);
        // only the moment they cannot give is then taken from jets that also translate.
        var result = Solve(thrusters, centerOfMassLocal, desiredTorqueBody, desiredForceBody, duty, axisWeights,
            1.0, warmStart: false);
        if (forceWeight >= 1.0) return result;
        // Couples alone already deliver the moment: nothing is left for translating jets.
        var shortfall = Weighted(result.TorqueBody - desiredTorqueBody, axisWeights);
        if (shortfall.MagnitudeSquared <= 1e-12 * Weighted(desiredTorqueBody, axisWeights).MagnitudeSquared)
            return result;
        return Solve(thrusters, centerOfMassLocal, desiredTorqueBody, desiredForceBody, duty, axisWeights,
            forceWeight, warmStart: true);
    }

    private static RcsAllocationResult Solve(IReadOnlyList<RcsThrusterGeometry> thrusters,
        Vector3d centerOfMassLocal, Vector3d desiredTorqueBody, Vector3d desiredForceBody, Span<double> duty,
        Vector3d axisWeights, double forceWeight, bool warmStart)
    {
        int n = thrusters.Count;
        if (n == 0) return default;
        if (!warmStart) duty.Clear();
        double torqueScale = 0.0, forceScale = 0.0;
        for (int i = 0; i < n; i++)
        {
            if (!thrusters[i].Available) continue;
            torqueScale = System.Math.Max(torqueScale, Weighted(thrusters[i].Torque(centerOfMassLocal), axisWeights).Magnitude);
            forceScale = System.Math.Max(forceScale, thrusters[i].ThrustN);
        }
        if (torqueScale <= 0.0 && forceScale <= 0.0) return default;
        // Moments are scaled no smaller than the largest thrust on a 1 m lever: a thruster
        // pack at the centre of mass carries only round-off moments, which must not
        // outweigh the force it is asked for.
        torqueScale = System.Math.Max(torqueScale, forceScale * 1.0);
        double wt = torqueScale > 0.0 ? 1.0 / torqueScale : 0.0;
        double wf = forceScale > 0.0 ? forceWeight / forceScale : 0.0;

        // Normalised columns of A (moment rows then force rows), computed once.
        Span<Vector3d> columnT = n <= 64 ? stackalloc Vector3d[n] : new Vector3d[n];
        Span<Vector3d> columnF = n <= 64 ? stackalloc Vector3d[n] : new Vector3d[n];
        Span<double> curvature = n <= 64 ? stackalloc double[n] : new double[n];
        for (int i = 0; i < n; i++)
        {
            if (!thrusters[i].Available) { curvature[i] = 0.0; continue; }
            columnT[i] = Weighted(thrusters[i].Torque(centerOfMassLocal), axisWeights) * wt;
            columnF[i] = thrusters[i].Force * wf;
            curvature[i] = columnT[i].MagnitudeSquared + columnF[i].MagnitudeSquared + DutyPenalty;
        }

        // Redistributed pseudo-inverse: the minimum-norm duties over the free thrusters,
        // clamping every one outside [0, 1] to its bound and solving again for the rest.
        Span<bool> free = n <= 64 ? stackalloc bool[n] : new bool[n];
        for (int i = 0; i < n; i++) free[i] = !warmStart && curvature[i] > 0.0;
        var targetT = Weighted(desiredTorqueBody, axisWeights) * wt;
        var targetF = desiredForceBody * wf;
        Span<double> normal = stackalloc double[36];
        Span<double> rhs = stackalloc double[6];
        for (int pass = 0; pass < (warmStart ? 0 : n); pass++)
        {
            var remainingT = targetT;
            var remainingF = targetF;
            for (int i = 0; i < n; i++)
            {
                if (free[i] || duty[i] == 0.0) continue;
                remainingT -= columnT[i] * duty[i];
                remainingF -= columnF[i] * duty[i];
            }
            normal.Clear();
            for (int i = 0; i < n; i++)
            {
                if (!free[i]) continue;
                Accumulate(normal, columnT[i], columnF[i]);
            }
            for (int k = 0; k < 6; k++) normal[k * 6 + k] += Regularisation;
            rhs[0] = remainingT.X; rhs[1] = remainingT.Y; rhs[2] = remainingT.Z;
            rhs[3] = remainingF.X; rhs[4] = remainingF.Y; rhs[5] = remainingF.Z;
            Solve6(normal, rhs);
            var yT = new Vector3d(rhs[0], rhs[1], rhs[2]);
            var yF = new Vector3d(rhs[3], rhs[4], rhs[5]);
            bool clamped = false;
            for (int i = 0; i < n; i++)
            {
                if (!free[i]) continue;
                double u = columnT[i].Dot(yT) + columnF[i].Dot(yF);
                if (u < 0.0) { duty[i] = 0.0; free[i] = false; clamped = true; }
                else if (u > 1.0) { duty[i] = 1.0; free[i] = false; clamped = true; }
                else duty[i] = u;
            }
            if (!clamped) break;
        }

        // Polish with projected coordinate descent on the full bounded problem (residual
        // r = W(A u − b) in normalised units, a small duty penalty favouring less propellant).
        var rt = -targetT;
        var rf = -targetF;
        for (int i = 0; i < n; i++)
        {
            rt += columnT[i] * duty[i];
            rf += columnF[i] * duty[i];
        }
        for (int sweep = 0; sweep < Sweeps; sweep++)
        {
            double change = 0.0;
            for (int i = 0; i < n; i++)
            {
                if (curvature[i] == 0.0) continue;
                var at = columnT[i];
                var af = columnF[i];
                double g = at.Dot(rt) + af.Dot(rf) + DutyPenalty * duty[i];
                double next = System.Math.Clamp(duty[i] - g / curvature[i], 0.0, 1.0);
                double delta = next - duty[i];
                if (delta == 0.0) continue;
                duty[i] = next;
                rt += at * delta;
                rf += af * delta;
                change = System.Math.Max(change, System.Math.Abs(delta));
            }
            if (change < 1e-9) break;
        }

        var torque = Vector3d.Zero;
        var force = Vector3d.Zero;
        double flow = 0.0;
        for (int i = 0; i < n; i++)
        {
            if (duty[i] <= 0.0) continue;
            var thruster = thrusters[i];
            torque += thruster.Torque(centerOfMassLocal) * duty[i];
            force += thruster.Force * duty[i];
            flow += thruster.MassFlowKgPerS * duty[i];
        }
        return new RcsAllocationResult(torque, force, flow);
    }

    private static Vector3d Weighted(Vector3d v, Vector3d w) => new(v.X * w.X, v.Y * w.Y, v.Z * w.Z);

    private static void Accumulate(Span<double> normal, Vector3d t, Vector3d f)
    {
        Span<double> c = stackalloc double[6] { t.X, t.Y, t.Z, f.X, f.Y, f.Z };
        for (int r = 0; r < 6; r++)
        {
            if (c[r] == 0.0) continue;
            for (int k = 0; k < 6; k++) normal[r * 6 + k] += c[r] * c[k];
        }
    }

    /// <summary>Solves the 6×6 system in place (Gaussian elimination, partial pivoting).</summary>
    private static void Solve6(Span<double> a, Span<double> b)
    {
        for (int col = 0; col < 6; col++)
        {
            int pivot = col;
            for (int r = col + 1; r < 6; r++)
                if (System.Math.Abs(a[r * 6 + col]) > System.Math.Abs(a[pivot * 6 + col])) pivot = r;
            if (pivot != col)
            {
                for (int k = 0; k < 6; k++) (a[col * 6 + k], a[pivot * 6 + k]) = (a[pivot * 6 + k], a[col * 6 + k]);
                (b[col], b[pivot]) = (b[pivot], b[col]);
            }
            double diagonal = a[col * 6 + col];
            if (System.Math.Abs(diagonal) < 1e-300) continue;
            for (int r = col + 1; r < 6; r++)
            {
                double factor = a[r * 6 + col] / diagonal;
                if (factor == 0.0) continue;
                for (int k = col; k < 6; k++) a[r * 6 + k] -= factor * a[col * 6 + k];
                b[r] -= factor * b[col];
            }
        }
        for (int row = 5; row >= 0; row--)
        {
            double sum = b[row];
            for (int k = row + 1; k < 6; k++) sum -= a[row * 6 + k] * b[k];
            double diagonal = a[row * 6 + row];
            b[row] = System.Math.Abs(diagonal) < 1e-300 ? 0.0 : sum / diagonal;
        }
    }
}
