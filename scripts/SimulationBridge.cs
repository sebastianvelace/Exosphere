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

    private bool            _running      = false;
    private VesselRenderer? _testRenderer = null;
    private Camera3D?       _camera       = null;

    public override void _Ready()
    {
        Instance = this;

        var dataPath = ProjectSettings.GlobalizePath(DataDirectory);
        Universe = Universe.LoadFromDataDirectory(dataPath);
        Universe.TimeScale = 1.0;
        _running = true;

        SpawnTestVessel(dataPath);
        SpawnPlanets();
        EmitSignal(SignalName.SimulationLoaded);

        _camera = GetTree().Root.FindChild("Camera3D", true, false) as Camera3D;
        if (_camera != null)
        {
            _camera.Position = new Godot.Vector3(0f, 3f, 12f);
            _camera.Far = 2000.0f;
        }

        // Apuntar la luz direccional para iluminar los planetas
        var light = GetTree().Root.FindChild("DirectionalLight3D", true, false) as DirectionalLight3D;
        if (light != null)
        {
            // Luz desde arriba-izquierda, apuntando hacia la escena
            light.RotationDegrees = new Godot.Vector3(-45f, -30f, 0f);
        }
    }

    public override void _Process(double delta)
    {
        if (!_running || Universe == null) return;
        Universe.Tick(delta);

        // Mantener la cámara apuntando al vessel activo (siempre en origen por FloatingOrigin)
        if (_camera != null && ActiveVessel != null)
            _camera.LookAt(Godot.Vector3.Zero, Godot.Vector3.Up);
    }

    // ── Vessel de prueba ───────────────────────────────────────────────────

    private void SpawnTestVessel(string dataPath)
    {
        var earth = Universe.GetBody("earth");
        if (earth == null) return;

        var partsDir = System.IO.Path.Combine(dataPath, "parts");
        var defs     = PartDefinition.LoadAllFromDirectory(partsDir);

        // Construir cohete: cápsula + tanque pequeño + motor SL
        var vessel = new Vessel { Name = "Testship-1 (LEO)" };

        if (!defs.TryGetValue("command_pod_mk1", out var podDef))    return;
        if (!defs.TryGetValue("fuel_tank_small",  out var tankDef))   return;
        if (!defs.TryGetValue("engine_liquid_sl", out var engineDef)) return;

        var pod    = new Part(podDef);
        var tank   = new Part(tankDef);
        var engine = new Part(engineDef);

        vessel.Parts.SetRoot(pod);
        vessel.Parts.AddPart(pod);
        vessel.Parts.AddPart(tank);
        vessel.Parts.AddPart(engine);

        // pod.bottom → tank.top → tank.bottom → engine.top
        vessel.Parts.AddJoint(new Joint(pod,  tank,   "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(tank, engine, "bottom", "top"));

        // Órbita circular a 250 km sobre la Tierra
        double r = earth.Radius + 250_000.0;
        double v = System.Math.Sqrt(earth.GM / r);
        vessel.Position = earth.Position + new Vector3d(r, 0.0, 0.0);
        vessel.Velocity = earth.Velocity + new Vector3d(0.0, v, 0.0);
        vessel.SASEnabled = true;

        Universe.AddVessel(vessel);
        Universe.ActiveVessel = vessel;

        // Crear renderer visual y registrarlo en FloatingOrigin
        var vesselsNode = GetTree().Root.FindChild("Vessels", true, false) as Node3D;
        if (vesselsNode != null)
        {
            _testRenderer = new VesselRenderer();
            _testRenderer.Name = "TestVesselRenderer";
            vesselsNode.AddChild(_testRenderer);
            _testRenderer.BuildFromVessel(vessel);

            var fo = GetTree().Root.FindChild("FloatingOrigin", true, false) as FloatingOrigin;
            fo?.RegisterVesselNode(vessel.Id, _testRenderer);
        }
    }

    // ── Planetas ───────────────────────────────────────────────────────────

    private void SpawnPlanets()
    {
        const float renderScale = 1.0f / 10000.0f;
        var planetsNode = GetTree().Root.FindChild("Planets", true, false) as Node3D;
        var fo          = GetTree().Root.FindChild("FloatingOrigin", true, false) as FloatingOrigin;
        if (planetsNode == null || fo == null) return;

        foreach (var body in Universe.Bodies)
        {
            float renderRadius = (float)(body.Radius * renderScale);

            var sphere = new SphereMesh();
            sphere.Radius         = renderRadius;
            sphere.Height         = renderRadius * 2.0f;
            sphere.RadialSegments = 64;
            sphere.Rings          = 32;

            var mat = new StandardMaterial3D();
            mat.AlbedoColor = GetPlanetColor(body.Id);
            mat.Roughness   = 0.8f;

            var mesh = new MeshInstance3D();
            mesh.Name = body.Name + "_mesh";
            mesh.Mesh = sphere;
            mesh.SetSurfaceOverrideMaterial(0, mat);

            planetsNode.AddChild(mesh);
            fo.RegisterPlanetNode(body.Id, mesh);
        }
    }

    private static Color GetPlanetColor(string id) => id switch
    {
        "earth"   => new Color(0.2f, 0.45f, 0.8f),
        "moon"    => new Color(0.6f, 0.6f, 0.6f),
        "mars"    => new Color(0.7f, 0.3f, 0.15f),
        "venus"   => new Color(0.85f, 0.75f, 0.4f),
        "mercury" => new Color(0.5f, 0.48f, 0.46f),
        "jupiter" => new Color(0.8f, 0.65f, 0.45f),
        "saturn"  => new Color(0.9f, 0.8f, 0.55f),
        "sun"     => new Color(1.0f, 0.9f, 0.3f),
        _         => new Color(0.7f, 0.7f, 0.7f)
    };

    // ── API pública ────────────────────────────────────────────────────────

    public void SetThrottle(double t) { if (ActiveVessel != null) ActiveVessel.Throttle = t; }
    public void SetSAS(bool enabled)  { if (ActiveVessel != null) ActiveVessel.SASEnabled = enabled; }

    public void TriggerStaging()
    {
        if (ActiveVessel == null) return;
        var debris = ActiveVessel.Stage();
        if (debris != null)
        {
            Universe.AddVessel(debris);
            EmitSignal(SignalName.VesselStaged, debris.Id);
        }
    }

    public void SetTimeScale(double scale) => Universe.TimeScale = scale;

    public Vessel? SpawnVesselAtLaunchPad(string launchSiteJson)
    {
        var earth = Universe.GetBody("earth");
        if (earth == null) return null;
        var vessel = new Vessel { Name = "New Vessel" };
        vessel.Position = earth.Position + new Vector3d(0, earth.Radius + 10, 0);
        vessel.Velocity = earth.Velocity;
        Universe.AddVessel(vessel);
        Universe.ActiveVessel = vessel;
        return vessel;
    }
}
