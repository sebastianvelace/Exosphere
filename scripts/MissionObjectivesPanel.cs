namespace Exosphere.Game;

using System.Collections.Generic;
using System.Linq;
using Exosphere.Simulation.Campaign;
using Godot;

/// <summary>
/// In-flight mission checklist (C1).
/// <para>
/// <see cref="CampaignRuntime"/> already evaluates every objective and limit against live
/// telemetry on each frame and stores the result in <see cref="CampaignRuntime.LastDebrief"/>;
/// until now that state was only ever read once, by <see cref="CampaignDebriefPanel"/>, after
/// the flight was over. This panel renders the same data while the player can still act on it.
/// </para>
/// <para>
/// Presentation follows <see cref="CampaignDebriefPanel"/>: mono status column, body-font
/// title, muted mono evidence. The status vocabulary differs because the mission is still
/// running — an unmet objective is PENDING, not a failure; only a breached limit is FAIL.
/// Sandbox flights have no <see cref="MissionDirector"/> and hide the panel entirely.
/// </para>
/// </summary>
public partial class MissionObjectivesPanel : PanelContainer
{
    /// A flight HUD is not a debrief: optional rows stop once the list reaches this length and
    /// are summarised in one line. Required objectives and breached limits are always shown.
    private const int MaxRows = 8;
    private const float GuideWidth = 340f;
    private const double RefreshIntervalSeconds = 0.25;

    private sealed record Row(HBoxContainer Root, Label Status, Label Title, Label Value);

    private Label _header = null!;
    private Label _guideNow = null!;
    private Label _guideHint = null!;
    private Label _guideNext = null!;
    private Label _hiddenSummary = null!;
    private VBoxContainer _rowBox = null!;
    private readonly List<Row> _rows = new();
    private readonly Dictionary<string, MissionRuleDefinition> _ruleById = new();
    private string _ruleCacheMissionId = "";
    private double _sinceRefresh = RefreshIntervalSeconds;

    /// <summary>Density gate driven by <see cref="HUDController"/>; Clean hides the checklist.</summary>
    public bool DensityAllowed { get; set; } = true;

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", InterfaceTheme.PanelStyle(0.88f, 14, 11));
        SetAnchorsPreset(LayoutPreset.TopLeft);
        CustomMinimumSize = new Vector2(GuideWidth, 0);
        OffsetLeft = 18;
        // Below the TIME readout (y ≈ 232–298), which used to cover the guide's first line.
        OffsetTop = 312;
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 5);
        AddChild(content);

        // Mission guide: the step to do now, how to do it, and what comes next.
        _guideNow = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(GuideWidth - 28, 0) };
        InterfaceTheme.ApplyBody(_guideNow, 13);
        _guideNow.AddThemeColorOverride("font_color", InterfaceTheme.Text);
        content.AddChild(_guideNow);
        _guideHint = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(GuideWidth - 28, 0) };
        InterfaceTheme.ApplyBody(_guideHint, 11);
        _guideHint.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        content.AddChild(_guideHint);
        _guideNext = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(GuideWidth - 28, 0) };
        InterfaceTheme.ApplyMono(_guideNext, 9);
        _guideNext.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        content.AddChild(_guideNext);
        content.AddChild(new HSeparator());

        _header = new Label { Text = "MISSION OBJECTIVES" };
        InterfaceTheme.ApplyLabel(_header, 12);
        _header.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        content.AddChild(_header);

        _rowBox = new VBoxContainer();
        _rowBox.AddThemeConstantOverride("separation", 3);
        content.AddChild(_rowBox);

        _hiddenSummary = new Label();
        InterfaceTheme.ApplyMono(_hiddenSummary, 9);
        _hiddenSummary.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        content.AddChild(_hiddenSummary);
    }

    public override void _Process(double delta)
    {
        var director = CampaignRuntime.Instance?.Director;
        var debrief = CampaignRuntime.Instance?.LastDebrief;
        if (!DensityAllowed || director == null || debrief == null)
        {
            Visible = false;
            return;
        }

        Visible = true;
        _sinceRefresh += delta;
        if (_sinceRefresh < RefreshIntervalSeconds) return;
        _sinceRefresh = 0.0;
        Render(director.Definition, debrief);
        RenderGuide(director, debrief);
    }

    private void RenderGuide(MissionDirector director, MissionDebrief debrief)
    {
        var procedure = director.Definition.Procedure;
        bool spanish = UserInterfaceSettings.Language == InterfaceLanguage.Spanish;
        bool hasGuide = procedure.Count > 0;
        _guideNow.Visible = _guideHint.Visible = _guideNext.Visible = hasGuide;
        if (!hasGuide) return;

        int current = MissionGuide.CurrentStepIndex(director.Definition, director.Evidence, debrief);
        if (current >= procedure.Count)
        {
            _guideNow.Text = spanish ? "PROCEDIMIENTO COMPLETO" : "PROCEDURE COMPLETE";
            _guideHint.Text = "";
            _guideNext.Text = "";
            return;
        }
        var step = procedure[current];
        bool assisted = HistoricalFlightProfileController.Instance?.IsEngaged == true;
        var mission = MissionManager.Instance;
        bool waitingToLaunch = mission?.IsHoldingForLaunchWindow == true || mission?.IsCountingDown == true;
        string prefix = spanish ? $"PASO {current + 1}/{procedure.Count} · " : $"STEP {current + 1}/{procedure.Count} · ";
        _guideNow.Text = prefix + (spanish ? step.TitleEs : step.TitleEn);
        _guideHint.Text = waitingToLaunch
            ? (spanish ? "Cuenta en curso: si hay ventana lunar, el reloj acelera solo hasta que abra. Control de Misión lanza en T-0."
                : "Count in progress: for a lunar window the clock speeds up on its own until it opens. Mission Control launches at T-0.")
            : assisted
            ? (spanish ? "Control de Misión está volando. [K] te devuelve el control." : "Mission Control is flying. [K] gives you control back.")
            : (spanish ? step.HintEs : step.HintEn);
        var upcoming = procedure.Skip(current + 1).Take(2).Select(next => spanish ? next.TitleEs : next.TitleEn).ToList();
        _guideNext.Text = upcoming.Count == 0 ? ""
            : (spanish ? "DESPUÉS: " : "NEXT: ") + string.Join("  ›  ", upcoming);
    }

    private void Render(MissionDefinition definition, MissionDebrief debrief)
    {
        CacheRules(definition);
        bool spanish = UserInterfaceSettings.Language == InterfaceLanguage.Spanish;
        _header.Text = spanish
            ? $"OBJETIVOS · {definition.TitleEs.ToUpperInvariant()}"
            : $"OBJECTIVES · {definition.TitleEn.ToUpperInvariant()}";

        // Required rules first, then optional ones; a breached limit outranks everything so a
        // mission the player has already lost never hides that fact behind a pending objective.
        // Breached limits and required objectives are never cut; optional rows fill up to
        // MaxRows and the rest collapse into one summary line.
        var all = debrief.Objectives
            .Select(result => (result, isLimit: false))
            .Concat(debrief.Limits.Select(result => (result, isLimit: true)))
            .OrderBy(entry => entry.isLimit && !entry.result.Passed ? 0
                : entry.result.Required && !entry.isLimit ? 1 : 2)
            .ToList();
        var ordered = all
            .Where((entry, index) => entry.isLimit && !entry.result.Passed
                || entry.result.Required && !entry.isLimit
                || index < MaxRows)
            .ToList();
        int hidden = all.Count - ordered.Count;
        int hiddenPassed = all.Except(ordered).Count(entry => entry.result.Passed);
        _hiddenSummary.Visible = hidden > 0;
        _hiddenSummary.Text = spanish
            ? $"+{hidden} más ({hiddenPassed} cumplidos)"
            : $"+{hidden} more ({hiddenPassed} met)";

        while (_rows.Count < ordered.Count) _rows.Add(CreateRow());
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            if (i >= ordered.Count) { row.Root.Visible = false; continue; }

            var (result, isLimit) = ordered[i];
            row.Root.Visible = true;
            (string status, Color statusColor) = StatusOf(result, isLimit);
            row.Status.Text = status;
            row.Status.AddThemeColorOverride("font_color", statusColor);
            row.Title.Text = spanish ? result.TitleEs : result.TitleEn;
            row.Title.AddThemeColorOverride(
                "font_color",
                result.Required ? InterfaceTheme.Text : InterfaceTheme.TextMuted);
            row.Value.Text = FormatProgress(result);
        }
    }

    private Row CreateRow()
    {
        var root = new HBoxContainer();
        root.AddThemeConstantOverride("separation", 6);
        _rowBox.AddChild(root);

        var status = new Label { CustomMinimumSize = new Vector2(46, 0) };
        InterfaceTheme.ApplyMono(status, 9);
        root.AddChild(status);

        var title = new Label
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.Off,
            ClipText = true,
        };
        InterfaceTheme.ApplyBody(title, 11);
        root.AddChild(title);

        var value = new Label { HorizontalAlignment = HorizontalAlignment.Right };
        InterfaceTheme.ApplyMono(value, 9);
        value.AddThemeColorOverride("font_color", InterfaceTheme.TextMuted);
        root.AddChild(value);

        var row = new Row(root, status, title, value);
        return row;
    }

    private static (string text, Color color) StatusOf(MissionRuleResult result, bool isLimit)
    {
        if (isLimit)
            return result.Passed
                ? ("OK", InterfaceTheme.TextMuted)
                : ("FAIL", InterfaceTheme.Alert);
        return result.Passed
            ? ("PASS", InterfaceTheme.Success)
            : ("···", InterfaceTheme.TextMuted);
    }

    private void CacheRules(MissionDefinition definition)
    {
        if (_ruleCacheMissionId == definition.Id) return;
        _ruleById.Clear();
        foreach (var rule in definition.Objectives.Concat(definition.Limits))
            _ruleById[rule.Id] = rule;
        _ruleCacheMissionId = definition.Id;
    }

    /// <summary>
    /// "current vs target" in the metric's own units. <see cref="MissionRuleResult"/> carries
    /// only raw doubles, so the unit comes from the rule definition it was evaluated from.
    /// </summary>
    private string FormatProgress(MissionRuleResult result)
    {
        if (!_ruleById.TryGetValue(result.Id, out var rule))
            return result.ActualValue is { } bare ? $"{bare:G4}" : "";

        if (rule.Comparison == MissionComparison.Reached)
            return PhaseLabel(rule.ExpectedText);
        if (rule.Comparison is MissionComparison.True or MissionComparison.False)
            return result.Passed ? "YES" : "NO";
        if (result.ActualValue is not { } actual) return "";

        // Both sides of the comparison are scaled off the TARGET, so "4 m → 180 km" never
        // happens: the player reads one unit and compares two numbers in it.
        string current = FormatMetric(rule.Metric, actual, rule.Target);
        if (rule.Comparison == MissionComparison.Between && rule.UpperTarget is { } upper)
            return $"{current} → {FormatMetric(rule.Metric, rule.Target, rule.Target)}"
                + $"–{FormatMetric(rule.Metric, upper, rule.Target)}";
        string bound = rule.Comparison == MissionComparison.AtMost ? "≤" : "→";
        return $"{current} {bound} {FormatMetric(rule.Metric, rule.Target, rule.Target)}";
    }

    /// <summary>A mission phase as the player reads it, not its enum name.</summary>
    private static string PhaseLabel(string phase)
    {
        bool es = UserInterfaceSettings.Language == InterfaceLanguage.Spanish;
        return phase switch
        {
            "LIFTOFF" => es ? "DESPEGUE" : "LIFTOFF",
            "ORBIT" => es ? "ÓRBITA" : "ORBIT",
            "TLI" => "TLI",
            "LUNAR_APPROACH" => es ? "LLEGADA LUNAR" : "LUNAR ARRIVAL",
            "LOI" => "LOI",
            "LUNAR_ORBIT" => es ? "ÓRBITA LUNAR" : "LUNAR ORBIT",
            "TEI" => "TEI",
            "ENTRY" => es ? "REENTRADA" : "ENTRY",
            "LANDED" => es ? "TOQUE" : "TOUCHDOWN",
            _ => phase,
        };
    }

    private static string FormatMetric(MissionMetric metric, double value, double scaleRef)
        => metric switch
        {
            MissionMetric.PeakAltitudeM or MissionMetric.MaximumDownrangeM =>
                System.Math.Abs(scaleRef) >= 1000.0
                    ? $"{value / 1000.0:F1}km"
                    : $"{value:F0}m",
            MissionMetric.PeakSurfaceSpeedMps or MissionMetric.PeakInertialSpeedMps =>
                $"{value:N0}m/s",
            MissionMetric.MaximumDynamicPressurePa => $"{value / 1000.0:F0}kPa",
            MissionMetric.MaximumGForce => $"{value:F1}g",
            MissionMetric.MaximumAngularRateDegPerS => $"{value:F1}°/s",
            MissionMetric.CompletedOrbits or MissionMetric.CompletedLunarOrbits =>
                $"{value:F2}",
            MissionMetric.ElapsedSeconds => FormatDuration(value),
            _ => $"{value:G4}",
        };

    private static string FormatDuration(double seconds)
    {
        if (seconds < 0.0) seconds = 0.0;
        int h = (int)(seconds / 3600.0);
        int m = (int)(seconds % 3600.0 / 60.0);
        int s = (int)(seconds % 60.0);
        return h > 0 ? $"{h:00}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
    }
}
