namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation.Flight;

/// <summary>Exploration controls and explicit diagnostic boundary, separate from mission success.</summary>
public partial class Flight14ExplorationHUD : Control
{
    private Label _status = null!;
    private Button _pause = null!;
    private Button _observe = null!;
    private Button _entry = null!;
    private VBoxContainer _controls = null!;
    private Control? _phaseBanner;
    private Viewport? _shortcutViewport;
    private bool _previousDisable3D;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        MouseFilter = MouseFilterEnum.Ignore;
        ZIndex = 30;
        _controls = new VBoxContainer { Name = "ExplorationControls" };
        _controls.SetAnchorsPreset(LayoutPreset.CenterTop);
        _controls.GrowHorizontal = GrowDirection.Both;
        _controls.OffsetLeft = -300;
        _controls.OffsetRight = 300;
        _controls.OffsetTop = 96;
        _controls.AddThemeConstantOverride("separation", 8);
        AddChild(_controls);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 10);
        _status = new Label
        {
            Name = "ExplorationStatus",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        InterfaceTheme.ApplyLabel(_status, 12);
        _controls.AddChild(_status);
        _controls.AddChild(row);
        _pause = AddButton(row, "PAUSE", () =>
        {
            if (SimulationBridge.Instance?.Flight14Preview is { } preview && !preview.IsStopped)
            {
                if (preview.IsAdvancingToEntry) preview.CancelAdvanceToEntry();
                else preview.IsPaused = !preview.IsPaused;
            }
        });
        _observe = AddButton(row, "SUPER HEAVY", () =>
        {
            if (SimulationBridge.Instance?.Flight14Preview is { } preview
                && preview.ObserveBooster(!preview.IsObservingBooster))
                CameraController.Instance?.EnterShipChaseView();
        });
        _entry = AddButton(row, "SKIP TO ENTRY", () =>
        {
            if (SimulationBridge.Instance is { Flight14Preview: { } preview } bridge
                && preview.BeginAdvanceToEntry())
            {
                bridge.SetWarpIndex(0);
                CameraController.Instance?.EnterShipChaseView();
            }
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

    public override void _ExitTree() => RestoreWorldView();

    private void RestoreWorldView()
    {
        if (_shortcutViewport != null && GodotObject.IsInstanceValid(_shortcutViewport))
            _shortcutViewport.Disable3D = _previousDisable3D;
        _shortcutViewport = null;
    }

    private static Button AddButton(HBoxContainer row, string text, Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(80, 34) };
        InterfaceTheme.StyleButton(button, minSize: new Vector2(84, 34), paddingX: 12, paddingY: 8);
        button.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        button.Pressed += action;
        row.AddChild(button);
        return button;
    }

    public override void _Process(double delta)
    {
        if (SimulationBridge.Instance?.Flight14Preview is not { } preview) return;
        if (preview.IsAdvancingToEntry && _shortcutViewport == null)
        {
            _shortcutViewport = GetViewport();
            _previousDisable3D = _shortcutViewport.Disable3D;
            _shortcutViewport.Disable3D = true;
        }
        else if (!preview.IsAdvancingToEntry) RestoreWorldView();
        _phaseBanner ??= GetTree().Root.FindChild("FlightPhaseBanner", true, false) as Control;
        // Work in this canvas's coordinates: global pixels are not local offsets at UI scale.
        var inverse = GetGlobalTransform().AffineInverse();
        float bannerBottom = _phaseBanner is { Visible: true }
            ? (inverse * _phaseBanner.GetGlobalRect().End).Y : 86;
        _controls.OffsetTop = Mathf.Max(96, bannerBottom + 10);
        _pause.Text = preview.IsAdvancingToEntry ? "CANCEL SKIP" : preview.IsPaused ? "RESUME" : "PAUSE";
        _entry.Disabled = !preview.CanAdvanceToEntry;
        _entry.TooltipText = preview.IsAdvancingToEntry
            ? "Advancing continuously to entry. CANCEL SKIP pauses at the current state."
            : preview.Run.ReturnController?.EntryInterface != null
                ? "The entry interface has already been reached."
                : "Available after all 26 satellites are deployed. Advances physics to entry and pauses; processing time depends on CPU speed.";
        _pause.Disabled = preview.IsStopped || preview.IsObservingBooster && preview.Run.BoosterReturnController is { IsStopped: true };
        _observe.Disabled = preview.IsAdvancingToEntry || preview.Run.BoosterReturnController?.Booster == null
            || !preview.IsObservingBooster && preview.Run.BoosterReturnController.Booster.IsDestroyed;
        _observe.Text = preview.IsObservingBooster ? "STARSHIP" : "SUPER HEAVY";
        string state = preview.IsTerminal ? preview.Run.PoweredReturnController?.Landing?.WaterEntryWitness != null
                ? "WATER RESPONSE OBSERVED · SEALED-HULL ESTIMATE · "
                    + (preview.Run.BoosterReturnController?.FlightTerminationTriggered == true
                        ? "BOOSTER FTS RECORDED" : "BOOSTER RETURN UNRESOLVED")
                : "100 M DIAGNOSTIC COMPLETE · WATER CONTACT PENDING"
            : preview.BlockReason != null ? "PREVIEW STOPPED · " + preview.BlockReason
            : preview.Run.Ship.IsDestroyed ? "VEHICLE LOST · RESTART TO EXPLORE AGAIN"
            : $"{preview.Phase.ToUpperInvariant()} · PAYLOADS {preview.Run.PayloadController.Releases.Count}/26"
                + (preview.IsPaused ? " · PAUSED" : " · [, .] TIME · DRAG / SCROLL CAMERA");
        if (preview.IsObservingBooster && preview.Run.BoosterReturnController is { } booster)
            state = "SUPER HEAVY · " + (booster.BlockReason ?? booster.Phase.ToString()).ToUpperInvariant()
                + (booster.FlightTerminationTriggered ? " · FTS RECORDED" : "")
                + (preview.IsPaused ? " · PAUSED" : " · [, .] TIME · DRAG / SCROLL CAMERA");
        if (preview.IsAdvancingToEntry)
        {
            var elapsed = TimeSpan.FromSeconds(preview.MissionElapsedSeconds);
            state = $"ADVANCING TO ENTRY · T+ {elapsed:hh\\:mm\\:ss} · CANCEL SKIP TO PAUSE";
        }
        _status.Text = "FLIGHT 14 / ENGINEERING EXPLORATION\n" + state;
    }
}
