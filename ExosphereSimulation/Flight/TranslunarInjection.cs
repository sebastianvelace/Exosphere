namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Navigation;

/// <summary>
/// Translunar injection flown in closed loop to the planned lunar arrival. The plan is an
/// impulse; the S-IVB burn lasts several minutes (≈ 20° of parking-orbit arc), and the
/// parking orbit flown under J2 drifts from the conic the plan started from. Burning prograde
/// to the planned energy matched the energy but rotated the line of apsides 3.3° and missed
/// the arrival by 23 600 km (a 324 m/s Lambert correction right after cutoff). Steering on the
/// velocity to be gained toward the planned arrival point absorbs both, as the Saturn IGM
/// steered to its target conic.
/// </summary>
public static class TranslunarInjection
{
    /// <summary>Cutoff on the remaining velocity to be gained (m/s).</summary>
    public const double CutoffDeltaVMps = 0.1;

    /// <summary>
    /// The guided injection for <paramref name="plan"/>, lit at <paramref name="ignitionTime"/>
    /// (normally the planned impulse less half the burn).
    /// </summary>
    public static LambertGuidedBurn Create(LunarTransferPlan plan, double earthGm, double ignitionTime,
        double maximumBurnSeconds = 900.0)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var (aim, _) = plan.EarthTransferOrbit.GetStateAtTime(plan.ArrivalTime, earthGm);
        var (position, velocity) = plan.EarthTransferOrbit.GetStateAtTime(plan.BurnTime, earthGm);
        return new LambertGuidedBurn(aim, plan.ArrivalTime, ignitionTime, position.Cross(velocity),
            CutoffDeltaVMps, maximumBurnSeconds);
    }
}
