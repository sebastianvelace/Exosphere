namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;

[GlobalClass]
public partial class SimulationBridge : Node
{
    public static SimulationBridge Instance { get; private set; } = null!;

    public Universe Universe { get; private set; } = null!;
    public Vessel?  ActiveVessel => Universe.ActiveVessel;

    // Ruta al directorio /data (relativa al ejecutable)
    [Export] public string DataDirectory { get; set; } = "res://data";

    // Señales para que otros nodos reaccionen a eventos de simulación
    [Signal] public delegate void VesselStagedEventHandler(string detachedVesselId);
    [Signal] public delegate void VesselDestroyedEventHandler(string vesselId);
    [Signal] public delegate void SimulationLoadedEventHandler();

    private bool _running = false;

    public override void _Ready()
    {
        Instance = this;

        // Resolver ruta del directorio de datos
        var dataPath = ProjectSettings.GlobalizePath(DataDirectory);
        Universe = Universe.LoadFromDataDirectory(dataPath);
        Universe.TimeScale = 1.0;
        _running = true;
        EmitSignal(SignalName.SimulationLoaded);
    }

    public override void _Process(double delta)
    {
        if (!_running || Universe == null) return;
        Universe.Tick(delta);

        // Detectar vessels con staging pendiente
        // (en una implementación completa, Universe.Tick retornaría eventos)
    }

    // ── API pública para la UI ─────────────────────────────────────────────

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
        // En Phase 1: crear un vessel vacío en la superficie de Earth
        var earth = Universe.GetBody("earth");
        if (earth == null) return null;

        var vessel = new Vessel { Name = "New Vessel" };
        // Posicionar en la superficie en la latitud/longitud del launchpad
        // (simplificado: colocar en el polo norte por ahora)
        vessel.Position = earth.Position + new Vector3d(0, earth.Radius + 10, 0);
        vessel.Velocity = earth.Velocity;
        Universe.AddVessel(vessel);
        Universe.ActiveVessel = vessel;
        return vessel;
    }
}
