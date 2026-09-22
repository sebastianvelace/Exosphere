namespace Exosphere.Simulation.Physics;

using Exosphere.Simulation.Math;

/// <summary>
/// Read-only evidence for one completed coupled 6-DoF integration step.
/// The COM state is reported in the inertial/world frame so it can be compared with
/// the legacy datum state without mixing coordinate frames.
/// </summary>
public readonly record struct Coupled6DofTelemetry(
    bool Integrated,
    double SimulationTime,
    double StepSeconds,
    double MassKg,
    Vector3d InitialCenterOfMassPositionWorld,
    Vector3d FinalCenterOfMassPositionWorld,
    Vector3d InitialCenterOfMassVelocityWorld,
    Vector3d FinalCenterOfMassVelocityWorld,
    Quaterniond FinalOrientation,
    Vector3d FinalAngularVelocityWorld)
{
    public static Coupled6DofTelemetry NotRun => default;

    public Vector3d TranslationDeltaWorld =>
        FinalCenterOfMassPositionWorld - InitialCenterOfMassPositionWorld;

    public Vector3d VelocityDeltaWorld =>
        FinalCenterOfMassVelocityWorld - InitialCenterOfMassVelocityWorld;

    public double OrientationNorm => FinalOrientation.Norm;

    public bool IsFinite =>
        double.IsFinite(SimulationTime)
        && double.IsFinite(StepSeconds)
        && double.IsFinite(MassKg)
        && IsFiniteVector(InitialCenterOfMassPositionWorld)
        && IsFiniteVector(FinalCenterOfMassPositionWorld)
        && IsFiniteVector(InitialCenterOfMassVelocityWorld)
        && IsFiniteVector(FinalCenterOfMassVelocityWorld)
        && double.IsFinite(OrientationNorm)
        && IsFiniteVector(FinalAngularVelocityWorld);

    private static bool IsFiniteVector(Vector3d value) =>
        double.IsFinite(value.X)
        && double.IsFinite(value.Y)
        && double.IsFinite(value.Z);
}
