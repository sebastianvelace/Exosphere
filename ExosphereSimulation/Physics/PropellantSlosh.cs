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
public static class PropellantSlosh
{
    /// <summary>First zero of the Bessel derivative J₁′ (first antisymmetric mode).</summary>
    public const double FirstModeRoot = 1.8411837813406593;

    public static SloshModeParameters FirstMode(double radiusM, double depthM, double liquidMassKg,
        double axialSpecificForceMps2)
    {
        if (!(radiusM > 0) || !double.IsFinite(radiusM)) throw new ArgumentOutOfRangeException(nameof(radiusM));
        if (!(depthM > 0) || !double.IsFinite(depthM)) throw new ArgumentOutOfRangeException(nameof(depthM));
        if (!(liquidMassKg > 0) || !double.IsFinite(liquidMassKg)) throw new ArgumentOutOfRangeException(nameof(liquidMassKg));
        if (!double.IsFinite(axialSpecificForceMps2)) throw new ArgumentOutOfRangeException(nameof(axialSpecificForceMps2));
        const double xi = FirstModeRoot;
        double depthRatio = depthM / radiusM;
        double tanh = System.Math.Tanh(xi * depthRatio);
        double massFraction = 2.0 * tanh / (xi * (xi * xi - 1.0) * depthRatio);
        double sloshMass = liquidMassKg * massFraction;
        double fixedMass = liquidMassKg - sloshMass;
        double pendulumLength = radiusM / (xi * tanh);
        double sloshDepthBelowSurface = 2.0 * radiusM / xi * System.Math.Tanh(xi * depthRatio / 2.0);
        double sloshHeight = depthM - sloshDepthBelowSurface;
        // m₀ h₀ + m₁ h₁ = m h/2 (the liquid's own centroid, settled).
        double fixedHeight = (liquidMassKg * depthM / 2.0 - sloshMass * sloshHeight) / fixedMass;
        // Settling needs a positive specific force; without it the free surface is unsettled.
        double frequency = axialSpecificForceMps2 > 0
            ? System.Math.Sqrt(axialSpecificForceMps2 * xi / radiusM * tanh) : 0.0;
        return new(frequency, sloshMass, fixedMass, pendulumLength, sloshHeight, fixedHeight,
            sloshHeight + pendulumLength);
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
        double carrierVelocity, double angle, double angleRate) =>
        carrierMassKg * carrierVelocity + sloshMassKg * (carrierVelocity + lengthM * angleRate * System.Math.Cos(angle));

    /// <summary>
    /// Time derivatives (v̇, θ̈) of the planar carrier-pendulum system with axial specific force a.
    /// The carrier has no lateral external force; the hinge reaction is internal.
    /// </summary>
    public static (double CarrierAcceleration, double AngularAcceleration) Derivatives(double carrierMassKg,
        double sloshMassKg, double lengthM, double axialSpecificForce, double angle, double angleRate)
    {
        // Lagrangian of M and m₁ with the bob at x = X + L sin θ, settled by a along −y.
        double sin = System.Math.Sin(angle), cos = System.Math.Cos(angle);
        double total = carrierMassKg + sloshMassKg;
        double denominator = lengthM * (total - sloshMassKg * cos * cos);
        // Ẍ cos θ + L θ̈ + a sin θ = 0 with (M + m₁) Ẍ = m₁ L (θ̇² sin θ − θ̈ cos θ).
        double angular = (-total * axialSpecificForce * sin - sloshMassKg * lengthM * angleRate * angleRate * sin * cos)
            / denominator;
        double carrier = sloshMassKg * lengthM * (angleRate * angleRate * sin - angular * cos) / total;
        return (carrier, angular);
    }
}
