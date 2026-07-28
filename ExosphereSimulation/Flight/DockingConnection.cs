namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;

/// <summary>
/// Persistent rigid connection between two otherwise independent vessels.
/// The primary remains the integrated/control vessel; the secondary follows the
/// captured relative pose until undocking.
/// </summary>
public sealed class DockingConnection
{
    public string Id { get; init; } = "";
    public string PrimaryVesselId { get; init; } = "";
    public string SecondaryVesselId { get; init; } = "";
    public string PrimaryPortPartId { get; init; } = "";
    public string SecondaryPortPartId { get; init; } = "";
    public string PrimaryPortNodeId { get; init; } = "";
    public string SecondaryPortNodeId { get; init; } = "";
    public Vector3d SecondaryPositionPrimaryLocal { get; init; }
    public Quaterniond SecondaryOrientationPrimaryLocal { get; init; }
}

public enum DockingFailure
{
    None,
    SameVessel,
    ConnectionIdConflict,
    VesselMissing,
    VesselAlreadyDocked,
    PortMissing,
    PortUnavailable,
    OutsideCaptureRange,
    ExcessiveClosingSpeed,
    Misaligned,
}

public readonly record struct DockingAttempt(
    bool Succeeded,
    DockingFailure Failure,
    DockingConnection? Connection,
    double DistanceM,
    double RelativeSpeedMps,
    double AlignmentErrorDeg);
