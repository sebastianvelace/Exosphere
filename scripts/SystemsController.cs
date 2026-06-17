// NOTE: This node must be instantiated by an integrator (e.g. SimulationBridge._Ready())
// by calling:  GetParent()?.CallDeferred("add_child", new SystemsController { Name = "SystemsController" });
// Agent F (N9) created this file. Agent D is responsible for wiring it in SimulationBridge._Ready().

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

    public override void _Ready()
    {
        Instance = this;

        // Add SystemsHUD as a sibling under the UI CanvasLayer.
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

        int crewCount = vessel.Crew.Count > 0 ? vessel.Crew.Count : 4; // default 4 crew
        LifeSupport.Tick(delta, crewCount);

        // Eclipse detection: is the vessel in Earth's shadow?
        var earthBody = universe.GetBody("earth");
        var sunBody   = universe.GetBody("sun");
        bool inEclipse = false;
        if (earthBody != null && sunBody != null)
        {
            var toSun   = (sunBody.Position - vessel.Position).Normalized;
            var toEarth = earthBody.Position - vessel.Position;
            double toEarthMag = toEarth.Magnitude;
            if (toEarthMag > 0.1)
            {
                double cosAngle = System.Math.Clamp(toSun.Dot(toEarth.Normalized), -1.0, 1.0);
                double angle    = System.Math.Acos(cosAngle);
                double shadowHalfAngle = System.Math.Asin(
                    System.Math.Clamp(earthBody.Radius / toEarthMag, 0.0, 1.0));
                inEclipse = angle < shadowHalfAngle;
            }
        }

        Vector3d sunPos = sunBody?.Position ?? Vector3d.Zero;
        Power.Tick(delta, vessel.Position, sunPos, inEclipse);

        bool inAtmo    = refBody.Atmosphere != null && alt < refBody.Atmosphere.MaxAltitude;
        double atmoTemp = inAtmo ? refBody.Atmosphere!.GetTemperature(alt) : 3.0;
        Thermal.Tick(delta, inEclipse, inAtmo, atmoTemp);

        Vector3d earthPos = earthBody?.Position ?? Vector3d.Zero;
        Comms.Tick(delta, vessel.Position, earthPos, universe.Bodies);
    }
}
