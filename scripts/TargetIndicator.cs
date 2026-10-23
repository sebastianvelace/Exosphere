namespace Exosphere.Game;

using Exosphere.Simulation.Flight;
using Godot;

/// <summary>
/// Docking target readout for hand-flown rendezvous: a marker on the nearest free docking
/// port (or an edge arrow when it is off screen) and one line with range, closing speed,
/// sideways drift and port alignment from <see cref="DockingTargeting"/>. Inside 200 m it
/// also says how fast it is safe to close.
/// </summary>
public partial class TargetIndicator : Control
{
    private const double RefreshIntervalSeconds = 0.2;
    private const float EdgeMargin = 46f;

    private Label _status = null!;
    private double _sinceRefresh = RefreshIntervalSeconds;
    private DockingTarget? _target;
    private Color _color = InterfaceTheme.Text;

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
            OffsetLeft = -360,
            OffsetRight = 360,
            OffsetTop = 116,
        };
        InterfaceTheme.ApplyMono(_status, 12);
        AddChild(_status);
    }

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        if (bridge == null || vessel == null)
        {
            Visible = false;
            return;
        }
        PlaceStatus();
        _sinceRefresh += delta;
        if (_sinceRefresh >= RefreshIntervalSeconds)
        {
            _sinceRefresh = 0.0;
            _target = DockingTargeting.FindNearest(bridge.Universe, vessel);
            UpdateStatus();
        }
        Visible = DensityAllowed && _target != null;
        QueueRedraw();
    }

    private void UpdateStatus()
    {
        if (_target is not { } target)
        {
            _status.Text = "";
            return;
        }
        bool es = UserInterfaceSettings.Language == InterfaceLanguage.Spanish;
        string range = target.RangeM >= 10_000.0 ? $"{target.RangeM / 1000.0:F1} km" : $"{target.RangeM:F1} m";
        string closing = es
            ? (target.ClosingSpeedMps >= 0.0 ? $"ACERCANDO {target.ClosingSpeedMps:F2} m/s" : $"ALEJANDO {-target.ClosingSpeedMps:F2} m/s")
            : (target.ClosingSpeedMps >= 0.0 ? $"CLOSING {target.ClosingSpeedMps:F2} m/s" : $"OPENING {-target.ClosingSpeedMps:F2} m/s");
        string text = $"{target.Target.Name.ToUpperInvariant()} {range} · {closing} · "
            + (es ? $"DERIVA {target.LateralSpeedMps:F2} m/s" : $"DRIFT {target.LateralSpeedMps:F2} m/s");
        if (target.RangeM < 200.0)
        {
            // The same √(range) closing profile the automated approach flies, capped near the port.
            double safe = System.Math.Clamp(0.05 * System.Math.Sqrt(target.RangeM), 0.1, 0.6);
            text += es
                ? $" · ALINEACIÓN {target.AlignmentErrorDeg:F0}° · MÁX {safe:F2} m/s"
                : $" · ALIGN {target.AlignmentErrorDeg:F0}° · MAX {safe:F2} m/s";
            _color = target.ClosingSpeedMps > safe || target.AlignmentErrorDeg > 14.0
                ? InterfaceTheme.Warning : InterfaceTheme.Success;
        }
        else
        {
            _color = InterfaceTheme.Text;
        }
        _status.Text = text;
    }

    /// <summary>
    /// Centres the status line under the top band from the viewport size: the HUD parent
    /// has no size of its own, so anchors alone left the line in the top-left corner.
    /// </summary>
    private void PlaceStatus()
    {
        var size = GetViewportRect().Size;
        _status.Position = new Vector2(size.X * 0.5f - 360f, 116f + 44f);
        _status.Size = new Vector2(720f, 20f);
    }

    public override void _Draw()
    {
        _status.AddThemeColorOverride("font_color", _color);
        var vessel = SimulationBridge.Instance?.ActiveVessel;
        var camera = GetViewport()?.GetCamera3D();
        if (_target is not { } target || vessel == null || camera == null) return;

        var los = target.LineOfSight;
        var direction = new Vector3((float)los.X, (float)los.Y, (float)los.Z).Normalized();
        var point = camera.GlobalPosition + direction * 1000f;
        var size = GetViewportRect().Size;
        var center = size * 0.5f;
        bool behind = camera.IsPositionBehind(point);
        Vector2 screen = camera.UnprojectPosition(point);
        bool onScreen = !behind && screen.X > EdgeMargin && screen.X < size.X - EdgeMargin
            && screen.Y > EdgeMargin && screen.Y < size.Y - EdgeMargin;
        if (onScreen)
        {
            DrawRect(new Rect2(screen - new Vector2(14f, 14f), new Vector2(28f, 28f)), _color, false, 2f);
            DrawString(InterfaceTheme.DisplayFont, screen + new Vector2(20f, 5f), "TGT",
                HorizontalAlignment.Left, -1, 14, _color);
            return;
        }
        var offset = behind ? center - screen : screen - center;
        if (offset.LengthSquared() < 1f) offset = Vector2.Up;
        var heading = offset.Normalized();
        float scale = Mathf.Min(
            (center.X - EdgeMargin) / Mathf.Max(Mathf.Abs(heading.X), 1e-3f),
            (center.Y - EdgeMargin) / Mathf.Max(Mathf.Abs(heading.Y), 1e-3f));
        var tip = center + heading * scale;
        var side = new Vector2(-heading.Y, heading.X);
        DrawColoredPolygon(new[] { tip, tip - heading * 20f + side * 10f, tip - heading * 20f - side * 10f }, _color);
        DrawString(InterfaceTheme.DisplayFont, tip - heading * 38f - new Vector2(12f, -5f), "TGT",
            HorizontalAlignment.Left, -1, 13, _color);
    }
}
