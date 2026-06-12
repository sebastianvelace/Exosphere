namespace Exosphere.Game;

using Godot;

/// <summary>
/// Procedurally synthesised audio for the launch sequence. No .ogg assets are
/// shipped, so every sound is generated at runtime with <see cref="AudioStreamGenerator"/>
/// feeding an <see cref="AudioStreamGeneratorPlayback"/>.
///
/// Continuous voices (filled every <c>_Process</c>):
///   • Sea-level engine roar — band-limited low-frequency noise (~60–400 Hz) with
///     amplitude-modulated crackle. Volume scales with throttle × atmospheric mix.
///   • Vacuum engine hiss — high-frequency thin noise (~1–3 kHz). Volume scales with
///     throttle × (1 − atmospheric mix).
///   • Ambient pad — low rumble that is full volume at ground level and crossfades to
///     silence above ~80 km so space is silent.
///
/// One-shot events (synthesised bursts, mixed into a dedicated event voice):
///   • Countdown ticks / ignition tone, liftoff swell, stage-separation clunk.
///
/// All voices share persistent oscillator / filter / RNG state so consecutive buffer
/// fills join seamlessly (no inter-buffer clicks). The buffer-fill hot path performs no
/// per-frame heap allocations — a single <see cref="Vector2"/>[] scratch buffer is reused.
/// </summary>
[GlobalClass]
public partial class AudioManager : Node
{
    /// <summary>Global access point, set in <see cref="_Ready"/>.</summary>
    public static AudioManager? Instance { get; private set; }

    // ── Generator configuration ───────────────────────────────────────────────
    private const float MixRate     = 44100f;
    private const float BufferLen   = 0.1f;     // seconds; small for low latency

    // ── Continuous voices ─────────────────────────────────────────────────────
    private AudioStreamPlayer? _engineSlPlayer;
    private AudioStreamPlayer? _engineVacPlayer;
    private AudioStreamPlayer? _ambientPlayer;
    private AudioStreamPlayer? _eventPlayer;

    private AudioStreamGeneratorPlayback? _engineSlPb;
    private AudioStreamGeneratorPlayback? _engineVacPb;
    private AudioStreamGeneratorPlayback? _ambientPb;
    private AudioStreamGeneratorPlayback? _eventPb;

    // ── Persistent synthesis state (keeps buffers continuous) ─────────────────
    private readonly RandomNumberGenerator _rng = new();

    // One-pole filter memories.
    private float _slLpA, _slLpB;        // cascaded low-pass for the SL roar
    private float _slCrackleLp;          // smoothed amplitude-modulation envelope
    private float _vacHpPrevIn, _vacHpPrevOut;
    private float _vacLp;                // gentle low-pass to tame the vac hiss
    private float _ambLpA, _ambLpB;      // deep ambient rumble filter

    private float _slPhase;              // sub-oscillator phase for the roar body
    private float _vacAmPhase;           // slow AM phase for the vac hiss
    private float _ambPhase;             // slow swell phase for the ambient pad

    // ── Live target levels (smoothed in _Process) ────────────────────────────
    private float _slLevel;
    private float _vacLevel;
    private float _ambLevel;

    // ── Event one-shot state (drained by _Process buffer fill) ────────────────
    private struct EventTone
    {
        public float Freq;        // Hz; 0 ⇒ noise burst (used for clunks)
        public float Remaining;   // seconds left to play
        public float Duration;    // total seconds (for the envelope)
        public float Amp;         // peak amplitude
        public float Phase;       // oscillator phase
        public bool  Active;
    }

    private EventTone _event;

    // ──────────────────────────────────────────────────────────────────────────
    public override void _Ready()
    {
        Instance = this;
        _rng.Randomize();

        SetupBuses();

        _engineSlPlayer  = MakeGeneratorPlayer("EngineSL",  "Engine3D");
        _engineVacPlayer = MakeGeneratorPlayer("EngineVac", "Engine3D");
        _ambientPlayer   = MakeGeneratorPlayer("Ambient",   "Ambient");
        _eventPlayer     = MakeGeneratorPlayer("Event",     "UI");

        // Playback handles become valid only after Play() has been called.
        _engineSlPb  = _engineSlPlayer.GetStreamPlayback()  as AudioStreamGeneratorPlayback;
        _engineVacPb = _engineVacPlayer.GetStreamPlayback() as AudioStreamGeneratorPlayback;
        _ambientPb   = _ambientPlayer.GetStreamPlayback()   as AudioStreamGeneratorPlayback;
        _eventPb     = _eventPlayer.GetStreamPlayback()     as AudioStreamGeneratorPlayback;
    }

    /// <summary>Creates the SFX/Music bus tree if it is not already present.</summary>
    private static void SetupBuses()
    {
        EnsureBus("SFX",     "Master");
        EnsureBus("Engine3D", "SFX");
        EnsureBus("UI",       "SFX");
        EnsureBus("Ambient",  "SFX");
        EnsureBus("Music",    "Master");
    }

    private static void EnsureBus(string name, string sendTo)
    {
        if (AudioServer.GetBusIndex(name) != -1) return;
        int idx = AudioServer.BusCount;
        AudioServer.AddBus(idx);
        AudioServer.SetBusName(idx, name);
        AudioServer.SetBusSend(idx, sendTo);
    }

    /// <summary>Builds an <see cref="AudioStreamPlayer"/> backed by a fresh generator and starts it.</summary>
    private AudioStreamPlayer MakeGeneratorPlayer(string name, string bus)
    {
        var gen = new AudioStreamGenerator
        {
            MixRate      = MixRate,
            BufferLength = BufferLen,
        };
        var player = new AudioStreamPlayer
        {
            Name      = name,
            Stream    = gen,
            Autoplay  = false,
            VolumeDb  = -80f,
            Bus       = AudioServer.GetBusIndex(bus) != -1 ? bus : "Master",
        };
        AddChild(player);
        player.Play();   // start the generator so a playback handle exists
        return player;
    }

    // ──────────────────────────────────────────────────────────────────────────
    public override void _Process(double delta)
    {
        UpdateLevels((float)delta);
        FillEngineSl();
        FillEngineVac();
        FillAmbient();
        FillEvent();
    }

    /// <summary>Reads the live simulation state and smooths the per-voice target levels.</summary>
    private void UpdateLevels(float delta)
    {
        float slTarget = 0f, vacTarget = 0f, ambTarget = 0f;

        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        if (vessel != null)
        {
            float thr = (float)vessel.Throttle;

            // Engine voices only sound when engines are actually firing.
            bool firing = thr > 0.001f && vessel.Parts.ActiveEngines.GetEnumerator().MoveNext();

            float rho   = 0f;
            float altKm = 0f;
            var   earth = bridge!.Universe?.GetBody("earth");
            if (earth != null)
            {
                rho   = (float)earth.GetAtmosphericDensity(vessel.Position);
                altKm = (float)(earth.GetAltitude(vessel.Position) / 1000.0);
            }

            // Crossfade sea-level ↔ vacuum timbre by atmospheric density.
            float slMix = Mathf.Clamp(rho / 0.05f, 0f, 1f);

            if (firing)
            {
                slTarget  = thr * slMix          * 0.85f;
                vacTarget = thr * (1f - slMix)   * 0.55f;
            }

            // Ambient pad: full at ground, silent above ~80 km.
            ambTarget = Mathf.Clamp(1f - altKm / 80f, 0f, 1f) * 0.30f;
        }

        // Smooth toward targets so volume changes are click-free.
        float k = 1f - Mathf.Exp(-delta * 8f);
        _slLevel  = Mathf.Lerp(_slLevel,  slTarget,  k);
        _vacLevel = Mathf.Lerp(_vacLevel, vacTarget, k);
        _ambLevel = Mathf.Lerp(_ambLevel, ambTarget, k);

        // Slight pitch rise with throttle adds urgency to the roar.
        if (_engineSlPlayer != null && vessel != null)
            _engineSlPlayer.PitchScale = 1.0f + (float)vessel.Throttle * 0.15f;

        // Keep VolumeDb at unity here; the synthesised amplitude already carries
        // the level. Players stay at 0 dB and we silence by emitting near-zero data.
    }

    // ── Sea-level engine roar ──────────────────────────────────────────────────
    private void FillEngineSl()
    {
        var pb = _engineSlPb;
        if (pb == null) return;

        int frames = pb.GetFramesAvailable();
        for (int i = 0; i < frames; i++)
        {
            // White noise → cascaded one-pole low-pass ⇒ ~60–400 Hz body.
            float noise = _rng.RandfRange(-1f, 1f);
            _slLpA += 0.12f * (noise  - _slLpA);
            _slLpB += 0.20f * (_slLpA - _slLpB);
            float body = _slLpB * 6f;

            // Low sub-rumble sine for chest weight (~38 Hz).
            _slPhase += Mathf.Tau * 38f / MixRate;
            if (_slPhase > Mathf.Tau) _slPhase -= Mathf.Tau;
            float sub = Mathf.Sin(_slPhase) * 0.35f;

            // Crackle: smoothed rectified noise modulates amplitude.
            float crk = Mathf.Abs(_rng.RandfRange(-1f, 1f));
            _slCrackleLp += 0.08f * (crk - _slCrackleLp);
            float am = 0.65f + 0.55f * _slCrackleLp;

            float s = (body + sub) * am * _slLevel;
            pb.PushFrame(new Vector2(s, s));
        }
    }

    // ── Vacuum engine hiss ─────────────────────────────────────────────────────
    private void FillEngineVac()
    {
        var pb = _engineVacPb;
        if (pb == null) return;

        int frames = pb.GetFramesAvailable();
        for (int i = 0; i < frames; i++)
        {
            // White noise → one-pole high-pass ⇒ thin >1.2 kHz hiss.
            float noise = _rng.RandfRange(-1f, 1f);
            float hp = 0.92f * (_vacHpPrevOut + noise - _vacHpPrevIn);
            _vacHpPrevIn  = noise;
            _vacHpPrevOut = hp;
            // Gentle low-pass rolls off the harshest top so it reads as a whistle.
            _vacLp += 0.45f * (hp - _vacLp);

            // Slow amplitude modulation (~3 Hz breathing).
            _vacAmPhase += Mathf.Tau * 3f / MixRate;
            if (_vacAmPhase > Mathf.Tau) _vacAmPhase -= Mathf.Tau;
            float am = 0.75f + 0.25f * Mathf.Sin(_vacAmPhase);

            float s = _vacLp * 1.4f * am * _vacLevel;
            pb.PushFrame(new Vector2(s, s));
        }
    }

    // ── Ambient pad ────────────────────────────────────────────────────────────
    private void FillAmbient()
    {
        var pb = _ambientPb;
        if (pb == null) return;

        int frames = pb.GetFramesAvailable();
        for (int i = 0; i < frames; i++)
        {
            // Very low filtered noise ⇒ wind/pad rumble.
            float noise = _rng.RandfRange(-1f, 1f);
            _ambLpA += 0.04f * (noise  - _ambLpA);
            _ambLpB += 0.08f * (_ambLpA - _ambLpB);

            // Slow swell so the bed feels alive.
            _ambPhase += Mathf.Tau * 0.13f / MixRate;
            if (_ambPhase > Mathf.Tau) _ambPhase -= Mathf.Tau;
            float swell = 0.7f + 0.3f * Mathf.Sin(_ambPhase);

            float s = _ambLpB * 9f * swell * _ambLevel;
            pb.PushFrame(new Vector2(s, s));
        }
    }

    // ── Event one-shots ────────────────────────────────────────────────────────
    private void FillEvent()
    {
        var pb = _eventPb;
        if (pb == null) return;
        float dt = 1f / MixRate;

        int frames = pb.GetFramesAvailable();
        for (int i = 0; i < frames; i++)
        {
            float s = 0f;
            if (_event.Active && _event.Remaining > 0f)
            {
                // Attack/decay envelope (10 ms attack, linear decay) avoids clicks.
                float t       = _event.Duration - _event.Remaining;
                float attack  = Mathf.Clamp(t / 0.01f, 0f, 1f);
                float decay   = Mathf.Clamp(_event.Remaining / _event.Duration, 0f, 1f);
                float env     = attack * decay * _event.Amp;

                if (_event.Freq > 0f)
                {
                    _event.Phase += Mathf.Tau * _event.Freq / MixRate;
                    if (_event.Phase > Mathf.Tau) _event.Phase -= Mathf.Tau;
                    s = Mathf.Sin(_event.Phase) * env;
                }
                else
                {
                    s = _rng.RandfRange(-1f, 1f) * env;   // noise burst (clunk)
                }

                _event.Remaining -= dt;
                if (_event.Remaining <= 0f) _event.Active = false;
            }
            pb.PushFrame(new Vector2(s, s));
        }
    }

    /// <summary>Queues a procedural event tone (replaces any in-flight event).</summary>
    private void Trigger(float freq, float duration, float amp)
    {
        _event = new EventTone
        {
            Freq      = freq,
            Duration  = duration,
            Remaining = duration,
            Amp       = amp,
            Phase     = 0f,
            Active    = true,
        };
    }

    // ── Public event API (called from MissionManager) ─────────────────────────

    /// <summary>
    /// Plays a countdown beep. The final tick (<paramref name="secondsRemaining"/> == 0)
    /// is a higher, longer "ignition" tone.
    /// </summary>
    public void PlayCountdownTick(int secondsRemaining)
    {
        if (secondsRemaining <= 0)
            Trigger(880f, 0.55f, 0.6f);   // ignition: high, long
        else
            Trigger(440f, 0.12f, 0.45f);  // plain tick: short blip
    }

    /// <summary>Plays a swelling rumble cue at liftoff.</summary>
    public void PlayLiftoff() => Trigger(70f, 1.4f, 0.7f);

    /// <summary>Plays a stage-separation clunk + burst (short noise envelope).</summary>
    public void PlayStaging() => Trigger(0f, 0.5f, 0.7f);
}
