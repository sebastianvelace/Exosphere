namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;

/// <summary>
/// Simulation-time schedule for the expensive entry footprint forecast. Attitude and
/// force integration remain at their existing control epochs; only the high-altitude
/// outer-loop forecast is held for at most 0.2 s. Terminal guidance always refreshes.
/// </summary>
public sealed class EntryPredictionRefresh
{
    public const double HighAltitudePeriodSeconds = 0.2;
    public const double TerminalAltitudeM = 25_000.0;
    private double _sampleTime = double.NaN;
    private Vector3d _relativePosition;
    private string? _vesselId;
    private string? _bodyId;
    public EntryCorridorGuidance.Prediction Prediction { get; private set; }
    public ulong SampleCount { get; private set; }

    public bool NeedsRefresh(double time, double altitudeM, string vesselId,
        string bodyId, Vector3d relativePosition) =>
        !double.IsFinite(_sampleTime) || !double.IsFinite(time)
        || !double.IsFinite(altitudeM)
        || !double.IsFinite(relativePosition.X) || !double.IsFinite(relativePosition.Y)
        || !double.IsFinite(relativePosition.Z)
        || time < _sampleTime || altitudeM <= TerminalAltitudeM
        || time - _sampleTime >= HighAltitudePeriodSeconds - 1e-9
        || _vesselId != vesselId || _bodyId != bodyId
        || (relativePosition - _relativePosition).MagnitudeSquared > 2_000.0 * 2_000.0;

    public void Store(double time, string vesselId, string bodyId,
        Vector3d relativePosition, EntryCorridorGuidance.Prediction prediction)
    {
        _sampleTime = time;
        _vesselId = vesselId;
        _bodyId = bodyId;
        _relativePosition = relativePosition;
        Prediction = prediction;
        SampleCount++;
    }
}
