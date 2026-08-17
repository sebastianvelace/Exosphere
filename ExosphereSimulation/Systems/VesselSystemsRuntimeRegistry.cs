namespace Exosphere.Simulation.Systems;

/// <summary>
/// Owns the explicitly materialized systems runtimes for a set of vessels.
///
/// The registry intentionally does not infer state for an unknown vessel. A caller must
/// materialize a vessel at a committed epoch or restore an authoritative snapshot first;
/// this is the boundary that lets a future interest policy fail closed for unmaterialized
/// vessels instead of silently assigning fresh oxygen/battery/thermal values.
/// </summary>
public sealed class VesselSystemsRuntimeRegistry
{
    private const double EpochToleranceSeconds = 1e-7;
    private readonly Dictionary<string, VesselSystemsRuntime> _runtimes =
        new(StringComparer.Ordinal);

    public int Count => _runtimes.Count;

    public bool Contains(string vesselId) =>
        !string.IsNullOrWhiteSpace(vesselId)
        && _runtimes.ContainsKey(vesselId);

    public bool TryGet(
        string vesselId,
        out VesselSystemsRuntime? runtime) =>
        _runtimes.TryGetValue(vesselId, out runtime);

    /// <summary>
    /// Materializes one new runtime at an already committed epoch. Reusing an existing
    /// runtime is allowed only at that exact epoch; callers must not overwrite it on a
    /// vessel switch.
    /// </summary>
    public VesselSystemsRuntime Materialize(string vesselId, double simulationTime)
    {
        ValidateVesselId(vesselId);
        ValidateEpoch(simulationTime);

        if (_runtimes.TryGetValue(vesselId, out var existing))
        {
            EnsureEpoch(existing.SimulationTime, simulationTime, vesselId);
            return existing;
        }

        var runtime = new VesselSystemsRuntime(vesselId, simulationTime);
        _runtimes.Add(vesselId, runtime);
        return runtime;
    }

    /// <summary>Removes one materialized runtime without affecting other vessels.</summary>
    public bool Remove(string vesselId) =>
        !string.IsNullOrWhiteSpace(vesselId)
        && _runtimes.Remove(vesselId);

    public void Clear() => _runtimes.Clear();

    /// <summary>
    /// Captures every materialized runtime at one exact committed epoch. A stale runtime
    /// fails the whole operation instead of producing a mixed-epoch save.
    /// </summary>
    public Dictionary<string, VesselSystemsState> CaptureStates(double committedEpoch)
    {
        ValidateEpoch(committedEpoch);
        var states = new Dictionary<string, VesselSystemsState>(StringComparer.Ordinal);
        foreach (var (vesselId, runtime) in _runtimes)
        {
            EnsureEpoch(runtime.SimulationTime, committedEpoch, vesselId);
            states.Add(vesselId, runtime.CaptureState());
        }
        return states;
    }

    /// <summary>
    /// Replaces the registry atomically from validated save snapshots. Unknown vessel IDs,
    /// duplicate records, wrong epochs, or invalid subsystem values leave the old registry
    /// untouched.
    /// </summary>
    public void RestoreStates(
        IEnumerable<VesselSystemsState> states,
        IEnumerable<string> knownVesselIds,
        double committedEpoch)
    {
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(knownVesselIds);
        ValidateEpoch(committedEpoch);

        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (string vesselId in knownVesselIds)
        {
            ValidateVesselId(vesselId);
            known.Add(vesselId);
        }

        var replacement = new Dictionary<string, VesselSystemsRuntime>(
            StringComparer.Ordinal);
        foreach (var state in states)
        {
            ArgumentNullException.ThrowIfNull(state);
            state.Validate();
            ValidateVesselId(state.VesselId);
            if (!known.Contains(state.VesselId))
                throw new InvalidDataException(
                    $"Systems state references unknown vessel '{state.VesselId}'.");
            EnsureEpoch(state.SimulationTime, committedEpoch, state.VesselId);
            if (replacement.ContainsKey(state.VesselId))
                throw new InvalidDataException(
                    $"Duplicate systems state for vessel '{state.VesselId}'.");

            var runtime = new VesselSystemsRuntime(state.VesselId);
            runtime.RestoreState(state);
            replacement.Add(state.VesselId, runtime);
        }

        _runtimes.Clear();
        foreach (var (vesselId, runtime) in replacement)
            _runtimes.Add(vesselId, runtime);
    }

    private static void ValidateVesselId(string vesselId)
    {
        if (string.IsNullOrWhiteSpace(vesselId))
            throw new ArgumentException("Vessel id is required.", nameof(vesselId));
    }

    private static void ValidateEpoch(double epoch)
    {
        if (!double.IsFinite(epoch) || epoch < 0.0)
            throw new ArgumentOutOfRangeException(nameof(epoch));
    }

    private static void EnsureEpoch(
        double actual,
        double expected,
        string vesselId)
    {
        if (!double.IsFinite(actual)
            || System.Math.Abs(actual - expected) > EpochToleranceSeconds)
        {
            throw new InvalidOperationException(
                $"Systems runtime '{vesselId}' is at epoch {actual:R}, "
                + $"expected {expected:R}.");
        }
    }
}
