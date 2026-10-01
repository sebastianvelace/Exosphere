namespace Exosphere.Game;

using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Campaign;
using Exosphere.Simulation.Persistence;
using Godot;

public partial class MainMenu : Control
{
    private GridContainer _navigation = null!;
    private PanelContainer _dossier = null!;
    private MarginContainer _bodyMargin = null!;
    private VBoxContainer _primaryColumn = null!;
    private Label _bodyTitle = null!;
    private Label _bodySubtitle = null!;
    private Control? _modal;
    private Button? _firstButton;
    private Control? _returnFocus;
    private readonly Dictionary<Control, FocusModeEnum> _suspendedFocus = new();
    private ScrollContainer _bodyScroll = null!;
    private PanelContainer? _modalPanel;
    private ScrollContainer? _modalScroll;
    private Button? _flight14Button;

    public override void _Ready()
    {
        UserInterfaceSettings.Load();
        GetWindow().ContentScaleFactor = UserInterfaceSettings.UiScale;
        SetAnchorsPreset(LayoutPreset.FullRect);
        BuildBackground();
        BuildHeader();
        BuildBody();
        BuildFooter();
        Resized += UpdateResponsiveLayout;
        UpdateResponsiveLayout();
        ApplyEntrance();
        FocusHome();
        MaybeAutoSmokeFlight();
    }

    private async void FocusHome()
    {
        var tree = GetTree();
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        if (!IsInstanceValid(this)) return;
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        if (!IsInstanceValid(this) || !IsInsideTree() || _modal != null) return;
        _firstButton?.GrabFocus();
        _bodyScroll.ScrollVertical = 0;
    }

    /// <summary>
    /// Private-test export smoke: <c>--exo-smoke-flight</c> opens sandbox Flight so
    /// logs show whether the loose <c>data/</c> tree beside the executable loads.
    /// </summary>
    private void MaybeAutoSmokeFlight()
    {
        bool requested = false;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--exo-smoke-flight")
            {
                requested = true;
                break;
            }
        }
        if (!requested)
        {
            foreach (string arg in OS.GetCmdlineArgs())
            {
                if (arg == "--exo-smoke-flight")
                {
                    requested = true;
                    break;
                }
            }
        }
        if (!requested)
            return;
        GD.Print("[ExportSmoke] --exo-smoke-flight: opening sandbox");
        CallDeferred(nameof(OpenSandbox));
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
        InterfaceTheme.ApplyLabel(mark, 19);
        mark.AddThemeColorOverride("font_color", InterfaceTheme.Text);
        row.AddChild(mark);

        var brand = new Label
        {
            Text = "EXOSPHERE",
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        InterfaceTheme.ApplyLabel(brand, 14);
        brand.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        row.AddChild(brand);

        var systemStatus = new Label
        {
            Text = UiText.Get("flight_operations"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        InterfaceTheme.ApplyMono(systemStatus, 10);
        systemStatus.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        row.AddChild(systemStatus);

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

        _bodyScroll = new ScrollContainer
        {
            Name = "HomeScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
        };
        _bodyMargin.AddChild(_bodyScroll);
        var split = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        split.AddThemeConstantOverride("separation", 48);
        _bodyScroll.AddChild(split);

        _primaryColumn = new VBoxContainer
        {
            Name = "PrimaryNavigation",
            CustomMinimumSize = new Vector2(430, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _primaryColumn.AddThemeConstantOverride("separation", 16);
        split.AddChild(_primaryColumn);

        var classification = new Label { Text = UiText.Get("flight14_label") + "  /  " + UiText.Get("in_development") };
        InterfaceTheme.ApplyLabel(classification, 13);
        classification.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        _primaryColumn.AddChild(classification);

        var rule = new ColorRect
        {
            Color = InterfaceTheme.EdgeStrong,
            CustomMinimumSize = new Vector2(52, 1),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _primaryColumn.AddChild(rule);

        _bodyTitle = new Label
        {
            Text = "STARSHIP\nFLIGHT 14",
            AutowrapMode = TextServer.AutowrapMode.Off,
        };
        InterfaceTheme.ApplyDisplay(_bodyTitle, 88);
        _bodyTitle.AddThemeColorOverride("font_color", InterfaceTheme.Text);
        _bodyTitle.AddThemeConstantOverride("line_spacing", -8);
        _primaryColumn.AddChild(_bodyTitle);

        _bodySubtitle = new Label
        {
            Text = UiText.Get("flight14_home"),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(390, 54),
        };
        InterfaceTheme.ApplyBody(_bodySubtitle, 14);
        _bodySubtitle.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        _primaryColumn.AddChild(_bodySubtitle);

        _navigation = new GridContainer
        {
            Name = "MissionNavigation",
            Columns = 2,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _navigation.AddThemeConstantOverride("h_separation", 10);
        _navigation.AddThemeConstantOverride("v_separation", 10);
        _primaryColumn.AddChild(_navigation);

        _flight14Button = ModalButton(UiText.Get("flight14_details"), ShowFlight14, true);
        _flight14Button.Name = "Flight14";
        _flight14Button.CustomMinimumSize = new Vector2(0, 54);
        _primaryColumn.AddChild(_flight14Button);
        _firstButton = _flight14Button;
        _primaryColumn.MoveChild(_navigation, _primaryColumn.GetChildCount() - 1);

        AddNavButton(UiText.Get("free_flight"), ShowVehicles, primary: true);
        AddNavButton(UiText.Get("campaign"), ShowCampaign);
        AddNavButton(UiText.Get("vab"),
            () => GetTree().ChangeSceneToFile("res://scenes/construction/Construction.tscn"));
        AddNavButton(UiText.Get("scenarios"), ShowScenarios);
        string[] saves = SaveSystem.ListSaveSlots();
        AddNavButton(UiText.Get("continue"), () => ContinueSave(saves),
            disabled: saves.Length == 0,
            tooltip: saves.Length == 0 ? UiText.Get("no_saves") : "");
        AddNavButton(UiText.Get("saves"), ShowSaves);
        AddNavButton(UiText.Get("settings"), ShowSettings);
        AddNavButton(UiText.Get("quit"), () => GetTree().Quit());

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
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Alignment = HorizontalAlignment.Left,
            Disabled = disabled,
            TooltipText = tooltip,
            FocusMode = FocusModeEnum.All,
        };
        InterfaceTheme.StyleDossierButton(button, primary);
        button.AddThemeFontSizeOverride("font_size", 12);
        button.CustomMinimumSize = new Vector2(198, 52);
        button.AddThemeConstantOverride("outline_size", 0);
        button.Pressed += action;
        _navigation.AddChild(button);
        _firstButton ??= button.Disabled ? null : button;
    }

    private static PanelContainer BuildDossier()
    {
        var panel = new PanelContainer
        {
            Name = "FeaturedMission",
            CustomMinimumSize = new Vector2(390, 410),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var style = InterfaceTheme.PanelStyle(0.94f, 28, 25, deep: true);
        style.BorderColor = InterfaceTheme.EdgeStrong;
        panel.AddThemeStyleboxOverride("panel", style);

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 15);
        panel.AddChild(content);

        var statusRow = new HBoxContainer();
        statusRow.AddThemeConstantOverride("separation", 8);

        var label = new Label
        {
            Text = UiText.Get("flight14_label"),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        InterfaceTheme.ApplyLabel(label, 12);
        label.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        statusRow.AddChild(label);

        var nominal = new Label { Text = UiText.Get("in_development") };
        InterfaceTheme.ApplyMono(nominal, 10);
        nominal.AddThemeColorOverride("font_color", InterfaceTheme.Warning);
        statusRow.AddChild(nominal);
        content.AddChild(statusRow);

        var mission = new Label { Text = UiText.Get("flight14_card_title") };
        InterfaceTheme.ApplyLabel(mission, 30);
        mission.AddThemeColorOverride("font_color", InterfaceTheme.Text);
        content.AddChild(mission);
        content.AddChild(Divider());
        content.AddChild(Metric(UiText.Get("site"), "STARBASE / BOCA CHICA"));
        content.AddChild(Metric(UiText.Get("vehicle"), "SHIP 41 / BOOSTER 21"));
        content.AddChild(Divider());
        foreach (string key in new[] { "phase_ascent", "phase_deploy", "phase_return" })
        {
            var phase = new Label { Text = UiText.Get(key) };
            InterfaceTheme.ApplyBody(phase, 14);
            phase.AddThemeColorOverride("font_color", InterfaceTheme.Text);
            content.AddChild(phase);
        }
        content.AddChild(Divider());

        var note = new Label
        {
            Text = UiText.Get("flight14_pending"),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        InterfaceTheme.ApplyBody(note, 12);
        note.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        content.AddChild(note);

        var profile = new Label { Text = UiText.Get("reference_only") };
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
        // Slot names are alphabetical, not timestamps. Let the player choose when
        // several saves exist instead of silently resuming the wrong flight.
        if (saves.Length > 1)
        {
            ShowSaves();
            return;
        }
        CraftLaunchRequest.Set(new LaunchIntent { Mode = "continue", SaveSlot = saves[0] });
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
        string data = GameDataPath.Resolve();
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

    private void ShowFlight14() => ShowModal("STARSHIP FLIGHT 14", body =>
    {
        body.AddChild(Description(UiText.Get("flight14_pending")));
        body.AddChild(Description(UiText.Get("flight14_scope")));
        body.AddChild(ModalButton(UiText.Get("flight14_launch_pending"), () => { }, disabled: true));
        body.AddChild(ModalButton(UiText.Get("choose_existing_vehicle"), ShowVehicles, true));
    });

    private void ShowVehicles() => ShowModal(UiText.Get("vehicle_selection"), body =>
    {
        body.AddChild(Description(UiText.Get("free_flight_description")));
        string data = GameDataPath.Resolve();
        foreach (var vehicle in FlightMenuCatalog.Launchers)
        {
            var variant = VehicleVariantDefinition.LoadFromJson(
                System.IO.Path.Combine(data, "vehicles", vehicle.VariantFile));
            var selected = vehicle;
            var button = ModalButton(variant.Name.ToUpperInvariant(),
                () => ShowVehicle(selected), primary: vehicle == FlightMenuCatalog.Launchers[0]);
            button.Name = variant.Id;
            body.AddChild(button);
        }
    });

    private void ShowVehicle(MenuLauncher vehicle) => ShowModal(UiText.Get("free_flight"), body =>
    {
        string data = GameDataPath.Resolve();
        var variant = VehicleVariantDefinition.LoadFromJson(
            System.IO.Path.Combine(data, "vehicles", vehicle.VariantFile));
        body.AddChild(Description(variant.Name));
        body.AddChild(Metric(UiText.Get("site"), vehicle.SiteLabel));
        body.AddChild(Metric(UiText.Get("configuration_date"), variant.AsOfDate.ToString("yyyy-MM-dd")));
        body.AddChild(Description(UiText.Get("manual_flight_note")));
        body.AddChild(ModalButton(UiText.Get("launch_vehicle"), () =>
            LaunchVehicleScenario(vehicle.VariantFile, vehicle.SiteId, "manual", "sandbox"), true));
        body.AddChild(ModalButton(UiText.Get("change_vehicle"), ShowVehicles));
    });

    private static Label Description(string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        InterfaceTheme.ApplyBody(label, 14);
        label.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        return label;
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
        string data = GameDataPath.Resolve();
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

        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 6);
        body.AddChild(list);

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
            bool partial = mission?.FlightProfileId == "apollo11-lunar-landing-return";
            bool completed = state.CompletedMissionIds.Contains(entry.DefinitionId ?? "");
            string status = playable
                ? completed ? (spanish ? "COMPLETADA" : "COMPLETED")
                    : partial ? (spanish ? "PARCIAL" : "PARTIAL") : (spanish ? "LISTA" : "READY")
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
                    ShowModal(entry.Title, briefing =>
                    {
                        briefing.AddChild(Description(selectedMission == null
                            ? UiText.Get("mission_planned")
                            : spanish ? selectedMission.SummaryEs : selectedMission.SummaryEn));
                        briefing.AddChild(Description($"{UiText.Get("mission_status")}: {status}"));
                        if (selectedMission != null)
                        {
                            if (partial) briefing.AddChild(Description(UiText.Get("apollo11_partial")));
                            var launcher = FlightMenuCatalog.Launchers.FirstOrDefault(item =>
                                item.VariantFile == System.IO.Path.GetFileName(selectedVariantFile));
                            briefing.AddChild(Metric(UiText.Get("site"), launcher?.SiteLabel ?? selectedMission.LaunchSiteId));
                            briefing.AddChild(Metric(UiText.Get("mission_date"), selectedMission.HistoricalDate.ToString("yyyy-MM-dd")));
                            if (!unlocked)
                            {
                                var prerequisites = selectedMission.PrerequisiteMissionIds
                                    .Where(id => !state.CompletedMissionIds.Contains(id))
                                    .Select(id => missionCatalog.Definitions.TryGetValue(id, out var prerequisite)
                                        ? spanish ? prerequisite.TitleEs : prerequisite.TitleEn : id);
                                briefing.AddChild(Description(UiText.Get("mission_prerequisites") + ": " + string.Join(", ", prerequisites)));
                            }
                            briefing.AddChild(ModalButton(UiText.Get("start_mission"), () =>
                            {
                                if (selectedVariantFile != null)
                                    LaunchCampaignMission(selectedMission, selectedVariantFile, state);
                            }, true, disabled: !playable));
                            briefing.AddChild(InterfaceTheme.SectionLabel(UiText.Get("mission_objectives")));
                            foreach (var objective in selectedMission.Objectives)
                                briefing.AddChild(Description("• " + (spanish ? objective.TitleEs : objective.TitleEn)));
                            if (selectedMission.Limits.Count > 0)
                            {
                                briefing.AddChild(InterfaceTheme.SectionLabel(UiText.Get("mission_limits")));
                                foreach (var limit in selectedMission.Limits)
                                    briefing.AddChild(Description("• " + (spanish ? limit.TitleEs : limit.TitleEn)));
                            }
                        }
                        briefing.AddChild(ModalButton(UiText.Get("back_to_missions"), ShowCampaign));
                    });
                },
                primary: entry.Sequence == 1,
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
        string data = GameDataPath.Resolve();
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

        shade.Name = "MenuModal";
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (string edge in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{edge}", 24);
        shade.AddChild(margin);
        var center = new CenterContainer();
        margin.AddChild(center);
        _modalPanel = new PanelContainer();
        _modalPanel.AddThemeStyleboxOverride(
            "panel", InterfaceTheme.PanelStyle(0.99f, 24, 20, deep: true));
        center.AddChild(_modalPanel);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        _modalPanel.AddChild(column);
        _modalScroll = new ScrollContainer
        {
            Name = "ModalScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
        };
        column.AddChild(_modalScroll);
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 12);
        _modalScroll.AddChild(body);

        var title = new Label { Text = titleText.ToUpperInvariant() };
        InterfaceTheme.ApplyLabel(title, 30);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        title.AddThemeColorOverride("font_color", InterfaceTheme.Text);
        column.AddChild(title);
        column.MoveChild(title, 0);
        var headingRule = Divider();
        column.AddChild(headingRule);
        column.MoveChild(headingRule, 1);
        populate(body);
        column.AddChild(Divider());
        var close = ModalButton(UiText.Get("close"), CloseModal);
        column.AddChild(close);
        UpdateResponsiveLayout();
        SuspendBackgroundFocus();
        var first = Descendants(body).OfType<Button>().FirstOrDefault(button => !button.Disabled);
        FocusModal(shade, first ?? close, _modalScroll);
    }

    private async void FocusModal(Control modal, Button first, ScrollContainer scroll)
    {
        // Containers need a layout pass before FollowFocus can compute a valid scroll offset.
        var tree = GetTree();
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        if (!IsInstanceValid(this)) return;
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        if (!IsInstanceValid(this) || _modal != modal || !IsInstanceValid(modal)) return;
        UpdateResponsiveLayout();
        first.GrabFocus();
        scroll.ScrollVertical = 0;
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
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Alignment = HorizontalAlignment.Left,
            Disabled = disabled,
            TooltipText = tooltip,
        };
        InterfaceTheme.StyleDossierButton(button, primary);
        button.CustomMinimumSize = new Vector2(0, 46);
        button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        button.Pressed += action;
        return button;
    }

    private void CloseModal()
    {
        if (_modal == null) return;
        RemoveChild(_modal);
        _modal.QueueFree();
        _modal = null;
        _modalPanel = null;
        _modalScroll = null;
        foreach (var (control, mode) in _suspendedFocus)
            if (IsInstanceValid(control)) control.FocusMode = mode;
        _suspendedFocus.Clear();
        if (IsInstanceValid(_returnFocus)) _returnFocus!.CallDeferred(Control.MethodName.GrabFocus);
        else _firstButton?.CallDeferred(Control.MethodName.GrabFocus);
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

    private static IEnumerable<Node> Descendants(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            yield return child;
            foreach (Node descendant in Descendants(child)) yield return descendant;
        }
    }

    private void SuspendBackgroundFocus()
    {
        var focus = GetViewport().GuiGetFocusOwner();
        if (focus != null && !_modal!.IsAncestorOf(focus)) _returnFocus = focus;
        foreach (var control in Descendants(this).OfType<Control>())
        {
            if (_modal!.IsAncestorOf(control) || control == _modal || control.FocusMode == FocusModeEnum.None)
                continue;
            _suspendedFocus[control] = control.FocusMode;
            control.FocusMode = FocusModeEnum.None;
        }
    }

    private void UpdateResponsiveLayout()
    {
        if (_dossier == null || _bodyMargin == null || _primaryColumn == null) return;
        // Size is already in logical UI coordinates; ContentScaleFactor has applied the scale.
        float effectiveWidth = Size.X;
        float effectiveHeight = Size.Y;
        bool compact = effectiveHeight < 820f || effectiveWidth < 1380f;
        bool narrow = effectiveWidth < 1180f;
        bool shortWindow = effectiveHeight < 560f;
        _dossier.Visible = !narrow;
        _bodyMargin.OffsetLeft = compact ? 32 : 70;
        _bodyMargin.OffsetTop = shortWindow ? 76 : compact ? 90 : 122;
        _bodyMargin.OffsetRight = compact ? -32 : -70;
        _bodyMargin.OffsetBottom = compact ? -66 : -84;
        _primaryColumn.CustomMinimumSize = new Vector2(Math.Min(440, Math.Max(0, effectiveWidth - 64)), 0);
        _primaryColumn.AddThemeConstantOverride("separation", shortWindow ? 6 : compact ? 10 : 18);
        _navigation.Columns = effectiveWidth < 560 ? 1 : 2;
        _bodyTitle.AddThemeFontSizeOverride("font_size", shortWindow ? 42 : compact ? 62 : 88);
        if (_flight14Button != null)
            _flight14Button.CustomMinimumSize = new Vector2(0, shortWindow ? 46 : 54);
        _bodySubtitle.CustomMinimumSize = new Vector2(0, compact ? 44 : 64);
        foreach (Node child in _navigation.GetChildren())
            if (child is Button button)
                button.CustomMinimumSize = new Vector2(0, compact ? 46 : 52);
        if (_modalPanel != null && _modalScroll != null)
        {
            _modalPanel.CustomMinimumSize = new Vector2(Math.Max(0, Math.Min(680, effectiveWidth - 48)), 0);
            float contentHeight = _modalScroll.GetChild<Control>(0).GetCombinedMinimumSize().Y;
            _modalScroll.CustomMinimumSize = new Vector2(0,
                Math.Min(contentHeight, Math.Max(80, Math.Min(460, effectiveHeight - 240))));
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
