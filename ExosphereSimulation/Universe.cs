// Forward declarations note:
// Vessel, Parts.Part, Physics.StressSolver, and Physics.ThermalModel are defined
// in other files within this assembly.  No additional using directives are required
// for types in the same namespace.

namespace Exosphere.Simulation;

using System.Diagnostics;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Integrators;
using Exosphere.Simulation.Math;

/// <summary>
/// Work dispatched for a vessel by the mixed/high-warp scheduler.  These values
/// describe the simulation path, not a visual LOD: <see cref="FullPhysics"/>
/// remains the only path that evaluates the vessel's RK4 forces.
/// </summary>
public enum VesselPhysicsWorkload
{
    FullPhysics,
    OnRails,
    SurfaceSettled,
    GroundHeld,
    Destroyed,
}

/// <summary>
/// Coarse simulation tier for a vessel relative to the currently controlled vessel.
/// This is a scheduling classification, not a visual LOD.  In particular,
/// <see cref="Hibernated"/> currently means that a vessel is eligible for a future
/// deferred-update policy; the current integrator still advances it through the
/// existing compatible path.
/// </summary>
public enum VesselSimulationTier
{
    /// <summary>The vessel currently controlled by the player.</summary>
    Active,

    /// <summary>A vessel that must remain responsive to local/contact/force events.</summary>
    Nearby,

    /// <summary>A coasting vessel already represented by an analytic conic.</summary>
    OnRails,

    /// <summary>A distant, non-force-sensitive vessel eligible for deferred updates.</summary>
    Hibernated,
}

/// <summary>
/// Root simulation container.
/// Owns all celestial bodies and vessels, advances simulation time,
/// and dispatches to the appropriate integrator based on warp factor.
/// </summary>
public class Universe
{
    private readonly List<CelestialBody> _bodies  = new();
    private readonly List<Vessel>        _vessels = new();
    private readonly List<Vessel>        _pendingStructuralDebris = new();
    private readonly List<DockingConnection> _dockingConnections = new();
    private readonly IReadOnlyList<CelestialBody> _bodiesView;
    private readonly IReadOnlyList<Vessel> _vesselsView;
    private readonly IReadOnlyList<DockingConnection> _dockingConnectionsView;
    private readonly Dictionary<string, double> _lastDeferredRailUpdate = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _nextDeferredRailDeadline = new(StringComparer.Ordinal);
    private readonly KeplerPropagator.BodyPropagationWorkspace _bodyPropagationWorkspace = new();
    private double _pendingSimulationSeconds;

    public Universe()
    {
        // Collection views are part of the frame-facing simulation API. Keep one stable
        // read-only facade per backing list so every HUD/controller query does not allocate
        // a new ReadOnlyCollection wrapper.
        _bodiesView = _bodies.AsReadOnly();
        _vesselsView = _vessels.AsReadOnly();
        _dockingConnectionsView = _dockingConnections.AsReadOnly();
    }

    public IReadOnlyList<CelestialBody> Bodies  => _bodiesView;
    public IReadOnlyList<Vessel>        Vessels => _vesselsView;
    public IReadOnlyList<DockingConnection> DockingConnections => _dockingConnectionsView;

    /// <summary>Current simulation time (seconds since J2000).</summary>
    public double CurrentTime { get; private set; } = 0.0;

    /// <summary>
    /// Restores simulation time from a save. Re-propagates celestial bodies to
    /// <paramref name="t"/> so vessel relative state matches the saved epoch.
    /// </summary>
    public void SetCurrentTime(double t)
    {
        if (!double.IsFinite(t))
            throw new ArgumentOutOfRangeException(nameof(t));
        CurrentTime = t;
        _pendingSimulationSeconds = 0.0;
        _lastDeferredRailUpdate.Clear();
        _nextDeferredRailDeadline.Clear();
        if (_bodies.Count > 0)
            KeplerPropagator.PropagateAllBodies(
                _bodies,
                CurrentTime,
                _bodyPropagationWorkspace);
    }

    /// <summary>
    /// Simulation time scale.
    /// 1 = real-time; 4 = full RK4 physics at 4× speed;
    /// up to 1000 = mixed (active vessel RK4, others on rails);
    /// above 1000 = everything on Keplerian rails.
    /// </summary>
    public double TimeScale { get; set; } = 1.0;

    /// <summary>
    /// Enables the development catch-up budget. When enabled, a frame may commit only
    /// <see cref="MaxSchedulerSubstepsPerTick"/> complete global steps; any remainder is
    /// retained as exact temporal debt instead of being discarded. It is disabled by
    /// default until the Godot systems consume processed simulation time explicitly.
    /// </summary>
    public bool SchedulerBudgetEnabled { get; set; }

    /// <summary>
    /// Development-only candidate for skipping non-active distant rail projections. It is
    /// deliberately false by default and has no effect unless an external owner guard is
    /// also installed. The normal game path therefore remains on the existing mixed
    /// scheduler until a later parity gate promotes this candidate.
    /// </summary>
    public bool DeferredPhysicsCandidateEnabled { get; set; }

    /// <summary>
    /// Authoritative external guard for the experimental candidate. The callback must return
    /// true only when the vessel's non-physics state is materialized at the supplied epoch;
    /// null or an exception fails closed to the existing scheduler path.
    /// </summary>
    public Func<Vessel, double, bool>? DeferredPhysicsCandidateEligibility { get; set; }

    private int _maxSchedulerSubstepsPerTick = 256;

    /// <summary>Maximum number of complete global scheduler steps in one budgeted call.</summary>
    public int MaxSchedulerSubstepsPerTick
    {
        get => _maxSchedulerSubstepsPerTick;
        set
        {
            if (value < 1)
                throw new ArgumentOutOfRangeException(nameof(value));
            _maxSchedulerSubstepsPerTick = value;
        }
    }

    /// <summary>
    /// Simulation seconds requested by previous calls but not yet committed. This is exact
    /// debt, not an approximation and not permission to skip a physical event.
    /// </summary>
    public double PendingSimulationSeconds => _pendingSimulationSeconds;

    /// <summary>
    /// Effective cap selected for the most recent mixed/high-warp tick. Zero means the
    /// last tick used the full-physics or pure-rails branch. This is diagnostic telemetry;
    /// it does not change the dispatch policy by itself.
    /// </summary>
    public double LastMixedPhysicsStepCap { get; private set; }

    /// <summary>
    /// Deterministic dispatch counters for the most recent <see cref="Tick(double)"/>.
    /// This records simulation work only; it intentionally excludes wall-clock timing.
    /// </summary>
    public PhysicsSchedulerTelemetry LastSchedulerTelemetry { get; private set; }

    /// <summary>The vessel the player is currently controlling.</summary>
    public Vessel? ActiveVessel { get; set; }

    /// <summary>
    /// Selects the controlled vessel by stable identifier. Payloads, capsules, boosters,
    /// rovers and docked craft all use the same path instead of renderer-specific references.
    /// </summary>
    public bool SetActiveVessel(string vesselId)
    {
        if (string.IsNullOrWhiteSpace(vesselId)) return false;
        var vessel = _vessels.FirstOrDefault(v => v.Id == vesselId);
        if (vessel == null) return false;
        ActiveVessel = vessel;
        return true;
    }

    /// <summary>Restores the authoritative simulation clock from a validated save.</summary>
    public void SetSimulationTime(double secondsSinceJ2000)
    {
        if (!double.IsFinite(secondsSinceJ2000))
            throw new ArgumentOutOfRangeException(nameof(secondsSinceJ2000));
        CurrentTime = secondsSinceJ2000;
        _pendingSimulationSeconds = 0.0;
        _lastDeferredRailUpdate.Clear();
        _nextDeferredRailDeadline.Clear();
        if (_bodies.Count > 0)
            KeplerPropagator.PropagateAllBodies(
                _bodies,
                CurrentTime,
                _bodyPropagationWorkspace);
    }

    /// <summary>Maximum physics sub-step (s) used in full-physics mode (50 Hz).</summary>
    private const double MaxPhysicsStep = 0.02;
    private const double MaxContactStep = 0.005;

    /// <summary>
    /// Diagnostic warning threshold for a single scheduler call. It does not cap or discard
    /// simulation time; it marks a call that needs hitch/catch-up policy review.
    /// </summary>
    public const int CatchUpWarningSubsteps = 128;

    /// <summary>
    /// Distance from the active vessel within which a non-active vessel remains in the
    /// responsive tier.  This is deliberately conservative and only affects the public
    /// classification in this phase; it does not alter the existing physics dispatch.
    /// </summary>
    public const double NearbyVesselDistance = 250_000.0;

    /// <summary>
    /// Distance beyond which a non-active vessel that is not force-sensitive is eligible
    /// for deferred updates at warp.  The current scheduler does not skip it yet.
    /// </summary>
    public const double HibernatedVesselDistance = 5_000_000.0;

    /// <summary>
    /// Maximum sub-step (s) used in the mixed time-warp branch for the active vessel.
    /// Bodies on rails are exact, so the only error source here is the vessel's RK4
    /// integration of its own orbit. RK4 keeps the orbit shape to ~1e-6 % even at much
    /// larger steps, but bounding the step also bounds how far the vessel moves before
    /// the dominant body / SOI is re-evaluated and before surface impact is checked —
    /// at 2 s a LEO vessel advances ~16 km/step, so it cannot tunnel through a planet
    /// or jump across an SOI boundary undetected. At warp 1000 (≈16.7 s of sim time per
    /// frame) this is ~8 sub-steps, which is negligible since only one vessel integrates.
    ///
    /// <para>This is the LOOSEST of the step caps, so it is also the largest dt any
    /// post-integration physics can be handed. <c>ThermalModel</c> sizes its sub-step
    /// ceiling against this value; raising it past
    /// <c>ThermalModel.MaxSubStep · ThermalModel.MaxSubSteps</c> silently coarsens entry
    /// heating, which is why it is public and asserted in <c>ThermalSubstepTests</c>.</para>
    /// </summary>
    public const double MaxCoastStep = 2.0;

    /// <summary>Max RK4 sub-step (s) while the active vessel is THRUSTING under warp — kept
    /// small so a powered burn stays accurate (≈2 steps/frame at x10).</summary>
    private const double MaxThrustStep = 0.1;

    /// <summary>
    /// Maximum surface-relative speed (m/s) that counts as a soft landing.
    /// At or below this threshold the vessel is gently clamped to the surface instead of
    /// being destroyed.  Covers real-time gentle set-down and EDL final approach.
    /// Orbital re-entry speeds (≥ 100 m/s) are several orders of magnitude above this
    /// threshold, so they will always trigger destruction.
    /// </summary>
    private const double SoftLandingThreshold = Flight.AscentStagingPolicy.SoftLandingSpeedMps;

    // Per-tick scheduler counters. Keeping these as fields avoids allocating a metrics
    // object in the frame-critical path; the immutable public snapshot is published once
    // after dispatch completes.
    private PhysicsSchedulerBranch _tickBranch;
    private PhysicsSchedulerSkipReason _tickSkipReason;
    private double _tickRealDeltaTime;
    private double _tickSimulatedSeconds;
    private double _tickProcessedSimulationSeconds;
    private bool _tickBudgetLimited;
    private double _tickEffectiveStepCap;
    private int _tickOuterSubsteps;
    private int _tickFullPhysicsDispatches;
    private int _tickOnRailsDispatches;
    private int _tickSurfaceSettledDispatches;
    private int _tickGroundHeldDispatches;
    private int _tickDestroyedDispatches;
    private int _tickDockedSecondarySkips;
    private int _tickRailsSlices;
    private int _tickDockingConstraintApplications;
    private int _tickDeadlineEligibleEvaluations;
    private int _tickDeadlineDeferredSkips;
    private int _tickDeadlineCatchUpDispatches;
    private int _tickDeadlineProjectedDispatches;
    private int _tickCandidateDeferredSkips;
    private long _tickStartTimestamp;

    // ── Object management ─────────────────────────────────────────────────

    /// <summary>Adds a celestial body to the universe (no-op if already present).</summary>
    public void AddBody(CelestialBody body)   { if (!_bodies.Contains(body))   _bodies.Add(body); }

    /// <summary>Adds a vessel to the universe (no-op if already present).</summary>
    public void AddVessel(Vessel vessel)
    {
        if (_vessels.Any(v => v.Id == vessel.Id && !ReferenceEquals(v, vessel)))
            throw new InvalidOperationException($"Duplicate vessel id '{vessel.Id}'.");
        if (!_vessels.Contains(vessel)) _vessels.Add(vessel);
    }

    /// <summary>
    /// Structural-break debris spawned since the last drain. The game layer uses this to
    /// spawn renderers without double-counting intentional staging debris.
    /// </summary>
    public IReadOnlyList<Vessel> DrainPendingStructuralDebris()
    {
        if (_pendingStructuralDebris.Count == 0)
            return System.Array.Empty<Vessel>();
        var copy = _pendingStructuralDebris.ToList();
        _pendingStructuralDebris.Clear();
        return copy;
    }

    /// <summary>Removes a vessel from the universe.</summary>
    public void RemoveVessel(Vessel vessel)
    {
        _dockingConnections.RemoveAll(connection =>
            connection.PrimaryVesselId == vessel.Id
            || connection.SecondaryVesselId == vessel.Id);
        _vessels.Remove(vessel);
        _lastDeferredRailUpdate.Remove(vessel.Id);
        _nextDeferredRailDeadline.Remove(vessel.Id);
        if (ReferenceEquals(ActiveVessel, vessel))
            ActiveVessel = null;
    }

    public DockingAttempt TryDock(
        string primaryVesselId,
        string primaryPortPartId,
        string secondaryVesselId,
        string secondaryPortPartId,
        string? connectionId = null)
    {
        if (primaryVesselId == secondaryVesselId)
            return FailedDocking(DockingFailure.SameVessel);
        if (!string.IsNullOrWhiteSpace(connectionId)
            && _dockingConnections.Any(connection =>
                connection.Id == connectionId))
            return FailedDocking(DockingFailure.ConnectionIdConflict);
        var primary = _vessels.FirstOrDefault(v => v.Id == primaryVesselId);
        var secondary = _vessels.FirstOrDefault(v => v.Id == secondaryVesselId);
        if (primary == null || secondary == null)
            return FailedDocking(DockingFailure.VesselMissing);
        CatchUpDeferredRailVessel(primary, CurrentTime);
        CatchUpDeferredRailVessel(secondary, CurrentTime);
        if (_dockingConnections.Any(connection =>
                connection.PrimaryVesselId == primaryVesselId
                || connection.SecondaryVesselId == primaryVesselId
                || connection.PrimaryVesselId == secondaryVesselId
                || connection.SecondaryVesselId == secondaryVesselId))
            return FailedDocking(DockingFailure.VesselAlreadyDocked);

        var primaryPort = primary.Parts.Parts.FirstOrDefault(part =>
            part.InstanceId == primaryPortPartId);
        var secondaryPort = secondary.Parts.Parts.FirstOrDefault(part =>
            part.InstanceId == secondaryPortPartId);
        if (primaryPort == null || secondaryPort == null)
            return FailedDocking(DockingFailure.PortMissing);
        if (!primaryPort.Definition.IsDockingPort
            || !secondaryPort.Definition.IsDockingPort
            || primaryPort.IsBroken
            || secondaryPort.IsBroken)
            return FailedDocking(DockingFailure.PortUnavailable);
        if (_dockingConnections.Any(connection =>
                connection.PrimaryPortPartId == primaryPortPartId
                || connection.SecondaryPortPartId == primaryPortPartId
                || connection.PrimaryPortPartId == secondaryPortPartId
                || connection.SecondaryPortPartId == secondaryPortPartId))
            return FailedDocking(DockingFailure.PortUnavailable);
        if (!TryGetDockingFrame(
                primary, primaryPort, out var primaryPosition,
                out var primaryAxis, out var primaryVelocity)
            || !TryGetDockingFrame(
                secondary, secondaryPort, out var secondaryPosition,
                out var secondaryAxis, out var secondaryVelocity))
            return FailedDocking(DockingFailure.PortMissing);

        double distance = (secondaryPosition - primaryPosition).Magnitude;
        double relativeSpeed = (secondaryVelocity - primaryVelocity).Magnitude;
        double alignmentError = System.Math.Acos(System.Math.Clamp(
            primaryAxis.Dot(-secondaryAxis), -1.0, 1.0))
            * MathUtils.RAD_TO_DEG;
        double captureRange = System.Math.Min(
            primaryPort.Definition.DockingCaptureRangeM,
            secondaryPort.Definition.DockingCaptureRangeM);
        double maximumSpeed = System.Math.Min(
            primaryPort.Definition.DockingMaxCaptureSpeedMps,
            secondaryPort.Definition.DockingMaxCaptureSpeedMps);
        double alignmentTolerance = System.Math.Min(
            primaryPort.Definition.DockingAlignmentToleranceDeg,
            secondaryPort.Definition.DockingAlignmentToleranceDeg);
        if (distance > captureRange)
            return FailedDocking(
                DockingFailure.OutsideCaptureRange,
                distance, relativeSpeed, alignmentError);
        if (relativeSpeed > maximumSpeed)
            return FailedDocking(
                DockingFailure.ExcessiveClosingSpeed,
                distance, relativeSpeed, alignmentError);
        if (alignmentError > alignmentTolerance)
            return FailedDocking(
                DockingFailure.Misaligned,
                distance, relativeSpeed, alignmentError);

        var connection = new DockingConnection
        {
            Id = string.IsNullOrWhiteSpace(connectionId)
                ? Guid.NewGuid().ToString()
                : connectionId,
            PrimaryVesselId = primary.Id,
            SecondaryVesselId = secondary.Id,
            PrimaryPortPartId = primaryPort.InstanceId,
            SecondaryPortPartId = secondaryPort.InstanceId,
            PrimaryPortNodeId = primaryPort.Definition.DockingNodeId,
            SecondaryPortNodeId = secondaryPort.Definition.DockingNodeId,
            SecondaryPositionPrimaryLocal = primary.Orientation.Inverse().Rotate(
                secondary.Position - primary.Position),
            SecondaryOrientationPrimaryLocal =
                (primary.Orientation.Inverse() * secondary.Orientation).Normalize(),
        };
        CaptureDockingMomentum(primary, secondary);
        WakeVesselFromRails(primary);
        WakeVesselFromRails(secondary);
        _lastDeferredRailUpdate.Remove(primary.Id);
        _lastDeferredRailUpdate.Remove(secondary.Id);
        _nextDeferredRailDeadline.Remove(primary.Id);
        _nextDeferredRailDeadline.Remove(secondary.Id);
        _dockingConnections.Add(connection);
        ApplyDockingConstraint(connection);
        return new DockingAttempt(
            true, DockingFailure.None, connection,
            distance, relativeSpeed, alignmentError);
    }

    public bool Undock(string connectionId, double separationSpeedMps = 0.0)
    {
        var connection = _dockingConnections.FirstOrDefault(candidate =>
            candidate.Id == connectionId);
        if (connection == null || separationSpeedMps < 0.0
            || !double.IsFinite(separationSpeedMps))
            return false;
        var primary = _vessels.FirstOrDefault(
            v => v.Id == connection.PrimaryVesselId);
        var secondary = _vessels.FirstOrDefault(
            v => v.Id == connection.SecondaryVesselId);
        _dockingConnections.Remove(connection);
        if (primary == null || secondary == null)
            return true;

        // A detached assembly must leave analytic/deferred rails before any separation
        // impulse is applied. This also covers a zero-speed manual undock.
        WakeVesselFromRails(primary);
        WakeVesselFromRails(secondary);
        _lastDeferredRailUpdate.Remove(primary.Id);
        _lastDeferredRailUpdate.Remove(secondary.Id);
        _nextDeferredRailDeadline.Remove(primary.Id);
        _nextDeferredRailDeadline.Remove(secondary.Id);
        if (separationSpeedMps == 0.0)
            return true;

        Vector3d direction = (secondary.Position - primary.Position).Normalized;
        if (direction.MagnitudeSquared < 0.5)
            direction = primary.Orientation.Rotate(Vector3d.Up);
        double totalMass = primary.TotalMass + secondary.TotalMass;
        if (totalMass <= 0.0) return true;
        primary.Velocity -= direction
            * (separationSpeedMps * secondary.TotalMass / totalMass);
        secondary.Velocity += direction
            * (separationSpeedMps * primary.TotalMass / totalMass);
        return true;
    }

    public void RestoreDockingConnection(DockingConnection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.Id)
            || _dockingConnections.Any(candidate =>
                candidate.Id == connection.Id))
            throw new InvalidDataException(
                $"Invalid or duplicate docking connection '{connection.Id}'.");
        var primary = _vessels.FirstOrDefault(
            v => v.Id == connection.PrimaryVesselId);
        var secondary = _vessels.FirstOrDefault(
            v => v.Id == connection.SecondaryVesselId);
        if (primary == null || secondary == null
            || primary.Parts.Parts.All(part =>
                part.InstanceId != connection.PrimaryPortPartId
                || !part.Definition.IsDockingPort)
            || secondary.Parts.Parts.All(part =>
                part.InstanceId != connection.SecondaryPortPartId
                || !part.Definition.IsDockingPort)
            || _dockingConnections.Any(candidate =>
                candidate.PrimaryVesselId == primary.Id
                || candidate.SecondaryVesselId == primary.Id
                || candidate.PrimaryVesselId == secondary.Id
                || candidate.SecondaryVesselId == secondary.Id
                || candidate.PrimaryPortPartId
                    == connection.PrimaryPortPartId
                || candidate.SecondaryPortPartId
                    == connection.PrimaryPortPartId
                || candidate.PrimaryPortPartId
                    == connection.SecondaryPortPartId
                || candidate.SecondaryPortPartId
                    == connection.SecondaryPortPartId))
            throw new InvalidDataException(
                $"Docking connection '{connection.Id}' has invalid references.");
        _dockingConnections.Add(connection);
        ApplyDockingConstraint(connection);
    }

    private static DockingAttempt FailedDocking(
        DockingFailure failure,
        double distance = double.NaN,
        double relativeSpeed = double.NaN,
        double alignmentError = double.NaN) =>
        new(false, failure, null, distance, relativeSpeed, alignmentError);

    private static bool TryGetDockingFrame(
        Vessel vessel,
        Parts.Part part,
        out Vector3d position,
        out Vector3d axis,
        out Vector3d velocity)
    {
        position = vessel.Position;
        axis = Vector3d.Zero;
        velocity = vessel.Velocity;
        var definition = part.Definition;
        if (definition.DockingAxisLocal is not { Length: >= 3 }
            || !vessel.Parts.TryGetAttachmentNodeLocalPosition(
                part.InstanceId, definition.DockingNodeId, out var localPosition))
            return false;
        Vector3d offset = vessel.Orientation.Rotate(localPosition);
        Vector3d localAxis = new(
            definition.DockingAxisLocal[0],
            definition.DockingAxisLocal[1],
            definition.DockingAxisLocal[2]);
        if (!double.IsFinite(localAxis.X)
            || !double.IsFinite(localAxis.Y)
            || !double.IsFinite(localAxis.Z)
            || localAxis.MagnitudeSquared < 0.5)
            return false;
        position += offset;
        axis = vessel.Orientation.Rotate(localAxis.Normalized);
        velocity += vessel.AngularVelocity.Cross(offset);
        return true;
    }

    private static void CaptureDockingMomentum(
        Vessel primary,
        Vessel secondary)
    {
        double primaryMass = primary.TotalMass;
        double secondaryMass = secondary.TotalMass;
        double totalMass = primaryMass + secondaryMass;
        if (totalMass <= 0.0) return;
        Vector3d centre = (
            primary.Position * primaryMass
            + secondary.Position * secondaryMass) / totalMass;
        Vector3d commonVelocity = (
            primary.Velocity * primaryMass
            + secondary.Velocity * secondaryMass) / totalMass;
        Vector3d primaryArm = primary.Position - centre;
        Vector3d secondaryArm = secondary.Position - centre;
        double primaryInertia = primary.Parts.TransverseMomentOfInertia;
        double secondaryInertia = secondary.Parts.TransverseMomentOfInertia;
        Vector3d angularMomentum =
            primary.AngularVelocity * primaryInertia
            + secondary.AngularVelocity * secondaryInertia
            + primaryArm.Cross(
                (primary.Velocity - commonVelocity) * primaryMass)
            + secondaryArm.Cross(
                (secondary.Velocity - commonVelocity) * secondaryMass);
        double combinedInertia = primaryInertia + secondaryInertia
            + primaryMass * primaryArm.MagnitudeSquared
            + secondaryMass * secondaryArm.MagnitudeSquared;
        Vector3d commonAngularVelocity = combinedInertia > 0.0
            ? angularMomentum / combinedInertia
            : Vector3d.Zero;
        primary.AngularVelocity = commonAngularVelocity;
        secondary.AngularVelocity = commonAngularVelocity;
        primary.Velocity = commonVelocity
            + commonAngularVelocity.Cross(primaryArm);
        secondary.Velocity = commonVelocity
            + commonAngularVelocity.Cross(secondaryArm);
    }

    private bool IsDockedSecondary(Vessel vessel) =>
        _dockingConnections.Any(connection =>
            connection.SecondaryVesselId == vessel.Id);

    private void ApplyDockingConstraints()
    {
        _dockingConnections.RemoveAll(connection =>
            _vessels.All(v =>
                v.Id != connection.PrimaryVesselId || v.IsDestroyed)
            || _vessels.All(v =>
                v.Id != connection.SecondaryVesselId || v.IsDestroyed));
        _tickDockingConstraintApplications += _dockingConnections.Count;
        foreach (var connection in _dockingConnections)
            ApplyDockingConstraint(connection);
    }

    private void ApplyDockingConstraint(DockingConnection connection)
    {
        var primary = _vessels.FirstOrDefault(
            v => v.Id == connection.PrimaryVesselId);
        var secondary = _vessels.FirstOrDefault(
            v => v.Id == connection.SecondaryVesselId);
        if (primary == null || secondary == null) return;
        Vector3d offset = primary.Orientation.Rotate(
            connection.SecondaryPositionPrimaryLocal);
        secondary.Position = primary.Position + offset;
        secondary.Orientation = (
            primary.Orientation
            * connection.SecondaryOrientationPrimaryLocal).Normalize();
        secondary.Velocity = primary.Velocity
            + primary.AngularVelocity.Cross(offset);
        secondary.AngularVelocity = primary.AngularVelocity;
        WakeVesselFromRails(secondary);
    }

    /// <summary>Finds a celestial body by its <see cref="CelestialBody.Id"/>.</summary>
    public CelestialBody? GetBody(string id)
    {
        // This is on the orbital propagation path. A captured LINQ predicate here created
        // a delegate/closure for every reference-body lookup, including every deferred-rail
        // projection. The body list is small and stable, so a direct scan is both cheaper
        // and allocation-free without changing lookup semantics.
        for (int i = 0; i < _bodies.Count; i++)
            if (_bodies[i].Id == id)
                return _bodies[i];
        return null;
    }

    /// <summary>
    /// Returns the celestial body whose sphere of influence contains
    /// <paramref name="position"/>.  When multiple SOIs overlap, the one where the
    /// position is deepest (smallest distance/SOI ratio) wins.
    /// Falls back to the most massive body (the Sun) when no SOI contains the point.
    /// </summary>
    public CelestialBody GetDominantBody(Vector3d position)
    {
        // Pick the body with the smallest SOI that still contains the position.
        // This correctly resolves the hierarchy: Moon < Earth < Sun.
        CelestialBody? best    = null;
        double         bestSoi = double.MaxValue;

        foreach (var body in _bodies)
        {
            double dist = (position - body.Position).Magnitude;
            if (dist < body.SphereOfInfluence && body.SphereOfInfluence < bestSoi)
            {
                bestSoi = body.SphereOfInfluence;
                best    = body;
            }
        }

        if (best != null) return best;

        // This fallback is queried by multiple render/physics consumers. Avoid
        // OrderByDescending here: the body list is stable and a manual maximum
        // keeps the no-SOI path allocation-free.
        CelestialBody fallback = _bodies[0];
        for (int i = 1; i < _bodies.Count; i++)
            if (_bodies[i].Mass > fallback.Mass)
                fallback = _bodies[i];
        return fallback;
    }

    // ── Main tick ──────────────────────────────────────────────────────────

    /// <summary>
    /// Advances the simulation by <paramref name="realDeltaTime"/> wall-clock seconds.
    /// The actual simulated time advance equals
    /// <c>realDeltaTime * <see cref="TimeScale"/></c>.
    /// </summary>
    public void Tick(double realDeltaTime)
    {
        LastMixedPhysicsStepCap = 0.0;
        double simDelta = realDeltaTime * TimeScale;
        BeginSchedulerTelemetry(realDeltaTime, simDelta);
        if (!double.IsFinite(realDeltaTime) || realDeltaTime <= 0.0)
        {
            _tickSkipReason = PhysicsSchedulerSkipReason.InvalidDelta;
            PublishSchedulerTelemetry();
            return;
        }
        if (!double.IsFinite(TimeScale) || TimeScale < 0.0)
        {
            _tickSkipReason = PhysicsSchedulerSkipReason.InvalidTimeScale;
            PublishSchedulerTelemetry();
            return;
        }
        if (TimeScale == 0.0)
        {
            _tickSkipReason = PhysicsSchedulerSkipReason.Paused;
            PublishSchedulerTelemetry();
            return;
        }
        if (!double.IsFinite(simDelta))
        {
            _tickSkipReason = PhysicsSchedulerSkipReason.InvalidDelta;
            PublishSchedulerTelemetry();
            return;
        }

        double accumulated = _pendingSimulationSeconds + simDelta;
        if (!double.IsFinite(accumulated))
        {
            _tickSkipReason = PhysicsSchedulerSkipReason.InvalidDelta;
            PublishSchedulerTelemetry();
            return;
        }
        _pendingSimulationSeconds = accumulated;

        int substepBudget = SchedulerBudgetEnabled
            ? MaxSchedulerSubstepsPerTick
            : int.MaxValue;

        bool anyForceSensitive = _vessels.Any(RequiresBoundedWarpPropagation);
        bool anyContactSensitive = _bodies.Count > 0 && _vessels.Any(v =>
            v.HasDeployedLandingGear
            && GetDominantBody(v.Position).GetAltitude(v.Position) < 100.0);

        if (TimeScale <= 4.0)
        {
            // Full RK4 physics, capped at MaxPhysicsStep per sub-step
            _tickBranch = PhysicsSchedulerBranch.FullPhysics;
            FlushDeferredRailsToCurrentTime();
            _lastDeferredRailUpdate.Clear();
            _nextDeferredRailDeadline.Clear();
            _tickEffectiveStepCap = anyContactSensitive ? MaxContactStep : MaxPhysicsStep;
            double remaining = _pendingSimulationSeconds;
            while (remaining > 1e-12 && _tickOuterSubsteps < substepBudget)
            {
                double step  = System.Math.Min(remaining,
                    anyContactSensitive ? MaxContactStep : MaxPhysicsStep);
                _tickOuterSubsteps++;
                TickPhysics(step);
                CurrentTime += step;
                remaining   -= step;
                _tickProcessedSimulationSeconds += step;
            }
        }
        else if (TimeScale <= 1000.0 || anyForceSensitive)
        {
            // Mixed: active vessel uses RK4; all others go on rails.
            // Sub-step (capped at MaxCoastStep) so a single big warp dt is never fed to
            // RK4 in one shot — this bounds per-step travel, keeps SOI/dominant-body
            // re-evaluation timely, and lets surface-impact be checked each sub-step.
            // While the active vessel is thrusting, tighten the sub-step so a powered burn under
            // warp integrates accurately (thrust + gravity) and matches a real-time burn.
            double cap = GetMixedPhysicsStepCap(anyContactSensitive);
            LastMixedPhysicsStepCap = cap;
            _tickBranch = PhysicsSchedulerBranch.Mixed;
            _tickEffectiveStepCap = cap;
            double remaining = _pendingSimulationSeconds;
            while (remaining > 1e-12 && _tickOuterSubsteps < substepBudget)
            {
                double step = System.Math.Min(remaining, cap);
                _tickOuterSubsteps++;
                TickPhysicsMixed(step);
                CurrentTime += step;
                remaining   -= step;
                _tickProcessedSimulationSeconds += step;
            }
        }
        else
        {
            // Pure rails: everything propagated analytically
            _tickBranch = PhysicsSchedulerBranch.Rails;
            FlushDeferredRailsToCurrentTime();
            _lastDeferredRailUpdate.Clear();
            _nextDeferredRailDeadline.Clear();
            _tickEffectiveStepCap = MaxCoastStep;
            double remaining = _pendingSimulationSeconds;
            if (SchedulerBudgetEnabled)
            {
                // TickRails has its own event-safe sampling, but a budgeted call must
                // still stop between complete global steps so docking, SOI and wake-up
                // decisions cannot be split across an unfinished fleet update.
                while (remaining > 1e-12 && _tickOuterSubsteps < substepBudget)
                {
                    double step = System.Math.Min(remaining, MaxCoastStep);
                    _tickOuterSubsteps++;
                    TickRails(step);
                    CurrentTime += step;
                    remaining -= step;
                    _tickProcessedSimulationSeconds += step;
                }
            }
            else
            {
                _tickOuterSubsteps++;
                TickRails(remaining);
                CurrentTime += remaining;
                _tickProcessedSimulationSeconds += remaining;
                remaining = 0.0;
            }

            _pendingSimulationSeconds = remaining;
        }

        if (_tickBranch != PhysicsSchedulerBranch.Rails)
            _pendingSimulationSeconds =
                System.Math.Max(0.0, _pendingSimulationSeconds - _tickProcessedSimulationSeconds);
        _tickBudgetLimited = _pendingSimulationSeconds > 1e-12;

        PublishSchedulerTelemetry();
    }

    private void BeginSchedulerTelemetry(double realDeltaTime, double simDelta)
    {
        _tickStartTimestamp = Stopwatch.GetTimestamp();
        _tickBranch = PhysicsSchedulerBranch.None;
        _tickSkipReason = PhysicsSchedulerSkipReason.None;
        _tickRealDeltaTime = realDeltaTime;
        _tickSimulatedSeconds = simDelta > 0.0 && double.IsFinite(simDelta)
            ? simDelta
            : 0.0;
        _tickProcessedSimulationSeconds = 0.0;
        _tickBudgetLimited = false;
        _tickEffectiveStepCap = 0.0;
        _tickOuterSubsteps = 0;
        _tickFullPhysicsDispatches = 0;
        _tickOnRailsDispatches = 0;
        _tickSurfaceSettledDispatches = 0;
        _tickGroundHeldDispatches = 0;
        _tickDestroyedDispatches = 0;
        _tickDockedSecondarySkips = 0;
        _tickRailsSlices = 0;
        _tickDockingConstraintApplications = 0;
        _tickDeadlineEligibleEvaluations = 0;
        _tickDeadlineDeferredSkips = 0;
        _tickDeadlineCatchUpDispatches = 0;
        _tickDeadlineProjectedDispatches = 0;
        _tickCandidateDeferredSkips = 0;
    }

    private void PublishSchedulerTelemetry()
    {
        double wallClockMilliseconds =
            (Stopwatch.GetTimestamp() - _tickStartTimestamp) * 1000.0 / Stopwatch.Frequency;
        if (!double.IsFinite(wallClockMilliseconds) || wallClockMilliseconds < 0.0)
            wallClockMilliseconds = 0.0;

        LastSchedulerTelemetry = new PhysicsSchedulerTelemetry(
            _tickBranch,
            _tickRealDeltaTime,
            _tickSimulatedSeconds,
            _tickEffectiveStepCap,
            _tickOuterSubsteps,
            _tickFullPhysicsDispatches,
            _tickOnRailsDispatches,
            _tickSurfaceSettledDispatches,
            _tickGroundHeldDispatches,
            _tickDestroyedDispatches,
            _tickDockedSecondarySkips,
            _tickRailsSlices,
            _tickDockingConstraintApplications,
            _tickDeadlineEligibleEvaluations,
            _tickDeadlineDeferredSkips,
            _tickDeadlineCatchUpDispatches,
            _tickDeadlineProjectedDispatches,
            wallClockMilliseconds,
            _tickOuterSubsteps >= CatchUpWarningSubsteps)
        {
            IsInitialized = true,
            SkipReason = _tickSkipReason,
            RequestedSimulationSeconds = _tickSimulatedSeconds,
            ProcessedSimulationSeconds = _tickProcessedSimulationSeconds,
            PendingSimulationSeconds = _pendingSimulationSeconds,
            BudgetLimited = _tickBudgetLimited,
            BudgetReason = _tickBudgetLimited
                ? PhysicsSchedulerBudgetReason.SubstepLimit
                : SchedulerBudgetEnabled
                    ? PhysicsSchedulerBudgetReason.None
                    : PhysicsSchedulerBudgetReason.Disabled,
            CandidateDeferredSkips = _tickCandidateDeferredSkips,
        };
    }

    private void RecordWorkload(VesselPhysicsWorkload workload)
    {
        switch (workload)
        {
            case VesselPhysicsWorkload.FullPhysics:
                _tickFullPhysicsDispatches++;
                break;
            case VesselPhysicsWorkload.OnRails:
                _tickOnRailsDispatches++;
                break;
            case VesselPhysicsWorkload.SurfaceSettled:
                _tickSurfaceSettledDispatches++;
                break;
            case VesselPhysicsWorkload.GroundHeld:
                _tickGroundHeldDispatches++;
                break;
            case VesselPhysicsWorkload.Destroyed:
                _tickDestroyedDispatches++;
                break;
        }
    }

    /// <summary>
    /// Returns the only deadline plan currently permitted by the scheduler. A plan is
    /// eligible only for a finite, force-free, non-active, non-docked vessel whose conic
    /// stays outside the modeled atmosphere/contact corridor. The plan contains no saved
    /// state; the caller must materialize the vessel before any other branch can mutate it.
    /// </summary>
    public PhysicsSchedulerDeadlinePlan GetPhysicsSchedulerDeadlinePlan(Vessel vessel)
    {
        ArgumentNullException.ThrowIfNull(vessel);

        if (ReferenceEquals(vessel, ActiveVessel))
            return new(false, 0.0, PhysicsSchedulerDeadlineReason.ActiveVessel);
        if (IsDockedSecondary(vessel))
            return new(false, 0.0, PhysicsSchedulerDeadlineReason.DockedSecondary);
        if (vessel.IsDestroyed)
            return new(false, 0.0, PhysicsSchedulerDeadlineReason.Destroyed);
        if (vessel.IsSurfaceSettled)
            return new(false, 0.0, PhysicsSchedulerDeadlineReason.SurfaceSettled);
        if (vessel.IsGroundHeld)
            return new(false, 0.0, PhysicsSchedulerDeadlineReason.GroundHeld);
        if (!IsFinitePosition(vessel.Position) || !IsFinitePosition(vessel.Velocity))
            return new(false, 0.0, PhysicsSchedulerDeadlineReason.InvalidState);

        if (ClassifyMixedPhysicsWorkload(vessel) != VesselPhysicsWorkload.OnRails)
            return new(false, 0.0, PhysicsSchedulerDeadlineReason.ForceSensitive);
        if (vessel.OrbitalState is not { } orbit)
            return new(false, 0.0, PhysicsSchedulerDeadlineReason.MissingOrbit);
        if (!double.IsFinite(orbit.SemiMajorAxis)
            || !double.IsFinite(orbit.Eccentricity)
            || !double.IsFinite(orbit.Periapsis))
            return new(false, 0.0, PhysicsSchedulerDeadlineReason.InvalidState);

        var body = GetBody(orbit.ReferenceBodyId);
        if (body is null)
            return new(false, 0.0, PhysicsSchedulerDeadlineReason.InvalidState);

        double protectedRadius = body.Radius
            + (body.Atmosphere?.MaxAltitude * 1.05 ?? 1_000.0);
        if (orbit.IsRadial || orbit.Periapsis <= protectedRadius)
            return new(false, 0.0, PhysicsSchedulerDeadlineReason.PeriapsisEvent);

        return new(true, MaxCoastStep, PhysicsSchedulerDeadlineReason.DeferredRails);
    }

    private void FlushDeferredRailsToCurrentTime()
    {
        for (int i = 0, count = _vessels.Count; i < count; i++)
        {
            var vessel = _vessels[i];
            if (IsDockedSecondary(vessel) || vessel.IsDestroyed)
                continue;
            CatchUpDeferredRailVessel(vessel, CurrentTime);
        }
    }

    /// <summary>
    /// Restores the last event-safe conic state of a deferred vessel.  During a skipped
    /// deadline the public position/velocity are projected to the current epoch for
    /// rendering and navigation, while the orbital elements remain anchored at the last
    /// event-check epoch.  A wake-up must restore that anchored state before running the
    /// full rails propagator; otherwise the propagator would interpret a current-epoch
    /// position as if it belonged to an older conic epoch and introduce a phase jump.
    /// </summary>
    private bool RestoreDeferredRailStateAtTime(Vessel vessel, double time)
    {
        if (vessel.OrbitalState is not { } orbit)
            return false;

        var reference = GetBody(orbit.ReferenceBodyId);
        if (reference is null || !double.IsFinite(reference.GM) || reference.GM <= 0.0)
            return false;

        try
        {
            var (relativePosition, relativeVelocity) =
                KeplerPropagator.PropagateToTime(orbit, time, reference.GM);
            var (referencePosition, referenceVelocity) = BodyStateAt(reference, time);
            Vector3d position = referencePosition + relativePosition;
            Vector3d velocity = referenceVelocity + relativeVelocity;
            if (!IsFinitePosition(position) || !IsFinitePosition(velocity))
                return false;

            vessel.Position = position;
            vessel.Velocity = velocity;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Projects a deferred conic to the current public epoch without changing its event
    /// anchor.  This is the cheap path: no atmosphere, SOI or surface scan is performed.
    /// The projection is accepted only while the reference body remains dominant and the
    /// conic is non-radial; otherwise the caller must materialize the vessel immediately.
    /// </summary>
    private bool ProjectDeferredRailsVesselToTime(Vessel vessel, double targetTime)
    {
        if (vessel.OrbitalState is not { } orbit || orbit.IsRadial)
            return false;

        var reference = GetBody(orbit.ReferenceBodyId);
        if (reference is null || !double.IsFinite(reference.GM) || reference.GM <= 0.0)
            return false;

        try
        {
            var (relativePosition, relativeVelocity) =
                KeplerPropagator.PropagateToTime(orbit, targetTime, reference.GM);
            var (referencePosition, referenceVelocity) = BodyStateAt(reference, targetTime);
            Vector3d position = referencePosition + relativePosition;
            Vector3d velocity = referenceVelocity + relativeVelocity;
            if (!IsFinitePosition(position) || !IsFinitePosition(velocity))
                return false;

            // Check an interior sample as well as the endpoint.  A high-speed conic can
            // cross a small sphere of influence and return to the original body before
            // the deadline ends; endpoint-only validation would miss that event.
            double midpointTime = CurrentTime + (targetTime - CurrentTime) * 0.5;
            var (midpointRelativePosition, _) =
                KeplerPropagator.PropagateToTime(orbit, midpointTime, reference.GM);
            var (midpointReferencePosition, _) = BodyStateAt(reference, midpointTime);
            Vector3d midpointPosition = midpointReferencePosition + midpointRelativePosition;
            if (!IsFinitePosition(midpointPosition)
                || GetDominantBodyAt(midpointPosition, midpointTime).Id != reference.Id
                || GetDominantBodyAt(position, targetTime).Id != reference.Id)
                return false;

            vessel.Position = position;
            vessel.Velocity = velocity;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private bool CatchUpDeferredRailVessel(Vessel vessel, double targetTime)
    {
        if (!_lastDeferredRailUpdate.TryGetValue(vessel.Id, out double lastTime))
            return false;
        if (lastTime >= targetTime - 1e-12)
            return false;

        if (!RestoreDeferredRailStateAtTime(vessel, lastTime))
        {
            // The deferred state is no longer trustworthy.  Drop the schedule and let
            // the caller use the conservative current-step fallback rather than trying
            // to integrate a projected state against an old epoch.
            _lastDeferredRailUpdate.Remove(vessel.Id);
            _nextDeferredRailDeadline.Remove(vessel.Id);
            return false;
        }

        PropagateVesselOnRails(vessel, lastTime, targetTime);
        _lastDeferredRailUpdate[vessel.Id] = targetTime;
        _nextDeferredRailDeadline.Remove(vessel.Id);
        _tickDeadlineCatchUpDispatches++;
        _tickOnRailsDispatches++;
        return true;
    }

    /// <summary>
    /// Selects the safe global slice for the mixed branch. Every vessel that will enter
    /// <see cref="IntegrateVesselOffRails"/> participates in this decision; using only
    /// <see cref="ActiveVessel"/> would allow a secondary atmospheric vessel to receive
    /// <see cref="MaxCoastStep"/> while the active vessel was coasting on rails.
    /// </summary>
    private double GetMixedPhysicsStepCap(bool anyContactSensitive)
    {
        double cap = anyContactSensitive ? MaxContactStep : MaxCoastStep;

        foreach (var vessel in _vessels)
        {
            if (IsDockedSecondary(vessel)
                || vessel.IsDestroyed
                || vessel.IsGroundHeld
                || vessel.IsSurfaceSettled && !HasWakeCommand(vessel))
                continue;

            if (!RequiresOffRailsPhysics(vessel))
                continue;

            bool thrusting = HasWakeCommand(vessel);
            double vesselCap = thrusting ? MaxThrustStep : MaxPhysicsStep;
            cap = System.Math.Min(cap, vesselCap);

            // No smaller cap exists in this scheduler. Avoid rescanning a large fleet
            // once the landing/contact cadence is already the limiting factor.
            if (cap <= MaxContactStep)
                break;
        }

        return cap;
    }

    /// <summary>
    /// True when analytic Kepler rails would discard a force or event that materially changes
    /// the outcome: thrust/spool, atmospheric loads or heating, ground proximity/contact.
    /// </summary>
    public bool RequiresOffRailsPhysics(Vessel vessel)
    {
        if (vessel.IsDestroyed) return false;
        if (!HasFinitePhysicalState(vessel)) return true;
        if (vessel.IsGroundHeld || HasWakeCommand(vessel))
            return true;
        if (_bodies.Count == 0) return false;

        var body = GetDominantBody(vessel.Position);
        double altitude = body.GetAltitude(vessel.Position);
        if (altitude < 1_000.0) return true; // airless landing/contact corridor too
        if (body.Atmosphere == null) return false;

        double density = body.GetAtmosphericDensity(vessel.Position);
        if (density <= 0.0) return false;

        // Residual thermosphere (R7) above MaxAltitude still exerts drag. Analytic rails
        // ignore that force, so low LEO would become immortal under warp ≥ 10. Keep RK4
        // while any modeled density remains below ThermosphereTopAltitude.
        double thermoTop = body.Atmosphere.ThermosphereTopAltitude;
        if (thermoTop > body.Atmosphere.MaxAltitude && altitude < thermoTop)
            return true;

        double speed = vessel.GetSurfaceVelocity(body).Magnitude;
        double q = 0.5 * density * speed * speed;
        double heatFlux = Physics.ThermalModel.ComputeHeatFlux(
            density, speed, System.Math.Max(0.1, vessel.MaximumDiameter * 0.5));
        return altitude <= body.Atmosphere.MaxAltitude * 1.05
            || q >= 0.5
            || heatFlux >= 500.0;
    }

    private static bool HasActiveEngineAboveThrottle(Vessel vessel, double threshold)
    {
        var engines = vessel.Parts.ActiveEngineList;
        for (int i = 0; i < engines.Count; i++)
        {
            if (engines[i].ThrottleLevel > threshold)
                return true;
        }

        return false;
    }

    private static bool HasWakeCommand(Vessel vessel) =>
        vessel.Throttle > 1e-3
        || HasActiveEngineAboveThrottle(vessel, 1e-3)
        || HasAttitudeCommand(vessel);

    /// <summary>
    /// A non-zero pilot/autopilot attitude command is a wake condition even when the
    /// throttle is closed. Treating it as idle would let a future rail/deferred path
    /// discard TVC/RCS torque and is especially dangerous immediately after a navigation
    /// jump or during a powered attitude correction.
    /// </summary>
    private static bool HasAttitudeCommand(Vessel vessel) =>
        vessel.PitchYawRoll.MagnitudeSquared > 1e-6;

    private static bool HasFinitePhysicalState(Vessel vessel) =>
        IsFinitePosition(vessel.Position)
        && IsFinitePosition(vessel.Velocity)
        && IsFinitePosition(vessel.AngularVelocity)
        && IsFinitePosition(vessel.PitchYawRoll)
        && double.IsFinite(vessel.Throttle)
        && vessel.Throttle >= 0.0
        && vessel.Throttle <= 1.0
        && double.IsFinite(vessel.Orientation.W)
        && double.IsFinite(vessel.Orientation.X)
        && double.IsFinite(vessel.Orientation.Y)
        && double.IsFinite(vessel.Orientation.Z)
        && vessel.Orientation.NormSquared > 1e-24;

    private static void WakeVesselFromRails(Vessel vessel)
    {
        vessel.IsOnRails = false;
        vessel.OrbitalState = null;
    }

    /// <summary>
    /// Also guards a coasting conic whose periapsis will enter the atmosphere. It may stay
    /// on rails while high, but warp must use bounded slices so the atmosphere boundary is
    /// detected instead of jumping across the entire entry in one analytic step.
    /// </summary>
    public bool RequiresBoundedWarpPropagation(Vessel vessel)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        // Preserve the cheap settled/destroyed short-circuit for callers that only need
        // bounded-entry state.  The combined bridge query still evaluates force sensitivity
        // separately because that value controls the user-visible warp cap.
        if (vessel.IsDestroyed || vessel.IsSurfaceSettled)
            return false;
        return RequiresBoundedWarpPropagation(vessel, RequiresOffRailsPhysics(vessel));
    }

    /// <summary>
    /// Evaluates both warp-safety decisions from one vessel snapshot.  The Godot bridge
    /// needs both values while selecting the user-visible warp limit; keeping this as a
    /// single API avoids recomputing atmospheric density, heating and engine activity in
    /// the same frame.  It deliberately does not cache the result: position, throttle and
    /// vessel state may change between frames.
    /// </summary>
    public (bool ForceSensitive, bool BoundedEntry) GetWarpPhysicsRequirements(Vessel vessel)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        bool forceSensitive = RequiresOffRailsPhysics(vessel);
        bool boundedEntry = RequiresBoundedWarpPropagation(vessel, forceSensitive);
        return (forceSensitive, boundedEntry);
    }

    private bool RequiresBoundedWarpPropagation(Vessel vessel, bool forceSensitive)
    {
        if (vessel.IsDestroyed || vessel.IsSurfaceSettled)
            return false;
        if (forceSensitive) return true;
        if (_bodies.Count == 0 || vessel.IsDestroyed) return false;
        var body = GetDominantBody(vessel.Position);
        if (body.Atmosphere == null) return false;

        try
        {
            var state = vessel.OrbitalState ?? OrbitalElements.FromStateVector(
                vessel.Position - body.Position,
                vessel.Velocity - body.Velocity,
                body.GM,
                body.Id,
                CurrentTime);
            return state.Periapsis <= body.Radius + body.Atmosphere.MaxAltitude * 1.05;
        }
        catch (ArgumentException)
        {
            return true; // degenerate/suborbital state: choose bounded physics safely
        }
    }

    /// <summary>
    /// Classifies a vessel into the deterministic active/nearby/on-rails/hibernated
    /// policy used by the performance roadmap.
    /// </summary>
    /// <remarks>
    /// Precedence is intentionally fixed and independent of vessel-list order:
    /// destroyed vessels are hibernated, the active vessel is active, force-sensitive
    /// or spatially close vessels are nearby, explicit analytic-rail vessels are
    /// on-rails, and only distant coasting vessels at warp are hibernation candidates.
    /// At real-time/low warp a distant vessel remains <see cref="Nearby"/> because the
    /// existing scheduler still evaluates full physics for that regime.
    ///
    /// This method has no side effects.  It does not set <see cref="Vessel.IsOnRails"/>,
    /// mutate orbital elements, or skip a tick; those changes require a separately
    /// validated wake-up/temporal-state implementation.
    /// </remarks>
    public VesselSimulationTier ClassifySimulationTier(Vessel vessel)
    {
        ArgumentNullException.ThrowIfNull(vessel);

        if (vessel.IsDestroyed)
            return VesselSimulationTier.Hibernated;

        if (ReferenceEquals(vessel, ActiveVessel))
            return VesselSimulationTier.Active;

        // Unsafe-to-rail conditions take precedence over distance and explicit rail
        // state so atmospheric/contact/thrust events cannot be hidden by tiering.
        if (RequiresOffRailsPhysics(vessel))
            return VesselSimulationTier.Nearby;

        // Never classify a numerically corrupted state as a deferred/analytic
        // candidate.  Nearby is the fail-safe tier until a higher-level invariant
        // checker reports the non-finite state.
        if (!IsFinitePosition(vessel.Position)
            || (ActiveVessel is not null && !IsFinitePosition(ActiveVessel.Position)))
        {
            return VesselSimulationTier.Nearby;
        }

        if (ActiveVessel is not null
            && (vessel.Position - ActiveVessel.Position).Magnitude
                <= NearbyVesselDistance)
        {
            return VesselSimulationTier.Nearby;
        }

        if (vessel.IsOnRails)
            return VesselSimulationTier.OnRails;

        if (TimeScale > 4.0
            && ActiveVessel is not null
            && IsFinitePosition(vessel.Position)
            && IsFinitePosition(ActiveVessel.Position)
            && (vessel.Position - ActiveVessel.Position).Magnitude
                >= HibernatedVesselDistance)
        {
            return VesselSimulationTier.Hibernated;
        }

        // At low warp, preserve the current full-physics contract.  At high warp a
        // non-active vessel takes the existing analytic path even if it has not yet
        // had IsOnRails set by the mixed dispatcher; expose that as OnRails rather
        // than calling it hibernated unless it is clearly distant.
        return TimeScale > 4.0
            ? VesselSimulationTier.OnRails
            : VesselSimulationTier.Nearby;
    }

    /// <summary>
    /// Builds the phase-45 interest decision from the authoritative vessel state. This is
    /// intentionally observational: it does not change rails, advance time, or skip work.
    /// The scheduler may consume this decision only after the event/deadline parity gate is
    /// promoted; until then <see cref="SimulationInterestPolicy.EnabledByDefault"/> remains
    /// false and the existing dispatch path is authoritative.
    /// </summary>
    public SimulationInterestDecision GetSimulationInterestDecision(Vessel vessel)
        => GetSimulationInterestDecision(vessel, SimulationExternalInterestInputs.None);

    /// <summary>
    /// Builds the same observational decision while applying a game-layer snapshot for
    /// mission callbacks and vehicle-system alerts. The snapshot is consumed only for
    /// classification; this method still never advances time, changes rails, or skips a
    /// physics tick.
    /// </summary>
    public SimulationInterestDecision GetSimulationInterestDecision(
        Vessel vessel,
        SimulationExternalInterestInputs externalInputs)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        if (_bodies.Count == 0
            || !HasFinitePhysicalState(vessel)
            || (ActiveVessel is not null && !HasFinitePhysicalState(ActiveVessel)))
        {
            return new(
                SimulationInterestTier.Active,
                SimulationWakeReason.InvalidInput);
        }

        bool isDocked = false;
        for (int i = 0; i < _dockingConnections.Count; i++)
        {
            var connection = _dockingConnections[i];
            if (connection.PrimaryVesselId == vessel.Id
                || connection.SecondaryVesselId == vessel.Id)
            {
                isDocked = true;
                break;
            }
        }

        bool wakeCommand = HasWakeCommand(vessel);
        bool forceSensitive = RequiresOffRailsPhysics(vessel);
        PhysicsSchedulerDeadlinePlan deadline = GetPhysicsSchedulerDeadlinePlan(vessel);
        // DeferredRails.IntervalSeconds is the scheduler's bounded projection cadence,
        // not a physical SOI/deadline event. Passing it into the interest policy would
        // wake every coasting vessel on every query because the default event window is
        // 60 s. Only a future physical event may populate SecondsUntilNextDeadline;
        // the current deadline planner exposes that event through PeriapsisEvent below.
        double? secondsUntilDeadline = null;
        bool soiOrDeadline = deadline.Reason is
            PhysicsSchedulerDeadlineReason.PeriapsisEvent;
        double? distanceToActive = ActiveVessel is { } active
            && !ReferenceEquals(active, vessel)
            ? (vessel.Position - active.Position).Magnitude
            : null;

        var inputs = new SimulationInterestInputs(
            IsActiveVessel: ReferenceEquals(vessel, ActiveVessel),
            IsPilotControlled: ReferenceEquals(vessel, ActiveVessel),
            IsMissionControlled: false,
            IsSelected: ReferenceEquals(vessel, ActiveVessel),
            HasThrust: wakeCommand,
            HasPendingCommand: wakeCommand,
            HasDockingOrContact: isDocked
                || vessel.HasSurfaceContact
                || vessel.IsAttemptingTowerCatch,
            IsAtmosphereOrReentry: forceSensitive
                || vessel.IsAttemptingTowerCatch,
            HasPendingSoiTransition: soiOrDeadline,
            SecondsUntilNextDeadline: secondsUntilDeadline,
            IsMissionCriticalState: vessel.StructuralControlLost
                || vessel.IsCaught
                || vessel.IsAttemptingTowerCatch,
            DistanceToActiveVesselM: distanceToActive,
            DistanceToInteractionM: vessel.IsAttemptingTowerCatch ? 0.0 : null);

        return SimulationInterestPolicy.Classify(inputs, externalInputs);
    }

    private static bool IsFinitePosition(Vector3d position) =>
        double.IsFinite(position.X)
        && double.IsFinite(position.Y)
        && double.IsFinite(position.Z);

    /// <summary>
    /// Classifies the work the mixed/high-warp scheduler should dispatch for the
    /// vessel's current state.  A vessel that is already on rails is classified as
    /// analytic only when the same conditions that would keep it on rails are true.
    /// In particular, an active vessel below warp 10 remains <see
    /// cref="VesselPhysicsWorkload.FullPhysics"/> so the existing active-vessel
    /// transition to RK4 is preserved.
    /// </summary>
    public VesselPhysicsWorkload ClassifyMixedPhysicsWorkload(Vessel vessel)
        => ClassifyMixedPhysicsWorkload(vessel, out _);

    private VesselPhysicsWorkload ClassifyMixedPhysicsWorkload(
        Vessel vessel,
        out bool requiresForces)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        requiresForces = false;

        // Match the dispatch precedence in TickPhysicsMixed exactly: a destroyed
        // vessel is never woken by a stale settled/rails flag.
        if (vessel.IsDestroyed)
            return VesselPhysicsWorkload.Destroyed;

        // A settled vessel with a command must wake the solver.  The idle path
        // remains an anchored update, not a rigid-body/RK4 update.
        if (vessel.IsSurfaceSettled && !HasWakeCommand(vessel))
            return VesselPhysicsWorkload.SurfaceSettled;

        if (vessel.IsGroundHeld)
            return VesselPhysicsWorkload.GroundHeld;

        if (!HasFinitePhysicalState(vessel))
        {
            requiresForces = true;
            return VesselPhysicsWorkload.FullPhysics;
        }

        requiresForces = RequiresOffRailsPhysics(vessel);

        // At TimeScale 5..9 the mixed branch deliberately does not promote the
        // active vessel to rails.  Existing rails state must therefore be cleared
        // and integrated by RK4 just as before this classification was introduced.
        bool canUseAnalyticPropagation = vessel != ActiveVessel || TimeScale >= 10.0;
        if (canUseAnalyticPropagation && !requiresForces)
            return VesselPhysicsWorkload.OnRails;

        return VesselPhysicsWorkload.FullPhysics;
    }

    // ── Integration modes ─────────────────────────────────────────────────

    private void TickPhysics(double dt)
    {
        // 1. Propagate celestial bodies on Keplerian rails
        KeplerPropagator.PropagateAllBodies(
            _bodies,
            CurrentTime + dt,
            _bodyPropagationWorkspace);

        // 2. Integrate each active vessel with RK4.
        // Snapshot the count: structural breakup may append debris mid-loop. New
        // vessels intentionally wait for the next scheduler tick, matching the
        // previous ToList snapshot without allocating per substep.
        for (int i = 0, count = _vessels.Count; i < count; i++)
        {
            var vessel = _vessels[i];
            if (IsDockedSecondary(vessel))
            {
                _tickDockedSecondarySkips++;
                continue;
            }
            if (vessel.IsDestroyed)
            {
                RecordWorkload(VesselPhysicsWorkload.Destroyed);
                AdvanceAnchoredWreck(vessel, dt);
                continue;
            }

            if (vessel.IsSurfaceSettled)
            {
                var settledBody = ResolveSurfaceBody(vessel);
                if (HasWakeCommand(vessel))
                {
                    vessel.IsSurfaceSettled = false;
                    vessel.SurfaceSettledDuration = 0.0;
                }
                else
                {
                    RecordWorkload(VesselPhysicsWorkload.SurfaceSettled);
                    AdvanceSurfaceAnchor(vessel, settledBody, dt);
                    vessel.Tick(dt, settledBody);
                    continue;
                }
            }

            if (vessel.IsGroundHeld)
            {
                RecordWorkload(VesselPhysicsWorkload.GroundHeld);
                // Vessel is clamped to the body surface — follow the body's orbit
                var heldBody = GetDominantBody(vessel.Position);
                AdvanceGroundHoldFrame(vessel, heldBody, dt);
                vessel.Position = heldBody.Position
                    + vessel.GroundNormal
                        * (heldBody.Radius + vessel.GroundOffset);
                vessel.Velocity = heldBody.Velocity
                    + heldBody.GetSurfaceVelocity(vessel.Position);
                vessel.Tick(dt, heldBody);
                continue;
            }

            if (vessel.IsOnRails)
            {
                if (RequiresOffRailsPhysics(vessel))
                {
                    WakeVesselFromRails(vessel);
                }
                else
                {
                    RecordWorkload(VesselPhysicsWorkload.OnRails);
                    PropagateVesselOnRails(vessel, dt);
                    continue;
                }
            }

            var refBody = GetDominantBody(vessel.Position);
            RecordWorkload(VesselPhysicsWorkload.FullPhysics);
            IntegrateVesselOffRails(vessel, refBody, dt);
        }
        ApplyDockingConstraints();
    }

    private static void AdvanceGroundHoldFrame(Vessel vessel, CelestialBody body, double dt)
    {
        if (body.AngularSpeed == 0.0 || dt <= 0.0) return;
        var rotation = Math.Quaterniond.FromAxisAngle(
            body.RotationAxis, body.AngularSpeed * dt);
        vessel.GroundNormal = rotation.Rotate(vessel.GroundNormal).Normalized;
        vessel.Orientation = (rotation * vessel.Orientation).Normalize();
    }

    private CelestialBody ResolveSurfaceBody(Vessel vessel) =>
        vessel.ReferenceBodyId is { Length: > 0 } bodyId
            && GetBody(bodyId) is { } referenced
                ? referenced
                : GetDominantBody(vessel.Position);

    private static void AdvanceSurfaceAnchor(
        Vessel vessel,
        CelestialBody body,
        double dt)
    {
        AdvanceGroundHoldFrame(vessel, body, dt);
        vessel.Position = body.Position
            + vessel.GroundNormal * (body.Radius + vessel.GroundOffset);
        vessel.Velocity = body.Velocity
            + body.GetSurfaceVelocity(vessel.Position);
        vessel.AngularVelocity = Vector3d.Zero;
        vessel.PitchYawRoll = Vector3d.Zero;
        vessel.IsOnRails = false;
        vessel.OrbitalState = null;
    }

    private void AdvanceAnchoredWreck(Vessel vessel, double dt)
    {
        if (!vessel.IsSurfaceSettled
            || vessel.DestructionCause != VesselDestructionCause.GroundImpact)
            return;
        AdvanceSurfaceAnchor(vessel, ResolveSurfaceBody(vessel), dt);
    }

    private static void AnchorToSurface(
        Vessel vessel,
        CelestialBody body,
        Vector3d direction,
        double offsetM)
    {
        vessel.GroundNormal = direction.MagnitudeSquared > 1e-12
            ? direction.Normalized
            : Vector3d.Up;
        vessel.GroundOffset = System.Math.Max(0.0, offsetM);
        vessel.ReferenceBodyId = body.Id;
        vessel.IsSurfaceSettled = true;
        vessel.SurfaceSettledDuration = 0.5;
        vessel.Position = body.Position
            + vessel.GroundNormal * (body.Radius + vessel.GroundOffset);
        vessel.Velocity = body.Velocity
            + body.GetSurfaceVelocity(vessel.Position);
        vessel.AngularVelocity = Vector3d.Zero;
        vessel.PitchYawRoll = Vector3d.Zero;
        vessel.IsOnRails = false;
        vessel.OrbitalState = null;
    }

    private void ApplyPostIntegrationPhysics(Vessel vessel, CelestialBody refBody, double dt)
    {
        var netAccel  = vessel.ComputeNetAcceleration(_bodies, refBody);
        var gravAccel = vessel.ComputeGravity(_bodies);
        var contactAccel = vessel.TotalMass > 0.0
            ? vessel.LastContactForceWorld / vessel.TotalMass
            : Vector3d.Zero;
        var nonGrav   = netAccel - gravAccel + contactAccel;
        Physics.StressSolver.ComputeLoads(vessel.Parts, nonGrav, vessel.Orientation);
        TryStructuralBreakup(vessel, nonGrav.Magnitude);

        double density = refBody.GetAtmosphericDensity(vessel.Position);
        if (density > 0.0 && !vessel.IsGroundHeld)
        {
            var surfVel = vessel.GetSurfaceVelocity(refBody);
            double airspeed = surfVel.Magnitude;
            double heatFlux = vessel.ComputeStagnationHeatFlux(density, surfVel);
            var flowDirLocal = airspeed > 1e-6
                ? vessel.Orientation.Inverse().Rotate(surfVel.Normalized)
                : Vector3d.Zero;
            var burned = Physics.StressSolver.ApplyThermalLoads(
                vessel.Parts, heatFlux, dt, flowDirLocal);
            if (burned.Count > 0 && !vessel.IsDestroyed)
                TryThermalBreakup(vessel, burned, airspeed);
        }

        HandleSurfaceImpact(vessel, refBody);
    }

    /// <summary>
    /// Sheds a thermally failed non-root subtree as debris. A burned command/root part still
    /// destroys the vessel, but an expendable external package no longer kills an intact
    /// protected capsule merely because both share one rigid-body graph.
    /// </summary>
    private void TryThermalBreakup(
        Vessel vessel,
        IReadOnlyCollection<Parts.Part> burned,
        double airspeed)
    {
        var root = vessel.Parts.Root;
        if (root == null || burned.Contains(root))
        {
            MarkThermallyDestroyed(vessel, airspeed);
            return;
        }

        var failed = burned.FirstOrDefault(part =>
            vessel.Parts.Joints.Any(joint => joint.Child == part));
        if (failed == null)
        {
            MarkThermallyDestroyed(vessel, airspeed);
            return;
        }

        var joint = vessel.Parts.Joints.First(candidate =>
            candidate.Child == failed);
        var debris = vessel.BreakAtJoint(joint);
        if (debris == null)
        {
            MarkThermallyDestroyed(vessel, airspeed);
            return;
        }

        AddVessel(debris);
        _pendingStructuralDebris.Add(debris);
    }

    private static void MarkThermallyDestroyed(Vessel vessel, double airspeed)
    {
        vessel.IsDestroyed = true;
        vessel.DestructionCause = VesselDestructionCause.ThermalBreakup;
        vessel.CrashImpactSpeed = airspeed;
        vessel.CrashSimPosition = vessel.Position;
    }

    /// <summary>
    /// Split at most one overloaded joint per tick (highest load ratio first). Detaches the
    /// child subtree as debris; only marks the parent vessel destroyed if it loses its root
    /// or all parts.
    /// </summary>
    private void TryStructuralBreakup(Vessel vessel, double nonGravAccelMagnitude)
    {
        if (vessel.IsDestroyed || vessel.Parts.Joints.Count == 0) return;

        var breaking = Physics.StressSolver.FindBreakingJoints(vessel.Parts)
            .OrderByDescending(OverloadRatio)
            .ToList();
        if (breaking.Count == 0) return;

        var joint = breaking[0];
        var debris = vessel.BreakAtJoint(joint);
        if (debris == null) return;

        AddVessel(debris);
        _pendingStructuralDebris.Add(debris);

        bool lostRoot = vessel.Parts.Root == null
            || vessel.Parts.Parts.Count == 0
            || !vessel.Parts.Parts.Contains(vessel.Parts.Root);
        if (lostRoot)
        {
            vessel.IsDestroyed = true;
            vessel.DestructionCause = VesselDestructionCause.StructuralBreakup;
            vessel.CrashImpactSpeed = System.Math.Max(vessel.CrashImpactSpeed, nonGravAccelMagnitude);
            vessel.CrashSimPosition = vessel.Position;
        }
    }

    private static double OverloadRatio(Parts.Joint joint)
    {
        double tensile = joint.TensileStrength > 0.0
            ? joint.CurrentTensileLoad / joint.TensileStrength
            : 0.0;
        double shear = joint.ShearStrength > 0.0
            ? joint.CurrentShearLoad / joint.ShearStrength
            : 0.0;
        return System.Math.Max(tensile, shear);
    }

    private static void HandleSurfaceImpact(Vessel vessel, CelestialBody refBody)
    {
        if (refBody.GetAltitude(vessel.Position) >= 0.0) return;

        double impactSpeed = vessel.GetSurfaceVelocity(refBody).Magnitude;
        double splashdownLimit = vessel.MaximumSplashdownSpeedMps;
        bool softLanding = vessel.IsGroundHeld
            || impactSpeed <= SoftLandingThreshold
            || refBody.Id == "earth"
                && splashdownLimit > 0.0
                && impactSpeed <= splashdownLimit;
        var dir = (vessel.Position - refBody.Position).Normalized;
        if (softLanding)
        {
            AnchorToSurface(vessel, refBody, dir, 1.0);
        }
        else
        {
            vessel.IsDestroyed      = true;
            vessel.DestructionCause = VesselDestructionCause.GroundImpact;
            vessel.CrashImpactSpeed = impactSpeed;
            vessel.CrashSimPosition = vessel.Position;
            AnchorToSurface(vessel, refBody, dir, 0.5);
        }
    }

    private void TickPhysicsMixed(double dt)
    {
        // All celestial bodies on rails
        KeplerPropagator.PropagateAllBodies(
            _bodies,
            CurrentTime + dt,
            _bodyPropagationWorkspace);
        double targetTime = CurrentTime + dt;

        // Structural breakup may append debris; preserve the old snapshot semantics
        // by capturing the starting count while avoiding a list allocation per substep.
        for (int i = 0, count = _vessels.Count; i < count; i++)
        {
            var vessel = _vessels[i];
            if (IsDockedSecondary(vessel))
            {
                _tickDockedSecondarySkips++;
                continue;
            }
            var workload = ClassifyMixedPhysicsWorkload(vessel, out bool requiresForces);
            if (workload == VesselPhysicsWorkload.Destroyed)
            {
                RecordWorkload(workload);
                AdvanceAnchoredWreck(vessel, dt);
                continue;
            }

            // A deferred rails vessel may be behind the global epoch when a command or
            // another event makes it non-rails. Materialize it at CurrentTime before
            // handing it to RK4/surface logic; integrating stale coordinates would be a
            // temporal teleport, not an optimization.
            if (workload != VesselPhysicsWorkload.OnRails)
                CatchUpDeferredRailVessel(vessel, CurrentTime);

            // Rails propagation does not need the dominant body selected here:
            // PropagateVesselOnRails resolves its own reference frame at the conic
            // epoch, and this avoids one duplicate body scan per rails vessel/substep.
            if (workload == VesselPhysicsWorkload.OnRails)
            {
                // Only the active vessel changes its explicit rails flag in this
                // branch.  Non-active vessels historically entered the analytic
                // propagator without changing that flag, so preserve that contract.
                if (vessel == ActiveVessel && !vessel.IsOnRails)
                {
                    vessel.IsOnRails = true;
                    vessel.OrbitalState = null;
                }

                PhysicsSchedulerDeadlinePlan deadline =
                    GetPhysicsSchedulerDeadlinePlan(vessel);
                if (deadline.CanDefer)
                {
                    _tickDeadlineEligibleEvaluations++;
                    if (TrySkipDeferredCandidate(
                        vessel, targetTime, deadline))
                    {
                        continue;
                    }
                    bool hasLastUpdate = _lastDeferredRailUpdate.TryGetValue(
                        vessel.Id, out double lastUpdate);
                    if (!hasLastUpdate)
                        lastUpdate = CurrentTime;
                    bool due = !hasLastUpdate
                        || !_nextDeferredRailDeadline.TryGetValue(
                            vessel.Id, out double nextDeadline)
                        || targetTime >= nextDeadline - 1e-12;
                    if (!due)
                    {
                        if (ProjectDeferredRailsVesselToTime(vessel, targetTime))
                        {
                            _tickDeadlineDeferredSkips++;
                            _tickDeadlineProjectedDispatches++;
                            RecordWorkload(workload);
                            continue;
                        }

                        // A cheap projection detected an event boundary (or a numerical
                        // problem).  Materialize from the last safe epoch now; this is
                        // the conservative fallback for an independent deadline.
                        if (CatchUpDeferredRailVessel(vessel, targetTime))
                        {
                            RecordWorkload(workload);
                            continue;
                        }

                        _lastDeferredRailUpdate.Remove(vessel.Id);
                        _nextDeferredRailDeadline.Remove(vessel.Id);
                        RecordWorkload(workload);
                        PropagateVesselOnRails(vessel, dt);
                        continue;
                    }

                    if (hasLastUpdate && !RestoreDeferredRailStateAtTime(vessel, lastUpdate))
                    {
                        _lastDeferredRailUpdate.Remove(vessel.Id);
                        _nextDeferredRailDeadline.Remove(vessel.Id);
                        RecordWorkload(workload);
                        PropagateVesselOnRails(vessel, dt);
                        continue;
                    }

                    PropagateVesselOnRails(vessel, hasLastUpdate ? lastUpdate : CurrentTime, targetTime);
                    _lastDeferredRailUpdate[vessel.Id] = targetTime;
                    _nextDeferredRailDeadline[vessel.Id] =
                        targetTime + deadline.IntervalSeconds;
                    RecordWorkload(workload);
                    continue;
                }

                _lastDeferredRailUpdate.Remove(vessel.Id);
                _nextDeferredRailDeadline.Remove(vessel.Id);
                RecordWorkload(workload);
                PropagateVesselOnRails(vessel, dt);
                continue;
            }

            // Preserve the pre-existing reference-body choice when a settled vessel
            // has just received throttle and is about to wake the full solver.
            var refBody = vessel.IsSurfaceSettled
                ? ResolveSurfaceBody(vessel)
                : GetDominantBody(vessel.Position);
            if (workload == VesselPhysicsWorkload.SurfaceSettled)
            {
                RecordWorkload(workload);
                // Rigid-body sleep for a landed vehicle.  This is not a launch
                // clamp: any commanded thrust wakes the solver immediately.
                AdvanceSurfaceAnchor(vessel, refBody, dt);
                vessel.Tick(dt, refBody);
                continue;
            }
            if (vessel.IsGroundHeld)
            {
                RecordWorkload(workload);
                AdvanceGroundHoldFrame(vessel, refBody, dt);
                vessel.Position = refBody.Position
                    + vessel.GroundNormal
                        * (refBody.Radius + vessel.GroundOffset);
                vessel.Velocity = refBody.Velocity
                    + refBody.GetSurfaceVelocity(vessel.Position);
                vessel.IsOnRails = false;
                vessel.Tick(dt, refBody);
                continue;
            }

            if (vessel == ActiveVessel)
            {
                // Decide whether the active vessel should be on rails this step.
                // Conditions: high time-warp AND coasting (throttle ≈ 0) AND above atmosphere.
                // When throttle > 0.01 the vessel exits rails immediately (≤ 1 sub-step latency)
                // so the next RK4 step picks up the thrust correctly.
                bool shouldBeOnRails = TimeScale >= 10.0
                    && !requiresForces;

                if (shouldBeOnRails && !vessel.IsOnRails)
                {
                    vessel.IsOnRails    = true;
                    vessel.OrbitalState = null; // will be computed in PropagateVesselOnRails
                }
                else if (!shouldBeOnRails && vessel.IsOnRails)
                {
                    WakeVesselFromRails(vessel);
                }

                if (vessel.IsOnRails)
                {
                    RecordWorkload(VesselPhysicsWorkload.OnRails);
                    PropagateVesselOnRails(vessel, dt);
                }
                else
                {
                    RecordWorkload(VesselPhysicsWorkload.FullPhysics);
                    IntegrateVesselOffRails(vessel, refBody, dt);
                }
            }
            else if (requiresForces)
            {
                WakeVesselFromRails(vessel);
                RecordWorkload(VesselPhysicsWorkload.FullPhysics);
                IntegrateVesselOffRails(vessel, refBody, dt);
            }
            else
            {
                RecordWorkload(VesselPhysicsWorkload.OnRails);
                PropagateVesselOnRails(vessel, dt);
            }
        }
        ApplyDockingConstraints();
    }

    private bool TrySkipDeferredCandidate(
        Vessel vessel,
        double targetTime,
        PhysicsSchedulerDeadlinePlan deadline)
    {
        if (!DeferredPhysicsCandidateEnabled
            || DeferredPhysicsCandidateEligibility is not { } eligibility
            || vessel == ActiveVessel
            || ClassifySimulationTier(vessel) != VesselSimulationTier.Hibernated)
        {
            return false;
        }

        bool eligible;
        try
        {
            eligible = eligibility(vessel, CurrentTime);
        }
        catch (Exception)
        {
            eligible = false;
        }
        if (!eligible)
            return false;

        if (!_lastDeferredRailUpdate.TryGetValue(vessel.Id, out double lastUpdate))
        {
            // A candidate can only anchor an already valid analytic state. If the first
            // rails pass still needs to construct OrbitalState, use the old path once.
            if (vessel.OrbitalState is null)
                return false;
            lastUpdate = CurrentTime;
            _lastDeferredRailUpdate[vessel.Id] = lastUpdate;
        }

        if (!_nextDeferredRailDeadline.TryGetValue(vessel.Id, out double nextDeadline))
        {
            nextDeadline = lastUpdate + deadline.IntervalSeconds;
            _nextDeferredRailDeadline[vessel.Id] = nextDeadline;
        }

        if (targetTime < nextDeadline - 1e-12)
        {
            _tickDeadlineDeferredSkips++;
            _tickCandidateDeferredSkips++;
            return true;
        }

        // Materialize from the anchored conic only at the event-safe candidate deadline.
        // A false result falls through to the conservative existing rails branch.
        return CatchUpDeferredRailVessel(vessel, targetTime);
    }

    private void IntegrateVesselOffRails(Vessel vessel, CelestialBody refBody, double dt)
    {
        // Celestial bodies are already at CurrentTime + dt, while the vessel still carries
        // its CurrentTime state. Integrating those absolute states together injects the
        // reference body's orbital displacement into low-altitude motion (Earth travels
        // about 600 m in a 20 ms physics step). Work in the body's translating frame:
        // preserve the vessel's start-relative state, evaluate forces against the body's
        // end state, subtract the rail acceleration of that frame, then reconstruct the
        // inertial end state. This keeps launch, atmosphere and contact at one relative epoch.
        var (referenceStartPosition, referenceStartVelocity) =
            BodyStateAt(refBody, CurrentTime);
        Vector3d relativePosition =
            vessel.Position - referenceStartPosition;
        Vector3d relativeVelocity =
            vessel.Velocity - referenceStartVelocity;
        Vector3d framedPosition =
            refBody.Position + relativePosition;
        Vector3d framedVelocity =
            refBody.Velocity + relativeVelocity;

        double evaluationTime = CurrentTime + dt;
        var contactBefore = EvaluateLandingContact(
            vessel, refBody, framedPosition, framedVelocity);
        var catchContactBefore = EvaluateCatchContact(
            vessel, framedPosition, framedVelocity, evaluationTime);
        vessel.LastContactForceWorld =
            (contactBefore?.ForceWorld ?? Vector3d.Zero) + (catchContactBefore?.ForceWorld ?? Vector3d.Zero);
        vessel.LastContactTorqueWorld =
            (contactBefore?.TorqueWorld ?? Vector3d.Zero) + (catchContactBefore?.TorqueWorld ?? Vector3d.Zero);

        vessel.Position = framedPosition;
        vessel.Velocity = framedVelocity;
        vessel.Tick(dt, refBody, vessel.LastContactTorqueWorld);
        Vector3d frameAcceleration =
            GetReferenceBodyRailAcceleration(refBody);
        (relativePosition, relativeVelocity) = RK4Integrator.StepPosVel(
            relativePosition,
            relativeVelocity,
            CurrentTime,
            dt,
            (relativePos, relativeVel, _) =>
            {
                Vector3d position = refBody.Position + relativePos;
                Vector3d velocity = refBody.Velocity + relativeVel;
                var stageContact = EvaluateLandingContact(
                    vessel, refBody, position, velocity);
                var stageCatchContact = EvaluateCatchContact(
                    vessel, position, velocity, evaluationTime);
                var contactAcceleration = vessel.TotalMass > 0.0
                    ? ((stageContact?.ForceWorld ?? Vector3d.Zero)
                        + (stageCatchContact?.ForceWorld ?? Vector3d.Zero)) / vessel.TotalMass
                    : Vector3d.Zero;
                return vessel.ComputeNetAccelerationAt(
                        position, velocity, _bodies, refBody)
                    - frameAcceleration
                    + contactAcceleration;
            });
        vessel.Position = refBody.Position + relativePosition;
        vessel.Velocity = refBody.Velocity + relativeVelocity;

        var contactAfter = EvaluateLandingContact(vessel, refBody, vessel.Position, vessel.Velocity);
        UpdateLandingContactState(vessel, refBody, contactAfter, dt);
        var catchContactAfter = EvaluateCatchContact(
            vessel, vessel.Position, vessel.Velocity, evaluationTime);
        UpdateCatchContactState(vessel, catchContactAfter, dt);
        ApplyPostIntegrationPhysics(vessel, refBody, dt);
    }

    // Cheap range gate evaluated before the full penalty-contact solve: the tower is a
    // single fixed point, and a catch-flagged flight spends almost all of its time nowhere
    // near it (deorbit, entry, aero descent). Generous enough to cover final-approach
    // maneuvering error without ever gating out a genuine catch attempt.
    private const double CatchRangeGateM = 500.0;
    private const double CatchCaptureRadiusM = 5.0;

    private static Physics.ContactWrench? EvaluateCatchContact(
        Vessel vessel, Vector3d position, Vector3d velocity, double evaluationTime)
    {
        Vector3d cradlePosition = vessel.GetCatchTargetPositionAt(evaluationTime);
        vessel.LastCatchEvaluationRangeM =
            (position - cradlePosition).Magnitude;
        vessel.LastCatchEvaluationPassedGate = false;
        if (!vessel.IsAttemptingTowerCatch || !vessel.HasCatchPins)
            return null;
        if ((position - cradlePosition).MagnitudeSquared
            > CatchRangeGateM * CatchRangeGateM)
            return null;
        vessel.LastCatchEvaluationPassedGate = true;

        var input = vessel.GetContactInput(position, velocity);
        Vector3d up = vessel.CatchTargetUpWorld;
        Vector3d cradleVelocity = vessel.CatchTargetVelocityWorld;
        return Physics.SurfaceContactSolver.Evaluate(
            input,
            vessel.CatchContactPoints,
            point => Physics.SurfaceSample.FromCatchCradle(
                cradlePosition, up, cradleVelocity, CatchCaptureRadiusM, point));
    }

    /// <summary>
    /// Settle criterion for a tower catch — deliberately separate from
    /// <see cref="UpdateLandingContactState"/> rather than a branch inside it: that method
    /// reasons about "upright against the local ground normal" and "≥3 feet sharing load",
    /// neither of which describes two pins resting in a fixed horizontal cradle. A missed
    /// catch is not treated as a failure here; it simply never settles, and the game layer's
    /// EDL guidance is responsible for having already diverted to a normal leg landing well
    /// before the vessel is close enough for a slow miss to mean a structural collision with
    /// the tower.
    /// </summary>
    private static void UpdateCatchContactState(
        Vessel vessel, Physics.ContactWrench? contact, double dt)
    {
        vessel.LastCatchContact = contact;
        if (contact == null || contact.ContactCount == 0)
        {
            vessel.CatchSettledDuration = 0.0;
            vessel.IsCaught = false;
            return;
        }

        double relativeSpeed = (vessel.Velocity - vessel.CatchTargetVelocityWorld).Magnitude;
        bool settledNow = contact.ContactCount == 2
            && relativeSpeed < 0.50
            && vessel.AngularVelocity.Magnitude < 0.05;
        vessel.CatchSettledDuration = settledNow ? vessel.CatchSettledDuration + dt : 0.0;
        vessel.IsCaught = vessel.CatchSettledDuration >= 0.50;
        if (vessel.IsCaught)
        {
            vessel.Velocity = vessel.CatchTargetVelocityWorld;
            vessel.AngularVelocity = Vector3d.Zero;
        }
    }

    private Vector3d GetReferenceBodyRailAcceleration(
        CelestialBody reference)
    {
        if (reference.OrbitalElements is not { } orbit)
            return Vector3d.Zero;
        var parent = GetBody(orbit.ReferenceBodyId);
        return parent?.GetGravityAt(reference.Position)
            ?? Vector3d.Zero;
    }

    private static Physics.ContactWrench? EvaluateLandingContact(
        Vessel vessel, CelestialBody body, Vector3d position, Vector3d velocity)
    {
        if (!vessel.HasDeployedLandingGear || vessel.LandingContactPoints.Count == 0)
            return null;
        var input = vessel.GetContactInput(position, velocity);
        return Physics.SurfaceContactSolver.EvaluateSphere(
            input, vessel.LandingContactPoints, body);
    }

    private static void UpdateLandingContactState(
        Vessel vessel, CelestialBody body, Physics.ContactWrench? contact, double dt)
    {
        vessel.LastSurfaceContact = contact;
        vessel.LastContactForceWorld = contact?.ForceWorld ?? Vector3d.Zero;
        vessel.LastContactTorqueWorld = contact?.TorqueWorld ?? Vector3d.Zero;

        if (contact == null || contact.ContactCount == 0)
        {
            vessel.SurfaceSettledDuration = 0.0;
            vessel.IsSurfaceSettled = false;
            return;
        }

        // Landing-leg joints dissipate residual pitch/yaw once the load is shared by at
        // least three feet. This is passive structural damping, not an attitude snap: the
        // integrated angular velocity decays continuously while contact torque remains free
        // to tip an actually unstable vehicle.
        if (contact.ContactCount >= 3)
            vessel.AngularVelocity *= System.Math.Exp(-8.0 * dt);

        double impactSpeed = vessel.GetSurfaceVelocity(body).Magnitude;
        // Bottom-out remains diagnostic until a non-linear bump-stop/primary-structure load
        // path exists. The penalty force is not capped, so the declared ultimate leg load is
        // the physical failure gate instead of an arbitrary penetration epsilon.
        if (contact.HasOverload)
        {
            vessel.IsDestroyed = true;
            vessel.DestructionCause = VesselDestructionCause.GroundImpact;
            vessel.CrashImpactSpeed = impactSpeed;
            vessel.CrashSimPosition = vessel.Position;
            var direction = (vessel.Position - body.Position).Normalized;
            AnchorToSurface(
                vessel,
                body,
                direction,
                System.Math.Max(0.5, body.GetAltitude(vessel.Position)));
            return;
        }

        var normal = (vessel.Position - body.Position).Normalized;
        var surfaceVelocity = vessel.GetSurfaceVelocity(body);
        double normalSpeed = System.Math.Abs(surfaceVelocity.Dot(normal));
        double tangentialSpeed = (surfaceVelocity - normal * surfaceVelocity.Dot(normal)).Magnitude;
        double upright = vessel.Orientation.Rotate(Vector3d.Up).Normalized.Dot(normal);
        double localGravity = body.GetGravityAt(vessel.Position).Magnitude;
        double normalSupportAcceleration = vessel.TotalMass > 0.0
            ? contact.ForceWorld.Dot(normal) / vessel.TotalMass
            : 0.0;
        // Penalty contacts can retain a short-lived bump-stop preload after the
        // landing transient.  Low kinetic state sustained for 0.5 s is the
        // decisive sleep criterion; overload remains the separate hard failure
        // gate above, so removing the narrow 1.25 g ceiling cannot hide damage.
        bool adequatelySupported = localGravity > 0.0
            && normalSupportAcceleration > 0.65 * localGravity;
        bool settledNow = contact.ContactCount >= 3
            && normalSpeed < 0.25
            && tangentialSpeed < 0.50
            && vessel.AngularVelocity.Magnitude < 0.03
            && upright > System.Math.Cos(10.0 * MathUtils.DEG_TO_RAD)
            && adequatelySupported;
        vessel.SurfaceSettledDuration = settledNow
            ? vessel.SurfaceSettledDuration + dt
            : 0.0;
        vessel.IsSurfaceSettled = vessel.SurfaceSettledDuration >= 0.50;
        if (vessel.IsSurfaceSettled)
        {
            vessel.GroundNormal = normal;
            vessel.GroundOffset = body.GetAltitude(vessel.Position);
            vessel.Velocity = body.Velocity + body.GetSurfaceVelocity(vessel.Position);
            vessel.AngularVelocity = Vector3d.Zero;
        }
    }

    private void TickRails(double dt)
    {
        KeplerPropagator.PropagateAllBodies(
            _bodies,
            CurrentTime + dt,
            _bodyPropagationWorkspace);
        foreach (var vessel in _vessels)
        {
            if (IsDockedSecondary(vessel))
            {
                _tickDockedSecondarySkips++;
                continue;
            }
            if (vessel.IsDestroyed)
            {
                RecordWorkload(VesselPhysicsWorkload.Destroyed);
                AdvanceAnchoredWreck(vessel, dt);
                continue;
            }
            if (vessel.IsSurfaceSettled)
            {
                if (HasWakeCommand(vessel))
                {
                    vessel.IsSurfaceSettled = false;
                    vessel.SurfaceSettledDuration = 0.0;
                }
                else
                {
                    RecordWorkload(VesselPhysicsWorkload.SurfaceSettled);
                    var body = ResolveSurfaceBody(vessel);
                    AdvanceSurfaceAnchor(vessel, body, dt);
                    vessel.Tick(dt, body);
                    continue;
                }
            }
            RecordWorkload(VesselPhysicsWorkload.OnRails);
            PropagateVesselOnRails(vessel, dt);
        }
        ApplyDockingConstraints();
    }

    // ── Vessel on-rails propagation ───────────────────────────────────────

    private void PropagateVesselOnRails(Vessel vessel, double dt) =>
        PropagateVesselOnRails(vessel, CurrentTime, CurrentTime + dt);

    private void PropagateVesselOnRails(
        Vessel vessel,
        double startTime,
        double targetTime)
    {
        double dt = targetTime - startTime;
        if (dt <= 1e-12)
            return;

        // Compute or reuse cached orbital elements.
        // CRITICAL: the global bodies were already propagated to the tick's END time by
        // PropagateAllBodies before this runs, but vessel.Position/Velocity still correspond
        // to CurrentTime (the conic's epoch). Build the relative state against the reference
        // body's state AT CurrentTime (via BodyStateAt) — using its end-of-tick position would
        // bias the initial conic by (body velocity × dt), which at high warp (dt up to 2000 s)
        // is tens of thousands of km — a wrong orbit the instant warp is engaged.
        if (vessel.OrbitalState is null)
        {
            var refBody      = GetDominantBodyAt(vessel.Position, startTime);
            var (refP, refV) = BodyStateAt(refBody, startTime);
            var relPos       = vessel.Position - refP;
            var relVel       = vessel.Velocity - refV;
            vessel.OrbitalState    = KeplerPropagator.ComputeElements(
                relPos, relVel, refBody.GM, refBody.Id, startTime);
            vessel.ReferenceBodyId = refBody.Id;
        }

        var reference = GetBody(vessel.OrbitalState.ReferenceBodyId);
        if (reference is null) return;

        // ── Patched-conic SOI transition guard (pre-step) ─────────────────
        // The vessel may have drifted into (or out of) another body's sphere of
        // influence since the cached conic was last computed against `reference`.
        // If the dominant body changed, re-frame the state to it and recompute the
        // conic there BEFORE propagating, so this step's arc is integrated in the
        // correct frame. GetDominantBody works on the inertial position — identical
        // in every frame — so re-framing preserves inertial continuity (no jump in
        // absolute position/velocity, only the orbital elements change).
        // vessel.Position corresponds to CurrentTime (end of the previous tick); evaluate the
        // dominant body and re-frame against the body state at THAT instant, not end-of-tick.
        var dominantNow = GetDominantBodyAt(vessel.Position, startTime);
        if (dominantNow.Id != vessel.OrbitalState!.ReferenceBodyId)
        {
            var (bp, bv) = BodyStateAt(dominantNow, startTime);
            ReframeVesselToBody(vessel, dominantNow, bp, bv, startTime);
            reference = dominantNow;
        }

        // ── Periapsis / sub-surface collision check (on-rails) ────────────
        // The Kepler propagator works in conic sections and cannot detect when the
        // orbital arc dips below the surface mid-step — the vessel would silently
        // "tunnel through" the planet and reappear on the other side.
        //
        // Guard: if the conic is suborbital (periapsis below the body radius) OR the
        // trajectory is radial (h≈0 — a straight fall/lob with no well-defined orbit),
        // the path already intersects the surface. Destroy the vessel immediately
        // instead of propagating a physically impossible trajectory.
        //
        // IsSuborbital() uses the true periapsis radius (valid for elliptic, hyperbolic
        // AND radial conics). The old a*(1-e) test hid radial/hyperbolic cases where the
        // numbers sign-flip into a misleading large-positive periapsis — that was the root
        // of the "rocket exits orbit straight through the planet" bug under warp.
        // Only a RADIAL conic (h≈0, degenerate) must be resolved here — it cannot be
        // propagated by Kepler at all. A normal suborbital ELLIPSE (e.g. a vessel that
        // lowered its periapsis to deorbit) is valid and is left to the per-slice surface
        // check below: it coasts down realistically and only impacts when it actually reaches
        // the surface, so the atmosphere/EDL can fly the reentry. Destroying it the instant
        // its periapsis dips below the radius — while still at apoapsis hundreds of km up —
        // made reentry under warp impossible.
        if (vessel.OrbitalState.IsRadial)
        {
            var (refP0, refV0) = BodyStateAt(reference, startTime);
            ResolveOnRailsImpact(vessel, reference, refP0, refV0);
            return;
        }

        // ── Sub-step sampling so high warp cannot skip below the surface ───
        // Even with a periapsis above the surface, a single large warp step could place
        // the sampled point on the far side of a tight periapsis pass without ever
        // "seeing" the dip. Walk the step in bounded slices (≤ MaxCoastStep) and check the
        // propagated radius at each sample. If any sample is below the surface, the arc
        // grazes the body within this step → resolve the impact at that point, never skip it.
        double remaining   = dt;
        double sampleTime  = startTime;
        var (referenceStartPosition, referenceStartVelocity) =
            BodyStateAt(reference, startTime);
        Vector3d lastRelP  = vessel.Position - referenceStartPosition;
        Vector3d lastRelV  = vessel.Velocity - referenceStartVelocity;
        while (remaining > 1e-9)
        {
            _tickRailsSlices++;
            double slice = System.Math.Min(remaining, MaxCoastStep);
            sampleTime  += slice;
            remaining   -= slice;

            (lastRelP, lastRelV) = KeplerPropagator.PropagateToTime(
                vessel.OrbitalState, sampleTime, reference.GM);

            // Reference body inertial state at THIS sub-step time (bodies are globally
            // frozen at end-of-tick; the crossing/impact happens earlier).
            var (refPosAt, refVelAt) = BodyStateAt(reference, sampleTime);

            if (lastRelP.Magnitude < reference.Radius)
            {
                // The conic crosses the surface inside this step — impact here.
                vessel.Position = refPosAt + lastRelP;
                vessel.Velocity = refVelAt + lastRelV;
                ResolveOnRailsImpact(vessel, reference, refPosAt, refVelAt);
                return;
            }

            // ── Mid-step SOI crossing (patched-conic) ─────────────────────
            // The slice may have carried the vessel across an SOI boundary (e.g.
            // into the Moon's SOI, or out of Earth's into the Sun's). Resolve the
            // dominant body at the propagated inertial point. If it changed, commit
            // the inertial state here, re-frame to the new dominant body with the
            // conic's epoch set to THIS crossing time (sampleTime) — so the remaining
            // slices, sampled at absolute times, stay phase-correct — and keep
            // sub-stepping the rest of the step in the new conic. This walks the
            // boundary instead of tunnelling through it under warp.
            // Reconstruct the inertial crossing point with the reference body at the SAME
            // instant (sampleTime), and decide the dominant body in that same frame.
            var inertialP = refPosAt + lastRelP;
            var inertialV = refVelAt + lastRelV;
            var dominantHere = GetDominantBodyAt(inertialP, sampleTime);
            if (dominantHere.Id != reference.Id)
            {
                vessel.Position = inertialP;
                vessel.Velocity = inertialV;
                var (newRefP, newRefV) = BodyStateAt(dominantHere, sampleTime);
                ReframeVesselToBody(vessel, dominantHere, newRefP, newRefV, sampleTime);
                reference = dominantHere;

                // Only a radial (degenerate) conic must be resolved here; a suborbital ellipse
                // in the new frame coasts down and is caught by the per-slice surface check.
                if (vessel.OrbitalState!.IsRadial)
                {
                    ResolveOnRailsImpact(vessel, reference, newRefP, newRefV);
                    return;
                }

                // Re-anchor the per-slice state from the NEW conic at this crossing time,
                // so a crossing on the final slice still reconstructs consistently below.
                (lastRelP, lastRelV) = KeplerPropagator.PropagateToTime(
                    vessel.OrbitalState!, sampleTime, reference.GM);
            }
        }

        var (referenceTargetPosition, referenceTargetVelocity) =
            BodyStateAt(reference, targetTime);
        vessel.Position = referenceTargetPosition + lastRelP;
        vessel.Velocity = referenceTargetVelocity + lastRelV;
    }

    /// <summary>
    /// Re-frames an on-rails vessel onto a new dominant body, recomputing its conic
    /// in that body's frame from the SAME inertial state. The vessel's absolute
    /// (inertial) position and velocity are unchanged — only the reference body and
    /// the derived Keplerian elements change — so the trajectory is continuous across
    /// the sphere-of-influence boundary (a patched-conic transition).
    /// </summary>
    /// <remarks>
    /// Caller must have set <see cref="Vessel.Position"/>/<see cref="Vessel.Velocity"/>
    /// to the inertial state at the crossing point, and pass the new body's inertial
    /// state <paramref name="bodyPos"/>/<paramref name="bodyVel"/> AT THE SAME instant
    /// (<paramref name="epoch"/>). The global body objects are frozen at the tick's end
    /// time during sub-stepping, so the crossing-time body state must be supplied
    /// explicitly (see <see cref="BodyStateAt"/>) — otherwise the relative state is
    /// computed against the wrong frame and a spurious inertial jump appears under warp.
    /// <paramref name="epoch"/> is the simulation time the inertial state corresponds to;
    /// the recomputed conic stores its mean anomaly at that epoch so subsequent
    /// propagation to absolute times stays phase-correct.
    /// </remarks>
    private static void ReframeVesselToBody(
        Vessel vessel, CelestialBody newBody, Vector3d bodyPos, Vector3d bodyVel, double epoch)
    {
        var relPos = vessel.Position - bodyPos;
        var relVel = vessel.Velocity - bodyVel;

        vessel.OrbitalState    = KeplerPropagator.ComputeElements(
            relPos, relVel, newBody.GM, newBody.Id, epoch);
        vessel.ReferenceBodyId = newBody.Id;
    }

    /// <summary>
    /// Inertial state (position, velocity) of a body at an arbitrary simulation time,
    /// WITHOUT mutating global body state. Mirrors <c>KeplerPropagator.PropagateAllBodies</c>
    /// by walking the reference chain (Moon → Earth → Sun); a root body with no orbital
    /// elements is treated as fixed at its current stored position.
    ///
    /// Needed because the global bodies are propagated once to the tick's END time, but
    /// an on-rails SOI crossing happens at an intermediate sub-step time — the reference
    /// body's state at THAT instant is what keeps the patched-conic re-frame continuous.
    /// </summary>
    private (Vector3d pos, Vector3d vel) BodyStateAt(CelestialBody body, double t)
    {
        if (body.OrbitalElements is null)
            return (body.Position, body.Velocity);   // root (e.g. Sun) — fixed at origin

        var refBody = GetBody(body.OrbitalElements.ReferenceBodyId);
        if (refBody is null)
            return (body.Position, body.Velocity);

        var (refPos, refVel)  = BodyStateAt(refBody, t);
        var (relPos, relVel)  = body.OrbitalElements.GetStateAtTime(t, refBody.GM);
        return (refPos + relPos, refVel + relVel);
    }

    /// <summary>
    /// Like <see cref="GetDominantBody"/> but evaluates each body's position at time
    /// <paramref name="t"/> (via <see cref="BodyStateAt"/>) instead of its frozen end-of-tick
    /// position — so an SOI boundary test during sub-stepping is decided in the right frame.
    /// </summary>
    private CelestialBody GetDominantBodyAt(Vector3d position, double t)
    {
        CelestialBody? best    = null;
        double         bestSoi = double.MaxValue;

        foreach (var body in _bodies)
        {
            var (bp, _) = BodyStateAt(body, t);
            double dist = (position - bp).Magnitude;
            if (dist < body.SphereOfInfluence && body.SphereOfInfluence < bestSoi)
            {
                bestSoi = body.SphereOfInfluence;
                best    = body;
            }
        }

        if (best != null) return best;

        CelestialBody fallback = _bodies[0];
        for (int i = 1; i < _bodies.Count; i++)
            if (_bodies[i].Mass > fallback.Mass)
                fallback = _bodies[i];
        return fallback;
    }

    /// <summary>
    /// Resolves a certain surface impact for an on-rails vessel: marks it destroyed,
    /// records the surface-relative impact speed, clamps the wreck to the surface for the
    /// renderer, and drops it off rails. The vessel reenters and is destroyed — it never
    /// bounces back to orbit or tunnels through the body (R16 user decision).
    /// </summary>
    /// <remarks>
    /// <paramref name="refPos"/>/<paramref name="refVel"/> are the reference body's inertial
    /// state AT THE IMPACT INSTANT (the bodies are globally frozen at the tick end, so under
    /// warp the body has moved tens of thousands of km since the impact). They are used for the
    /// impact-relative speed and the radial direction, so the wreck is clamped on the correct
    /// side of the body (using the body's stale end-of-tick position for the direction could
    /// put it on the far side at high warp). The surface clamp itself uses the body's current
    /// position, since that is where the body renders this frame.
    /// </remarks>
    private void ResolveOnRailsImpact(
        Vessel vessel, CelestialBody reference, Vector3d refPos, Vector3d refVel)
    {
        var    relP0       = vessel.Position - refPos;
        var    relV0       = vessel.Velocity - refVel;
        double impactSpeed = relV0.Magnitude; // conservative: full orbital speed

        vessel.IsDestroyed      = true;
        vessel.DestructionCause = VesselDestructionCause.GroundImpact;
        vessel.CrashImpactSpeed = impactSpeed;
        vessel.CrashSimPosition = vessel.Position;

        // Clamp to the body's current surface for the renderer, along the true impact direction.
        var dir = relP0.Magnitude > 0.0 ? relP0.Normalized : Vector3d.Up;
        AnchorToSurface(vessel, reference, dir, 0.5);
    }

    // ── Factory ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates and populates a <see cref="Universe"/> from a data directory.
    /// Expects a <c>bodies/</c> sub-directory containing <c>*.json</c> body files.
    /// </summary>
    /// <param name="dataDir">Root data directory (e.g. <c>res://data</c>).</param>
    public static Universe LoadFromDataDirectory(string dataDir)
    {
        var universe  = new Universe();
        var bodiesDir = System.IO.Path.Combine(dataDir, "bodies");
        var bodies    = CelestialBody.LoadAllFromDirectory(bodiesDir);

        // Root body sits at the inertial origin. PropagateAllBodies recursively resolves
        // parents before children, independent of JSON/filesystem enumeration order.
        if (bodies.TryGetValue("sun", out var sun))
        {
            sun.Position = Vector3d.Zero;
            sun.Velocity = Vector3d.Zero;
        }
        KeplerPropagator.PropagateAllBodies(bodies.Values, 0.0);

        foreach (var body in bodies.Values)
            universe.AddBody(body);

        return universe;
    }
}
