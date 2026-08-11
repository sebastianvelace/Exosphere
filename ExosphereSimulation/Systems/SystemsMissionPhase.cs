namespace Exosphere.Simulation.Systems;

/// <summary>
/// Simplified mission phase for systems consumption (sim layer).
/// Mapped from game-layer MissionPhase in SystemsController.
/// </summary>
public enum SystemsMissionPhase
{
    /// Pre-launch, landed, or otherwise idle — minimal life-support EC draw.
    Idle,
    /// Orbit/coast/transfer ops — nominal crew systems load.
    Active,
    /// Ascent, boostback, or landing burns — elevated avionics/EC draw.
    HighLoad,
    /// Atmospheric entry / aero descent — elevated EC + cabin thermal coupling.
    Entry,
    /// Peak heating — maximum EC and TPS leak into the cabin thermal model.
    PeakHeating,
}
