namespace Exosphere.Game;

using Godot;
using System.Linq;

public partial class HUDController : Control
{
    // ── Palette ─────────────────────────────────────────────────────────────
    private static readonly Color PanelBg     = new(0.04f, 0.06f, 0.09f, 0.55f);
    private static readonly Color PanelBorder = new(0.35f, 0.65f, 0.95f, 0.55f);
    private static readonly Color LabelDim    = new(0.62f, 0.70f, 0.80f, 1f);
    private static readonly Color ValueBright = new(0.92f, 0.96f, 1.00f, 1f);
    private static readonly Color Accent      = new(0.45f, 0.80f, 1.00f, 1f);
    private static readonly Color GaugeTrack  = new(0.12f, 0.16f, 0.22f, 0.90f);
    private static readonly Color ThrottleCol = new(1.00f, 0.62f, 0.15f, 1f);
    private static readonly Color FuelCol     = new(0.30f, 0.85f, 0.55f, 1f);
    private static readonly Color FuelLowCol  = new(0.95f, 0.40f, 0.30f, 1f);
    private static readonly Color GreenTwr    = new(0.30f, 1.00f, 0.45f, 1f);
    private static readonly Color RedTwr      = new(1.00f, 0.42f, 0.32f, 1f);

    // ── Value labels (left telemetry) ───────────────────────────────────────
    private Label _altValue   = null!;
    private Label _speedValue = null!;
    private Label _throttleValue = null!;
    private Label _fuelValue  = null!;
    private Label _massValue   = null!;
    private Label _twrValue    = null!;
    private ColorRect _throttleFill = null!;
    private ColorRect _fuelFill     = null!;
    private float _throttleTrackW;
    private float _fuelTrackW;

    // ── Value labels (right orbital) ────────────────────────────────────────
    private Label _apValue   = null!;
    private Label _peValue   = null!;
    private Label _timeValue = null!;
    private Label _warpValue = null!;

    // ── Phase banner / progress / countdown ─────────────────────────────────
    private Label _phaseLabel  = null!;
    private HBoxContainer _phaseTrack = null!;
    private readonly System.Collections.Generic.List<ColorRect> _phaseDots = new();
    private Label _countdownLabel = null!;

    // Phases shown on the progress strip (the main flight sequence).
    private static readonly MissionPhase[] PhaseSequence =
    {
        MissionPhase.COUNTDOWN, MissionPhase.LIFTOFF, MissionPhase.ASCENT_SH,
        MissionPhase.MAX_Q, MissionPhase.MECO, MissionPhase.SEPARATION,
        MissionPhase.ASCENT_SHIP, MissionPhase.ORBIT,
    };

    public override void _Ready()
    {
        BuildLeftPanel();
        BuildRightPanel();
        BuildPhaseBanner();
        BuildCountdown();
        BuildControlsHint();
    }

    // ── Panel construction ──────────────────────────────────────────────────

    private void BuildLeftPanel()
    {
        var panel = MakePanel();
        panel.OffsetLeft = 18; panel.OffsetTop = 18;
        AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 7);
        panel.AddChild(vbox);

        vbox.AddChild(MakeHeader("VESSEL TELEMETRY"));

        _altValue      = AddRow(vbox, "ALTITUDE", "---");
        _speedValue    = AddRow(vbox, "SURF SPEED", "---");
        _massValue     = AddRow(vbox, "MASS", "---");
        _twrValue      = AddRow(vbox, "TWR", "---");

        // Throttle gauge
        vbox.AddChild(MakeGaugeLabel("THROTTLE"));
        (_throttleFill, _throttleValue, _throttleTrackW) = AddGauge(vbox, ThrottleCol);

        // Fuel gauge
        vbox.AddChild(MakeGaugeLabel("FUEL"));
        (_fuelFill, _fuelValue, _fuelTrackW) = AddGauge(vbox, FuelCol);
    }

    private void BuildRightPanel()
    {
        var panel = MakePanel();
        panel.SetAnchorsPreset(LayoutPreset.TopRight);
        panel.GrowHorizontal = GrowDirection.Begin;
        panel.OffsetRight = -18; panel.OffsetTop = 18;
        AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 7);
        panel.AddChild(vbox);

        vbox.AddChild(MakeHeader("ORBITAL DATA"));
        _apValue   = AddRow(vbox, "APOAPSIS", "---");
        _peValue   = AddRow(vbox, "PERIAPSIS", "---");
        _timeValue = AddRow(vbox, "MISSION TIME", "T+00:00:00");
        _warpValue = AddRow(vbox, "TIME WARP", "Real Time");
    }

    private void BuildPhaseBanner()
    {
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.TopWide);
        center.OffsetTop = 14;
        center.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(center);

        var vbox = new VBoxContainer();
        vbox.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddThemeConstantOverride("separation", 6);
        center.AddChild(vbox);

        _phaseLabel = new Label { Text = "PRE-LAUNCH" };
        _phaseLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _phaseLabel.AddThemeFontSizeOverride("font_size", 30);
        _phaseLabel.AddThemeColorOverride("font_color", PhaseColor(MissionPhase.PRE_LAUNCH));
        _phaseLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        _phaseLabel.AddThemeConstantOverride("outline_size", 6);
        vbox.AddChild(_phaseLabel);

        // Mission progress strip
        _phaseTrack = new HBoxContainer();
        _phaseTrack.Alignment = BoxContainer.AlignmentMode.Center;
        _phaseTrack.AddThemeConstantOverride("separation", 6);
        vbox.AddChild(_phaseTrack);
        foreach (var _ in PhaseSequence)
        {
            var dot = new ColorRect
            {
                CustomMinimumSize = new Vector2(34, 4),
                Color = GaugeTrack,
            };
            _phaseDots.Add(dot);
            _phaseTrack.AddChild(dot);
        }
    }

    private void BuildCountdown()
    {
        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.Center);
        center.OffsetTop = -120;
        center.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(center);

        _countdownLabel = new Label { Text = "" };
        _countdownLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _countdownLabel.AddThemeFontSizeOverride("font_size", 80);
        _countdownLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.2f));
        _countdownLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        _countdownLabel.AddThemeConstantOverride("outline_size", 10);
        _countdownLabel.Visible = false;
        center.AddChild(_countdownLabel);
    }

    private void BuildControlsHint()
    {
        var hint = new Label
        {
            Text = "[Z/X] throttle   [W/S] pitch   [A/D] yaw   [Q/E] roll   [T] SAS   " +
                   "[,/.] warp   [Space] stage   [L] launch   [C] camera   [M] map",
        };
        hint.SetAnchorsPreset(LayoutPreset.BottomLeft);
        hint.GrowVertical = GrowDirection.Begin;
        hint.OffsetLeft = 18; hint.OffsetBottom = -12;
        hint.AddThemeColorOverride("font_color", new Color(0.55f, 0.60f, 0.66f, 0.85f));
        hint.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.7f));
        hint.AddThemeConstantOverride("outline_size", 3);
        hint.AddThemeFontSizeOverride("font_size", 12);
        AddChild(hint);
    }

    // ── Widget factories ────────────────────────────────────────────────────

    private static PanelContainer MakePanel()
    {
        var sb = new StyleBoxFlat
        {
            BgColor = PanelBg,
            BorderColor = PanelBorder,
            ContentMarginLeft = 14, ContentMarginRight = 14,
            ContentMarginTop = 10, ContentMarginBottom = 12,
        };
        sb.SetBorderWidthAll(1);
        sb.SetCornerRadiusAll(8);
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", sb);
        panel.CustomMinimumSize = new Vector2(290, 0);
        panel.MouseFilter = MouseFilterEnum.Ignore;
        return panel;
    }

    private static Label MakeHeader(string text)
    {
        var lbl = new Label { Text = text };
        lbl.AddThemeFontSizeOverride("font_size", 13);
        lbl.AddThemeColorOverride("font_color", Accent);
        return lbl;
    }

    private static Label MakeGaugeLabel(string text)
    {
        var lbl = new Label { Text = text };
        lbl.AddThemeFontSizeOverride("font_size", 12);
        lbl.AddThemeColorOverride("font_color", LabelDim);
        return lbl;
    }

    // A "LABEL ............ value" row using an HBox with a spacer.
    private static Label AddRow(VBoxContainer parent, string caption, string initial)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        var cap = new Label { Text = caption };
        cap.AddThemeFontSizeOverride("font_size", 14);
        cap.AddThemeColorOverride("font_color", LabelDim);
        cap.CustomMinimumSize = new Vector2(125, 0);
        row.AddChild(cap);

        var val = new Label { Text = initial };
        val.AddThemeFontSizeOverride("font_size", 16);
        val.AddThemeColorOverride("font_color", ValueBright);
        val.HorizontalAlignment = HorizontalAlignment.Right;
        val.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(val);

        parent.AddChild(row);
        return val;
    }

    // Horizontal bar gauge: a track ColorRect with a fill ColorRect overlaid,
    // plus a right-aligned numeric overlay. Returns (fill, valueLabel, trackWidth).
    private static (ColorRect fill, Label value, float trackWidth) AddGauge(
        VBoxContainer parent, Color fillColor)
    {
        const float TrackW = 262f, TrackH = 18f;

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
        value.AddThemeFontSizeOverride("font_size", 12);
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
        if (vessel == null || universe == null) return;

        // ── Rotation controls ──────────────────────────────────────────────
        double pitch = 0, yaw = 0, roll = 0;
        if (Input.IsKeyPressed(Key.W)) pitch += 1.0;
        if (Input.IsKeyPressed(Key.S)) pitch -= 1.0;
        if (Input.IsKeyPressed(Key.A)) yaw   -= 1.0;
        if (Input.IsKeyPressed(Key.D)) yaw   += 1.0;
        if (Input.IsKeyPressed(Key.Q)) roll  -= 1.0;
        if (Input.IsKeyPressed(Key.E)) roll  += 1.0;
        vessel.PitchYawRoll = new Exosphere.Simulation.Math.Vector3d(pitch, yaw, roll);

        var refBody = universe.GetDominantBody(vessel.Position);
        double alt   = vessel.GetAltitude(refBody);
        // True speed relative to the rotating surface (reads 0 when landed), not merely
        // relative to the body's orbital motion.
        double speed = vessel.GetSurfaceVelocity(refBody).Magnitude;

        // ── Left telemetry ─────────────────────────────────────────────────
        _altValue.Text   = FormatDistance(alt);
        _speedValue.Text = $"{speed:F1} m/s";
        _massValue.Text  = $"{vessel.TotalMass / 1000.0:F1} t";

        // Throttle gauge
        bool enginesActive = vessel.Parts.ActiveEngines.Any();
        double thr = vessel.Throttle;
        string engStatus = thr > 0.01 ? (enginesActive ? "FIRING" : "FLAME-OUT") : "OFF";
        _throttleValue.Text = $"{thr * 100:F0}%  {engStatus}";
        _throttleFill.Size = new Vector2(_throttleTrackW * (float)System.Math.Clamp(thr, 0, 1), 18);
        _throttleFill.Color = (thr > 0.01 && !enginesActive) ? FuelLowCol : ThrottleCol;

        // Fuel gauge (fraction of capacity)
        double fuel = vessel.Parts.TotalLiquidFuel + vessel.Parts.TotalOxidizer;
        double fuelCap = vessel.Parts.Parts.Sum(
            p => p.Definition.FuelCapacityLF + p.Definition.FuelCapacityOx);
        double fuelFrac = fuelCap > 0 ? fuel / fuelCap : 0;
        _fuelValue.Text = $"{fuel / 1000.0:F1} t";
        _fuelFill.Size  = new Vector2(_fuelTrackW * (float)System.Math.Clamp(fuelFrac, 0, 1), 18);
        _fuelFill.Color = fuelFrac < 0.15 ? FuelLowCol : FuelCol;

        // TWR
        if (vessel.TotalMass > 0)
        {
            double surfG = refBody.GetSurfaceGravity();
            double thrust = 0;
            foreach (var en in vessel.Parts.ActiveEngines)
                thrust += en.Definition.ThrustVac * en.ThrottleLevel;
            double twr = thrust / (vessel.TotalMass * surfG);
            if (thr > 0.01)
            {
                _twrValue.Text = $"{twr:F2}";
                _twrValue.AddThemeColorOverride("font_color", twr >= 1.0 ? GreenTwr : RedTwr);
            }
            else
            {
                _twrValue.Text = "---";
                _twrValue.AddThemeColorOverride("font_color", ValueBright);
            }
        }

        // ── Right orbital data ─────────────────────────────────────────────
        try
        {
            var relPos = vessel.Position - refBody.Position;
            var relVel = vessel.Velocity - refBody.Velocity;
            var el = Exosphere.Simulation.OrbitalElements.FromStateVector(
                relPos, relVel, refBody.GM, refBody.Id, universe.CurrentTime);
            _apValue.Text = FormatDistance(el.Apoapsis  - refBody.Radius);
            _peValue.Text = FormatDistance(el.Periapsis - refBody.Radius);
        }
        catch
        {
            _apValue.Text = "---";
            _peValue.Text = "---";
        }

        _timeValue.Text = FormatMissionTime(universe.CurrentTime);

        double ts = universe.TimeScale;
        _warpValue.Text = ts <= 1.0 ? "Real Time" : $"× {(int)ts}";
        _warpValue.AddThemeColorOverride("font_color", ts > 1.0 ? Accent : ValueBright);

        // ── Mission phase banner + progress ────────────────────────────────
        if (mission != null)
        {
            _phaseLabel.Text = FormatPhase(mission.Phase);
            _phaseLabel.AddThemeColorOverride("font_color", PhaseColor(mission.Phase));
            UpdatePhaseTrack(mission.Phase);

            // Countdown
            bool show = mission.Phase is MissionPhase.COUNTDOWN or MissionPhase.IGNITION;
            bool liftoffFlash = mission.Phase is MissionPhase.LIFTOFF
                && mission.CountdownTimer <= 0.0;
            if (show)
            {
                _countdownLabel.Visible = true;
                int secs = (int)System.Math.Ceiling(mission.CountdownTimer);
                if (secs > 0)
                {
                    _countdownLabel.Text = $"T- {secs:D2}";
                    _countdownLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.2f));
                }
                else
                {
                    _countdownLabel.Text = "LIFTOFF";
                    _countdownLabel.AddThemeColorOverride("font_color", new Color(1f, 0.55f, 0.1f));
                }
            }
            else
            {
                _countdownLabel.Visible = false;
            }
        }
    }

    private void UpdatePhaseTrack(MissionPhase current)
    {
        int currentIdx = System.Array.IndexOf(PhaseSequence, current);
        // IGNITION belongs to the countdown bucket on the strip.
        if (currentIdx < 0 && current == MissionPhase.IGNITION) currentIdx = 0;
        for (int i = 0; i < _phaseDots.Count; i++)
        {
            if (currentIdx < 0)
                _phaseDots[i].Color = GaugeTrack;                 // pre-launch: all dim
            else if (i < currentIdx)
                _phaseDots[i].Color = new Color(Accent, 0.45f);   // completed
            else if (i == currentIdx)
                _phaseDots[i].Color = PhaseColor(current);        // active
            else
                _phaseDots[i].Color = GaugeTrack;                 // upcoming
        }
    }

    // ── Keyboard input (unchanged) ──────────────────────────────────────────
    public override void _UnhandledInput(InputEvent @event)
    {
        var bridge = SimulationBridge.Instance;
        if (bridge == null) return;

        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            switch (key.Keycode)
            {
                case Key.Z:
                    bridge.SetThrottle(System.Math.Min(1.0, (bridge.ActiveVessel?.Throttle ?? 0) + 0.05));
                    break;
                case Key.X:
                    bridge.SetThrottle(System.Math.Max(0.0, (bridge.ActiveVessel?.Throttle ?? 0) - 0.05));
                    break;
                case Key.Space:
                    bridge.TriggerStaging();
                    break;
                case Key.T:
                    bridge.SetSAS(!(bridge.ActiveVessel?.SASEnabled ?? true));
                    break;
                case Key.L:
                    MissionManager.Instance?.StartCountdown();
                    break;
                case Key.Period:
                    bridge.SetTimeScale(System.Math.Min(1000.0, bridge.Universe.TimeScale * 2.0));
                    break;
                case Key.Comma:
                    bridge.SetTimeScale(System.Math.Max(1.0, bridge.Universe.TimeScale / 2.0));
                    break;
            }
        }
    }

    // ── Formatting helpers ──────────────────────────────────────────────────

    private static string FormatPhase(MissionPhase phase) => phase switch
    {
        MissionPhase.PRE_LAUNCH  => "PRE-LAUNCH",
        MissionPhase.ASCENT_SH   => "ASCENT · SUPER HEAVY",
        MissionPhase.MAX_Q       => "MAX-Q",
        MissionPhase.MECO        => "MECO",
        MissionPhase.ASCENT_SHIP => "ASCENT · STARSHIP",
        _ => phase.ToString().Replace("_", " "),
    };

    private static Color PhaseColor(MissionPhase phase) => phase switch
    {
        MissionPhase.PRE_LAUNCH  => new Color(0.70f, 0.78f, 0.88f),
        MissionPhase.COUNTDOWN   => new Color(1.00f, 0.80f, 0.20f),
        MissionPhase.IGNITION    => new Color(1.00f, 0.65f, 0.15f),
        MissionPhase.LIFTOFF     => new Color(1.00f, 0.55f, 0.12f),
        MissionPhase.ASCENT_SH   => new Color(1.00f, 0.45f, 0.20f),
        MissionPhase.MAX_Q       => new Color(1.00f, 0.30f, 0.25f),
        MissionPhase.MECO        => new Color(1.00f, 0.70f, 0.30f),
        MissionPhase.SEPARATION  => new Color(0.85f, 0.70f, 1.00f),
        MissionPhase.ASCENT_SHIP => new Color(0.55f, 0.80f, 1.00f),
        MissionPhase.ORBIT       => new Color(0.30f, 0.95f, 1.00f),
        MissionPhase.COAST       => new Color(0.45f, 0.90f, 1.00f),
        MissionPhase.ENTRY        => new Color(1.00f, 0.60f, 0.30f),
        MissionPhase.PEAK_HEATING => new Color(1.00f, 0.35f, 0.20f),
        MissionPhase.AERO_DESCENT => new Color(1.00f, 0.78f, 0.40f),
        MissionPhase.RETRO_BURN   => new Color(0.50f, 0.85f, 1.00f),
        MissionPhase.FINAL_DESCENT=> new Color(0.60f, 0.90f, 1.00f),
        MissionPhase.LANDED      => new Color(0.45f, 1.00f, 0.60f),
        _ => new Color(0.90f, 0.90f, 0.90f),
    };

    private static string FormatDistance(double meters)
    {
        if (System.Math.Abs(meters) >= 1e9)  return $"{meters / 1e9:F3} Gm";
        if (System.Math.Abs(meters) >= 1e6)  return $"{meters / 1e6:F3} Mm";
        if (System.Math.Abs(meters) >= 1000) return $"{meters / 1000.0:F1} km";
        return $"{meters:F0} m";
    }

    private static string FormatMissionTime(double seconds)
    {
        if (seconds < 0) return "T-00:00:00";
        int d = (int)(seconds / 86400);
        int h = (int)(seconds % 86400 / 3600);
        int m = (int)(seconds % 3600 / 60);
        int s = (int)(seconds % 60);
        return d > 0 ? $"T+{d}d {h:D2}:{m:D2}:{s:D2}" : $"T+{h:D2}:{m:D2}:{s:D2}";
    }
}
