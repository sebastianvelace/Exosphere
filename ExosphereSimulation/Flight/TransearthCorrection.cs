namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;

/// <summary>
/// Transearth midcourse corrections: <see cref="PeriapsisCorrection"/> burns that move the
/// perigee to the entry-corridor radius on a fixed schedule. Retargeting from the flown state
/// absorbs the TEI aim error and lunar-escape bending.
/// </summary>
public static class TransearthCorrection
{
    /// <summary>
    /// Correction schedule. The first burn is due a fixed time after leaving the Moon's sphere
    /// of influence; the trims are due at these times before perigee, where the corridor is
    /// progressively less sensitive to velocity error (Apollo MCC-5 and MCC-7).
    /// </summary>
    public const double FirstCorrectionDelaySeconds = 6.0 * 3_600.0;
    public static readonly double[] TrimLeadTimesSeconds = [8.0 * 3_600.0, 2.0 * 3_600.0];
    public static int CorrectionCount => 1 + TrimLeadTimesSeconds.Length;

    /// <summary>True once correction <paramref name="index"/> (0-based) is due.</summary>
    public static bool IsDue(int index, double secondsSinceEarthReturn, double timeToPerigee) =>
        index == 0
            ? secondsSinceEarthReturn >= FirstCorrectionDelaySeconds
            : index < CorrectionCount && timeToPerigee <= TrimLeadTimesSeconds[index - 1];

    /// <summary>Velocity-to-be-gained cutoff: coarse for the first burn, fine for the trims.</summary>
    public static double CutoffDeltaVFor(int index) => index == 0 ? 0.1 : 0.02;

    public static LambertGuidedBurn Create(Vessel vessel, CelestialBody earth, double epoch,
        double targetPerigeeRadius, double cutoffDeltaVMps = 0.1, double maximumBurnSeconds = 300.0) =>
        PeriapsisCorrection.Create(vessel, earth, epoch, targetPerigeeRadius, cutoffDeltaVMps, maximumBurnSeconds);
}
