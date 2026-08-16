namespace Exosphere.Simulation.Flight;

/// <summary>
/// Narrow, game-independent gate for a Starbase ship catch.  This is an entry policy,
/// not a contact solver: it only says when the game layer may arm the approach.  The
/// dual-pin contact solver remains the authority that can actually produce <c>IsCaught</c>.
/// </summary>
public static class StarbaseCatchPolicy
{
    public const double MinimumEntrySpeedMps = 1_200.0;
    public const double MaximumEntryAltitudeFactor = 1.05;
    public const double MaximumDescentSpeedForEntryMps = -20.0;

    public static bool IsValidEntry(
        string? bodyId,
        string? launchSiteId,
        bool isDestroyed,
        bool hasCatchPins,
        bool isStarshipShip,
        double altitudeM,
        double atmosphereTopM,
        double verticalSpeedMps,
        double surfaceSpeedMps)
    {
        if (isDestroyed || !hasCatchPins || !isStarshipShip)
            return false;
        if (!string.Equals(bodyId, "earth", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.IsNullOrWhiteSpace(launchSiteId)
            || !launchSiteId.StartsWith("starbase", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!double.IsFinite(altitudeM) || !double.IsFinite(atmosphereTopM)
            || !double.IsFinite(verticalSpeedMps) || !double.IsFinite(surfaceSpeedMps))
            return false;
        if (atmosphereTopM <= 0.0 || altitudeM < 0.0)
            return false;

        return altitudeM <= atmosphereTopM * MaximumEntryAltitudeFactor
            && verticalSpeedMps < MaximumDescentSpeedForEntryMps
            && surfaceSpeedMps > MinimumEntrySpeedMps;
    }
}
