namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Propulsion;

/// <summary>
/// Delayed onboard observation of an active-stage engine-out condition.
/// The propulsion model owns the failure state; this sensor only observes it and
/// debounces the observation before guidance is allowed to react.
/// </summary>
public sealed class EngineOutSensor
{
    public const double DefaultDetectionLatencySeconds = 0.10;
    public const double DefaultMinimumIsolationConfidence = 0.70;
    public const double DefaultMinimumTorqueResidualRatio =
        EngineFaultIsolation.DefaultMinimumTorqueResidualRatio;

    private double _rawConditionSeconds;
    private double _corroboratedConditionSeconds;

    public EngineOutSensor(
        double detectionLatencySeconds = DefaultDetectionLatencySeconds,
        double minimumIsolationConfidence = DefaultMinimumIsolationConfidence,
        double minimumTorqueResidualRatio = DefaultMinimumTorqueResidualRatio)
    {
        if (!double.IsFinite(detectionLatencySeconds) || detectionLatencySeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(detectionLatencySeconds));

        DetectionLatencySeconds = detectionLatencySeconds;
        if (!double.IsFinite(minimumIsolationConfidence)
            || minimumIsolationConfidence <= 0.0
            || minimumIsolationConfidence > 1.0)
            throw new ArgumentOutOfRangeException(nameof(minimumIsolationConfidence));
        if (!double.IsFinite(minimumTorqueResidualRatio)
            || minimumTorqueResidualRatio < 0.0)
            throw new ArgumentOutOfRangeException(nameof(minimumTorqueResidualRatio));

        MinimumIsolationConfidence = minimumIsolationConfidence;
        MinimumTorqueResidualRatio = minimumTorqueResidualRatio;
    }

    public double DetectionLatencySeconds { get; }
    public double MinimumIsolationConfidence { get; }
    public double MinimumTorqueResidualRatio { get; }
    public EngineOutSensorState LastState { get; private set; }

    public EngineOutSensorState Sample(Vessel vessel, double deltaSeconds)
        => Sample(vessel, body: null, deltaSeconds);

    public EngineOutSensorState Sample(
        Vessel vessel,
        CelestialBody? body,
        double deltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));

        var population = EngineOutRecoveryGuidance.Inspect(vessel);
        double ambientPressure = vessel.GetAmbientPressure(body);
        var telemetry = new List<EngineTelemetry>();
        var geometry = new List<EngineFaultIsolationGeometry>();
        foreach (var part in vessel.Parts.ActiveEngines)
        {
            if (!part.HasEngineRuntime) continue;
            telemetry.AddRange(part.GetEngineTelemetry(ambientPressure));
            foreach (var row in part.GetEngineInstanceFaultIsolationGeometrySnapshot())
                geometry.Add(new EngineFaultIsolationGeometry(
                    row.InstanceId,
                    row.PositionM,
                    row.ThrustDirection));
        }

        var isolation = EngineFaultIsolation.Infer(
            telemetry,
            geometry,
            vessel.Parts.CenterOfMass);
        bool isolatedCondition = isolation.FaultDetected
            && isolation.Confidence >= MinimumIsolationConfidence;
        bool torqueCorroborated = isolatedCondition
            && isolation.TorqueResidualRatio >= MinimumTorqueResidualRatio;
        bool rawCondition = population.IsEngineOut || isolatedCondition;
        if (rawCondition)
        {
            _rawConditionSeconds += deltaSeconds;
            if (torqueCorroborated)
                _corroboratedConditionSeconds += deltaSeconds;
            else
                _corroboratedConditionSeconds = 0.0;

            bool detected = _rawConditionSeconds + 1e-12 >= DetectionLatencySeconds
                && _corroboratedConditionSeconds + 1e-12 >= DetectionLatencySeconds;
            LastState = new EngineOutSensorState(
                population,
                isolation,
                RawEngineOut: true,
                TorqueCorroborated: torqueCorroborated,
                DetectedEngineOut: detected,
                RawConditionSeconds: _rawConditionSeconds,
                CorroboratedConditionSeconds: _corroboratedConditionSeconds);
        }
        else
        {
            _rawConditionSeconds = 0.0;
            _corroboratedConditionSeconds = 0.0;
            LastState = new EngineOutSensorState(
                population,
                isolation,
                RawEngineOut: false,
                TorqueCorroborated: false,
                DetectedEngineOut: false,
                RawConditionSeconds: 0.0,
                CorroboratedConditionSeconds: 0.0);
        }

        return LastState;
    }

    public void Reset()
    {
        _rawConditionSeconds = 0.0;
        _corroboratedConditionSeconds = 0.0;
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

        _previousCommand = observation.Population.IsEngineOut
            ? EngineOutRecoveryGuidance.ComputeCommand(
                vessel,
                targetWorldAxis,
                _previousCommand,
                deltaSeconds)
            : EngineOutRecoveryGuidance.ComputeCommandFromConfirmedFault(
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
    EngineFaultIsolationResult Isolation,
    bool RawEngineOut,
    bool TorqueCorroborated,
    bool DetectedEngineOut,
    double RawConditionSeconds,
    double CorroboratedConditionSeconds);
