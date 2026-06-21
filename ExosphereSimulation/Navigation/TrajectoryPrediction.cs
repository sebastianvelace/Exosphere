namespace Exosphere.Simulation.Navigation;

using Exosphere.Simulation.Math;

/// <summary>
/// Result of a forward-propagated encounter search against a moving target body.
/// All distances are metres relative to the central body's centre; times are
/// simulation seconds (since J2000), matching <see cref="Universe.CurrentTime"/>.
/// </summary>
/// <param name="HasEncounter">
/// True when the predicted path enters the target's sphere of influence within the
/// search window. When false the result still reports the closest approach found.
/// </param>
/// <param name="TimeOfClosestApproach">Sim time (s) of minimum separation.</param>
/// <param name="ClosestApproachDistance">Minimum centre-to-centre separation (m).</param>
/// <param name="TimeOfSoiEntry">
/// Sim time (s) at which the path first crosses the target SOI, or +∞ if it never does.
/// </param>
/// <param name="EncounterPosition">
/// Vessel position relative to the CENTRAL body at the encounter instant
/// (SOI entry if any, otherwise closest approach), for drawing the marker (m).
/// </param>
public readonly record struct EncounterResult(
    bool     HasEncounter,
    double   TimeOfClosestApproach,
    double   ClosestApproachDistance,
    double   TimeOfSoiEntry,
    Vector3d EncounterPosition);

/// <summary>
/// Pure, Godot-free forward propagation to predict an encounter (SOI entry or closest
/// approach) between a coasting vessel on a fresh conic and a moving target body.
///
/// The vessel is propagated analytically as a Kepler conic about the central body, while
/// the target body's position is supplied by a caller-provided sampler (so this stays
/// decoupled from <c>Universe</c>). The search marches forward at a fixed coarse step and
/// refines the minimum-separation bracket with a golden-section pass for a tight readout.
///
/// Predicción pura (sin Godot) del encuentro con un cuerpo en movimiento: propaga la
/// cónica del vessel y muestrea la posición del objetivo, detectando la entrada a su SOI
/// o el punto de máxima aproximación.
/// </summary>
public static class TrajectoryPrediction
{
    /// <summary>
    /// Searches for an encounter between the vessel conic <paramref name="vesselOrbit"/>
    /// (about a central body with parameter <paramref name="centralGm"/>) and a target body.
    /// </summary>
    /// <param name="vesselOrbit">
    /// Vessel orbit about the central body, expressed as elements whose state is sampled
    /// via <see cref="OrbitalElements.GetStateAtTime"/>. Position is relative to the central body.
    /// </param>
    /// <param name="centralGm">Gravitational parameter μ of the central body (m³/s²).</param>
    /// <param name="targetRelPositionAt">
    /// Sampler returning the target body's position RELATIVE to the central body at a given
    /// sim time (m). The vessel conic and this sampler must share the same central body frame.
    /// </param>
    /// <param name="targetSoiRadius">Target body's sphere-of-influence radius (m).</param>
    /// <param name="startTime">Sim time (s) to begin the search (typically the burn time).</param>
    /// <param name="searchWindow">Forward span to scan (s). Clamped to a sane minimum.</param>
    /// <param name="coarseSteps">
    /// Number of coarse samples across the window. More steps = finer SOI detection,
    /// at linear cost. Clamped to a sane minimum.
    /// </param>
    public static EncounterResult FindEncounter(
        OrbitalElements vesselOrbit,
        double          centralGm,
        Func<double, Vector3d> targetRelPositionAt,
        double          targetSoiRadius,
        double          startTime,
        double          searchWindow,
        int             coarseSteps = 720)
    {
        if (searchWindow < 1.0)  searchWindow = 1.0;
        if (coarseSteps  < 16)   coarseSteps  = 16;

        double dt = searchWindow / coarseSteps;

        double   bestT    = startTime;
        double   bestDist = double.PositiveInfinity;
        Vector3d bestPos  = Vector3d.Zero;

        double   soiEntryT = double.PositiveInfinity;
        bool     soiFound  = false;
        Vector3d soiPos    = Vector3d.Zero;

        double prevSep = double.NaN;

        for (int i = 0; i <= coarseSteps; i++)
        {
            double   t      = startTime + i * dt;
            Vector3d vPos   = vesselOrbit.GetStateAtTime(t, centralGm).position;
            Vector3d tPos   = targetRelPositionAt(t);
            double   sep    = (vPos - tPos).Magnitude;

            if (sep < bestDist)
            {
                bestDist = sep;
                bestT    = t;
                bestPos  = vPos;
            }

            // First SOI crossing: detect the step where separation drops below the SOI
            // radius and refine the crossing time by bisection within that sub-interval.
            if (!soiFound && targetSoiRadius > 0.0 && sep <= targetSoiRadius)
            {
                double   entryT   = t;
                Vector3d entryPos = vPos;
                if (!double.IsNaN(prevSep) && prevSep > targetSoiRadius && i > 0)
                {
                    double a = t - dt, b = t;
                    for (int k = 0; k < 40; k++)
                    {
                        double   m    = 0.5 * (a + b);
                        Vector3d vm   = vesselOrbit.GetStateAtTime(m, centralGm).position;
                        double   sepM = (vm - targetRelPositionAt(m)).Magnitude;
                        if (sepM <= targetSoiRadius) { b = m; entryPos = vm; }
                        else                          a = m;
                    }
                    entryT = b;
                }
                soiFound  = true;
                soiEntryT = entryT;
                soiPos    = entryPos;
            }

            prevSep = sep;
        }

        // Refine the closest-approach time with a golden-section search inside the bracket
        // [bestT - dt, bestT + dt] so the readout distance/ETA is sub-step accurate.
        if (bestDist < double.PositiveInfinity)
        {
            (bestT, bestDist, bestPos) = RefineClosest(
                vesselOrbit, centralGm, targetRelPositionAt,
                System.Math.Max(startTime, bestT - dt), bestT + dt);
        }

        return new EncounterResult(
            HasEncounter:            soiFound,
            TimeOfClosestApproach:   bestT,
            ClosestApproachDistance: bestDist,
            TimeOfSoiEntry:          soiFound ? soiEntryT : double.PositiveInfinity,
            EncounterPosition:       soiFound ? soiPos : bestPos);
    }

    // Golden-section minimisation of separation(t) over [lo, hi]; the function is smooth
    // and unimodal near a fly-by, so this converges to the true closest approach quickly.
    private static (double t, double dist, Vector3d pos) RefineClosest(
        OrbitalElements vesselOrbit, double centralGm,
        Func<double, Vector3d> targetRelPositionAt, double lo, double hi)
    {
        const double Gr = 0.6180339887498949;   // 1/φ

        double Sep(double t) =>
            (vesselOrbit.GetStateAtTime(t, centralGm).position - targetRelPositionAt(t)).Magnitude;

        double c = hi - Gr * (hi - lo);
        double d = lo + Gr * (hi - lo);
        double fc = Sep(c), fd = Sep(d);

        for (int i = 0; i < 60 && (hi - lo) > 1e-3; i++)
        {
            if (fc < fd) { hi = d; d = c; fd = fc; c = hi - Gr * (hi - lo); fc = Sep(c); }
            else         { lo = c; c = d; fc = fd; d = lo + Gr * (hi - lo); fd = Sep(d); }
        }

        double tBest = 0.5 * (lo + hi);
        Vector3d pos = vesselOrbit.GetStateAtTime(tBest, centralGm).position;
        double dist  = (pos - targetRelPositionAt(tBest)).Magnitude;
        return (tBest, dist, pos);
    }
}
