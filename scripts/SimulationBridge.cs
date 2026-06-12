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

    private bool                 _running          = false;
    private VesselRenderer?      _vesselRenderer   = null;
    private Camera3D?            _camera           = null;
    private LaunchPadController? _launchPad        = null;
    private Vector3d             _padWorldPos;   // Earth surface point directly below spawn

    public override void _Ready()
    {
        Instance = this;

        var dataPath = ProjectSettings.GlobalizePath(DataDirectory);
        Universe = Universe.LoadFromDataDirectory(dataPath);
        Universe.TimeScale = 1.0;
        _running = true;

        // Create MissionManager as sibling — deferred: parent (Flight) is busy in _Ready()
        var mm = new MissionManager();
        mm.Name = "MissionManager";
        GetParent()?.CallDeferred("add_child", mm);

        // Create LaunchPadController in the World node
        var worldNode = GetTree().Root.FindChild("World", true, false) as Node3D;
        if (worldNode != null)
        {
            _launchPad = new LaunchPadController();
            _launchPad.Name = "LaunchPadController";
            worldNode.CallDeferred("add_child", _launchPad);
        }

        SpawnStarshipStack(dataPath);
        SpawnPlanets();
        EmitSignal(SignalName.SimulationLoaded);

        _camera = GetTree().Root.FindChild("Camera3D", true, false) as Camera3D;
        if (_camera != null)
            _camera.Far = 2_000_000.0f;

        var light = GetTree().Root.FindChild("DirectionalLight3D", true, false) as DirectionalLight3D;
        if (light != null)
            light.RotationDegrees = new Godot.Vector3(-45f, -30f, 0f);
    }

    public override void _Process(double delta)
    {
        if (!_running || Universe == null) return;
        Universe.Tick(delta);

        // Update LaunchPad position: anchored to Earth surface, offset from active vessel
        if (_launchPad != null && ActiveVessel != null && _padWorldPos != Vector3d.Zero)
        {
            var offset = _padWorldPos - ActiveVessel.Position;
            _launchPad.Position = new Godot.Vector3((float)offset.X, (float)offset.Y, (float)offset.Z);
            var earth = Universe.GetBody("earth");
            double alt = earth != null ? ActiveVessel.GetAltitude(earth) : 1e6;
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

        // Place on Earth surface at +Y from Earth centre (north pole, simple & clean)
        // Visual model has stack bottom at y=0, so spawn altitude = 0 → launch mount height
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

            var fo = GetTree().Root.FindChild("FloatingOrigin", true, false) as FloatingOrigin;
            fo?.RegisterVesselNode(vessel.Id, _vesselRenderer);
        }
    }

    // ── Planets ───────────────────────────────────────────────────────────

    private void SpawnPlanets()
    {
        const float renderScale = 1.0f / 10000.0f;
        var planetsNode = GetTree().Root.FindChild("Planets", true, false) as Node3D;
        var fo          = GetTree().Root.FindChild("FloatingOrigin", true, false) as FloatingOrigin;
        if (planetsNode == null || fo == null) return;

        foreach (var body in Universe.Bodies)
        {
            float r = (float)(body.Radius * renderScale);

            var sphere = new SphereMesh { Radius = r, Height = r * 2f, RadialSegments = 64, Rings = 32 };
            var mat    = new StandardMaterial3D();
            mat.AlbedoColor     = GetPlanetColor(body.Id);
            mat.Roughness       = 0.88f;
            mat.EmissionEnabled = true;
            mat.Emission        = GetPlanetColor(body.Id) * 0.28f;

            var mesh = new MeshInstance3D { Name = body.Name + "_mesh", Mesh = sphere };
            mesh.SetSurfaceOverrideMaterial(0, mat);
            planetsNode.AddChild(mesh);
            fo.RegisterPlanetNode(body.Id, mesh);

            if (body.Id == "earth") SpawnAtmosphereGlow(mesh, r);
        }
    }

    private static void SpawnAtmosphereGlow(MeshInstance3D earthMesh, float surfaceRadius)
    {
        float r = surfaceRadius * 1.028f;
        var atm = new SphereMesh { Radius = r, Height = r * 2f, RadialSegments = 64, Rings = 32 };
        var mat = new StandardMaterial3D
        {
            AlbedoColor              = new Color(0.25f, 0.55f, 1.0f, 0.0f),
            Transparency             = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode                 = BaseMaterial3D.CullModeEnum.Front,
            EmissionEnabled          = true,
            Emission                 = new Color(0.10f, 0.42f, 0.96f),
            EmissionEnergyMultiplier = 0.60f,
        };
        var node = new MeshInstance3D { Name = "atmosphere_glow", Mesh = atm };
        node.SetSurfaceOverrideMaterial(0, mat);
        earthMesh.AddChild(node);
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
