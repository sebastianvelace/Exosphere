namespace Exosphere.Game;

using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Campaign;
using Exosphere.Simulation.Persistence;
using Godot;

public partial class MainMenu : Control
{
    private VBoxContainer _navigation = null!;
    private PanelContainer _dossier = null!;
    private MarginContainer _bodyMargin = null!;
    private VBoxContainer _primaryColumn = null!;
    private Label _bodyTitle = null!;
    private Label _bodySubtitle = null!;
    private Control? _modal;
    private Button? _firstButton;

    public override void _Ready()
    {
        UserInterfaceSettings.Load();
        GetWindow().ContentScaleFactor = UserInterfaceSettings.UiScale;
        SetAnchorsPreset(LayoutPreset.FullRect);
        BuildBackground();
        BuildHeader();
        BuildBody();
        BuildFooter();
        ApplyEntrance();
        Resized += UpdateResponsiveLayout;
        UpdateResponsiveLayout();
        _firstButton?.CallDeferred(Control.MethodName.GrabFocus);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel") && _modal != null)
        {
            CloseModal();
            GetViewport().SetInputAsHandled();
        }
    }

    private void BuildBackground()
    {
        var background = new TextureRect
        {
            Name = "OrbitalEarth",
            Texture = GD.Load<Texture2D>("res://assets/textures/menu_orbital_dossier.png"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var leftScrim = new TextureRect
        {
            Name = "ReadabilityScrim",
            Texture = BuildScrimTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        leftScrim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(leftScrim);

        var trace = new OrbitalDossierTrace
        {
            Name = "CharacteristicOrbit",
            MouseFilter = MouseFilterEnum.Ignore,
            ReducedMotion = UserInterfaceSettings.ReducedMotion,
        };
        trace.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(trace);
    }

    private static GradientTexture2D BuildScrimTexture()
    {
        var gradient = new Gradient
        {
            Colors =
            [
                new Color(0.008f, 0.010f, 0.014f, 0.99f),
                new Color(0.008f, 0.010f, 0.014f, 0.92f),
                new Color(0.008f, 0.010f, 0.014f, 0.28f),
                new Color(0.008f, 0.010f, 0.014f, 0.08f),
            ],
            Offsets = [0f, 0.34f, 0.66f, 1f],
        };
        return new GradientTexture2D
        {
            Gradient = gradient,
            Width = 1024,
            Height = 2,
            FillFrom = Vector2.Zero,
            FillTo = Vector2.Right,
        };
    }

    private void BuildHeader()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.TopWide);
        margin.OffsetLeft = 54;
        margin.OffsetTop = 30;
        margin.OffsetRight = -54;
        AddChild(margin);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        margin.AddChild(row);

        var mark = new Label
        {
            Text = "EXO",
            CustomMinimumSize = new Vector2(48, 34),
            VerticalAlignment = VerticalAlignment.Center,
        };
        InterfaceTheme.ApplyDisplay(mark, 18);
        mark.AddThemeColorOverride("font_color", InterfaceTheme.Orbital);
        row.AddChild(mark);

        var brand = new Label
        {
            Text = "EXOSPHERE",
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        InterfaceTheme.ApplyBody(brand, 13, medium: true);
        brand.AddThemeColorOverride("font_color", InterfaceTheme.Text);
        row.AddChild(brand);

        var language = new Button
        {
            Text = UserInterfaceSettings.Language == InterfaceLanguage.English ? "ES" : "EN",
            CustomMinimumSize = new Vector2(52, 36),
            TooltipText = "English / Español",
        };
        InterfaceTheme.StyleDossierButton(language);
        language.CustomMinimumSize = new Vector2(52, 36);
        language.Pressed += ToggleLanguage;
        row.AddChild(language);
    }

    private void BuildBody()
    {
        _bodyMargin = new MarginContainer { Name = "BodyMargin" };
        _bodyMargin.SetAnchorsPreset(LayoutPreset.FullRect);
        _bodyMargin.OffsetLeft = 70;
        _bodyMargin.OffsetTop = 112;
        _bodyMargin.OffsetRight = -70;
        _bodyMargin.OffsetBottom = -74;
        AddChild(_bodyMargin);

        var split = new HBoxContainer();
        split.AddThemeConstantOverride("separation", 72);
        _bodyMargin.AddChild(split);

        _primaryColumn = new VBoxContainer
        {
            Name = "PrimaryNavigation",
            CustomMinimumSize = new Vector2(430, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _primaryColumn.AddThemeConstantOverride("separation", 16);
        split.AddChild(_primaryColumn);

        var classification = new Label { Text = UiText.Get("flight_operations") };
        InterfaceTheme.ApplyMono(classification, 11);
        classification.AddThemeColorOverride("font_color", InterfaceTheme.Orbital);
        _primaryColumn.AddChild(classification);

        _bodyTitle = new Label
        {
            Text = UiText.Get("dossier"),
            AutowrapMode = TextServer.AutowrapMode.Off,
        };
        InterfaceTheme.ApplyDisplay(_bodyTitle, 55);
        _bodyTitle.AddThemeColorOverride("font_color", InterfaceTheme.Text);
        _bodyTitle.AddThemeConstantOverride("line_spacing", -8);
        _primaryColumn.AddChild(_bodyTitle);

        _bodySubtitle = new Label
        {
            Text = UiText.Get("subtitle"),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(390, 54),
        };
        InterfaceTheme.ApplyBody(_bodySubtitle, 14);
        _bodySubtitle.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        _primaryColumn.AddChild(_bodySubtitle);

        _navigation = new VBoxContainer();
        _navigation.AddThemeConstantOverride("separation", 7);
        _primaryColumn.AddChild(_navigation);

        string[] saves = SaveSystem.ListSaveSlots();
        AddNavButton(UiText.Get("continue"), () => ContinueSave(saves), primary: true,
            disabled: saves.Length == 0,
            tooltip: saves.Length == 0 ? UiText.Get("no_saves") : "");
        AddNavButton(UiText.Get("campaign"), ShowCampaign);
        AddNavButton(UiText.Get("sandbox"), OpenSandbox);
        AddNavButton(UiText.Get("vab"),
            () => GetTree().ChangeSceneToFile("res://scenes/construction/Construction.tscn"));
        AddNavButton(UiText.Get("scenarios"), ShowScenarios);
        AddNavButton(UiText.Get("reentry"), OpenReentry);
        AddNavButton(UiText.Get("saves"), ShowSaves);
        AddNavButton(UiText.Get("settings"), ShowSettings);

        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        split.AddChild(spacer);

        _dossier = BuildDossier();
        split.AddChild(_dossier);
    }

    private void AddNavButton(
        string text,
        Action action,
        bool primary = false,
        bool disabled = false,
        string tooltip = "")
    {
        var button = new Button
        {
            Text = text,
            Alignment = HorizontalAlignment.Left,
            Disabled = disabled,
            TooltipText = tooltip,
            FocusMode = FocusModeEnum.All,
        };
        InterfaceTheme.StyleDossierButton(button, primary);
        button.CustomMinimumSize = new Vector2(360, 43);
        button.Pressed += action;
        _navigation.AddChild(button);
        _firstButton ??= button.Disabled ? null : button;
    }

    private static PanelContainer BuildDossier()
    {
        var panel = new PanelContainer
        {
            Name = "SelectedDossier",
            CustomMinimumSize = new Vector2(390, 410),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var style = InterfaceTheme.GlassPanel(0.88f, 0, 28, 25);
        style.BorderColor = new Color(InterfaceTheme.Orbital, 0.34f);
        panel.AddThemeStyleboxOverride("panel", style);

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 15);
        panel.AddChild(content);

        var label = new Label { Text = UiText.Get("mission") };
        InterfaceTheme.ApplyMono(label, 10);
        label.AddThemeColorOverride("font_color", InterfaceTheme.Orbital);
        content.AddChild(label);

        var mission = new Label { Text = UiText.Get("dossier_vehicle_title") };
        InterfaceTheme.ApplyDisplay(mission, 30);
        mission.AddThemeColorOverride("font_color", InterfaceTheme.Text);
        content.AddChild(mission);
        content.AddChild(Divider());
        content.AddChild(Metric(UiText.Get("vehicle"), UiText.Get("dossier_vehicle_value")));
        content.AddChild(Metric(UiText.Get("site"), UiText.Get("dossier_site_value")));
        content.AddChild(Metric(UiText.Get("objective"), UiText.Get("dossier_objective_value")));
        content.AddChild(Metric(UiText.Get("physics"), UiText.Get("dossier_physics_value")));
        content.AddChild(Divider());

        var note = new Label
        {
            Text = UiText.Get("dossier_note"),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        InterfaceTheme.ApplyBody(note, 12);
        note.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        content.AddChild(note);

        var profile = new Label { Text = UiText.Get("dossier_profile") };
        InterfaceTheme.ApplyMono(profile, 10);
        profile.AddThemeColorOverride("font_color", InterfaceTheme.TextFaint);
        content.AddChild(profile);
        return panel;
    }

    private void BuildFooter()
    {
        var footer = new MarginContainer();
        footer.SetAnchorsPreset(LayoutPreset.BottomWide);
        footer.OffsetLeft = 54;
        footer.OffsetRight = -54;
        footer.OffsetBottom = -26;
        footer.GrowVertical = GrowDirection.Begin;
        AddChild(footer);

        var row = new HBoxContainer();
        footer.AddChild(row);

        var note = new Label
        {
            Text = UiText.Get("footer_controls"),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        InterfaceTheme.ApplyMono(note, 10);
        note.AddThemeColorOverride("font_color", InterfaceTheme.TextFaint);
        row.AddChild(note);

        var status = new Label { Text = UiText.Get("physics_ready") };
        InterfaceTheme.ApplyMono(status, 10);
        status.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        row.AddChild(status);
    }

    private void ApplyEntrance()
    {
        if (UserInterfaceSettings.ReducedMotion) return;
        _navigation.Modulate = new Color(1, 1, 1, 0);
        _dossier.Modulate = new Color(1, 1, 1, 0);
        var tween = CreateTween().SetParallel();
        tween.SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tween.TweenProperty(_navigation, "modulate:a", 1.0f, 0.42f);
        tween.TweenProperty(_dossier, "modulate:a", 1.0f, 0.55f).SetDelay(0.08f);
    }

    private void ContinueSave(string[] saves)
    {
        if (saves.Length == 0) return;
        CraftLaunchRequest.Set(new LaunchIntent { Mode = "continue", SaveSlot = saves[^1] });
        OpenFlight();
    }

    private void OpenSandbox()
        => LaunchVehicleScenario(
            "starship_flight7_block2_2025.json",
            "starbase",
            "manual",
            mode: "sandbox");

    private void OpenReentry()
        => LaunchVehicleScenario(
            "starship_flight7_block2_2025.json",
            "starbase",
            "starship-reentry-70km");

    private void LaunchStarshipFlight7Scenario()
        => LaunchVehicleScenario(
            "starship_flight7_block2_2025.json",
            "starbase",
            "starship-flight7-ascent");

    private void LaunchStarshipFlight12Scenario()
        => LaunchVehicleScenario(
            "starship_flight12_v3_2026.json",
            "starbase_pad2",
            "starship-flight12-ascent");

    private void LaunchFalconScenario()
        => LaunchVehicleScenario(
            "falcon9_block5_standard_2025.json",
            "kennedy",
            "falcon9-block5-ascent");

    private void LaunchNewGlennScenario()
        => LaunchVehicleScenario(
            "newglenn_7x2_public_2026.json",
            "cape_canaveral_lc36",
            "newglenn-7x2-ascent");

    private void LaunchVehicleScenario(
        string variantFile,
        string launchSiteId,
        string flightProfileId,
        string mode = "scenario")
    {
        string data = ProjectSettings.GlobalizePath("res://data");
        var catalog = PartCatalog.LoadFromDirectory(System.IO.Path.Combine(data, "parts"));
        var variant = VehicleVariantDefinition.LoadFromJson(
            System.IO.Path.Combine(data, "vehicles", variantFile));
        var craft = variant.Build(catalog).ToCraftDocument(variant.Name);
        craft.VehicleVariantId = variant.Id;
        CraftLaunchRequest.Set(new LaunchIntent
        {
            Mode = mode,
            VehicleVariantId = variant.Id,
            LaunchSiteId = launchSiteId,
            FlightProfileId = flightProfileId,
            Craft = craft,
        });
        OpenFlight();
    }

    private void ShowScenarios() => ShowModal(UiText.Get("scenario_title"), body =>
    {
        body.AddChild(ModalButton("FALCON 9 BLOCK 5 / KENNEDY", LaunchFalconScenario, true));
        body.AddChild(ModalButton(
            "NEW GLENN 7x2 / CAPE CANAVERAL SLC-36",
            LaunchNewGlennScenario));
        body.AddChild(ModalButton(
            "STARSHIP FLIGHT 7 / SHIP 33 + BOOSTER 14",
            LaunchStarshipFlight7Scenario));
        body.AddChild(ModalButton(
            "STARSHIP FLIGHT 12 / V3 + RAPTOR 3",
            LaunchStarshipFlight12Scenario));
        body.AddChild(ModalButton("STARSHIP / 70 KM ENTRY INTERFACE", OpenReentry));
        body.AddChild(ModalButton("STARSHIP / STARBASE MANUAL LAUNCH", OpenSandbox));
    });

    private void ShowCampaign() => ShowModal(UiText.Get("campaign_preview"), body =>
    {
        string data = ProjectSettings.GlobalizePath("res://data");
        var campaignDefinition = CampaignDefinition.LoadFromJson(
            System.IO.Path.Combine(
                data, "campaigns", "historical_nasa_spacex.json"));
        var missionCatalog = MissionDefinitionCatalog.LoadFromDirectory(
            System.IO.Path.Combine(data, "missions"));
        CampaignSaveV2 state = SaveSystem.ReadMostRecentCampaignState();
        var service = new CampaignService(state, missionCatalog.Ordered);
        bool spanish =
            UserInterfaceSettings.Language == InterfaceLanguage.Spanish;

        var description = new Label
        {
            Text = spanish
                ? campaignDefinition.DescriptionEs
                : campaignDefinition.DescriptionEn,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        InterfaceTheme.ApplyBody(description, 12);
        description.AddThemeColorOverride(
            "font_color", InterfaceTheme.TextMuted);
        body.AddChild(description);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 500),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        body.AddChild(scroll);
        var list = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        list.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(list);

        foreach (var entry in campaignDefinition.Missions.OrderBy(item => item.Sequence))
        {
            MissionDefinition? mission = null;
            if (entry.DefinitionId != null
                && missionCatalog.Definitions.TryGetValue(
                    entry.DefinitionId, out var foundMission))
                mission = foundMission;
            bool hasDefinition = mission != null;
            string? variantFile = hasDefinition
                ? FindVehicleVariantFile(data, mission!.VehicleVariantId)
                : null;
            bool launchSiteExists = hasDefinition
                && System.IO.File.Exists(System.IO.Path.Combine(
                    data, "launch_sites", $"{mission!.LaunchSiteId}.json"));
            bool unlocked = hasDefinition && service.CanStart(mission!.Id);
            bool playable = unlocked && variantFile != null && launchSiteExists;
            string status = playable
                ? (spanish ? "LISTA" : "READY")
                : hasDefinition && !unlocked
                    ? (spanish ? "BLOQUEADA" : "LOCKED")
                    : hasDefinition
                        ? (spanish ? "VEHÍCULO PENDIENTE" : "VEHICLE PENDING")
                        : (spanish ? "PLANIFICADA" : "PLANNED");
            string buttonText =
                $"{entry.Sequence:00}  {entry.Title.ToUpperInvariant()}   /   {status}";
            MissionDefinition? selectedMission = hasDefinition ? mission : null;
            string? selectedVariantFile = variantFile;
            list.AddChild(ModalButton(
                buttonText,
                () =>
                {
                    if (selectedMission != null && selectedVariantFile != null)
                        LaunchCampaignMission(
                            selectedMission, selectedVariantFile, state);
                },
                primary: entry.Sequence == 1,
                disabled: !playable,
                tooltip: hasDefinition
                    ? (spanish ? mission!.SummaryEs : mission!.SummaryEn)
                    : (spanish
                        ? "La definición histórica llegará con su vehículo."
                        : "Historical definition arrives with its vehicle.")));
        }
    });

    private static string? FindVehicleVariantFile(
        string dataPath,
        string vehicleVariantId)
    {
        string directory = System.IO.Path.Combine(dataPath, "vehicles");
        foreach (string path in System.IO.Directory.GetFiles(directory, "*.json"))
        {
            try
            {
                if (string.Equals(
                        VehicleVariantDefinition.LoadFromJson(path).Id,
                        vehicleVariantId,
                        StringComparison.Ordinal))
                    return path;
            }
            catch
            {
                // The strict data tests report malformed variants with better context.
            }
        }
        return null;
    }

    private void LaunchCampaignMission(
        MissionDefinition mission,
        string variantPath,
        CampaignSaveV2 campaignState)
    {
        string data = ProjectSettings.GlobalizePath("res://data");
        var catalog = PartCatalog.LoadFromDirectory(
            System.IO.Path.Combine(data, "parts"));
        var variant = VehicleVariantDefinition.LoadFromJson(variantPath);
        var craft = variant.Build(catalog).ToCraftDocument(variant.Name);
        craft.VehicleVariantId = variant.Id;
        CraftLaunchRequest.Set(new LaunchIntent
        {
            Mode = "campaign",
            MissionId = mission.Id,
            VehicleVariantId = variant.Id,
            LaunchSiteId = mission.LaunchSiteId,
            FlightProfileId = mission.FlightProfileId,
            Craft = craft,
            CampaignState = campaignState,
        });
        OpenFlight();
    }

    private void ShowSaves() => ShowModal(UiText.Get("saves"), body =>
    {
        string[] saves = SaveSystem.ListSaveSlots();
        if (saves.Length == 0)
        {
            var empty = new Label { Text = UiText.Get("no_saves") };
            InterfaceTheme.ApplyBody(empty, 14);
            empty.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
            body.AddChild(empty);
            return;
        }
        foreach (string slot in saves)
        {
            string selected = slot;
            body.AddChild(ModalButton(selected.ToUpperInvariant(), () =>
            {
                CraftLaunchRequest.Set(new LaunchIntent { Mode = "continue", SaveSlot = selected });
                OpenFlight();
            }));
        }
    });

    private void ShowSettings() => ShowModal(UiText.Get("settings_title"), body =>
    {
        body.AddChild(SettingRow(UiText.Get("language"),
            UserInterfaceSettings.Language == InterfaceLanguage.English ? "ENGLISH" : "ESPAÑOL",
            ToggleLanguage));
        body.AddChild(SettingRow(UiText.Get("motion"),
            UserInterfaceSettings.ReducedMotion ? "ON" : "OFF", () =>
            {
                UserInterfaceSettings.SetReducedMotion(!UserInterfaceSettings.ReducedMotion);
                Rebuild();
            }));
        body.AddChild(SettingRow(UiText.Get("scale"),
            $"{UserInterfaceSettings.UiScale * 100:F0}%", () =>
            {
                float next = UserInterfaceSettings.UiScale switch
                {
                    < 1.24f => 1.25f,
                    < 1.49f => 1.5f,
                    _ => 1.0f,
                };
                UserInterfaceSettings.SetUiScale(next);
                GetWindow().ContentScaleFactor = next;
                Rebuild();
            }));
    });

    private static Control SettingRow(string label, string value, Action action)
    {
        var row = new HBoxContainer();
        var key = new Label { Text = label, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        InterfaceTheme.ApplyBody(key, 13);
        key.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        row.AddChild(key);
        row.AddChild(ModalButton(value, action));
        return row;
    }

    private void ShowModal(string titleText, Action<VBoxContainer> populate)
    {
        CloseModal();
        var shade = new ColorRect
        {
            Color = new Color(0.004f, 0.006f, 0.009f, 0.72f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        shade.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(shade);
        _modal = shade;

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        shade.AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(540, 0) };
        panel.AddThemeStyleboxOverride("panel", InterfaceTheme.GlassPanel(0.97f, 0, 28, 25));
        center.AddChild(panel);

        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 12);
        panel.AddChild(body);

        var title = new Label { Text = titleText.ToUpperInvariant() };
        InterfaceTheme.ApplyDisplay(title, 30);
        title.AddThemeColorOverride("font_color", InterfaceTheme.Text);
        body.AddChild(title);
        body.AddChild(Divider());
        populate(body);
        body.AddChild(Divider());
        var close = ModalButton(UiText.Get("close"), CloseModal);
        body.AddChild(close);
        close.CallDeferred(Control.MethodName.GrabFocus);
    }

    private static Button ModalButton(
        string text,
        Action action,
        bool primary = false,
        bool disabled = false,
        string tooltip = "")
    {
        var button = new Button
        {
            Text = text,
            Alignment = HorizontalAlignment.Left,
            Disabled = disabled,
            TooltipText = tooltip,
        };
        InterfaceTheme.StyleDossierButton(button, primary);
        button.CustomMinimumSize = new Vector2(320, 42);
        button.Pressed += action;
        return button;
    }

    private void CloseModal()
    {
        _modal?.QueueFree();
        _modal = null;
        _firstButton?.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void ToggleLanguage()
    {
        UserInterfaceSettings.SetLanguage(
            UserInterfaceSettings.Language == InterfaceLanguage.English
                ? InterfaceLanguage.Spanish : InterfaceLanguage.English);
        Rebuild();
    }

    private void Rebuild() => GetTree().ReloadCurrentScene();
    private void OpenFlight() => GetTree().ChangeSceneToFile("res://scenes/flight/Flight.tscn");

    private void UpdateResponsiveLayout()
    {
        if (_dossier == null || _bodyMargin == null || _primaryColumn == null) return;
        float effectiveWidth = Size.X / System.Math.Max(1.0f, UserInterfaceSettings.UiScale);
        float effectiveHeight = Size.Y / System.Math.Max(1.0f, UserInterfaceSettings.UiScale);
        bool compact = effectiveHeight < 820f || effectiveWidth < 1380f;
        bool narrow = effectiveWidth < 1380f;

        // At 1280×720 the dossier is intentionally removed, but the original
        // desktop spacing still left the last nav rows behind the footer. Compress
        // only the presentation chrome; hit targets remain at least 36 px high.
        _dossier.Visible = !narrow;
        _bodyMargin.OffsetLeft = narrow ? 54 : 70;
        _bodyMargin.OffsetTop = compact ? 86 : 112;
        _bodyMargin.OffsetRight = narrow ? -54 : -70;
        _bodyMargin.OffsetBottom = compact ? -50 : -74;
        _primaryColumn.AddThemeConstantOverride("separation", compact ? 8 : 16);
        _navigation.AddThemeConstantOverride("separation", compact ? 4 : 7);
        _bodyTitle.AddThemeFontSizeOverride("font_size", compact ? 46 : 55);
        _bodySubtitle.CustomMinimumSize = new Vector2(390, compact ? 40 : 54);
        foreach (Node child in _navigation.GetChildren())
        {
            if (child is Button button)
                button.CustomMinimumSize = new Vector2(360, compact ? 36 : 43);
        }
    }

    private static Control Divider() => new ColorRect
    {
        Color = InterfaceTheme.Edge,
        CustomMinimumSize = new Vector2(0, 1),
        MouseFilter = MouseFilterEnum.Ignore,
    };

    private static Control Metric(string label, string value)
    {
        var row = new HBoxContainer();
        var key = new Label { Text = label, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        InterfaceTheme.ApplyMono(key, 10);
        key.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        row.AddChild(key);
        var val = new Label { Text = value, HorizontalAlignment = HorizontalAlignment.Right };
        InterfaceTheme.ApplyMono(val, 10);
        val.AddThemeColorOverride("font_color", InterfaceTheme.Text);
        row.AddChild(val);
        return row;
    }
}

public partial class OrbitalDossierTrace : Control
{
    public bool ReducedMotion { get; set; }
    private float _phase;

    public override void _Process(double delta)
    {
        if (ReducedMotion) return;
        _phase = (_phase + (float)delta * 0.08f) % 1f;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var center = new Vector2(Size.X * 0.79f, Size.Y * 0.51f);
        var points = new Vector2[121];
        float rx = Size.X * 0.235f;
        float ry = Size.Y * 0.115f;
        float tilt = -0.28f;
        for (int i = 0; i < points.Length; i++)
        {
            float angle = Mathf.Tau * i / (points.Length - 1);
            var local = new Vector2(Mathf.Cos(angle) * rx, Mathf.Sin(angle) * ry);
            points[i] = center + local.Rotated(tilt);
        }
        DrawPolyline(points, new Color(InterfaceTheme.Orbital, 0.46f), 1.15f, true);

        int marker = Mathf.Clamp((int)(_phase * (points.Length - 1)), 0, points.Length - 1);
        DrawCircle(points[marker], 3.2f, InterfaceTheme.Orbital);
        DrawCircle(points[marker], 7.5f, new Color(InterfaceTheme.Orbital, 0.14f));
    }
}
