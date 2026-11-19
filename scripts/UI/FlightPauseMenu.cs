namespace Exosphere.Game;

using Godot;

/// <summary>Owns in-flight interruptions while the rest of the scene is paused.</summary>
public partial class FlightPauseMenu : CanvasLayer
{
    public const float HudTopInset = 44;
    private Control _overlay = null!;
    private PanelContainer _panel = null!;
    private Label _title = null!;
    private Label _context = null!;
    private Label _status = null!;
    private ScrollContainer _scroll = null!;
    private VBoxContainer _body = null!;
    private readonly List<Button> _buttons = new();
    private bool _previousPause;
    private bool _showingControls;
    private Control? _previousFocus;

    public override void _Ready()
    {
        Layer = 20;
        ProcessMode = ProcessModeEnum.Always;
        var launcher = new Button
        {
            Name = "PauseFlight",
            Text = T("Ⅱ  FLIGHT MENU", "Ⅱ  MENÚ DE VUELO"),
            TooltipText = T("Pause flight (Esc)", "Pausar el vuelo (Esc)"),
            FocusMode = Control.FocusModeEnum.None,
        };
        InterfaceTheme.StyleButton(launcher, minSize: new Vector2(132, 30), paddingX: 12, paddingY: 4);
        launcher.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        launcher.GrowHorizontal = Control.GrowDirection.Begin;
        launcher.OffsetRight = -20;
        launcher.OffsetTop = 6;
        launcher.Pressed += () => Open(ShowMenu);
        AddChild(launcher);

        _overlay = new ColorRect
        {
            Name = "PauseOverlay",
            Color = new Color(0.009f, 0.015f, 0.024f, 0.64f),
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,
        };
        _overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_overlay);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _overlay.AddChild(center);

        _panel = new PanelContainer();
        var panelStyle = InterfaceTheme.PanelStyle(0.98f, 22, 18, deep: true);
        panelStyle.BorderColor = InterfaceTheme.EdgeStrong;
        _panel.AddThemeStyleboxOverride("panel", panelStyle);
        center.AddChild(_panel);
        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 9);
        _panel.AddChild(layout);

        _title = new Label { Name = "PauseTitle" };
        InterfaceTheme.ApplyDisplay(_title, 34);
        layout.AddChild(_title);
        _context = CopyLabel();
        layout.AddChild(_context);
        layout.AddChild(new HSeparator());

        _scroll = new ScrollContainer
        {
            Name = "PauseScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
        };
        layout.AddChild(_scroll);
        _body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", 7);
        _scroll.AddChild(_body);
        _status = CopyLabel();
        _status.Hide();
        layout.AddChild(_status);
        GetViewport().SizeChanged += UpdateLayout;
        UpdateLayout();
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= UpdateLayout;
        if (IsInstanceValid(_overlay) && _overlay.Visible)
            GetTree().Paused = _previousPause;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            if (_overlay.Visible && @event.IsActionPressed("ui_cancel"))
            {
                GetViewport().SetInputAsHandled();
                Close();
            }
            return;
        }

        if (key.Keycode == Key.Escape)
        {
            GetViewport().SetInputAsHandled();
            if (_overlay.Visible) Close();
            else Open(ShowMenu);
        }
        else if (key.Keycode == Key.F1)
        {
            GetViewport().SetInputAsHandled();
            if (_overlay.Visible && _showingControls) Close();
            else Open(ShowControls);
        }
        else if (_showingControls && _overlay.Visible && key.Keycode is Key.Pageup or Key.Pagedown)
        {
            _scroll.ScrollVertical += (int)_scroll.Size.Y * (key.Keycode == Key.Pageup ? -1 : 1);
            GetViewport().SetInputAsHandled();
        }
    }

    private void Open(Action showPage)
    {
        if (!_overlay.Visible)
        {
            _previousPause = GetTree().Paused;
            _previousFocus = GetViewport().GuiGetFocusOwner();
            GetTree().Paused = true;
            _overlay.Show();
        }
        showPage();
    }

    private void Close()
    {
        _overlay.Hide();
        GetTree().Paused = _previousPause;
        if (IsInstanceValid(_previousFocus) && _previousFocus!.IsVisibleInTree())
            _previousFocus.GrabFocus();
        _previousFocus = null;
    }

    private void ShowMenu()
    {
        var bridge = SimulationBridge.Instance;
        var training = bridge?.ActiveTraining;
        string context = training != null ? T(training.TitleEn, training.TitleEs) : bridge?.ActiveVessel?.Name ?? "";
        BeginPage(T("Flight paused", "Vuelo en pausa"), context);
        AddButton(T("Resume flight", "Reanudar vuelo"), Close, primary: true);
        AddButton(T("Flight controls", "Controles de vuelo"), ShowControls);
        if (bridge?.ActiveVessel != null && !bridge.IsGuidedStarshipExploration && bridge.ActiveTraining == null)
        {
            AddButton(T("Quick save", "Guardado rápido"), SaveFlight);
            if (MissionCheckpoints.Exists)
                AddButton(T("Restore last checkpoint", "Volver al último punto de control"), ConfirmCheckpoint);
        }
        AddButton(T("Restart flight", "Reiniciar vuelo"), () => ConfirmTransition(restart: true));
        AddButton(T("Return to main menu", "Volver al menú principal"), () => ConfirmTransition(restart: false));
        EndPage();
    }

    private void ShowControls()
    {
        var bridge = SimulationBridge.Instance;
        bool guided = bridge?.IsGuidedStarshipExploration == true;
        BeginPage(T("Flight controls", "Controles de vuelo"), guided
            ? T("Guided exploration", "Exploración guiada")
            : (bridge?.ActiveTraining is { } training ? T(training.TitleEn, training.TitleEs) : null) ?? T("Manual flight and mission assistance", "Vuelo manual y asistencia de misión"));
        _showingControls = true;
        AddButton(T("Back to flight menu", "Volver al menú de vuelo"), ShowMenu, primary: true);

        AddGroup(T("View and time", "Vista y tiempo"),
            ("Esc / F1", T("Return to flight", "Volver al vuelo")),
            ("M", T("Orbital map", "Mapa orbital")),
            ("C", T("Camera preset or cockpit", "Cambiar cámara o cabina")),
            (T("Right drag / Wheel", "Arrastre dcho. / Rueda"), T("Orbit camera / Zoom", "Girar cámara / Zoom")),
            (",  .", T("Decrease / Increase time warp", "Reducir / Aumentar la aceleración del tiempo")),
            ("F3", T("Change HUD density", "Cambiar la densidad de la interfaz")));

        if (!guided)
        {
            AddGroup(T("Pilot", "Pilotaje"),
                ("W S / A D / Q E", T("Pitch / Yaw / Roll", "Cabeceo / Guiñada / Alabeo")),
                ("Z / X", T("Hold to ignite or raise / lower throttle", "Mantén pulsada la tecla para encender o subir / bajar el acelerador")),
                (T("Space", "Espacio"), T("Separate stage", "Separar etapa")),
                ("T / Y / Shift+Y", T("Toggle SAS / Next or previous hold mode", "Activar SAS / Modo de orientación siguiente o anterior")),
                (T("Arrows / PgUp PgDn", "Flechas / RePág AvPág"), T("Translate with RCS (hold Shift for fine control)", "Trasladar con RCS (mantén Shift para control fino)")),
                ("P", T("Arm or disarm parachutes (if installed)", "Armar o desarmar paracaídas (si están instalados)")));
            AddGroup(T("Mission and recovery", "Misión y recuperación"),
                ("L / G / H", T("Automatic countdown / Ascent guidance / Pitch assist", "Cuenta automática / Guiado de ascenso / Asistencia de cabeceo")),
                ("K", T("Toggle Mission Control on historical flights", "Delegar o recuperar el control en vuelos históricos")),
                ("F5 / F9", T("Quick save / Quick load", "Guardado rápido / Carga rápida")),
                ("F6", T("Restore last mission checkpoint", "Volver al último punto de control de misión")),
                ("F2", T("Acknowledge alert", "Confirmar alerta")));
            AddGroup(T("Orbital map", "Mapa orbital"),
                ("Tab / 1…6", T("Switch map range / Select transfer target", "Cambiar escala del mapa / Seleccionar destino")),
                ("B / N / Enter", T("Plan a burn / Warp to node / Execute node", "Planear un quemado / Acelerar al nodo / Ejecutar nodo")),
                (T("Wheel / Ctrl / Alt", "Rueda / Ctrl / Alt"), T("Adjust node Δv axes", "Ajustar ejes de Δv del nodo")),
                ("[ ] / Del", T("Fine-adjust or clear node", "Ajustar con precisión o borrar el nodo")));
            AddGroup(T("Practice shortcuts", "Atajos de práctica"),
                ("O / R / V", T("Jump to orbit / Reentry demo / Vehicle assembly", "Saltar a órbita / Demo de reentrada / Ensamblar vehículo")),
                ("J", T("Jump to a body from the map", "Saltar a un cuerpo desde el mapa")));
        }
        EndPage();
    }

    private void ConfirmTransition(bool restart)
    {
        BeginPage(restart ? T("Restart flight?", "¿Reiniciar el vuelo?") : T("Leave this flight?", "¿Salir del vuelo?"),
            T("Progress since your last save will be lost.", "Se perderá el progreso posterior al último guardado."));
        AddButton(T("Keep flying", "Seguir en el vuelo"), ShowMenu, primary: true);
        AddButton(restart ? T("Restart flight", "Reiniciar vuelo") : T("Return to main menu", "Volver al menú principal"),
            () => ChangeScene(restart));
        EndPage();
    }

    private void ConfirmCheckpoint()
    {
        BeginPage(T("Restore checkpoint?", "¿Volver al punto de control?"),
            T("Progress after this checkpoint will be lost.", "Se perderá el progreso posterior a este punto de control."));
        AddButton(T("Keep flying", "Seguir en el vuelo"), ShowMenu, primary: true);
        AddButton(T("Restore last checkpoint", "Volver al último punto de control"), () =>
        {
            if (!MissionCheckpoints.Exists)
            {
                SetStatus(T("No checkpoint is available.", "No hay ningún punto de control disponible."));
                return;
            }
            Close();
            MissionCheckpoints.Revert(GetTree());
        });
        EndPage();
    }

    private void ChangeScene(bool restart)
    {
        if (restart && SimulationBridge.Instance?.InitialLaunchIntent is { } intent)
            CraftLaunchRequest.Set(intent);
        var tree = GetTree();
        Close();
        var result = restart ? tree.ReloadCurrentScene() : tree.ChangeSceneToFile("res://scenes/ui/MainMenu.tscn");
        if (result == Error.Ok) return;
        if (restart) CraftLaunchRequest.Pop();
        Open(ShowMenu);
        SetStatus(T("The scene could not be opened.", "No se pudo abrir la escena."));
    }

    private void SaveFlight()
    {
        try
        {
            SaveSystem.SaveGame("quicksave");
            SetStatus(T("Flight saved. Use F9 to load it.", "Vuelo guardado. Pulsa F9 para cargarlo."));
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        {
            GD.PushWarning(error.Message);
            SetStatus(T("Quick save failed.", "No se pudo guardar el vuelo."));
        }
    }

    private void BeginPage(string title, string context)
    {
        _showingControls = false;
        foreach (var child in _body.GetChildren())
        {
            _body.RemoveChild(child);
            child.QueueFree();
        }
        _buttons.Clear();
        _title.Text = title;
        _context.Text = context;
        _context.Visible = context.Length > 0;
        _status.Hide();
        _scroll.ScrollVertical = 0;
    }

    private void EndPage()
    {
        UpdateLayout();
        for (int index = 0; index < _buttons.Count; index++)
        {
            var button = _buttons[index];
            var previous = button.GetPathTo(_buttons[(index + _buttons.Count - 1) % _buttons.Count]);
            var next = button.GetPathTo(_buttons[(index + 1) % _buttons.Count]);
            button.FocusPrevious = button.FocusNeighborTop = previous;
            button.FocusNext = button.FocusNeighborBottom = next;
            button.FocusNeighborLeft = button.FocusNeighborRight = button.GetPathTo(button);
        }
        _buttons[0].GrabFocus();
    }

    private void UpdateLayout()
    {
        var size = GetViewport().GetVisibleRect().Size;
        _scroll.CustomMinimumSize = new Vector2(
            Mathf.Clamp(size.X - 80, 250, 460),
            Mathf.Min(Mathf.Max(size.Y - 190, 120), _showingControls ? 415 : _buttons.Count * 44 + 8));
    }

    private void AddButton(string text, Action action, bool primary = false)
    {
        var button = new Button { Text = text, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        InterfaceTheme.StyleButton(button, primary, minSize: new Vector2(0, 36), paddingX: 12, paddingY: 6);
        button.Pressed += action;
        _body.AddChild(button);
        _buttons.Add(button);
    }

    private void AddGroup(string title, params (string Key, string Action)[] actions)
    {
        var heading = new Label { Text = title.ToUpperInvariant() };
        InterfaceTheme.ApplyLabel(heading, 15);
        heading.AddThemeColorOverride("font_color", InterfaceTheme.Text);
        _body.AddChild(heading);
        foreach (var (key, action) in actions)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);
            _body.AddChild(row);
            var binding = CopyLabel();
            binding.Text = key;
            InterfaceTheme.ApplyMono(binding, 12);
            binding.CustomMinimumSize = new Vector2(142, 0);
            row.AddChild(binding);
            var description = CopyLabel();
            description.Text = action;
            description.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(description);
        }
    }

    private void SetStatus(string message)
    {
        _status.Text = message;
        _status.Show();
    }

    private static Label CopyLabel()
    {
        var label = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        InterfaceTheme.ApplyBody(label, 13);
        label.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        return label;
    }

    private static string T(string english, string spanish) =>
        UserInterfaceSettings.Language == InterfaceLanguage.Spanish ? spanish : english;
}
