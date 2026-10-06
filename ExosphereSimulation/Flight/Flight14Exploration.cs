namespace Exosphere.Simulation.Flight;

/// <summary>
/// Playable engineering preview of the continuous loaded mission. Warp requests more
/// whole control steps, never rails, reseeding or larger actuator integration intervals.
/// Stops at the configured contact observation boundary; it does not assert recovery or capsize.
/// </summary>
public sealed class Flight14Exploration(Flight14LaunchDiagnostic run)
{
    public const string ProfileId = "starship-flight14-exploration";
    public Flight14LaunchDiagnostic Run { get; } = run;
    public bool IsObservingBooster { get; private set; }
    private bool _boosterBoundaryPaused;
    public Vessel ObservedVessel => IsObservingBooster && Run.BoosterReturnController?.Booster is { } booster ? booster : Run.Ship;
    public string ObservedPhase => IsObservingBooster ? Run.BoosterReturnController?.Phase.ToString() ?? "WaitingForSeparation" : Phase;
    public bool ObserveBooster(bool observe)
    {
        if (IsAdvancingToEntry) return false;
        if (observe && (Run.BoosterReturnController?.Booster == null
            || !IsObservingBooster && Run.BoosterReturnController.Booster.IsDestroyed)) return false;
        if (!observe && _boosterBoundaryPaused) { IsPaused = false; _boosterBoundaryPaused = false; }
        IsObservingBooster = observe; return true;
    }
    public bool IsPaused { get; set; }
    public bool IsTerminal => Run.PoweredReturnController?.Landing?.Phase is Flight14LandingPhase.TerminalReached or Flight14LandingPhase.SplashdownReached;
    public string? BlockReason => Run.PoweredReturnController?.BlockReason;
    public bool IsStopped => IsTerminal || BlockReason != null || Run.Ship.IsDestroyed;
    public double MissionElapsedSeconds => double.IsFinite(Run.Controller.LiftoffEpoch)
        ? System.Math.Max(0, Run.Universe.CurrentTime - Run.Controller.LiftoffEpoch) : 0;
    public string Phase => Run.PoweredReturnController?.Landing is { } landing ? landing.Phase.ToString()
        : Run.DescentController is { Phase: not Flight14DescentPhase.WaitingForEntry } descent ? descent.Phase.ToString()
        : Run.ReturnController is { Phase: not Flight14ReturnPhase.WaitingForPayload } returning ? returning.Phase.ToString()
        : Run.Controller.Phase == Flight14LaunchPhase.OrbitReady ? "Payload" + Run.PayloadController.Phase
        : Run.Controller.Phase.ToString();

    public bool IsAdvancingToEntry { get; private set; }
    public bool CanAdvanceToEntry => !IsStopped && !IsAdvancingToEntry
        && Run.PayloadController.Phase == Flight14PayloadPhase.Complete
        && Run.ReturnController is { EntryInterface: null };

    /// <summary>Request continuous, budgeted advancement after all payloads have been released.</summary>
    public bool BeginAdvanceToEntry()
    {
        if (!CanAdvanceToEntry) return false;
        IsObservingBooster = false;
        _boosterBoundaryPaused = false;
        _remainder = 0;
        IsPaused = false;
        IsAdvancingToEntry = true;
        return true;
    }

    /// <summary>Cancel at the last committed state, leaving the mission paused.</summary>
    public void CancelAdvanceToEntry()
    {
        if (!IsAdvancingToEntry) return;
        IsAdvancingToEntry = false;
        IsPaused = true;
        _remainder = 0;
    }

    /// <summary>
    /// Commit a bounded batch at the normal control cadence, regardless of selected warp.
    /// Stop on the first physically detected entry interface; never reseed the vehicle.
    /// </summary>
    public double AdvanceToEntry(int maximumSteps = Universe.MaxPassivePayloadBatchSteps)
    {
        if (maximumSteps < 1 || maximumSteps > Universe.MaxPassivePayloadBatchSteps)
            throw new ArgumentOutOfRangeException(nameof(maximumSteps));
        if (!IsAdvancingToEntry) return 0;
        if (IsPaused || IsStopped || Run.ReturnController?.EntryInterface != null)
        {
            CancelAdvanceToEntry();
            return 0;
        }
        var universe = Run.Universe;
        double requestedWarp = universe.TimeScale;
        double start = universe.CurrentTime;
        try
        {
            universe.TimeScale = 1;
            // Completed deployment/return controllers never read released satellites.
            // Keep residual drag with a conservative <=0.5 s passive-coast cap.
            universe.TickPassivePayloadBatch(maximumSteps,
                Run.PayloadController.Releases.Select(release => release.Satellite),
                () => IsStopped || Run.ReturnController?.EntryInterface != null);
            if (IsStopped || Run.ReturnController?.EntryInterface != null)
                CancelAdvanceToEntry();
        }
        finally { universe.TimeScale = requestedWarp; }
        return universe.CurrentTime - start;
    }

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
        if (IsAdvancingToEntry || IsPaused || IsStopped || frameSeconds == 0 || requestedWarp == 0) return 0;
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
                if (IsObservingBooster && Run.BoosterReturnController is { IsStopped: true })
                {
                    IsPaused = true; _boosterBoundaryPaused = true; pending = 0; break;
                }
            }
        }
        finally { universe.TimeScale = requestedWarp; }
        _remainder = IsStopped ? 0 : System.Math.Max(0, pending);
        return universe.CurrentTime - start;
    }
}
