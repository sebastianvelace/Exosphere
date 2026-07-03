namespace Exosphere.Game;

using Godot;

/// <summary>
/// HUD overlay that displays the current time-warp level and the allowed maximum.
/// Press <c>[.]</c> to increase warp and <c>[,]</c> to decrease warp.
/// Added to the scene automatically by <see cref="SimulationBridge._Ready"/>.
/// </summary>
public partial class WarpController : Control
{
    private Font _font = null!;
    private StyleBoxFlat _panelStyle = null!;

    public override void _Ready()
    {
        _font = ThemeDB.FallbackFont;
        _panelStyle = InterfaceTheme.GlassPanel(0.68f, 12, 0, 0);
        SetAnchorsPreset(LayoutPreset.TopLeft);
        CustomMinimumSize = new Vector2(178, 50);
        OffsetLeft   = 320;
        OffsetTop    = 18;
        OffsetRight  = 498;
        OffsetBottom = 68;
        MouseFilter  = MouseFilterEnum.Ignore;
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventKey { Pressed: true, Echo: false } key)
        {
            var bridge = SimulationBridge.Instance;
            if (bridge == null) return;

            bool handled = false;
            if (key.Keycode == Key.Period)
            {
                bridge.SetWarpIndex(bridge.WarpIndex + 1);
                handled = true;
            }
            else if (key.Keycode == Key.Comma)
            {
                bridge.SetWarpIndex(bridge.WarpIndex - 1);
                handled = true;
            }

            if (handled)
                GetViewport().SetInputAsHandled();
        }
    }

    public override void _Draw()
    {
        var bridge = SimulationBridge.Instance;
        if (bridge == null || _font == null) return;

        double currentRate = SimulationBridge.WarpLevels[bridge.WarpIndex];
        double maxRate     = SimulationBridge.WarpLevels[bridge.MaxAllowedWarpIndex];

        string line1 = $"TIME  x{currentRate:G}";
        string line2 = $"MAXIMUM  x{maxRate:G}";

        var universe = bridge.Universe;
        bool showClamp = bridge.WarpClampReason != null
            && universe != null
            && universe.CurrentTime < bridge.WarpClampReasonUntil;

        float panelH = showClamp ? 68f : 50f;
        CustomMinimumSize = new Vector2(178, panelH);
        OffsetBottom = OffsetTop + panelH;

        DrawStyleBox(_panelStyle, new Rect2(Vector2.Zero, Size));

        var col1 = bridge.WarpIndex > 0 ? InterfaceTheme.Warning : InterfaceTheme.Text;
        DrawString(_font, new Vector2(14, 20), line1, HorizontalAlignment.Left, -1, 13, col1);
        DrawString(_font, new Vector2(14, 39), line2, HorizontalAlignment.Left, -1, 10, InterfaceTheme.TextMuted);

        if (showClamp)
        {
            string line3 = $"CLAMP — {bridge.WarpClampReason}";
            DrawString(_font, new Vector2(14, 58), line3, HorizontalAlignment.Left, -1, 10, InterfaceTheme.Warning);
        }
    }

    public override void _Process(double delta) => QueueRedraw();
}
