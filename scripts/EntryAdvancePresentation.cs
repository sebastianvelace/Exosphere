namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation.Flight;

/// <summary>Lightweight 2D presentation while continuous entry propagation suspends 3D.</summary>
public partial class EntryAdvancePresentation : Control
{
    private CanvasLayer _backdrop = null!;
    private TextureRect _snapshot = null!;
    private PanelContainer _card = null!;
    private Label _clock = null!, _phase = null!;
    private readonly Label[] _steps = new Label[3];

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _backdrop = new CanvasLayer { Name = "EntryAdvanceBackdrop", Layer = 0, Visible = false };
        AddChild(_backdrop);
        var background = new ColorRect { Color = InterfaceTheme.Void, MouseFilter = MouseFilterEnum.Ignore };
        _backdrop.AddChild(background);
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _snapshot = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            Modulate = new Color(0.16f, 0.16f, 0.16f), MouseFilter = MouseFilterEnum.Ignore,
        };
        _backdrop.AddChild(_snapshot);
        _snapshot.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _card = new PanelContainer { Name = "EntryAdvanceCard", Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _card.AddThemeStyleboxOverride("panel", InterfaceTheme.PanelStyle(0.98f, 24, 18, true));
        AddChild(_card);
        _card.SetAnchorsPreset(LayoutPreset.CenterTop);
        _card.GrowHorizontal = GrowDirection.Both;
        var content = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        content.AddThemeConstantOverride("separation", 10);
        _card.AddChild(content);
        Label Caption(string text, int size)
        {
            var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
            InterfaceTheme.ApplyLabel(label, size);
            content.AddChild(label);
            return label;
        }
        Caption("ADVANCING TO ENTRY", 22);
        _clock = Caption("", 30);
        InterfaceTheme.ApplyMono(_clock, 30);
        _phase = Caption("", 13);
        var steps = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        steps.AddThemeConstantOverride("separation", 24);
        content.AddChild(steps);
        string[] names = ["01  COAST", "02  DEORBIT", "03  ENTRY"];
        for (int i = 0; i < names.Length; i++)
        {
            _steps[i] = new Label { Text = names[i], MouseFilter = MouseFilterEnum.Ignore };
            InterfaceTheme.ApplyLabel(_steps[i], 13);
            steps.AddChild(_steps[i]);
        }
        Caption("Automatic pause at entry · CANCEL SKIP to pause now", 12);
        Caption("FLIGHT VIEW HELD · LIVE TELEMETRY", 11).Modulate = InterfaceTheme.TextMuted;
    }

    public void CaptureBackground()
    {
        // Render one clean world frame and read it back once per request, never
        // in the advancing loop. Exclude the HUD so resizing cannot duplicate it.
        _snapshot.Texture = null;
        if (DisplayServer.GetName() == "headless") return;
        CanvasLayer? ui = null;
        for (var parent = GetParent(); parent != null; parent = parent.GetParent())
            if (parent is CanvasLayer layer) { ui = layer; break; }
        bool wasVisible = ui?.Visible ?? false;
        try
        {
            if (ui != null) ui.Visible = false;
            RenderingServer.ForceDraw(false);
            using var image = GetViewport().GetTexture().GetImage();
            if (image == null || image.IsEmpty()) return;
            int height = Mathf.Max(1, Mathf.RoundToInt(640f * image.GetHeight() / image.GetWidth()));
            image.Resize(640, height, Image.Interpolation.Bilinear);
            _snapshot.Texture = ImageTexture.CreateFromImage(image);
        }
        finally
        {
            if (ui != null) ui.Visible = wasVisible;
        }
    }

    public void ShowProgress(Flight14Exploration preview, Vector2 controlsEnd)
    {
        _backdrop.Visible = _card.Visible = true;
        var elapsed = TimeSpan.FromSeconds(preview.MissionElapsedSeconds);
        _clock.Text = $"T+ {elapsed:hh\\:mm\\:ss}";
        var phase = preview.Run.ReturnController?.Phase;
        int step = phase is Flight14ReturnPhase.AligningDeorbit or Flight14ReturnPhase.DeorbitBurn ? 1
            : phase is Flight14ReturnPhase.EntryCoast or Flight14ReturnPhase.AtmosphericEntry ? 2 : 0;
        _phase.Text = step == 0 ? "ORBITAL COAST" : step == 1 ? "DEORBIT MANEUVER" : "APPROACHING ENTRY";
        for (int i = 0; i < _steps.Length; i++)
            _steps[i].Modulate = i == step ? InterfaceTheme.Text : i < step ? InterfaceTheme.Success : InterfaceTheme.TextFaint;
        float width = Mathf.Min(500, Size.X - 32);
        _card.OffsetLeft = -width / 2;
        _card.OffsetRight = width / 2;
        // Keep the card between the controls and the broadcast band, including
        // the taller alert layout. The root uses logical canvas coordinates.
        float top = controlsEnd.Y + 20;
        float available = Size.Y - 160 - top;
        _card.OffsetTop = top + Mathf.Max(0, (available - _card.GetCombinedMinimumSize().Y) / 2);
    }

    public void HideProgress()
    {
        if (!IsNodeReady()) return;
        _backdrop.Visible = _card.Visible = false;
        _snapshot.Texture = null;
    }
}
