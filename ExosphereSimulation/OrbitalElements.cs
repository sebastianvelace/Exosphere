using Exosphere.Simulation.Math;

namespace Exosphere.Simulation;

public class OrbitalElements
{
    public double SemiMajorAxis              { get; set; }   // m
    public double Eccentricity               { get; set; }
    public double Inclination                { get; set; }   // radians
    public double LongitudeOfAscendingNode   { get; set; }   // radians
    public double ArgumentOfPeriapsis        { get; set; }   // radians
    public double MeanAnomalyAtEpoch         { get; set; }   // radians
    public double Epoch                      { get; set; }   // seconds
    public string ReferenceBodyId            { get; set; } = "";

    /// <summary>
    /// Specific angular momentum magnitude |h| = |r × v| (m²/s).
    /// Stored from <see cref="FromStateVector"/> so degenerate (near-radial) trajectories
    /// can be detected reliably — a conic section alone cannot distinguish a thin ellipse
    /// from a true radial fall once eccentricity rounds to ~1.
    /// |h| ≈ 0  ⇒  radial trajectory (straight up/down, the rocket goes to the body centre).
    /// </summary>
    public double SpecificAngularMomentum    { get; set; }

    /// <summary>
    /// True periapsis distance from the body centre (m), valid for ALL conic types.
    /// For a radial trajectory the periapsis is the body centre (0).
    /// Use this — not <c>SemiMajorAxis*(1-e)</c> directly — when deciding impacts:
    /// for hyperbolic orbits a &lt; 0 and e &gt; 1, so a*(1-e) is still correct, but
    /// radial cases need the explicit override below.
    /// </summary>
    public double PeriapsisRadius            { get; set; }

    // ── Classification ────────────────────────────────────────────────────────

    /// <summary>True when the orbit is open (escape / fly-by): e ≥ 1 or a ≤ 0.</summary>
    public bool IsHyperbolic => Eccentricity >= 1.0 || SemiMajorAxis <= 0.0;

    /// <summary>
    /// True when the trajectory is (nearly) radial: |h| ≈ 0. A radial state has no
    /// transverse motion, so the conic degenerates to a line through the body centre.
    /// These states must NOT be propagated as ordinary conics (the elliptic solver
    /// returns NaN, the hyperbolic solver diverges) — they always strike the body.
    /// </summary>
    public bool IsRadial { get; set; }

    // ── Computed properties ──────────────────────────────────────────────────

    /// <summary>
    /// Apoapsis distance from centre (m). Bound (elliptic) orbits only;
    /// open (hyperbolic/parabolic) and radial-escape trajectories return +∞.
    /// </summary>
    public double Apoapsis =>
        (IsHyperbolic || SemiMajorAxis <= 0.0)
            ? double.PositiveInfinity
            : SemiMajorAxis * (1.0 + Eccentricity);

    /// <summary>
    /// Periapsis distance from centre (m), valid for all conic types.
    /// Returns the stored <see cref="PeriapsisRadius"/> when available (set by
    /// <see cref="FromStateVector"/>); falls back to a*(1-e) for hand-built elements.
    /// </summary>
    public double Periapsis =>
        IsRadial      ? 0.0
        : _periapsisSet ? PeriapsisRadius
                        : SemiMajorAxis * (1.0 - Eccentricity);

    /// <summary>Semi-latus rectum (m). Stays positive for hyperbolic conics (a&lt;0, e&gt;1).</summary>
    public double SemiLatusRectum => SemiMajorAxis * (1.0 - Eccentricity * Eccentricity);

    /// <summary>
    /// True when the conic dips below <paramref name="bodyRadius"/> — i.e. the
    /// trajectory intersects the surface (suborbital lob or radial fall).
    /// </summary>
    public bool IsSuborbital(double bodyRadius) => IsRadial || Periapsis < bodyRadius;

    // Internal flag: distinguishes "PeriapsisRadius was explicitly computed (possibly 0)"
    // from "hand-built element with default 0". Set only by FromStateVector.
    private bool _periapsisSet;

    // ── Methods ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the mean anomaly (radians) at simulation time <paramref name="t"/> (seconds).
    /// </summary>
    public double GetMeanAnomaly(double t, double gm)
    {
        // Mean motion  n = √(GM / |a|³)  rad/s.
        // Para órbitas hiperbólicas (a < 0) se usa |a|; la propagación hiperbólica
        // completa vive en GetStateAtTime (Kepler hiperbólico), esto solo da el M lineal.
        double a3 = System.Math.Abs(SemiMajorAxis);
        a3 = a3 * a3 * a3;
        if (a3 <= 0.0) return MeanAnomalyAtEpoch;
        double n = System.Math.Sqrt(gm / a3);
        double M = MeanAnomalyAtEpoch + n * (t - Epoch);
        // Wrap to [0, 2π)
        M %= 2.0 * System.Math.PI;
        if (M < 0.0) M += 2.0 * System.Math.PI;
        return M;
    }

    /// <summary>
    /// Converts eccentric anomaly E to true anomaly ν (radians).
    /// </summary>
    public static double TrueAnomalyFromEccentric(double E, double e)
    {
        // Standard formula: tan(ν/2) = √((1+e)/(1-e)) · tan(E/2).
        // Guard e→1 (parabolic limit) to avoid division by zero / NaN.
        double denom = 1.0 - e;
        if (denom < 1e-12) denom = 1e-12;
        double halfE = E * 0.5;
        double tanHalfNu = System.Math.Sqrt((1.0 + e) / denom) * System.Math.Tan(halfE);
        double nu = 2.0 * System.Math.Atan(tanHalfNu);
        // Wrap to [0, 2π)
        if (nu < 0.0) nu += 2.0 * System.Math.PI;
        return nu;
    }

    /// <summary>
    /// Returns position and velocity in the inertial frame (relative to reference body)
    /// at time <paramref name="t"/>. Handles elliptic, hyperbolic and radial conics
    /// without producing NaN / frozen states.
    /// </summary>
    public (Vector3d position, Vector3d velocity) GetStateAtTime(double t, double gm)
    {
        // Radial trajectories have no well-defined orbital plane (h ≈ 0): the perifocal
        // construction below divides by p = a(1-e²) ≈ 0 and yields NaN. Such states are
        // always on a collision course with the body, so the caller (Universe) resolves
        // them via the impact guard before this is reached. Return a finite sentinel
        // (the body centre) rather than NaN if it is ever consumed directly.
        if (IsRadial)
        {
            return (Vector3d.Zero, Vector3d.Zero);
        }

        if (IsHyperbolic)
        {
            return GetHyperbolicStateAtTime(t, gm);
        }

        double M  = GetMeanAnomaly(t, gm);
        double E  = MathUtils.SolveKeplerEquation(M, Eccentricity);
        double nu = TrueAnomalyFromEccentric(E, Eccentricity);

        return MathUtils.OrbitalToInertialStateVector(
            SemiMajorAxis,
            Eccentricity,
            nu,
            Inclination,
            LongitudeOfAscendingNode,
            ArgumentOfPeriapsis,
            gm);
    }

    /// <summary>
    /// Hyperbolic propagation via the hyperbolic Kepler equation  M = e·sinh F − F.
    /// The elliptic mean-anomaly machinery does not apply for e &gt; 1 (it freezes the
    /// vessel at a wrong static point), so escape / interplanetary fly-by trajectories
    /// MUST use this path.
    /// </summary>
    private (Vector3d position, Vector3d velocity) GetHyperbolicStateAtTime(double t, double gm)
    {
        double e = Eccentricity;
        double a = SemiMajorAxis;        // a < 0 for a hyperbola
        if (a >= 0.0 || e <= 1.0)
        {
            // Parabolic / borderline (e == 1, a → ∞): fall back to perifocal at the stored
            // true-anomaly proxy (MeanAnomalyAtEpoch holds ν for these). Keeps it finite.
            return MathUtils.OrbitalToInertialStateVector(
                a, e, MeanAnomalyAtEpoch, Inclination,
                LongitudeOfAscendingNode, ArgumentOfPeriapsis, gm);
        }

        // Mean motion for the hyperbola: n = √(μ / (−a)³).
        double absA = -a;
        double n    = System.Math.Sqrt(gm / (absA * absA * absA));
        double M    = MeanAnomalyAtEpoch + n * (t - Epoch);   // M0 stored as hyperbolic mean anomaly

        double F = SolveHyperbolicKepler(M, e);

        // True anomaly from hyperbolic anomaly:
        // tan(ν/2) = √((e+1)/(e−1)) · tanh(F/2)
        double nu = 2.0 * System.Math.Atan2(
            System.Math.Sqrt(e + 1.0) * System.Math.Sinh(F * 0.5),
            System.Math.Sqrt(e - 1.0) * System.Math.Cosh(F * 0.5));

        return MathUtils.OrbitalToInertialStateVector(
            a, e, nu, Inclination,
            LongitudeOfAscendingNode, ArgumentOfPeriapsis, gm);
    }

    /// <summary>
    /// Solves the hyperbolic Kepler equation  M = e·sinh F − F  for the hyperbolic
    /// anomaly F using Newton-Raphson with a logarithmic starter (robust for large |M|).
    /// </summary>
    private static double SolveHyperbolicKepler(double M, double e)
    {
        // Logarithmic initial guess keeps Newton stable for large hyperbolic anomalies.
        double F = (System.Math.Abs(M) > 6.0)
            ? System.Math.Sign(M) * System.Math.Log(2.0 * System.Math.Abs(M) / e + 1.8)
            : M;

        for (int i = 0; i < 100; i++)
        {
            double f  = e * System.Math.Sinh(F) - F - M;
            double fp = e * System.Math.Cosh(F) - 1.0;
            double d  = f / fp;
            F -= d;
            if (System.Math.Abs(d) < 1e-12) break;
        }
        return F;
    }

    /// <summary>
    /// Computes Keplerian orbital elements from a Cartesian state vector.
    /// Uses the standard algorithm via specific angular momentum and eccentricity vector.
    /// </summary>
    /// <param name="pos">Position relative to reference body (m).</param>
    /// <param name="vel">Velocity relative to reference body (m/s).</param>
    /// <param name="gm">Gravitational parameter of reference body (m³/s²).</param>
    /// <param name="referenceBodyId">Id of the reference body.</param>
    /// <param name="epoch">Current simulation time (s) — used as epoch for M₀.</param>
    public static OrbitalElements FromStateVector(
        Vector3d pos,
        Vector3d vel,
        double   gm,
        string   referenceBodyId,
        double   epoch)
    {
        double r = pos.Magnitude;
        double v = vel.Magnitude;

        // Guard against a degenerate state (coincident with body centre, or GM≤0):
        // returning a trivial radial element set avoids NaN propagation downstream.
        if (r < 1e-9 || gm <= 0.0)
        {
            return new OrbitalElements
            {
                ReferenceBodyId = referenceBodyId,
                Epoch           = epoch,
                IsRadial        = true,
                PeriapsisRadius = 0.0,
                _periapsisSet   = true,
            };
        }

        // ── Specific angular momentum vector ─────────────────────────────────
        Vector3d h    = pos.Cross(vel);   // h = r × v
        double   hMag = h.Magnitude;

        // ── Radial-trajectory detection (h ≈ 0) ──────────────────────────────
        // A straight-up / straight-down trajectory has |h| ≈ 0. The eccentricity vector
        // then rounds to ~1 and the semi-major axis collapses (a → 0 or sign-flips),
        // so the conic-section elements become meaningless or NaN. Flag it explicitly:
        // such a trajectory falls to the body centre, so its periapsis is 0 and the
        // Universe impact guard MUST destroy it instead of propagating a conic.
        //
        // Threshold: |h| is compared against a small fraction of the circular angular
        // momentum at the current radius (√(μ·r)). Below ~1e-4 of that, transverse
        // motion is negligible and the trajectory is, for collision purposes, radial.
        double hCircular = System.Math.Sqrt(gm * r);
        bool   isRadial  = hMag < hCircular * 1e-4;

        // ── Node vector  n̂ = ẑ × h ─────────────────────────────────────────
        Vector3d kHat = new(0.0, 0.0, 1.0);
        Vector3d nVec = kHat.Cross(h);
        double   nMag = nVec.Magnitude;

        // ── Eccentricity vector ──────────────────────────────────────────────
        // e⃗ = (1/μ) [ (v²−μ/r) r⃗ − (r⃗·v⃗) v⃗ ]
        double   rDotV = pos.Dot(vel);
        Vector3d eVec  = (pos * (v * v - gm / r) - vel * rDotV) / gm;
        double   e     = eVec.Magnitude;

        // ── Specific orbital energy → semi-major axis ────────────────────────
        double energy = v * v * 0.5 - gm / r;
        double a;
        if (System.Math.Abs(e - 1.0) < 1e-10)
        {
            // Parabolic: use semi-latus rectum as proxy (a→∞)
            a = hMag * hMag / gm;   // p = a(1-e²) → p for e=1
        }
        else
        {
            a = -gm / (2.0 * energy);
        }

        // ── True periapsis radius (valid for every conic) ────────────────────
        // rp = p / (1 + e), with p = |h|²/μ. This avoids the sign traps of a*(1-e):
        //   • elliptic:  p>0, e<1  → rp = a(1-e)
        //   • hyperbolic: a<0, e>1 → a(1-e) is also positive, but p/(1+e) is numerically safer
        //   • radial:    h≈0       → p≈0 → rp≈0 (falls to the centre)
        double p  = hMag * hMag / gm;
        double rp = isRadial ? 0.0 : p / (1.0 + e);

        // ── Inclination ──────────────────────────────────────────────────────
        double i = hMag > 1e-12
            ? System.Math.Acos(System.Math.Clamp(h.Z / hMag, -1.0, 1.0))
            : 0.0;

        // ── Longitude of ascending node ──────────────────────────────────────
        double lan;
        if (nMag < 1e-10)
        {
            // Equatorial orbit — LAN undefined, set to 0
            lan = 0.0;
        }
        else
        {
            lan = System.Math.Acos(System.Math.Clamp(nVec.X / nMag, -1.0, 1.0));
            if (nVec.Y < 0.0) lan = 2.0 * System.Math.PI - lan;
        }

        // ── Argument of periapsis ─────────────────────────────────────────────
        double aop;
        if (e < 1e-10)
        {
            // Circular — AoP undefined, use true longitude of periapsis
            aop = 0.0;
        }
        else if (nMag < 1e-10)
        {
            // Equatorial non-circular — use angle of e-vector in reference plane
            aop = System.Math.Atan2(eVec.Y, eVec.X);
            if (eVec.Z < 0.0) aop = 2.0 * System.Math.PI - aop;
        }
        else
        {
            double nDotE = nVec.Dot(eVec);
            aop = System.Math.Acos(System.Math.Clamp(nDotE / (nMag * e), -1.0, 1.0));
            if (eVec.Z < 0.0) aop = 2.0 * System.Math.PI - aop;
        }

        // ── True anomaly ──────────────────────────────────────────────────────
        double nu;
        if (e < 1e-10)
        {
            // Circular — use argument of latitude
            if (nMag < 1e-10)
            {
                nu = System.Math.Atan2(pos.Y, pos.X);
            }
            else
            {
                double nDotR = nVec.Dot(pos);
                nu = System.Math.Acos(System.Math.Clamp(nDotR / (nMag * r), -1.0, 1.0));
                if (vel.Dot(pos) < 0.0) nu = 2.0 * System.Math.PI - nu;
            }
        }
        else
        {
            double eDotR = eVec.Dot(pos);
            nu = System.Math.Acos(System.Math.Clamp(eDotR / (e * r), -1.0, 1.0));
            if (rDotV < 0.0) nu = 2.0 * System.Math.PI - nu;
        }

        // ── Mean anomaly at epoch (per conic type) ───────────────────────────
        double M0;
        if (e < 1.0)
        {
            // Elliptic: E from ν, then M = E − e·sin E.
            double tanHalfNu = System.Math.Tan(nu * 0.5);
            double tanHalfE  = System.Math.Sqrt((1.0 - e) / (1.0 + e)) * tanHalfNu;
            double E_0       = 2.0 * System.Math.Atan(tanHalfE);
            if (E_0 < 0.0) E_0 += 2.0 * System.Math.PI;
            M0 = E_0 - e * System.Math.Sin(E_0);
            if (M0 < 0.0) M0 += 2.0 * System.Math.PI;
        }
        else if (e > 1.0)
        {
            // Hyperbolic: F from ν, then M = e·sinh F − F (NOT wrapped — M grows monotonically).
            double tanHalfNu = System.Math.Tan(nu * 0.5);
            double tanhHalfF = System.Math.Sqrt((e - 1.0) / (e + 1.0)) * tanHalfNu;
            // Clamp to the valid atanh domain (numerical safety near the asymptote).
            tanhHalfF = System.Math.Clamp(tanhHalfF, -0.999999999999, 0.999999999999);
            double F_0 = 2.0 * System.Math.Atanh(tanhHalfF);
            M0 = e * System.Math.Sinh(F_0) - F_0;
        }
        else
        {
            // Parabolic (e == 1 exactly): store ν as proxy.
            M0 = nu;
        }

        return new OrbitalElements
        {
            SemiMajorAxis            = a,
            Eccentricity             = e,
            Inclination              = i,
            LongitudeOfAscendingNode = lan,
            ArgumentOfPeriapsis      = aop,
            MeanAnomalyAtEpoch       = M0,
            Epoch                    = epoch,
            ReferenceBodyId          = referenceBodyId,
            SpecificAngularMomentum  = hMag,
            PeriapsisRadius          = rp,
            IsRadial                 = isRadial,
            _periapsisSet            = true,
        };
    }
}
