namespace Exosphere.Simulation.Systems;

/// <summary>
/// Decides whether player stick/throttle write the onboard FCS directly or
/// travel through the ground-command uplink (light-time + LOS gating).
/// Crewed pad/LEO defaults to local immediate control; unmanned craft use the
/// ground path. Structural dead-stick claims neither — <c>Vessel.Tick</c> zeros
/// the stick via <c>ControlAuthority</c>.
/// </summary>
public static class PilotCommandRouting
{
    /// <summary>
    /// True when the HUD should write <c>PitchYawRoll</c> / throttle deltas
    /// straight to the vessel. Link-down must not block this path.
    /// </summary>
    public static bool UsesOnboardStick(bool crewAlive, bool structuralControlLost)
    {
        if (structuralControlLost) return false;
        return crewAlive;
    }

    /// <summary>
    /// True when attitude/throttle samples should go through
    /// <see cref="GroundCommandRelay"/> (may delay or drop on LOS).
    /// </summary>
    public static bool UsesGroundUplink(bool crewAlive, bool structuralControlLost)
    {
        if (structuralControlLost) return false;
        return !crewAlive;
    }
}
