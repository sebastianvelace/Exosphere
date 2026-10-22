namespace Exosphere.Simulation.Navigation;

using Exosphere.Simulation.Math;

/// <summary>Terminal-phase-initiation intercept from the chaser's orbit to a target.</summary>
public sealed record RendezvousInterceptPlan(
    double BurnTime, double ArrivalTime, Vector3d AimPosition, Vector3d InitiationDeltaV,
    Vector3d BrakingDeltaV)
{
    public double TotalDeltaVMps => InitiationDeltaV.Magnitude + BrakingDeltaV.Magnitude;
}

/// <summary>
/// Gemini-style terminal phase initiation (TPI): the chaser travels a fixed transfer angle
/// (~130°) on a Lambert arc that meets the target, offset to a standoff point so the arc is
/// not a collision course. The epoch with the least total Δv over the search window wins.
/// </summary>
public static class RendezvousPlanner
{
    public const double TransferAngleDegrees = 130.0;

    public static RendezvousInterceptPlan PlanIntercept(double gm, OrbitalElements chaserOrbit,
        OrbitalElements targetOrbit, double earliestBurnTime, double searchSeconds, double standoffM,
        double stepSeconds = 30.0)
    {
        ArgumentNullException.ThrowIfNull(chaserOrbit);
        ArgumentNullException.ThrowIfNull(targetOrbit);
        if (!(gm > 0.0) || !(searchSeconds > 0.0) || !(stepSeconds > 0.0) || standoffM < 0.0)
            throw new ArgumentOutOfRangeException(nameof(searchSeconds));
        double chaserPeriod = 2.0 * System.Math.PI * System.Math.Sqrt(
            System.Math.Pow(chaserOrbit.SemiMajorAxis, 3.0) / gm);
        double timeOfFlight = TransferAngleDegrees / 360.0 * chaserPeriod;
        RendezvousInterceptPlan? best = null;
        for (double t = earliestBurnTime; t <= earliestBurnTime + searchSeconds; t += stepSeconds)
        {
            var (chaserPosition, chaserVelocity) = chaserOrbit.GetStateAtTime(t, gm);
            var (targetPosition, targetVelocity) = targetOrbit.GetStateAtTime(t + timeOfFlight, gm);
            var aim = Standoff(targetPosition, standoffM);
            LambertSolution arc;
            try
            {
                arc = LambertSolver.Solve(gm, chaserPosition, aim, timeOfFlight,
                    chaserPosition.Cross(chaserVelocity));
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            {
                continue;
            }
            var plan = new RendezvousInterceptPlan(t, t + timeOfFlight, aim,
                arc.DepartureVelocity - chaserVelocity, targetVelocity - arc.ArrivalVelocity);
            if (best == null || plan.TotalDeltaVMps < best.TotalDeltaVMps) best = plan;
        }
        return best ?? throw new InvalidOperationException("No rendezvous intercept in the search window.");
    }

    /// <summary>
    /// Half the translation-RCS burn time for <paramref name="deltaVMps"/> (s): start a long OAMS
    /// burn this early so it is centred on the impulsive epoch the plan assumed.
    /// </summary>
    public static double HalfBurnSeconds(Vessel vessel, double deltaVMps)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        double acceleration = vessel.Parts.RcsTranslation.ThrustN / System.Math.Max(1.0, vessel.TranslationalMassKg);
        return acceleration > 0.0 ? 0.5 * deltaVMps / acceleration : 0.0;
    }

    /// <summary>Standoff aim point for an arrival epoch, from the target's current conic.</summary>
    public static Vector3d AimPosition(OrbitalElements targetOrbit, double gm, double arrivalTime, double standoffM)
    {
        ArgumentNullException.ThrowIfNull(targetOrbit);
        return Standoff(targetOrbit.GetStateAtTime(arrivalTime, gm).position, standoffM);
    }

    private static Vector3d Standoff(Vector3d targetPosition, double standoffM) =>
        targetPosition + targetPosition.Normalized * standoffM;
}
