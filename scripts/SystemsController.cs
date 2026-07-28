namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation.Systems;
using Exosphere.Simulation.Math;

/// Nodo que actualiza todos los sistemas cada frame y los expone al HUD.
public partial class SystemsController : Node
{
    public static SystemsController? Instance { get; private set; }

    public LifeSupportSystem LifeSupport { get; } = new();
    public PowerSystem       Power       { get; } = new();
    public ThermalSystem     Thermal     { get; } = new();
    public CommsSystem       Comms       { get; } = new();

    public bool ControlLimited { get; private set; }

    public override void _Ready()
    {
        Instance = this;

        var uiLayer = GetTree().Root.FindChild("UI", true, false) as CanvasLayer;
        if (uiLayer != null)
        {
            var hud = new SystemsHUD { Name = "SystemsHUD" };
            uiLayer.CallDeferred("add_child", hud);
        }
    }

    public override void _Process(double delta)
    {
        var bridge   = SimulationBridge.Instance;
        var vessel   = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (vessel == null || universe == null) return;

        var refBody = universe.GetDominantBody(vessel.Position);
        double alt  = vessel.GetAltitude(refBody);

        int crewCount = vessel.Crew.Count > 0 ? vessel.Crew.Count : 4;
        var sysPhase  = MapMissionPhase(MissionManager.Instance?.Phase ?? MissionPhase.PRE_LAUNCH);
        LifeSupport.Tick(delta, crewCount, sysPhase);

        var earthBody = universe.GetBody("earth");
        var sunBody   = universe.GetBody("sun");
        double solarVisibility = 1.0;
        if (sunBody != null)
        {
            foreach (var body in universe.Bodies)
            {
                if (body.Id == "sun") continue;
                solarVisibility = System.Math.Min(solarVisibility,
                    MissionGeometry.SolarDiscVisibility(vessel.Position, body.Position,
                        body.Radius, sunBody.Position, sunBody.Radius));
            }
        }

        Vector3d sunPos = sunBody?.Position ?? Vector3d.Zero;
        double lsLoadKw = LifeSupport.GetEcLoadKw(crewCount, sysPhase);
        Power.Tick(delta, vessel.Position, sunPos, solarVisibility, lsLoadKw);

        bool inAtmo    = refBody.Atmosphere != null && alt < refBody.Atmosphere.MaxAltitude;
        double atmoTemp = inAtmo ? refBody.Atmosphere!.GetTemperature(alt) : 3.0;
        Thermal.Tick(delta, solarVisibility, inAtmo, atmoTemp);

        Vector3d earthPos = earthBody?.Position ?? Vector3d.Zero;
        Comms.Tick(delta, vessel.Position, earthPos, universe.Bodies);

        ApplyGameplayConsequences(vessel);
    }

    private void ApplyGameplayConsequences(Exosphere.Simulation.Vessel vessel)
    {
        bool structuralLost = vessel.StructuralControlLost;
        ControlLimited = Power.NoPowerAlert || !Comms.HasSignal || !LifeSupport.CrewAlive
            || structuralLost;

        if (ControlLimited)
        {
            vessel.SASEnabled = false;
            vessel.PitchYawRoll = Vector3d.Zero;

            ManeuverExecutor.Instance?.Abort();

            if (GetTree().Root.FindChild("AutopilotController", true, false) is AutopilotController ap)
                ap.Disarm();

            // Structural dead-stick: cut commanded throttle so a tumbling wreck does not
            // keep burning propellant under a stuck autopilot setpoint.
            if (structuralLost)
                vessel.Throttle = 0.0;
        }
    }

    private static SystemsMissionPhase MapMissionPhase(MissionPhase phase) => phase switch
    {
        MissionPhase.PRE_LAUNCH or MissionPhase.COUNTDOWN or MissionPhase.LANDED => SystemsMissionPhase.Idle,
        _ => SystemsMissionPhase.Active,
    };
}
