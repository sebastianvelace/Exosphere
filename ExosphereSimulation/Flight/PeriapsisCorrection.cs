namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;

/// <summary>
/// Velocity-to-be-gained Lambert burn that keeps the current conic's periapsis epoch and
/// direction but moves the periapsis to a target radius, re-solved from the flown state. Used
/// for the transearth entry corridor and for the lunar approach inside the Moon's sphere of
/// influence, where an Earth-centred patched-conic prediction no longer holds.
/// </summary>
public static class PeriapsisCorrection
{
    public static LambertGuidedBurn Create(Vessel vessel, CelestialBody body, double epoch,
        double targetPeriapsisRadius, double cutoffDeltaVMps = 0.1, double maximumBurnSeconds = 300.0)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        ArgumentNullException.ThrowIfNull(body);
        var position = vessel.Position - body.Position;
        var velocity = vessel.Velocity - body.Velocity;
        double timeToPeriapsis = PeriapsisCaptureBurn.TimeToPeriapsis(position, velocity, body.GM);
        if (!double.IsFinite(timeToPeriapsis) || timeToPeriapsis <= 0.0)
            throw new InvalidOperationException("The conic has no periapsis ahead.");
        var orbit = OrbitalElements.FromStateVector(position, velocity, body.GM, body.Id, epoch);
        var (periapsis, _) = orbit.GetStateAtTime(epoch + timeToPeriapsis, body.GM);
        var direction = periapsis.Normalized;
        var normal = position.Cross(velocity);

        // An arc through a point at the target radius is generally not horizontal there, so its
        // true periapsis is lower. Secant-iterate the aim radius until the arc's periapsis matches.
        double aimRadius = targetPeriapsisRadius;
        double previousRadius = double.NaN, previousError = double.NaN;
        for (int iteration = 0; iteration < 12; iteration++)
        {
            var arc = Navigation.LambertSolver.Solve(body.GM, position, direction * aimRadius,
                timeToPeriapsis, normal);
            double error = PeriapsisRadius(position, arc.DepartureVelocity, body.GM) - targetPeriapsisRadius;
            if (System.Math.Abs(error) < 50.0) break;
            double next = double.IsNaN(previousError) || System.Math.Abs(error - previousError) < 1e-9
                ? aimRadius - error
                : aimRadius - error * (aimRadius - previousRadius) / (error - previousError);
            previousRadius = aimRadius;
            previousError = error;
            aimRadius = System.Math.Max(0.5 * targetPeriapsisRadius, next);
        }
        return new LambertGuidedBurn(direction * aimRadius, epoch + timeToPeriapsis, epoch,
            normal, cutoffDeltaVMps, maximumBurnSeconds);
    }

    private static double PeriapsisRadius(Vector3d position, Vector3d velocity, double gm)
    {
        var h = position.Cross(velocity);
        double energy = 0.5 * velocity.MagnitudeSquared - gm / position.Magnitude;
        double p = h.MagnitudeSquared / gm;
        double eccentricity = System.Math.Sqrt(System.Math.Max(0.0, 1.0 + 2.0 * energy * p / gm));
        return p / (1.0 + eccentricity);
    }
}
