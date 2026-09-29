namespace Exosphere.Game;

using Godot;

/// <summary>
/// Cinematic title. One action: start the Flight 7 stack at Starbase.
/// Campaign, VAB and the scenario library stay in <see cref="MainMenu"/> and are
/// not presented from this screen.
/// The black hole is a NASA still, not a drawing. Credits live in
/// res://assets/title/CREDITS.txt.
/// </summary>
public partial class TitleScreen : Control
{
    [Signal]
    public delegate void StartRequestedEventHandler();

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        BuildVoid();
        BuildBlackHole();
        BuildMark();
    }

    private void BuildVoid()
    {
        var voidFill = new ColorRect
        {
            Color = new Color(0.0f, 0.0f, 0.0f, 1f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        voidFill.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(voidFill);
    }

    private void BuildBlackHole()
    {
        var hole = new TextureRect
        {
            Texture = GD.Load<Texture2D>("res://assets/title/black_hole.jpg"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        hole.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(hole);
    }

    private void BuildMark()
    {
        var face = new FontVariation
        {
            BaseFont = GD.Load<Font>("res://assets/fonts/jost/Jost-Light.ttf"),
            SpacingGlyph = 4,
        };

        var column = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        column.SetAnchorsPreset(LayoutPreset.Center);
        column.OffsetLeft = -520;
        column.OffsetTop = -78;
        column.OffsetRight = 40;
        column.OffsetBottom = 78;
        column.AddThemeConstantOverride("separation", 26);
        AddChild(column);

        var word = new Label
        {
            Text = "EXOSPHERE",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        word.AddThemeFontOverride("font", face);
        word.CustomMinimumSize = new Vector2(620, 70);
        word.AddThemeFontSizeOverride("font_size", 46);
        word.AddThemeColorOverride("font_color", new Color(0.93f, 0.94f, 0.95f));
        column.AddChild(word);

        var start = new Button
        {
            Text = "INICIAR",
            CustomMinimumSize = new Vector2(156, 38),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            FocusMode = FocusModeEnum.All,
        };
        StyleStart(start, face);
        start.Pressed += () => EmitSignal(SignalName.StartRequested);
        column.AddChild(start);
        start.CallDeferred(Control.MethodName.GrabFocus);
    }

    private static void StyleStart(Button button, Font face)
    {
        var idle = Pill(new Color(1f, 1f, 1f, 0.72f));
        var hover = Pill(Colors.White);
        button.AddThemeStyleboxOverride("normal", idle);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", hover);
        button.AddThemeStyleboxOverride("focus", hover);
        button.AddThemeFontOverride("font", face);
        button.AddThemeFontSizeOverride("font_size", 14);
        button.AddThemeColorOverride("font_color", new Color(0.93f, 0.94f, 0.95f));
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", Colors.White);
        button.AddThemeColorOverride("font_focus_color", Colors.White);
    }

    private static StyleBoxFlat Pill(Color border) => new()
    {
        BgColor = new Color(0f, 0f, 0f, 0.2f),
        BorderColor = border,
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = 19,
        CornerRadiusTopRight = 19,
        CornerRadiusBottomRight = 19,
        CornerRadiusBottomLeft = 19,
        ContentMarginLeft = 26,
        ContentMarginRight = 26,
        ContentMarginTop = 6,
        ContentMarginBottom = 6,
    };
}
