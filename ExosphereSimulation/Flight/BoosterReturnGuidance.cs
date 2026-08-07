namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

/// <summary>
/// Pure helpers for the Super Heavy return-to-launch-site profile (R12).
/// Game-layer <c>BoosterReturnController</c> is the sole runtime authority that
/// commands the booster vessel; this type stays free of Godot so xUnit can pin
/// the aiming/cutoff rules without a scene.
/// </summary>
public static class BoosterReturnGuidance
{
    /// <summary>Horizontal surface speed (m/s) below which the boostback burn cuts.</summary>
    public const double BoostbackCutoffHorizontalMps = 80.0;

    /// <summary>
    /// Absolute floor on remaining propellant fraction during boostback — never burn
    /// through the landing/catch reserve that <see cref="AscentStagingPolicy.BoosterReserveFraction"/>
    /// already left on the stage at MECO. Kept slightly below that so a short boostback
    /// is still affordable.
    /// </summary>
    public const double BoostbackMinFuelFraction = 0.025;

    /// <summary>How many centre/inner engines the boostback burn lights.</summary>
    public const int BoostbackEngineCount = 13;

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
        double horizontalSurfaceSpeedMps,
        double remainingFuelFraction) =>
        horizontalSurfaceSpeedMps > BoostbackCutoffHorizontalMps
        && remainingFuelFraction > BoostbackMinFuelFraction;
}
