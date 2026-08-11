namespace Exosphere.Game;

using Godot;
using System.Linq;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Presentation;
using Exosphere.Simulation.Systems;

// ── Flight HUD (SpaceX-webcast aesthetic) ────────────────────────────────────
// Dark translucent panels, thin lines, condensed type, cyan/white accents. A big
// centred bottom telemetry band (SPEED / ALTITUDE / T+), a milestone countdown, a
// left "loads & trajectory" panel and a right "stages & Δv + event log" panel.
// Attitude cluster (engines — navball — data strip) is spawned as children here.
// All physics-derived values arrive through FlightHudSnapshot.
public partial class HUDController : Control
{
    public static FlightHudSnapshot? LatestSnapshot { get; private set; }

    // ── Palette ─────────────────────────────────────────────────────────────
    private static readonly Color PanelBg     = InterfaceTheme.Glass;
    private static readonly Color PanelBorder = InterfaceTheme.Edge;
    private static readonly Color LabelDim    = InterfaceTheme.TextMuted;
    private static readonly Color ValueBright = InterfaceTheme.Text;
    private static readonly Color Accent      = InterfaceTheme.Text;
    private static readonly Color GaugeTrack  = InterfaceTheme.Track;
    private static readonly Color FuelCol     = new(0.76f, 0.79f, 0.84f, 1f);
    private static readonly Color OxCol       = new(0.96f, 0.97f, 1.00f, 1f);
    private static readonly Color FuelLowCol  = InterfaceTheme.Alert;
    private static readonly Color WarnCol     = InterfaceTheme.Warning;

    // ── Left panel: loads & trajectory ──────────────────────────────────────
    // ALTITUDE deliberately absent: the bottom telemetry band is the single authoritative
    // altitude readout for the exterior view (C2 dedupe).
    private Label _vspeedValue = null!;
    private Label _gValue     = null!;
    private Label _qValue     = null!;
    private Label _pitchValue = null!;
    private Label _hdgValue   = null!;
    private Label _downrangeValue = null!;
    private Label _maxqFlag    = null!;

    // ── Right panel: stages, Δv, propellant, event log ──────────────────────
    private Label _apValue   = null!;
    private Label _peValue   = null!;
    private Label _suborbitalWarn = null!;   // aviso de trayectoria de impacto / impact-trajectory warning
    private Label _massValue = null!;
    private Label _dvValue   = null!;
    // TIME WARP deliberately absent: the standalone WarpController box owns it (C2 dedupe).
    private ColorRect _lfFill = null!;
    private ColorRect _oxFill = null!;
    private Label _lfValue = null!;
    private Label _oxValue = null!;
    private float _lfTrackW, _oxTrackW;
    private Label _eventLog = null!;

    // ── Bottom-centre big telemetry band ────────────────────────────────────
    private Label _bigSpeed = null!;
    private Label _bigAlt   = null!;
    private Label _bigTime  = null!;

    // ── Phase banner / progress / countdown ─────────────────────────────────
    private Label _phaseLabel  = null!;
    private HBoxContainer _navRow = null!;
    private HBoxContainer _phaseTrack = null!;
    private readonly System.Collections.Generic.List<ColorRect> _phaseDots = new();
    private Label _countdownLabel = null!;
    private Label _countdownMilestone = null!;
    private Label _guidanceLabel = null!;
    private Label _boosterLabel = null!;
    private Label _densityToast = null!;
    private double _densityToastTimer;
    private Label _alertLabel = null!;
    private Label _alertAction = null!;
    private readonly System.Collections.Generic.Dictionary<FlightNavigationMode, Label> _navLabels = new();

    private Control _leftRoot = null!;
    private Control _rightRoot = null!;
    private Control _phaseRoot = null!;
    private Control _bottomRoot = null!;
    private Control _timeRoot = null!;
    private EngineGridHUD _engineGrid = null!;
    private AttitudeNavball _navball = null!;
    private AttitudeDataStrip _attitudeStrip = null!;

    // ── Pad help overlay + launch path callout (UX-001 / UX-002) ─────────────
    private PanelContainer _padHelpRoot = null!;
    private Button _reentryDemoButton = null!;
    private Label _launchPathLabel = null!;
    private bool _padHelpDismissed;
    private bool _padHelpAutoDismissed;

    // ── Presentation state ─────────────────────────────────────────────────
    private readonly FlightHudPresenter _presenter = new();
    private FlightHudSnapshot? _snapshot;
    private MissionPhase _lastPhase = MissionPhase.PRE_LAUNCH;
    private bool     _maxqSeen;
    private bool     _pastEntryInterface;   // latch: RETRO_BURN after ENTRY → landing slot
    private readonly System.Collections.Generic.List<string> _events = new();

    /// <summary>Dot track mirrors <see cref="MissionPhaseTrack.Sequence"/> (includes COAST + RETRO_BURN).</summary>
    private static readonly MissionPhase[] PhaseSequence =
        MissionPhaseTrack.Sequence.Select(System.Enum.Parse<MissionPhase>).ToArray();

    public override void _Ready()
    {
        BuildLeftPanel();
        BuildRightPanel();
        BuildPhaseBanner();
        BuildBottomBand();
        BuildCountdown();
        BuildPadHelpOverlay();
        BuildDensityToast();

        // Attitude cluster: navball owns CenterBottom; engines/strip are children
        // with local Position so they cannot vanish from a zero-size HBox layout.
        BuildAttitudeCluster();
        _objectives = new MissionObjectivesPanel { Name = "MissionObjectives" };
        AddChild(_objectives);
    }

    private void BuildAttitudeCluster()
    {
        _navball = new AttitudeNavball { Name = "Navball" };
        _navball.ZIndex = 30;
        AddChild(_navball);

        _engineGrid = new EngineGridHUD { Name = "EngineGridHUD" };
        _attitudeStrip = new AttitudeDataStrip { Name = "AttitudeDataStrip" };
        // Parent to the navball so the trio moves as one instrument.
        _navball.AddChild(_engineGrid);
        _navball.AddChild(_attitudeStrip);

        // Local coords: navball origin is its top-left. Place boards beside the disc.
        const float gap = 10f;
        float navW = 2f * 78f + 28f; // matches AttitudeNavball.Radius
        _engineGrid.Position = new Vector2(-(EngineGridHUD.BoardWidth + gap), 8f);
        _attitudeStrip.Position = new Vector2(navW + gap, 8f);
        _engineGrid.Size = new Vector2(EngineGridHUD.BoardWidth, EngineGridHUD.BoardHeightCompact);
        _attitudeStrip.Size = new Vector2(AttitudeDataStrip.BoardWidth, AttitudeDataStrip.BoardHeight);
    }

    private void BuildDensityToast()
    {
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.CenterTop);
        center.GrowHorizontal = GrowDirection.Both;
        center.OffsetTop = 132;
        center.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(center);

        _densityToast = new Label { Text = "" };
        _densityToast.HorizontalAlignment = HorizontalAlignment.Center;
        InterfaceTheme.ApplyMono(_densityToast, 12);
        _densityToast.AddThemeColorOverride("font_color", InterfaceTheme.Text);
        _densityToast.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        _densityToast.AddThemeConstantOverride("outline_size", 5);
        center.AddChild(_densityToast);
        _densityToastRoot = center;
        center.Visible = false;
    }

    private CenterContainer _densityToastRoot = null!;
    private MissionObjectivesPanel _objectives = null!;

    public override void _ExitTree()
    {
        LatestSnapshot = null;
    }

    // ── Panel construction ──────────────────────────────────────────────────

    private void BuildLeftPanel()
    {
        var panel = MakePanel();
        _leftRoot = panel;
        panel.OffsetLeft = 18; panel.OffsetTop = 18;
        AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        panel.AddChild(vbox);

        vbox.AddChild(MakeHeader("FLIGHT"));
        _vspeedValue    = AddRow(vbox, "VERT SPEED", "---");
        _gValue         = AddRow(vbox, "G-FORCE", "---");
        _qValue         = AddRow(vbox, "DYN PRESS q", "---");
        _pitchValue     = AddRow(vbox, "FLIGHT PITCH", "---");
        _hdgValue       = AddRow(vbox, "HEADING", "---");
        _downrangeValue = AddRow(vbox, "DOWNRANGE", "---");

        _maxqFlag = new Label { Text = "" };
        InterfaceTheme.ApplyMono(_maxqFlag, 12);
        _maxqFlag.AddThemeColorOverride("font_color", WarnCol);
        _maxqFlag.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(_maxqFlag);
    }

    private void BuildRightPanel()
    {
        var panel = MakePanel();
        _rightRoot = panel;
        panel.SetAnchorsPreset(LayoutPreset.TopRight);
        panel.GrowHorizontal = GrowDirection.Begin;
        panel.OffsetRight = -18; panel.OffsetTop = 18;
        AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        panel.AddChild(vbox);

        vbox.AddChild(MakeHeader("ORBIT / VEHICLE"));
        _massValue = AddRow(vbox, "MASS", "---");
        _dvValue   = AddRow(vbox, "STAGE Δv", "---");
        _apValue   = AddRow(vbox, "APOAPSIS", "---");
        _peValue   = AddRow(vbox, "PERIAPSIS", "---");

        // Aviso de trayectoria suborbital: parte del bloque de órbita (en el VBox), por lo
        // que nunca solapa otros paneles. Vacío salvo cuando la periapsis cae bajo superficie.
        // Suborbital-trajectory warning: part of the orbit block (inside the VBox), so it never
        // overlaps other panels. Empty unless periapsis falls below the surface.
        _suborbitalWarn = new Label { Text = "" };
        InterfaceTheme.ApplyMono(_suborbitalWarn, 11);
        _suborbitalWarn.AddThemeColorOverride("font_color", FuelLowCol);
        _suborbitalWarn.HorizontalAlignment = HorizontalAlignment.Center;
        _suborbitalWarn.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _suborbitalWarn.CustomMinimumSize = new Vector2(246, 0);
        vbox.AddChild(_suborbitalWarn);

        vbox.AddChild(MakeGaugeLabel("LIQUID FUEL"));
        (_lfFill, _lfValue, _lfTrackW) = AddGauge(vbox, FuelCol);
        vbox.AddChild(MakeGaugeLabel("OXIDIZER"));
        (_oxFill, _oxValue, _oxTrackW) = AddGauge(vbox, OxCol);

        vbox.AddChild(MakeHeader("EVENT LOG"));
        _eventLog = new Label { Text = "-" };
        InterfaceTheme.ApplyMono(_eventLog, 10);
        _eventLog.AddThemeColorOverride("font_color", LabelDim);
        _eventLog.CustomMinimumSize = new Vector2(246, 56);
        _eventLog.VerticalAlignment = VerticalAlignment.Top;
        vbox.AddChild(_eventLog);
    }

    private void BuildPhaseBanner()
    {
        var center = new PanelContainer();
        _phaseRoot = center;
        center.SetAnchorsPreset(LayoutPreset.CenterTop);
        center.GrowHorizontal = GrowDirection.Both;
        center.OffsetLeft = -320;
        center.OffsetTop = 18;
        center.OffsetRight = 320;
        center.AddThemeStyleboxOverride("panel", InterfaceTheme.GlassPanel(0.62f, 12, 18, 10));
        center.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(center);

        var vbox = new VBoxContainer();
        vbox.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddThemeConstantOverride("separation", 6);
        center.AddChild(vbox);

        _phaseLabel = new Label { Text = "PRE-LAUNCH" };
        _phaseLabel.HorizontalAlignment = HorizontalAlignment.Center;
        InterfaceTheme.ApplyDisplay(_phaseLabel, 20);
        _phaseLabel.AddThemeColorOverride("font_color", PhaseColor(MissionPhase.PRE_LAUNCH));
        _phaseLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        _phaseLabel.AddThemeConstantOverride("outline_size", 3);
        vbox.AddChild(_phaseLabel);

        _launchPathLabel = new Label { Text = "" };
        _launchPathLabel.HorizontalAlignment = HorizontalAlignment.Center;
        InterfaceTheme.ApplyBody(_launchPathLabel, 11);
        _launchPathLabel.AddThemeColorOverride("font_color", LabelDim);
        _launchPathLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _launchPathLabel.CustomMinimumSize = new Vector2(580, 0);
        vbox.AddChild(_launchPathLabel);

        // UX-014: the one place ascent-guidance and EDL status may speak. AscentController
        // and EDLController no longer draw banners of their own; they publish here.
        _guidanceLabel = new Label { Text = "" };
        _guidanceLabel.HorizontalAlignment = HorizontalAlignment.Center;
        InterfaceTheme.ApplyMono(_guidanceLabel, 10);
        _guidanceLabel.AddThemeColorOverride("font_color", InterfaceTheme.Orbital);
        _guidanceLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _guidanceLabel.CustomMinimumSize = new Vector2(580, 0);
        vbox.AddChild(_guidanceLabel);

        // R12: booster return is a parallel vehicle — keep Ship mission phase intact
        // and publish Super Heavy status on its own line under Ship guidance.
        _boosterLabel = new Label { Text = "" };
        _boosterLabel.HorizontalAlignment = HorizontalAlignment.Center;
        InterfaceTheme.ApplyMono(_boosterLabel, 10);
        _boosterLabel.AddThemeColorOverride("font_color", InterfaceTheme.Warning);
        _boosterLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _boosterLabel.CustomMinimumSize = new Vector2(580, 0);
        vbox.AddChild(_boosterLabel);

        var nav = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        nav.AddThemeConstantOverride("separation", 12);
        vbox.AddChild(nav);
        _navRow = nav;
        foreach (FlightNavigationMode mode in System.Enum.GetValues<FlightNavigationMode>())
        {
            var label = new Label { Text = mode.ToString().ToUpperInvariant() };
            label.AddThemeFontSizeOverride("font_size", 10);
            label.AddThemeColorOverride("font_color", LabelDim);
            InterfaceTheme.ApplyMono(label, 10);
            nav.AddChild(label);
            _navLabels[mode] = label;
        }

        _alertLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        InterfaceTheme.ApplyBody(_alertLabel, 12, medium: true);
        vbox.AddChild(_alertLabel);

        _alertAction = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        InterfaceTheme.ApplyBody(_alertAction, 10);
        _alertAction.AddThemeColorOverride("font_color", LabelDim);
        vbox.AddChild(_alertAction);

        _phaseTrack = new HBoxContainer();
        _phaseTrack.Alignment = BoxContainer.AlignmentMode.Center;
        _phaseTrack.AddThemeConstantOverride("separation", 4);
        vbox.AddChild(_phaseTrack);
        foreach (var _ in PhaseSequence)
        {
            var dot = new ColorRect
            {
                CustomMinimumSize = new Vector2(18, 2),
                Color = GaugeTrack,
            };
            _phaseDots.Add(dot);
            _phaseTrack.AddChild(dot);
        }
    }

    // Big centred bottom telemetry band: SPEED · ALTITUDE · T+.
    private void BuildBottomBand()
    {
        // SPEED y ALTITUDE flanquean el navball (centro-abajo, ~190 px de ancho): un hueco
        // central amplio deja la esfera entre ambos sin solaparse. El reloj T+ se coloca
        // como etiqueta independiente justo ENCIMA del navball para no quedar tapado.
        // SPEED and ALTITUDE flank the centre-bottom navball (~190 px wide) with a wide gap;
        // the T+ clock is a separate label ABOVE the navball so the disc never covers it.
        var center = new CenterContainer();
        _bottomRoot = center;
        center.SetAnchorsPreset(LayoutPreset.BottomWide);
        center.GrowVertical = GrowDirection.Begin;
        center.OffsetBottom = -32;
        center.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(center);

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 260);
        hbox.Alignment = BoxContainer.AlignmentMode.Center;
        center.AddChild(hbox);

        _bigSpeed = AddBigStat(hbox, "SURFACE SPEED", "0", "KM/H · VERT — 0 M/S");
        _bigAlt   = AddBigStat(hbox, "ALTITUDE", "0.0", "KM");

        // T+ clock — centred, above the navball disc.
        var timeCenter = new CenterContainer();
        _timeRoot = timeCenter;
        timeCenter.SetAnchorsPreset(LayoutPreset.BottomWide);
        timeCenter.GrowVertical = GrowDirection.Begin;
        timeCenter.OffsetBottom = -220;
        timeCenter.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(timeCenter);

        var timeBox = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        timeCenter.AddChild(timeBox);
        _bigTime = AddBigStat(timeBox, "T+", "00:00:00", "");
    }

    private Label AddBigStat(HBoxContainer parent, string caption, string value, string unit)
    {
        var vbox = new VBoxContainer();
        vbox.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddThemeConstantOverride("separation", 0);

        var cap = new Label { Text = caption };
        cap.HorizontalAlignment = HorizontalAlignment.Center;
        InterfaceTheme.ApplyMono(cap, 13);
        cap.AddThemeColorOverride("font_color", LabelDim);
        cap.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
        cap.AddThemeConstantOverride("outline_size", 4);
        vbox.AddChild(cap);

        var val = new Label { Text = value };
        val.HorizontalAlignment = HorizontalAlignment.Center;
        InterfaceTheme.ApplyDisplay(val, 34);
        val.AddThemeColorOverride("font_color", ValueBright);
        val.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        val.AddThemeConstantOverride("outline_size", 6);
        vbox.AddChild(val);

        if (unit.Length > 0)
        {
            var u = new Label { Text = unit };
            u.HorizontalAlignment = HorizontalAlignment.Center;
            InterfaceTheme.ApplyMono(u, 12);
            u.AddThemeColorOverride("font_color", LabelDim);
            u.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
            u.AddThemeConstantOverride("outline_size", 3);
            vbox.AddChild(u);
        }
        parent.AddChild(vbox);
        return val;
    }

    private void BuildCountdown()
    {
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.Center);
        center.OffsetTop = -178;
        center.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(center);

        var vbox = new VBoxContainer();
        vbox.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddThemeConstantOverride("separation", 2);
        center.AddChild(vbox);

        _countdownLabel = new Label { Text = "" };
        _countdownLabel.HorizontalAlignment = HorizontalAlignment.Center;
        InterfaceTheme.ApplyDisplay(_countdownLabel, 48);
        _countdownLabel.AddThemeColorOverride("font_color", WarnCol);
        _countdownLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        _countdownLabel.AddThemeConstantOverride("outline_size", 7);
        vbox.AddChild(_countdownLabel);

        _countdownMilestone = new Label { Text = "" };
        _countdownMilestone.HorizontalAlignment = HorizontalAlignment.Center;
        InterfaceTheme.ApplyBody(_countdownMilestone, 14, medium: true);
        _countdownMilestone.AddThemeColorOverride("font_color", LabelDim);
        _countdownMilestone.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        _countdownMilestone.AddThemeConstantOverride("outline_size", 6);
        vbox.AddChild(_countdownMilestone);

        center.Visible = false;
        _countdownRoot = center;
    }
    private CenterContainer _countdownRoot = null!;

    /// <summary>
    /// C5: the complete flight key inventory, grouped, re-invokable with [F1] at any time.
    /// It used to list 9 of ~30 keys — omitting [Space] staging, the single most important
    /// key in the game — and hid itself permanently at liftoff.
    /// </summary>
    private void BuildPadHelpOverlay()
    {
        _padHelpRoot = new PanelContainer();
        _padHelpRoot.SetAnchorsPreset(LayoutPreset.Center);
        // Keep the sheet above the attitude cluster so a late dismiss cannot bury engines/strip.
        _padHelpRoot.OffsetLeft = -400;
        _padHelpRoot.OffsetTop = -280;
        _padHelpRoot.OffsetRight = 400;
        _padHelpRoot.OffsetBottom = 120;
        _padHelpRoot.AddThemeStyleboxOverride("panel", InterfaceTheme.GlassPanel(0.86f, 14, 22, 18));
        _padHelpRoot.MouseFilter = MouseFilterEnum.Stop;
        _padHelpRoot.ZIndex = 5;
        AddChild(_padHelpRoot);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);
        _padHelpRoot.AddChild(vbox);

        var title = new Label { Text = "MISSION CONTROLS" };
        title.HorizontalAlignment = HorizontalAlignment.Center;
        InterfaceTheme.ApplyDisplay(title, 22);
        title.AddThemeColorOverride("font_color", ValueBright);
        vbox.AddChild(title);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 28);
        columns.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(columns);

        var left = MakeHelpColumn(columns);
        AddHelpGroup(left, "FLIGHT", new[]
        {
            ("Space", "STAGE / SEPARATE"),
            ("hold Z", "IGNITION / THROTTLE UP"),
            ("hold X", "THROTTLE DOWN"),
            ("W S", "PITCH"),
            ("A D", "YAW"),
            ("Q E", "ROLL"),
            ("T", "SAS HOLD ON / OFF"),
        });
        AddHelpGroup(left, "MISSION", new[]
        {
            ("L", "AUTO LAUNCH SEQUENCE"),
            ("G", "FULL ASCENT GUIDANCE"),
            ("H", "PITCH ASSIST ON / OFF"),
            ("R", "REENTRY DEMONSTRATION"),
            ("V", "VEHICLE ASSEMBLY"),
            ("Esc", "MAIN MENU"),
        });

        var right = MakeHelpColumn(columns);
        AddHelpGroup(right, "VIEW", new[]
        {
            ("C", "CAMERA PRESET / COCKPIT"),
            ("mouse drag", "ORBIT CAMERA"),
            ("wheel", "ZOOM"),
            ("M", "ORBITAL MAP"),
            ("F1", "THIS PANEL"),
            ("F2", "ACKNOWLEDGE ALERT"),
            ("F3", "HUD DENSITY"),
        });
        AddHelpGroup(right, "TIME & FILE", new[]
        {
            (", .", "TIME WARP DOWN / UP"),
            ("F5", "QUICKSAVE"),
            ("F9", "QUICKLOAD"),
        });
        AddHelpGroup(right, "MAP VIEW", new[]
        {
            ("Tab", "SOLAR / LOCAL VIEW"),
            ("1…6", "TRANSFER TARGET"),
            ("Enter", "EXECUTE NODE"),
            ("B", "PLAN DEORBIT BURN"),
            ("[ ]", "NODE Δv −5% / +5%"),
            ("Del", "CLEAR NODE"),
        });
        AddHelpGroup(right, "DEBUG", new[]
        {
            ("O", "JUMP TO ORBIT"),
            ("J", "JUMP TO BODY (MAP)"),
            ("F8", "INJECT ENGINE FAILURE"),
        }, debug: true);

        _reentryDemoButton = new Button
        {
            Text = "VIEW REENTRY → LANDING",
            CustomMinimumSize = new Vector2(360, 38),
            TooltipText = "Start the verified physical EDL demonstration at the 70 km entry interface",
        };
        InterfaceTheme.StyleButton(_reentryDemoButton, primary: true);
        _reentryDemoButton.Pressed += OnReentryDemoPressed;
        vbox.AddChild(_reentryDemoButton);

        var dismiss = new Label
        {
            Text = "[F1] show / dismiss at any time  ·  auto-hides at ignition",
        };
        dismiss.HorizontalAlignment = HorizontalAlignment.Center;
        InterfaceTheme.ApplyBody(dismiss, 10);
        dismiss.AddThemeColorOverride("font_color", LabelDim);
        vbox.AddChild(dismiss);
    }

    private static VBoxContainer MakeHelpColumn(HBoxContainer parent)
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 2);
        parent.AddChild(column);
        return column;
    }

    private static void AddHelpGroup(
        VBoxContainer column,
        string heading,
        (string key, string action)[] bindings,
        bool debug = false)
    {
        var head = new Label { Text = debug ? $"{heading} — NOT PART OF FLIGHT" : heading };
        InterfaceTheme.ApplyMono(head, 9);
        head.AddThemeColorOverride(
            "font_color", debug ? InterfaceTheme.Warning : InterfaceTheme.Orbital);
        column.AddChild(head);

        foreach (var (key, action) in bindings)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);

            var keyLabel = new Label
            {
                Text = key,
                CustomMinimumSize = new Vector2(86, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            InterfaceTheme.ApplyMono(keyLabel, 11);
            keyLabel.AddThemeColorOverride(
                "font_color", debug ? InterfaceTheme.TextFaint : ValueBright);
            row.AddChild(keyLabel);

            var actionLabel = new Label
            {
                Text = action,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            InterfaceTheme.ApplyBody(actionLabel, 11);
            actionLabel.AddThemeColorOverride(
                "font_color", debug ? InterfaceTheme.TextFaint : LabelDim);
            row.AddChild(actionLabel);

            column.AddChild(row);
        }

        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
    }

    private void OnReentryDemoPressed()
    {
        if (SimulationBridge.Instance?.BeginReentryDemonstration() != true) return;
        _padHelpDismissed = true;
        _events.Insert(0, $"{FormatClock(SimulationBridge.Instance.Universe.CurrentTime)}  REENTRY DEMO");
        if (_events.Count > 5) _events.RemoveAt(_events.Count - 1);
        _eventLog.Text = string.Join("\n", _events);
    }

    // ── Widget factories ────────────────────────────────────────────────────

    private static PanelContainer MakePanel()
    {
        var sb = InterfaceTheme.GlassPanel(0.76f, 12, 16, 13);
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", sb);
        panel.CustomMinimumSize = new Vector2(278, 0);
        panel.MouseFilter = MouseFilterEnum.Ignore;
        return panel;
    }

    private static Label MakeHeader(string text)
    {
        var lbl = new Label { Text = text };
        InterfaceTheme.ApplyMono(lbl, 10);
        lbl.AddThemeColorOverride("font_color", LabelDim);
        return lbl;
    }

    private static Label MakeGaugeLabel(string text)
    {
        var lbl = new Label { Text = text };
        InterfaceTheme.ApplyBody(lbl, 11);
        lbl.AddThemeColorOverride("font_color", LabelDim);
        return lbl;
    }

    private static Label AddRow(VBoxContainer parent, string caption, string initial)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        var cap = new Label { Text = caption };
        InterfaceTheme.ApplyBody(cap, 11);
        cap.AddThemeColorOverride("font_color", LabelDim);
        cap.CustomMinimumSize = new Vector2(118, 0);
        row.AddChild(cap);

        // Numeric readouts are monospaced so a changing digit never shifts the column.
        var val = new Label { Text = initial };
        InterfaceTheme.ApplyMono(val, 12);
        val.AddThemeColorOverride("font_color", ValueBright);
        val.HorizontalAlignment = HorizontalAlignment.Right;
        val.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(val);

        parent.AddChild(row);
        return val;
    }

    private static (ColorRect fill, Label value, float trackWidth) AddGauge(
        VBoxContainer parent, Color fillColor)
    {
        const float TrackW = 246f, TrackH = 8f;

        var track = new ColorRect
        {
            CustomMinimumSize = new Vector2(TrackW, TrackH),
            Color = GaugeTrack,
        };
        track.MouseFilter = MouseFilterEnum.Ignore;

        var fill = new ColorRect
        {
            Color = fillColor,
            Size = new Vector2(0, TrackH),
            Position = Vector2.Zero,
        };
        fill.MouseFilter = MouseFilterEnum.Ignore;
        track.AddChild(fill);

        var value = new Label { Text = "0%" };
        value.SetAnchorsPreset(LayoutPreset.FullRect);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.VerticalAlignment = VerticalAlignment.Center;
        InterfaceTheme.ApplyMono(value, 9);
        value.AddThemeColorOverride("font_color", ValueBright);
        value.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
        value.AddThemeConstantOverride("outline_size", 3);
        value.OffsetLeft = 6; value.OffsetRight = -6;
        value.MouseFilter = MouseFilterEnum.Ignore;
        track.AddChild(value);

        parent.AddChild(track);
        return (fill, value, TrackW);
    }

    // ── Per-frame update ────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        var bridge   = SimulationBridge.Instance;
        var vessel   = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        var mission  = MissionManager.Instance;
        if (bridge == null || vessel == null || universe == null) return;
        var refBody = universe.GetDominantBody(vessel.Position);

        // ── Attitude / throttle ─────────────────────────────────────────────
        // Crewed craft fly an onboard FCS: stick writes the vessel directly and
        // must not be gated by ground LOS/blackout. Unmanned / deep-space ground
        // mode still rides GroundCommandRelay (light-time + link drop).
        double pitchIn = 0, yawIn = 0, rollIn = 0;
        if (Input.IsKeyPressed(Key.W)) pitchIn += 1.0;
        if (Input.IsKeyPressed(Key.S)) pitchIn -= 1.0;
        if (Input.IsKeyPressed(Key.A)) yawIn   -= 1.0;
        if (Input.IsKeyPressed(Key.D)) yawIn   += 1.0;
        if (Input.IsKeyPressed(Key.Q)) rollIn  -= 1.0;
        if (Input.IsKeyPressed(Key.E)) rollIn  += 1.0;
        var stick = new Vector3d(pitchIn, yawIn, rollIn);

        bool crewAlive = SystemsController.Instance?.LifeSupport.CrewAlive ?? true;
        bool groundUplink = SystemsController.Instance != null
            && PilotCommandRouting.UsesGroundUplink(crewAlive, vessel.StructuralControlLost);

        // Onboard (crewed) or structural dead-stick (Vessel.Tick zeros authority): local write.
        // Unmanned only: ground relay.
        if (groundUplink)
            SystemsController.Instance!.SubmitGroundAttitude(stick);
        else
            vessel.PitchYawRoll = stick;

        // ── Hold-throttle (despegue manual) ─────────────────────────────────
        // [Z] mantenida: en tierra arranca la ignición (suelta el clamp al commit);
        // ya en vuelo, sube el throttle de forma progresiva. [X] mantenida lo baja.
        // Hold [Z]: on the pad starts ignition (releases hold-down at commit-to-launch);
        // already flying, spools the throttle up. Hold [X] spools it down.
        if (Input.IsPhysicalKeyPressed(Key.Z))
        {
            if (vessel.IsGroundHeld || bridge.IsIgnitionActive) bridge.Ignite();
            else if (groundUplink)
                SystemsController.Instance!.SubmitGroundThrottleDelta(0.5 * delta);
            else
                bridge.ThrottleUp(delta);
        }
        else if (Input.IsPhysicalKeyPressed(Key.X))
        {
            if (groundUplink)
                SystemsController.Instance!.SubmitGroundThrottleDelta(-0.5 * delta);
            else
                bridge.ThrottleDown(delta);
        }

        var viewMode = MapViewController.Instance?.Visible == true
            ? FlightHudViewMode.Map
            : CameraController.Instance?.IsCockpitView == true
                ? FlightHudViewMode.Cockpit
                : FlightHudViewMode.Exterior;
        string phaseName = (mission?.Phase ?? MissionPhase.PRE_LAUNCH).ToString();
        _snapshot = _presenter.Capture(
            universe,
            vessel,
            phaseName,
            viewMode,
            MapViewController.Instance?.HasNavigationTarget == true);
        var snapshot = _snapshot;
        LatestSnapshot = snapshot;

        _vspeedValue.Text = $"{snapshot.VerticalSpeedMps:+0.0;-0.0} m/s";
        _gValue.Text = $"{snapshot.ProperAccelerationG:F2} g";
        _gValue.AddThemeColorOverride(
            "font_color", snapshot.ProperAccelerationG > 4.0 ? WarnCol : ValueBright);
        _qValue.Text = $"{snapshot.DynamicPressurePa / 1000.0:F1} kPa";
        _pitchValue.Text = snapshot.SurfaceSpeedMps > 0.5
            ? $"{snapshot.FlightPathAngleDeg:F0}°"
            : "---";
        _hdgValue.Text = snapshot.SurfaceSpeedMps > 0.5
            ? $"{snapshot.HeadingDeg:F0}°"
            : "---";
        _downrangeValue.Text = snapshot.HasDownrangeReference
            ? FormatDistance(snapshot.DownrangeM)
            : "---";

        if (mission?.Phase == MissionPhase.MAX_Q)
        {
            _maxqFlag.Text = "◆ MAX-Q ◆";
            _maxqSeen = true;
        }
        else if (_maxqSeen) _maxqFlag.Text = "max-q passed";
        else _maxqFlag.Text = "";

        _massValue.Text = $"{snapshot.TotalMassKg / 1000.0:F1} t";
        _dvValue.Text = snapshot.StageDeltaVMps > 0.0
            ? $"{snapshot.StageDeltaVMps:N0} m/s"
            : "---";
        _apValue.Text = snapshot.ApoapsisAltitudeM is { } apoapsis
            ? FormatDistance(apoapsis)
            : "---";
        if (snapshot.IsImpactTrajectory)
        {
            _peValue.Text = "IMPACT";
            _peValue.AddThemeColorOverride("font_color", FuelLowCol);
        }
        else
        {
            _peValue.Text = snapshot.PeriapsisAltitudeM is { } periapsis
                ? FormatDistance(periapsis)
                : "---";
            _peValue.AddThemeColorOverride("font_color", ValueBright);
        }
        _suborbitalWarn.Text = snapshot.Alerts.Any(a => a.Code == "TRAJECTORY")
            ? "SUBORBITAL / IMPACT TRAJECTORY"
            : "";

        double peAlt = snapshot.PeriapsisAltitudeM ?? double.NaN;
        double atmoMax = refBody.Atmosphere?.MaxAltitude ?? double.NaN;
        double timeToPe = double.NaN;
        try
        {
            var elements = Exosphere.Simulation.OrbitalElements.FromStateVector(
                vessel.Position - refBody.Position,
                vessel.Velocity - refBody.Velocity,
                refBody.GM,
                refBody.Id,
                universe.CurrentTime);
            if (!elements.IsRadial && !elements.IsHyperbolic)
                timeToPe = MissionPhaseTrack.ApproximateTimeToPeriapsisSec(
                    elements.SemiMajorAxis,
                    elements.Eccentricity,
                    elements.GetMeanAnomaly(
                        universe.CurrentTime, refBody.GM),
                    refBody.GM);
        }
        catch (System.ArgumentException)
        {
            // Presenter values remain authoritative when a radial pad state
            // cannot produce conventional orbital elements.
        }

        _lfValue.Text = $"{snapshot.LiquidFuelKg / 1000.0:F1} t";
        _oxValue.Text = $"{snapshot.OxidizerKg / 1000.0:F1} t";
        _lfFill.Size = new Vector2(
            _lfTrackW * (float)System.Math.Clamp(snapshot.LiquidFuelFraction, 0, 1), 8);
        _oxFill.Size = new Vector2(
            _oxTrackW * (float)System.Math.Clamp(snapshot.OxidizerFraction, 0, 1), 8);
        _lfFill.Color = snapshot.LiquidFuelFraction < 0.12 ? FuelLowCol : FuelCol;
        _oxFill.Color = snapshot.OxidizerFraction < 0.12 ? FuelLowCol : OxCol;

        _bigSpeed.Text = $"{snapshot.SurfaceSpeedMps * 3.6:N0}";
        string verticalDirection = snapshot.VerticalSpeedMps switch
        {
            > 0.05 => "↑",
            < -0.05 => "↓",
            _ => "—",
        };
        ((Label)_bigSpeed.GetParent().GetChild(2)).Text =
            $"KM/H · VERT {verticalDirection} {System.Math.Abs(snapshot.VerticalSpeedMps):N0} M/S";
        _bigAlt.Text = snapshot.AltitudeM >= 1000
            ? $"{snapshot.AltitudeM / 1000.0:F1}"
            : $"{snapshot.AltitudeM:F0}";
        ((Label)_bigAlt.GetParent().GetChild(2)).Text =
            snapshot.AltitudeM >= 1000 ? "KM" : "M";
        _bigTime.Text = FormatClock(snapshot.MissionTimeS);
        RenderNavigationAndAlerts(snapshot);

        if (mission != null)
        {
            UpdateEntryInterfaceLatch(mission.Phase);
            _phaseLabel.Text = FormatPhase(mission.Phase);
            _phaseLabel.AddThemeColorOverride("font_color", PhaseColor(mission.Phase));
            UpdatePhaseTrack(mission.Phase);
            UpdateEventLog(mission.Phase, universe.CurrentTime);
            UpdateCountdown(mission);
            UpdateLaunchPathCallout(mission, bridge, snapshot);
            UpdateDeorbitEdlCue(mission, peAlt, atmoMax, timeToPe);
            UpdateControlAuthorityCue(vessel, mission);
            UpdatePadHelp(mission);
        }
        UpdateGuidanceLine();
        UpdateBoosterLine();
        UpdateDensityToast(delta);
        _attitudeStrip.UpdateFromSnapshot(snapshot);
        ApplyViewMode(snapshot.ViewMode);
    }

    /// <summary>
    /// UX-014 consolidation: the ascent autopilot and the EDL controller used to draw their
    /// own full-screen banners at 21.5% and 16% of viewport height. They now publish a status
    /// line and this is the only place it is rendered. Descent outranks ascent.
    /// Ownership cue: MANUAL / ASCENT / EDL / HISTORICAL.
    /// </summary>
    private void UpdateGuidanceLine()
    {
        if (EDLController.Instance?.BannerStatus is { Length: > 0 } edl)
        {
            _guidanceLabel.Text = edl;
            return;
        }

        if (AscentController.Instance?.BannerStatus is { Length: > 0 } ascent)
        {
            _guidanceLabel.Text = ascent;
            return;
        }

        if (HistoricalFlightProfileController.Instance?.IsEngaged == true)
        {
            _guidanceLabel.Text = "HISTORICAL";
            return;
        }

        _guidanceLabel.Text = "MANUAL";
    }

    private void UpdateBoosterLine()
    {
        string? line = BoosterReturnController.Instance?.StatusLine;
        _boosterLabel.Text = string.IsNullOrEmpty(line) ? "" : line!;
    }

    private void UpdateDensityToast(double delta)
    {
        if (_densityToastTimer <= 0.0) return;
        _densityToastTimer -= delta;
        if (_densityToastTimer <= 0.0) _densityToastRoot.Visible = false;
    }

    private void CycleHudDensity()
    {
        var density = UserInterfaceSettings.CycleHudDensity();
        _densityToast.Text = $"HUD  {density.ToString().ToUpperInvariant()}   [F3]";
        _densityToastRoot.Visible = true;
        _densityToastTimer = 2.0;
        ApplyBandScale(density == HudDensity.Full);
    }

    private void RenderNavigationAndAlerts(FlightHudSnapshot snapshot)
    {
        foreach (var (mode, label) in _navLabels)
        {
            bool active = mode == snapshot.NavigationMode;
            label.AddThemeColorOverride("font_color", active ? InterfaceTheme.Orbital : LabelDim);
            label.AddThemeFontSizeOverride("font_size", active ? 11 : 10);
        }

        var alert = snapshot.Alerts.FirstOrDefault();
        if (alert == null)
        {
            _alertLabel.Text = "";
            _alertAction.Text = "";
            return;
        }

        string acknowledgement = alert.Acknowledged ? "  ACK" : "  F2 ACK";
        _alertLabel.Text =
            $"{alert.Severity.ToString().ToUpperInvariant()}  {alert.Title}  " +
            $"{alert.Value} / LIMIT {alert.Limit}{acknowledgement}";
        _alertLabel.AddThemeColorOverride(
            "font_color",
            alert.Severity == FlightAlertSeverity.Critical ? FuelLowCol : WarnCol);
        _alertAction.Text = $"ACTION: {alert.RecommendedAction}";
    }

    /// <summary>
    /// Composes the camera-derived view mode with the player's HUD density (C3).
    /// View mode still decides what makes sense to draw at all (map/cockpit); density
    /// decides how much of the exterior HUD is worth the screen space.
    /// </summary>
    private void ApplyViewMode(FlightHudViewMode viewMode)
    {
        var density = UserInterfaceSettings.HudDensity;
        bool exterior = viewMode == FlightHudViewMode.Exterior;
        bool cockpit = viewMode == FlightHudViewMode.Cockpit;
        bool full = density == HudDensity.Full;
        bool clean = density == HudDensity.Clean;

        // Secondary reference panels (loads/trajectory, orbit/vehicle, event log) are the
        // first thing to go: everything they carry is diagnostic, not fly-the-vehicle data.
        _leftRoot.Visible = exterior && full;
        _rightRoot.Visible = exterior && full;
        _bottomRoot.Visible = exterior && !clean;
        _timeRoot.Visible = exterior && !clean;
        ApplyBandScale(full);

        bool banner = (exterior || cockpit) && !clean;
        bool criticalOnly = clean && (exterior || cockpit) && HasCriticalAlert();
        _phaseRoot.Visible = banner || criticalOnly;
        _phaseLabel.Visible = banner;
        _launchPathLabel.Visible = banner;
        _guidanceLabel.Visible = banner && _guidanceLabel.Text.Length > 0;
        _boosterLabel.Visible = banner && _boosterLabel.Text.Length > 0;
        _navRow.Visible = banner && full;
        _phaseTrack.Visible = banner;
        _countdownRoot.Visible &= (exterior || cockpit) && !clean;

        // Attitude cluster (navball + child engines/strip) in every exterior density.
        bool cluster = exterior;
        bool instruments = exterior && !clean;
        _objectives.DensityAllowed = instruments;
        _navball.Visible = cluster;
        _navball.ProcessMode = cluster
            ? ProcessModeEnum.Inherit
            : ProcessModeEnum.Disabled;
        _engineGrid.Visible = cluster;
        _attitudeStrip.Visible = cluster;
        _engineGrid.ApplyDensityLayout();
        // Sit above the SPEED/ALT band in Minimal/Full; drop lower when Clean hides it.
        _navball.SetClusterBottomOffset(clean ? -36f : -108f);
    }

    private bool HasCriticalAlert() =>
        _snapshot?.Alerts.Any(a => a.Severity == FlightAlertSeverity.Critical) == true;

    /// <summary>MINIMAL keeps SPEED/ALTITUDE/T+ but at a compact size.</summary>
    private void ApplyBandScale(bool full)
    {
        if (_bandScaleFull == full) return;
        _bandScaleFull = full;
        foreach (var value in new[] { _bigSpeed, _bigAlt, _bigTime })
        {
            var box = value.GetParent();
            value.AddThemeFontSizeOverride("font_size", full ? 34 : 23);
            for (int i = 0; i < box.GetChildCount(); i++)
            {
                if (box.GetChild(i) is Label label && label != value)
                    label.AddThemeFontSizeOverride("font_size", full ? 13 : 10);
            }
        }
    }

    private bool _bandScaleFull = true;

    /// <summary>
    /// Banner-level control-loss / degraded cue after structural breakup (overrides deorbit line).
    /// </summary>
    private void UpdateControlAuthorityCue(
        Exosphere.Simulation.Vessel vessel, MissionManager mission)
    {
        bool onPad = mission.Phase is MissionPhase.PRE_LAUNCH
            or MissionPhase.COUNTDOWN or MissionPhase.IGNITION;
        if (onPad) return;

        double auth = vessel.ControlAuthorityFactor;
        if (vessel.StructuralControlLost)
        {
            _launchPathLabel.Text = "CONTROL LOST — STRUCTURAL";
            _launchPathLabel.AddThemeColorOverride("font_color", FuelLowCol);
            return;
        }

        if (Exosphere.Simulation.Flight.ControlAuthority.IsDegraded(auth))
        {
            _launchPathLabel.Text = auth <= Exosphere.Simulation.Flight.ControlAuthority.FlapsOnly + 0.01
                ? "CONTROL DEGRADED — FLAPS ONLY"
                : "CONTROL DEGRADED";
            _launchPathLabel.AddThemeColorOverride("font_color", WarnCol);
        }
    }

    private void UpdateEntryInterfaceLatch(MissionPhase phase)
    {
        if (phase is MissionPhase.PRE_LAUNCH or MissionPhase.ORBIT
            or MissionPhase.COUNTDOWN or MissionPhase.IGNITION)
        {
            _pastEntryInterface = false;
            return;
        }

        if (phase is MissionPhase.ENTRY or MissionPhase.PEAK_HEATING
            or MissionPhase.AERO_DESCENT or MissionPhase.FINAL_DESCENT
            or MissionPhase.LANDED or MissionPhase.CAUGHT)
        {
            _pastEntryInterface = true;
        }
    }

    /// <summary>
    /// C3 actionable cue under the phase title for COAST / RETRO_BURN / ORBIT-with-Pe-in-atmo.
    /// Skipped while pad callouts own <see cref="_launchPathLabel"/>. THERMAL stays on EDL overlay.
    /// </summary>
    private void UpdateDeorbitEdlCue(
        MissionManager mission,
        double peAltitudeM,
        double atmosphereMaxAltitudeM,
        double timeToPeriapsisSec)
    {
        bool onPad = mission.Phase is MissionPhase.PRE_LAUNCH
            or MissionPhase.COUNTDOWN or MissionPhase.IGNITION;
        if (onPad)
            return; // UpdateLaunchPathCallout owns the secondary line on the pad.

        bool peInAtmo = MissionPhaseTrack.PeriapsisInAtmosphere(
            peAltitudeM, atmosphereMaxAltitudeM);
        string? cue = MissionPhaseTrack.FormatActionableCue(
            mission.Phase.ToString(),
            peInAtmo,
            timeToPeriapsisSec,
            afterEntryInterface: _pastEntryInterface);

        if (cue == null)
        {
            // Clear only when we previously wrote a deorbit/EDL cue (don't blank pad leftovers
            // after liftoff — those are already cleared by UpdateLaunchPathCallout).
            if (_launchPathLabel.Text.Contains("ENTRY INTERFACE")
                || _launchPathLabel.Text.Contains("DEORBIT"))
                _launchPathLabel.Text = "";
            return;
        }

        _launchPathLabel.Text = cue;
        _launchPathLabel.AddThemeColorOverride("font_color",
            mission.Phase is MissionPhase.RETRO_BURN or MissionPhase.ENTRY
                ? WarnCol
                : Accent);
    }

    private void UpdatePadHelp(MissionManager mission)
    {
        // Auto-hide once the stack leaves the pad. Phase>=LIFTOFF alone was not enough:
        // a WASD soft-disengage during IGNITION could leave MissionManager stuck in
        // IGNITION after clamps released, so this overlay covered the attitude cluster
        // for the whole climb. Also dismiss on altitude / ground-hold clear.
        if (!_padHelpAutoDismissed && ShouldAutoDismissPadHelp(mission))
        {
            _padHelpAutoDismissed = true;
            _padHelpDismissed = true;
        }

        _padHelpRoot.Visible = !_padHelpDismissed
            && _snapshot?.ViewMode == FlightHudViewMode.Exterior;
    }

    private static bool ShouldAutoDismissPadHelp(MissionManager mission)
    {
        // Dismiss as soon as the launch sequence commits (IGNITION+), not only after
        // LIFTOFF — otherwise the sheet sits over the attitude cluster through spool-up.
        if (mission.Phase >= MissionPhase.IGNITION)
            return true;

        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (vessel == null || universe == null)
            return false;

        if (!vessel.IsGroundHeld && mission.Phase is not MissionPhase.PRE_LAUNCH)
            return true;

        var body = universe.GetDominantBody(vessel.Position);
        return vessel.GetAltitude(body) > 80.0;
    }

    private void UpdateLaunchPathCallout(
        MissionManager mission,
        SimulationBridge bridge,
        FlightHudSnapshot snapshot)
    {
        bool onPad = mission.Phase is MissionPhase.PRE_LAUNCH
            or MissionPhase.COUNTDOWN or MissionPhase.IGNITION;
        if (!onPad)
        {
            _launchPathLabel.Text = "";
            return;
        }

        if (mission.Phase == MissionPhase.PRE_LAUNCH)
        {
            _launchPathLabel.Text = "AUTO SEQUENCE [L]  ·  MANUAL STARTUP [hold Z]";
            _launchPathLabel.AddThemeColorOverride("font_color", LabelDim);
            return;
        }

        if (bridge.IsIgnitionActive && !mission.IsCountingDown)
        {
            double twr = snapshot.ThrustToWeightRatio;
            double gate = HoldDownReleasePolicy.MinThrustToWeight;
            _launchPathLabel.Text = twr <= gate
                ? $"MANUAL STARTUP / HOLD (TWR {twr:F2} < {gate:F2})"
                : "MANUAL STARTUP / RELEASING CLAMPS";
            _launchPathLabel.AddThemeColorOverride("font_color", WarnCol);
            return;
        }

        if (mission.IsCountingDown)
        {
            double twr = snapshot.ThrustToWeightRatio;
            double gate = HoldDownReleasePolicy.MinThrustToWeight;
            if (mission.Phase == MissionPhase.IGNITION
                && snapshot.IsGroundHeld
                && twr <= gate)
            {
                _launchPathLabel.Text = mission.CountdownTimer <= 0.0
                    ? $"AUTO SEQUENCE / HOLD (TWR {twr:F2} < {gate:F2})"
                    : "AUTO SEQUENCE / ENGINE START";
                _launchPathLabel.AddThemeColorOverride("font_color", WarnCol);
            }
            else if (mission.Phase == MissionPhase.COUNTDOWN)
            {
                int secs = (int)System.Math.Ceiling(mission.CountdownTimer);
                _launchPathLabel.Text = $"AUTO SEQUENCE / T- {secs:00}";
                _launchPathLabel.AddThemeColorOverride("font_color", LabelDim);
            }
            else
            {
                _launchPathLabel.Text = "AUTO SEQUENCE / ENGINE START";
                _launchPathLabel.AddThemeColorOverride("font_color", WarnCol);
            }
        }
    }

    private void UpdateCountdown(MissionManager mission)
    {
        bool show = mission.Phase is MissionPhase.COUNTDOWN or MissionPhase.IGNITION;
        if (!show) { _countdownRoot.Visible = false; return; }

        _countdownRoot.Visible = true;
        double t = mission.CountdownTimer;
        int secs = (int)System.Math.Ceiling(t);

        if (mission.Phase == MissionPhase.LIFTOFF || secs <= 0)
        {
            _countdownLabel.Text = "LIFTOFF";
            _countdownLabel.AddThemeColorOverride("font_color", ValueBright);
            _countdownMilestone.Text = "VEHICLE HAS CLEARED THE TOWER";
        }
        else
        {
            _countdownLabel.Text = $"T- {secs:00}";
            _countdownLabel.AddThemeColorOverride("font_color", WarnCol);
            // SpaceX-style milestone callouts down the count.
            _countdownMilestone.Text = secs switch
            {
                > 7 => "STARTUP / GO FOR LAUNCH",
                > 4 => "ENGINE CHILL",
                > 2 => "IGNITION SEQUENCE START",
                _   => "ENGINE IGNITION",
            };
        }
    }

    private void UpdateEventLog(MissionPhase phase, double t)
    {
        if (phase != _lastPhase)
        {
            string stamp = FormatClock(t);
            string ev = phase switch
            {
                MissionPhase.LIFTOFF     => "LIFTOFF",
                MissionPhase.MAX_Q       => "MAX-Q",
                MissionPhase.MECO        => "MECO",
                MissionPhase.SEPARATION  => "STAGE SEP",
                MissionPhase.ASCENT_SHIP => "SHIP IGNITION",
                MissionPhase.ORBIT       => "SECO / ORBIT",
                MissionPhase.COAST       => "COAST",
                MissionPhase.ENTRY       => "ENTRY INTERFACE",
                MissionPhase.PEAK_HEATING => "PEAK HEATING",
                MissionPhase.AERO_DESCENT => "AERO DESCENT",
                MissionPhase.RETRO_BURN  => "RETRO BURN",
                MissionPhase.FINAL_DESCENT => "FINAL DESCENT",
                MissionPhase.LANDED      => "TOUCHDOWN",
                MissionPhase.CAUGHT      => "CAUGHT",
                MissionPhase.CRASHED     => "VEHICLE LOST",
                _ => null!,
            };
            if (ev != null)
            {
                _events.Insert(0, $"{stamp}  {ev}");
                if (_events.Count > 5) _events.RemoveAt(_events.Count - 1);
                _eventLog.Text = string.Join("\n", _events);
            }
            _lastPhase = phase;
        }
        if (_events.Count == 0) _eventLog.Text = "Awaiting launch";
    }

    private void UpdatePhaseTrack(MissionPhase current)
    {
        int currentIdx = MissionPhaseTrack.IndexOf(
            current.ToString(),
            afterEntryInterface: _pastEntryInterface && current == MissionPhase.RETRO_BURN);
        for (int i = 0; i < _phaseDots.Count; i++)
        {
            if (currentIdx < 0)        _phaseDots[i].Color = GaugeTrack;
            else if (i < currentIdx)   _phaseDots[i].Color = new Color(Accent, 0.45f);
            else if (i == currentIdx)  _phaseDots[i].Color = PhaseColor(current);
            else                       _phaseDots[i].Color = GaugeTrack;
        }
    }

    // ── Keyboard input ──────────────────────────────────────────────────────
    public override void _UnhandledInput(InputEvent @event)
    {
        var bridge = SimulationBridge.Instance;
        if (bridge == null) return;

        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            switch (key.Keycode)
            {
                case Key.Escape:
                    GetTree().ChangeSceneToFile("res://scenes/ui/MainMenu.tscn");
                    GetViewport().SetInputAsHandled();
                    break;
                // [Z]/[X] son hold-throttle: se sondean en _Process (mantener para
                // encender/acelerar / bajar). Aquí solo van las acciones de pulsación única.
                // [Z]/[X] are hold-throttle, polled in _Process; only one-shot actions here.
                case Key.Space:
                    bridge.TriggerStaging();
                    break;
                case Key.T:
                    bridge.SetSAS(!(bridge.ActiveVessel?.SASEnabled ?? true));
                    break;
                case Key.L:
                    MissionManager.Instance?.StartCountdown();
                    break;
                case Key.O:
                    bridge.JumpToOrbit();
                    break;
                case Key.R:
                    OnReentryDemoPressed();
                    GetViewport().SetInputAsHandled();
                    break;
                case Key.V:
                    GetTree().ChangeSceneToFile("res://scenes/construction/Construction.tscn");
                    GetViewport().SetInputAsHandled();
                    break;
                // [,] / [.] time warp is handled by WarpController alone (C2 dedupe):
                // both handlers used to fire, stepping the warp index twice per press.
                case Key.F1:
                    _padHelpDismissed = !_padHelpDismissed;
                    GetViewport().SetInputAsHandled();
                    break;
                case Key.F3:
                    CycleHudDensity();
                    GetViewport().SetInputAsHandled();
                    break;
                case Key.F2:
                    if (_snapshot?.Alerts.FirstOrDefault(a => !a.Acknowledged) is { } alert)
                        _presenter.AcknowledgeAlert(alert.Code);
                    GetViewport().SetInputAsHandled();
                    break;
                case Key.F8:
                    if (bridge.InjectActiveEngineFailure())
                    {
                        _events.Insert(
                            0,
                            $"{FormatClock(bridge.Universe.CurrentTime)}  ENGINE OUT TEST");
                        if (_events.Count > 5)
                            _events.RemoveAt(_events.Count - 1);
                    }
                    GetViewport().SetInputAsHandled();
                    break;
                case Key.F5:
                    SaveSystem.SaveGame("quicksave");
                    PushToast("QUICKSAVE");
                    GetViewport().SetInputAsHandled();
                    break;
                case Key.F9:
                    if (SaveSystem.LoadGame("quicksave"))
                        PushToast("QUICKLOAD");
                    else
                        PushToast("NO QUICKSAVE");
                    GetViewport().SetInputAsHandled();
                    break;
            }
        }
    }

    private void PushToast(string message)
    {
        GD.Print($"[HUD] {message}");
        var bridge = SimulationBridge.Instance;
        double t = bridge?.Universe.CurrentTime ?? 0.0;
        _events.Insert(0, $"{FormatClock(t)}  {message}");
        if (_events.Count > 5) _events.RemoveAt(_events.Count - 1);
        if (_eventLog != null)
            _eventLog.Text = string.Join("\n", _events);
    }

    // ── Formatting helpers ──────────────────────────────────────────────────

    private static string FormatPhase(MissionPhase phase) => phase switch
    {
        MissionPhase.PRE_LAUNCH  => "PRE-LAUNCH",
        MissionPhase.ASCENT_SH   => "ASCENT / SUPER HEAVY",
        MissionPhase.MAX_Q       => "MAX-Q",
        MissionPhase.MECO        => "MECO",
        MissionPhase.ASCENT_SHIP => "ASCENT / UPPER STAGE",
        MissionPhase.AERO_DESCENT => "AERO DESCENT",
        MissionPhase.PEAK_HEATING => "PEAK HEATING",
        MissionPhase.FINAL_DESCENT => "FINAL DESCENT",
        MissionPhase.RETRO_BURN  => "RETRO BURN",
        _ => phase.ToString().Replace("_", " "),
    };

    private static Color PhaseColor(MissionPhase phase) => phase switch
    {
        MissionPhase.COUNTDOWN or MissionPhase.IGNITION => WarnCol,
        MissionPhase.MAX_Q or MissionPhase.PEAK_HEATING or MissionPhase.CRASHED => FuelLowCol,
        MissionPhase.LANDED or MissionPhase.CAUGHT => InterfaceTheme.Success,
        _ => ValueBright,
    };

    private static string FormatDistance(double meters)
    {
        if (System.Math.Abs(meters) >= 1e9)  return $"{meters / 1e9:F3} Gm";
        if (System.Math.Abs(meters) >= 1e6)  return $"{meters / 1e6:F3} Mm";
        if (System.Math.Abs(meters) >= 1000) return $"{meters / 1000.0:F1} km";
        return $"{meters:F0} m";
    }

    private static string FormatClock(double seconds)
    {
        if (seconds < 0) return "00:00:00";
        int h = (int)(seconds % 86400 / 3600);
        int m = (int)(seconds % 3600 / 60);
        int s = (int)(seconds % 60);
        return $"{h:00}:{m:00}:{s:00}";
    }
}
