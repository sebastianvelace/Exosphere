namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;

[GlobalClass]
public partial class SimulationBridge : Node
{
    public static SimulationBridge Instance { get; private set; } = null!;

    public Universe Universe { get; private set; } = null!;
    public Vessel?  ActiveVessel => Universe.ActiveVessel;

    [Export] public string DataDirectory { get; set; } = "res://data";

    [Signal] public delegate void VesselStagedEventHandler(string detachedVesselId);
    [Signal] public delegate void VesselDestroyedEventHandler(string vesselId);
    [Signal] public delegate void SimulationLoadedEventHandler();

    // ── Time-warp API ─────────────────────────────────────────────────────
    public static readonly double[] WarpLevels = { 1, 2, 3, 5, 10, 50, 100, 1000, 10000, 100000 };
    public int WarpIndex          { get; private set; } = 0;
    public int MaxAllowedWarpIndex { get; private set; } = 9;

    /// <summary>Reason shown when the player tries to warp above <see cref="MaxAllowedWarpIndex"/>.</summary>
    public string? WarpClampReason { get; private set; }
    /// <summary>Simulation time (s) until <see cref="WarpClampReason"/> stops displaying.</summary>
    public double WarpClampReasonUntil { get; private set; }

    public void SetWarpIndex(int i)
    {
        int requested = i;
        i = System.Math.Clamp(i, 0, MaxAllowedWarpIndex);
        if (requested > MaxAllowedWarpIndex)
        {
            WarpClampReason = ComputeWarpClampReason();
            WarpClampReasonUntil = Universe.CurrentTime + 4.0;
        }
        WarpIndex = i;
        SetTimeScale(WarpLevels[WarpIndex]);
    }

    private string ComputeWarpClampReason()
    {
        var v = ActiveVessel;
        if (v != null && v.Throttle > 0.01) return "THRUSTING";
        if (v != null)
        {
            var refB = Universe.GetDominantBody(v.Position);
            if (refB.GetAtmosphericDensity(v.Position) > 0.01) return "ATMOSPHERE";
        }
        return "WARP LIMIT";
    }

    private bool                 _running        = false;
    private VesselRenderer?      _vesselRenderer = null;
    private Camera3D?            _camera         = null;
    private LaunchPadController? _launchPad      = null;

    // ── Launch site ───────────────────────────────────────────────────────
    /// <summary>Id of the pad every vessel launches from (see data/launch_sites).</summary>
    [Export] public string LaunchSiteId { get; set; } = "starbase";

    private LaunchSite? _launchSite;

    // ── Ignition ramp state ───────────────────────────────────────────────
    // True while Ignite() is spooling up and waiting for TWR > 1.02 to release the hold-down.
    private bool   _ignitionActive  = false;
    // Throttle rate used during the ignition ramp (throttle units per second).
    private const double IgnitionRampRate = 0.5;

    public override void _Ready()
    {
        Instance = this;

        var dataPath = ProjectSettings.GlobalizePath(DataDirectory);
        Universe = Universe.LoadFromDataDirectory(dataPath);
        Universe.TimeScale = 1.0;
        _running = true;

        var sites = LaunchSite.LoadAllFromDirectory(System.IO.Path.Combine(dataPath, "launch_sites"));
        if (!sites.TryGetValue(LaunchSiteId, out _launchSite))
            GD.PushWarning($"[Sim] Launch site '{LaunchSiteId}' not found; pad will fall back to the equator.");

        // Create sibling nodes — deferred: parent (Flight) is busy in _Ready()
        var mm = new MissionManager { Name = "MissionManager" };
        GetParent()?.CallDeferred("add_child", mm);

        var sky = new SkyController { Name = "SkyController" };
        GetParent()?.CallDeferred("add_child", sky);

        var audio = new AudioManager { Name = "AudioManager" };
        GetParent()?.CallDeferred("add_child", audio);

        // Sun-accurate lighting: orients the DirectionalLight from the real Sun direction
        // and feeds sun_dir to the planet materials (day/night terminator + city lights).
        var sun = new SunController { Name = "SunController" };
        GetParent()?.CallDeferred("add_child", sun);

        // Phase-driven environment: blends ambient/glow/sun energy by altitude so the
        // pad daylight and the space vacuum look are each correct. SunController owns
        // the light orientation; this owns its energy + the WorldEnvironment.
        var phaseLight = new PhaseLightingController { Name = "PhaseLightingController" };
        GetParent()?.CallDeferred("add_child", phaseLight);

        // Human-eye exposure adapts after the sky and phase-light controllers have
        // established the physical luminance state for this frame.
        var exposure = new VisualExposureController { Name = "VisualExposureController" };
        GetParent()?.CallDeferred("add_child", exposure);

        // Orbital map panel (toggle with M). Lives under the UI CanvasLayer so it
        // renders above the 3D world; it owns the autopilot as a child.
        var uiLayer = GetTree().Root.FindChild("UI", true, false) as CanvasLayer;
        if (uiLayer != null)
        {
            var map = new MapViewController { Name = "MapViewController" };
            uiLayer.CallDeferred("add_child", map);

            var edl = new EDLController { Name = "EDLController" };
            uiLayer.CallDeferred("add_child", edl);

            var ascent = new AscentController { Name = "AscentController" };
            uiLayer.CallDeferred("add_child", ascent);

            var warpCtrl = new WarpController { Name = "WarpController" };
            uiLayer.CallDeferred("add_child", warpCtrl);

            var systemsCtrl = new SystemsController { Name = "SystemsController" };
            uiLayer.CallDeferred("add_child", systemsCtrl);
        }

        // Create LaunchPadController in the World node
        var worldNode = GetTree().Root.FindChild("World", true, false) as Node3D;
        if (worldNode != null)
        {
            _launchPad = new LaunchPadController();
            _launchPad.Name = "LaunchPadController";
            worldNode.CallDeferred("add_child", _launchPad);

            var marsTerrain = new MarsTerrainController { Name = "MarsTerrainController" };
            worldNode.CallDeferred("add_child", marsTerrain);

            // Local true-scale Earth ground patch: flat far horizon + scrolling surface
            // features for motion at low altitude; fades into the scaled-space backdrop.
            var earthGround = new EarthGroundController { Name = "EarthGroundController" };
            worldNode.CallDeferred("add_child", earthGround);

            // Liftoff steam/dust deluge cloud at the pad.
            var launchFx = new LaunchEffectsController { Name = "LaunchEffectsController" };
            worldNode.CallDeferred("add_child", launchFx);

            // Pre-release engine startup glow, steam and ignition flicker at the mount.
            var startupFx = new EngineStartupController { Name = "EngineStartupController" };
            worldNode.CallDeferred("add_child", startupFx);

            // Transient Ship-engine flash and soot burst at Starship/Super Heavy hot-staging.
            var hotStageFx = new HotStageFlashController { Name = "HotStageFlashController" };
            worldNode.CallDeferred("add_child", hotStageFx);
        }

        SpawnStarshipStack(dataPath);
        SpawnPendingConstructedVessel(dataPath);
        SpawnPlanets();
        EmitSignal(SignalName.SimulationLoaded);

        _camera = GetTree().Root.FindChild("Camera3D", true, false) as Camera3D;
        if (_camera != null)
        {
            // Planets render as scaled-space backdrops at ~50 k units, so a modest far
            // plane suffices — which keeps the depth buffer precise across the whole scene.
            _camera.Near = 0.5f;
            _camera.Far  = 120_000.0f;
        }

        var light = GetTree().Root.FindChild("DirectionalLight3D", true, false) as DirectionalLight3D;
        if (light != null)
            light.RotationDegrees = new Godot.Vector3(-45f, -30f, 0f);
    }

    public override void _Process(double delta)
    {
        if (!_running || Universe == null) return;

        // ── Recalculate MaxAllowedWarpIndex ──────────────────────────────
        var av = ActiveVessel;
        if (av != null)
        {
            var refB = Universe.GetDominantBody(av.Position);
            bool forceSensitive = Universe.RequiresOffRailsPhysics(av);
            bool boundedEntry = Universe.RequiresBoundedWarpPropagation(av);
            bool atmosphericZone = refB.Atmosphere != null
                && av.GetAltitude(refB) <= refB.Atmosphere.MaxAltitude * 1.05;
            if (av.Throttle > 0.01)
            {
                // Warp IS allowed while thrusting now — the active vessel stays on RK4 with a
                // bounded sub-step (Universe.MaxThrustStep) so the burn is physics-faithful.
                // Cap it: x3 in atmosphere, x10 in vacuum (never on-rails while powered).
                MaxAllowedWarpIndex = atmosphericZone ? 2 : 4; // x3 atmosphere, x10 vacuum
            }
            else
            {
                MaxAllowedWarpIndex = forceSensitive ? 2
                    : boundedEntry ? 7                         // x1000 bounded coast to entry
                    : WarpLevels.Length - 1;
            }
            // Clamp current warp index if it now exceeds the allowed maximum
            if (WarpIndex > MaxAllowedWarpIndex)
                SetWarpIndex(MaxAllowedWarpIndex);
        }

        Universe.Tick(delta);

        // Hot-stage overlap finished in sim time → mechanical separation this frame.
        if (av != null && av.HotStageOverlapCompletedPending)
        {
            av.HotStageOverlapCompletedPending = false;
            TriggerStaging();
            av = ActiveVessel;
        }

        // ── Ignition ramp: sube throttle y suelta hold-down cuando TWR > 1.02 ──────
        if (_ignitionActive && av != null)
        {
            // Avanzar el throttle comandado hacia 1.0 a una tasa controlada
            av.Throttle = System.Math.Min(av.Throttle + IgnitionRampRate * delta, 1.0);

            if (av.IsGroundHeld)
            {
                // Verificar si el empuje actual ya supera la gravedad local × 1.02
                var refB2 = Universe.GetDominantBody(av.Position);
                if (refB2 != null && av.TotalMass > 0.0)
                {
                    double twr = av.GetThrustToWeightRatio(refB2);
                    if (twr > 1.02)
                    {
                        av.ReleaseGroundHold();
                        // Lanzamiento manual: al soltar los clamps por primera vez, arranca la FSM
                        // de misión (PRE_LAUNCH → LIFTOFF). BeginFlight() es idempotente, así que
                        // no pasa nada si la misión ya despegó por [L] (countdown).
                        // Manual launch: kick the mission FSM off PRE_LAUNCH the moment the clamps
                        // release. BeginFlight() is idempotent, so [L]/countdown launches are safe.
                        MissionManager.Instance?.BeginFlight();
                    }
                }
            }
            else
            {
                // Ya en vuelo: ignición completada cuando throttle alcanza 1.0
                if (av.Throttle >= 1.0)
                    _ignitionActive = false;
            }
        }

        // Anchor the launch complex to its fixed geodetic site, not to the point below the
        // moving vessel. Its local +Y must follow radial up; leaving Basis.Identity makes
        // the entire tower lean by the site's latitude/axial tilt in render space.
        var padEarth = Universe.GetBody("earth");
        if (_launchPad != null && ActiveVessel != null && padEarth != null)
        {
            const float metresPerUnit = 2.8f;
            double alt = ActiveVessel.GetAltitude(padEarth);
            double time = Universe.CurrentTime;
            var surfacePos = _launchSite?.GetPosition(padEarth, time)
                ?? padEarth.GetSurfacePositionAtTime(0.0, 0.0, time);
            var offset = surfacePos - ActiveVessel.Position;          // = -up·alt metres
            var position = new Godot.Vector3(
                (float)(offset.X / metresPerUnit),
                (float)(offset.Y / metresPerUnit),
                (float)(offset.Z / metresPerUnit));
            var frame = _launchSite?.GetLocalFrame(padEarth, time);
            var east = frame?.East ?? padEarth.GetEastDirection(surfacePos);
            var up = frame?.Up ?? (surfacePos - padEarth.Position).Normalized;
            var south = frame?.South ?? east.Cross(up).Normalized;
            var basis = new Basis(ToGodotVector(east), ToGodotVector(up), ToGodotVector(south));
            _launchPad.Transform = new Transform3D(basis, position);
            _launchPad.Visible = alt < 8_000;   // hide above 8 km
        }
    }

    private void SpawnPendingConstructedVessel(string dataPath)
    {
        var craft = CraftLaunchRequest.Pop();
        if (craft == null) return;

        var catalog = PartCatalog.LoadFromDirectory(System.IO.Path.Combine(dataPath, "parts"));
        var assembly = VesselAssembly.FromCraft(catalog, craft);
        PlaceConstructedVesselOnPad(assembly.ToVessel(craft.Name));
        GD.Print($"[VAB] Placed constructed craft on pad: {craft.Name}");
    }

    // ── Starship + Super Heavy stack on Starbase launchpad ────────────────

    private void SpawnStarshipStack(string dataPath)
    {
        var earth = Universe.GetBody("earth");
        if (earth == null) return;

        var defs = PartDefinition.LoadAllFromDirectory(System.IO.Path.Combine(dataPath, "parts"));

        if (!defs.TryGetValue("starship_command",  out var cmdDef))  return;
        if (!defs.TryGetValue("starship_tank",      out var tankDef)) return;
        if (!defs.TryGetValue("starship_engines",   out var engDef))  return;
        if (!defs.TryGetValue("starship_landing_gear", out var gearDef)) return;
        if (!defs.TryGetValue("decoupler_heavy",    out var decDef))  return;
        if (!defs.TryGetValue("super_heavy_booster",out var shDef))   return;

        var vessel = new Vessel { Name = "Starship IFT-7" };

        var command  = new Part(cmdDef);
        var tank     = new Part(tankDef);
        var engines  = new Part(engDef);
        var gear     = new Part(gearDef);
        var decoupler= new Part(decDef);
        var sh       = new Part(shDef);

        vessel.Parts.SetRoot(command);
        vessel.Parts.AddPart(command);
        vessel.Parts.AddPart(tank);
        vessel.Parts.AddPart(engines);
        vessel.Parts.AddPart(gear);
        vessel.Parts.AddPart(decoupler);
        vessel.Parts.AddPart(sh);

        // Stack (top → bottom): command → tank → engines → EDL gear → decoupler → SH.
        // The zero-length aggregate gear keeps the physical engine/interstage datum unchanged.
        vessel.Parts.AddJoint(new Joint(command,   tank,      "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(tank,      engines,   "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(engines,   gear,      "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(gear,      decoupler, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(decoupler, sh,        "bottom", "top"));
        vessel.ConfigureLandingContactsFromParts();

        // Stand the stack on the real launch site. The hull's +Y is rotated onto the local
        // vertical there, so the rocket is upright at the pad's true latitude instead of
        // being planted on an arbitrary axis. The stack bottom is placed at the shared
        // engineering spec's vehicle-interface elevation above civil grade.
        double mountHeightM = LaunchComplexSpec.StarbasePostDeluge.VehicleInterfaceElevation;
        StandOnPad(vessel, earth, mountHeightM);

        Universe.AddVessel(vessel);
        Universe.ActiveVessel = vessel;

        // Build renderer
        var vesselsNode = GetTree().Root.FindChild("Vessels", true, false) as Node3D;
        if (vesselsNode != null)
        {
            _vesselRenderer = new VesselRenderer();
            _vesselRenderer.Name = "StarshipRenderer";
            vesselsNode.AddChild(_vesselRenderer);
            _vesselRenderer.BuildFromVessel(vessel);

            // First-person cockpit interior — a SIBLING of the rocket model (at the render origin)
            // so the CameraController can hide the rocket and show only the cockpit in Cockpit mode.
            // CameraController orients it to the vessel each frame.
            vesselsNode.AddChild(new CockpitRenderer { Name = "CockpitRenderer" });
            AddChild(new CockpitInstruments { Name = "CockpitInstruments" });

            // MaxQ condensation ring — tracks active vessel (always at render origin)
            var maxQ = new MaxQRingController { Name = "MaxQRing" };
            vesselsNode.AddChild(maxQ);

            // Re-entry plasma glow — driven by the real convective heat flux (ρ·v³)
            var plasma = new ReentryPlasmaController { Name = "ReentryPlasma" };
            vesselsNode.AddChild(plasma);

            var fo = GetTree().Root.FindChild("FloatingOrigin", true, false) as FloatingOrigin;
            fo?.RegisterVesselNode(vessel.Id, _vesselRenderer);
        }
    }

    // ── Planets ───────────────────────────────────────────────────────────

    private void SpawnPlanets()
    {
        var planetsNode = GetTree().Root.FindChild("Planets", true, false) as Node3D;
        var fo          = GetTree().Root.FindChild("FloatingOrigin", true, false) as FloatingOrigin;
        if (planetsNode == null || fo == null) return;

        foreach (var body in Universe.Bodies)
        {
            // The photosphere is rendered by the sky shader at its exact angular radius.
            // A second scaled-space sphere would overlap it and break eclipse silhouettes.
            if (body.Id == "sun") continue;
            // Unit sphere — FloatingOrigin scales each planet per-frame to its correct
            // angular size as a precision-safe "scaled-space" backdrop. The shader supplies
            // its own atmospheric Fresnel rim, so no separate glow shell is needed.
            var sphere = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 96, Rings = 48 };
            var mat = body.Id == "earth"
                ? PlanetMaterials.CreateEarth()
                : PlanetMaterials.CreatePlanet(body.Id, GetPlanetColor(body.Id));

            var mesh = new MeshInstance3D { Name = body.Name + "_mesh", Mesh = sphere };
            mesh.SetSurfaceOverrideMaterial(0, mat);
            planetsNode.AddChild(mesh);
            fo.RegisterPlanetNode(body.Id, mesh);

            if (body.Id == "saturn") AddSaturnRing(mesh);
        }
    }

    // Saturn's rings: a flat annulus child (local XZ plane) that scales/tilts with the
    // scaled-space backdrop sphere. Inner/outer radii in body-radius units (sphere = 1).
    private static void AddSaturnRing(MeshInstance3D parent)
    {
        var ring = new MeshInstance3D { Name = "SaturnRing", Mesh = BuildRingMesh(1.20f, 2.30f, 160) };
        var shader = GD.Load<Shader>("res://assets/shaders/saturn_ring.gdshader");
        if (shader != null)
        {
            var rmat = new ShaderMaterial { Shader = shader };
            var img = Image.LoadFromFile(ProjectSettings.GlobalizePath("res://assets/textures/saturn_ring.png"));
            if (img != null) { img.GenerateMipmaps(); rmat.SetShaderParameter("ring_tex", ImageTexture.CreateFromImage(img)); }
            ring.SetSurfaceOverrideMaterial(0, rmat);
        }
        ring.CustomAabb = new Aabb(new Godot.Vector3(-2.4f, -0.1f, -2.4f), new Godot.Vector3(4.8f, 0.2f, 4.8f));
        parent.AddChild(ring);
    }

    private static ArrayMesh BuildRingMesh(float inner, float outer, int seg)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < seg; i++)
        {
            float a0 = i / (float)seg * Mathf.Tau, a1 = (i + 1) / (float)seg * Mathf.Tau;
            var d0 = new Godot.Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
            var d1 = new Godot.Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            float u0 = i / (float)seg, u1 = (i + 1) / (float)seg;
            RingVert(st, d0 * inner, 0f, u0); RingVert(st, d0 * outer, 1f, u0); RingVert(st, d1 * outer, 1f, u1);
            RingVert(st, d0 * inner, 0f, u0); RingVert(st, d1 * outer, 1f, u1); RingVert(st, d1 * inner, 0f, u1);
        }
        return st.Commit();
    }

    private static void RingVert(SurfaceTool st, Godot.Vector3 p, float radialU, float angV)
    {
        st.SetNormal(Godot.Vector3.Up);
        st.SetUV(new Godot.Vector2(radialU, angV));
        st.AddVertex(p);
    }

    private static Color GetPlanetColor(string id) => id switch
    {
        "earth"   => new Color(0.20f, 0.45f, 0.80f),
        "moon"    => new Color(0.60f, 0.60f, 0.60f),
        "mars"    => new Color(0.70f, 0.30f, 0.15f),
        "venus"   => new Color(0.85f, 0.75f, 0.40f),
        "mercury" => new Color(0.50f, 0.48f, 0.46f),
        "jupiter" => new Color(0.80f, 0.65f, 0.45f),
        "saturn"  => new Color(0.90f, 0.80f, 0.55f),
        "sun"     => new Color(1.00f, 0.90f, 0.30f),
        _         => new Color(0.70f, 0.70f, 0.70f),
    };

    private static Godot.Vector3 ToGodotVector(Vector3d value) => new(
        (float)value.X, (float)value.Y, (float)value.Z);

    // ── Public API ────────────────────────────────────────────────────────

    public void SetThrottle(double t) { if (ActiveVessel != null) ActiveVessel.Throttle = t; }
    public void SetSAS(bool on)       { if (ActiveVessel != null) ActiveVessel.SASEnabled = on; }
    public void ReleaseGroundHold()   { ActiveVessel?.ReleaseGroundHold(); }

    /// <summary>
    /// Stands <paramref name="vessel"/> on the launch pad: at the launch site's real
    /// geodetic position, upright along the local vertical, and already moving with the
    /// ground beneath it.
    ///
    /// The site's latitude is what earns the free eastward velocity the body's rotation
    /// gives a pad (ω·R·cos φ — ≈408 m/s at Kennedy). Spawning along an arbitrary axis
    /// instead puts the pad at the wrong latitude and quietly steals that delta-v, so the
    /// pad frame is derived from data, never hardcoded.
    /// </summary>
    private void StandOnPad(Vessel vessel, CelestialBody body, double mountHeightM)
    {
        Vector3d padSurface = _launchSite != null
            ? _launchSite.GetPosition(body)
            : body.GetSurfacePosition(0.0, 0.0);   // fallback: the equator, not the pole

        var upDir = (padSurface - body.Position).Normalized;

        vessel.Position    = padSurface + upDir * mountHeightM;
        vessel.Velocity    = body.Velocity + body.GetSurfaceVelocity(vessel.Position);
        vessel.Orientation = Quaterniond.FromTo(Vector3d.Up, upDir);  // hull +Y → local vertical
        vessel.SASEnabled  = true;

        // Ground hold: keeps the vessel locked to the surface until T-0.
        vessel.IsGroundHeld = true;
        vessel.GroundNormal = upDir;
        vessel.GroundOffset = mountHeightM;

    }

    /// <summary>
    /// Places an externally constructed vessel on the active launch pad and makes it the
    /// controlled vessel. Used by the VAB/export flow; keeps the same ground-hold contract
    /// as the default Starship stack.
    /// </summary>
    public void PlaceConstructedVesselOnPad(Vessel vessel, double mountHeightM = -1.0)
    {
        var earth = Universe.GetBody("earth");
        if (earth == null) return;

        if (ActiveVessel != null)
            Universe.RemoveVessel(ActiveVessel);

        if (mountHeightM < 0.0)
            mountHeightM = LaunchComplexSpec.StarbasePostDeluge.VehicleInterfaceElevation;
        StandOnPad(vessel, earth, mountHeightM);
        vessel.ConfigureLandingContactsFromParts();

        Universe.AddVessel(vessel);
        Universe.ActiveVessel = vessel;

        var vesselsNode = GetTree().Root.FindChild("Vessels", true, false) as Node3D;
        if (_vesselRenderer == null && vesselsNode != null)
        {
            _vesselRenderer = new VesselRenderer { Name = "StarshipRenderer" };
            vesselsNode.AddChild(_vesselRenderer);
        }

        _vesselRenderer?.BuildFromVessel(vessel);
        var fo = GetTree().Root.FindChild("FloatingOrigin", true, false) as FloatingOrigin;
        if (_vesselRenderer != null)
            fo?.RegisterVesselNode(vessel.Id, _vesselRenderer);
    }

    // ── Ignition / throttle contracts (consumed by HUDController / Agente E) ─

    /// <summary>
    /// True while Ignite() is ramping up thrust and waiting for TWR &gt; 1.02 to release
    /// the hold-down clamps. Resets once the vessel lifts off and throttle reaches 1.0.
    /// </summary>
    public bool IsIgnitionActive => _ignitionActive;

    /// <summary>
    /// Secuencia de ignición: arranca la rampa de throttle comandado hacia 1.0 y suelta
    /// los hold-downs automáticamente cuando TWR &gt; 1.02.
    /// Si el vessel ya está en vuelo, fija el throttle al máximo de inmediato.
    /// </summary>
    public void Ignite()
    {
        var v = ActiveVessel;
        if (v == null) return;

        if (v.IsGroundHeld)
        {
            // Inicio de secuencia de despegue: rampa controlada hasta soltar los clamps
            _ignitionActive = true;
        }
        else
        {
            // En vuelo: throttle máximo instantáneo (sin secuencia de hold-down)
            v.Throttle = 1.0;
            _ignitionActive = false;
        }
    }

    /// <summary>
    /// Sube el throttle comandado de forma continua. El spool de los motores suaviza la respuesta.
    /// Llamar cada frame con el dt real para un ascenso fluido sosteniendo la tecla.
    /// </summary>
    public void ThrottleUp(double dt)
    {
        var v = ActiveVessel;
        if (v == null) return;
        v.Throttle = System.Math.Min(v.Throttle + 0.5 * dt, 1.0);
    }

    /// <summary>
    /// Baja el throttle comandado de forma continua. El spool de los motores suaviza la respuesta.
    /// </summary>
    public void ThrottleDown(double dt)
    {
        var v = ActiveVessel;
        if (v == null) return;
        v.Throttle = System.Math.Max(v.Throttle - 0.5 * dt, 0.0);
    }

    /// <summary>
    /// Starts the Flight 7 dual-thrust hot-stage window. Ship engines join the burning set
    /// while Super Heavy remains attached; mechanical stage fires when the window expires.
    /// </summary>
    public void BeginHotStageOverlap(double durationSeconds = -1.0)
    {
        if (ActiveVessel == null) return;
        if (ActiveVessel.IsHotStageOverlapping) return;
        double duration = durationSeconds > 0.0
            ? durationSeconds
            : AscentStagingPolicy.HotStageOverlapSeconds;
        ActiveVessel.BeginHotStageOverlap(duration);
    }

    public void TriggerStaging()
    {
        if (ActiveVessel == null) return;
        // Instant stage (manual / post-overlap) cancels any remaining overlap cleanly.
        ActiveVessel.HotStageOverlapCompletedPending = false;
        var debris = ActiveVessel.Stage();
        if (debris == null) return;

        // Rebase the remaining Ship from the old stack-base datum to the physical
        // separation plane. Without this, Ship and Super Heavy occupy the same world point
        // while their renderers use different local origins, producing total interpenetration.
        Vector3d axis = ActiveVessel.Orientation.Rotate(Vector3d.Up).Normalized;
        double separationHeight = System.Math.Max(1.0, debris.VehicleLength);
        ActiveVessel.Position += axis * separationHeight;

        // Small spring/pusher impulse with zero net momentum and 1 m/s relative opening.
        double shipMass = System.Math.Max(ActiveVessel.TotalMass, 1.0);
        double debrisMass = System.Math.Max(debris.TotalMass, 1.0);
        double totalMass = shipMass + debrisMass;
        ActiveVessel.Velocity += axis * (debrisMass / totalMass);
        debris.Velocity -= axis * (shipMass / totalMass);

        Universe.AddVessel(debris);

        // Rebuild active vessel renderer: SH is now gone → shows standalone Starship
        _vesselRenderer?.BuildFromVessel(ActiveVessel);

        // Spawn a renderer for the SH debris
        var fo          = GetTree().Root.FindChild("FloatingOrigin", true, false) as FloatingOrigin;
        var vesselsNode = GetTree().Root.FindChild("Vessels",        true, false) as Node3D;
        if (vesselsNode != null)
        {
            var debrisRenderer = new VesselRenderer();
            debrisRenderer.Name = "SHDebris_" + debris.Id[..8];
            vesselsNode.AddChild(debrisRenderer);
            debrisRenderer.BuildFromVessel(debris);
            fo?.RegisterVesselNode(debris.Id, debrisRenderer);
        }

        EmitSignal(SignalName.VesselStaged, debris.Id);
        MissionManager.Instance?.NotifyStaged();
    }

    public void SetTimeScale(double scale) => Universe.TimeScale = scale;

    /// <summary>
    /// Starts the verified Starship entry-to-landing demonstration used by the HUD and the
    /// visual regression harness.  This is a real physics state: EDL owns attitude/throttle,
    /// atmospheric drag and heating run normally, Raptors relight for the powered flip, and
    /// landing contact is solved by the regular surface-contact model.
    /// </summary>
    public bool BeginReentryDemonstration()
    {
        var earth = Universe.GetBody("earth");
        var vessel = ActiveVessel;
        if (earth == null || vessel == null || vessel.IsDestroyed) return false;

        // The launch scene begins as a full stack. Separate first so EDL controls the Ship
        // engine cluster and aerodynamic body rather than an impossible attached booster.
        if (vessel.Parts.Parts.Any(p => p.Definition.Id == "super_heavy_booster"))
        {
            TriggerStaging();
            vessel = ActiveVessel;
        }
        if (vessel == null || !vessel.Parts.Parts.Any(
                p => p.Definition.Id == "starship_engines"))
            return false;

        Vector3d currentUp = (vessel.Position - earth.Position).Normalized;
        if (currentUp.MagnitudeSquared < 1e-9) currentUp = Vector3d.Right;

        // Put the demonstration on the daylight side so entry plasma, flaps and landing
        // attitude remain inspectable instead of inheriting whatever local solar time the
        // launch pad happens to have at J2000.
        Vector3d up = currentUp;
        var sun = Universe.GetBody("sun");
        if (sun != null)
        {
            Vector3d toSun = (sun.Position - earth.Position).Normalized;
            Vector3d terminatorUp = currentUp - toSun * currentUp.Dot(toSun);
            if (terminatorUp.MagnitudeSquared < 1e-9)
            {
                Vector3d seed = System.Math.Abs(toSun.Dot(Vector3d.Up)) < 0.9
                    ? Vector3d.Up
                    : Vector3d.Right;
                terminatorUp = seed - toSun * seed.Dot(toSun);
            }
            const double solarElevation = 25.0 * System.Math.PI / 180.0;
            up = (terminatorUp.Normalized * System.Math.Cos(solarElevation)
                + toSun * System.Math.Sin(solarElevation)).Normalized;
        }
        Vector3d east = earth.RotationAxis.Cross(up).Normalized;
        if (east.MagnitudeSquared < 1e-9) east = Vector3d.Forward;

        // A repeatable suborbital entry state chosen to expose heating, aerodynamic descent,
        // belly-flop, powered flip and touchdown in one watchable session. It intentionally
        // starts at the 70 km entry interface instead of pretending to perform a deorbit burn.
        Vector3d airVelocity = east * 1_800.0 - up * 120.0;
        Vector3d velocityDirection = airVelocity.Normalized;
        Vector3d longAxis = AerodynamicsModel.ComputeLiftUpEntryAxis(up, velocityDirection);

        vessel.Position = earth.Position + up * (earth.Radius + 70_000.0);
        vessel.Velocity = earth.Velocity + earth.GetSurfaceVelocity(vessel.Position) + airVelocity;
        vessel.Orientation = AerodynamicsModel.ComputeBellyFirstOrientation(
            longAxis, velocityDirection);
        vessel.AngularVelocity = Vector3d.Zero;
        vessel.PitchYawRoll = Vector3d.Zero;
        vessel.SASEnabled = false;
        vessel.IsGroundHeld = false;
        vessel.IsOnRails = false;
        vessel.OrbitalState = null;
        vessel.ReferenceBodyId = earth.Id;
        vessel.Throttle = 0.0;
        vessel.ConfigureLandingContactsFromParts();

        foreach (var part in vessel.Parts.Parts)
        {
            part.Temperature = 290.0;
            part.SkinTemperature = 290.0;
            part.ThermalDamage = 0.0;
            part.IsBroken = false;
            part.IsDeployed = false;
            part.ThrottleLevel = 0.0;
            part.GimbalOffset = Vector3d.Zero;

            double totalCapacity = part.Definition.FuelCapacityLF
                + part.Definition.FuelCapacityOx;
            if (totalCapacity <= 0.0) continue;
            double target = totalCapacity * 0.12;
            double mixtureCapacity = totalCapacity > 1e-9
                ? part.Definition.FuelCapacityLF / totalCapacity
                : 0.45;
            part.LiquidFuel = target * mixtureCapacity;
            part.Oxidizer = target * (1.0 - mixtureCapacity);
        }

        SetWarpIndex(0);
        MissionManager.Instance?.EnterPhase(MissionPhase.ORBIT);
        CameraController.Instance?.EnterShipChaseView();
        GD.Print("[DEMO] Starship reentry → landing started at 70 km / 1.80 km/s");
        return true;
    }

    /// DEBUG: drop the active vessel straight into a circular orbit (~200 km) around Earth,
    /// to test orbital features (transfer planner, etc.) without flying the whole ascent.
    public void JumpToOrbit(double altitude = 200_000.0)
    {
        var earth = Universe.GetBody("earth");
        var v = ActiveVessel;
        if (earth == null || v == null) return;

        v.IsGroundHeld = false;
        var up = (v.Position - earth.Position).Normalized;
        if (up.MagnitudeSquared < 1e-9) up = new Vector3d(0, 1, 0);
        double r = earth.Radius + altitude;
        v.Position = earth.Position + up * r;

        var refDir  = System.Math.Abs(up.Dot(new Vector3d(0, 1, 0))) < 0.9 ? new Vector3d(0, 1, 0) : new Vector3d(1, 0, 0);
        var tangent = refDir.Cross(up).Normalized;
        double vCirc = System.Math.Sqrt(earth.GM / r);
        v.Velocity = earth.Velocity + tangent * vCirc;
        v.Throttle = 0.0;

        MissionManager.Instance?.EnterPhase(MissionPhase.ORBIT);
        GD.Print($"[DEBUG] JumpToOrbit -> {altitude / 1000:F0} km circular, v={vCirc:F0} m/s");
    }

    /// DEBUG: jump to a ~300 km circular orbit around an arbitrary body (e.g. the transfer
    /// target), to preview arrival/EDL without flying the whole cruise.
    public void JumpToBody(string bodyId, double altitude = 300_000.0)
    {
        var body = Universe.GetBody(bodyId);
        var v = ActiveVessel;
        if (body == null || v == null) return;

        v.IsGroundHeld = false;

        // Approach direction + distance: ringed bodies (Saturn) are viewed from OUTSIDE the
        // ring system (rings reach ~2.3 R) at a 3/4 angle so the rings read as an open ellipse;
        // other bodies are viewed from a sensible fraction of their radius.
        Vector3d up;
        double r;
        if (bodyId == "saturn")
        {
            up = new Vector3d(0.45, 0.65, 0.5).Normalized;
            r  = body.Radius * 5.0;
        }
        else
        {
            up = new Vector3d(1, 0, 0);
            r  = body.Radius + System.Math.Max(altitude, body.Radius * 0.6);
        }
        v.Position = body.Position + up * r;
        var tangent = new Vector3d(0, 1, 0).Cross(up).Normalized;
        double vCirc = System.Math.Sqrt(body.GM / r);
        v.Velocity = body.Velocity + tangent * vCirc;
        v.Throttle = 0.0;
        GD.Print($"[DEBUG] JumpToBody {bodyId} -> orbit {altitude / 1000:F0} km");
    }
}
