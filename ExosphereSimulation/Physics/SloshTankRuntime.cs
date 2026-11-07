namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;

/// <summary>
/// Store-owned modal state in tank axes. PreviousParameters retains the ending one-sided
/// schedule, including its rates, until the next opening momentum handoff. It is not an
/// inventory, and it never adds to or withdraws from the resource store.
/// </summary>
public sealed record SloshTankRuntime(ResourceStoreId Store, SloshDisplacementState State,
    CylindricalSloshParameters? PreviousParameters = null, SloshCurvatureHold? CurvatureHold = null);

public enum SloshContinuationKind { NonsettlingDrain, RapidDrain }

/// <summary>
/// Bounded mechanical continuation outside the quasi-static settled-fluid domain. The
/// current curvature is retained until depletion; no fluid distribution in low g is implied.
/// </summary>
public readonly record struct SloshCurvatureHold(double CurvaturePerM, SloshContinuationKind Reason);

/// <summary>One member's construction datum and axes in the primary frame.</summary>
public readonly record struct SloshAttachedVessel(Vessel Vessel, Vector3d DatumPrimaryBodyM,
    Quaterniond OrientationPrimaryBody);
