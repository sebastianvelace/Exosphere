namespace Exosphere.Simulation.Flight;

/// <summary>
/// Unified commit-to-launch gate for pad hold-downs. Release only when thrust
/// clearly exceeds weight <em>and</em> the commanded throttle is nearly full —
/// prevents mid-ramp snap-offs from a partial [Z] hold or early countdown tick.
/// </summary>
public static class HoldDownReleasePolicy
{
    public const double MinThrustToWeight = 1.05;
    public const double MinCommandedThrottle = 0.95;

    public static bool CanRelease(double thrustToWeight, double commandedThrottle) =>
        double.IsFinite(thrustToWeight)
        && double.IsFinite(commandedThrottle)
        && thrustToWeight > MinThrustToWeight
        && commandedThrottle >= MinCommandedThrottle;
}
