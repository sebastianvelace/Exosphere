namespace Exosphere.Game;

using Exosphere.Simulation.Flight;
using Godot;

/// <summary>
/// Landing aids line (Kerbal Engineer style) from <see cref="LandingReadout"/>: altitude,
/// vertical and horizontal speed, time to impact and the altitude left before a full-thrust
/// braking burn must start. Shown while descending below 40 km over an airless body or
/// 20 km over an atmosphere.
/// </summary>
public partial class LandingAidIndicator : Control
{
    private const double RefreshIntervalSeconds = 0.1;
    private Label _status = null!;
    private double _sinceRefresh = RefreshIntervalSeconds;

    /// <summary>Density gate driven by <see cref="HUDController"/>; Clean hides it.</summary>
    public bool DensityAllowed { get; set; } = true;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _status = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        InterfaceTheme.ApplyMono(_status, 13);
        AddChild(_status);
    }

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        if (bridge == null || vessel == null || vessel.IsDestroyed || !DensityAllowed)
        {
            Visible = false;
            return;
        }
        _sinceRefresh += delta;
        if (_sinceRefresh < RefreshIntervalSeconds) return;
        _sinceRefresh = 0.0;

        var body = bridge.Universe.GetDominantBody(vessel.Position);
        var status = LandingReadout.Compute(vessel, body);
        double ceiling = body.Atmosphere == null ? 40_000.0 : 20_000.0;
        Visible = status.AltitudeM < ceiling && status.VerticalSpeedMps < -1.0
            && double.IsFinite(status.TimeToImpactS);
        if (!Visible) return;

        var size = GetViewportRect().Size;
        _status.Position = new Vector2(size.X * 0.5f - 400f, 184f);
        _status.Size = new Vector2(800f, 22f);

        bool es = UserInterfaceSettings.Language == InterfaceLanguage.Spanish;
        string altitude = status.AltitudeM >= 10_000.0 ? $"{status.AltitudeM / 1000.0:F1} km" : $"{status.AltitudeM:N0} m";
        string text = $"ALT {altitude} · V {status.VerticalSpeedMps:F1} m/s · H {status.HorizontalSpeedMps:F1} m/s · "
            + (es ? $"IMPACTO {status.TimeToImpactS:F0} s" : $"IMPACT {status.TimeToImpactS:F0} s");
        Color color = InterfaceTheme.Text;
        if (double.IsFinite(status.BurnMarginM))
        {
            if (status.BurnMarginM <= 0.0)
            {
                text += es ? " · QUEMÁ YA" : " · BURN NOW";
                color = InterfaceTheme.Alert;
            }
            else
            {
                string margin = status.BurnMarginM >= 10_000.0 ? $"{status.BurnMarginM / 1000.0:F1} km" : $"{status.BurnMarginM:N0} m";
                text += es ? $" · FRENAR EN {margin}" : $" · BURN IN {margin}";
                if (status.BurnMarginM < 0.25 * status.AltitudeM) color = InterfaceTheme.Warning;
            }
        }
        else if (double.IsPositiveInfinity(status.StoppingDistanceM))
        {
            text += es ? " · EMPUJE INSUFICIENTE" : " · NOT ENOUGH THRUST";
            color = InterfaceTheme.Alert;
        }
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", color);
    }
}
