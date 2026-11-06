namespace Exosphere.Simulation.Parts;

using Exosphere.Simulation.Math;

public partial class PartGraph
{
    private readonly List<(Part Engine, double Duration)> _engineExhaustions = new();

    private void RegisterEngineExhaustion(Part engine, double duration)
    {
        if (!_physicsTickActive)
        {
            engine.MarkFuelDepleted();
            _tickActiveEngineCacheValid = false;
            return;
        }
        foreach (var entry in _engineExhaustions)
            if (ReferenceEquals(entry.Engine, engine)) return;
        _engineExhaustions.Add((engine, duration));
        if (duration == 0) engine.FundingThrustScale = 0;
    }

    private void ClearEngineFunding()
    {
        foreach (var entry in _engineExhaustions) entry.Engine.FundingThrustScale = 1;
        _engineExhaustions.Clear();
    }

    private void CompleteEngineFunding()
    {
        foreach (var entry in _engineExhaustions)
        {
            entry.Engine.FundingThrustScale = 1;
            entry.Engine.MarkFuelDepleted();
        }
        _engineExhaustions.Clear();
    }

    private void SelectEngineInterval(double elapsed)
    {
        foreach (var entry in _engineExhaustions)
            entry.Engine.FundingThrustScale = elapsed < entry.Duration ? 1 : 0;
    }

    /// <summary>
    /// Split only at resource boundaries. Each substep owns a constant live-engine set,
    /// including its endpoint RK4 sample; the following substep uses the other one-sided set.
    /// Stateful propulsion/control preparation and resource debit are never repeated here.
    /// </summary>
    internal T IntegrateEngineIntervals<T>(T state, double duration, Func<T, double, double, T> step)
        => IntegrateEngineIntervals(state, duration, null, step);

    /// <summary>
    /// As above, also splitting at <paramref name="extraBoundaries"/> (step-local seconds): the
    /// knots where a variable-mass profile changes rate, so no RK4 stage straddles one.
    /// </summary>
    internal T IntegrateEngineIntervals<T>(T state, double duration, IReadOnlyList<double>? extraBoundaries,
        Func<T, double, double, T> step)
    {
        if (_engineExhaustions.Count == 0 && (extraBoundaries == null || extraBoundaries.Count == 0))
            return step(state, 0, duration);
        double elapsed = 0;
        try
        {
            while (elapsed < duration)
            {
                double end = duration;
                foreach (var entry in _engineExhaustions)
                    if (entry.Duration > elapsed && entry.Duration < end) end = entry.Duration;
                if (extraBoundaries != null)
                    foreach (double boundary in extraBoundaries)
                        if (boundary > elapsed && boundary < end) end = boundary;
                SelectEngineInterval(elapsed);
                state = step(state, elapsed, end - elapsed);
                elapsed = end;
            }
            return state;
        }
        finally { SelectEngineInterval(0); }
    }

    // The legacy attitude update is explicit Euler: integrate the funded torque impulse
    // once, rather than applying a full-step engine moment or advancing controls twice.
    internal Vector3d GetFundedAngularAcceleration(double pressure, double duration)
    {
        foreach (var entry in _engineExhaustions)
            entry.Engine.FundingThrustScale = System.Math.Clamp(entry.Duration / duration, 0, 1);
        try { return GetPitchYawRollAngularAcceleration(pressure); }
        finally { SelectEngineInterval(0); }
    }
}
