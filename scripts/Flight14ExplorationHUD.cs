namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation.Flight;

/// <summary>Exploration controls and explicit diagnostic boundary, separate from mission success.</summary>
public partial class Flight14ExplorationHUD : Control
{
    private Label _status = null!;
    private Button _pause = null!;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        MouseFilter = MouseFilterEnum.Ignore;
        ZIndex = 30;
        var row = new HBoxContainer { OffsetLeft = 20, OffsetTop = 96, OffsetRight = -20 };
        row.SetAnchorsPreset(LayoutPreset.TopWide);
        row.AddThemeConstantOverride("separation", 10);
        AddChild(row);
        _status = new Label
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(280, 42),
        };
        InterfaceTheme.ApplyLabel(_status, 12);
        row.AddChild(_status);
        _pause = AddButton(row, "PAUSE", () =>
        {
            if (SimulationBridge.Instance?.Flight14Preview is { } preview && !preview.IsStopped)
                preview.IsPaused = !preview.IsPaused;
        });
        AddButton(row, "RESTART", () =>
        {
            CraftLaunchRequest.Set(new LaunchIntent
            {
                Mode = "exploration", FlightProfileId = Flight14Exploration.ProfileId,
                LaunchSiteId = "starbase_pad2",
            });
            GetTree().ChangeSceneToFile("res://scenes/flight/Flight.tscn");
        });
        AddButton(row, "MENU", () => GetTree().ChangeSceneToFile("res://scenes/ui/MainMenu.tscn"));
    }

    private static Button AddButton(HBoxContainer row, string text, Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(80, 34) };
        InterfaceTheme.StyleButton(button);
        button.CustomMinimumSize = new Vector2(84, 34);
        button.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        button.Pressed += action;
        row.AddChild(button);
        return button;
    }

    public override void _Process(double delta)
    {
        if (SimulationBridge.Instance?.Flight14Preview is not { } preview) return;
        _pause.Text = preview.IsPaused ? "RESUME" : "PAUSE";
        _pause.Disabled = preview.IsStopped;
        string state = preview.IsTerminal ? "PREVIEW ENDS AT 100 M · SPLASHDOWN / BOOSTER RECOVERY PENDING"
            : preview.BlockReason != null ? "PREVIEW STOPPED · " + preview.BlockReason
            : preview.Run.Ship.IsDestroyed ? "VEHICLE LOST · RESTART TO EXPLORE AGAIN"
            : $"{preview.Phase.ToUpperInvariant()} · PAYLOADS {preview.Run.PayloadController.Releases.Count}/26"
                + (preview.IsPaused ? " · PAUSED" : " · [, .] TIME · DRAG / SCROLL CAMERA");
        _status.Text = "FLIGHT 14 / ENGINEERING EXPLORATION\n" + state;
    }
}
