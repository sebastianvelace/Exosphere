namespace Exosphere.Game;

using Exosphere.Simulation.Navigation;
using Godot;

/// <summary>
/// "Where is the Moon?" without opening the map: an arrow on the screen edge (or a ring when
/// the Moon is in view) pointing at the Moon, and one status line from
/// <see cref="LunarStatusReadout"/> — on course for its sphere of influence, missing it, or
/// captured in lunar orbit. Shown on lunar missions once off the pad, and whenever the
/// vessel is inside the Moon's sphere.
/// </summary>
public partial class MoonIndicator : Control
{
    private const double RefreshIntervalSeconds = 0.5;
    private const float EdgeMargin = 46f;

    private Label _status = null!;
    private double _sinceRefresh = RefreshIntervalSeconds;
    private LunarStatus? _lunar;
    private Color _statusColor = InterfaceTheme.Text;

    /// <summary>Density gate driven by <see cref="HUDController"/>; Clean hides it.</summary>
    public bool DensityAllowed { get; set; } = true;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _status = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            OffsetLeft = -330,
            OffsetRight = 330,
            OffsetTop = 92,
        };
        InterfaceTheme.ApplyMono(_status, 12);
        AddChild(_status);
    }

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        bool lunarMission = bridge?.ActiveFlightProfileId is
            Exosphere.Simulation.Flight.Apollo8FlightProfile.Id
            or Exosphere.Simulation.Flight.Apollo11FlightProfile.Id;
        var phase = MissionManager.Instance?.Phase;
        bool offPad = phase is not (null or MissionPhase.PRE_LAUNCH or MissionPhase.COUNTDOWN
            or MissionPhase.IGNITION);
        bool nearMoon = _lunar?.InLunarSphere == true;
        Visible = DensityAllowed && vessel != null && (lunarMission && offPad || nearMoon);
        if (bridge == null || vessel == null) return;
        PlaceStatus();

        _sinceRefresh += delta;
        if (_sinceRefresh >= RefreshIntervalSeconds)
        {
            _sinceRefresh = 0.0;
            _lunar = LunarStatusReadout.Compute(vessel, bridge.Universe);
            UpdateStatusText();
        }
        QueueRedraw();
    }

    private void UpdateStatusText()
    {
        if (_lunar is not { } lunar)
        {
            _status.Text = "";
            return;
        }
        bool es = UserInterfaceSettings.Language == InterfaceLanguage.Spanish;
        string distance = $"{lunar.DistanceM / 1000.0:N0} km";
        if (lunar.InLunarSphere)
        {
            if (lunar.CapturedInLunarOrbit)
            {
                _statusColor = InterfaceTheme.Success;
                _status.Text = es
                    ? $"ÓRBITA LUNAR · PERILUNIO {lunar.PeriluneAltitudeM / 1000.0:F0} km"
                    : $"LUNAR ORBIT · PERILUNE {lunar.PeriluneAltitudeM / 1000.0:F0} km";
            }
            else if (lunar.PeriluneAltitudeM < 0.0)
            {
                _statusColor = InterfaceTheme.Alert;
                _status.Text = es ? "LUNA · RUMBO DE IMPACTO" : "MOON · IMPACT COURSE";
            }
            else
            {
                _statusColor = InterfaceTheme.Warning;
                string eta = double.IsFinite(lunar.TimeToEventS) && lunar.TimeToEventS > 0.0
                    ? FormatDuration(lunar.TimeToEventS) : "—";
                _status.Text = es
                    ? $"LUNA · PERILUNIO {lunar.PeriluneAltitudeM / 1000.0:F0} km EN {eta} · FRENÁ AHÍ (LOI)"
                    : $"MOON · PERILUNE {lunar.PeriluneAltitudeM / 1000.0:F0} km IN {eta} · BRAKE THERE (LOI)";
            }
            return;
        }
        switch (lunar.EncounterPredicted)
        {
            case true:
                _statusColor = InterfaceTheme.Success;
                _status.Text = es
                    ? $"LUNA {distance} · LLEGADA EN {FormatDuration(lunar.TimeToEventS)} · PERILUNIO {lunar.PeriluneAltitudeM / 1000.0:F0} km"
                    : $"MOON {distance} · ARRIVAL IN {FormatDuration(lunar.TimeToEventS)} · PERILUNE {lunar.PeriluneAltitudeM / 1000.0:F0} km";
                break;
            case false:
                _statusColor = InterfaceTheme.Warning;
                _status.Text = es
                    ? $"LUNA {distance} · NO LLEGÁS: PASÁS A {lunar.MissDistanceM / 1000.0:N0} km · CORREGÍ EL RUMBO"
                    : $"MOON {distance} · MISS BY {lunar.MissDistanceM / 1000.0:N0} km · CORRECT COURSE";
                break;
            default:
                _statusColor = InterfaceTheme.TextMuted;
                _status.Text = es
                    ? $"LUNA {distance} · TODAVÍA SIN TRAYECTORIA LUNAR (TLI)"
                    : $"MOON {distance} · NOT ON A LUNAR TRAJECTORY YET (TLI)";
                break;
        }
    }

    /// <summary>
    /// Centres the status line under the top band from the viewport size: the HUD parent
    /// has no size of its own, so anchors alone left the line in the top-left corner.
    /// </summary>
    private void PlaceStatus()
    {
        var size = GetViewportRect().Size;
        _status.Position = new Vector2(size.X * 0.5f - 360f, 92f + 44f);
        _status.Size = new Vector2(720f, 20f);
    }

    public override void _Draw()
    {
        _status.AddThemeColorOverride("font_color", _statusColor);
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var moon = bridge?.Universe.GetBody("moon");
        var camera = GetViewport()?.GetCamera3D();
        if (vessel == null || moon == null || camera == null) return;

        // Simulation and render axes coincide; only the direction matters here.
        var toMoon = moon.Position - vessel.Position;
        var direction = new Vector3((float)toMoon.X, (float)toMoon.Y, (float)toMoon.Z).Normalized();
        var target = camera.GlobalPosition + direction * 1000f;
        var size = GetViewportRect().Size;
        var center = size * 0.5f;
        bool behind = camera.IsPositionBehind(target);
        Vector2 screen = camera.UnprojectPosition(target);
        bool onScreen = !behind && screen.X > EdgeMargin && screen.X < size.X - EdgeMargin
            && screen.Y > EdgeMargin && screen.Y < size.Y - EdgeMargin;
        var color = _statusColor;
        if (onScreen)
        {
            DrawArc(screen, 18f, 0f, Mathf.Tau, 32, color, 2f, true);
            DrawString(InterfaceTheme.DisplayFont, screen + new Vector2(24f, 6f), "MOON",
                HorizontalAlignment.Left, -1, 15, color);
            return;
        }
        // Off screen: put an arrow on the screen edge in the Moon's direction.
        var offset = behind ? center - screen : screen - center;
        if (offset.LengthSquared() < 1f) offset = Vector2.Up;
        var heading = offset.Normalized();
        float scale = Mathf.Min(
            (center.X - EdgeMargin) / Mathf.Max(Mathf.Abs(heading.X), 1e-3f),
            (center.Y - EdgeMargin) / Mathf.Max(Mathf.Abs(heading.Y), 1e-3f));
        var tip = center + heading * scale;
        var side = new Vector2(-heading.Y, heading.X);
        DrawColoredPolygon(new[] { tip, tip - heading * 22f + side * 11f, tip - heading * 22f - side * 11f }, color);
        DrawString(InterfaceTheme.DisplayFont, tip - heading * 40f - new Vector2(18f, -5f), "MOON",
            HorizontalAlignment.Left, -1, 14, color);
    }

    private static string FormatDuration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0.0) return "—";
        int days = (int)(seconds / 86_400.0);
        int hours = (int)(seconds % 86_400.0 / 3_600.0);
        int minutes = (int)(seconds % 3_600.0 / 60.0);
        return days > 0 ? $"{days}d {hours:00}h" : hours > 0 ? $"{hours}h {minutes:00}m" : $"{minutes}m {(int)(seconds % 60.0):00}s";
    }
}
