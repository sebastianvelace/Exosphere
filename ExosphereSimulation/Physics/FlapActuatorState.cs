namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;

/// <summary>
/// State of the four Starship body flaps represented as three semantic control channels.
/// Values are normalized commanded deflections in [-1, 1]: pitch, yaw and roll. Keeping the
/// state in the simulation layer makes legacy and coupled integration consume the same
/// actuator position instead of treating a pilot command as an instantaneous force.
/// </summary>
public readonly record struct FlapActuatorState(
    double Pitch,
    double Yaw,
    double Roll)
{
    /// <summary>Zero-deflection state.</summary>
    public static FlapActuatorState Zero => new(0.0, 0.0, 0.0);

    /// <summary>Maximum normalized deflection for each semantic channel.</summary>
    public const double MaximumNormalizedDeflection = 1.0;

    /// <summary>
    /// Nominal full-scale deflection used by the aggregate flap model. This is the renderer's
    /// current geometric envelope, not a claim about a public SpaceX actuator specification.
    /// </summary>
    public const double MaximumDeflectionRadians = 52.0 * MathUtils.DEG_TO_RAD;

    /// <summary>
    /// Simulator estimate for the full-scale actuator rate. Public flight material does not
    /// specify a body-flap servo rate; keep this value explicit so it can be calibrated later.
    /// </summary>
    public const double DefaultRateRadiansPerSecond = 25.0 * MathUtils.DEG_TO_RAD;

    /// <summary>Normalized slew rate derived from the declared angular rate.</summary>
    public const double DefaultRatePerSecond =
        DefaultRateRadiansPerSecond / MaximumDeflectionRadians;

    public bool IsZero => System.Math.Abs(Pitch) < 1e-12
        && System.Math.Abs(Yaw) < 1e-12
        && System.Math.Abs(Roll) < 1e-12;

    public FlapActuatorState Clamp()
        => new(
            System.Math.Clamp(Pitch, -MaximumNormalizedDeflection, MaximumNormalizedDeflection),
            System.Math.Clamp(Yaw, -MaximumNormalizedDeflection, MaximumNormalizedDeflection),
            System.Math.Clamp(Roll, -MaximumNormalizedDeflection, MaximumNormalizedDeflection));

    public static FlapActuatorState FromCommand(Vector3d command)
        => new FlapActuatorState(command.X, command.Y, command.Z).Clamp();

    /// <summary>
    /// Advances each independent actuator toward its demand with a symmetric rate limit.
    /// Saturation is applied before and after the slew so malformed or damaged inputs cannot
    /// create a deflection outside the physical envelope.
    /// </summary>
    public static FlapActuatorState Advance(
        FlapActuatorState current,
        Vector3d command,
        double dt,
        double ratePerSecond = DefaultRatePerSecond)
    {
        if (dt <= 0.0)
            return current.Clamp();

        double rate = System.Math.Max(0.0, ratePerSecond);
        var target = FromCommand(command);
        var previous = current.Clamp();
        double maxStep = rate * dt;
        return new FlapActuatorState(
            Slew(previous.Pitch, target.Pitch, maxStep),
            Slew(previous.Yaw, target.Yaw, maxStep),
            Slew(previous.Roll, target.Roll, maxStep));
    }

    private static double Slew(double current, double target, double maxStep)
    {
        double error = target - current;
        if (System.Math.Abs(error) <= maxStep)
            return target;
        return current + System.Math.CopySign(maxStep, error);
    }
}
