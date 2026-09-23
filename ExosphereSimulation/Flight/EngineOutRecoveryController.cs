namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;

/// <summary>
/// Delayed onboard observation of an active-stage engine-out condition.
/// The propulsion model owns the failure state; this sensor only observes it and
/// debounces the observation before guidance is allowed to react.
/// </summary>
public sealed class EngineOutSensor
{
    public const double DefaultDetectionLatencySeconds = 0.10;

    private double _rawConditionSeconds;

    public EngineOutSensor(double detectionLatencySeconds = DefaultDetectionLatencySeconds)
    {
        if (!double.IsFinite(detectionLatencySeconds) || detectionLatencySeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(detectionLatencySeconds));

        DetectionLatencySeconds = detectionLatencySeconds;
    }

    public double DetectionLatencySeconds { get; }
    public EngineOutSensorState LastState { get; private set; }

    public EngineOutSensorState Sample(Vessel vessel, double deltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));

        var population = EngineOutRecoveryGuidance.Inspect(vessel);
        bool rawCondition = population.IsEngineOut;
        if (rawCondition)
        {
            _rawConditionSeconds += deltaSeconds;
            if (_rawConditionSeconds + 1e-12 >= DetectionLatencySeconds)
                LastState = new EngineOutSensorState(
                    population,
                    RawEngineOut: true,
                    DetectedEngineOut: true,
                    RawConditionSeconds: _rawConditionSeconds);
            else
                LastState = new EngineOutSensorState(
                    population,
                    RawEngineOut: true,
                    DetectedEngineOut: false,
                    RawConditionSeconds: _rawConditionSeconds);
        }
        else
        {
            _rawConditionSeconds = 0.0;
            LastState = new EngineOutSensorState(
                population,
                RawEngineOut: false,
                DetectedEngineOut: false,
                RawConditionSeconds: 0.0);
        }

        return LastState;
    }

    public void Reset()
    {
        _rawConditionSeconds = 0.0;
        LastState = default;
    }
}

/// <summary>Onboard engine-out observation plus bounded attitude recovery.</summary>
public sealed class EngineOutRecoveryController
{
    private Vector3d _previousCommand;

    public EngineOutRecoveryController(
        double detectionLatencySeconds = EngineOutSensor.DefaultDetectionLatencySeconds)
    {
        Sensor = new EngineOutSensor(detectionLatencySeconds);
    }

    public EngineOutSensor Sensor { get; }
    public Vector3d LastCommand => _previousCommand;

    public Vector3d ComputeCommand(
        Vessel vessel,
        Vector3d targetWorldAxis,
        double deltaSeconds)
    {
        var observation = Sensor.Sample(vessel, deltaSeconds);
        if (!observation.DetectedEngineOut)
        {
            _previousCommand = Vector3d.Zero;
            return _previousCommand;
        }

        _previousCommand = EngineOutRecoveryGuidance.ComputeCommand(
            vessel,
            targetWorldAxis,
            _previousCommand,
            deltaSeconds);
        return _previousCommand;
    }

    public void Reset()
    {
        Sensor.Reset();
        _previousCommand = Vector3d.Zero;
    }
}

public readonly record struct EngineOutSensorState(
    EngineOutRecoveryState Population,
    bool RawEngineOut,
    bool DetectedEngineOut,
    double RawConditionSeconds);
