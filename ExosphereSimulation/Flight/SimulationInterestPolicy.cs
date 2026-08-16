namespace Exosphere.Simulation.Flight;

/// <summary>
/// CPU work tier for a simulation object. This is a policy decision only; it does not
/// advance, serialize, wake, or otherwise mutate simulation state.
/// </summary>
public enum SimulationInterestTier
{
    /// <summary>Full-resolution work for the controlled or mission-critical object.</summary>
    Active,

    /// <summary>Responsive local work for proximity or an event that needs prompt service.</summary>
    Proximity,

    /// <summary>Analytic/event-driven work until a known future deadline.</summary>
    EventDriven,

    /// <summary>Persistent snapshot/event work with no known near-term deadline.</summary>
    Dormant,
}

/// <summary>
/// Reasons that prevent an object from being safely deferred. The flags are deliberately
/// conservative: any non-zero value blocks <see cref="SimulationInterestTier.EventDriven"/>
/// and <see cref="SimulationInterestTier.Dormant"/>.
/// </summary>
[Flags]
public enum SimulationWakeReason
{
    None = 0,
    Thrust = 1 << 0,
    Command = 1 << 1,
    DockingContact = 1 << 2,
    AtmosphereReentry = 1 << 3,
    SoiDeadline = 1 << 4,
    Selection = 1 << 5,
    MissionCriticalState = 1 << 6,

    /// <summary>Input validation failed; the decision must remain fail-closed.</summary>
    InvalidInput = 1 << 7,

    /// <summary>A mission callback or phase transition still needs prompt service.</summary>
    MissionCallback = 1 << 8,

    /// <summary>A life-support, power, thermal, or communications alert is active.</summary>
    SystemsAlert = 1 << 9,

    /// <summary>A future systems alert is inside the configured wake window.</summary>
    SystemsDeadline = 1 << 10,

    // Descriptive aliases for callers that prefer the wording used in the phase plan.
    DockingOrContact = DockingContact,
    AtmosphereOrReentry = AtmosphereReentry,
    SoiOrDeadline = SoiDeadline,
    MissionCritical = MissionCriticalState,
}

/// <summary>
/// Immutable, deterministic snapshot consumed by <see cref="SimulationInterestPolicy"/>.
/// Nullable distances mean that no corresponding proximity anchor exists. A nullable
/// deadline means that no future SOI/deadline is currently known.
/// </summary>
public readonly record struct SimulationInterestInputs(
    bool IsActiveVessel,
    bool IsPilotControlled,
    bool IsMissionControlled,
    bool IsSelected,
    bool HasThrust,
    bool HasPendingCommand,
    bool HasDockingOrContact,
    bool IsAtmosphereOrReentry,
    bool HasPendingSoiTransition,
    double? SecondsUntilNextDeadline,
    bool IsMissionCriticalState,
    double? DistanceToActiveVesselM,
    double? DistanceToInteractionM)
{
    /// <summary>True when all supplied numeric values are finite and non-negative.</summary>
    public bool IsValid => NumericValuesAreValid();

    /// <summary>
    /// Fail-fast validation for a caller that wants to reject a malformed snapshot before
    /// asking for a decision. <see cref="SimulationInterestPolicy.Classify"/> uses the
    /// same rule but returns a fail-closed decision instead of throwing.
    /// </summary>
    public void Validate()
    {
        ValidateOptionalNonNegative(DistanceToActiveVesselM, nameof(DistanceToActiveVesselM));
        ValidateOptionalNonNegative(DistanceToInteractionM, nameof(DistanceToInteractionM));
        ValidateOptionalNonNegative(
            SecondsUntilNextDeadline,
            nameof(SecondsUntilNextDeadline));
    }

    private bool NumericValuesAreValid() =>
        IsFiniteNonNegative(DistanceToActiveVesselM)
        && IsFiniteNonNegative(DistanceToInteractionM)
        && IsFiniteNonNegative(SecondsUntilNextDeadline);

    private static bool IsFiniteNonNegative(double? value) =>
        !value.HasValue || double.IsFinite(value.Value) && value.Value >= 0.0;

    private static void ValidateOptionalNonNegative(double? value, string parameterName)
    {
        if (!IsFiniteNonNegative(value))
            throw new ArgumentOutOfRangeException(parameterName);
    }
}

/// <summary>
/// Immutable state supplied by the game layer when a mission controller or vehicle
/// systems have information that the pure vessel snapshot cannot see. This boundary is
/// deliberately Godot-free and has no mutation or time-advancement semantics.
/// </summary>
public readonly record struct SimulationExternalInterestInputs(
    bool IsMissionControlled,
    bool IsMissionCriticalState,
    bool IsAtmosphereOrReentry,
    bool HasPendingMissionCallback,
    bool HasSystemsAlert,
    double? SecondsUntilNextSystemsDeadline)
{
    /// <summary>True when the optional systems deadline is finite and non-negative.</summary>
    public bool IsValid => !SecondsUntilNextSystemsDeadline.HasValue
        || double.IsFinite(SecondsUntilNextSystemsDeadline.Value)
            && SecondsUntilNextSystemsDeadline.Value >= 0.0;

    /// <summary>Fail-fast validation for the optional systems deadline.</summary>
    public void Validate()
    {
        if (!IsValid)
            throw new ArgumentOutOfRangeException(nameof(SecondsUntilNextSystemsDeadline));
    }

    /// <summary>No game-layer override is present.</summary>
    public static SimulationExternalInterestInputs None => new(
        IsMissionControlled: false,
        IsMissionCriticalState: false,
        IsAtmosphereOrReentry: false,
        HasPendingMissionCallback: false,
        HasSystemsAlert: false,
        SecondsUntilNextSystemsDeadline: null);
}

/// <summary>Numeric thresholds for one deterministic interest decision.</summary>
public readonly record struct SimulationInterestPolicyOptions(
    double ProximityRadiusM,
    double DeadlineWakeWindowSeconds)
{
    /// <summary>Conservative initial bands from the phase-45 policy proposal.</summary>
    public static SimulationInterestPolicyOptions Default => new(
        ProximityRadiusM: 250_000.0,
        DeadlineWakeWindowSeconds: 60.0);

    /// <summary>True when both thresholds are finite and non-negative.</summary>
    public bool IsValid =>
        double.IsFinite(ProximityRadiusM)
        && ProximityRadiusM >= 0.0
        && double.IsFinite(DeadlineWakeWindowSeconds)
        && DeadlineWakeWindowSeconds >= 0.0;

    /// <summary>Rejects non-finite or negative policy thresholds.</summary>
    public void Validate()
    {
        if (!double.IsFinite(ProximityRadiusM) || ProximityRadiusM < 0.0)
            throw new ArgumentOutOfRangeException(nameof(ProximityRadiusM));
        if (!double.IsFinite(DeadlineWakeWindowSeconds)
            || DeadlineWakeWindowSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(DeadlineWakeWindowSeconds));
        }
    }
}

/// <summary>Result of one side-effect-free interest policy evaluation.</summary>
public readonly record struct SimulationInterestDecision(
    SimulationInterestTier Tier,
    SimulationWakeReason WakeReasons)
{
    /// <summary>True when the decision was forced to the safe tier by malformed input.</summary>
    public bool IsFailClosed =>
        (WakeReasons & SimulationWakeReason.InvalidInput) != 0;

    /// <summary>
    /// True only when a deferred tier is safe according to this policy snapshot. A caller
    /// still owns the actual materialization and event/deadline bookkeeping.
    /// </summary>
    public bool AllowsDeferredWork =>
        !IsFailClosed
        && WakeReasons == SimulationWakeReason.None
        && Tier is SimulationInterestTier.EventDriven or SimulationInterestTier.Dormant;
}

/// <summary>
/// Pure CPU policy primitive for phase 45. It is not wired into the scheduler or project
/// settings; observational adapters may query it, but the default runtime configuration
/// remains off until a later parity gate promotes it.
/// </summary>
public static class SimulationInterestPolicy
{
    /// <summary>This policy is not enabled by default; it cannot skip work by itself.</summary>
    public const bool EnabledByDefault = false;

    /// <summary>
    /// Classifies one immutable snapshot. Invalid numeric values fail closed to
    /// <see cref="SimulationInterestTier.Active"/> and carry
    /// <see cref="SimulationWakeReason.InvalidInput"/>.
    /// </summary>
    public static SimulationInterestDecision Classify(
        SimulationInterestInputs inputs,
        SimulationInterestPolicyOptions? options = null)
        => Classify(inputs, SimulationExternalInterestInputs.None, options);

    /// <summary>
    /// Classifies a vessel snapshot together with an optional game-layer state snapshot.
    /// This overload remains side-effect-free and keeps the default vessel-only API
    /// compatible with callers that have no mission or systems controller.
    /// </summary>
    public static SimulationInterestDecision Classify(
        SimulationInterestInputs inputs,
        SimulationExternalInterestInputs externalInputs,
        SimulationInterestPolicyOptions? options = null)
    {
        var effectiveOptions = options ?? SimulationInterestPolicyOptions.Default;
        if (!inputs.IsValid || !externalInputs.IsValid || !effectiveOptions.IsValid)
        {
            return new(
                SimulationInterestTier.Active,
                SimulationWakeReason.InvalidInput);
        }

        SimulationWakeReason wakeReasons = GetWakeUpReasonsValidated(
            inputs, externalInputs, effectiveOptions);

        // Selection and mission ownership are promoted to Active even when they also
        // carry a wake reason. This keeps player/mission state at full resolution.
        if (inputs.IsActiveVessel
            || inputs.IsPilotControlled
            || inputs.IsMissionControlled
            || inputs.IsSelected
            || inputs.IsMissionCriticalState
            || externalInputs.IsMissionControlled
            || externalInputs.IsMissionCriticalState)
        {
            return new(SimulationInterestTier.Active, wakeReasons);
        }

        // Any event that could change an observable outcome in the current wake window
        // blocks both deferred tiers. This is the fail-closed precedence rule.
        if (wakeReasons != SimulationWakeReason.None
            || IsWithinProximity(inputs.DistanceToActiveVesselM, effectiveOptions.ProximityRadiusM)
            || IsWithinProximity(inputs.DistanceToInteractionM, effectiveOptions.ProximityRadiusM))
        {
            return new(SimulationInterestTier.Proximity, wakeReasons);
        }

        // A known but not-yet-near deadline is safe for event-driven propagation. Without
        // one, the object has no scheduled work and is eligible for Dormant treatment.
        return inputs.SecondsUntilNextDeadline.HasValue
            || externalInputs.SecondsUntilNextSystemsDeadline.HasValue
            ? new(SimulationInterestTier.EventDriven, SimulationWakeReason.None)
            : new(SimulationInterestTier.Dormant, SimulationWakeReason.None);
    }

    /// <summary>
    /// Returns every wake-up flag that applies to a valid snapshot. Invalid inputs return
    /// <see cref="SimulationWakeReason.InvalidInput"/> rather than being treated as idle.
    /// </summary>
    public static SimulationWakeReason GetWakeUpReasons(
        SimulationInterestInputs inputs,
        SimulationInterestPolicyOptions? options = null)
        => GetWakeUpReasons(inputs, SimulationExternalInterestInputs.None, options);

    /// <summary>Returns wake flags after applying an external mission/systems snapshot.</summary>
    public static SimulationWakeReason GetWakeUpReasons(
        SimulationInterestInputs inputs,
        SimulationExternalInterestInputs externalInputs,
        SimulationInterestPolicyOptions? options = null)
    {
        var effectiveOptions = options ?? SimulationInterestPolicyOptions.Default;
        if (!inputs.IsValid || !externalInputs.IsValid || !effectiveOptions.IsValid)
            return SimulationWakeReason.InvalidInput;

        return GetWakeUpReasonsValidated(inputs, externalInputs, effectiveOptions);
    }

    private static SimulationWakeReason GetWakeUpReasonsValidated(
        SimulationInterestInputs inputs,
        SimulationExternalInterestInputs externalInputs,
        SimulationInterestPolicyOptions options)
    {
        SimulationWakeReason reasons = SimulationWakeReason.None;

        if (inputs.HasThrust)
            reasons |= SimulationWakeReason.Thrust;
        if (inputs.HasPendingCommand)
            reasons |= SimulationWakeReason.Command;
        if (inputs.HasDockingOrContact)
            reasons |= SimulationWakeReason.DockingContact;
        if (inputs.IsAtmosphereOrReentry)
            reasons |= SimulationWakeReason.AtmosphereReentry;
        if (inputs.HasPendingSoiTransition
            || inputs.SecondsUntilNextDeadline is double deadline
                && deadline <= options.DeadlineWakeWindowSeconds)
        {
            reasons |= SimulationWakeReason.SoiDeadline;
        }
        if (inputs.IsSelected)
            reasons |= SimulationWakeReason.Selection;
        if (inputs.IsMissionControlled || inputs.IsMissionCriticalState)
            reasons |= SimulationWakeReason.MissionCriticalState;
        if (externalInputs.IsMissionControlled || externalInputs.IsMissionCriticalState)
            reasons |= SimulationWakeReason.MissionCriticalState;
        if (externalInputs.IsAtmosphereOrReentry)
            reasons |= SimulationWakeReason.AtmosphereReentry;
        if (externalInputs.HasPendingMissionCallback)
            reasons |= SimulationWakeReason.MissionCallback;
        if (externalInputs.HasSystemsAlert)
            reasons |= SimulationWakeReason.SystemsAlert;
        if (externalInputs.SecondsUntilNextSystemsDeadline is double systemsDeadline
            && systemsDeadline <= options.DeadlineWakeWindowSeconds)
        {
            reasons |= SimulationWakeReason.SystemsDeadline;
        }

        return reasons;
    }

    private static bool IsWithinProximity(double? distanceM, double radiusM) =>
        distanceM is double distance && distance <= radiusM;
}
