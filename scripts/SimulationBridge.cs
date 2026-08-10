namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Exosphere.Simulation.Propulsion;

[GlobalClass]
public partial class SimulationBridge : Node
{
    public static SimulationBridge Instance { get; private set; } = null!;

    public Universe Universe { get; private set; } = null!;
    public Vessel?  ActiveVessel => Universe.ActiveVessel;
    public string ActiveFlightProfileId { get; private set; } = "manual";
    public void SetActiveFlightProfile(string profileId)
    {
        if (!string.IsNullOrWhiteSpace(profileId))
            ActiveFlightProfileId = profileId;
    }

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

    /// <summary>Active launch site, if the configured id resolved from data.</summary>
    public LaunchSite? LaunchSiteOrNull => _launchSite;

    // ── Ignition ramp state ───────────────────────────────────────────────
    // True while Ignite() is spooling up and waiting for the commit-to-launch gate.
    private bool   _ignitionActive  = false;
    // Throttle rate used during the ignition ramp (throttle units per second).
    private const double IgnitionRampRate = 0.5;

    // The re-entry demonstration is a watchable mission, not a propellant-starvation
    // challenge. Keep a realistic landing reserve in the ship tank so the centre Raptor
    // remains lit through the multi-leg contact transient and the normal settling gate.
    // The flight model still consumes the exact pressure/throttle-dependent mass flow.
    private const double ReentryDemoReserveFraction = 0.20;

    public override void _Ready()
    {
        Instance = this;

        var dataPath = ProjectSettings.GlobalizePath(DataDirectory);
        Universe = Universe.LoadFromDataDirectory(dataPath);
        Universe.TimeScale = 1.0;
        _running = true;

        var pendingIntent = CraftLaunchRequest.Pop();
        if (pendingIntent != null)
        {
            LaunchSiteId = pendingIntent.LaunchSiteId;
            ActiveFlightProfileId = pendingIntent.FlightProfileId;
        }
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

            var boosterReturn = new BoosterReturnController { Name = "BoosterReturnController" };
            uiLayer.CallDeferred("add_child", boosterReturn);

            var historical = new HistoricalFlightProfileController
            {
                Name = "HistoricalFlightProfileController",
            };
            uiLayer.CallDeferred("add_child", historical);

            var warpCtrl = new WarpController { Name = "WarpController" };
            uiLayer.CallDeferred("add_child", warpCtrl);

            var systemsCtrl = new SystemsController { Name = "SystemsController" };
            uiLayer.CallDeferred("add_child", systemsCtrl);
        }

        // Create LaunchPadController in the World node
        var worldNode = GetTree().Root.FindChild("World", true, false) as Node3D;
        if (worldNode != null)
        {
            _launchPad = new LaunchPadController { LaunchSiteId = LaunchSiteId };
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

        bool needsDefaultStack = pendingIntent == null
            || pendingIntent.Craft == null
                && string.IsNullOrWhiteSpace(pendingIntent.SaveSlot);
        if (needsDefaultStack)
            SpawnStarshipStack(dataPath);
        SpawnPendingConstructedVessel(dataPath, pendingIntent);
        var campaignRuntime = new CampaignRuntime
        {
            Name = "CampaignRuntime",
        };
        AddChild(campaignRuntime);
        bool continuingSave =
            !string.IsNullOrWhiteSpace(pendingIntent?.SaveSlot);
        campaignRuntime.Initialize(
            dataPath,
            pendingIntent?.MissionId
                ?? (continuingSave
                    ? SaveSystem.LastLoadedMetadata?.Mission.MissionId
                    : null),
            pendingIntent?.CampaignState
                ?? SaveSystem.LastLoadedMetadata?.Campaign,
            continuingSave ? SaveSystem.LastLoadedMetadata?.Mission : null);
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
            var missionPhase = MissionManager.Instance?.Phase;
            // Real-time only through tower clear — atmospheric ×3 warp made pad liftoff
            // feel like a snap even with correct TWR.
            if (missionPhase is MissionPhase.COUNTDOWN
                or MissionPhase.IGNITION
                or MissionPhase.LIFTOFF)
            {
                MaxAllowedWarpIndex = 0;
            }
            else if (av.Throttle > 0.01)
            {
                // Warp IS allowed while thrusting now — the active vessel stays on RK4 with a
                // bounded sub-step (Universe.MaxThrustStep) so the burn is physics-faithful.
                // Cap it: x3 in atmosphere, x10 in vacuum (never on-rails while powered).
                MaxAllowedWarpIndex = atmosphericZone ? 2 : 4; // x3 atmosphere, x10 vacuum
            }
            else
            {
                bool historicalOrbitalCoast =
                    ActiveFlightProfileId is
                        MercuryAtlasFlightProfile.Id
                        or Gemini8FlightProfile.Id
                    && missionPhase is MissionPhase.ORBIT
                        or MissionPhase.COAST
                    && av.GetAltitude(refB) > 120_000.0;
                MaxAllowedWarpIndex = forceSensitive
                    ? historicalOrbitalCoast ? 6 : 2
                    : boundedEntry ? 7                         // x1000 bounded coast to entry
                    : WarpLevels.Length - 1;
            }
            // Clamp current warp index if it now exceeds the allowed maximum
            if (WarpIndex > MaxAllowedWarpIndex)
                SetWarpIndex(MaxAllowedWarpIndex);
        }

        Universe.Tick(delta);
        SyncStructuralDebrisRenderers();

        // Hot-stage overlap finished in sim time → mechanical separation this frame.
        if (av != null && av.HotStageOverlapCompletedPending)
        {
            av.HotStageOverlapCompletedPending = false;
            TriggerStaging();
            av = ActiveVessel;
        }

        // ── Ignition ramp: spool throttle; release only at commit-to-launch ──────
        if (_ignitionActive && av != null)
        {
            // Avanzar el throttle comandado hacia 1.0 a una tasa controlada
            av.Throttle = System.Math.Min(av.Throttle + IgnitionRampRate * delta, 1.0);

            if (av.IsGroundHeld)
            {
                var refB2 = Universe.GetDominantBody(av.Position);
                if (refB2 != null && av.TotalMass > 0.0)
                {
                    double twr = av.GetThrustToWeightRatio(refB2);
                    if (HoldDownReleasePolicy.CanRelease(twr, av.Throttle))
                    {
                        av.ReleaseGroundHold();
                        // PRE_LAUNCH → LIFTOFF (manual [Z]); IGNITION → LIFTOFF ([L]/[G]).
                        // BeginFlight alone left countdown/ignition missions stuck in IGNITION
                        // after a WASD soft-disengage, which also kept MISSION CONTROLS up.
                        MissionManager.Instance?.BeginFlight();
                        MissionManager.Instance?.NotifyHoldDownReleased();
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

            // While a catch approach is armed, refresh the sim-side cradle target every
            // frame from the same site/spec data the render tower uses — the tower is
            // bolted to a rotating body, so a target computed once at arm time would drift
            // away under it exactly like the render pad would without this same refresh.
            // R12: refresh EVERY vessel attempting a catch (Ship or returning booster),
            // not only ActiveVessel — the booster return flies while Ship stays active.
            if (_launchSite != null)
            {
                var cradle = LaunchComplexSpec.StarbasePostDeluge.GetCatchCradlePosition(
                    _launchSite, padEarth, time);
                var cradleVel = padEarth.Velocity + padEarth.GetSurfaceVelocity(cradle);
                foreach (var vessel in Universe.Vessels)
                {
                    if (!vessel.IsAttemptingTowerCatch) continue;
                    vessel.CatchTargetPositionWorld = cradle;
                    vessel.CatchTargetUpWorld = up;
                    vessel.CatchTargetVelocityWorld = cradleVel;
                }
            }
        }
    }

    /// <summary>
    /// Arms a return-to-launch-site tower catch for the active ship. No-ops (and returns
    /// false) if the vessel does not carry catch-pin hardpoints — most vehicles, and every
    /// non-V3 Starship, always fall through to the normal leg landing untouched.
    /// </summary>
    public bool ArmTowerCatchApproach() => ArmTowerCatchApproach(ActiveVessel);

    /// <summary>
    /// Arms a tower catch for an explicit vessel (R12 booster return while Ship remains
    /// the active/piloted vessel).
    /// </summary>
    public bool ArmTowerCatchApproach(Vessel? vessel)
    {
        if (vessel == null || !vessel.HasCatchPins) return false;
        vessel.IsAttemptingTowerCatch = true;
        return true;
    }

    private void SpawnPendingConstructedVessel(
        string dataPath,
        LaunchIntent? intent)
    {
        if (intent == null) return;

        if (!string.IsNullOrWhiteSpace(intent.SaveSlot))
        {
            if (!SaveSystem.LoadGame(intent.SaveSlot))
                throw new InvalidDataException($"Could not continue save '{intent.SaveSlot}'.");
            RebuildActiveVesselRenderer();
            return;
        }

        var craft = intent.Craft;
        if (craft == null)
        {
            if (intent.FlightProfileId == "starship-reentry-70km")
                BeginReentryDemonstration();
            return;
        }

        var catalog = PartCatalog.LoadFromDirectory(System.IO.Path.Combine(dataPath, "parts"));
        var sites = LaunchSite.LoadAllFromDirectory(System.IO.Path.Combine(dataPath, "launch_sites"));
        if (!sites.TryGetValue(intent.LaunchSiteId, out _launchSite))
            throw new InvalidOperationException(
                $"Launch intent references unknown site '{intent.LaunchSiteId}'.");
        LaunchSiteId = intent.LaunchSiteId;
        var assembly = VesselAssembly.FromCraft(catalog, craft);
        PlaceConstructedVesselOnPad(assembly.ToVessel(craft.Name));
        GD.Print(
            $"[VAB] Placed {craft.Name} at {intent.LaunchSiteId}; " +
            $"profile={intent.FlightProfileId}, mission={intent.MissionId ?? "sandbox"}");
        if (intent.FlightProfileId == "starship-reentry-70km")
            BeginReentryDemonstration();
    }

    // ── Starship + Super Heavy stack on Starbase launchpad ────────────────

    private void SpawnStarshipStack(string dataPath)
    {
        var earth = Universe.GetBody("earth");
        if (earth == null) return;

        var defs = PartCatalog.LoadFromDirectory(
            System.IO.Path.Combine(dataPath, "parts")).Parts;

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
        vessel.ConfigureCatchContactsFromParts();

        // Stand the stack on the real launch site. The hull's +Y is rotated onto the local
        // vertical there, so the rocket is upright at the pad's true latitude instead of
        // being planted on an arbitrary axis. The stack bottom is placed at the shared
        // engineering spec's vehicle-interface elevation above civil grade.
        double mountHeightM = LaunchComplexSpec.StarbasePostDeluge.VehicleInterfaceElevation;
        StandOnPad(vessel, earth, mountHeightM);

        Universe.AddVessel(vessel);
        Universe.SetActiveVessel(vessel.Id);

        EnsureActiveVesselPresentation(vessel);
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

    public bool InjectActiveEngineFailure(
        int ordinal = 0,
        string failureCode = "INJECTED_ENGINE_OUT")
    {
        var vessel = ActiveVessel;
        if (vessel == null) return false;
        var candidates = vessel.Parts.ActiveEngines
            .SelectMany(part => part.EngineStates)
            .Where(engine => engine.FailureCode == null)
            .OrderBy(engine => engine.InstanceId, StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0) return false;
        int index = System.Math.Clamp(ordinal, 0, candidates.Length - 1);
        bool injected = vessel.InjectEngineFailure(
            candidates[index].InstanceId, failureCode);
        if (injected)
            GD.Print(
                $"[PROPULSION] Engine out injected: {candidates[index].InstanceId} " +
                $"({failureCode})");
        return injected;
    }

    public bool ScheduleActiveEngineFailure(
        EngineLifecycleState triggerState,
        double triggerAfterStateSeconds,
        int ordinal = 0,
        int triggerStartAttempt = 0,
        string failureCode = "SCHEDULED_ENGINE_FAILURE")
    {
        var vessel = ActiveVessel;
        if (vessel == null) return false;
        var candidates = vessel.Parts.ActiveEngines
            .SelectMany(part => part.EngineStates.Select(state => (part, state)))
            .Where(candidate => candidate.state.FailureCode == null)
            .OrderBy(
                candidate => candidate.state.InstanceId,
                StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0) return false;
        int index = System.Math.Clamp(ordinal, 0, candidates.Length - 1);
        var selected = candidates[index];
        selected.part.ScheduleEngineFailure(new EngineFailureInjection
        {
            EngineInstanceId = selected.state.InstanceId,
            TriggerState = triggerState,
            TriggerStartAttempt = triggerStartAttempt,
            TriggerAfterStateSeconds = triggerAfterStateSeconds,
            FailureCode = failureCode,
        });
        GD.Print(
            $"[PROPULSION] Scheduled {failureCode} for " +
            $"{selected.state.InstanceId} in {triggerState}.");
        return true;
    }

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
    /// Rebuilds the active vessel mesh after a mission load (Id and part graph may be new).
    /// </summary>
    public void RebuildActiveVesselRenderer()
    {
        var vessel = ActiveVessel;
        if (vessel == null) return;
        EnsureActiveVesselPresentation(vessel);
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
            mountHeightM = _launchPad?.VehicleInterfaceElevationM
                ?? LaunchComplexSpec.StarbasePostDeluge.VehicleInterfaceElevation;
        StandOnPad(vessel, earth, mountHeightM);
        vessel.ConfigureLandingContactsFromParts();
        vessel.ConfigureCatchContactsFromParts();

        Universe.AddVessel(vessel);
        Universe.SetActiveVessel(vessel.Id);

        EnsureActiveVesselPresentation(vessel);
    }

    private void EnsureActiveVesselPresentation(Vessel vessel)
    {
        var root = GetTree().Root;
        var vesselsNode = root.FindChild("Vessels", true, false) as Node3D;
        if (vesselsNode == null) return;

        if (_vesselRenderer == null)
        {
            _vesselRenderer = new VesselRenderer { Name = "ActiveVesselRenderer" };
            vesselsNode.AddChild(_vesselRenderer);
        }
        _vesselRenderer.BuildFromVessel(vessel);

        // These consumers follow the active vessel and are independent of vehicle identity.
        if (root.FindChild("CockpitRenderer", true, false) == null)
            vesselsNode.AddChild(new CockpitRenderer { Name = "CockpitRenderer" });
        if (root.FindChild("CockpitInstruments", true, false) == null)
            AddChild(new CockpitInstruments { Name = "CockpitInstruments" });
        if (root.FindChild("MaxQRing", true, false) == null)
            vesselsNode.AddChild(new MaxQRingController { Name = "MaxQRing" });
        if (root.FindChild("ReentryPlasma", true, false) == null)
            vesselsNode.AddChild(
                new ReentryPlasmaController { Name = "ReentryPlasma" });

        var floating = root.FindChild("FloatingOrigin", true, false) as FloatingOrigin;
        floating?.RegisterVesselNode(vessel.Id, _vesselRenderer);
    }

    // ── Ignition / throttle contracts (consumed by HUDController / Agente E) ─

    /// <summary>
    /// True while Ignite() is ramping up thrust and waiting for the commit-to-launch
    /// gate (TWR &gt; 1.05 and throttle ≥ 0.95). Resets once the vessel lifts off and
    /// throttle reaches 1.0.
    /// </summary>
    public bool IsIgnitionActive => _ignitionActive;

    /// <summary>
    /// Secuencia de ignición: arranca la rampa de throttle comandado hacia 1.0 y suelta
    /// los hold-downs automáticamente al commit-to-launch
    /// (<see cref="HoldDownReleasePolicy"/>).
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

        Universe.AddVessel(debris);

        // Rebuild active vessel renderer: SH is now gone → shows standalone Starship
        _vesselRenderer?.BuildFromVessel(ActiveVessel);
        SpawnDebrisRenderer(debris, "SHDebris_");

        EmitSignal(SignalName.VesselStaged, debris.Id);
        MissionManager.Instance?.NotifyStaged();
    }

    /// <summary>
    /// Detaches an arbitrary part subtree as another persisted vessel while keeping the
    /// surviving carrier active. Used by historical escape-tower and spacecraft events.
    /// </summary>
    public Vessel? DeployPartAsVessel(
        string rootPartInstanceId,
        string name,
        Vector3d separationVelocityLocal)
    {
        var carrier = ActiveVessel;
        if (carrier == null) return null;
        var detached = carrier.DeployPayload(
            rootPartInstanceId,
            name,
            separationVelocityLocal);
        if (detached == null) return null;
        Universe.AddVessel(detached);
        _vesselRenderer?.BuildFromVessel(carrier);
        SpawnDebrisRenderer(detached, "Separated_");
        EmitSignal(SignalName.VesselStaged, detached.Id);
        return detached;
    }

    /// <summary>
    /// Creates the already-orbiting Agena 5003 target for the Gemini VIII campaign.
    /// Its stable identity and ordinary PartGraph make it saveable, targetable and
    /// dockable through the same multi-vessel path as player-deployed payloads.
    /// </summary>
    public Vessel? EnsureGemini8AgenaTarget()
    {
        const string targetId = "gemini8-agena-5003";
        var existing = Universe.Vessels.FirstOrDefault(v => v.Id == targetId);
        if (existing != null) return existing;

        var earth = Universe.GetBody("earth");
        var carrier = ActiveVessel;
        if (earth == null || carrier == null) return null;
        string dataPath = ProjectSettings.GlobalizePath(DataDirectory);
        var catalog = PartCatalog.LoadFromDirectory(
            System.IO.Path.Combine(dataPath, "parts"));
        var variant = VehicleVariantDefinition.LoadFromJson(
            System.IO.Path.Combine(
                dataPath, "vehicles", "agena8_target_5003_1966.json"));
        var target = variant.Build(catalog).ToVessel(
            "Gemini Agena Target Vehicle 5003", targetId);

        Vector3d up = (carrier.Position - earth.Position).Normalized;
        if (up.MagnitudeSquared < 1e-9) up = Vector3d.Right;
        Vector3d tangent = GetLaunchHeadingDirection();
        tangent = (tangent - up * tangent.Dot(up)).Normalized;
        if (tangent.MagnitudeSquared < 1e-9)
            tangent = earth.RotationAxis.Cross(up).Normalized;
        double radius = earth.Radius
            + Gemini8FlightProfile.HistoricalTargetOrbitM;
        target.Position = earth.Position + up * radius;
        target.Velocity = earth.Velocity
            + tangent * System.Math.Sqrt(earth.GM / radius);
        target.Orientation = Quaterniond.FromTo(Vector3d.Up, tangent);
        target.ReferenceBodyId = earth.Id;
        target.IsOnRails = true;
        target.OrbitalState = OrbitalElements.FromStateVector(
            target.Position - earth.Position,
            target.Velocity - earth.Velocity,
            earth.GM,
            earth.Id,
            Universe.CurrentTime);
        target.SASEnabled = true;
        Universe.AddVessel(target);
        SpawnDebrisRenderer(target, "Target_");
        GD.Print("[HISTORICAL] Agena 5003 acquired in its 161.3 nmi target orbit.");
        return target;
    }

    /// <summary>
    /// After CSM/S-IVB separation, extract LM-5 Eagle from the opaque SLA envelope,
    /// carve its wet mass out of the SLA dry mass, and spawn Eagle as a dockable vessel.
    /// </summary>
    public Vessel? EnsureApollo11EagleExtracted()
    {
        const string eagleId = Exosphere.Simulation.Flight.Apollo11FlightProfile.EagleVesselId;
        var existing = Universe.Vessels.FirstOrDefault(v => v.Id == eagleId);
        if (existing != null) return existing;

        var slaHost = Universe.Vessels.FirstOrDefault(v =>
            v.Parts.Parts.Any(part =>
                part.Definition.HasVehicleRole("sla_lunar_module")));
        if (slaHost == null) return null;

        var sla = slaHost.Parts.Parts.First(part =>
            part.Definition.HasVehicleRole("sla_lunar_module"));
        sla.MassDryOffset =
            Exosphere.Simulation.Flight.Apollo11FlightProfile.EmptySlaDryMassKg
            - sla.Definition.MassDry;

        string dataPath = ProjectSettings.GlobalizePath(DataDirectory);
        var catalog = PartCatalog.LoadFromDirectory(
            System.IO.Path.Combine(dataPath, "parts"));
        var variant = VehicleVariantDefinition.LoadFromJson(
            System.IO.Path.Combine(
                dataPath, "vehicles", "apollo11_lm5_eagle_1969.json"));
        var eagle = variant.Build(catalog).ToVessel("LM-5 Eagle", eagleId);

        // Place Eagle just ahead of the SLA nose along the host +Y thrust/stack axis.
        Vector3d forward = slaHost.Orientation.Rotate(Vector3d.Up).Normalized;
        eagle.Position = slaHost.Position + forward * 12.0;
        eagle.Velocity = slaHost.Velocity;
        eagle.Orientation = slaHost.Orientation;
        eagle.AngularVelocity = Vector3d.Zero;
        eagle.ReferenceBodyId = slaHost.ReferenceBodyId;
        eagle.IsOnRails = false;
        eagle.OrbitalState = null;
        eagle.SASEnabled = true;
        eagle.Throttle = 0.0;
        Universe.AddVessel(eagle);
        SpawnDebrisRenderer(eagle, "Eagle_");
        GD.Print(
            $"[HISTORICAL] LM-5 Eagle extracted from SLA "
            + $"(SLA shell {Exosphere.Simulation.Flight.Apollo11FlightProfile.EmptySlaDryMassKg:F0} kg).");
        return eagle;
    }

    public Vector3d GetLaunchHeadingDirection()
    {
        var earth = Universe.GetBody("earth");
        if (earth == null || ActiveVessel == null) return Vector3d.Forward;
        var frame = _launchSite?.GetLocalFrame(earth, Universe.CurrentTime);
        Vector3d east = frame?.East ?? earth.GetEastDirection(ActiveVessel.Position);
        Vector3d north = frame?.North
            ?? (ActiveVessel.Position - earth.Position)
                .Normalized.Cross(east).Normalized;
        double heading = (_launchSite?.Heading ?? 90.0) * MathUtils.DEG_TO_RAD;
        return (north * System.Math.Cos(heading)
            + east * System.Math.Sin(heading)).Normalized;
    }

    /// <summary>
    /// After each sim tick, spawn renderers for vessels created by structural breakup
    /// (overload joints). Staging debris is registered in <see cref="TriggerStaging"/>
    /// and is not listed in the structural pending drain.
    /// </summary>
    private void SyncStructuralDebrisRenderers()
    {
        var pending = Universe.DrainPendingStructuralDebris();
        if (pending.Count == 0) return;

        foreach (var debris in pending)
            SpawnDebrisRenderer(debris, "StructuralDebris_");

        // Parent stack lost parts — rebuild so meshes match the remaining graph.
        if (ActiveVessel != null)
            _vesselRenderer?.BuildFromVessel(ActiveVessel);

        foreach (var debris in pending)
            EmitSignal(SignalName.VesselStaged, debris.Id);

        if (ActiveVessel != null
            && ActiveVessel.IsDestroyed
            && ActiveVessel.DestructionCause == VesselDestructionCause.StructuralBreakup)
        {
            EmitSignal(SignalName.VesselDestroyed, ActiveVessel.Id);
        }
    }

    private void SpawnDebrisRenderer(Vessel debris, string namePrefix)
    {
        var fo          = GetTree().Root.FindChild("FloatingOrigin", true, false) as FloatingOrigin;
        var vesselsNode = GetTree().Root.FindChild("Vessels",        true, false) as Node3D;
        if (vesselsNode == null) return;

        var debrisRenderer = new VesselRenderer();
        debrisRenderer.Name = namePrefix + debris.Id[..8];
        vesselsNode.AddChild(debrisRenderer);
        debrisRenderer.BuildFromVessel(debris);
        fo?.RegisterVesselNode(debris.Id, debrisRenderer);
    }

    public void SetTimeScale(double scale) => Universe.TimeScale = scale;

    /// <summary>
    /// Starts the verified Starship entry-to-landing demonstration used by the HUD and the
    /// visual regression harness.  This is a real physics state: EDL owns attitude/throttle,
    /// atmospheric drag and heating run normally, Raptors relight for the powered flip, and
    /// landing contact is solved by the regular surface-contact model.
    /// </summary>
    /// <param name="bellyFirst">
    /// True (default) seeds the protected heat-shield-forward belly-flop attitude used by the
    /// HUD button. False seeds a deliberately wrong nose-first attitude with a tumble rate,
    /// solely so the visual capture harness (tools/visual_playtest.sh --reentry-compare) can
    /// show the VFX/thermal difference against a bad attitude. EDL guidance itself is
    /// untouched — this only changes the initial state handed to it.
    /// </param>
    public bool BeginReentryDemonstration(bool bellyFirst = true)
    {
        var earth = Universe.GetBody("earth");
        var vessel = ActiveVessel;
        if (earth == null || vessel == null || vessel.IsDestroyed) return false;

        // The launch scene begins as a full stack. Separate first so EDL controls the Ship
        // engine cluster and aerodynamic body rather than an impossible attached booster.
        if (vessel.Parts.Parts.Any(
                p => p.Definition.IsStarshipFamily
                    && p.Definition.HasVehicleRole("booster")))
        {
            TriggerStaging();
            vessel = ActiveVessel;
        }
        if (vessel == null || !vessel.Parts.Parts.Any(
                p => p.Definition.IsStarshipFamily
                    && p.Definition.HasVehicleRole("ship_engines")))
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
        if (bellyFirst)
        {
            vessel.Orientation = AerodynamicsModel.ComputeBellyFirstOrientation(
                longAxis, velocityDirection);
            vessel.AngularVelocity = Vector3d.Zero;
        }
        else
        {
            // Deliberately wrong entry attitude for capture comparison only: broadside to
            // the airflow (nose pointed radially outward, perpendicular to velocity) instead
            // of the protected belly-flop. Left at zero initial spin — imposing an explicit
            // tumble rate here previously destabilized the linear velocity state that
            // EDLController's activation check depends on (_vUp < -20) in a non-deterministic
            // way; a fixed wrong orientation is deterministic, and any tumble should emerge
            // physically from aerodynamic instability once EDLController/aero take over.
            // Capture-only — does not touch EDLController/AscentController guidance.
            vessel.Orientation = Quaterniond.FromTo(Vector3d.Up, up);
            vessel.AngularVelocity = Vector3d.Zero;
        }
        vessel.PitchYawRoll = Vector3d.Zero;
        vessel.SASEnabled = false;
        vessel.IsGroundHeld = false;
        vessel.IsOnRails = false;
        vessel.OrbitalState = null;
        vessel.ReferenceBodyId = earth.Id;
        vessel.Throttle = 0.0;
        vessel.ConfigureLandingContactsFromParts();
        vessel.ConfigureCatchContactsFromParts();

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
            double target = totalCapacity * ReentryDemoReserveFraction;
            double mixtureCapacity = totalCapacity > 1e-9
                ? part.Definition.FuelCapacityLF / totalCapacity
                : 0.45;
            part.LiquidFuel = target * mixtureCapacity;
            part.Oxidizer = target * (1.0 - mixtureCapacity);
        }

        SetWarpIndex(0);
        MissionManager.Instance?.EnterPhase(MissionPhase.ORBIT);
        CameraController.Instance?.EnterShipChaseView();
        GD.Print($"[DEMO] Starship reentry → landing started at 70 km / 1.80 km/s " +
            $"(bellyFirst={bellyFirst})");
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

    /// <summary>
    /// Map-facing helper: plan a retrograde deorbit burn on the orbital-map planner that
    /// lowers periapsis into the atmosphere (default Pe altitude 80 km). Does not execute
    /// the burn — arm with Enter on the map. Leaves <see cref="BeginReentryDemonstration"/>
    /// alone (that remains a teleport demo).
    /// </summary>
    public bool PlanDeorbitForActiveVessel(double targetPeAltitudeM = 80_000.0)
    {
        var map = MapViewController.Instance;
        var vessel = ActiveVessel;
        var earth = Universe.GetBody("earth");
        if (map == null || vessel == null || earth == null || vessel.IsDestroyed)
            return false;

        var relPos = vessel.Position - earth.Position;
        var relVel = vessel.Velocity - earth.Velocity;
        map.Planner.SetOrbit(relPos, relVel, earth.GM);
        if (!map.Planner.PlanDeorbit(earth, targetPeAltitudeM))
            return false;

        GD.Print($"[Bridge] Deorbit planned: Δv={map.Planner.DeltaVMagnitude:F1} m/s " +
                 $"(Pe target {targetPeAltitudeM / 1000.0:F0} km)");
        return true;
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
