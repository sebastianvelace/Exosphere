namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

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
    public static readonly double[] WarpLevels = { 1, 2, 3, 5, 10, 50, 100, 1000 };
    public int WarpIndex          { get; private set; } = 0;
    public int MaxAllowedWarpIndex { get; private set; } = 7;

    public void SetWarpIndex(int i)
    {
        i = System.Math.Clamp(i, 0, MaxAllowedWarpIndex);
        WarpIndex = i;
        SetTimeScale(WarpLevels[WarpIndex]);
    }

    private bool                 _running        = false;
    private VesselRenderer?      _vesselRenderer = null;
    private Camera3D?            _camera         = null;
    private LaunchPadController? _launchPad      = null;
    private Vector3d             _padWorldPos;

    public override void _Ready()
    {
        Instance = this;

        var dataPath = ProjectSettings.GlobalizePath(DataDirectory);
        Universe = Universe.LoadFromDataDirectory(dataPath);
        Universe.TimeScale = 1.0;
        _running = true;

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

            var starfield = new StarfieldController { Name = "StarfieldController" };
            worldNode.CallDeferred("add_child", starfield);

            // Liftoff steam/dust deluge cloud at the pad.
            var launchFx = new LaunchEffectsController { Name = "LaunchEffectsController" };
            worldNode.CallDeferred("add_child", launchFx);
        }

        SpawnStarshipStack(dataPath);
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
            if (av.Throttle > 0.01)
            {
                MaxAllowedWarpIndex = 0; // no warp while thrusting
            }
            else
            {
                var refB = Universe.GetDominantBody(av.Position);
                double atmDensity = refB.GetAtmosphericDensity(av.Position);
                if (atmDensity > 0.01)
                    MaxAllowedWarpIndex = 2; // max x3 in atmosphere
                else
                    MaxAllowedWarpIndex = WarpLevels.Length - 1; // max in vacuum/orbit
            }
            // Clamp current warp index if it now exceeds the allowed maximum
            if (WarpIndex > MaxAllowedWarpIndex)
                SetWarpIndex(MaxAllowedWarpIndex);
        }

        Universe.Tick(delta);

        // Anchor the LaunchPad to the Earth surface point directly BELOW the vessel,
        // recomputed each frame (Earth orbits the Sun, so a fixed spawn-time world point
        // drifts away). Convert the metres offset to render units (1 unit ≈ 2.8 m).
        var padEarth = Universe.GetBody("earth");
        if (_launchPad != null && ActiveVessel != null && padEarth != null)
        {
            const float metresPerUnit = 2.8f;
            double alt = ActiveVessel.GetAltitude(padEarth);
            var up = (ActiveVessel.Position - padEarth.Position).Normalized;
            var surfacePos = padEarth.Position + up * padEarth.Radius;
            var offset = surfacePos - ActiveVessel.Position;          // = -up·alt metres
            _launchPad.Position = new Godot.Vector3(
                (float)(offset.X / metresPerUnit),
                (float)(offset.Y / metresPerUnit),
                (float)(offset.Z / metresPerUnit));
            _launchPad.Visible = alt < 8_000;   // hide above 8 km
        }
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
        if (!defs.TryGetValue("decoupler_medium",   out var decDef))  return;
        if (!defs.TryGetValue("super_heavy_booster",out var shDef))   return;

        var vessel = new Vessel { Name = "Starship IFT-7" };

        var command  = new Part(cmdDef);
        var tank     = new Part(tankDef);
        var engines  = new Part(engDef);
        var decoupler= new Part(decDef);
        var sh       = new Part(shDef);

        vessel.Parts.SetRoot(command);
        vessel.Parts.AddPart(command);
        vessel.Parts.AddPart(tank);
        vessel.Parts.AddPart(engines);
        vessel.Parts.AddPart(decoupler);
        vessel.Parts.AddPart(sh);

        // Stack (top → bottom): command → tank → engines → decoupler → super_heavy
        vessel.Parts.AddJoint(new Joint(command,   tank,      "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(tank,      engines,   "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(engines,   decoupler, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(decoupler, sh,        "bottom", "top"));

        // Spawn at +Y so the stack stands vertical in the render frame. The Earth backdrop
        // is rotated (see FloatingOrigin) so the blue/green equator — not the polar ice —
        // faces the launch site, keeping the planet realistic without tilting the rocket.
        // Visual model has stack bottom at y=0, so spawn altitude = 0 → launch mount height.
        const double mountHeightM = 12.0;  // OLM height in metres
        var upDir = new Vector3d(0, 1, 0); // +Y from Earth centre

        vessel.Position = earth.Position + upDir * (earth.Radius + mountHeightM);
        vessel.Velocity = earth.Velocity + earth.GetSurfaceVelocity(vessel.Position);
        vessel.Orientation = Quaterniond.Identity;  // +Y up = radial up at spawn point
        vessel.SASEnabled = true;

        // Ground hold: keeps vessel locked to surface until T-0
        vessel.IsGroundHeld  = true;
        vessel.GroundNormal  = upDir;
        vessel.GroundOffset  = mountHeightM;

        // Save pad surface position for LaunchPad visual anchoring
        _padWorldPos = earth.Position + upDir * earth.Radius;

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

            // MaxQ condensation ring — tracks active vessel (always at render origin)
            var maxQ = new MaxQRingController { Name = "MaxQRing" };
            vesselsNode.AddChild(maxQ);

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
        }
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

    // ── Public API ────────────────────────────────────────────────────────

    public void SetThrottle(double t) { if (ActiveVessel != null) ActiveVessel.Throttle = t; }
    public void SetSAS(bool on)       { if (ActiveVessel != null) ActiveVessel.SASEnabled = on; }
    public void ReleaseGroundHold()   { ActiveVessel?.ReleaseGroundHold(); }

    public void TriggerStaging()
    {
        if (ActiveVessel == null) return;
        var debris = ActiveVessel.Stage();
        if (debris == null) return;

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
}
