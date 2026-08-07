namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

/// <summary>
/// Pure helpers for the Super Heavy return-to-launch-site profile (R12).
/// Game-layer <c>BoosterReturnController</c> is the sole runtime authority that
/// commands the booster vessel; this type stays free of Godot so xUnit can pin
/// the aiming/cutoff rules without a scene.
///
/// Profile mirrors Flight 5/7 class ops: boostback (~13 engines) reverses the
/// outbound horizontal component, coast through reentry, then a dedicated
/// entry/landing burn (13 → 3 engines) into the Mechazilla cradle — not a full
/// null of post-MECO speed during boostback alone.
/// </summary>
public static class BoosterReturnGuidance
{
    /// <summary>
    /// Cut boostback once the outbound horizontal component (away from the pad)
    /// falls to this threshold. IFT-class profiles reverse ~1.4 km/s outbound into
    /// a modest inbound/near-zero outbound rather than killing all airspeed.
    /// </summary>
    public const double BoostbackCutoffOutboundMps = 100.0;

    /// <summary>
    /// Absolute floor on remaining propellant fraction during boostback — never burn
    /// through the landing/catch reserve that <see cref="AscentStagingPolicy.BoosterReserveFraction"/>
    /// already left on the stage at MECO. Kept slightly below that so a short boostback
    /// is still affordable.
    /// </summary>
    public const double BoostbackMinFuelFraction = 0.025;

    /// <summary>How many centre/inner engines the boostback burn lights.</summary>
    public const int BoostbackEngineCount = 13;

    /// <summary>Altitude (m) at which the 13-engine entry/landing burn arms.</summary>
    public const double EntryBurnArmAltitudeM = 5_000.0;

    /// <summary>Altitude (m) at which the burn throttles to the 3-engine catch set.</summary>
    public const double CatchBurnAltitudeM = 1_500.0;

    /// <summary>Engines lit for the high-thrust entry/landing burn.</summary>
    public const int EntryBurnEngineCount = 13;

    /// <summary>Engines lit for the final chopsticks catch.</summary>
    public const int CatchEngineCount = 3;

    /// <summary>
    /// Plausible IFT-class boostback Δv budget (m/s) from the MECO reserve window
    /// (6% → ~2.5%). Used as an xUnit gate, not a runtime limiter.
    /// </summary>
    public const double IftBoostbackDeltaVMinMps = 800.0;

    public const double IftBoostbackDeltaVMaxMps = 1_800.0;

    public static bool IsStarshipBooster(Vessel vessel) =>
        vessel.Parts.Parts.Any(p =>
            p.Definition.IsStarshipFamily && p.Definition.HasVehicleRole("booster"));

    public static Part? FindBoosterEnginePart(Vessel vessel) =>
        vessel.Parts.Parts.FirstOrDefault(p =>
            p.Definition.IsStarshipFamily && p.Definition.HasVehicleRole("booster"));

    /// <summary>
    /// Remaining liquid+ox fraction on the booster engine/tank part (0–1).
    /// </summary>
    public static double RemainingFuelFraction(Part booster)
    {
        double cap = booster.Definition.FuelCapacityLF + booster.Definition.FuelCapacityOx;
        if (cap <= 0.0) return 0.0;
        return (booster.LiquidFuel + booster.Oxidizer) / cap;
    }

    /// <summary>
    /// Horizontal surface-speed component directed away from the pad (m/s).
    /// Positive = still fleeing downrange; negative = already inbound.
    /// </summary>
    public static double OutboundHorizontalSpeedMps(
        Vector3d surfaceVelocity,
        Vector3d up,
        Vector3d padPosition,
        Vector3d vesselPosition)
    {
        Vector3d vHoriz = surfaceVelocity - up * surfaceVelocity.Dot(up);
        Vector3d toPad = padPosition - vesselPosition;
        Vector3d toPadHoriz = toPad - up * toPad.Dot(up);
        if (toPadHoriz.Magnitude < 1.0)
            return vHoriz.Magnitude;
        return vHoriz.Dot((-toPadHoriz).Normalized);
    }

    /// <summary>
    /// Desired inertial thrust direction for a boostback burn: cancel horizontal
    /// surface velocity and bias slightly toward the pad. Returns a unit vector, or
    /// <see cref="Vector3d.Zero"/> when there is nothing left to cancel.
    /// </summary>
    public static Vector3d BoostbackThrustDirection(
        Vector3d surfaceVelocity,
        Vector3d up,
        Vector3d padPosition,
        Vector3d vesselPosition)
    {
        Vector3d vHoriz = surfaceVelocity - up * surfaceVelocity.Dot(up);
        Vector3d toPad = padPosition - vesselPosition;
        Vector3d toPadHoriz = toPad - up * toPad.Dot(up);

        var aim = -vHoriz;
        if (toPadHoriz.Magnitude > 1.0)
            aim += toPadHoriz.Normalized * System.Math.Min(200.0, vHoriz.Magnitude * 0.25);

        if (aim.Magnitude < 1e-3) return Vector3d.Zero;
        return aim.Normalized;
    }

    /// <summary>
    /// True when the boostback burn should keep firing.
    /// </summary>
    public static bool ShouldContinueBoostback(
        double outboundHorizontalMps,
        double remainingFuelFraction) =>
        outboundHorizontalMps > BoostbackCutoffOutboundMps
        && remainingFuelFraction > BoostbackMinFuelFraction;

    /// <summary>True when coast should hand off to the 13-engine entry burn.</summary>
    public static bool ShouldArmEntryBurn(double altitudeM, bool hasCatchPins) =>
        hasCatchPins && altitudeM < EntryBurnArmAltitudeM;

    /// <summary>True while the high-thrust entry burn should keep the 13-engine set.</summary>
    public static bool ShouldContinueEntryBurn(double altitudeM) =>
        altitudeM >= CatchBurnAltitudeM;

    /// <summary>Rocket-equation Δv (m/s) between two wet masses at a given Isp.</summary>
    public static double EstimateRocketDeltaVMps(double mass0Kg, double mass1Kg, double ispSec)
    {
        if (!(mass0Kg > mass1Kg) || mass1Kg <= 0.0 || ispSec <= 0.0
            || !double.IsFinite(mass0Kg) || !double.IsFinite(mass1Kg) || !double.IsFinite(ispSec))
            return 0.0;
        return ispSec * 9.80665 * System.Math.Log(mass0Kg / mass1Kg);
    }

    /// <summary>
    /// Δv budget available for boostback from the MECO reserve window down to the
    /// boostback fuel floor (Flight 7 Super Heavy class masses).
    /// </summary>
    public static double EstimateBoostbackBudgetDeltaVMps(
        double dryMassKg,
        double propellantCapacityKg,
        double ispVacSec,
        double startFuelFraction = AscentStagingPolicy.BoosterReserveFraction,
        double endFuelFraction = BoostbackMinFuelFraction)
    {
        if (propellantCapacityKg <= 0.0 || dryMassKg <= 0.0) return 0.0;
        double m0 = dryMassKg + propellantCapacityKg * startFuelFraction;
        double m1 = dryMassKg + propellantCapacityKg * endFuelFraction;
        return EstimateRocketDeltaVMps(m0, m1, ispVacSec);
    }
}
