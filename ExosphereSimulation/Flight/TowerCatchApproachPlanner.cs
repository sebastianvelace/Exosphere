namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;

/// <summary>
/// Plans the belly-flop approach to a single-burn tower catch. The flip throws the ship along
/// its nose, which leads the glide, so the burn must light short of the tower on the line the
/// ship is flying in on. Low in the descent the fall is nearly vertical and the lift lies
/// almost horizontal: turning the belly flop about the vertical points that lift, and with it
/// the glide, toward any heading. That authority is small per second but large over the tens
/// of seconds of the terminal descent, so the heading is set from high up, not at ignition.
/// </summary>
public static class TowerCatchApproachPlanner
{
    /// <summary>
    /// Ignition point: <paramref name="leadM"/> short of the cradle on the horizontal line of
    /// sight from the ship, so the ship arrives there flying toward the tower.
    /// </summary>
    public static Vector3d IgnitionPoint(Vector3d cradleWorld, Vector3d upWorld, Vector3d shipWorld, double leadM)
    {
        var up = upWorld.Normalized;
        var lineOfSight = cradleWorld - shipWorld;
        lineOfSight -= up * lineOfSight.Dot(up);
        if (lineOfSight.MagnitudeSquared < 1e-6) return cradleWorld;
        return cradleWorld - lineOfSight.Normalized * leadM;
    }

    /// <summary>
    /// Horizontal acceleration the glide should produce: track a closing speed toward the
    /// ignition point, bounded by the glide the belly flop can sustain. Of the laws tried in the
    /// game this one reached ignition best aligned (see the Flight 15 capture audit); a timed
    /// arrival and a heading law both lost to the belly flop's slow turn rate.
    /// </summary>
    public static Vector3d GlideAccelerationCommand(Vector3d upWorld, Vector3d shipWorld,
        Vector3d horizontalVelocity, Vector3d ignitionPoint, TowerCatchBurnDefinition definition)
    {
        var up = upWorld.Normalized;
        var toPoint = ignitionPoint - shipWorld;
        toPoint -= up * toPoint.Dot(up);
        double distance = toPoint.Magnitude;
        var desired = distance > 1e-6
            ? toPoint * (System.Math.Min(definition.GlideMaximumClosingSpeedMps,
                definition.GlideClosingGainPerSecond * distance) / distance)
            : Vector3d.Zero;
        return (desired - horizontalVelocity) * definition.GlideVelocityGainPerSecond;
    }

    /// <summary>Floor on the time to ignition, so the command stays bounded at the end (s).</summary>
    public const double MinimumTimeToIgnitionS = 5.0;

    /// <summary>Seconds until the ship falls to the ignition clearance at its current descent.</summary>
    public static double TimeToIgnitionS(double pinClearanceM, double descentSpeedMps,
        TowerCatchBurnDefinition definition) =>
        descentSpeedMps > 1.0
            ? System.Math.Max(0.0, (pinClearanceM - definition.IgnitionClearanceM(descentSpeedMps)) / descentSpeedMps)
            : double.PositiveInfinity;

    /// <summary>
    /// Lift direction that steers the glide toward <paramref name="horizontalAcceleration"/>:
    /// its component perpendicular to the airflow. Returns <paramref name="neutralLift"/> when
    /// the command is negligible or lies along the flow.
    /// </summary>
    public static Vector3d LiftDirection(Vector3d airflowDirection, Vector3d horizontalAcceleration,
        Vector3d neutralLift, double deadbandMps2 = 0.05)
    {
        var flow = airflowDirection.Normalized;
        var perpendicular = horizontalAcceleration - flow * horizontalAcceleration.Dot(flow);
        return perpendicular.Magnitude > deadbandMps2 ? perpendicular.Normalized : neutralLift.Normalized;
    }

    /// <summary>
    /// Weight (0..1) of the tower line in the glide lift: zero until
    /// <paramref name="windowS"/> before ignition, full over the last half of the window so the
    /// attitude has settled on it when the engines light.
    /// </summary>
    public static double FinalAlignmentWeight(double timeToIgnitionS, double windowS)
    {
        if (!(windowS > 0.0) || !double.IsFinite(timeToIgnitionS)) return 0.0;
        return System.Math.Clamp((windowS - timeToIgnitionS) / (0.5 * windowS), 0.0, 1.0);
    }

    /// <summary>
    /// Lift that points the belly flop at the tower: the horizontal line of sight, less its
    /// component along the airflow. Falling nearly vertically, the nose leans toward the lift,
    /// so this aims the nose, and the flip that thrusts along it, at the cradle.
    /// </summary>
    public static Vector3d TowerLineLift(Vector3d airflowDirection, Vector3d upWorld, Vector3d shipWorld,
        Vector3d cradleWorld, Vector3d neutralLift)
    {
        var up = upWorld.Normalized;
        var lineOfSight = cradleWorld - shipWorld;
        lineOfSight -= up * lineOfSight.Dot(up);
        if (lineOfSight.MagnitudeSquared < 1e-6) return neutralLift.Normalized;
        return LiftDirection(airflowDirection, lineOfSight.Normalized, neutralLift, 1e-3);
    }
}
