namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Navigation;

/// <summary>
/// Transearth injection flown in closed loop to the planned departure asymptote. The plan is
/// an impulse with a required Moon-relative v∞; steering the SPS on the velocity to be gained
/// toward the departure hyperbola with that asymptote (<see cref="HyperbolicDeparture"/>)
/// absorbs the finite burn, as <see cref="TranslunarInjection"/> does for the TLI. Burning
/// prograde to the planned energy left the first transearth correction at 104 m/s.
/// </summary>
public static class TransearthInjection
{
    /// <summary>Cutoff on the remaining velocity to be gained (m/s).</summary>
    public const double CutoffDeltaVMps = 0.1;

    public static LambertGuidedBurn Create(TransearthInjectionPlan plan, double moonGm, double ignitionTime,
        double maximumBurnSeconds = 600.0)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var vInfinity = plan.HyperbolicExcessVelocity;
        return new LambertGuidedBurn(
            (position, velocity, _) => HyperbolicDeparture.RequiredVelocity(moonGm, position, vInfinity,
                position.Cross(velocity)),
            plan.ArrivalTime, ignitionTime, CutoffDeltaVMps, maximumBurnSeconds);
    }
}
