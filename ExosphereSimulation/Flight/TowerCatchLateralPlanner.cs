namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;

/// <summary>
/// Identified response of the ship's side acceleration to a commanded side acceleration,
/// through the attitude loop, the gimbals and the thrust:
/// <c>a/u = K(1 − T_z s) / (T² s² + 2ζ T s + 1)</c>. The right-half-plane zero is the
/// thrust-vector-control reversal: to tilt the ship toward the command the gimbals first push
/// the tail the other way, so the side acceleration starts opposite to the command. It caps
/// the bandwidth of any feedback law near <c>1/(2T_z)</c>.
/// </summary>
public readonly record struct LateralPlantModel(double Gain, double ZeroTimeS, double PeriodS, double Damping)
{
    public bool IsValid => Gain > 0.0 && ZeroTimeS >= 0.0 && PeriodS > 0.0 && Damping > 0.0
        && double.IsFinite(Gain) && double.IsFinite(ZeroTimeS) && double.IsFinite(PeriodS) && double.IsFinite(Damping);
}

/// <summary>
/// Receding-horizon planner for the horizontal channel of the catch burn. Each step it finds
/// the smoothest piecewise-constant command sequence that, through the identified plant
/// (<see cref="LateralPlantModel"/>), brings the horizontal offset, velocity and the plant's
/// own state to zero at the end of the horizon, and returns the first command. The plant is
/// linear and the two horizontal axes are alike, so the first command is a fixed linear
/// combination of offset, velocity and plant state for a given horizon; the planner solves
/// for those four gains. The plant state is carried by an internal model driven by the
/// command actually applied.
/// </summary>
public sealed class TowerCatchLateralPlanner
{
    private const int Knots = 20;
    private const int SubstepsPerKnot = 8;
    private readonly LateralPlantModel _model;
    private readonly double _smoothingS2;
    private Vector3d _z1, _z2;

    /// <param name="smoothingS2">Weight of command changes against command size (s²).</param>
    public TowerCatchLateralPlanner(LateralPlantModel model, double smoothingS2 = 1.0)
    {
        if (!model.IsValid) throw new ArgumentException("Invalid lateral plant model.", nameof(model));
        if (!(smoothingS2 >= 0.0)) throw new ArgumentOutOfRangeException(nameof(smoothingS2));
        _model = model;
        _smoothingS2 = smoothingS2;
    }

    public LateralPlantModel Model => _model;

    /// <summary>The same planner state over another plant, after the lit engines change.</summary>
    public TowerCatchLateralPlanner WithModel(LateralPlantModel model) =>
        new(model, _smoothingS2) { _z1 = _z1, _z2 = _z2 };

    /// <summary>Side acceleration the internal model expects now (m/s²).</summary>
    public Vector3d ModelAcceleration => Output(_z1, _z2);

    /// <summary>Command (m/s²) that nulls offset and velocity in <paramref name="horizonS"/>.</summary>
    public Vector3d Command(Vector3d offsetM, Vector3d velocityMps, double horizonS)
    {
        if (!(horizonS > 0.0) || !double.IsFinite(horizonS)) throw new ArgumentOutOfRangeException(nameof(horizonS));
        var gains = FirstCommandGains(_model, horizonS, _smoothingS2);
        return -(offsetM * gains[0] + velocityMps * gains[1] + _z1 * gains[2] + _z2 * gains[3]);
    }

    /// <summary>
    /// Starts the internal model at a steady side acceleration, as when the burn takes over a
    /// ship that the flip left already tilted.
    /// </summary>
    public void Initialize(Vector3d measuredAccelerationMps2)
    {
        double w = 1.0 / _model.PeriodS;
        _z1 = measuredAccelerationMps2 / (_model.Gain * w * w);
        _z2 = Vector3d.Zero;
    }

    /// <summary>
    /// Pulls the internal model toward the measured side acceleration with time constant
    /// <paramref name="timeConstantS"/>, so aerodynamic and off-axis forces the model does not
    /// carry, and the model's own error, do not accumulate between plans.
    /// </summary>
    public void Observe(Vector3d measuredAccelerationMps2, double dt, double timeConstantS)
    {
        if (!(dt > 0.0) || !(timeConstantS > 0.0)) throw new ArgumentOutOfRangeException(nameof(dt));
        double w = 1.0 / _model.PeriodS;
        var error = measuredAccelerationMps2 - ModelAcceleration;
        _z1 += error / (_model.Gain * w * w) * System.Math.Min(1.0, dt / timeConstantS);
    }

    /// <summary>Carries the internal model over <paramref name="dt"/> with the command applied.</summary>
    public void Advance(Vector3d appliedCommand, double dt)
    {
        if (!(dt > 0.0)) throw new ArgumentOutOfRangeException(nameof(dt));
        double w = 1.0 / _model.PeriodS;
        // The plant poles are slower than the control step; split long steps for accuracy.
        int n = System.Math.Max(1, (int)System.Math.Ceiling(dt / 0.02));
        double h = dt / n;
        for (int i = 0; i < n; i++)
        {
            var (z1, z2) = (_z1, _z2);
            Vector3d D1(Vector3d a, Vector3d b) => b;
            Vector3d D2(Vector3d a, Vector3d b) => appliedCommand - a * (w * w) - b * (2.0 * _model.Damping * w);
            var k1a = D1(z1, z2); var k1b = D2(z1, z2);
            var k2a = D1(z1 + k1a * (h / 2), z2 + k1b * (h / 2)); var k2b = D2(z1 + k1a * (h / 2), z2 + k1b * (h / 2));
            var k3a = D1(z1 + k2a * (h / 2), z2 + k2b * (h / 2)); var k3b = D2(z1 + k2a * (h / 2), z2 + k2b * (h / 2));
            var k4a = D1(z1 + k3a * h, z2 + k3b * h); var k4b = D2(z1 + k3a * h, z2 + k3b * h);
            _z1 = z1 + (k1a + k2a * 2.0 + k3a * 2.0 + k4a) * (h / 6.0);
            _z2 = z2 + (k1b + k2b * 2.0 + k3b * 2.0 + k4b) * (h / 6.0);
        }
    }

    private Vector3d Output(Vector3d z1, Vector3d z2)
    {
        double w = 1.0 / _model.PeriodS;
        return (z1 - z2 * _model.ZeroTimeS) * (_model.Gain * w * w);
    }

    /// <summary>
    /// Gains <c>k</c> such that the first command of the plan is <c>−k·[r, v, z₁, z₂]</c>.
    /// The terminal state is <c>Φx₀ + Γu</c>; the plan minimises <c>uᵀHu</c> with
    /// <c>H = Δt·I + (smoothing/Δt)·DᵀD</c> (size plus changes) subject to a zero terminal
    /// state, so <c>u = −H⁻¹Γᵀ(ΓH⁻¹Γᵀ)⁻¹Φx₀</c>.
    /// </summary>
    public static double[] FirstCommandGains(LateralPlantModel model, double horizonS, double smoothingS2)
    {
        double dt = horizonS / Knots;
        var phi = new double[4, 4];
        for (int j = 0; j < 4; j++)
        {
            var x = new double[4]; x[j] = 1.0;
            var end = Propagate(model, x, horizonS, _ => 0.0);
            for (int i = 0; i < 4; i++) phi[i, j] = end[i];
        }
        var gamma = new double[4, Knots];
        for (int k = 0; k < Knots; k++)
        {
            int knot = k;
            var end = Propagate(model, new double[4], horizonS, t => System.Math.Min(Knots - 1, (int)(t / dt)) == knot ? 1.0 : 0.0);
            for (int i = 0; i < 4; i++) gamma[i, k] = end[i];
        }
        var h = new double[Knots, Knots];
        for (int k = 0; k < Knots; k++)
        {
            h[k, k] += dt;
            if (k > 0)
            {
                double s = smoothingS2 / dt;
                h[k, k] += s; h[k - 1, k - 1] += s; h[k, k - 1] -= s; h[k - 1, k] -= s;
            }
        }
        // Y = H⁻¹Γᵀ (Knots×4), M = ΓY (4×4).
        var gammaT = new double[Knots, 4];
        for (int k = 0; k < Knots; k++) for (int i = 0; i < 4; i++) gammaT[k, i] = gamma[i, k];
        var y = Solve(h, gammaT);
        var m = new double[4, 4];
        for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++)
        {
            double sum = 0.0;
            for (int k = 0; k < Knots; k++) sum += gamma[i, k] * y[k, j];
            m[i, j] = sum;
        }
        var mInvPhi = Solve(m, phi);
        var gains = new double[4];
        for (int j = 0; j < 4; j++)
        {
            double sum = 0.0;
            for (int i = 0; i < 4; i++) sum += y[0, i] * mInvPhi[i, j];
            gains[j] = sum;
        }
        return gains;
    }

    // State [r, v, z1, z2]: r' = v, v' = a, z1' = z2, z2' = u − w²z1 − 2ζw z2, a = K w²(z1 − T_z z2).
    private static double[] Propagate(LateralPlantModel model, double[] x0, double duration, Func<double, double> input)
    {
        double w = 1.0 / model.PeriodS;
        int steps = Knots * SubstepsPerKnot;
        double h = duration / steps;
        var x = (double[])x0.Clone();
        double[] F(double[] s, double u) =>
        [
            s[1],
            model.Gain * w * w * (s[2] - model.ZeroTimeS * s[3]),
            s[3],
            u - w * w * s[2] - 2.0 * model.Damping * w * s[3],
        ];
        for (int n = 0; n < steps; n++)
        {
            // The command is held over each substep, sampled at its middle.
            double u = input((n + 0.5) * h);
            var k1 = F(x, u);
            var k2 = F(Add(x, k1, h / 2), u);
            var k3 = F(Add(x, k2, h / 2), u);
            var k4 = F(Add(x, k3, h), u);
            for (int i = 0; i < 4; i++) x[i] += h / 6.0 * (k1[i] + 2 * k2[i] + 2 * k3[i] + k4[i]);
        }
        return x;
    }

    private static double[] Add(double[] x, double[] d, double s)
    {
        var r = new double[x.Length];
        for (int i = 0; i < x.Length; i++) r[i] = x[i] + d[i] * s;
        return r;
    }

    /// <summary>Solves <c>A X = B</c> by Gaussian elimination with partial pivoting.</summary>
    private static double[,] Solve(double[,] a, double[,] b)
    {
        int n = a.GetLength(0), m = b.GetLength(1);
        var aa = (double[,])a.Clone();
        var bb = (double[,])b.Clone();
        for (int c = 0; c < n; c++)
        {
            int p = c;
            for (int r = c + 1; r < n; r++) if (System.Math.Abs(aa[r, c]) > System.Math.Abs(aa[p, c])) p = r;
            if (System.Math.Abs(aa[p, c]) < 1e-300) throw new InvalidOperationException("Singular lateral plan.");
            if (p != c)
            {
                for (int k = 0; k < n; k++) (aa[c, k], aa[p, k]) = (aa[p, k], aa[c, k]);
                for (int k = 0; k < m; k++) (bb[c, k], bb[p, k]) = (bb[p, k], bb[c, k]);
            }
            for (int r = c + 1; r < n; r++)
            {
                double f = aa[r, c] / aa[c, c];
                if (f == 0.0) continue;
                for (int k = c; k < n; k++) aa[r, k] -= f * aa[c, k];
                for (int k = 0; k < m; k++) bb[r, k] -= f * bb[c, k];
            }
        }
        var x = new double[n, m];
        for (int r = n - 1; r >= 0; r--)
            for (int k = 0; k < m; k++)
            {
                double sum = bb[r, k];
                for (int c = r + 1; c < n; c++) sum -= aa[r, c] * x[c, k];
                x[r, k] = sum / aa[r, r];
            }
        return x;
    }
}
