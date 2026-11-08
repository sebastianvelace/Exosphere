namespace Exosphere.Simulation.Flight;

using System.Diagnostics;

/// <summary>Outcome of <see cref="PhysicsWarmup.Prime"/>; purely informational.</summary>
public sealed record PhysicsWarmupResult(bool Ran, int Steps, double ElapsedMs,
    string VariableMassStatus, string? Failure);

/// <summary>
/// Moves the first-use JIT cost of the production physics step out of the flight
/// (performance audit O9). The first powered steps after liftoff compile about a thousand
/// methods (≈ 64 ms in one 20 ms step on Release, 27 ms with ReadyToRun), which the player
/// sees as a hitch at the most watched moment of a launch.
///
/// Prime flies an isolated throwaway Flight 14 diagnostic for a few hundred steps while the
/// scene loads. It owns its own <see cref="Universe"/> and touches no gameplay state, so the
/// live simulation is unchanged bit for bit; compiled code is process-wide, so it runs once
/// per process. Any failure is swallowed and reported: a warm-up must never break loading.
/// It does not cover vehicle-specific paths the diagnostic never takes (capsule aero, docking).
/// </summary>
public static class PhysicsWarmup
{
    /// <summary>Steps past liftoff (~step 142) into the active variable-mass ascent.</summary>
    public const int DefaultSteps = 200;
    public const double StepSeconds = 0.02;

    private static readonly object Gate = new();
    private static PhysicsWarmupResult? _result;

    /// <summary>Result of the process's warm-up, or null if it has not run.</summary>
    public static PhysicsWarmupResult? LastResult
    {
        get { lock (Gate) return _result; }
    }

    public static PhysicsWarmupResult Prime(string dataDirectory, int steps = DefaultSteps)
    {
        lock (Gate)
        {
            if (_result is { Ran: true }) return _result with { Ran = false };
            var watch = Stopwatch.StartNew();
            int done = 0;
            string status = "none";
            string? failure = null;
            try
            {
                var run = Flight14LaunchDiagnostic.Create(dataDirectory);
                for (; done < steps; done++) run.AdvanceFrame(StepSeconds);
                status = run.Ship.LastVariableMassStatus ?? "none";
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            _result = new PhysicsWarmupResult(true, done, watch.Elapsed.TotalMilliseconds, status, failure);
            return _result;
        }
    }
}
