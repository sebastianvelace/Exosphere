namespace Exosphere.Simulation.Systems;

/// <summary>
/// Simplified mission phase for systems consumption (sim layer).
/// Mapped from game-layer MissionPhase in SystemsController.
/// </summary>
public enum SystemsMissionPhase
{
    /// Pre-launch, landed, or otherwise idle — minimal life-support EC draw.
    Idle,
    /// Ascent, orbit ops, coast, or descent — full crew systems load.
    Active,
}
