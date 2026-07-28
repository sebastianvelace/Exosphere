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
    // ── Entry, Descent & Landing (driven by EDLController) ──
    ENTRY,
    PEAK_HEATING,
    AERO_DESCENT,
    RETRO_BURN,
    FINAL_DESCENT,
    LANDED,
    CRASHED,  // hard impact — vehicle lost
}

[GlobalClass]
public partial class MissionManager : Node
{
    public static MissionManager? Instance { get; private set; }

    public MissionPhase Phase        { get; private set; } = MissionPhase.PRE_LAUNCH;
    public double       CountdownTimer { get; private set; } = 10.0;
    public bool         IsCountingDown { get; private set; }
    public bool         IsCrashed => Phase == MissionPhase.CRASHED;

    [Signal] public delegate void PhaseChangedEventHandler(string phaseName);
    [Signal] public delegate void LaunchCommittedEventHandler();

    private bool _maxQTriggered;
    private int  _lastTickSecond = -1;   // last whole-second countdown beep emitted

    public override void _Ready()
    {
        Instance = this;

        // Spawn ExplosionController into the World node so it lives in 3D space.
        var worldNode = GetTree()?.Root.FindChild("World", true, false) as Node3D;
        if (worldNode != null)
        {
            var explosion = new ExplosionController { Name = "ExplosionController" };
            worldNode.CallDeferred("add_child", explosion);
        }

        if (SaveSystem.PendingMissionPhase is MissionPhase pending)
        {
            SaveSystem.PendingMissionPhase = null;
            EnterPhase(pending);
        }
    }

    // ── Public API ────────────────────────────────────────────────────────

    public void StartCountdown()
    {
        if (Phase != MissionPhase.PRE_LAUNCH) return;
        CountdownTimer  = 10.0;
        IsCountingDown  = true;
        _maxQTriggered  = false;
        _lastTickSecond = -1;
        SetPhase(MissionPhase.COUNTDOWN);
    }

    /// <summary>
    /// Lanzamiento manual (hold-[Z]): salta el countdown y arranca la FSM en LIFTOFF.
    /// La llama SimulationBridge cuando suelta los hold-downs por primera vez (TWR &gt; 1.02).
    /// Idempotente: si ya salió de PRE_LAUNCH (countdown en curso o ya en vuelo) no hace nada,
    /// para no pisar [L] (StartCountdown) ni reiniciar una misión en progreso.
    ///
    /// Manual launch (hold-[Z]): skips the countdown and starts the FSM at LIFTOFF.
    /// Called by SimulationBridge once it releases the hold-downs (TWR &gt; 1.02).
    /// Idempotent: does nothing unless we are still in PRE_LAUNCH.
    /// </summary>
    public void BeginFlight()
    {
        if (Phase != MissionPhase.PRE_LAUNCH) return;
        // No estábamos en countdown, pero reseteamos los gatillos de fase por si acaso.
        IsCountingDown  = false;
        _maxQTriggered  = false;
        SetPhase(MissionPhase.LIFTOFF);
        EmitSignal(SignalName.LaunchCommitted);
    }

    /// Call from SimulationBridge.TriggerStaging when a stage fires.
    /// MECO/separation timing for [G] is owned by AscentController; manual staging skips MECO.
    public void NotifyStaged()
    {
        if (Phase is MissionPhase.MECO or MissionPhase.ASCENT_SH or MissionPhase.MAX_Q)
            SetPhase(MissionPhase.SEPARATION);
    }

    /// Allows the EDLController to drive entry/descent/landing phases.
    public void EnterPhase(MissionPhase p) => SetPhase(p);

    /// True once an interplanetary/descent profile has begun — pauses ascent logic.
    public bool InDescent => Phase is MissionPhase.ENTRY or MissionPhase.PEAK_HEATING
        or MissionPhase.AERO_DESCENT or MissionPhase.RETRO_BURN
        or MissionPhase.FINAL_DESCENT or MissionPhase.LANDED or MissionPhase.CRASHED;

    // ── Process ───────────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        var bridge   = SimulationBridge.Instance;
        var vessel   = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (bridge == null || universe == null) return;

        // ── Crash detection (highest priority) ────────────────────────────────
        if (vessel != null && vessel.IsDestroyed && Phase != MissionPhase.CRASHED)
        {
            SetPhase(MissionPhase.CRASHED);
            return; // stop all ascent/descent logic
        }
        // CRASHED es terminal solo mientras el vessel siga destruido.
        // Si el vessel fue recreado/no-está-destruido (e.g. un descenso fue abortado y el
        // vessel rebotó de vuelta a órbita), limpiamos la fase para que el juego continúe.
        // CRASHED is terminal only while the vessel is actually destroyed; if the vessel
        // is not destroyed we fall through so normal ascent/descent logic can resume.
        if (Phase == MissionPhase.CRASHED)
        {
            if (vessel == null || vessel.IsDestroyed) return;
            // Vessel alive — clear the stuck CRASHED state so the player can fly again.
            SetPhase(MissionPhase.ORBIT);
        }

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
                SetPhase(MissionPhase.IGNITION);

            // Engine spool-up: ramp thrust over the final 3 s instead of snapping to full.
            if (Phase == MissionPhase.IGNITION)
                bridge.SetThrottle(System.Math.Clamp((3.0 - CountdownTimer) / 3.0, 0.0, 1.0));

            if (CountdownTimer <= 0.0)
            {
                CountdownTimer = 0.0;
                bridge.SetThrottle(1.0);

                // Release the hold-downs only once the engines can actually lift the stack
                // (thrust > weight), not merely because the clock hit zero.
                bool canLift = false;
                if (vessel != null)
                {
                    var rb = universe.GetDominantBody(vessel.Position);
                    canLift = vessel.GetThrustToWeightRatio(rb) > 1.02;
                }
                if (canLift)
                {
                    IsCountingDown = false;
                    bridge.ReleaseGroundHold();
                    SetPhase(MissionPhase.LIFTOFF);
                    EmitSignal(SignalName.LaunchCommitted);
                    // [L] owns the complete automatic mission: guidance and hot
                    // staging must start with the launch, not require a hidden [G].
                    AscentController.Instance?.Engage();
                }
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
        // COAST is set by AutopilotController on deorbit arm / post-burn — do not override here.
        switch (newPhase)
        {
            case MissionPhase.LIFTOFF:     AudioManager.Instance?.PlayLiftoff(); break;
            case MissionPhase.SEPARATION:  AudioManager.Instance?.PlayStaging(); break;
            case MissionPhase.RETRO_BURN:  AudioManager.Instance?.PlayRetroBurn(); break;
            case MissionPhase.ENTRY:       AudioManager.Instance?.PlayEntryInterface(); break;
            case MissionPhase.LANDED:      AudioManager.Instance?.PlayTouchdown(); break;
        }

        EmitSignal(SignalName.PhaseChanged, newPhase.ToString());

        if (newPhase == MissionPhase.CRASHED)
        {
            var activeVessel = SimulationBridge.Instance?.ActiveVessel;
            GD.Print($"[Mission] → CRASHED (VEHICLE LOST / CRASHED) (impact {activeVessel?.CrashImpactSpeed:F0} m/s)");
        }
        else
        {
            GD.Print($"[Mission] → {newPhase}");
        }
    }
}
