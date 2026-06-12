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

    // ── Computed properties ──────────────────────────────────────────────────

    /// <summary>Apoapsis distance from centre (m).</summary>
    public double Apoapsis => SemiMajorAxis * (1.0 + Eccentricity);

    /// <summary>Periapsis distance from centre (m).</summary>
    public double Periapsis => SemiMajorAxis * (1.0 - Eccentricity);

    /// <summary>Semi-latus rectum (m).</summary>
    public double SemiLatusRectum => SemiMajorAxis * (1.0 - Eccentricity * Eccentricity);

    // ── Methods ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the mean anomaly (radians) at simulation time <paramref name="t"/> (seconds).
    /// </summary>
    public double GetMeanAnomaly(double t, double gm)
    {
        // Mean motion  n = √(GM / |a|³)  rad/s.
        // Para órbitas hiperbólicas (a < 0) se usa |a| para evitar NaN; la propagación
        // hiperbólica completa no está implementada (ver nota en FromStateVector).
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
    /// Returns position and velocity in the inertial frame (relative to reference body) at time <paramref name="t"/>.
    /// </summary>
    public (Vector3d position, Vector3d velocity) GetStateAtTime(double t, double gm)
    {
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
        // returning a trivial circular element set avoids NaN propagation downstream.
        if (r < 1e-9 || gm <= 0.0)
        {
            return new OrbitalElements { ReferenceBodyId = referenceBodyId, Epoch = epoch };
        }

        // ── Specific angular momentum vector ─────────────────────────────────
        Vector3d h = pos.Cross(vel);   // h = r × v
        double   hMag = h.Magnitude;

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

        // ── Inclination ──────────────────────────────────────────────────────
        double i = System.Math.Acos(System.Math.Clamp(h.Z / hMag, -1.0, 1.0));

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

        // ── Eccentric anomaly from true anomaly ──────────────────────────────
        double E_0;
        if (e < 1.0)
        {
            double tanHalfNu = System.Math.Tan(nu * 0.5);
            double tanHalfE  = System.Math.Sqrt((1.0 - e) / (1.0 + e)) * tanHalfNu;
            E_0 = 2.0 * System.Math.Atan(tanHalfE);
            if (E_0 < 0.0) E_0 += 2.0 * System.Math.PI;
        }
        else
        {
            // Hyperbolic / parabolic: store nu as "eccentric anomaly" placeholder
            E_0 = nu;
        }

        // ── Mean anomaly at epoch ─────────────────────────────────────────────
        double M0;
        if (e < 1.0)
        {
            M0 = E_0 - e * System.Math.Sin(E_0);
            if (M0 < 0.0) M0 += 2.0 * System.Math.PI;
        }
        else
        {
            M0 = E_0; // simplified for hyperbolic
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
        };
    }
}
