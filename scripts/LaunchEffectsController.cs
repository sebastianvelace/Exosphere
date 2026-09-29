namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;

/// <summary>
/// Ground-interaction launch VFX: the iconic Super Heavy deluge cloud.
/// When the booster ignites on the pad, a massive billowing wall of white-grey
/// steam/smoke (water-deluge vaporisation + exhaust) erupts at y≈0 and rolls
/// OUTWARD horizontally, then boils upward — accompanied by darker dust kicked
/// up low and outward.
///
/// This is purely the ground cloud; the engine flame itself is owned by
/// <c>PlumeSystem</c>. The close layers stay sized for the gameplay chase
/// camera. The aerial bank is two cumulus lobes left and right of a clear
/// stack corridor, white on the outside and gold toward the flame, matching
/// the Flight 14 Pad 2 still. It fades out inside chase range so that view
/// does not collapse into white balls. We anchor the cloud to the
/// ground point directly under the vessel. Because the active vessel sits at the render origin and the
/// floating-origin scheme keeps it there, the ground recedes downward as the
/// rocket climbs: we place the emitters at <c>-up * (altitude / MetresPerUnit)</c>
/// so the cloud is "left behind" on the pad while the booster ascends away.
///
/// Self-wiring: drop in as a child of the World Node3D. It finds the vessel and
/// the dominant body each frame through <see cref="SimulationBridge"/>,
/// null-guarding everything, and only toggles <c>Emitting</c> / transform per
/// frame — all heavy objects are built once in <see cref="_Ready"/>.
/// </summary>
public partial class LaunchEffectsController : Node3D
{
    // ── Tuning ───────────────────────────────────────────────────────────────
    // Render scale: 1 unit ≈ 2.8 m. The whole cloud is sized in render units.
    private const float MetresPerUnit = 2.8f;

    // N5: cloud dominates 0–3 s; ceiling lowered so it dissipates by ~300 m.
    // Altitude band (metres) over which the cloud is active and fades out.
    private const float TriggerCeilingM = 550f;   // above this: fully off (smoke column lingers longer)
    private const float FullIntensityM  = 140f;   // at/under this: full force (huge cloud through the first seconds)
    private const float MinThrottle     = 0.02f;  // throttle floor to count as "lit"

    // Pivot we rotate to align local +Y with the planet's "up" at the vessel,
    // and translate down to the receding ground point.
    private Node3D _pivot = null!;

    // The five layers of the deluge cloud.
    private GpuParticles3D _steamCore   = null!;  // dense, fast outward billow
    private GpuParticles3D _steamBoil   = null!;  // taller lingering boil-up
    private GpuParticles3D _dust        = null!;  // low dark debris/dust
    private GpuParticles3D _haze        = null!;  // faint lingering ground dust haze
    // N5: second dust emitter — radial blast wave at ground level.
    private GpuParticles3D _dustRadial  = null!;  // fast flat dust ring expanding radially
    private MultiMeshInstance3D _instantSteam = null!; // guaranteed ignition cloud bank
    private Node3D _billowBank = null!;
    private StandardMaterial3D _billowMaterial = null!;
    // Kilometre-scale lobes for the aerial liftoff frame. Hidden inside the
    // gameplay chase distance; see WideCameraFullDistance.
    private MultiMeshInstance3D _wideLobes = null!;
    private MultiMeshInstance3D _wideCore = null!;

    // Shared soft-round billboard texture for the close layers.
    private static ImageTexture? _softCircle;
    private static ImageTexture SoftCircle => _softCircle ??= BuildSoftCircleTexture();
    // Smooth cumulus for the aerial bank. Kept separate so the close-range
    // circle's high-frequency breakup is not stretched across hundreds of metres.
    private static ImageTexture? _wideCloud;
    private static ImageTexture WideCloudTexture => _wideCloud ??= BuildWideCloudTexture();

    // Smoothed intensity so ignition/cutoff ramps instead of popping.
    private float _intensity;
    private bool _emitting;
    private bool _wideShown;
    private float _ignitionAge;
    private const double PhysicsSamplePeriodSeconds = 1.0 / 20.0;
    private double _physicsSampleTimer;
    private Vessel? _sampledVessel;
    private Universe? _sampledUniverse;
    private bool _sampledStateValid;
    private float _sampledTarget;
    private double _sampledAltitude;
    private Vector3 _sampledUp = Vector3.Up;

    public override void _Ready()
    {
        _pivot = new Node3D { Name = "DelugePivot" };
        AddChild(_pivot);

        _steamCore  = BuildSteamCore();
        _steamBoil  = BuildSteamBoil();
        _dust       = BuildDust();
        _haze       = BuildHaze();
        _dustRadial = BuildDustRadial();  // N5: ground-level radial blast ring
        _instantSteam = BuildImmediateSteamBank();
        _billowBank = BuildBillowBank();
        // Sheets are oriented in the pad frame. A billboard here stood the
        // cloud up into the hazy sky and it disappeared. The core stays a
        // camera-facing trench glow.
        // Low emission so the gold/white vertex colours survive. A bright
        // emission multiplier clipped both lobes back to the same white.
        _wideLobes = BuildWideBank("WideDelugeLobes", WideSheetCount, 0.10f, new Vector2(40f, 40f), billboard: false);
        _wideCore = BuildWideBank("WideDelugeCore", WideCoreCount, 0.85f, new Vector2(22f, 14f), billboard: true);

        _pivot.AddChild(_haze);        // faint ground haze underneath everything
        _pivot.AddChild(_dustRadial);  // N5: radial blast wave at pad deck level
        _pivot.AddChild(_dust);        // dust sits under the steam
        _pivot.AddChild(_steamBoil);
        _pivot.AddChild(_steamCore);
        _pivot.AddChild(_instantSteam);
        _pivot.AddChild(_billowBank);
        _pivot.AddChild(_wideLobes);
        _pivot.AddChild(_wideCore);

        SetEmitting(false);
        Visible = false;
    }

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        _physicsSampleTimer -= System.Math.Max(0.0, delta);
        if (_physicsSampleTimer <= 0.0
            || !ReferenceEquals(vessel, _sampledVessel)
            || !ReferenceEquals(universe, _sampledUniverse))
        {
            _physicsSampleTimer = PhysicsSamplePeriodSeconds;
            _sampledVessel = vessel;
            _sampledUniverse = universe;
            SampleLaunchState(vessel, universe);
        }

        if (!_sampledStateValid)
        {
            FadeOut(delta);
            return;
        }

        float target = _sampledTarget;

        // Starbase's deluge is already flowing when the Raptors light. Seed a
        // substantial cloud on the ignition edge instead of visually ramping
        // from an empty particle buffer.
        if (target > 0f && _intensity < 0.01f)
            _intensity = 0.38f;

        // Asymmetric smoothing: erupt fast at ignition, linger/fade slower so the
        // cloud reads as a self-sustaining body of vapour, not a switch.
        float rate = target > _intensity ? 8f : 1.6f;
        _intensity = Mathf.Lerp(_intensity, target, Mathf.Clamp((float)delta * rate, 0f, 1f));

        if (_intensity < 0.01f)
        {
            if (Visible) SetEmitting(false);
            Visible = false;
            return;
        }

        Visible = true;

        // ── Anchor to the receding ground point under the vessel ──────────────
        // Up direction = radial from planet centre to vessel, in render space the
        // floating origin keeps the vessel at (0,0,0), so the ground sits at
        // -up * altitudeUnits below us.
        float altUnits = (float)(_sampledAltitude / MetresPerUnit);
        _pivot.Position = -_sampledUp * altUnits;

        // Orient pivot so its local +Y aligns with planet up (cloud rolls "out"
        // in the local XZ plane and boils up along +Y).
        AlignUp(_pivot, _sampledUp);

        // ── Drive the layers ──────────────────────────────────────────────────
        SetEmitting(true);
        DriveAmounts(_intensity);
        _ignitionAge += (float)delta;
        DriveImmediateSteam(_intensity, _ignitionAge);
        DriveWideCloud(_intensity, _ignitionAge);
    }

    private void SampleLaunchState(Vessel? vessel, Universe? universe)
    {
        _sampledStateValid = false;
        _sampledTarget = 0f;
        _sampledAltitude = 0.0;
        _sampledUp = Vector3.Up;

        if (vessel == null || universe == null) return;

        // Dominant body must be Earth for a pad launch.
        var body = universe.GetDominantBody(vessel.Position);
        if (body == null || body.Id != "earth") return;

        double altitude = vessel.GetAltitude(body); // metres
        bool lit = vessel.Throttle > MinThrottle && vessel.HasActiveEngineParts;
        bool onPad = altitude < TriggerCeilingM;

        // Target intensity: full near the deck, easing to zero by the ceiling.
        float target = 0f;
        if (lit && onPad)
        {
            float t = ((float)altitude - FullIntensityM) /
                      (TriggerCeilingM - FullIntensityM);
            target = 1f - Mathf.Clamp(t, 0f, 1f); // 1 at/under FullIntensityM → 0 at ceiling
        }

        Vector3 up = ToGodot((vessel.Position - body.Position).Normalized);
        if (up.LengthSquared() < 1e-6f) up = Vector3.Up;

        _sampledStateValid = true;
        _sampledTarget = target;
        _sampledAltitude = altitude;
        _sampledUp = up;
    }

    // ── Per-frame intensity → emission amount (no per-frame allocations) ──────
    private void DriveAmounts(float k)
    {
        // Keep Amount fixed after construction. Mutating it rebuilt GPU buffers
        // during spool-up and repeatedly discarded the initial steam cloud.
        // Cap peak ratios below 1 so the deluge bank does not erase the stack silhouette
        // in a lateral pad capture (PLAN_VISUAL V2 deluge silhouette gap).
        float ratio = Mathf.Clamp(k, 0.02f, 1f);
        _steamCore.AmountRatio  = Mathf.Lerp(0.40f, 0.82f, ratio);
        _steamBoil.AmountRatio  = Mathf.Lerp(0.28f, 0.72f, ratio);
        _dust.AmountRatio       = Mathf.Lerp(0.20f, 0.70f, ratio);
        _haze.AmountRatio       = Mathf.Lerp(0.18f, 0.55f, ratio);
        _dustRadial.AmountRatio = Mathf.Lerp(0.30f, 0.65f, ratio);
    }

    private void SetEmitting(bool on)
    {
        if (_emitting == on) return;
        _emitting = on;
        _steamCore.Emitting  = on;
        _steamBoil.Emitting  = on;
        _dust.Emitting       = on;
        _haze.Emitting       = on;
        _dustRadial.Emitting = on;  // N5
        _instantSteam.Visible = on;
        _billowBank.Visible = on;
        if (on)
        {
            _ignitionAge = 0f;
            _steamCore.Restart(true);
            _steamBoil.Restart(true);
            _dust.Restart(true);
            _haze.Restart(true);
            _dustRadial.Restart(true);
        }
    }

    private MultiMeshInstance3D BuildImmediateSteamBank()
    {
        var mat = SteamDrawMaterial(energy: 0.52f);
        mat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;
        var quad = new QuadMesh { Size = new Vector2(12.5f, 4.2f) };
        quad.SurfaceSetMaterial(0, mat);
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = quad,
            InstanceCount = 120,
        };
        var bank = new MultiMeshInstance3D
        {
            Name = "ImmediateDelugeBank",
            Multimesh = mm,
            Visible = false,
            CustomAabb = new Aabb(new Vector3(-90f, -4f, -90f), new Vector3(180f, 70f, 180f)),
        };
        DriveImmediateSteam(bank, 0.45f, 0.0f);
        return bank;
    }

    private Node3D BuildBillowBank()
    {
        var bank = new Node3D { Name = "ImmediateDelugeBillows", Visible = false };
        // Soft irregular billboards, not SphereMesh lobes. Play-camera ignition
        // was reading as a pile of white balls on the OLM; the GPU steam and the
        // MultiMesh bank already supply volume, this layer only guarantees a
        // horizontal sheet on the first frames of the compatibility renderer.
        _billowMaterial = SteamDrawMaterial(energy: 0.22f);
        _billowMaterial.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;
        _billowMaterial.AlbedoColor = new Color(0.76f, 0.77f, 0.76f, 0.64f);
        _billowMaterial.EmissionEnabled = false;
        for (int i = 0; i < 40; i++)
        {
            float phase = Mathf.PosMod(i * 0.618034f, 1f);
            float angle = i * 2.399963f;
            float radius = 2.0f + (i % 12) * 1.75f;
            // Wide, low sheets merge into a deluge wall. Circular puffs read as
            // the white spheres on the play-camera pad.
            float width = 9.0f + phase * 6.0f;
            float height = 3.6f + phase * 2.4f;
            var puff = new MeshInstance3D
            {
                Name = $"DelugeBillow{i}",
                Mesh = new QuadMesh { Size = new Vector2(width, height) },
                Position = new Vector3(Mathf.Cos(angle) * radius,
                    0.6f + (i % 7) * 0.72f, Mathf.Sin(angle) * radius),
                MaterialOverride = _billowMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            bank.AddChild(puff);
        }
        return bank;
    }

    private void DriveImmediateSteam(float intensity, float age) =>
        DriveImmediateSteamLayers(intensity, age);

    private void DriveImmediateSteamLayers(float intensity, float age)
    {
        DriveImmediateSteam(_instantSteam, intensity, age);
        float life = Mathf.Clamp(1f - Mathf.Max(0f, age - 5f) / 7f, 0f, 1f);
        Color color = _billowMaterial.AlbedoColor;
        color.A = Mathf.Clamp(Mathf.Lerp(0.42f, 0.78f, intensity) * life, 0f, 0.78f);
        _billowMaterial.AlbedoColor = color;
    }

    private static void DriveImmediateSteam(MultiMeshInstance3D bank, float intensity, float age)
    {
        var mm = bank.Multimesh;
        if (mm == null) return;
        float life = Mathf.Clamp(1f - Mathf.Max(0f, age - 5f) / 7f, 0f, 1f);
        for (int i = 0; i < mm.InstanceCount; i++)
        {
            float phase = Mathf.PosMod(i * 0.618034f, 1f);
            float angle = i * 2.399963f + phase * 0.35f;
            float speed = 1.2f + phase * 2.2f;
            // Keep the compatibility layer as a low connected sheet. A very wide,
            // tall ring reads as isolated white balls in the lateral pad camera.
            float radius = 3.0f + (i % 15) * 1.45f + Mathf.Min(age, 7f) * speed * 0.65f;
            float height = 1.0f + (i % 8) * 0.48f + Mathf.Min(age, 7f) * (0.30f + phase * 0.40f);
            float size = (1.9f + phase * 2.30f) * (1f + Mathf.Min(age, 6f) * 0.12f);
            var basis = Basis.Identity.Scaled(new Vector3(size * (1.20f + phase * 0.30f), size * 0.40f, 1f));
            var origin = new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius);
            mm.SetInstanceTransform(i, new Transform3D(basis, origin));
            mm.SetInstanceColor(i, new Color(
                0.78f + phase * 0.08f,
                0.82f + phase * 0.06f,
                0.84f + phase * 0.05f,
                Mathf.Clamp(Mathf.Lerp(0.32f, 0.62f, intensity) * life, 0f, 0.68f)));
        }
    }

    // Gameplay chase tops out near 200 render units. The aerial preset sits
    // at 460. Fade across that gap so zooming out reveals the large cloud
    // without ever covering the pad camera.
    private const float WideCameraStartDistance = 200f;
    private const float WideCameraFullDistance = 360f;
    private const int WideSheetCount = 78;
    private const int WideCoreCount = 6;

    private void DriveWideCloud(float intensity, float age)
    {
        // The viewport camera can be a sub-viewport during the same frame the
        // chase camera is already at the aerial preset. Measure that camera.
        Camera3D? camera = CameraController.Instance?.PresentationCamera
            ?? GetViewport()?.GetCamera3D();
        float distance = camera == null || !GodotObject.IsInstanceValid(camera)
            ? 0f
            : _pivot.GlobalPosition.DistanceTo(camera.GlobalPosition);
        // Keep the carpet through tower clear. The close particle bank still
        // eases off above 140 m; this one is the aerial subject.
        float altitudeFade = 1f;
        if (_sampledAltitude > 420.0)
        {
            float t = ((float)_sampledAltitude - 420f) / (1100f - 420f);
            altitudeFade = 1f - Mathf.Clamp(t, 0f, 1f);
        }
        float weight = Mathf.SmoothStep(WideCameraStartDistance, WideCameraFullDistance, distance)
            * altitudeFade * Mathf.Clamp(intensity, 0f, 1f);
        bool show = weight > 0.05f;
        _wideLobes.Visible = show;
        _wideCore.Visible = show;
        if (show != _wideShown)
        {
            _wideShown = show;
            GD.Print($"[VISUAL_DELUGE] wide={(show ? "on" : "off")} weight={weight:F2} " +
                $"dist={distance:F0} age={age:F1} alt={_sampledAltitude:F0}");
        }
        if (!show)
            return;

        float spread = Mathf.Lerp(0.85f, 1.15f, Mathf.Clamp(age / 8f, 0f, 1f));
        // Flight 14's still is two cumulus masses left and right of a clear
        // stack. Orient them to this camera so the corridor stays open.
        Vector3 lateral = Vector3.Right;
        Vector3 towardCamera = Vector3.Back;
        if (camera != null && GodotObject.IsInstanceValid(camera))
        {
            Basis pivot = _pivot.GlobalTransform.Basis;
            lateral = pivot.Inverse() * camera.GlobalTransform.Basis.X;
            lateral.Y = 0f;
            towardCamera = pivot.Inverse() * (camera.GlobalPosition - _pivot.GlobalPosition);
            towardCamera.Y = 0f;
        }
        PoseWideSheets(_wideLobes, spread, weight, age, lateral, towardCamera);
        PoseWideCore(_wideCore, spread, weight, age);
    }

    /// <summary>
    /// Flight 14's still is not two flat sheets. Each side is a stack of
    /// round sunlit puffs: bright caps, darker bellies, and a gold body on
    /// the screen-right mass. The stack corridor stays empty above the pad.
    /// A short far-side skirt covers the horizon wetland the launch gate
    /// otherwise scores as neon.
    /// </summary>
    private static void PoseWideSheets(
        MultiMeshInstance3D bank, float spread, float weight, float age,
        Vector3 lateral, Vector3 towardCamera)
    {
        MultiMesh? mesh = bank.Multimesh;
        if (mesh == null)
            return;

        lateral.Y = 0f;
        if (lateral.LengthSquared() < 1e-4f)
            lateral = Vector3.Right;
        else
            lateral = lateral.Normalized();
        towardCamera.Y = 0f;
        if (towardCamera.LengthSquared() < 1e-4f)
            towardCamera = new Vector3(-lateral.Z, 0f, lateral.X);
        else
            towardCamera = towardCamera.Normalized();

        const float quad = 40f;
        int count = mesh.InstanceCount;
        int skirts = Mathf.Min(8, count);
        for (int i = 0; i < count; i++)
        {
            bool skirt = i >= count - skirts;
            int side = (i % 2 == 0) ? -1 : 1;
            float h = PuffHash(i * 3 + 1);
            float v = PuffHash(i * 5 + 2);
            float w = PuffHash(i * 7 + 3);
            float along;
            float fore;
            float width;
            float height;
            float rise;
            float spine;
            if (skirt)
            {
                // Far-side row. Depth test keeps it behind the stack, and it
                // covers the horizon marsh the launch gate scores as neon.
                int k = i - (count - skirts);
                along = (k - (skirts - 1) * 0.5f) * 26f;
                fore = -50f;
                width = 78f;
                height = 70f;
                rise = 0.4f;
                spine = 34f;
            }
            else
            {
                bool deck = v < 0.28f;
                rise = deck ? v / 0.28f * 0.22f : Mathf.Pow(v, 0.72f);
                spine = deck ? 6f + rise * 18f : Mathf.Lerp(14f, 102f, rise);
                float lateralSpread = deck ? 70f : Mathf.Lerp(36f, 18f, rise);
                along = side * (deck ? 36f + h * lateralSpread : 46f + h * lateralSpread) * spread;
                // Mid-height puffs close the horizon slot. That slot was a
                // band of wetland beside the booster, which the launch gate
                // scores as neon. The crown stays wide of the nose.
                if (!deck && rise < 0.48f && h < 0.42f)
                    along = side * (22f + h * 18f) * spread;
                fore = (w - 0.5f) * (deck ? 22f : Mathf.Lerp(30f, 14f, rise));
                float puff = deck
                    ? 22f + h * 14f
                    : Mathf.Lerp(16f, 32f, rise) * Mathf.Lerp(0.82f, 1.2f, h);
                width = Mathf.Max(puff, 12f);
                height = Mathf.Max(puff * Mathf.Lerp(0.86f, 1.08f, w), 12f);
                spine += Mathf.Sin(age * 0.2f + i) * 0.6f;
                rise = Mathf.Clamp(spine / 110f, 0f, 1f);
            }

            float y = 2f + spine;
            Vector3 outward = lateral * side;
            Vector3 cardZ = towardCamera;
            Vector3 cardUp = (Vector3.Up * 0.94f + outward * 0.08f).Normalized();
            Vector3 cardX = cardUp.Cross(cardZ).Normalized();
            if (cardX.LengthSquared() < 1e-4f)
                cardX = outward;
            cardUp = cardZ.Cross(cardX).Normalized();
            var basis = new Basis(cardX, cardUp, cardZ).Scaled(
                new Vector3(width / quad, height / quad, 1f));
            var origin = lateral * along + towardCamera * fore + Vector3.Up * y;
            mesh.SetInstanceTransform(i, new Transform3D(basis, origin));

            // Screen-right body is flame-lit gold. Caps stay bright; bellies
            // and crevices drop so neighbouring puffs stay separate.
            float warm = skirt
                ? 0.18f
                : side > 0
                    ? Mathf.Lerp(0.88f, 0.28f, rise)
                    : Mathf.Lerp(0.22f, 0.04f, rise);
            float crevice = skirt ? 0f : (1f - rise) * (1f - Mathf.Abs(h - 0.45f) * 1.6f);
            float lit = Mathf.Lerp(0.58f, 1f, rise) * Mathf.Lerp(1f, 0.70f, Mathf.Clamp(crevice, 0f, 1f));
            mesh.SetInstanceColor(i, new Color(
                lit,
                lit * Mathf.Lerp(0.98f, 0.55f, warm),
                lit * Mathf.Lerp(0.96f, 0.26f, warm),
                (skirt ? 0.94f : 0.90f) * weight));
        }
    }

    private static float PuffHash(int n)
    {
        uint x = (uint)n * 747796405u + 2891336453u;
        x = ((x >> 16) ^ x) * 73244475u;
        return (x & 65535) / 65535f;
    }

    private static void PoseWideCore(MultiMeshInstance3D bank, float spread, float weight, float age)
    {
        MultiMesh? mesh = bank.Multimesh;
        if (mesh == null)
            return;

        for (int i = 0; i < mesh.InstanceCount; i++)
        {
            float phase = Mathf.PosMod(i * 0.618034f, 1f);
            float angle = i * 2.399963f;
            float radius = (6f + phase * 10f) * Mathf.Lerp(0.9f, 1f, spread);
            float y = 3.5f + phase * 4.5f + Mathf.Sin(age * 0.8f + angle) * 0.6f;
            float size = 1.55f + phase * 0.85f;
            var basis = Basis.Identity.Scaled(new Vector3(size * 1.35f, size, 1f));
            var origin = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
            mesh.SetInstanceTransform(i, new Transform3D(basis, origin));
            mesh.SetInstanceColor(i, new Color(
                1f,
                0.42f + phase * 0.16f,
                0.08f + phase * 0.06f,
                Mathf.Clamp(0.88f * weight, 0f, 0.88f)));
        }
    }

    private MultiMeshInstance3D BuildWideBank(
        string name, int count, float emission, Vector2 quadSize, bool billboard)
    {
        // Own texture: the close-range soft circle has a pixel sine that turns
        // into a visible grid once a card is hundreds of metres across.
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode = BaseMaterial3D.BlendModeEnum.Mix,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoTexture = WideCloudTexture,
            AlbedoColor = Colors.White,
            EmissionEnabled = true,
            EmissionEnergyMultiplier = emission,
            VertexColorUseAsAlbedo = true,
            BillboardMode = billboard
                ? BaseMaterial3D.BillboardModeEnum.Enabled
                : BaseMaterial3D.BillboardModeEnum.Disabled,
        };
        var quad = new QuadMesh { Size = quadSize };
        quad.SurfaceSetMaterial(0, material);
        var mesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = quad,
            InstanceCount = count,
        };
        return new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = mesh,
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            CustomAabb = new Aabb(new Vector3(-520f, -8f, -520f), new Vector3(1040f, 260f, 1040f)),
        };
    }

    private void FadeOut(double delta)
    {
        if (_intensity <= 0f)
        {
            if (Visible) { SetEmitting(false); Visible = false; }
            return;
        }
        _intensity = Mathf.Lerp(_intensity, 0f, Mathf.Clamp((float)delta * 1.6f, 0f, 1f));
        if (_intensity < 0.01f)
        {
            _intensity = 0f;
            SetEmitting(false);
            Visible = false;
        }
    }

    // ── Layer builders (called once) ─────────────────────────────────────────

    /// <summary>
    /// Dense, fast deluge billow: a wide ring of vapour shot OUTWARD low to the
    /// ground with strong damping so it spreads sideways and rolls up. Big soft
    /// white billboards that grow over their lifetime — the iconic cloud body.
    /// </summary>
    private GpuParticles3D BuildSteamCore()
    {
        // White-grey steam ramp: bright vapour → cooler grey → soft fade.
        var grad = new Gradient
        {
            Colors = new[]
            {
                new Color(1.00f, 0.34f, 0.08f, 0.68f), // exhaust-lit steam at the source
                new Color(1.00f, 0.66f, 0.30f, 0.88f), // orange inner cloud
                new Color(0.96f, 0.95f, 0.96f, 0.84f), // dense white steam
                new Color(0.82f, 0.83f, 0.86f, 0.70f), // cooling grey
                new Color(0.70f, 0.71f, 0.75f, 0.00f), // dissipate
            },
            Offsets = new[] { 0f, 0.16f, 0.42f, 0.76f, 1f },
        };

        var pm = new ParticleProcessMaterial
        {
            // N5: wider emission ring — the deluge arc spans the full pad diameter.
            EmissionShape           = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingAxis        = Vector3.Up,
            EmissionRingRadius      = 9.0f,   // N5: ~25 m radius ring (was 6 m)
            EmissionRingInnerRadius = 1.2f,
            EmissionRingHeight      = 1.5f,   // N5: taller emission band (was 1.0)

            // Fire mostly OUTWARD & slightly up; wide spread so it fans across pad.
            // Lower the vertical bias so the FIRST motion is a ground-hugging surge.
            Direction          = new Vector3(0f, 0.22f, 1f).Normalized(),
            Spread             = 88f,
            Flatness           = 0.78f,        // strong bias toward horizontal sheeting
            // N5: faster initial burst so the cloud DOMINATES the screen at 0-3 s.
            // Godot values are render units/s: 11–26 corresponds to roughly
            // 31–73 m/s, consistent with a violent but ground-bound deluge front.
            InitialVelocityMin = 11f,
            InitialVelocityMax = 26f,

            // Heavy damping so it decelerates and balloons rather than streaking.
            DampingMin = 1.3f,
            DampingMax = 3.4f,

            // Buoyancy: once the outward surge slows, it mushrooms upward.
            Gravity = new Vector3(0f, 0.75f, 0f),

            // Turbulent, slow drift for that churning volume.
            TurbulenceEnabled               = true,
            TurbulenceNoiseStrength         = 4.0f,   // N5: chunkier turbulence (was 3.4)
            TurbulenceNoiseScale            = 1.1f,
            TurbulenceInfluenceMin          = 0.14f,
            TurbulenceInfluenceMax          = 0.60f,

            AngularVelocityMin = -55f,
            AngularVelocityMax = 55f,

            // N5: bigger initial scale — the billows dominate the frame from birth.
            ScaleMin = 1.3f,
            ScaleMax = 3.4f,
            ColorRamp = new GradientTexture1D { Gradient = grad },
        };
        SetGrowCurve(pm, 0.30f, 1.0f); // N5: start smaller and grow more aggressively

        // N5: larger quad mesh — each billboard covers more screen area.
        var quad = new QuadMesh { Size = new Vector2(6.8f, 2.6f) };
        quad.SurfaceSetMaterial(0, SteamDrawMaterial(energy: 0.72f)); // N5: brighter (was 1.15)

        return new GpuParticles3D
        {
            Name            = "DelugeSteamCore",
            Amount          = 560,
            Lifetime        = 7.5f,           // N5: longer-lived (was 6.5)
            Preprocess      = 1.85f,          // dense, already-developed ignition frame
            Explosiveness   = 0.12f,          // N5: more burst-like at ignition (was 0.08)
            Randomness      = 0.5f,
            ProcessMaterial = pm,
            DrawPass1       = quad,
            Emitting        = false,
            LocalCoords     = true,           // follows the explicitly body-fixed ground pivot
            VisibilityAabb  = new Aabb(new Vector3(-320f, -12f, -320f), new Vector3(640f, 380f, 640f)),
        };
    }

    /// <summary>
    /// Taller, slower boil-up column behind the core — fills in the vertical
    /// mushrooming as the cloud climbs. Larger, dimmer, longer-lived puffs.
    /// </summary>
    private GpuParticles3D BuildSteamBoil()
    {
        var grad = new Gradient
        {
            Colors = new[]
            {
                new Color(1.00f, 0.48f, 0.18f, 0.40f),
                new Color(0.98f, 0.86f, 0.78f, 0.70f),
                new Color(0.74f, 0.75f, 0.79f, 0.45f),
                new Color(0.62f, 0.63f, 0.68f, 0.00f),
            },
            Offsets = new[] { 0f, 0.08f, 0.65f, 1f },
        };

        var pm = new ParticleProcessMaterial
        {
            EmissionShape           = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingAxis        = Vector3.Up,
            EmissionRingRadius      = 16.0f,   // N5: wider boil column (was 13)
            EmissionRingInnerRadius = 3.0f,
            EmissionRingHeight      = 2.0f,    // N5: taller emission band (was 1.5)

            Direction          = new Vector3(0f, 1f, 0.45f).Normalized(),
            Spread             = 62f,
            // N5: faster vertical surge during 0-3 s — tower of steam above the pad.
            InitialVelocityMin = 3.5f,
            InitialVelocityMax = 9.0f,

            DampingMin = 0.8f,
            DampingMax = 2.2f,

            Gravity = new Vector3(0f, 1.1f, 0f),

            TurbulenceEnabled       = true,
            TurbulenceNoiseStrength = 4.2f,     // N5: chunkier (was 3.8)
            TurbulenceNoiseScale    = 0.85f,
            TurbulenceInfluenceMin  = 0.18f,
            TurbulenceInfluenceMax  = 0.65f,

            AngularVelocityMin = -32f,
            AngularVelocityMax = 32f,

            // N5: larger puffs — the boil column fills the sky above the pad.
            ScaleMin = 1.6f,
            ScaleMax = 4.2f,
            ColorRamp = new GradientTexture1D { Gradient = grad },
        };
        SetGrowCurve(pm, 0.40f, 1.0f);

        var quad = new QuadMesh { Size = new Vector2(7.2f, 2.8f) };
        quad.SurfaceSetMaterial(0, SteamDrawMaterial(energy: 0.62f));   // N5: slightly brighter (was 0.9)

        return new GpuParticles3D
        {
            Name            = "DelugeSteamBoil",
            Amount          = 300,
            Lifetime        = 11.0f,           // N5: slightly longer (was 10.0)
            Preprocess      = 1.0f,
            Explosiveness   = 0.05f,           // N5: slight burst at ignition (was 0.0)
            Randomness      = 0.6f,
            ProcessMaterial = pm,
            DrawPass1       = quad,
            Emitting        = false,
            LocalCoords     = true,
            VisibilityAabb  = new Aabb(new Vector3(-360f, -12f, -360f), new Vector3(720f, 560f, 720f)),
        };
    }

    /// <summary>
    /// Low, dark dust/debris kicked outward across the deck. Smaller, faster,
    /// hugs the ground, browner and more opaque than steam.
    /// </summary>
    private GpuParticles3D BuildDust()
    {
        var grad = new Gradient
        {
            Colors = new[]
            {
                new Color(0.42f, 0.38f, 0.33f, 0.00f),
                new Color(0.40f, 0.36f, 0.31f, 0.65f), // dark dust
                new Color(0.50f, 0.47f, 0.43f, 0.40f), // lightening as it spreads
                new Color(0.55f, 0.53f, 0.50f, 0.00f),
            },
            Offsets = new[] { 0f, 0.15f, 0.6f, 1f },
        };

        var pm = new ParticleProcessMaterial
        {
            EmissionShape           = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingAxis        = Vector3.Up,
            EmissionRingRadius      = 6.0f,    // N5: wider source ring (was 4.0)
            EmissionRingInnerRadius = 0.6f,
            EmissionRingHeight      = 0.4f,

            // Almost flat — blasts sideways across the pad, fast and far.
            Direction          = new Vector3(0f, 0.06f, 1f).Normalized(),
            Spread             = 90f,
            Flatness           = 0.95f,
            // N5: faster blast for a dramatic ground-level sweep.
            InitialVelocityMin = 16f,
            InitialVelocityMax = 34f,

            DampingMin = 2.5f,
            DampingMax = 6.0f,

            Gravity = new Vector3(0f, -1.2f, 0f), // dust settles, doesn't rise

            TurbulenceEnabled       = true,
            TurbulenceNoiseStrength = 2.0f,
            TurbulenceNoiseScale    = 1.3f,
            TurbulenceInfluenceMin  = 0.05f,
            TurbulenceInfluenceMax  = 0.35f,

            // N5: larger dust chunks — more visible on screen.
            ScaleMin = 4.0f,   // was 3.0
            ScaleMax = 8.0f,   // was 6.0
            ColorRamp = new GradientTexture1D { Gradient = grad },
        };
        SetGrowCurve(pm, 0.50f, 1.0f);

        var quad = new QuadMesh { Size = new Vector2(9.5f, 3.4f) };
        // Dust is lit-ish but still unshaded soft; alpha blend (not additive) so
        // it reads as dark, occluding debris rather than glowing vapour.
        var drawMat = new StandardMaterial3D
        {
            BillboardMode          = BaseMaterial3D.BillboardModeEnum.Particles,
            ShadingMode            = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode              = BaseMaterial3D.BlendModeEnum.Mix,
            Transparency           = BaseMaterial3D.TransparencyEnum.Alpha,
            DepthDrawMode          = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            AlbedoTexture          = SoftCircle,
            AlbedoColor            = Colors.White,
            VertexColorUseAsAlbedo = true,
        };
        quad.SurfaceSetMaterial(0, drawMat);

        return new GpuParticles3D
        {
            Name            = "DelugeDust",
            Amount          = 110,             // N5: was 90
            Lifetime        = 5.5f,            // N5: longer-lived (was 4.5)
            Preprocess      = 0.3f,
            Explosiveness   = 0.18f,           // N5: more burst (was 0.12)
            Randomness      = 0.5f,
            ProcessMaterial = pm,
            DrawPass1       = quad,
            Emitting        = false,
            LocalCoords     = true,
            VisibilityAabb  = new Aabb(new Vector3(-320f, -10f, -320f), new Vector3(640f, 80f, 640f)),
        };
    }

    /// <summary>
    /// Faint, very slow-drifting ground dust haze that lingers across the deck
    /// after the initial blast — large, near-transparent low puffs that read as a
    /// settling pall of vapour/dust hanging over the pad. Cheap and long-lived.
    /// </summary>
    private GpuParticles3D BuildHaze()
    {
        var grad = new Gradient
        {
            Colors = new[]
            {
                new Color(0.80f, 0.80f, 0.82f, 0.00f),
                new Color(0.78f, 0.78f, 0.80f, 0.22f), // faint pale haze
                new Color(0.72f, 0.72f, 0.75f, 0.16f),
                new Color(0.68f, 0.68f, 0.71f, 0.00f),
            },
            Offsets = new[] { 0f, 0.2f, 0.7f, 1f },
        };

        var pm = new ParticleProcessMaterial
        {
            EmissionShape           = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingAxis        = Vector3.Up,
            EmissionRingRadius      = 18.0f,
            EmissionRingInnerRadius = 2.0f,
            EmissionRingHeight      = 0.5f,

            // Slow, near-flat outward creep — the haze just hangs and spreads.
            Direction          = new Vector3(0f, 0.05f, 1f).Normalized(),
            Spread             = 90f,
            Flatness           = 0.92f,
            InitialVelocityMin = 0.8f,
            InitialVelocityMax = 3.0f,

            DampingMin = 1.5f,
            DampingMax = 4f,

            Gravity = new Vector3(0f, 0.4f, 0f), // barely rises

            TurbulenceEnabled       = true,
            TurbulenceNoiseStrength = 1.2f,
            TurbulenceNoiseScale    = 0.7f,
            TurbulenceInfluenceMin  = 0.05f,
            TurbulenceInfluenceMax  = 0.25f,

            ScaleMin = 10.0f,
            ScaleMax = 17.0f,
            ColorRamp = new GradientTexture1D { Gradient = grad },
        };
        SetGrowCurve(pm, 0.6f, 1.0f);

        var quad = new QuadMesh { Size = new Vector2(14.0f, 4.2f) };
        // Soft alpha-blended haze — not additive, so it reads as a dim pall.
        var drawMat = new StandardMaterial3D
        {
            BillboardMode          = BaseMaterial3D.BillboardModeEnum.Particles,
            ShadingMode            = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode              = BaseMaterial3D.BlendModeEnum.Mix,
            Transparency           = BaseMaterial3D.TransparencyEnum.Alpha,
            DepthDrawMode          = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            AlbedoTexture          = SoftCircle,
            AlbedoColor            = Colors.White,
            VertexColorUseAsAlbedo = true,
        };
        quad.SurfaceSetMaterial(0, drawMat);

        return new GpuParticles3D
        {
            Name            = "DelugeHaze",
            Amount          = 60,
            Lifetime        = 12.0f,
            Preprocess      = 2.0f,           // start with haze already settled
            Explosiveness   = 0.0f,
            Randomness      = 0.7f,
            ProcessMaterial = pm,
            DrawPass1       = quad,
            Emitting        = false,
            LocalCoords     = true,
            VisibilityAabb  = new Aabb(new Vector3(-360f, -10f, -360f), new Vector3(720f, 120f, 720f)),
        };
    }

    /// <summary>
    /// N5: Fast, very flat radial dust blast ring at deck level.
    /// A short-lived, high-explosiveness emitter that fires at ignition and
    /// expands RADIALLY outward in a thin disk, like a pressure wave sweeping
    /// the pad surface. This is the "shockwave of dust" seen 0–2 s after ignition
    /// in real Super Heavy launches — it spreads 50–100 m from the mount in
    /// the first two seconds and then quickly fades.
    /// </summary>
    private GpuParticles3D BuildDustRadial()
    {
        var grad = new Gradient
        {
            Colors = new[]
            {
                new Color(0.55f, 0.50f, 0.42f, 0.00f), // born transparent
                new Color(0.50f, 0.46f, 0.38f, 0.80f), // opaque tan dust
                new Color(0.60f, 0.57f, 0.52f, 0.50f), // fading lighter
                new Color(0.65f, 0.63f, 0.60f, 0.00f), // dissipate
            },
            Offsets = new[] { 0f, 0.10f, 0.55f, 1f },
        };

        var pm = new ParticleProcessMaterial
        {
            // Emit from a tight ring right at the pad-arm radius.
            EmissionShape           = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingAxis        = Vector3.Up,
            EmissionRingRadius      = 5.5f,
            EmissionRingInnerRadius = 0.5f,
            EmissionRingHeight      = 0.15f,   // very thin — hugs the deck

            // Purely radial outward blast: no vertical component.
            Direction          = new Vector3(0f, 0.03f, 1f).Normalized(),
            Spread             = 90f,
            Flatness           = 0.98f,        // nearly perfectly horizontal
            InitialVelocityMin = 24f,
            InitialVelocityMax = 48f,

            // Strong deceleration — the wave front hits air resistance quickly.
            DampingMin = 5f,
            DampingMax = 11f,

            Gravity = new Vector3(0f, -2.5f, 0f), // dust falls back to deck

            TurbulenceEnabled       = true,
            TurbulenceNoiseStrength = 1.5f,
            TurbulenceNoiseScale    = 2.0f,
            TurbulenceInfluenceMin  = 0.04f,
            TurbulenceInfluenceMax  = 0.20f,

            // Medium scale — flat slabs of kicked concrete dust.
            ScaleMin = 2.5f,
            ScaleMax = 5.5f,
            ColorRamp = new GradientTexture1D { Gradient = grad },
        };
        SetGrowCurve(pm, 0.45f, 1.0f);

        var quad = new QuadMesh { Size = new Vector2(4.5f, 4.5f) };
        var drawMat = new StandardMaterial3D
        {
            BillboardMode          = BaseMaterial3D.BillboardModeEnum.Particles,
            ShadingMode            = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode              = BaseMaterial3D.BlendModeEnum.Mix,   // opaque occlusion
            Transparency           = BaseMaterial3D.TransparencyEnum.Alpha,
            DepthDrawMode          = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            AlbedoTexture          = SoftCircle,
            AlbedoColor            = Colors.White,
            VertexColorUseAsAlbedo = true,
        };
        quad.SurfaceSetMaterial(0, drawMat);

        return new GpuParticles3D
        {
            Name            = "DelugeRadialDust",
            Amount          = 100,
            Lifetime        = 3.0f,            // short-lived: gone by ~3 s
            Preprocess      = 0.0f,            // no preprocess — this IS the initial blast
            Explosiveness   = 0.65f,           // very burst-like at ignition
            Randomness      = 0.4f,
            ProcessMaterial = pm,
            DrawPass1       = quad,
            Emitting        = false,
            LocalCoords     = true,
            VisibilityAabb  = new Aabb(new Vector3(-380f, -10f, -380f), new Vector3(760f, 30f, 760f)),
        };
    }

    // ── Shared material / texture helpers ────────────────────────────────────

    /// <summary>
    /// Soft, slightly self-illuminated steam billboard. Camera-facing alpha
    /// cards are required here because this material is shared by both the GPU
    /// particle layers and the immediate ignition bank.
    /// </summary>
    private static StandardMaterial3D SteamDrawMaterial(float energy)
    {
        return new StandardMaterial3D
        {
            BillboardMode            = BaseMaterial3D.BillboardModeEnum.Enabled,
            ShadingMode              = BaseMaterial3D.ShadingModeEnum.Unshaded,
            // Alpha mixing lets hundreds of overlapping billows become an
            // optically dense wall instead of isolated glowing discs.
            BlendMode                = BaseMaterial3D.BlendModeEnum.Mix,
            Transparency             = BaseMaterial3D.TransparencyEnum.Alpha,
            DepthDrawMode            = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            CullMode                 = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoTexture            = SoftCircle,
            AlbedoColor              = Colors.White,
            EmissionEnabled          = true,
            EmissionEnergyMultiplier = energy,
            VertexColorUseAsAlbedo   = true,
        };
    }

    /// <summary>Sets a scale-over-lifetime curve so billboards grow as they age.</summary>
    private static void SetGrowCurve(ParticleProcessMaterial pm, float start, float end)
    {
        var curve = new Curve();
        curve.AddPoint(new Vector2(0f, start));
        curve.AddPoint(new Vector2(1f, end));
        pm.ScaleCurve = new CurveTexture { Curve = curve };
    }

    /// <summary>
    /// Procedural soft round billboard (radial alpha falloff). Shared by all
    /// layers; built once and cached statically.
    /// </summary>
    private static ImageTexture BuildSoftCircleTexture()
    {
        const int S = 96;
        var img = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
        float half = S * 0.5f;
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float dx = (x - half) / half;
            float dy = (y - half) / half;
            // Overlapping anisotropic lobes form an irregular billow rather
            // than the former perfect circular "particle ball".
            float l0 = Mathf.Exp(-(dx * dx * 2.4f + dy * dy * 3.0f));
            float l1 = Mathf.Exp(-((dx + 0.34f) * (dx + 0.34f) * 5.2f
                + (dy - 0.05f) * (dy - 0.05f) * 6.0f));
            float l2 = Mathf.Exp(-((dx - 0.30f) * (dx - 0.30f) * 5.8f
                + (dy + 0.12f) * (dy + 0.12f) * 4.8f));
            float l3 = Mathf.Exp(-((dx + 0.05f) * (dx + 0.05f) * 7.0f
                + (dy + 0.38f) * (dy + 0.38f) * 7.5f));
            float edge = Mathf.Clamp(1f - Mathf.Sqrt(dx * dx + dy * dy) * 0.86f, 0f, 1f);
            float breakup = 0.86f + 0.14f * Mathf.Sin(x * 0.51f + y * 0.37f)
                * Mathf.Sin(x * 0.19f - y * 0.43f);
            float density = Mathf.Clamp(l0 * 0.72f + l1 * 0.42f + l2 * 0.40f + l3 * 0.28f,
                0f, 1f);
            float a = Mathf.SmoothStep(0f, 1f, density * edge * breakup);
            img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>
    /// One round puff. The top is sunlit and the belly is darker, so a stack
    /// of cards reads as cauliflower instead of one flat sheet. Image row 0
    /// is the top of a Godot texture.
    /// </summary>
    private static ImageTexture BuildWideCloudTexture()
    {
        const int Size = 128;
        var img = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);
        float half = Size * 0.5f;
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float dx = (x - half) / half;
            float dy = (y - half) / half;
            float radius = Mathf.Sqrt(dx * dx + dy * dy);
            float alpha = radius >= 1f ? 0f : Mathf.SmoothStep(0.98f, 0.46f, radius);
            // Bright cap, shaded underside. y=0 is the top of the image.
            float shade = Mathf.Lerp(1f, 0.42f, y / (float)(Size - 1));
            float crevice = Mathf.Clamp(1f - radius * 0.35f, 0.7f, 1f);
            float lit = shade * crevice;
            img.SetPixel(x, y, new Color(lit, lit * 0.985f, lit * 0.96f, alpha));
        }
        return ImageTexture.CreateFromImage(img);
    }

    // ── Math helpers ─────────────────────────────────────────────────────────

    private static Vector3 ToGodot(Vector3d v) =>
        new((float)v.X, (float)v.Y, (float)v.Z);

    /// <summary>
    /// Rotate <paramref name="node"/> so its local +Y axis points along
    /// <paramref name="up"/>, with a stable arbitrary roll.
    /// </summary>
    private static void AlignUp(Node3D node, Vector3 up)
    {
        up = up.Normalized();
        // Pick a reference not parallel to up to build an orthonormal basis.
        Vector3 reference = Mathf.Abs(up.Dot(Vector3.Forward)) > 0.95f
            ? Vector3.Right
            : Vector3.Forward;
        Vector3 x = reference.Cross(up).Normalized();
        Vector3 z = up.Cross(x).Normalized();
        var basis = new Basis(x, up, z);
        node.Transform = new Transform3D(basis, node.Position);
    }
}
