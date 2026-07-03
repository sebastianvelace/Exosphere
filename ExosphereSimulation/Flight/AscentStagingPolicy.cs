namespace Exosphere.Simulation.Flight;

/// <summary>
/// Pure staging decisions for the Flight 7 Super Heavy hot-stage profile.
/// <see cref="Exosphere.Game.AscentController"/> is the sole [G] authority that calls this;
/// <see cref="Exosphere.Game.MissionManager"/> must not cut throttle or trigger MECO independently.
/// </summary>
public static class AscentStagingPolicy
{
    /// <summary>Surface speed (m/s) at which Super Heavy should hot-stage with reserve still on board.</summary>
    public const double StagingSpeedMps = 2300.0;

    /// <summary>Minimum altitude (m) before velocity-based hot-staging is allowed.</summary>
    public const double StagingMinAltitudeM = 45_000.0;

    /// <summary>Minimum remaining propellant fraction — never burn the booster dry.</summary>
    public const double BoosterReserveFraction = 0.06;

    /// <summary>
    /// Maximum surface-relative impact speed (m/s) that counts as a soft landing in
    /// <see cref="Universe"/> surface impact handling. Must match the EDL touchdown target.
    /// </summary>
    public const double SoftLandingSpeedMps = 3.0;

    /// <summary>
    /// Returns true when the integrated stack should hot-stage: Super Heavy still attached,
    /// staging not yet performed, and either MECO velocity/altitude or booster reserve reached.
    /// </summary>
    public static bool ShouldHotStageSuperHeavy(
        bool alreadyStaged,
        bool boosterStillAttached,
        double surfaceSpeedMps,
        double altitudeMeters,
        double remainingFuelFraction)
    {
        if (alreadyStaged || !boosterStillAttached) return false;

        bool mecoBySpeed = surfaceSpeedMps >= StagingSpeedMps && altitudeMeters > StagingMinAltitudeM;
        bool mecoByReserve = remainingFuelFraction <= BoosterReserveFraction;
        return mecoBySpeed || mecoByReserve;
    }
}
