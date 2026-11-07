namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;

/// <summary>
/// A debited store's relative-flow momentum. Angular momentum is about the skirt datum,
/// so the contribution can follow its owning part through a split. A null owner denotes
/// an older save's unpartitioned aggregate, conservatively retained by the primary body.
/// </summary>
public readonly record struct TransportMomentumShare(string? PartInstanceId,
    Vector3d LinearBodyKgMps, Vector3d AngularAboutDatumBodyKgM2PerS);
