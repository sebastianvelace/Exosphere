namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation.Physics;

/// <summary>
/// Procedurally synthesised audio for the whole flight. No .ogg assets are
/// shipped, so every sound is generated at runtime with <see cref="AudioStreamGenerator"/>
/// feeding an <see cref="AudioStreamGeneratorPlayback"/>.
///
/// Every continuous voice is driven by a real simulation quantity, never by a mission
/// phase flag, so what the crew hears tracks what the physics is doing:
///
///   • Sea-level engine roar — band-limited low-frequency noise (~60–400 Hz) with
///     amplitude-modulated crackle. Airborne path: level scales with throttle and with
///     ambient density, so it fades out as the atmosphere thins.
///   • Structure-borne engine rumble — what remains once there is no air to carry the
///     sound: conduction through the airframe. Dull, low-passed, no bright hiss. Level
///     scales with throttle and rises as the airborne path disappears.
///   • Aerodynamic airflow — broadband shear noise whose level follows dynamic pressure
///     q and whose brightness follows Mach. This makes Max-Q audibly the loudest moment
///     of ascent and gives entry its rising roar, with no scripting: q does the work.
///   • Re-entry plasma roar — deep, crackling noise gated on the SAME convective heat
///     flux (<see cref="ThermalModel.ComputeHeatFlux"/>) and the same thresholds that
///     <c>ReentryPlasmaController</c> uses for the fireball, so audio and visuals ignite
///     together.
///   • Buffet — amplitude modulation layered over airflow + plasma, peaking through the
///     transonic band and again under entry heating, so shaking is heard, not just seen.
///   • Ambient pad — ground-level rumble that crossfades to silence above ~80 km.
///
/// One-shot events (synthesised bursts, mixed into a dedicated event voice):
///   • Countdown ticks / ignition tone, liftoff swell, stage-separation clunk,
///     touchdown thud, crash/explosion burst.
///
/// All voices share persistent oscillator / filter / RNG state so consecutive buffer
/// fills join seamlessly (no inter-buffer clicks). The buffer-fill hot path performs no
/// per-frame heap allocations.
/// </summary>
[GlobalClass]
public partial class AudioManager : Node
{
    /// <summary>Global access point, set in <see cref="_Ready"/>.</summary>
    public static AudioManager? Instance { get; private set; }

    // ── Generator configuration ───────────────────────────────────────────────
    private const float MixRate     = 44100f;
    private const float BufferLen   = 0.1f;     // seconds; small for low latency

    // ── Physical reference points for the level mappings ──────────────────────

    /// <summary>Dynamic pressure (Pa) at which airflow noise saturates. Matches the
    /// project's Max-Q acceptance band (28–38 kPa), so Max-Q is the loudest point of
    /// ascent by construction rather than by a scripted cue.</summary>
    private const float MaxQRefPa = 35_000f;

    /// <summary>Mach at which airflow brightness saturates into a hard shear hiss.</summary>
    private const float MachRef = 5f;

    // Heat-flux gates (W/m²). Deliberately identical to ReentryPlasmaController's
    // FLUX_THRESH / FLUX_PEAK so the roar and the fireball ignite on the same physics.
    private const double FluxThreshold = 5.0e4;
    private const double FluxPeak      = 6.0e5;

    /// <summary>Density (kg/m³) above which the airborne engine path is fully carried.
    /// Below it the atmosphere is too thin to conduct the roar and the structure-borne
    /// path takes over.</summary>
    private const float AirborneDensityRef = 0.05f;

    // ── Continuous voices ─────────────────────────────────────────────────────
    private AudioStreamPlayer? _engineSlPlayer;
    private AudioStreamPlayer? _engineVacPlayer;
    private AudioStreamPlayer? _aeroPlayer;
    private AudioStreamPlayer? _ambientPlayer;
    private AudioStreamPlayer? _eventPlayer;

    private AudioStreamGeneratorPlayback? _engineSlPb;
    private AudioStreamGeneratorPlayback? _engineVacPb;
    private AudioStreamGeneratorPlayback? _aeroPb;
    private AudioStreamGeneratorPlayback? _ambientPb;
    private AudioStreamGeneratorPlayback? _eventPb;

    // ── Persistent synthesis state (keeps buffers continuous) ─────────────────
    private readonly RandomNumberGenerator _rng = new();

    // One-pole filter memories.
    private float _slLpA, _slLpB;        // cascaded low-pass for the SL roar
    private float _slCrackleLp;          // smoothed amplitude-modulation envelope
    private float _vacLpA, _vacLpB;      // cascaded low-pass for the structure-borne rumble
    private float _ambLpA, _ambLpB;      // deep ambient rumble filter
    private float _aeroLp;               // splits airflow noise into dull body / shear top
    private float _plasmaLpA, _plasmaLpB; // cascaded low-pass for the plasma roar
    private float _plasmaCrackle;        // decaying envelope for sparse ionisation pops
    private float _buffetEnv;            // smoothed random envelope driving the shake

    private float _slPhase;              // sub-oscillator phase for the roar body
    private float _vacRingPhase;         // metallic hull resonance for the structure path
    private float _ambPhase;             // slow swell phase for the ambient pad
    private float _plasmaSubPhase;       // sub-bass body under the plasma roar
    private float _buffetPhase;          // low-frequency shake carried in the buffet AM

    // ── Live target levels (smoothed in _Process) ────────────────────────────
    private float _slLevel;
    private float _vacLevel;
    private float _ambLevel;
    private float _aeroLevel;            // airflow noise gain, from dynamic pressure
    private float _plasmaLevel;          // plasma roar gain, from convective heat flux
    private float _aeroBright;           // 0 = subsonic rush, 1 = hypersonic shear hiss
    private float _buffetDepth;          // 0 = smooth flow, 1 = heavy shake

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
        _aeroPlayer      = MakeGeneratorPlayer("Aero",      "Aero");
        _ambientPlayer   = MakeGeneratorPlayer("Ambient",   "Ambient");
        _eventPlayer     = MakeGeneratorPlayer("Event",     "UI");

        // Playback handles become valid only after Play() has been called.
        _engineSlPb  = _engineSlPlayer.GetStreamPlayback()  as AudioStreamGeneratorPlayback;
        _engineVacPb = _engineVacPlayer.GetStreamPlayback() as AudioStreamGeneratorPlayback;
        _aeroPb      = _aeroPlayer.GetStreamPlayback()      as AudioStreamGeneratorPlayback;
        _ambientPb   = _ambientPlayer.GetStreamPlayback()   as AudioStreamGeneratorPlayback;
        _eventPb     = _eventPlayer.GetStreamPlayback()     as AudioStreamGeneratorPlayback;
    }

    /// <summary>Creates the SFX/Music bus tree if it is not already present.</summary>
    private static void SetupBuses()
    {
        EnsureBus("SFX",     "Master");
        EnsureBus("Engine3D", "SFX");
        EnsureBus("Aero",     "SFX");
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
        FillAero();
        FillAmbient();
        FillEvent();
    }

    /// <summary>Reads the live simulation state and smooths the per-voice target levels.</summary>
    private void UpdateLevels(float delta)
    {
        float slTarget    = 0f;
        float vacTarget   = 0f;
        float ambTarget   = 0f;
        float aeroTarget  = 0f;
        float plasmaTarget = 0f;
        float brightTarget = 0f;
        float buffetTarget = 0f;

        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        if (vessel != null && bridge!.Universe != null)
        {
            float thr = (float)vessel.Throttle;

            // Engine voices only sound when engines are actually firing.
            bool firing = thr > 0.001f && vessel.Parts.ActiveEngines.GetEnumerator().MoveNext();

            // Whatever body we are actually flying at — not always Earth. A Mars entry
            // has to sound like a Mars entry.
            var body = bridge.Universe.GetDominantBody(vessel.Position);

            double alt      = body.GetAltitude(vessel.Position);
            double rho      = body.GetAtmosphericDensity(vessel.Position);
            double airspeed = vessel.GetSurfaceVelocity(body).Magnitude;
            double q        = vessel.GetDynamicPressure(body);

            // Mach needs the local air temperature; a body with no atmosphere model has
            // no speed of sound to speak of, so treat it as vacuum.
            double mach = body.Atmosphere != null
                ? AerodynamicsModel.ComputeMach(airspeed, body.Atmosphere.GetTemperature(alt))
                : 0.0;

            // Same convective flux — and the same nose radius — the fireball is drawn from.
            double flux = ThermalModel.ComputeHeatFlux(
                rho, airspeed, System.Math.Max(0.1, vessel.MaximumDiameter * 0.5));

            // How much of the engine sound the air can still carry. This is the whole
            // airborne/structure-borne split: in vacuum it is zero.
            float airborne = Mathf.Clamp((float)rho / AirborneDensityRef, 0f, 1f);

            if (firing)
            {
                slTarget  = thr * airborne        * 0.85f;
                vacTarget = thr * (1f - airborne) * 0.40f;
            }

            // Airflow gain from dynamic pressure. The 0.6 exponent keeps the rush audible
            // early in ascent instead of slamming in only near Max-Q.
            float qNorm = Mathf.Clamp((float)q / MaxQRefPa, 0f, 1f);
            aeroTarget  = Mathf.Pow(qNorm, 0.6f) * 0.55f;

            // Brightness follows Mach: subsonic is a dull rush, hypersonic a hard shear hiss.
            brightTarget = Mathf.Clamp((float)mach / MachRef, 0f, 1f);

            // Plasma roar rides the flux gate the visuals already use.
            plasmaTarget = Mathf.Clamp(
                (float)((flux - FluxThreshold) / (FluxPeak - FluxThreshold)), 0f, 1f) * 0.7f;

            // Buffet has three sources, all gated on q because nothing shakes an airframe
            // in vacuum: the transonic band (roughest), the aerodynamic load itself (so
            // Max-Q shakes even though it sits past Mach 1), and entry turbulence.
            float transonic = Mathf.Exp(-Mathf.Pow(((float)mach - 1f) / 0.35f, 2f));
            buffetTarget = Mathf.Clamp(
                (transonic * 0.55f + qNorm * 0.35f + plasmaTarget * 0.5f) * qNorm, 0f, 0.75f);

            // Ambient pad: full at ground, silent above ~80 km.
            ambTarget = Mathf.Clamp(1f - (float)(alt / 80_000.0), 0f, 1f) * 0.30f;
        }

        // Smooth toward targets so volume changes are click-free.
        float k = 1f - Mathf.Exp(-delta * 8f);
        _slLevel     = Mathf.Lerp(_slLevel,     slTarget,     k);
        _vacLevel    = Mathf.Lerp(_vacLevel,    vacTarget,    k);
        _ambLevel    = Mathf.Lerp(_ambLevel,    ambTarget,    k);
        _aeroLevel   = Mathf.Lerp(_aeroLevel,   aeroTarget,   k);
        _plasmaLevel = Mathf.Lerp(_plasmaLevel, plasmaTarget, k);
        _buffetDepth = Mathf.Lerp(_buffetDepth, buffetTarget, k);

        // Timbre moves more slowly than level so the Mach sweep glides instead of stepping.
        float kb = 1f - Mathf.Exp(-delta * 2f);
        _aeroBright = Mathf.Lerp(_aeroBright, brightTarget, kb);

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

    // ── Structure-borne engine rumble (the vacuum path) ───────────────────────
    private void FillEngineVac()
    {
        var pb = _engineVacPb;
        if (pb == null) return;

        int frames = pb.GetFramesAvailable();
        for (int i = 0; i < frames; i++)
        {
            // With no air to carry it, the roar reaches the crew only by conduction
            // through the airframe. A hull is a low-pass filter: the bright top of the
            // spectrum never makes it, so this is a dull rumble, not an open-air hiss.
            float noise = _rng.RandfRange(-1f, 1f);
            _vacLpA += 0.09f * (noise   - _vacLpA);
            _vacLpB += 0.15f * (_vacLpA - _vacLpB);
            float body = _vacLpB * 5.5f;

            // A faint metallic resonance is the one thing the structure does add.
            _vacRingPhase += Mathf.Tau * 210f / MixRate;
            if (_vacRingPhase > Mathf.Tau) _vacRingPhase -= Mathf.Tau;
            float ring = Mathf.Sin(_vacRingPhase) * 0.12f;

            float s = (body + ring) * _vacLevel;
            pb.PushFrame(new Vector2(s, s));
        }
    }

    // ── Aerodynamic airflow + re-entry plasma ─────────────────────────────────
    private void FillAero()
    {
        var pb = _aeroPb;
        if (pb == null) return;

        int frames = pb.GetFramesAvailable();
        for (int i = 0; i < frames; i++)
        {
            // ── Airflow: split one noise source into a dull body and a shear top, then
            // crossfade by Mach. Subsonic flight rushes; hypersonic flight hisses.
            float noise = _rng.RandfRange(-1f, 1f);
            _aeroLp += 0.08f * (noise - _aeroLp);
            float dull  = _aeroLp * 5f;
            float shear = (noise - _aeroLp) * 0.9f;
            float airflow = Mathf.Lerp(dull, shear, _aeroBright) * _aeroLevel;

            // ── Plasma: deep filtered roar with sparse ionisation crackle on top.
            float pn = _rng.RandfRange(-1f, 1f);
            _plasmaLpA += 0.05f * (pn         - _plasmaLpA);
            _plasmaLpB += 0.10f * (_plasmaLpA - _plasmaLpB);
            float roar = _plasmaLpB * 7f;

            if (_rng.Randf() > 0.9992f) _plasmaCrackle = _rng.RandfRange(-1f, 1f);
            _plasmaCrackle *= 0.9985f;   // exponential decay of each pop

            _plasmaSubPhase += Mathf.Tau * 46f / MixRate;
            if (_plasmaSubPhase > Mathf.Tau) _plasmaSubPhase -= Mathf.Tau;
            float sub = Mathf.Sin(_plasmaSubPhase) * 0.30f;

            float plasma = (roar + sub + _plasmaCrackle * 0.5f) * _plasmaLevel;

            // ── Buffet: a slow random envelope plus a ~17 Hz shake, modulating both.
            // Depth is zero in smooth flow, so this costs nothing outside the rough bits.
            _buffetEnv += 0.004f * (Mathf.Abs(_rng.RandfRange(-1f, 1f)) - _buffetEnv);
            _buffetPhase += Mathf.Tau * 17f / MixRate;
            if (_buffetPhase > Mathf.Tau) _buffetPhase -= Mathf.Tau;
            float shake = 0.5f + 0.5f * Mathf.Sin(_buffetPhase);
            float am = 1f - _buffetDepth * (0.55f * _buffetEnv + 0.45f * shake);

            float s = Mathf.Clamp((airflow + plasma) * am, -1f, 1f);
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

    /// <summary>Plays the thud of the legs taking the vehicle's weight.</summary>
    public void PlayTouchdown() => Trigger(55f, 0.9f, 0.65f);

    /// <summary>Plays a broad noise burst for hard vehicle loss.</summary>
    public void PlayCrash() => Trigger(0f, 1.6f, 1.0f);
}
