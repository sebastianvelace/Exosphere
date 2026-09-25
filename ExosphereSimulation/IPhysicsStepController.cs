namespace Exosphere.Simulation;

/// <summary>
/// Supplies control commands at deterministic simulation-time boundaries immediately before
/// force integration. Implementations live outside the simulation core (for example, the
/// Godot bridge) and must not advance simulation time themselves.
/// </summary>
public interface IPhysicsStepController
{
    /// <summary>
    /// Returns whether fixed-cadence control is required for the current physical state.
    /// Inactive controllers do not reduce the scheduler's normal propagation step.
    /// </summary>
    bool RequiresFixedCadence(Universe universe);

    /// <summary>
    /// Refreshes commands for the interval that starts at
    /// <see cref="Universe.CurrentTime"/>. The callback runs before any force from that
    /// interval is integrated.
    /// </summary>
    void BeforePhysicsStep(Universe universe, double controlIntervalSeconds);
}
