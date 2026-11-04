namespace Exosphere.Simulation.Physics;

/// <summary>
/// Viscous crossflow drag of a circular body of revolution: the product η·C_dn that multiplies
/// ½ρ|v⊥|²·A_planform in the Allen–Perkins crossflow term. Digitised from L. H. Jorgensen,
/// "Prediction of Static Aerodynamic Characteristics for Slender Bodies Alone and with Lifting
/// Surfaces to Very High Angles of Attack", NASA TR R-474 (1977):
///
///   fig. 1 — C_dn against crossflow Mach number M_n (subcritical crossflow Reynolds number);
///   figs. 2–3 — C_dn against crossflow Reynolds number Re_n for M_n ≤ 0.4–0.5 (drag crisis
///               near Re_n ≈ 4·10⁵, transcritical recovery to ≈ 0.6 above 10⁷);
///   fig. 4 — η, finite-length to infinite-cylinder ratio, against length/diameter at very low M_n;
///   fig. 6 — η against M_n (bodies of l/d 10–12); η is unity for supersonic crossflow (§2.3.2).
///
/// M_n and Re_n are the components normal to the body axis: M_n = |v⊥|/a, Re_n = ρ|v⊥|d/μ.
/// The curves are piecewise-linear interpolations of the published plots (Re_n in log10).
/// </summary>
public static class CrossflowDrag
{
    /// <summary>Fig. 1: C_dn(M_n) at subcritical Re_n (solid/dashed faired curve and insert).</summary>
    private static readonly (double X, double Y)[] MachCurve =
    [
        (0.0, 1.20), (0.2, 1.20), (0.3, 1.22), (0.4, 1.25), (0.5, 1.33), (0.6, 1.45),
        (0.7, 1.55), (0.8, 1.56), (0.9, 1.60), (0.97, 1.98), (1.1, 1.80), (1.2, 1.72),
        (1.3, 1.66), (1.5, 1.52), (1.6, 1.48), (2.0, 1.43), (2.4, 1.37), (2.8, 1.34), (3.2, 1.33),
        (4.0, 1.32), (5.0, 1.28), (6.0, 1.26), (8.0, 1.24),
    ];

    /// <summary>Figs. 2–3: C_dn(log10 Re_n) for M_n ≤ 0.4 (faired through the data band).</summary>
    private static readonly (double X, double Y)[] ReynoldsCurve =
    [
        (4.0, 1.10), (4.3, 1.20), (5.3, 1.20), (5.4, 1.10), (5.48, 0.95), (5.54, 0.70),
        (5.60, 0.45), (5.65, 0.30), (5.78, 0.28), (5.90, 0.30), (6.0, 0.35), (6.3, 0.45),
        (6.6, 0.53), (7.0, 0.60),
    ];

    /// <summary>Fig. 4: η(l/d) for circular cylinders at very low crossflow Mach number.</summary>
    private static readonly (double X, double Y)[] FinenessEta =
    [
        (1.0, 0.57), (2.0, 0.59), (4.0, 0.62), (8.0, 0.66), (10.0, 0.695), (12.0, 0.705),
        (20.0, 0.75), (40.0, 0.86),
    ];

    /// <summary>Fig. 6: η(M_n) for bodies of l/d 10–12 (unity once the crossflow is supersonic).</summary>
    private static readonly (double X, double Y)[] MachEta =
    [
        (0.0, 0.70), (0.4, 0.72), (0.6, 0.80), (0.8, 0.98), (0.85, 1.00), (0.9, 0.97),
        (1.0, 0.77), (1.1, 0.90), (1.2, 0.95), (1.4, 1.00),
    ];

    /// <summary>Fineness the fig. 6 curve was derived for (bodies of l/d 10 and 12).</summary>
    private const double Fig6Fineness = 11.0;

    /// <summary>Re_n below which the subcritical Mach curve of fig. 1 applies unchanged.</summary>
    private const double SubcriticalReynolds = 2.0e5;

    /// <summary>
    /// η·C_dn for one body element. <paramref name="fineness"/> is the whole body's
    /// length/diameter (η is an end effect of the finite body, not of the element).
    /// </summary>
    public static double Coefficient(double crossflowMach, double crossflowReynolds, double fineness)
    {
        double mn = System.Math.Max(0.0, crossflowMach);
        return Eta(mn, fineness) * SectionCoefficient(mn, crossflowReynolds);
    }

    /// <summary>C_dn of the infinite circular cylinder (figs. 1–3).</summary>
    public static double SectionCoefficient(double crossflowMach, double crossflowReynolds)
    {
        double mn = System.Math.Max(0.0, crossflowMach);
        double compressible = Interpolate(MachCurve, mn);
        if (!(crossflowReynolds > SubcriticalReynolds) || mn >= 0.5) return compressible;
        // Figs. 1 and 3: the drag crisis fills in between M_n 0.35 and 0.5 (supercritical data
        // rise from ~0.6 at M_n 0.35 to ~1.2 at 0.5) as compressibility separates the boundary
        // layer regardless of Reynolds number.
        double viscous = Interpolate(ReynoldsCurve, System.Math.Log10(crossflowReynolds));
        double blend = System.Math.Clamp((mn - 0.35) / 0.15, 0.0, 1.0);
        return viscous + (compressible - viscous) * blend;
    }

    /// <summary>
    /// η(M_n, l/d): fig. 4 at low M_n scaled along fig. 6's Mach trend; the fineness
    /// correction fades out as η reaches unity near M_n 0.8 (fig. 6 converges for l/d 10–12).
    /// </summary>
    public static double Eta(double crossflowMach, double fineness)
    {
        double mn = System.Math.Max(0.0, crossflowMach);
        double machEta = Interpolate(MachEta, mn);
        double l = double.IsFinite(fineness) && fineness > 0.0 ? fineness : Fig6Fineness;
        double ratio = Interpolate(FinenessEta, l) / Interpolate(FinenessEta, Fig6Fineness);
        double fade = System.Math.Clamp((0.8 - mn) / 0.4, 0.0, 1.0);
        double eta = machEta * (1.0 + (ratio - 1.0) * (mn <= 0.4 ? 1.0 : fade));
        return System.Math.Min(1.0, eta);
    }

    private static double Interpolate((double X, double Y)[] curve, double x)
    {
        if (x <= curve[0].X) return curve[0].Y;
        for (int i = 1; i < curve.Length; i++)
        {
            if (x > curve[i].X) continue;
            var (x0, y0) = curve[i - 1];
            var (x1, y1) = curve[i];
            return y0 + (y1 - y0) * (x - x0) / (x1 - x0);
        }
        return curve[^1].Y;
    }
}
