namespace Exosphere.Game;

using Godot;

public partial class MainMenu : Control
{
    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        BuildBackground();
        BuildHeader();
        BuildHero();
        BuildMissionCard();
        BuildFooter();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter })
            OpenFlight();
    }

    private void BuildBackground()
    {
        var background = new TextureRect
        {
            Texture = GD.Load<Texture2D>("res://assets/textures/starmap_milkyway_8k.jpg"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        background.Modulate = new Color(0.47f, 0.50f, 0.58f, 1f);
        AddChild(background);

        var scrim = new ColorRect
        {
            Color = new Color(0.01f, 0.012f, 0.018f, 0.58f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        scrim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(scrim);

    }

    private void BuildHeader()
    {
        var header = new MarginContainer();
        header.SetAnchorsPreset(LayoutPreset.TopWide);
        header.OffsetLeft = 48;
        header.OffsetTop = 34;
        header.OffsetRight = -48;
        AddChild(header);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        header.AddChild(row);

        var mark = new PanelContainer
        {
            CustomMinimumSize = new Vector2(34, 34),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var markStyle = InterfaceTheme.GlassPanel(0.72f, 10, 0, 0);
        markStyle.BorderColor = InterfaceTheme.EdgeStrong;
        mark.AddThemeStyleboxOverride("panel", markStyle);
        row.AddChild(mark);

        var markText = new Label { Text = "E", HorizontalAlignment = HorizontalAlignment.Center };
        markText.AddThemeFontSizeOverride("font_size", 15);
        markText.AddThemeColorOverride("font_color", InterfaceTheme.Text);
        mark.AddChild(markText);

        var brand = new Label
        {
            Text = "EXOSPHERE",
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        brand.AddThemeFontSizeOverride("font_size", 14);
        brand.AddThemeColorOverride("font_color", InterfaceTheme.Text);
        row.AddChild(brand);

        var build = new Label
        {
            Text = "SOLAR SYSTEM SIMULATION",
            VerticalAlignment = VerticalAlignment.Center,
        };
        build.AddThemeFontSizeOverride("font_size", 11);
        build.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        row.AddChild(build);
    }

    private void BuildHero()
    {
        var hero = new VBoxContainer();
        hero.SetAnchorsPreset(LayoutPreset.CenterLeft);
        hero.OffsetLeft = 118;
        hero.OffsetTop = -225;
        hero.OffsetRight = 690;
        hero.OffsetBottom = 250;
        hero.AddThemeConstantOverride("separation", 18);
        AddChild(hero);

        var eyebrow = new Label { Text = "FLIGHT SYSTEMS ONLINE" };
        eyebrow.AddThemeFontSizeOverride("font_size", 12);
        eyebrow.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        hero.AddChild(eyebrow);

        var title = new Label
        {
            Text = "BEYOND\nTHE HORIZON",
            AutowrapMode = TextServer.AutowrapMode.Off,
        };
        title.AddThemeFontSizeOverride("font_size", 62);
        title.AddThemeColorOverride("font_color", InterfaceTheme.Text);
        title.AddThemeConstantOverride("line_spacing", -8);
        hero.AddChild(title);

        var description = new Label
        {
            Text = "Build, launch and navigate through a physically simulated solar system.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(430, 0),
        };
        description.AddThemeFontSizeOverride("font_size", 16);
        description.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        description.AddThemeConstantOverride("line_spacing", 5);
        hero.AddChild(description);

        var actions = new VBoxContainer();
        actions.AddThemeConstantOverride("separation", 10);
        hero.AddChild(actions);

        var flight = new Button { Text = "START FLIGHT" };
        InterfaceTheme.StyleButton(flight, primary: true);
        flight.Pressed += OpenFlight;
        actions.AddChild(flight);

        var assembly = new Button { Text = "VEHICLE ASSEMBLY" };
        InterfaceTheme.StyleButton(assembly);
        assembly.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/construction/Construction.tscn");
        actions.AddChild(assembly);

        var quit = new Button { Text = "QUIT" };
        InterfaceTheme.StyleButton(quit);
        quit.Pressed += () => GetTree().Quit();
        actions.AddChild(quit);
    }

    private void BuildMissionCard()
    {
        var card = new PanelContainer();
        card.SetAnchorsPreset(LayoutPreset.CenterRight);
        card.GrowHorizontal = GrowDirection.Begin;
        card.OffsetLeft = -442;
        card.OffsetTop = -128;
        card.OffsetRight = -88;
        card.OffsetBottom = 198;
        card.AddThemeStyleboxOverride("panel", InterfaceTheme.GlassPanel(0.72f, 14, 22, 20));
        AddChild(card);

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 14);
        card.AddChild(content);

        var title = new Label { Text = "CURRENT VEHICLE" };
        title.AddThemeFontSizeOverride("font_size", 11);
        title.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        content.AddChild(title);

        var vessel = new Label { Text = "STARSHIP IFT-7" };
        vessel.AddThemeFontSizeOverride("font_size", 25);
        vessel.AddThemeColorOverride("font_color", InterfaceTheme.Text);
        content.AddChild(vessel);

        content.AddChild(Divider());
        content.AddChild(Metric("LAUNCH SITE", "STARBASE"));
        content.AddChild(Metric("DESTINATION", "LOW EARTH ORBIT"));
        content.AddChild(Metric("SIMULATION", "REAL-TIME PHYSICS"));
        content.AddChild(Divider());

        var controls = new Label
        {
            Text = "Pad: [L] auto sequence  ·  [hold Z] manual ignition\nFlight: [G] guidance  ·  [H] pitch assist  ·  [M] map",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        controls.AddThemeFontSizeOverride("font_size", 11);
        controls.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        content.AddChild(controls);

        var hint = new Label { Text = "Press Enter to begin flight" };
        hint.AddThemeFontSizeOverride("font_size", 12);
        hint.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        content.AddChild(hint);
    }

    private void BuildFooter()
    {
        var footer = new MarginContainer();
        footer.SetAnchorsPreset(LayoutPreset.BottomWide);
        footer.OffsetLeft = 48;
        footer.OffsetRight = -48;
        footer.OffsetBottom = -32;
        footer.GrowVertical = GrowDirection.Begin;
        AddChild(footer);

        var row = new HBoxContainer();
        footer.AddChild(row);

        var note = new Label
        {
            Text = "NEW FLIGHT  /  ASSEMBLY  /  EXIT",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        note.AddThemeFontSizeOverride("font_size", 10);
        note.AddThemeColorOverride("font_color", InterfaceTheme.TextFaint);
        row.AddChild(note);

        var status = new Label { Text = "READY" };
        status.AddThemeFontSizeOverride("font_size", 10);
        status.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        row.AddChild(status);
    }

    private static Control Divider()
    {
        return new ColorRect
        {
            Color = InterfaceTheme.Edge,
            CustomMinimumSize = new Vector2(0, 1),
            MouseFilter = MouseFilterEnum.Ignore,
        };
    }

    private static Control Metric(string label, string value)
    {
        var row = new HBoxContainer();
        var key = new Label { Text = label, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        key.AddThemeFontSizeOverride("font_size", 11);
        key.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        row.AddChild(key);

        var val = new Label { Text = value, HorizontalAlignment = HorizontalAlignment.Right };
        val.AddThemeFontSizeOverride("font_size", 12);
        val.AddThemeColorOverride("font_color", InterfaceTheme.Text);
        row.AddChild(val);
        return row;
    }

    private void OpenFlight() =>
        GetTree().ChangeSceneToFile("res://scenes/flight/Flight.tscn");
}
