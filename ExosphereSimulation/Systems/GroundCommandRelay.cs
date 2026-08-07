namespace Exosphere.Simulation.Systems;

using Exosphere.Simulation.Math;

/// <summary>
/// One-way light-time delay for ground-link player commands.
/// Onboard guidance (ascent assist, EDL, SAS profiles) must write vessel controls
/// directly and bypass this relay — plasma blackout kills the uplink, not the FCS.
/// </summary>
public sealed class GroundCommandRelay
{
    /// <summary>
    /// Delays below this threshold apply immediately. LEO one-way light time is
    /// a few milliseconds; gameplay latency only matters past cis-lunar scales.
    /// </summary>
    public const double ImmediateThresholdSeconds = 0.05;

    private readonly Queue<(double ApplyAt, Vector3d Pyr)> _attitude = new();
    private readonly Queue<(double ApplyAt, double Delta)> _throttle = new();

    public Vector3d LastAppliedAttitude { get; private set; }
    public int PendingAttitudeCount => _attitude.Count;
    public int PendingThrottleCount => _throttle.Count;
    public bool HasPending => _attitude.Count > 0 || _throttle.Count > 0;

    /// <summary>
    /// Queue or immediately apply an attitude stick sample.
    /// Dropped entirely when <paramref name="linkUp"/> is false (LOS / blackout).
    /// </summary>
    public void SubmitAttitude(double now, double delaySeconds, Vector3d pyr, bool linkUp,
        Action<Vector3d>? applyNow = null)
    {
        if (!linkUp) return;
        if (!double.IsFinite(now) || !double.IsFinite(delaySeconds)) return;

        if (delaySeconds < ImmediateThresholdSeconds)
        {
            LastAppliedAttitude = pyr;
            applyNow?.Invoke(pyr);
            return;
        }

        _attitude.Enqueue((now + delaySeconds, pyr));
    }

    /// <summary>
    /// Queue or immediately apply a throttle delta (same units as a single frame's
    /// <c>ThrottleUp</c>/<c>ThrottleDown</c> step). Dropped when link is down.
    /// </summary>
    public void SubmitThrottleDelta(double now, double delaySeconds, double delta, bool linkUp,
        Action<double>? applyNow = null)
    {
        if (!linkUp) return;
        if (!double.IsFinite(now) || !double.IsFinite(delaySeconds) || !double.IsFinite(delta))
            return;
        if (delta == 0.0) return;

        if (delaySeconds < ImmediateThresholdSeconds)
        {
            applyNow?.Invoke(delta);
            return;
        }

        _throttle.Enqueue((now + delaySeconds, delta));
    }

    /// <summary>Apply every command whose light-time arrival has elapsed.</summary>
    public void Tick(double now, Action<Vector3d>? applyAttitude = null,
        Action<double>? applyThrottleDelta = null)
    {
        if (!double.IsFinite(now)) return;

        while (_attitude.Count > 0 && _attitude.Peek().ApplyAt <= now)
        {
            var sample = _attitude.Dequeue();
            LastAppliedAttitude = sample.Pyr;
            applyAttitude?.Invoke(sample.Pyr);
        }

        while (_throttle.Count > 0 && _throttle.Peek().ApplyAt <= now)
        {
            var sample = _throttle.Dequeue();
            applyThrottleDelta?.Invoke(sample.Delta);
        }
    }

    public void Clear()
    {
        _attitude.Clear();
        _throttle.Clear();
    }
}
