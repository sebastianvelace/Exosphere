namespace Exosphere.Game;

using Godot;
using System.Linq;

public enum MissionPhase
{
    PRE_LAUNCH,
    COUNTDOWN,
    IGNITION,
    LIFTOFF,
    ASCENT_SH,
    MAX_Q,
    MECO,
    SEPARATION,
    ASCENT_SHIP,
    ORBIT,
    COAST,
    LANDED,
}

[GlobalClass]
public partial class MissionManager : Node
{
    public static MissionManager? Instance { get; private set; }

    public MissionPhase Phase        { get; private set; } = MissionPhase.PRE_LAUNCH;
    public double       CountdownTimer { get; private set; } = 10.0;
    public bool         IsCountingDown { get; private set; }

    [Signal] public delegate void PhaseChangedEventHandler(string phaseName);
    [Signal] public delegate void LaunchCommittedEventHandler();

    private bool _maxQTriggered;
    private bool _mecoTriggered;
    private int  _lastTickSecond = -1;   // last whole-second countdown beep emitted

    public override void _Ready() => Instance = this;

    // ── Public API ────────────────────────────────────────────────────────

    public void StartCountdown()
    {
        if (Phase != MissionPhase.PRE_LAUNCH) return;
        CountdownTimer  = 10.0;
        IsCountingDown  = true;
        _maxQTriggered  = false;
        _mecoTriggered  = false;
        _lastTickSecond = -1;
        SetPhase(MissionPhase.COUNTDOWN);
    }

    /// Call from SimulationBridge.TriggerStaging when a stage fires.
    public void NotifyStaged()
    {
        if (Phase == MissionPhase.MECO)
            SetPhase(MissionPhase.SEPARATION);
    }

    // ── Process ───────────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        var bridge   = SimulationBridge.Instance;
        var vessel   = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (bridge == null || universe == null) return;

        // ── Countdown ──────────────────────────────────────────────────────
        if (IsCountingDown)
        {
            CountdownTimer -= delta;

            // Audio: beep once per whole second as the countdown crosses each integer.
            int sec = (int)System.Math.Ceiling(System.Math.Max(CountdownTimer, 0.0));
            if (sec != _lastTickSecond)
            {
                _lastTickSecond = sec;
                AudioManager.Instance?.PlayCountdownTick(sec);
            }

            if (CountdownTimer <= 3.0 && Phase == MissionPhase.COUNTDOWN)
            {
                SetPhase(MissionPhase.IGNITION);
                bridge.SetThrottle(1.0);
            }

            if (CountdownTimer <= 0.0)
            {
                CountdownTimer  = 0.0;
                IsCountingDown  = false;
                bridge.ReleaseGroundHold();
                SetPhase(MissionPhase.LIFTOFF);
                EmitSignal(SignalName.LaunchCommitted);
            }
        }

        if (vessel == null) return;

        var refBody   = universe.GetDominantBody(vessel.Position);
        double alt    = vessel.GetAltitude(refBody);
        double speed  = (vessel.Velocity - refBody.Velocity).Magnitude;

        // ── Phase auto-transitions ─────────────────────────────────────────
        switch (Phase)
        {
            case MissionPhase.LIFTOFF when alt > 150:
                SetPhase(MissionPhase.ASCENT_SH);
                break;

            case MissionPhase.ASCENT_SH:
                // Max-Q detection
                if (!_maxQTriggered && alt is > 8_000 and < 30_000 && refBody.Atmosphere != null)
                {
                    double density = refBody.Atmosphere.GetDensity(alt);
                    var    surfVel = vessel.GetSurfaceVelocity(refBody);
                    double dynQ   = 0.5 * density * surfVel.Magnitude * surfVel.Magnitude;
                    if (dynQ > 30_000)
                    {
                        _maxQTriggered = true;
                        SetPhase(MissionPhase.MAX_Q);
                    }
                }
                // Auto-MECO when SH propellant is nearly gone and we're high enough
                if (!_mecoTriggered && alt > 55_000)
                {
                    // After staging, SH part is gone — check if SH is still in vessel parts
                    bool shStillPresent = vessel.Parts.Parts.Any(p => p.Definition.Id == "super_heavy_booster");
                    if (shStillPresent)
                    {
                        var shPart = vessel.Parts.Parts.First(p => p.Definition.Id == "super_heavy_booster");
                        double shFuel = shPart.LiquidFuel + shPart.Oxidizer;
                        if (shFuel < 10_000)
                        {
                            _mecoTriggered = true;
                            bridge.SetThrottle(0.0);
                            SetPhase(MissionPhase.MECO);
                        }
                    }
                }
                break;

            case MissionPhase.MAX_Q when alt > 25_000:
                SetPhase(MissionPhase.ASCENT_SH);
                break;

            case MissionPhase.SEPARATION:
                // Auto-relight Starship engines after staging
                if (vessel.Parts.ActiveEngines.Any())
                {
                    bridge.SetThrottle(1.0);
                    SetPhase(MissionPhase.ASCENT_SHIP);
                }
                break;

            case MissionPhase.ASCENT_SHIP when alt > 150_000 && speed > 7_500:
                SetPhase(MissionPhase.ORBIT);
                break;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void SetPhase(MissionPhase newPhase)
    {
        Phase = newPhase;

        // Audio cues on phase entry (null-safe; AudioManager may not exist yet).
        switch (newPhase)
        {
            case MissionPhase.LIFTOFF:    AudioManager.Instance?.PlayLiftoff(); break;
            case MissionPhase.SEPARATION: AudioManager.Instance?.PlayStaging(); break;
        }

        EmitSignal(SignalName.PhaseChanged, newPhase.ToString());
        GD.Print($"[Mission] → {newPhase}");
    }
}
