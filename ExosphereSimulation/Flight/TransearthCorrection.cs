namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;

/// <summary>
/// Transearth midcourse correction: a velocity-to-be-gained Lambert burn that keeps the
/// current conic's perigee epoch and direction but moves the perigee to the entry-corridor
/// radius. Retargeting from the flown state absorbs the TEI aim error and lunar-escape bending.
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
        double targetPerigeeRadius, double cutoffDeltaVMps = 0.1, double maximumBurnSeconds = 300.0)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        ArgumentNullException.ThrowIfNull(earth);
        var position = vessel.Position - earth.Position;
        var velocity = vessel.Velocity - earth.Velocity;
        double timeToPerigee = PeriapsisCaptureBurn.TimeToPeriapsis(position, velocity, earth.GM);
        if (!double.IsFinite(timeToPerigee) || timeToPerigee <= 0.0)
            throw new InvalidOperationException("The transearth conic has no perigee ahead.");
        var orbit = OrbitalElements.FromStateVector(position, velocity, earth.GM, earth.Id, epoch);
        var (perigee, _) = orbit.GetStateAtTime(epoch + timeToPerigee, earth.GM);
        var direction = perigee.Normalized;
        var normal = position.Cross(velocity);

        // An arc through a point at the target radius is generally not horizontal there, so its
        // true perigee is lower. Secant-iterate the aim radius until the arc's perigee matches.
        double aimRadius = targetPerigeeRadius;
        double previousRadius = double.NaN, previousError = double.NaN;
        for (int iteration = 0; iteration < 12; iteration++)
        {
            var arc = Navigation.LambertSolver.Solve(earth.GM, position, direction * aimRadius,
                timeToPerigee, normal);
            double error = PerigeeRadius(position, arc.DepartureVelocity, earth.GM) - targetPerigeeRadius;
            if (System.Math.Abs(error) < 50.0) break;
            double next = double.IsNaN(previousError) || System.Math.Abs(error - previousError) < 1e-9
                ? aimRadius - error
                : aimRadius - error * (aimRadius - previousRadius) / (error - previousError);
            previousRadius = aimRadius;
            previousError = error;
            aimRadius = System.Math.Max(0.5 * targetPerigeeRadius, next);
        }
        return new LambertGuidedBurn(direction * aimRadius, epoch + timeToPerigee, epoch,
            normal, cutoffDeltaVMps, maximumBurnSeconds);
    }

    private static double PerigeeRadius(Vector3d position, Vector3d velocity, double gm)
    {
        var h = position.Cross(velocity);
        double energy = 0.5 * velocity.MagnitudeSquared - gm / position.Magnitude;
        double p = h.MagnitudeSquared / gm;
        double eccentricity = System.Math.Sqrt(System.Math.Max(0.0, 1.0 + 2.0 * energy * p / gm));
        return p / (1.0 + eccentricity);
    }
}
