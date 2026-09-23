namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Propulsion;

/// <summary>
/// Snapshot of the engine population that is currently eligible to provide attitude
/// authority. Engines in an inactive stage are intentionally excluded.
/// </summary>
public readonly record struct EngineOutRecoveryState(
    int NominalEngineCount,
    int FailedEngineCount,
    int LiveEngineCount)
{
    public bool IsEngineOut => FailedEngineCount > 0 && LiveEngineCount > 0;
}

/// <summary>
/// Deterministic attitude recovery command after an active-stage engine failure.
/// This class only computes commands; <see cref="Vessel"/> remains responsible for
/// applying control authority, gimbal allocation and physical integration.
/// </summary>
public static class EngineOutRecoveryGuidance
{
    public const double DefaultProportionalGain = 2.6;
    public const double DefaultDampingGain = 1.2;
    public const double DefaultCommandRatePerSecond = 2.0;

    /// <summary>Inspects only the currently active engine parts.</summary>
    public static EngineOutRecoveryState Inspect(Vessel vessel)
    {
        ArgumentNullException.ThrowIfNull(vessel);

        int nominal = 0;
        int failed = 0;
        int live = 0;
        foreach (var part in vessel.Parts.ActiveEngines)
        {
            foreach (var state in part.EngineStates)
            {
                nominal++;
                bool isFailed = state.State == EngineLifecycleState.Failed
                    || state.FailureCode != null;
                if (isFailed)
                    failed++;
                else
                    live++;
            }
        }

        return new EngineOutRecoveryState(nominal, failed, live);
    }

    /// <summary>
    /// Computes a bounded semantic pitch/yaw command that keeps the vessel's local +Y axis
    /// on <paramref name="targetWorldAxis"/> while damping angular rate. A command is
    /// returned only when the active stage has both a failed and a live engine.
    /// </summary>
    public static Vector3d ComputeCommand(
        Vessel vessel,
        Vector3d targetWorldAxis,
        Vector3d previousCommand,
        double deltaSeconds,
        double maximumCommandRatePerSecond = DefaultCommandRatePerSecond,
        double proportionalGain = DefaultProportionalGain,
        double dampingGain = DefaultDampingGain)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (!double.IsFinite(maximumCommandRatePerSecond)
            || maximumCommandRatePerSecond < 0.0)
            throw new ArgumentOutOfRangeException(nameof(maximumCommandRatePerSecond));
        if (targetWorldAxis.MagnitudeSquared < 1e-12)
            return Vector3d.Zero;

        if (!Inspect(vessel).IsEngineOut)
            return Vector3d.Zero;

        return ComputeCommandFromConfirmedFault(
            vessel,
            targetWorldAxis,
            previousCommand,
            deltaSeconds,
            maximumCommandRatePerSecond,
            proportionalGain,
            dampingGain);
    }

    /// <summary>
    /// Computes the same bounded recovery command after an independent onboard fault
    /// isolator has confirmed the anomaly. This path deliberately does not inspect the
    /// simulator's failure flag: a degraded but not-yet-declared engine can still be
    /// recovered once peer thrust and torque residuals agree.
    /// </summary>
    public static Vector3d ComputeCommandFromConfirmedFault(
        Vessel vessel,
        Vector3d targetWorldAxis,
        Vector3d previousCommand,
        double deltaSeconds,
        double maximumCommandRatePerSecond = DefaultCommandRatePerSecond,
        double proportionalGain = DefaultProportionalGain,
        double dampingGain = DefaultDampingGain)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (!double.IsFinite(maximumCommandRatePerSecond)
            || maximumCommandRatePerSecond < 0.0)
            throw new ArgumentOutOfRangeException(nameof(maximumCommandRatePerSecond));
        if (targetWorldAxis.MagnitudeSquared < 1e-12)
            return Vector3d.Zero;

        var targetCommand = AttitudeGuidance.ComputeAxisPointingCommand(
            vessel.Orientation,
            Vector3d.Up,
            targetWorldAxis,
            vessel.AngularVelocity,
            proportionalGain,
            dampingGain);
        return LimitCommandRate(
            previousCommand,
            targetCommand,
            deltaSeconds,
            maximumCommandRatePerSecond);
    }

    /// <summary>Limits normalized actuator-command slew without changing its direction.</summary>
    public static Vector3d LimitCommandRate(
        Vector3d previousCommand,
        Vector3d targetCommand,
        double deltaSeconds,
        double maximumCommandRatePerSecond)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (!double.IsFinite(maximumCommandRatePerSecond)
            || maximumCommandRatePerSecond < 0.0)
            throw new ArgumentOutOfRangeException(nameof(maximumCommandRatePerSecond));
        if (deltaSeconds == 0.0 || maximumCommandRatePerSecond == 0.0)
            return previousCommand;

        var delta = targetCommand - previousCommand;
        double maximumStep = maximumCommandRatePerSecond * deltaSeconds;
        if (delta.Magnitude <= maximumStep)
            return targetCommand;
        return previousCommand + delta.Normalized * maximumStep;
    }
}
