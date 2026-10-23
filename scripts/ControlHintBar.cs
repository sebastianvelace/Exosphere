namespace Exosphere.Game;

using System.Collections.Generic;
using Exosphere.Simulation.Flight;
using Godot;

/// <summary>
/// Always-visible strip of the keys that matter right now, above the bottom band (the
/// F1 sheet hides at ignition and lists everything). It follows the flight: pad, ascent,
/// orbit, near a docking target, descent, Mission Control flying.
/// </summary>
public partial class ControlHintBar : Control
{
    private const double RefreshIntervalSeconds = 0.5;
    private Label _label = null!;
    private double _sinceRefresh = RefreshIntervalSeconds;

    /// <summary>Density gate driven by <see cref="HUDController"/>; Clean hides it.</summary>
    public bool DensityAllowed { get; set; } = true;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _label = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        InterfaceTheme.ApplyMono(_label, 11);
        _label.AddThemeColorOverride("font_color", InterfaceTheme.Text);
        // A dark strip so the keys read over bright sand, sky or plume.
        _label.AddThemeStyleboxOverride("normal", InterfaceTheme.PanelStyle(0.78f, 12, 4));
        AddChild(_label);
    }

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        Visible = DensityAllowed && vessel != null && !vessel.IsDestroyed;
        if (!Visible || bridge == null || vessel == null) return;
        _sinceRefresh += delta;
        if (_sinceRefresh < RefreshIntervalSeconds) return;
        _sinceRefresh = 0.0;

        var size = GetViewportRect().Size;
        _label.Text = string.Join("   ·   ", Hints(bridge, vessel));
        _label.Size = _label.GetMinimumSize();
        _label.Position = new Vector2((size.X - _label.Size.X) * 0.5f, size.Y - 214f);
    }

    private static IEnumerable<string> Hints(SimulationBridge bridge, Exosphere.Simulation.Vessel vessel)
    {
        bool es = UserInterfaceSettings.Language == InterfaceLanguage.Spanish;
        string T(string en, string sp) => es ? sp : en;
        var phase = MissionManager.Instance?.Phase ?? MissionPhase.PRE_LAUNCH;
        bool historical = HistoricalFlightProfileController.Instance != null
            && bridge.ActiveFlightProfileId != "manual";
        var hints = new List<string>();

        if (HistoricalFlightProfileController.Instance?.IsEngaged == true)
        {
            hints.Add(T("K  TAKE CONTROL", "K  TOMAR EL CONTROL"));
            hints.Add(T(", .  WARP", ", .  WARP"));
            hints.Add(T("C  CAMERA", "C  CÁMARA"));
            hints.Add(T("M  MAP", "M  MAPA"));
            return hints;
        }
        if (phase is MissionPhase.PRE_LAUNCH or MissionPhase.COUNTDOWN)
        {
            hints.Add(T("L  AUTO COUNT", "L  CUENTA AUTOMÁTICA"));
            if (historical) hints.Add(T("K  MISSION CONTROL", "K  CONTROL DE MISIÓN"));
            hints.Add(T("hold Z  IGNITE", "mantené Z  ENCENDER"));
            hints.Add(T("G  ASCENT AUTOPILOT", "G  PILOTO DE ASCENSO"));
            hints.Add(T("F1  ALL CONTROLS", "F1  TODOS LOS CONTROLES"));
            return hints;
        }

        var body = bridge.Universe.GetDominantBody(vessel.Position);
        if (DockingTargeting.FindNearest(bridge.Universe, vessel, 2_000.0) is { } target)
        {
            hints.Add(T("arrows PgUp PgDn  RCS", "flechas RePág AvPág  RCS"));
            hints.Add(T("Shift  FINE", "Shift  FINO"));
            hints.Add(T("Y  SAS TARGET", "Y  SAS OBJETIVO"));
            hints.Add(T($"range {target.RangeM:F0} m", $"rango {target.RangeM:F0} m"));
            return hints;
        }
        var landing = LandingReadout.Compute(vessel, body);
        bool descending = landing.VerticalSpeedMps < -1.0 && double.IsFinite(landing.TimeToImpactS);
        if (descending && landing.AltitudeM < 40_000.0)
        {
            hints.Add(T("Z X  THROTTLE", "Z X  ACELERADOR"));
            hints.Add(T("Y  SAS RETROGRADE", "Y  SAS RETRÓGRADO"));
            if (vessel.HasParachutes) hints.Add(T("P  PARACHUTES", "P  PARACAÍDAS"));
            hints.Add(T("T  SAS", "T  SAS"));
            if (historical) hints.Add(T("K  MISSION CONTROL", "K  CONTROL DE MISIÓN"));
            return hints;
        }
        if (phase is MissionPhase.LIFTOFF or MissionPhase.ASCENT_SH or MissionPhase.MAX_Q
            or MissionPhase.SEPARATION or MissionPhase.ASCENT_SHIP or MissionPhase.MECO)
        {
            hints.Add(T("W S A D  STEER", "W S A D  DIRIGIR"));
            hints.Add(T("Space  STAGE", "Espacio  SEPARAR ETAPA"));
            hints.Add(T("Z X  THROTTLE", "Z X  ACELERADOR"));
            hints.Add(T("G  AUTOPILOT", "G  PILOTO AUTOMÁTICO"));
            if (historical) hints.Add(T("K  MISSION CONTROL", "K  CONTROL DE MISIÓN"));
            return hints;
        }
        hints.Add(T("M  MAP · NODES", "M  MAPA · NODOS"));
        hints.Add(T("Y  SAS MODE", "Y  MODO SAS"));
        hints.Add(T(", .  WARP", ", .  WARP"));
        hints.Add(T("N  WARP TO NODE", "N  WARP AL NODO"));
        if (body.Id == "moon") hints.Add(T("M then B  LUNAR NODE", "M y B  NODO LUNAR"));
        if (historical) hints.Add(T("K  MISSION CONTROL", "K  CONTROL DE MISIÓN"));
        return hints;
    }
}
