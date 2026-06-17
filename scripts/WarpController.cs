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

    public override void _Ready()
    {
        _font = ThemeDB.FallbackFont;
        SetAnchorsPreset(LayoutPreset.TopLeft);
        CustomMinimumSize = new Vector2(160, 40);
        OffsetLeft   = 10;
        OffsetTop    = 10;
        OffsetRight  = 170;
        OffsetBottom = 50;
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

        string line1 = $"WARP  x{currentRate:G}";
        string line2 = $"[max: x{maxRate:G}]";

        var bgRect = new Rect2(0, 0, 160, 40);
        DrawRect(bgRect, new Color(0, 0, 0, 0.55f));

        // Highlight warp indicator in amber when warping, white at x1
        var col1 = bridge.WarpIndex > 0 ? new Color(1.0f, 0.80f, 0.20f) : new Color(0.85f, 0.85f, 0.85f);
        DrawString(_font, new Vector2(8, 16), line1, HorizontalAlignment.Left, -1, 15, col1);
        DrawString(_font, new Vector2(8, 34), line2, HorizontalAlignment.Left, -1, 12, new Color(0.6f, 0.6f, 0.6f));
    }

    public override void _Process(double delta) => QueueRedraw();
}
