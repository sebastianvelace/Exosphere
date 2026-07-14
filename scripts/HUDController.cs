namespace Exosphere.Game;

using Godot;
using System.Linq;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;

// ── Flight HUD (SpaceX-webcast aesthetic) ────────────────────────────────────
// Dark translucent panels, thin lines, condensed type, cyan/white accents. A big
// centred bottom telemetry band (SPEED / ALTITUDE / T+), a milestone countdown, a
// left "loads & trajectory" panel and a right "stages & Δv + event log" panel.
// The engine board (EngineGridHUD) and navball (NavballController) are spawned as
// children here. Reads ONLY existing public getters; all derived values (G-force,
// Δv, downrange, TWR, vertical speed) are computed in this HUD layer.
public partial class HUDController : Control
{
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
    private Label _altValue   = null!;
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
    private Label _warpValue = null!;
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
    private HBoxContainer _phaseTrack = null!;
    private readonly System.Collections.Generic.List<ColorRect> _phaseDots = new();
    private Label _countdownLabel = null!;
    private Label _countdownMilestone = null!;

    // ── Pad help overlay + launch path callout (UX-001 / UX-002) ─────────────
    private PanelContainer _padHelpRoot = null!;
    private Button _reentryDemoButton = null!;
    private Label _launchPathLabel = null!;
    private bool _padHelpDismissed;

    // ── Derived-state tracking ──────────────────────────────────────────────
    private Vector3d _lastVel;
    private double   _lastT = -1;
    private double   _gSmoothed;
    private MissionPhase _lastPhase = MissionPhase.PRE_LAUNCH;
    private bool     _maxqSeen;
    private bool     _pastEntryInterface;   // latch: RETRO_BURN after ENTRY → landing slot
    private Vector3d _launchSurfacePoint;   // body-relative; captured at liftoff
    private bool     _launchCaptured;
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

        // Spawn the engine board and navball as children.
        AddChild(new EngineGridHUD  { Name = "EngineGridHUD" });
        AddChild(new AttitudeNavball { Name = "Navball" });
    }

    // ── Panel construction ──────────────────────────────────────────────────

    private void BuildLeftPanel()
    {
        var panel = MakePanel();
        panel.OffsetLeft = 18; panel.OffsetTop = 18;
        AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        panel.AddChild(vbox);

        vbox.AddChild(MakeHeader("FLIGHT"));
        _altValue       = AddRow(vbox, "ALTITUDE", "---");
        _vspeedValue    = AddRow(vbox, "VERT SPEED", "---");
        _gValue         = AddRow(vbox, "G-FORCE", "---");
        _qValue         = AddRow(vbox, "DYN PRESS q", "---");
        _pitchValue     = AddRow(vbox, "FLIGHT PITCH", "---");
        _hdgValue       = AddRow(vbox, "HEADING", "---");
        _downrangeValue = AddRow(vbox, "DOWNRANGE", "---");

        _maxqFlag = new Label { Text = "" };
        _maxqFlag.AddThemeFontSizeOverride("font_size", 13);
        _maxqFlag.AddThemeColorOverride("font_color", WarnCol);
        _maxqFlag.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(_maxqFlag);
    }

    private void BuildRightPanel()
    {
        var panel = MakePanel();
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
        _suborbitalWarn.AddThemeFontSizeOverride("font_size", 13);
        _suborbitalWarn.AddThemeColorOverride("font_color", FuelLowCol);
        _suborbitalWarn.HorizontalAlignment = HorizontalAlignment.Center;
        _suborbitalWarn.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _suborbitalWarn.CustomMinimumSize = new Vector2(246, 0);
        vbox.AddChild(_suborbitalWarn);

        _warpValue = AddRow(vbox, "TIME WARP", "Real Time");

        vbox.AddChild(MakeGaugeLabel("LIQUID CH4"));
        (_lfFill, _lfValue, _lfTrackW) = AddGauge(vbox, FuelCol);
        vbox.AddChild(MakeGaugeLabel("LIQUID O2"));
        (_oxFill, _oxValue, _oxTrackW) = AddGauge(vbox, OxCol);

        vbox.AddChild(MakeHeader("EVENT LOG"));
        _eventLog = new Label { Text = "-" };
        _eventLog.AddThemeFontSizeOverride("font_size", 11);
        _eventLog.AddThemeColorOverride("font_color", LabelDim);
        _eventLog.CustomMinimumSize = new Vector2(246, 56);
        _eventLog.VerticalAlignment = VerticalAlignment.Top;
        vbox.AddChild(_eventLog);
    }

    private void BuildPhaseBanner()
    {
        var center = new PanelContainer();
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
        _phaseLabel.AddThemeFontSizeOverride("font_size", 16);
        _phaseLabel.AddThemeColorOverride("font_color", PhaseColor(MissionPhase.PRE_LAUNCH));
        _phaseLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        _phaseLabel.AddThemeConstantOverride("outline_size", 3);
        vbox.AddChild(_phaseLabel);

        _launchPathLabel = new Label { Text = "" };
        _launchPathLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _launchPathLabel.AddThemeFontSizeOverride("font_size", 11);
        _launchPathLabel.AddThemeColorOverride("font_color", LabelDim);
        _launchPathLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _launchPathLabel.CustomMinimumSize = new Vector2(580, 0);
        vbox.AddChild(_launchPathLabel);

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
        center.SetAnchorsPreset(LayoutPreset.BottomWide);
        center.GrowVertical = GrowDirection.Begin;
        center.OffsetBottom = -32;
        center.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(center);

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 260);
        hbox.Alignment = BoxContainer.AlignmentMode.Center;
        center.AddChild(hbox);

        _bigSpeed = AddBigStat(hbox, "SPEED", "0", "KM/H");
        _bigAlt   = AddBigStat(hbox, "ALTITUDE", "0.0", "KM");

        // T+ clock — centred, above the navball disc.
        var timeCenter = new CenterContainer();
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
        cap.AddThemeFontSizeOverride("font_size", 13);
        cap.AddThemeColorOverride("font_color", LabelDim);
        cap.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
        cap.AddThemeConstantOverride("outline_size", 4);
        vbox.AddChild(cap);

        var val = new Label { Text = value };
        val.HorizontalAlignment = HorizontalAlignment.Center;
        val.AddThemeFontSizeOverride("font_size", 34);
        val.AddThemeColorOverride("font_color", ValueBright);
        val.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        val.AddThemeConstantOverride("outline_size", 6);
        vbox.AddChild(val);

        if (unit.Length > 0)
        {
            var u = new Label { Text = unit };
            u.HorizontalAlignment = HorizontalAlignment.Center;
            u.AddThemeFontSizeOverride("font_size", 12);
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
        _countdownLabel.AddThemeFontSizeOverride("font_size", 48);
        _countdownLabel.AddThemeColorOverride("font_color", WarnCol);
        _countdownLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        _countdownLabel.AddThemeConstantOverride("outline_size", 7);
        vbox.AddChild(_countdownLabel);

        _countdownMilestone = new Label { Text = "" };
        _countdownMilestone.HorizontalAlignment = HorizontalAlignment.Center;
        _countdownMilestone.AddThemeFontSizeOverride("font_size", 15);
        _countdownMilestone.AddThemeColorOverride("font_color", LabelDim);
        _countdownMilestone.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        _countdownMilestone.AddThemeConstantOverride("outline_size", 6);
        vbox.AddChild(_countdownMilestone);

        center.Visible = false;
        _countdownRoot = center;
    }
    private CenterContainer _countdownRoot = null!;

    private void BuildPadHelpOverlay()
    {
        _padHelpRoot = new PanelContainer();
        _padHelpRoot.SetAnchorsPreset(LayoutPreset.Center);
        _padHelpRoot.OffsetLeft = -290;
        _padHelpRoot.OffsetTop = 40;
        _padHelpRoot.OffsetRight = 290;
        _padHelpRoot.OffsetBottom = 270;
        _padHelpRoot.AddThemeStyleboxOverride("panel", InterfaceTheme.GlassPanel(0.78f, 14, 22, 18));
        _padHelpRoot.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_padHelpRoot);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);
        _padHelpRoot.AddChild(vbox);

        var title = new Label { Text = "MISSION CONTROLS" };
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 14);
        title.AddThemeColorOverride("font_color", ValueBright);
        vbox.AddChild(title);

        vbox.AddChild(MakeHelpLine("[L]  AUTO LAUNCH SEQUENCE"));
        vbox.AddChild(MakeHelpLine("[hold Z]  MANUAL IGNITION / THROTTLE UP"));
        vbox.AddChild(MakeHelpLine("[hold X]  THROTTLE DOWN"));
        vbox.AddChild(MakeHelpLine("[G]  FULL ASCENT GUIDANCE"));
        vbox.AddChild(MakeHelpLine("[H]  PITCH ASSIST (HOLD / RELEASE)"));
        vbox.AddChild(MakeHelpLine("[M]  ORBITAL MAP    [Tab]  SOLAR VIEW"));
        vbox.AddChild(MakeHelpLine("[,] / [.]  TIME WARP"));

        _reentryDemoButton = new Button
        {
            Text = "VIEW STARSHIP REENTRY → LANDING",
            CustomMinimumSize = new Vector2(360, 38),
            TooltipText = "Start the verified physical EDL demonstration at the 70 km entry interface",
        };
        InterfaceTheme.StyleButton(_reentryDemoButton, primary: true);
        _reentryDemoButton.Pressed += OnReentryDemoPressed;
        vbox.AddChild(_reentryDemoButton);

        var dismiss = new Label { Text = "[R] reentry demo  ·  [F1] dismiss  ·  auto-hides after liftoff" };
        dismiss.HorizontalAlignment = HorizontalAlignment.Center;
        dismiss.AddThemeFontSizeOverride("font_size", 10);
        dismiss.AddThemeColorOverride("font_color", LabelDim);
        vbox.AddChild(dismiss);
    }

    private static Label MakeHelpLine(string text)
    {
        var lbl = new Label { Text = text };
        lbl.HorizontalAlignment = HorizontalAlignment.Center;
        lbl.AddThemeFontSizeOverride("font_size", 12);
        lbl.AddThemeColorOverride("font_color", LabelDim);
        return lbl;
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
        lbl.AddThemeFontSizeOverride("font_size", 11);
        lbl.AddThemeColorOverride("font_color", LabelDim);
        return lbl;
    }

    private static Label MakeGaugeLabel(string text)
    {
        var lbl = new Label { Text = text };
        lbl.AddThemeFontSizeOverride("font_size", 12);
        lbl.AddThemeColorOverride("font_color", LabelDim);
        return lbl;
    }

    private static Label AddRow(VBoxContainer parent, string caption, string initial)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        var cap = new Label { Text = caption };
        cap.AddThemeFontSizeOverride("font_size", 12);
        cap.AddThemeColorOverride("font_color", LabelDim);
        cap.CustomMinimumSize = new Vector2(118, 0);
        row.AddChild(cap);

        var val = new Label { Text = initial };
        val.AddThemeFontSizeOverride("font_size", 14);
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
        value.AddThemeFontSizeOverride("font_size", 9);
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

        // ── Rotation controls ──────────────────────────────────────────────
        double pitchIn = 0, yawIn = 0, rollIn = 0;
        if (Input.IsKeyPressed(Key.W)) pitchIn += 1.0;
        if (Input.IsKeyPressed(Key.S)) pitchIn -= 1.0;
        if (Input.IsKeyPressed(Key.A)) yawIn   -= 1.0;
        if (Input.IsKeyPressed(Key.D)) yawIn   += 1.0;
        if (Input.IsKeyPressed(Key.Q)) rollIn  -= 1.0;
        if (Input.IsKeyPressed(Key.E)) rollIn  += 1.0;
        vessel.PitchYawRoll = new Vector3d(pitchIn, yawIn, rollIn);

        // ── Hold-throttle (despegue manual) ─────────────────────────────────
        // [Z] mantenida: en tierra arranca la ignición (suelta el clamp al TWR>1.02);
        // ya en vuelo, sube el throttle de forma progresiva. [X] mantenida lo baja.
        // Hold [Z]: on the pad starts ignition (releases the hold-down at TWR>1.02);
        // already flying, spools the throttle up. Hold [X] spools it down.
        if (Input.IsPhysicalKeyPressed(Key.Z))
        {
            if (vessel.IsGroundHeld || bridge.IsIgnitionActive) bridge.Ignite();
            else                                                 bridge.ThrottleUp(delta);
        }
        else if (Input.IsPhysicalKeyPressed(Key.X))
        {
            bridge.ThrottleDown(delta);
        }

        var refBody = universe.GetDominantBody(vessel.Position);
        double alt   = vessel.GetAltitude(refBody);
        var surfVel  = vessel.GetSurfaceVelocity(refBody);
        double speed = surfVel.Magnitude;
        var up       = (vessel.Position - refBody.Position).Normalized;
        double vspeed = surfVel.Dot(up);                  // climb rate (m/s)

        // ── Proper acceleration felt by the crew (gravity-free in orbit) ───
        double gNow = vessel.GetProperAcceleration(refBody).Magnitude / 9.80665;
        _lastVel = vessel.Velocity;
        _lastT   = universe.CurrentTime;
        _gSmoothed = _gSmoothed + (gNow - _gSmoothed) * 0.2;

        // ── Dynamic pressure ───────────────────────────────────────────────
        double q = vessel.GetDynamicPressure(refBody);

        // ── Flight pitch / heading (velocity-vector based) ─────────────────
        double flightPitch = 0, heading = 0;
        if (speed > 0.5)
        {
            var vdir = surfVel.Normalized;
            flightPitch = System.Math.Asin(System.Math.Clamp(vdir.Dot(up), -1, 1)) * 180.0 / System.Math.PI;
            var spinAxis = refBody.RotationAxis;
            var north = spinAxis - up * spinAxis.Dot(up);
            if (north.MagnitudeSquared > 1e-9)
            {
                north = north.Normalized;
                var east = north.Cross(up).Normalized;
                var vh = vdir - up * vdir.Dot(up);
                if (vh.MagnitudeSquared > 1e-9)
                {
                    vh = vh.Normalized;
                    heading = (System.Math.Atan2(vh.Dot(east), vh.Dot(north)) * 180.0 / System.Math.PI + 360.0) % 360.0;
                }
            }
        }

        // ── Downrange (great-circle from launch surface point) ─────────────
        if (!_launchCaptured && (mission?.Phase is MissionPhase.LIFTOFF or MissionPhase.ASCENT_SH) && alt > 30)
        {
            var inertial = (vessel.Position - refBody.Position).Normalized;
            _launchSurfacePoint = refBody.ToBodyFixedDirection(
                inertial, universe.CurrentTime).Normalized;
            _launchCaptured = true;
        }
        double downrange = 0;
        if (_launchCaptured)
        {
            var inertialNow = (vessel.Position - refBody.Position).Normalized;
            var now = refBody.ToBodyFixedDirection(
                inertialNow, universe.CurrentTime).Normalized;
            double cosAng = System.Math.Clamp(now.Dot(_launchSurfacePoint), -1, 1);
            downrange = System.Math.Acos(cosAng) * refBody.Radius;
        }

        // ── Left panel ──────────────────────────────────────────────────────
        _altValue.Text    = FormatDistance(alt);
        _vspeedValue.Text = $"{vspeed:+0.0;-0.0} m/s";
        _gValue.Text      = $"{_gSmoothed:F2} g";
        _gValue.AddThemeColorOverride("font_color", _gSmoothed > 4.0 ? WarnCol : ValueBright);
        _qValue.Text      = $"{q / 1000.0:F1} kPa";
        _pitchValue.Text  = speed > 0.5 ? $"{flightPitch:F0}°" : "---";
        _hdgValue.Text    = speed > 0.5 ? $"{heading:F0}°" : "---";
        _downrangeValue.Text = _launchCaptured ? FormatDistance(downrange) : "---";

        // MAX-Q flag: latch once q peaks and starts falling in atmosphere.
        if (mission?.Phase == MissionPhase.MAX_Q) { _maxqFlag.Text = "◆ MAX-Q ◆"; _maxqSeen = true; }
        else if (_maxqSeen) _maxqFlag.Text = "max-q passed";
        else _maxqFlag.Text = "";

        // ── Right panel: mass, Δv, orbit ───────────────────────────────────
        _massValue.Text = $"{vessel.TotalMass / 1000.0:F1} t";

        // Stage Δv = Isp·g0·ln(m0/m1) for the current stage's engines & propellant.
        _dvValue.Text = FormatDv(vessel, refBody);

        bool suborbital = false;
        double peAlt = double.NaN;
        double timeToPe = double.NaN;
        double atmoMax = refBody.Atmosphere?.MaxAltitude ?? double.NaN;
        try
        {
            var relPos = vessel.Position - refBody.Position;
            var relVel = vessel.Velocity - refBody.Velocity;
            var el = Exosphere.Simulation.OrbitalElements.FromStateVector(
                relPos, relVel, refBody.GM, refBody.Id, universe.CurrentTime);
            peAlt = el.Periapsis - refBody.Radius;
            _apValue.Text = FormatDistance(el.Apoapsis - refBody.Radius);

            // Periapsis bajo la superficie ⇒ la órbita cruza el cuerpo: trayectoria
            // suborbital/radial que va a impactar. Mostramos un aviso en rojo en lugar de
            // un número negativo confuso (que no comunica que vas a estrellarte).
            // Periapsis below the surface ⇒ the orbit crosses the body: a suborbital/radial
            // impact trajectory. Show a red warning instead of a confusing negative number.
            suborbital = peAlt < 0;
            if (suborbital)
            {
                _peValue.Text = "IMPACT";
                _peValue.AddThemeColorOverride("font_color", FuelLowCol);
            }
            else
            {
                _peValue.Text = FormatDistance(peAlt);
                _peValue.AddThemeColorOverride("font_color", ValueBright);
            }

            if (!el.IsRadial && !el.IsHyperbolic)
            {
                timeToPe = MissionPhaseTrack.ApproximateTimeToPeriapsisSec(
                    el.SemiMajorAxis,
                    el.Eccentricity,
                    el.GetMeanAnomaly(universe.CurrentTime, refBody.GM),
                    refBody.GM);
            }
        }
        catch
        {
            _apValue.Text = "---"; _peValue.Text = "---";
            _peValue.AddThemeColorOverride("font_color", ValueBright);
        }
        _suborbitalWarn.Text = suborbital ? "SUBORBITAL / IMPACT TRAJECTORY" : "";

        double ts = universe.TimeScale;
        if (universe.CurrentTime < bridge.WarpClampReasonUntil && bridge.WarpClampReason != null)
        {
            _warpValue.Text = ts <= 1.0
                ? $"Real Time  ·  {bridge.WarpClampReason}"
                : $"× {(int)ts}  ·  {bridge.WarpClampReason}";
            _warpValue.AddThemeColorOverride("font_color", WarnCol);
        }
        else
        {
            _warpValue.Text = ts <= 1.0 ? "Real Time" : $"× {(int)ts}";
            _warpValue.AddThemeColorOverride("font_color", ts > 1.0 ? Accent : ValueBright);
        }

        // ── Propellant bars (fuel vs oxidizer fractions) ───────────────────
        double lf = vessel.Parts.TotalLiquidFuel;
        double ox = vessel.Parts.TotalOxidizer;
        double lfCap = vessel.Parts.Parts.Sum(p => p.Definition.FuelCapacityLF);
        double oxCap = vessel.Parts.Parts.Sum(p => p.Definition.FuelCapacityOx);
        double lfFrac = lfCap > 0 ? lf / lfCap : 0;
        double oxFrac = oxCap > 0 ? ox / oxCap : 0;
        _lfValue.Text = $"{lf / 1000.0:F1} t";
        _oxValue.Text = $"{ox / 1000.0:F1} t";
        _lfFill.Size = new Vector2(_lfTrackW * (float)System.Math.Clamp(lfFrac, 0, 1), 8);
        _oxFill.Size = new Vector2(_oxTrackW * (float)System.Math.Clamp(oxFrac, 0, 1), 8);
        _lfFill.Color = lfFrac < 0.12 ? FuelLowCol : FuelCol;
        _oxFill.Color = oxFrac < 0.12 ? FuelLowCol : OxCol;

        // ── Big bottom band ────────────────────────────────────────────────
        _bigSpeed.Text = $"{speed * 3.6:N0}";
        _bigAlt.Text   = alt >= 1000 ? $"{alt / 1000.0:F1}" : $"{alt:F0}";
        ((Label)_bigAlt.GetParent().GetChild(2)).Text = alt >= 1000 ? "KM" : "M";
        _bigTime.Text  = FormatClock(universe.CurrentTime);

        // ── Mission phase banner + progress + event log ────────────────────
        if (mission != null)
        {
            UpdateEntryInterfaceLatch(mission.Phase);
            _phaseLabel.Text = FormatPhase(mission.Phase);
            _phaseLabel.AddThemeColorOverride("font_color", PhaseColor(mission.Phase));
            UpdatePhaseTrack(mission.Phase);
            UpdateEventLog(mission.Phase, universe.CurrentTime);
            UpdateCountdown(mission);
            UpdateLaunchPathCallout(mission, bridge, vessel, refBody);
            UpdateDeorbitEdlCue(mission, peAlt, atmoMax, timeToPe);
            UpdateControlAuthorityCue(vessel, mission);
            UpdatePadHelp(mission);
        }
    }

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
            _launchPathLabel.AddThemeColorOverride("font_color", new Color(1f, 0.25f, 0.22f));
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
            or MissionPhase.LANDED)
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
        if (mission.Phase >= MissionPhase.LIFTOFF)
            _padHelpDismissed = true;

        bool onPad = mission.Phase is MissionPhase.PRE_LAUNCH
            or MissionPhase.COUNTDOWN or MissionPhase.IGNITION;
        _padHelpRoot.Visible = onPad && !_padHelpDismissed;
    }

    private void UpdateLaunchPathCallout(
        MissionManager mission,
        SimulationBridge bridge,
        Exosphere.Simulation.Vessel vessel,
        Exosphere.Simulation.CelestialBody refBody)
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
            double twr = vessel.GetThrustToWeightRatio(refBody);
            _launchPathLabel.Text = twr <= 1.02
                ? $"MANUAL STARTUP — HOLD (TWR {twr:F2} < 1.02)"
                : "MANUAL STARTUP — RELEASING CLAMPS";
            _launchPathLabel.AddThemeColorOverride("font_color", WarnCol);
            return;
        }

        if (mission.IsCountingDown)
        {
            double twr = vessel.GetThrustToWeightRatio(refBody);
            if (mission.Phase == MissionPhase.IGNITION && vessel.IsGroundHeld && twr <= 1.02)
            {
                _launchPathLabel.Text = mission.CountdownTimer <= 0.0
                    ? $"AUTO SEQUENCE — HOLD (TWR {twr:F2} < 1.02)"
                    : "AUTO SEQUENCE — ENGINE START";
                _launchPathLabel.AddThemeColorOverride("font_color", WarnCol);
            }
            else if (mission.Phase == MissionPhase.COUNTDOWN)
            {
                int secs = (int)System.Math.Ceiling(mission.CountdownTimer);
                _launchPathLabel.Text = $"AUTO SEQUENCE — T- {secs:00}";
                _launchPathLabel.AddThemeColorOverride("font_color", LabelDim);
            }
            else
            {
                _launchPathLabel.Text = "AUTO SEQUENCE — ENGINE START";
                _launchPathLabel.AddThemeColorOverride("font_color", WarnCol);
            }
        }
    }

    // Stage Δv (rocket equation): m0 = total mass, m1 = total − current-stage propellant.
    private static string FormatDv(
        Exosphere.Simulation.Vessel vessel,
        Exosphere.Simulation.CelestialBody body)
    {
        double dv = vessel.GetCurrentStageDeltaV(body);
        if (dv <= 0.0) return "---";
        return $"{dv:N0} m/s";
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
                case Key.Period:
                    bridge.SetWarpIndex(bridge.WarpIndex + 1);
                    break;
                case Key.Comma:
                    bridge.SetWarpIndex(bridge.WarpIndex - 1);
                    break;
                case Key.F1:
                    _padHelpDismissed = !_padHelpDismissed;
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
        MissionPhase.ASCENT_SHIP => "ASCENT / STARSHIP",
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
        MissionPhase.LANDED => new Color(0.55f, 0.95f, 0.65f, 1f),
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
