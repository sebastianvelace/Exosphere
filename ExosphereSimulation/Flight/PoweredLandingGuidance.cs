namespace Exosphere.Simulation.Flight;

/// <summary>
/// Converts a descent-profile error into a requested upward acceleration.
///
/// The returned value is an engine demand, not a promise that the propulsion model can
/// produce continuous thrust. A vehicle below its target descent speed must be allowed
/// to coast; this is essential for engines with a substantial minimum throttle.
/// </summary>
public static class PoweredLandingGuidance
{
    public static double ComputeAccelerationCommand(
        double gravity,
        double thrustUpComponent,
        double verticalError,
        double horizontalError,
        double verticalVelocityUp)
    {
        if (!double.IsFinite(gravity)
            || !double.IsFinite(thrustUpComponent)
            || !double.IsFinite(verticalError)
            || !double.IsFinite(horizontalError)
            || !double.IsFinite(verticalVelocityUp))
            return 0.0;

        // A pin approach can report a small lateral residual while the cradle is already
        // effectively centred. Do not pay the full gravity feed-forward cost for that sensor
        // noise: engines with a high minimum throttle would turn the correction into a hover.
        const double LateralCorrectionDeadbandMps = 0.50;
        if (verticalError <= 0.0 && horizontalError <= LateralCorrectionDeadbandMps)
            return 0.0;

        double brakingError = System.Math.Max(0.0, System.Math.Max(verticalError, horizontalError));
        if (brakingError <= 0.0)
            return 0.0;

        double safeThrustUpComponent = System.Math.Max(0.20, thrustUpComponent);
        double descentBias = System.Math.Clamp(0.90 * verticalError, -3.0, 0.0);
        double command = 1.6 * brakingError
            + System.Math.Max(0.0, gravity) / safeThrustUpComponent
            + descentBias
            - 1.2 * System.Math.Max(0.0, verticalVelocityUp);
        return System.Math.Max(0.0, command);
    }
}
