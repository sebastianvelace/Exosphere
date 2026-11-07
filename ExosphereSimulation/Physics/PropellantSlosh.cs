namespace Exosphere.Simulation.Physics;

/// <summary>
/// First lateral slosh mode of a settled liquid column in an upright circular cylindrical tank
/// with a flat (or equal-volume flat) bottom, as an equivalent pendulum. Parameters from NASA
/// SP-8009 "Propellant Slosh Loads" (1968), table of mechanical-model parameters for a cylinder,
/// after NASA SP-106 (Abramson 1966) chapter 6:
/// <list type="bullet">
/// <item>ω₁² = (ξ₁ a/R) tanh(ξ₁ h/R), with ξ₁ = 1.8412 the first root of J₁′ and a the axial
/// specific force that settles the liquid (thrust less drag over mass, not gravity);</item>
/// <item>m₁/m = 2R tanh(ξ₁ h/R) / (ξ₁ (ξ₁² − 1) h) (SP-8009: 0.4547 (R/h) tanh(1.841 h/R));</item>
/// <item>m₁ sits (2R/ξ₁) tanh(ξ₁ h/2R) below the free surface (SP-8009: 1.08 R tanh(0.920 h/R));</item>
/// <item>pendulum length L₁ = R/(ξ₁ tanh(ξ₁ h/R)) = a/ω₁², hinged above m₁;</item>
/// <item>m₀ = m − m₁ fixed at the height that keeps the liquid's centre of mass.</item>
/// </list>
/// The slosh mass is part of the column's own mass: m₀ + m₁ = m exactly, and no mass is added.
/// Inviscid potential flow, small amplitudes and a settled free surface are assumed; baffles
/// change the damping, not these inertial parameters (SP-106 §6.9).
/// </summary>
public static partial class PropellantSlosh
{
    /// <summary>First zero of the Bessel derivative J₁′ (first antisymmetric mode).</summary>
    public const double FirstModeRoot = 1.8411837813406593;
    /// <summary>Numerical series boundary, not a physical tank calibration.</summary>
    public const double ShallowSeriesLimit = 0.001;

    public static SloshModeParameters FirstMode(double radiusM, double depthM, double liquidMassKg,
        double axialSpecificForceMps2)
    {
        if (!(radiusM > 0) || !double.IsFinite(radiusM)) throw new ArgumentOutOfRangeException(nameof(radiusM));
        if (!(depthM > 0) || !double.IsFinite(depthM)) throw new ArgumentOutOfRangeException(nameof(depthM));
        if (!(liquidMassKg > 0) || !double.IsFinite(liquidMassKg)) throw new ArgumentOutOfRangeException(nameof(liquidMassKg));
        if (!double.IsFinite(axialSpecificForceMps2)) throw new ArgumentOutOfRangeException(nameof(axialSpecificForceMps2));
        const double xi = FirstModeRoot;
        double x = xi * (depthM / radiusM);
        if (!double.IsFinite(x) || x <= 0)
            throw new ArgumentException("The dimensionless liquid depth is not representable.");
        double tanh = System.Math.Tanh(x);
        double massFraction = 2.0 / (xi * xi - 1.0) * (tanh / x);
        double sloshMass = liquidMassKg * massFraction;
        double fixedMass = liquidMassKg - sloshMass;
        double pendulumLength = (radiusM / xi) / tanh;
        double sloshHeight = depthM * OneMinusTanhOverX(x / 2.0);
        // Normalize the moment equation before multiplication: m*h may overflow even
        // though both equivalent heights and masses remain representable.
        double fixedHeight = (depthM / 2.0 - (sloshMass / liquidMassKg) * sloshHeight)
            / (fixedMass / liquidMassKg);
        // sqrt(a)/sqrt(L) preserves finite frequencies when a/L would overflow/underflow.
        double frequency = axialSpecificForceMps2 > 0
            ? System.Math.Sqrt(axialSpecificForceMps2) / System.Math.Sqrt(pendulumLength) : 0;
        double hinge = sloshHeight + pendulumLength;
        if (!double.IsFinite(sloshMass) || sloshMass <= 0 || fixedMass <= 0
            || !double.IsFinite(pendulumLength) || pendulumLength <= 0
            || !double.IsFinite(sloshHeight) || sloshHeight < 0
            || !double.IsFinite(fixedHeight) || !double.IsFinite(hinge)
            || !double.IsFinite(frequency) || (axialSpecificForceMps2 > 0 && frequency == 0))
            throw new ArgumentException("The slosh mode cannot be represented with finite parameters.");
        return new(frequency, sloshMass, fixedMass, pendulumLength, sloshHeight, fixedHeight, hinge);
    }

    private static double OneMinusTanhOverX(double x)
    {
        if (x >= ShallowSeriesLimit) return 1.0 - System.Math.Tanh(x) / x;
        double square = x * x;
        return square * (1.0 / 3.0 + square * (-2.0 / 15.0
            + square * (17.0 / 315.0 - square * 62.0 / 2835.0)));

    }
}

/// <summary>
/// Equivalent-pendulum parameters of one tank's first slosh mode. Heights are measured up the
/// tank axis from the column's (equivalent flat) floor.
/// </summary>
public readonly record struct SloshModeParameters(
    double FrequencyRadPerS, double SloshMassKg, double FixedMassKg, double PendulumLengthM,
    double SloshMassHeightM, double FixedMassHeightM, double HingeHeightM)
{
    public double FrequencyHz => FrequencyRadPerS / (2.0 * System.Math.PI);
}

/// <summary>
/// Planar coupling of one slosh pendulum to the rigid vehicle that carries its tank, under a
/// constant axial specific force (the design's coupling equations, free of external lateral
/// load). States: the vehicle's lateral velocity and the pendulum angle; the hinge rides on the
/// vehicle. Total lateral momentum M v + m₁ (v + L θ̇ cos θ) is conserved; small oscillations run
/// at ω² = (a/L)(1 + m₁/M), the free-free correction of a pendulum on a free carrier.
/// </summary>
public static class SloshPendulumCoupling
{
    /// <summary>Lateral momentum of carrier (mass M, lateral velocity v) plus bob.</summary>
    public static double LateralMomentum(double carrierMassKg, double sloshMassKg, double lengthM,
        double carrierVelocity, double angle, double angleRate)
    {
        Validate(carrierMassKg, sloshMassKg, lengthM, carrierVelocity, angle, angleRate);
        double momentum = (carrierMassKg + sloshMassKg) * carrierVelocity
            + sloshMassKg * (lengthM * (angleRate * System.Math.Cos(angle)));
        if (!double.IsFinite(momentum)) throw new ArgumentException("The lateral momentum is not representable.");
        return momentum;
    }

    /// <summary>
    /// Time derivatives (v_dot, theta_ddot) with no external lateral force. Optional viscous
    /// damping acts on the relative angle: it dissipates energy and preserves total momentum.
    /// Damping ratio is supplied by the caller's tank data; zero is the inviscid limit.
    /// </summary>
    public static (double CarrierAcceleration, double AngularAcceleration) Derivatives(double carrierMassKg,
        double sloshMassKg, double lengthM, double axialSpecificForce, double angle, double angleRate,
        double dampingRatio = 0)
    {
        Validate(carrierMassKg, sloshMassKg, lengthM, 0, angle, angleRate);
        if (!double.IsFinite(axialSpecificForce) || axialSpecificForce < 0)
            throw new ArgumentOutOfRangeException(nameof(axialSpecificForce));
        if (!double.IsFinite(dampingRatio) || dampingRatio < 0)
            throw new ArgumentOutOfRangeException(nameof(dampingRatio));
        double sin = System.Math.Sin(angle), cos = System.Math.Cos(angle);
        double total = carrierMassKg + sloshMassKg;
        // M + m*sin²(theta) avoids cancellation in (M+m)-m*cos²(theta).
        double denominator = carrierMassKg + (sloshMassKg * sin) * sin;
        double damping = dampingRatio > 0 && angleRate != 0
            ? 2 * dampingRatio * (System.Math.Sqrt(axialSpecificForce) / System.Math.Sqrt(lengthM)) * angleRate : 0;
        double drive = axialSpecificForce * sin / lengthM + damping;
        double centrifugal = (angleRate * sin) * angleRate;
        double angular = -(drive == 0 ? 0 : (total / denominator) * drive)
            - (centrifugal == 0 ? 0 : (sloshMassKg / denominator) * centrifugal * cos);
        double carrier = sloshMassKg / total * lengthM * (centrifugal - angular * cos);
        if (!double.IsFinite(angular) || !double.IsFinite(carrier))
            throw new ArgumentException("The pendulum accelerations are not representable.");
        return (carrier, angular);
    }

    private static void Validate(double carrier, double slosh, double length, double velocity, double angle, double rate)
    {
        if (!double.IsFinite(carrier) || carrier <= 0) throw new ArgumentOutOfRangeException(nameof(carrier));
        if (!double.IsFinite(slosh) || slosh <= 0) throw new ArgumentOutOfRangeException(nameof(slosh));
        if (!double.IsFinite(carrier + slosh)) throw new ArgumentException("Total mass is not representable.");
        if (!double.IsFinite(length) || length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (!double.IsFinite(velocity)) throw new ArgumentOutOfRangeException(nameof(velocity));
        if (!double.IsFinite(angle)) throw new ArgumentOutOfRangeException(nameof(angle));
        if (!double.IsFinite(rate)) throw new ArgumentOutOfRangeException(nameof(rate));
    }
}
