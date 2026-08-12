namespace Exosphere.Simulation;

/// <summary>
/// Top-level dispatch path used by the simulation scheduler for the most recent tick.
/// This is simulation telemetry, not a renderer or visual-quality tier.
/// </summary>
public enum PhysicsSchedulerBranch
{
    None,
    FullPhysics,
    Mixed,
    Rails,
}

/// <summary>
/// Why a vessel did or did not receive an independent rails deadline.  A non-deferred
/// reason always means the caller must use the conservative global scheduler cadence.
/// </summary>
public enum PhysicsSchedulerDeadlineReason
{
    DeferredRails,
    ActiveVessel,
    DockedSecondary,
    Destroyed,
    SurfaceSettled,
    GroundHeld,
    ForceSensitive,
    InvalidState,
    MissingOrbit,
    PeriapsisEvent,
}

/// <summary>
/// Side-effect-free scheduling decision for one vessel at the current simulation epoch.
/// This is deliberately a plan, not deferred state: it contains no saved time or physics
/// state and therefore cannot silently advance a vessel without materializing it.
/// </summary>
public readonly record struct PhysicsSchedulerDeadlinePlan(
    bool CanDefer,
    double IntervalSeconds,
    PhysicsSchedulerDeadlineReason Reason);

/// <summary>
/// Deterministic workload counters for one <see cref="Universe.Tick(double)"/> call.
/// Counters describe dispatches, not wall-clock time, so they are comparable across
/// machines and are safe to use as an acceptance baseline before introducing deferred
/// simulation state.
/// </summary>
public readonly record struct PhysicsSchedulerTelemetry(
    PhysicsSchedulerBranch Branch,
    double RealDeltaTime,
    double SimulatedSeconds,
    double EffectiveStepCap,
    int OuterSubsteps,
    int FullPhysicsDispatches,
    int OnRailsDispatches,
    int SurfaceSettledDispatches,
    int GroundHeldDispatches,
    int DestroyedDispatches,
    int DockedSecondarySkips,
    int RailsSlices,
    int DockingConstraintApplications,
    int DeadlineEligibleEvaluations,
    int DeadlineDeferredSkips,
    int DeadlineCatchUpDispatches,
    int DeadlineProjectedDispatches)
{
    /// <summary>Total vessel work items dispatched, excluding docked secondary skips.</summary>
    public int TotalWorkDispatches =>
        FullPhysicsDispatches
        + OnRailsDispatches
        + SurfaceSettledDispatches
        + GroundHeldDispatches
        + DestroyedDispatches;
}
