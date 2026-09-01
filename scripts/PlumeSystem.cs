namespace Exosphere.Game;

using Godot;
using System.Collections.Generic;

/// <summary>
/// Engine-plume VFX for Starship + Super Heavy Raptor engines.
///
/// Each engine ring is rendered as a layered plume:
///   • a shader-driven emissive cone (<see cref="assets/shaders/raptor_plume.gdshader"/>)
///     that paints the bright blue-white supersonic core, periodic Mach (shock)
///     diamonds, and a translucent incandescent outer sheath;
    ///   • a GPU-particle emitter for hot steam/deluge that breaks up the silhouette;
    ///   • a separate, low-energy dust/debris emitter driven by ground interaction;
///   • an <see cref="OmniLight3D"/> at the nozzle so the plume illuminates the
///     pad / vehicle on launch.
///
/// The plume EXPANDS in vacuum (longer, broader, sparser diamonds) and is tight
/// with closely-spaced diamonds at sea level, driven from vessel altitude.
///
/// Public surface used by VesselRenderer is preserved exactly:
///   SetupSH(float, float, float, float), SetupStarship(float, float, float),
///   Update(float, bool, double), with a pressure-aware overload for planetary flight.
/// </summary>
public partial class PlumeSystem : Node3D
{
    public readonly record struct EnginePlumeMount(
        string InstanceId,
        Vector3 Position,
        float ExitRadius,
        bool VacuumOptimized);

    // One render unit ≈ 2.8 metres in this project. Plume sizing below is in
    // render units, scaled relative to the engine bell radii VesselRenderer passes.

    /// <summary>A single engine ring's visual stack (cone + particles + light).</summary>
    private sealed class PlumeUnit
    {
        public Node3D           Pivot    = null!;   // anchored at the nozzle; scaled to stretch the plume
        public MeshInstance3D   Cone     = null!;   // shader-driven core + diamonds + sheath
        public ShaderMaterial   ConeMat  = null!;
        public MeshInstance3D   Core     = null!;   // narrow inner core, separate from the optically-thin sheath
        public ShaderMaterial   CoreMat  = null!;
        public GpuParticles3D   Smoke    = null!;   // turbulent steam/deluge envelope
        public GpuParticles3D   Dust     = null!;   // low-altitude pad dust/debris
        public OmniLight3D?     Light;              // ground illumination (group leaders only)

        public float BaseLength;                    // sea-level plume length (render units)
        public float BaseRadius;                    // plume mouth radius at the nozzle
        public float BaseEnergy;
        public float CoreScale;                      // radial scale of the visible inner core
        public bool  IsSuperHeavy;
        public bool  IsSkirt;
        public string InstanceId = "";
    }

    private readonly List<PlumeUnit> _shUnits   = new();
    private readonly List<PlumeUnit> _shipUnits = new();
    private readonly List<PlumeUnit> _genericUnits = new();
    private float _visualTimeSeconds;
    private bool _farFieldActive;

    // The pad tracking shot is deliberately kept in the near-field so its broad
    // steam/dust envelope remains readable. At larger camera distances the outer
    // transparent shell contributes mostly overdraw and aliases into thin bars.
    // Hysteresis prevents the two representations from flickering at the boundary.
    private const float FarFieldEnterDistance = 520f;
    private const float FarFieldExitDistance  = 420f;

    private static Shader? _plumeShader;
    private static Shader PlumeShader =>
        _plumeShader ??= GD.Load<Shader>("res://assets/shaders/raptor_plume.gdshader");

    // ── Setup calls (called once from VesselRenderer after building geometry) ─

    /// <summary>Sets up the 33-engine Super Heavy plume (3 concentric rings + core).</summary>
    public void SetupSH(float innerR, float midR, float outerR, float bellY)
    {
        // 33 Raptors firing together merge into one enormous incandescent plume.
        // We model it as a bright merged central column plus three concentric
        // rings; the column dominates and the rings broaden the silhouette. Base
        // lengths are generous — at sea level this is a short, fat, blinding flame;
        // altitude then stretches it far longer (see Update()).

        // Bright central column (the merged plume core of the densely-packed cluster).
        // Real Super Heavy liftoff is a SHORT but very WIDE, blinding flame disk — the 33
        // engines merge into one incandescent column that spills past the OLM ring.
        _shUnits.Add(BuildUnit("SH_Core", bellY, mouthR: 1.75f,
            length: 15.0f, count: 33,
            core: new Color(0.95f, 0.97f, 1.00f), withLight: true, sh: true));

        // Inner ring — 3 engines.
        _shUnits.Add(BuildUnit("SH_Inner", bellY, mouthR: innerR + 0.56f,
            length: 13.2f, count: 3,
            core: new Color(0.90f, 0.94f, 1.00f), withLight: false, sh: true));

        // Mid ring — 10 engines.
        _shUnits.Add(BuildUnit("SH_Mid", bellY + 0.05f, mouthR: midR + 0.62f,
            length: 12.4f, count: 10,
            core: new Color(0.88f, 0.93f, 1.00f), withLight: false, sh: true));

        // Outer ring — 20 engines, broadest cluster.
        _shUnits.Add(BuildUnit("SH_Outer", bellY + 0.10f, mouthR: outerR + 0.70f,
            length: 11.6f, count: 20,
            core: new Color(0.86f, 0.92f, 1.00f), withLight: true, sh: true));

        // Expanding fountain under the stack: hits the water-cooled plate and
        // spills out between the OLM legs so a pad-side camera actually sees fire.
        _shUnits.Add(BuildUnit("SH_Skirt", bellY, mouthR: 2.35f,
            length: 9.5f, count: 33,
            core: new Color(0.98f, 0.92f, 0.78f), withLight: true, sh: true,
            tailRadius: 0.72f));
    }

    /// <summary>Sets up six individual Starship plumes at the six nozzle exits.</summary>
    public void SetupStarship(float vacRingR, float slRingR, float vacExitY, float slExitY,
        float vacExitR, float slExitR)
    {
        for (int i = 0; i < 3; i++)
        {
            float a = i * Mathf.Tau / 3f + Mathf.Pi / 3f;
            _shipUnits.Add(BuildUnit($"Ship_Vac{i}", vacExitY, vacExitR,
                length: 7.5f, count: 1, core: new Color(0.86f, 0.93f, 1.00f),
                withLight: i == 0, sh: false,
                xPos: vacRingR * Mathf.Cos(a), zPos: vacRingR * Mathf.Sin(a)));
        }
        for (int i = 0; i < 3; i++)
        {
            float a = i * Mathf.Tau / 3f;
            _shipUnits.Add(BuildUnit($"Ship_SL{i}", slExitY, slExitR,
                length: 6.8f, count: 1, core: new Color(0.92f, 0.97f, 1.00f),
                withLight: i == 0, sh: false,
                xPos: slRingR * Mathf.Cos(a), zPos: slRingR * Mathf.Sin(a)));
        }
    }

    public void SetupGenericCluster(IEnumerable<EnginePlumeMount> mounts)
    {
        foreach (var mount in mounts)
        {
            var unit = BuildUnit(
                $"Engine_{mount.InstanceId.Replace(':', '_')}",
                mount.Position.Y,
                mount.ExitRadius,
                length: mount.VacuumOptimized ? 8.5f : 5.8f,
                count: 1,
                core: mount.VacuumOptimized
                    ? new Color(0.82f, 0.91f, 1.00f)
                    : new Color(0.90f, 0.95f, 1.00f),
                withLight: _genericUnits.Count == 0,
                sh: false,
                xPos: mount.Position.X,
                zPos: mount.Position.Z,
                tailRadius: mount.VacuumOptimized ? 0.22f : 0.10f);
            unit.InstanceId = mount.InstanceId;
            _genericUnits.Add(unit);
        }
    }

    // ── Per-frame update ──────────────────────────────────────────────────

    /// <summary>Update emitters from vessel state. Call in VesselRenderer._Process().</summary>
    public void Update(float throttle, bool shPresent, double altitude)
    {
        float pressureRatio = (float)System.Math.Exp(-System.Math.Max(0.0, altitude) / 7_000.0);
        Update(shPresent ? throttle : 0f, shPresent ? 0f : throttle,
            altitude, pressureRatio);
    }

    /// <summary>
    /// Pressure-aware update. <paramref name="ambientPressureRatio"/> is p/p₀, so Mars,
    /// Venus and vacuum expand the exhaust according to their actual atmosphere rather
    /// than borrowing Earth's scale height from the altitude alone.
    /// </summary>
    public void Update(float throttle, bool shPresent, double altitude, double ambientPressureRatio)
        => Update(shPresent ? throttle : 0f, shPresent ? 0f : throttle,
            altitude, ambientPressureRatio, selectedShipEngines: 6);

    public void Update(float throttle, bool shPresent, double altitude,
        double ambientPressureRatio, int selectedShipEngines)
        => Update(shPresent ? throttle : 0f, shPresent ? 0f : throttle,
            altitude, ambientPressureRatio, selectedShipEngines);

    /// <summary>
    /// Pressure-aware dual-stage update. During hot-stage overlap both engines can be
    /// delivered simultaneously; geometry presence is not a firing/exclusion flag.
    /// </summary>
    public void Update(float superHeavyThrottle, float shipThrottle, double altitude,
        double ambientPressureRatio, int selectedShipEngines = 6,
        double visualDeltaSeconds = 1.0 / 30.0)
    {
        // Advance optical turbulence from wall-clock frame time, not from the number
        // of renderer callbacks. The latter slows the plume under llvmpipe and makes
        // a low-FPS flight feel like a sequence of frozen poses. Clamp recovery from
        // a long hitch so one overloaded frame cannot teleport the pattern.
        float visualDelta = (float)System.Math.Clamp(visualDeltaSeconds, 0.0, 0.12);
        _visualTimeSeconds = Mathf.PosMod(_visualTimeSeconds + visualDelta, 10_000f);
        superHeavyThrottle = Mathf.Clamp(superHeavyThrottle, 0f, 1f);
        shipThrottle = Mathf.Clamp(shipThrottle, 0f, 1f);
        bool farField = ResolveFarFieldState();

        // Measured ambient-pressure ratio → expansion factor. At p≈p0 the plume is
        // tight/over-expanded; as p→0 it becomes the long vacuum plume. The smoothstep
        // avoids visible changes in derivative as the vehicle crosses atmospheric layers.
        float pressRatio = (float)System.Math.Clamp(ambientPressureRatio, 0.0, 1.0);
        float expansion  = System.Math.Clamp(1f - pressRatio, 0f, 1f);
        expansion = expansion * expansion * (3f - 2f * expansion); // smoothstep

        UpdateGroup(_shUnits, superHeavyThrottle > 0.01f, superHeavyThrottle,
            expansion, pressRatio, altitude, 1f, flickerPhase: _visualTimeSeconds,
            flickerOffset: 0.0f, farField: farField);

        int slActive = System.Math.Clamp(selectedShipEngines, 0, 3);
        int vacActive = System.Math.Clamp(selectedShipEngines - 3, 0, 3);
        if (_shipUnits.Count >= 3)
            UpdateGroup(_shipUnits, shipThrottle > 0.01f && vacActive > 0,
                shipThrottle, expansion, pressRatio, altitude, 1f,
                start: 0, count: 3, activeCount: vacActive,
                flickerPhase: _visualTimeSeconds, flickerOffset: 1.7f, farField: farField);
        if (_shipUnits.Count >= 6)
            UpdateGroup(_shipUnits, shipThrottle > 0.01f && slActive > 0,
                shipThrottle, expansion, pressRatio, altitude, 1f,
                start: 3, count: 3, activeCount: slActive,
                flickerPhase: _visualTimeSeconds, flickerOffset: 3.1f, farField: farField);

    }

    public void UpdateGeneric(
        IReadOnlyDictionary<string, double> engineThrottles,
        double altitude,
        double ambientPressureRatio,
        double visualDeltaSeconds = 1.0 / 30.0)
    {
        float pressureRatio = (float)System.Math.Clamp(
            ambientPressureRatio, 0.0, 1.0);
        float expansion = 1f - pressureRatio;
        expansion = expansion * expansion * (3f - 2f * expansion);
        float visualDelta = (float)System.Math.Clamp(visualDeltaSeconds, 0.0, 0.12);
        _visualTimeSeconds = Mathf.PosMod(_visualTimeSeconds + visualDelta, 10_000f);
        bool farField = ResolveFarFieldState();
        for (int i = 0; i < _genericUnits.Count; i++)
        {
            var unit = _genericUnits[i];
            float throttle = engineThrottles.TryGetValue(unit.InstanceId, out double value)
                ? (float)System.Math.Clamp(value, 0.0, 1.0)
                : 0f;
            UpdateGroup(
                _genericUnits,
                throttle > 0.01f,
                throttle,
                expansion,
                pressureRatio,
                altitude,
                1f,
                start: i,
                count: 1,
                activeCount: throttle > 0.01f ? 1 : 0,
                flickerPhase: _visualTimeSeconds,
                flickerOffset: i * 0.71f, farField: farField);
        }
    }

    private bool ResolveFarFieldState()
    {
        var camera = GetViewport().GetCamera3D();
        if (camera == null || !GodotObject.IsInstanceValid(camera))
            return _farFieldActive;

        float distance = GlobalPosition.DistanceTo(camera.GlobalPosition);
        bool next = _farFieldActive
            ? distance > FarFieldExitDistance
            : distance >= FarFieldEnterDistance;
        if (next != _farFieldActive)
        {
            _farFieldActive = next;
            GD.Print($"[VISUAL_PLUME_LOD] state={(_farFieldActive ? "far" : "near")} " +
                $"cameraDistance={distance:F1} enter={FarFieldEnterDistance:F0} " +
                $"exit={FarFieldExitDistance:F0}");
        }
        return _farFieldActive;
    }

    private static void UpdateGroup(List<PlumeUnit> units,
        bool firing, float throttle, float expansion, float pressureRatio, double altitude,
        float activeFraction, int start = 0, int count = -1, int activeCount = -1,
        float flickerPhase = 0f, float flickerOffset = 0f, bool farField = false)
    {
        float altT = (float)System.Math.Clamp((altitude - 50.0) / 450.0, 0.0, 1.0);
        var dir = Vector3.Down;
        float smokeSpread = Mathf.Lerp(58f, 10f + expansion * 6f, altT);
        float groundInteraction = 1f - Mathf.SmoothStep(
            0f, 260f, (float)System.Math.Max(0.0, altitude));

        // Smooth deterministic modulation shared per group. Randomizing this at 30 Hz made
        // the plume and its light stutter independently of the physical engine cadence.
        float flick = 0.95f + 0.05f * SmoothFlicker(flickerPhase, flickerOffset);
        float motionFlicker = SmoothFlicker(flickerPhase * 1.23f, flickerOffset + 0.8f);

        // N7: atmospheric-pressure proxy for the new shader uniforms.
        // atmo_pressure = exp(-alt/7000) already computed as (1 - expansion) before
        // the smoothstep, but we keep the simpler inverse relationship here.
        float atmoPressure = pressureRatio;

        int end = count < 0 ? units.Count : System.Math.Min(units.Count, start + count);
        for (int index = start; index < end; index++)
        {
            var u = units[index];
            bool unitFiring = firing && (activeCount < 0 || index - start < activeCount);
            // Near-field skirt: keep a broad, low-energy exhaust fan through the
            // first 140 m so the 33-engine cluster reads as a merged plume rather
            // than a tiny white cone. It fades before the camera's ascent handoff.
            if (u.IsSkirt && altitude > 140.0)
                unitFiring = false;
            // ── Shader-driven core cone ──────────────────────────────────────
            u.Pivot.Visible = unitFiring;
            if (unitFiring)
            {
                // Shock cells are a pressure-mismatch feature, not a permanent
                // decorative pattern. Deluge steam and pad ejecta lower their
                // contrast in the first few hundred metres without changing thrust.
                float steamOcclusion = Mathf.Clamp(
                    groundInteraction * (u.IsSuperHeavy ? 0.72f : 0.20f), 0f, 0.85f);
                float shockCellStrength = Mathf.Clamp(
                    (1f - expansion) * (0.92f - steamOcclusion * 0.55f)
                    * (0.72f + throttle * 0.28f), 0f, 1f);
                float afterburnStrength = Mathf.Clamp(
                    (1f - expansion) * 0.68f + groundInteraction * 0.44f, 0f, 1f);
                float padInteraction = groundInteraction * (u.IsSuperHeavy ? 1f : 0.45f);
                float shockSpacing = Mathf.Lerp(0.88f, 1.12f, expansion);
                float shockSoftness = Mathf.Lerp(0.08f, 0.34f, expansion);
                float outerOpacity = Mathf.Lerp(
                    u.IsSuperHeavy ? 0.56f : 0.50f,
                    u.IsSuperHeavy ? 0.18f : 0.12f,
                    expansion);
                float coreOpacity = Mathf.Lerp(0.92f, 0.78f, expansion);
                SetPlumeMaterial(
                    u.ConeMat, throttle, expansion, atmoPressure,
                    u.BaseEnergy * 0.58f * Mathf.Max(0.28f, activeFraction),
                    outerOpacity, shockCellStrength, shockSpacing, shockSoftness,
                    steamOcclusion, afterburnStrength, padInteraction, coreLayer: 0f,
                    farField: farField);
                float farFieldCoreOpacity = farField
                    ? Mathf.Lerp(1.00f, 0.90f, expansion)
                    : coreOpacity;
                float farFieldCoreEnergy = farField ? u.BaseEnergy * 1.30f : u.BaseEnergy * 0.86f;
                SetPlumeMaterial(
                    u.CoreMat, throttle, expansion, atmoPressure,
                    farFieldCoreEnergy * Mathf.Max(0.28f, activeFraction),
                    farFieldCoreOpacity, shockCellStrength * 0.72f, shockSpacing, shockSoftness,
                    steamOcclusion * 0.35f, afterburnStrength * 0.80f, padInteraction * 0.65f,
                    coreLayer: 1f, farField: farField);
                u.Cone.Visible = unitFiring && !farField;
                u.Core.Visible = unitFiring;

                // Length grows with throttle and (strongly) with altitude;
                // mouth broadens in vacuum (underexpanded). Flicker jitters length.
                // The cone mesh is unit height (1.0) and unit-ish radius (0.5),
                // anchored at the nozzle via the pivot, so scaling the pivot's
                // Y stretches the plume downward while the mouth stays put.
                //
                // SL→vacuum: at sea level the plume is short & fat; in vacuum it
                // lengthens dramatically (up to ~4x) and widens (up to ~2.3x) into
                // the long faint underexpanded plume.
                float vacuumLengthGain = u.IsSuperHeavy ? 3.0f : 1.8f;
                float lenScale = (0.55f + 0.45f * throttle)
                               * (1.0f + expansion * vacuumLengthGain) * flick;
                // The Super Heavy's sea-level exhaust is a broad merged disk, not a
                // needle. Widen only the radial envelope so the pad-side silhouette
                // reads at distance without lengthening the plume into a white streak.
                float seaLevelBroadening = u.IsSuperHeavy ? 1.62f : 1.12f;
                float radScale = (0.85f + 0.30f * throttle)
                               * (1.0f + expansion * (u.IsSuperHeavy ? 1.3f : 0.72f))
                               * Mathf.Lerp(seaLevelBroadening, 1.0f, expansion);
                if (farField)
                    radScale *= 1.42f;
                u.Pivot.Scale = new Vector3(
                    (u.BaseRadius / 0.5f) * radScale * Mathf.Sqrt(activeFraction),
                    u.BaseLength * lenScale,
                    (u.BaseRadius / 0.5f) * radScale * Mathf.Sqrt(activeFraction));
                float coreScale = u.CoreScale * (farField ? 1.28f : 1f);
                u.Core.Scale = new Vector3(coreScale, 1f, coreScale);
            }

            // ── Turbulent steam/deluge particles ─────────────────────────────
            // Methalox vacuum exhaust is optically thin. Keep the dense white/grey
            // envelope tied to low altitude so an orbital burn does not inherit a
            // pad-sized cloud.
            float smokePresence = Mathf.Clamp(1f - expansion * (u.IsSuperHeavy ? 0.92f : 1.12f), 0f, 1f);
            float smokeAmount = throttle * smokePresence * smokePresence * activeFraction
                * groundInteraction * (u.IsSuperHeavy ? 0.88f : 0.20f);
            u.Smoke.Emitting = unitFiring && !farField && smokeAmount > 0.02f;
            if (unitFiring)
            {
                u.Smoke.AmountRatio = Mathf.Clamp(smokeAmount, 0.0f, 1f);
                u.Smoke.SpeedScale  = 0.70f + throttle * 0.28f + smokePresence * 0.25f
                    + motionFlicker * 0.08f;
                if (u.Smoke.ProcessMaterial is ParticleProcessMaterial pm)
                {
                    pm.Direction = dir;
                    pm.Spread    = smokeSpread;
                    // Big, billowing soot near the pad; smoke thins out in vacuum
                    // (no air to billow into) leaving just the bright core.
                    pm.ScaleMin  = (u.IsSuperHeavy ? 1.6f : 0.55f) * Mathf.Lerp(0.20f, 1.0f, smokePresence);
                    pm.ScaleMax  = (u.IsSuperHeavy ? 3.8f : 1.35f) * Mathf.Lerp(0.28f, 1.0f, smokePresence);
                }
            }

            // ── Ground dust/debris particles ─────────────────────────────────
            // This is deliberately weaker and browner than the steam. It exists
            // only while the plume can still couple to the pad, preventing a
            // persistent soot cone or a vacuum dust trail.
            float dustAmount = throttle * groundInteraction * groundInteraction
                * smokePresence * activeFraction * (u.IsSuperHeavy ? 0.36f : 0.035f);
            u.Dust.Emitting = unitFiring && !farField && dustAmount > 0.015f;
            if (unitFiring)
            {
                u.Dust.AmountRatio = Mathf.Clamp(dustAmount, 0f, 1f);
                u.Dust.SpeedScale = 0.65f + throttle * 0.18f + motionFlicker * 0.06f;
                if (u.Dust.ProcessMaterial is ParticleProcessMaterial dust)
                {
                    dust.Direction = dir;
                    dust.Spread = u.IsSuperHeavy ? 72f : 28f;
                    dust.ScaleMin = u.IsSuperHeavy ? 0.32f : 0.18f;
                    dust.ScaleMax = u.IsSuperHeavy ? 1.15f : 0.55f;
                }
            }

            // ── Nozzle glow light ────────────────────────────────────────────
            if (u.Light != null)
            {
                u.Light.Visible = unitFiring && !farField;
                if (unitFiring)
                {
                    // Strong at the pad for ground illumination, eases off with
                    // altitude (nothing to light up in vacuum), flickers alive.
                    float groundBoost = 1f - altT * 0.45f;
                    u.Light.LightEnergy = (u.IsSuperHeavy ? 14.0f : 7.0f)
                                        * throttle * groundBoost * flick * activeFraction;
                    u.Light.OmniRange   = u.BaseLength
                                        * (u.IsSuperHeavy ? 2.8f : 1.8f)
                                        * (0.9f + throttle * 0.6f);
                }
            }
        }
    }

    private static float SmoothFlicker(float phase, float offset)
    {
        return 0.5f + 0.5f * Mathf.Sin(phase * 7.3f + offset * 2.1f);
    }

    private static void SetPlumeMaterial(
        ShaderMaterial material,
        float throttle,
        float expansion,
        float atmoPressure,
        float energy,
        float layerOpacity,
        float shockCellStrength,
        float shockCellSpacing,
        float shockCellSoftness,
        float steamOcclusion,
        float afterburnStrength,
        float padInteraction,
        float coreLayer,
        bool farField)
    {
        material.SetShaderParameter("throttle", throttle);
        material.SetShaderParameter("expansion", expansion);
        material.SetShaderParameter("atmo_pressure", atmoPressure);
        material.SetShaderParameter("throttle_level", throttle);
        material.SetShaderParameter("energy", energy);
        material.SetShaderParameter("layer_opacity", layerOpacity);
        material.SetShaderParameter("core_layer", coreLayer);
        material.SetShaderParameter("shock_cell_strength", shockCellStrength);
        material.SetShaderParameter("shock_cell_spacing", shockCellSpacing);
        material.SetShaderParameter("shock_cell_softness", shockCellSoftness);
        material.SetShaderParameter("steam_occlusion", steamOcclusion);
        material.SetShaderParameter("afterburn_strength", afterburnStrength);
        material.SetShaderParameter("pad_interaction", padInteraction);
        material.SetShaderParameter("far_field", farField ? 1f : 0f);
    }

    // ── Factory helpers ────────────────────────────────────────────────────

    private PlumeUnit BuildUnit(string name, float yPos, float mouthR,
        float length, int count, Color core, bool withLight, bool sh,
        float xPos = 0f, float zPos = 0f, float tailRadius = -1f)
    {
        float resolvedTailRadius = tailRadius >= 0f
            ? tailRadius
            : sh ? 0.14f : 0.90f;
        var unit = new PlumeUnit
        {
            BaseLength   = length,
            BaseRadius   = mouthR,
            BaseEnergy   = sh ? (name.Contains("Skirt") ? 1.35f : 4.6f) : 5.5f,
            CoreScale    = sh ? 0.52f : 0.82f,
            IsSuperHeavy = sh,
            IsSkirt = name.Contains("Skirt"),
        };

        // ── Shader cone ──────────────────────────────────────────────────────
        // A pivot Node3D is placed at the nozzle (yPos). The cone mesh is its
        // child, offset DOWN by half its unit height so the wide mouth sits at
        // the pivot origin. Scaling the pivot then stretches the plume downward
        // while the mouth stays anchored at the nozzle across all throttles.
        //
        // Cone mesh: unit height (1.0), mouth radius 0.5 toward +Y (nozzle),
        // tapering to a fine tip at -Y. Authored at fixed dims so the shader's
        // axial/radial UVs stay scale-independent.
        var coneMesh = new CylinderMesh
        {
            TopRadius      = 0.5f,
            BottomRadius   = resolvedTailRadius,
            Height         = 1.0f,
            RadialSegments = 20,
            Rings          = 24,
            CapTop         = false,
            CapBottom      = false,
        };

        var mat = new ShaderMaterial { Shader = PlumeShader };
        mat.SetShaderParameter("core_color",     core);
        mat.SetShaderParameter("edge_color",    new Color(1.0f, 0.45f, 0.12f));
        mat.SetShaderParameter("diamond_count", sh ? 8.0f : 9.0f);
        mat.SetShaderParameter("tail_radius", resolvedTailRadius);
        // Master brightness — the merged 33-engine column reads brighter against the daytime
        // sky than a single ring; bumped so liftoff/ascent exhaust actually glows.
        mat.SetShaderParameter("energy",        unit.BaseEnergy);
        mat.SetShaderParameter("throttle",      0f);
        mat.SetShaderParameter("expansion",     0f);
        // N7: initialize the new atmospheric-pressure uniforms.
        mat.SetShaderParameter("atmo_pressure",  1f);  // sea level at start
        mat.SetShaderParameter("throttle_level", 0f);  // engines off at start
        mat.SetShaderParameter("shock_cell_strength", 0f);
        mat.SetShaderParameter("shock_cell_spacing", 1f);
        mat.SetShaderParameter("shock_cell_softness", 0.10f);
        mat.SetShaderParameter("steam_occlusion", 0f);
        mat.SetShaderParameter("afterburn_strength", 0f);
        mat.SetShaderParameter("pad_interaction", 0f);
        mat.SetShaderParameter("layer_opacity",     sh ? 0.56f : 0.50f);
        mat.SetShaderParameter("core_layer",         0f);
        mat.RenderPriority = 2;

        // A cone surface is an envelope, not a volumetric sample: without a second
        // narrow layer, its side-facing fragments only show the skin and read as a
        // solid white teardrop. The core layer supplies the blue-white axial column;
        // the outer layer remains transparent enough to preserve the turbulent edge.
        float coreTailRadius = sh
            ? resolvedTailRadius * 0.42f
            : resolvedTailRadius * 0.34f;
        var coreMesh = new CylinderMesh
        {
            TopRadius      = 0.5f,
            BottomRadius   = coreTailRadius,
            Height         = 1.0f,
            RadialSegments = 20,
            Rings          = 24,
            CapTop         = false,
            CapBottom      = false,
        };
        var coreMat = new ShaderMaterial { Shader = PlumeShader };
        coreMat.SetShaderParameter("core_color", core);
        coreMat.SetShaderParameter("edge_color", new Color(1.0f, 0.45f, 0.12f));
        coreMat.SetShaderParameter("diamond_count", sh ? 8.0f : 9.0f);
        coreMat.SetShaderParameter("tail_radius", coreTailRadius);
        coreMat.SetShaderParameter("energy", unit.BaseEnergy * 0.86f);
        coreMat.SetShaderParameter("throttle", 0f);
        coreMat.SetShaderParameter("expansion", 0f);
        coreMat.SetShaderParameter("atmo_pressure", 1f);
        coreMat.SetShaderParameter("throttle_level", 0f);
        coreMat.SetShaderParameter("shock_cell_strength", 0f);
        coreMat.SetShaderParameter("shock_cell_spacing", 1f);
        coreMat.SetShaderParameter("shock_cell_softness", 0.10f);
        coreMat.SetShaderParameter("steam_occlusion", 0f);
        coreMat.SetShaderParameter("afterburn_strength", 0f);
        coreMat.SetShaderParameter("pad_interaction", 0f);
        coreMat.SetShaderParameter("layer_opacity",     0.92f);
        coreMat.SetShaderParameter("core_layer",         1f);
        coreMat.RenderPriority = 3;

        var pivot = new Node3D
        {
            Name     = name + "_Pivot",
            Position = new Vector3(xPos, yPos, zPos),
            Visible  = false,
        };
        AddChild(pivot);

        var cone = new MeshInstance3D
        {
            Name             = name + "_Cone",
            Mesh             = coneMesh,
            // Mesh is centred on its own origin; shift down by 0.5 so the +Y mouth
            // lands exactly at the pivot origin (the nozzle).
            Position         = new Vector3(0, -0.5f, 0),
            MaterialOverride = mat,
            CastShadow       = GeometryInstance3D.ShadowCastingSetting.Off,
            SortingOffset    = 2f,
            // Generous AABB so the long vacuum plume isn't culled when off-screen.
            CustomAabb       = new Aabb(new Vector3(-4.0f, -6.0f, -4.0f),
                                        new Vector3(8f, 8f, 8f)),
        };
        pivot.AddChild(cone);
        var coreCone = new MeshInstance3D
        {
            Name             = name + "_Core",
            Mesh             = coreMesh,
            Position         = new Vector3(0, -0.5f, 0),
            MaterialOverride = coreMat,
            CastShadow       = GeometryInstance3D.ShadowCastingSetting.Off,
            SortingOffset    = 3f,
            Visible          = false,
            CustomAabb       = new Aabb(new Vector3(-4.0f, -6.0f, -4.0f),
                                        new Vector3(8f, 8f, 8f)),
        };
        pivot.AddChild(coreCone);
        unit.Pivot   = pivot;
        unit.Cone    = cone;
        unit.ConeMat = mat;
        unit.Core    = coreCone;
        unit.CoreMat = coreMat;

        // ── Turbulent smoke / soot particles ─────────────────────────────────
        unit.Smoke = BuildSmoke(name + "_Smoke", xPos, yPos, zPos, mouthR, count, sh);
        AddChild(unit.Smoke);
        unit.Dust = BuildDust(name + "_Dust", xPos, yPos, zPos, mouthR, count, sh);
        AddChild(unit.Dust);

        // ── Nozzle glow light ────────────────────────────────────────────────
        if (withLight)
        {
            var light = new OmniLight3D
            {
                Name             = name + "_Glow",
                Position         = new Vector3(xPos, yPos - 0.3f, zPos),
                LightColor       = new Color(0.85f, 0.92f, 1.0f),
                OmniRange        = length,
                LightEnergy      = 0f,
                ShadowEnabled    = false,
                Visible          = false,
                LightSpecular    = 0.4f,
            };
            AddChild(light);
            unit.Light = light;
        }

        return unit;
    }

    // Soft circular gradient: white centre → transparent edge (smoke puffs).
    private static ImageTexture BuildSoftCircleTexture()
    {
        const int S = 64;
        var img = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
        float half = S * 0.5f;
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float dx = (x - half) / half;
            float dy = (y - half) / half;
            float r  = Mathf.Sqrt(dx * dx + dy * dy);
            float a  = Mathf.Clamp(1f - r * r, 0f, 1f);
            a = a * a;
            img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        return ImageTexture.CreateFromImage(img);
    }

    private static ImageTexture? _softCircle;
    private static ImageTexture SoftCircle => _softCircle ??= BuildSoftCircleTexture();

    private static ImageTexture? _softSheet;
    private static ImageTexture SoftSheet => _softSheet ??= BuildSoftSheetTexture();

    // Vertical steam sheet: fade on the sides, hold density along Y so SH soot
    // reads as a curtain, not a field of circular puffs.
    private static ImageTexture BuildSoftSheetTexture()
    {
        const int S = 64;
        var img = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
        float half = S * 0.5f;
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float dx = (x - half) / half;
            float ny = y / (float)(S - 1);
            float side = Mathf.Clamp(1f - dx * dx * 1.35f, 0f, 1f);
            float axial = Mathf.Clamp(1f - Mathf.Abs(ny - 0.42f) * 1.15f, 0f, 1f);
            float a = side * side * axial;
            img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        return ImageTexture.CreateFromImage(img);
    }

    private GpuParticles3D BuildSmoke(string name, float xPos, float yPos, float zPos, float mouthR,
        int engineCount, bool sh)
    {
        // Particle budget kept low: total stays in the low hundreds across all
        // rings. Soot/smoke is a translucent supporting layer, not the main show.
        int amount = sh
            ? Mathf.Clamp(90 + engineCount * 6, 120, 260)
            : Mathf.Clamp(28 + engineCount * 4, 28, 110);

        // Edge soot colour ramp: incandescent root → dense grey steam → fade.
        var grad = new Gradient
        {
            Colors  = new[]
            {
                sh
                    ? new Color(0.96f, 0.92f, 0.86f, 0.76f) // hot steam at the root
                    : new Color(1.00f, 0.55f, 0.20f, 0.55f), // incandescent near nozzle
                sh
                    ? new Color(0.72f, 0.72f, 0.70f, 0.80f) // dense grey-white deluge/steam
                    : new Color(0.48f, 0.42f, 0.36f, 0.35f),
                new Color(0.22f, 0.22f, 0.23f, 0.0f),       // smoke fade-out
            },
            Offsets = new[] { 0f, 0.4f, 1f },
        };
        var gradTex = new GradientTexture1D { Gradient = grad };

        var pm = new ParticleProcessMaterial
        {
            EmissionShape           = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingRadius      = mouthR,
            EmissionRingInnerRadius = Mathf.Max(0f, mouthR - 0.15f),
            EmissionRingAxis        = Vector3.Up,
            EmissionRingHeight      = 0.04f,

            Direction          = new Vector3(0, -1, 0),
            Spread             = sh ? 48f : 14f,
            InitialVelocityMin = sh ? 7f : 10f,
            InitialVelocityMax = sh ? 19f : 26f,

            DampingMin = 2f,
            DampingMax = 5f,

            ScaleMin = sh ? 1.8f : 1.0f,
            ScaleMax = sh ? 5.8f : 3.0f,

            ColorRamp = gradTex,
        };
        pm.ParticleFlagAlignY = sh;

        // Elongated sheets for the SH cluster; circular puffs still read as toy balls.
        var quad = new QuadMesh { Size = sh ? new Vector2(2.8f, 6.8f) : new Vector2(2.2f, 2.2f) };
        var drawMat = new StandardMaterial3D
        {
            BillboardMode            = sh
                ? BaseMaterial3D.BillboardModeEnum.Particles
                : BaseMaterial3D.BillboardModeEnum.Enabled,
            ShadingMode              = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode                = sh ? BaseMaterial3D.BlendModeEnum.Mix : BaseMaterial3D.BlendModeEnum.Add,
            Transparency             = BaseMaterial3D.TransparencyEnum.Alpha,
            DepthDrawMode            = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            AlbedoTexture            = sh ? SoftSheet : SoftCircle,
            AlbedoColor              = Colors.White,
            EmissionEnabled          = true,
            Emission               = sh ? new Color(0.72f, 0.72f, 0.68f) : Colors.White,
            EmissionEnergyMultiplier = sh ? 0.35f : 1.6f,
            VertexColorUseAsAlbedo   = true,
        };
        quad.SurfaceSetMaterial(0, drawMat);

        return new GpuParticles3D
        {
            Name            = name,
            Position        = new Vector3(xPos, yPos, zPos),
            Amount          = amount,
            Lifetime        = sh ? 3.4f : 1.1f,
            ProcessMaterial = pm,
            DrawPass1       = quad,
            Emitting        = false,
            LocalCoords     = true,
            OneShot         = false,
            Preprocess      = 0.2f,
            SpeedScale      = 1.0f,
            VisibilityAabb  = new Aabb(new Vector3(-20f, -340f, -20f),
                                       new Vector3(40f, 480f, 40f)),
        };
    }

    private GpuParticles3D BuildDust(string name, float xPos, float yPos, float zPos, float mouthR,
        int engineCount, bool sh)
    {
        int amount = sh
            ? Mathf.Clamp(44 + engineCount * 3, 64, 150)
            : Mathf.Clamp(12 + engineCount * 2, 12, 42);

        var grad = new Gradient
        {
            Colors = new[]
            {
                sh ? new Color(0.50f, 0.45f, 0.38f, 0.22f) : new Color(0.42f, 0.37f, 0.31f, 0.16f),
                sh ? new Color(0.32f, 0.31f, 0.29f, 0.16f) : new Color(0.24f, 0.25f, 0.25f, 0.10f),
                new Color(0.16f, 0.17f, 0.17f, 0f),
            },
            Offsets = new[] { 0f, 0.32f, 1f },
        };

        var pm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingRadius = mouthR * (sh ? 1.05f : 0.8f),
            EmissionRingInnerRadius = mouthR * (sh ? 0.35f : 0.55f),
            EmissionRingAxis = Vector3.Up,
            EmissionRingHeight = 0.05f,
            Direction = Vector3.Down,
            Spread = sh ? 72f : 28f,
            InitialVelocityMin = sh ? 3.0f : 1.5f,
            InitialVelocityMax = sh ? 11.0f : 6.0f,
            DampingMin = 3f,
            DampingMax = 8f,
            ScaleMin = sh ? 0.32f : 0.18f,
            ScaleMax = sh ? 1.15f : 0.55f,
            ColorRamp = new GradientTexture1D { Gradient = grad },
        };

        var quad = new QuadMesh { Size = sh ? new Vector2(1.6f, 1.6f) : new Vector2(0.9f, 0.9f) };
        var material = new StandardMaterial3D
        {
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode = BaseMaterial3D.BlendModeEnum.Mix,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            AlbedoTexture = SoftCircle,
            AlbedoColor = Colors.White,
            VertexColorUseAsAlbedo = true,
        };
        quad.SurfaceSetMaterial(0, material);

        return new GpuParticles3D
        {
            Name = name,
            Position = new Vector3(xPos, yPos, zPos),
            Amount = amount,
            Lifetime = sh ? 1.35f : 0.8f,
            ProcessMaterial = pm,
            DrawPass1 = quad,
            Emitting = false,
            LocalCoords = true,
            OneShot = false,
            Preprocess = 0.1f,
            SpeedScale = 1f,
            VisibilityAabb = new Aabb(new Vector3(-24f, -80f, -24f), new Vector3(48f, 100f, 48f)),
        };
    }
}
