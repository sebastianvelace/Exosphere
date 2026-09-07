namespace Exosphere.Simulation.Visual;

using Exosphere.Simulation.Math;

/// <summary>Presentation-only damping, measured in wall-clock seconds.</summary>
public static class CameraSmoothing
{
    /// <summary>
    /// Exact first-order response to a held target. Splitting a time interval into
    /// render frames leaves the response unchanged; no elapsed time means no motion.
    /// </summary>
    public static double Blend(double deltaSeconds, double ratePerSecond)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0.0
            || !double.IsFinite(ratePerSecond) || ratePerSecond <= 0.0)
            return 0.0;
        return 1.0 - System.Math.Exp(-ratePerSecond * deltaSeconds);
    }

    /// <summary>Continuous shake oscillator; frequency is radians per second.</summary>
    public static double Oscillator(double elapsedSeconds, double angularFrequency, double phase)
        => System.Math.Sin(elapsedSeconds * angularFrequency + phase);
}

/// <summary>Starts each cockpit visit at the current attitude, then damps tracking.</summary>
public sealed class CameraOrientationSmoother
{
    private Quaterniond _orientation;
    private bool _initialized;

    public void Reset() => _initialized = false;

    public Quaterniond Update(Quaterniond target, double deltaSeconds)
    {
        if (!_initialized)
        {
            _orientation = target.Normalize();
            _initialized = true;
        }
        else
        {
            _orientation = _orientation.Slerp(target, CameraSmoothing.Blend(deltaSeconds, 8.0));
        }
        return _orientation;
    }
}
