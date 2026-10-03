namespace Exosphere.Simulation.Flight;

/// <summary>
/// Playable engineering preview of the continuous loaded mission. Warp requests more
/// whole control steps, never rails, reseeding or larger actuator integration intervals.
/// Stops at the diagnostic boundary; it does not assert a water recovery.
/// </summary>
public sealed class Flight14Exploration(Flight14LaunchDiagnostic run)
{
    public const string ProfileId = "starship-flight14-exploration";
    public Flight14LaunchDiagnostic Run { get; } = run;
    public bool IsPaused { get; set; }
    public bool IsTerminal => Run.PoweredReturnController?.Landing?.Phase == Flight14LandingPhase.TerminalReached;
    public string? BlockReason => Run.PoweredReturnController?.BlockReason;
    public bool IsStopped => IsTerminal || BlockReason != null || Run.Ship.IsDestroyed;
    public double MissionElapsedSeconds => double.IsFinite(Run.Controller.LiftoffEpoch)
        ? System.Math.Max(0, Run.Universe.CurrentTime - Run.Controller.LiftoffEpoch) : 0;
    public string Phase => Run.PoweredReturnController?.Landing is { } landing ? landing.Phase.ToString()
        : Run.DescentController is { Phase: not Flight14DescentPhase.WaitingForEntry } descent ? descent.Phase.ToString()
        : Run.ReturnController is { Phase: not Flight14ReturnPhase.WaitingForPayload } returning ? returning.Phase.ToString()
        : Run.Controller.Phase == Flight14LaunchPhase.OrbitReady ? "Payload" + Run.PayloadController.Phase
        : Run.Controller.Phase.ToString();

    private double _remainder;

    /// <returns>Actual committed simulation seconds, for rate-dependent game systems.</returns>
    public double AdvanceFrame(double frameSeconds, int maximumSteps = 100)
    {
        if (!double.IsFinite(frameSeconds) || frameSeconds < 0 || maximumSteps < 1)
            throw new ArgumentOutOfRangeException(nameof(frameSeconds));
        var universe = Run.Universe;
        double requestedWarp = universe.TimeScale;
        if (!double.IsFinite(requestedWarp) || requestedWarp < 0 || requestedWarp > 200)
            throw new ArgumentOutOfRangeException(nameof(universe.TimeScale));
        if (IsPaused || IsStopped || frameSeconds == 0 || requestedWarp == 0) return 0;
        double period = Universe.DeterministicControlPeriodSeconds;
        // Bound CPU work and queued latency. A requested acceleration can run slower on
        // a busy machine; the HUD clock always reflects committed physics, not this demand.
        double pending = _remainder + System.Math.Min(frameSeconds * requestedWarp, maximumSteps * period);
        double start = universe.CurrentTime;
        try
        {
            universe.TimeScale = 1;
            for (int step = 0; step < maximumSteps && pending + 1e-12 >= period && !IsStopped; step++)
            {
                universe.Tick(period);
                pending -= period;
            }
        }
        finally { universe.TimeScale = requestedWarp; }
        _remainder = IsStopped ? 0 : System.Math.Max(0, pending);
        return universe.CurrentTime - start;
    }
}
