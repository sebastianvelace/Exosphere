namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Navigation;

/// <summary>
/// Shared maneuver-planning model. Holds a single maneuver node (true anomaly +
/// prograde/normal/radial ΔV) and exposes the perifocal geometry needed both to
/// draw the orbit/projection on the map and to execute the burn with the autopilot.
///
/// All vectors are relative to the reference body's centre, double precision.
/// The orbit is captured as a snapshot via <see cref="SetOrbit"/> each frame.
/// </summary>
public sealed class ManeuverPlanner
{
    // ── Orbit snapshot (relative to reference body) ───────────────────────────
    public bool     HasOrbit       { get; private set; }
    public double   Mu             { get; private set; }   // GM of reference body
    public Vector3d PeriapsisDir   { get; private set; }   // p̂ (toward periapsis)
    public Vector3d AheadDir       { get; private set; }   // q̂ (90° ahead, in plane)
    public Vector3d NormalDir      { get; private set; }   // ŵ (orbit normal)
    public double   Eccentricity   { get; private set; }
    public double   SemiMajorAxis  { get; private set; }   // m (negative for hyperbolic)
    public double   SemiLatusRectum{ get; private set; }   // p = h²/μ
    public double   TrueAnomalyNow { get; private set; }   // ν of the vessel right now (rad)

    // ── Maneuver node ─────────────────────────────────────────────────────────
    public bool   HasNode        { get; private set; }
    public double NodeTrueAnomaly{ get; set; }             // ν at which the burn occurs (rad)
    public double DvPrograde     { get; set; }             // m/s (+ prograde, − retro)
    public double DvNormal       { get; set; }             // m/s (+ along orbit normal)
    public double DvRadial       { get; set; }             // m/s (+ radial out)

    public double DeltaVMagnitude =>
        System.Math.Sqrt(DvPrograde * DvPrograde + DvNormal * DvNormal + DvRadial * DvRadial);

    /// <summary>Captures the current orbit from a state vector relative to the reference body.</summary>
    public void SetOrbit(Vector3d relPos, Vector3d relVel, double mu)
    {
        double r = relPos.Magnitude;
        double v = relVel.Magnitude;
        if (r < 1e-6 || mu <= 0.0) { HasOrbit = false; return; }

        Vector3d h = relPos.Cross(relVel);
        double   hMag = h.Magnitude;
        if (hMag < 1e-6) { HasOrbit = false; return; }   // radial / degenerate

        Vector3d w = h / hMag;

        // Eccentricity vector
        double   rDotV = relPos.Dot(relVel);
        Vector3d eVec  = (relPos * (v * v - mu / r) - relVel * rDotV) / mu;
        double   e     = eVec.Magnitude;

        Vector3d pHat = e > 1e-8 ? eVec / e : relPos / r;
        Vector3d qHat = w.Cross(pHat);

        double p = hMag * hMag / mu;
        double energy = v * v * 0.5 - mu / r;
        double a = System.Math.Abs(e - 1.0) < 1e-9
                 ? double.PositiveInfinity
                 : -mu / (2.0 * energy);

        // Current true anomaly in the (p̂, q̂) basis
        double nu = System.Math.Atan2(relPos.Dot(qHat), relPos.Dot(pHat));
        if (nu < 0.0) nu += 2.0 * System.Math.PI;

        Mu              = mu;
        PeriapsisDir    = pHat;
        AheadDir        = qHat;
        NormalDir       = w;
        Eccentricity    = e;
        SemiMajorAxis   = a;
        SemiLatusRectum = p;
        TrueAnomalyNow  = nu;
        HasOrbit        = true;

        if (!HasNode)
        {
            // Default a fresh node a quarter-orbit ahead of the vessel
            NodeTrueAnomaly = nu;
        }
    }

    public void CreateNodeAt(double trueAnomaly)
    {
        HasNode = true;
        NodeTrueAnomaly = WrapTwoPi(trueAnomaly);
    }

    public void ClearNode()
    {
        HasNode = false;
        DvPrograde = DvNormal = DvRadial = 0.0;
    }

    /// <summary>
    /// Map preset: place a retrograde deorbit node that lowers periapsis into the
    /// atmosphere (default Pe altitude 80 km). Near-circular orbits (e &lt; 0.01) burn
    /// immediately at the current true anomaly; otherwise the node is at apoapsis (ν=π).
    /// Requires a prior <see cref="SetOrbit"/> snapshot. Returns false if no orbit.
    /// </summary>
    public bool PlanDeorbit(CelestialBody body, double targetPeAltitudeM = 80_000.0)
    {
        if (!HasOrbit || body == null) return false;

        // Circular LEO: burn now so the player does not wait half a period for apoapsis.
        // Eccentric: burn at apoapsis — that is where lowering Pe is cheapest.
        double nu = Eccentricity < 0.01 ? TrueAnomalyNow : System.Math.PI;
        CreateNodeAt(nu);

        double radiusAtNode = PositionAt(NodeTrueAnomaly).Magnitude;
        double targetPeRadius = body.Radius + targetPeAltitudeM;
        double atmoMax = body.Atmosphere != null
            ? body.Atmosphere.MaxAltitude
            : double.NaN;

        double dv = DeorbitPlanner.ComputeRetroDeltaV(
            Mu, radiusAtNode, targetPeRadius, body.Radius, atmoMax);

        DvPrograde = -dv;
        DvNormal   = 0.0;
        DvRadial   = 0.0;
        return dv > 0.0;
    }

    // ── Perifocal sampling for drawing ────────────────────────────────────────

    /// <summary>
    /// Returns the relative position (m) on the captured orbit at true anomaly ν.
    /// </summary>
    public Vector3d PositionAt(double nu)
    {
        double denom = 1.0 + Eccentricity * System.Math.Cos(nu);
        if (System.Math.Abs(denom) < 1e-9) denom = 1e-9;
        double rr = SemiLatusRectum / denom;
        return PeriapsisDir * (rr * System.Math.Cos(nu)) + AheadDir * (rr * System.Math.Sin(nu));
    }

    /// <summary>Inertial (relative) velocity vector on the captured orbit at ν.</summary>
    public Vector3d VelocityAt(double nu)
    {
        double k = System.Math.Sqrt(Mu / SemiLatusRectum);
        double vx = -k * System.Math.Sin(nu);
        double vy =  k * (Eccentricity + System.Math.Cos(nu));
        return PeriapsisDir * vx + AheadDir * vy;
    }

    /// <summary>
    /// Burn direction unit vectors at the node (live from the captured orbit).
    /// </summary>
    public (Vector3d prograde, Vector3d normal, Vector3d radial) BurnBasisAtNode()
    {
        Vector3d vel = VelocityAt(NodeTrueAnomaly);
        Vector3d pos = PositionAt(NodeTrueAnomaly);
        Vector3d pro = vel.Magnitude > 1e-9 ? vel.Normalized : AheadDir;
        Vector3d rad = pos.Magnitude > 1e-9 ? pos.Normalized : PeriapsisDir;
        return (pro, NormalDir, rad);
    }

    /// <summary>Inertial ΔV vector produced by the current node settings.</summary>
    public Vector3d DeltaVInertial()
    {
        var (pro, nrm, rad) = BurnBasisAtNode();
        return pro * DvPrograde + nrm * DvNormal + rad * DvRadial;
    }

    /// <summary>
    /// State vector (relative position + velocity) immediately after the burn.
    /// </summary>
    public (Vector3d pos, Vector3d vel) PostBurnState()
    {
        Vector3d pos = PositionAt(NodeTrueAnomaly);
        Vector3d vel = VelocityAt(NodeTrueAnomaly) + DeltaVInertial();
        return (pos, vel);
    }

    /// <summary>
    /// Estimated burn duration (s) given the vessel's full-throttle vacuum thrust.
    /// </summary>
    public double EstimateBurnTime(double thrustVac, double mass)
    {
        if (thrustVac <= 0.0 || mass <= 0.0) return 0.0;
        double accel = thrustVac / mass;
        return DeltaVMagnitude / accel;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public static double WrapTwoPi(double x)
    {
        x %= 2.0 * System.Math.PI;
        if (x < 0.0) x += 2.0 * System.Math.PI;
        return x;
    }

    /// <summary>True-anomaly limit for an open conic (e ≥ 1); 2π for closed orbits.</summary>
    public double TrueAnomalyLimit()
    {
        if (Eccentricity < 1.0) return System.Math.PI;        // half-sweep each side
        // Asymptote: cos ν∞ = −1/e
        double nuInf = System.Math.Acos(System.Math.Clamp(-1.0 / Eccentricity, -1.0, 1.0));
        return nuInf * 0.98;                                  // stay just inside asymptote
    }

    /// <summary>
    /// Time (s) from the vessel's current position forward to the maneuver node, for a
    /// closed orbit. Returns 0 for open conics (where "next pass" is not periodic) or when
    /// the orbit is undefined. Converts both true anomalies to mean anomaly and divides the
    /// forward angular gap by the mean motion n = √(μ/a³).
    /// Tiempo hasta el nodo: ν → M (vía E) y el hueco angular hacia adelante / movimiento medio.
    /// </summary>
    public double TimeToNode()
    {
        if (!HasOrbit || !HasNode || Eccentricity >= 1.0 || SemiMajorAxis <= 0.0)
            return 0.0;

        double n = System.Math.Sqrt(Mu / (SemiMajorAxis * SemiMajorAxis * SemiMajorAxis));
        if (n <= 0.0 || double.IsNaN(n)) return 0.0;

        double mNow  = MeanAnomalyFromTrue(TrueAnomalyNow, Eccentricity);
        double mNode = MeanAnomalyFromTrue(NodeTrueAnomaly, Eccentricity);
        double dM    = WrapTwoPi(mNode - mNow);   // forward to the next node pass
        return dM / n;
    }

    /// <summary>Mean anomaly (rad) from true anomaly for an elliptic orbit (e &lt; 1).</summary>
    private static double MeanAnomalyFromTrue(double nu, double e)
    {
        double tanHalfNu = System.Math.Tan(nu * 0.5);
        double E = 2.0 * System.Math.Atan(System.Math.Sqrt((1.0 - e) / (1.0 + e)) * tanHalfNu);
        double M = E - e * System.Math.Sin(E);
        return WrapTwoPi(M);
    }
}
