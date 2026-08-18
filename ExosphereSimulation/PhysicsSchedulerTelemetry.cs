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

/// <summary>Reason why a scheduler call did not dispatch a physics branch.</summary>
public enum PhysicsSchedulerSkipReason
{
    NotInitialized,
    None,
    Paused,
    InvalidDelta,
    InvalidTimeScale,
}

/// <summary>Why a scheduler call stopped before consuming all temporal debt.</summary>
public enum PhysicsSchedulerBudgetReason
{
    None,
    Disabled,
    SubstepLimit,
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
    int DeadlineProjectedDispatches,
    double WallClockMilliseconds,
    bool CatchUpRisk)
{
    /// <summary>True after at least one call to <see cref="Universe.Tick(double)"/>.</summary>
    public bool IsInitialized { get; init; }

    /// <summary>Distinguishes a valid pause from rejected scheduler input.</summary>
    public PhysicsSchedulerSkipReason SkipReason { get; init; } =
        PhysicsSchedulerSkipReason.NotInitialized;

    /// <summary>
    /// Newly requested simulation seconds for this call. Kept separate from the
    /// processed amount so a capped call can be audited without inferring state from
    /// the wall-clock frame time.
    /// </summary>
    public double RequestedSimulationSeconds { get; init; }

    /// <summary>Simulation seconds actually committed to <see cref="Universe.CurrentTime"/>.</summary>
    public double ProcessedSimulationSeconds { get; init; }

    /// <summary>Exact simulation seconds retained for a later scheduler call.</summary>
    public double PendingSimulationSeconds { get; init; }

    /// <summary>Whether the opt-in per-call substep budget stopped this call early.</summary>
    public bool BudgetLimited { get; init; }

    /// <summary>Number of slices skipped by the opt-in deferred-physics candidate.</summary>
    public int CandidateDeferredSkips { get; init; }

    /// <summary>Reason the scheduler retained pending simulation seconds.</summary>
    public PhysicsSchedulerBudgetReason BudgetReason { get; init; } =
        PhysicsSchedulerBudgetReason.None;

    /// <summary>Total vessel work items dispatched, excluding docked secondary skips.</summary>
    public int TotalWorkDispatches =>
        FullPhysicsDispatches
        + OnRailsDispatches
        + SurfaceSettledDispatches
        + GroundHeldDispatches
        + DestroyedDispatches;
}
