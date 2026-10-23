namespace Exosphere.Game;

using Exosphere.Simulation.Training;
using Godot;

/// <summary>
/// The training drill's card: title, how to do it, and whether the goal is met. It replaces
/// the campaign objectives panel during training (no mission director runs there).
/// </summary>
public partial class TrainingPanel : PanelContainer
{
    private const double RefreshIntervalSeconds = 0.5;
    private Label _title = null!;
    private Label _instructions = null!;
    private Label _status = null!;
    private double _sinceRefresh = RefreshIntervalSeconds;
    private bool _completed;

    /// <summary>Density gate driven by <see cref="HUDController"/>; Clean hides it.</summary>
    public bool DensityAllowed { get; set; } = true;

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", InterfaceTheme.PanelStyle(0.88f, 14, 11));
        SetAnchorsPreset(LayoutPreset.TopLeft);
        CustomMinimumSize = new Vector2(360, 0);
        OffsetLeft = 18;
        OffsetTop = 312;
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 5);
        AddChild(content);
        _title = new Label();
        InterfaceTheme.ApplyBody(_title, 13);
        content.AddChild(_title);
        _instructions = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(332, 0) };
        InterfaceTheme.ApplyBody(_instructions, 11);
        _instructions.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        content.AddChild(_instructions);
        _status = new Label();
        InterfaceTheme.ApplyMono(_status, 11);
        content.AddChild(_status);
    }

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        var training = bridge?.ActiveTraining;
        var vessel = bridge?.ActiveVessel;
        Visible = DensityAllowed && training != null && vessel != null;
        if (!Visible || bridge == null || training == null || vessel == null) return;
        _sinceRefresh += delta;
        if (_sinceRefresh < RefreshIntervalSeconds) return;
        _sinceRefresh = 0.0;

        bool es = UserInterfaceSettings.Language == InterfaceLanguage.Spanish;
        _title.Text = (es ? "ENTRENAMIENTO · " : "TRAINING · ") + (es ? training.TitleEs : training.TitleEn).ToUpperInvariant();
        _instructions.Text = es ? training.InstructionsEs : training.InstructionsEn;
        if (!_completed && TrainingGoals.IsMet(training, bridge.Universe, vessel))
        {
            _completed = true;
            GD.Print($"[Training] {training.Id} complete.");
        }
        if (vessel.IsDestroyed)
        {
            _status.Text = es ? "NAVE PERDIDA · [Esc] MENÚ PARA REINTENTAR" : "VEHICLE LOST · [Esc] MENU TO RETRY";
            _status.AddThemeColorOverride("font_color", InterfaceTheme.Alert);
        }
        else if (_completed)
        {
            _status.Text = es ? "✓ ENTRENAMIENTO COMPLETADO" : "✓ TRAINING COMPLETE";
            _status.AddThemeColorOverride("font_color", InterfaceTheme.Success);
        }
        else
        {
            _status.Text = es ? "META EN CURSO…" : "GOAL IN PROGRESS…";
            _status.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        }
    }
}
