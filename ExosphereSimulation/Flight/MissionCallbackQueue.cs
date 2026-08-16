namespace Exosphere.Simulation.Flight;

using System.IO;

/// <summary>Serializable record for one mission callback delivery.</summary>
public sealed class MissionCallbackState
{
    public long Sequence { get; set; }
    public string EventType { get; set; } = "";
    public string Payload { get; set; } = "";
    public double SimulationTime { get; set; }
    public bool Delivered { get; set; }

    public MissionCallbackState Clone() => new()
    {
        Sequence = Sequence,
        EventType = EventType,
        Payload = Payload,
        SimulationTime = SimulationTime,
        Delivered = Delivered,
    };
}

/// <summary>Versioned callback-log state embedded in MissionSaveV2.</summary>
public sealed class MissionCallbackQueueState
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public long NextSequence { get; set; } = 1;
    public List<MissionCallbackState> Events { get; set; } = new();

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported callback schema {SchemaVersion}.");
        if (NextSequence < 1)
            throw new InvalidDataException("Callback sequence must start at one.");

        var sequences = new HashSet<long>();
        foreach (var callback in Events ?? throw new InvalidDataException(
                     "Callback event collection is null."))
        {
            if (callback == null)
                throw new InvalidDataException("Callback event is null.");
            if (callback.Sequence < 1 || callback.Sequence >= NextSequence
                || !sequences.Add(callback.Sequence))
            {
                throw new InvalidDataException(
                    $"Invalid or duplicate callback sequence {callback.Sequence}.");
            }
            if (string.IsNullOrWhiteSpace(callback.EventType)
                || callback.Payload == null
                || !double.IsFinite(callback.SimulationTime)
                || callback.SimulationTime < 0.0)
            {
                throw new InvalidDataException(
                    $"Invalid callback payload for sequence {callback.Sequence}.");
            }
        }
    }
}

/// <summary>
/// Ordered mission callback log. Current gameplay can publish and deliver synchronously;
/// future deferred dispatch can enqueue without delivery and wake on <see cref="HasPending"/>.
/// </summary>
public sealed class MissionCallbackQueue
{
    private long _nextSequence = 1;
    private readonly List<MissionCallbackState> _events = new();

    public bool HasPending => _events.Any(callback => !callback.Delivered);
    public int Count => _events.Count;

    public MissionCallbackState Enqueue(
        string eventType,
        string payload,
        double simulationTime)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("Callback event type is required.", nameof(eventType));
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));
        if (!double.IsFinite(simulationTime) || simulationTime < 0.0)
            throw new ArgumentOutOfRangeException(nameof(simulationTime));

        var callback = new MissionCallbackState
        {
            Sequence = _nextSequence++,
            EventType = eventType,
            Payload = payload,
            SimulationTime = simulationTime,
            Delivered = false,
        };
        _events.Add(callback);
        return callback.Clone();
    }

    /// <summary>Publishes one callback while preserving the current synchronous behavior.</summary>
    public MissionCallbackState Publish(
        string eventType,
        string payload,
        double simulationTime,
        Action deliver)
    {
        ArgumentNullException.ThrowIfNull(deliver);
        MissionCallbackState callback = Enqueue(eventType, payload, simulationTime);
        deliver();
        MarkDelivered(callback.Sequence);
        callback.Delivered = true;
        return callback;
    }

    public void MarkDelivered(long sequence)
    {
        var callback = _events.FirstOrDefault(item => item.Sequence == sequence)
            ?? throw new InvalidOperationException(
                $"Unknown mission callback sequence {sequence}.");
        callback.Delivered = true;
    }

    /// <summary>
    /// Delivers pending callbacks in sequence order. If delivery throws, the current event
    /// remains pending and later events are not reordered or silently dropped.
    /// </summary>
    public void DispatchPending(Action<MissionCallbackState> deliver)
    {
        ArgumentNullException.ThrowIfNull(deliver);
        var pending = _events.Where(item => !item.Delivered).ToArray();
        foreach (var callback in pending)
        {
            deliver(callback.Clone());
            MarkDelivered(callback.Sequence);
        }
    }

    public MissionCallbackQueueState CaptureState() => new()
    {
        NextSequence = _nextSequence,
        Events = _events.Select(callback => callback.Clone()).ToList(),
    };

    public void RestoreState(MissionCallbackQueueState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Validate();
        _nextSequence = state.NextSequence;
        _events.Clear();
        _events.AddRange(state.Events.Select(callback => callback.Clone()));
    }

    public void Clear()
    {
        _nextSequence = 1;
        _events.Clear();
    }
}
