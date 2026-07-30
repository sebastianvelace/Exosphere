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
    /// A flight HUD is not a debrief: past the first few rows this stops being a glanceable
    /// checklist. Required rules are kept first so the cap never hides a mission-critical one.
    private const int MaxRows = 6;
    private const double RefreshIntervalSeconds = 0.25;

    private sealed record Row(HBoxContainer Root, Label Status, Label Title, Label Value);

    private Label _header = null!;
    private VBoxContainer _rowBox = null!;
    private readonly List<Row> _rows = new();
    private readonly Dictionary<string, MissionRuleDefinition> _ruleById = new();
    private string _ruleCacheMissionId = "";
    private double _sinceRefresh = RefreshIntervalSeconds;

    /// <summary>Density gate driven by <see cref="HUDController"/>; Clean hides the checklist.</summary>
    public bool DensityAllowed { get; set; } = true;

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", InterfaceTheme.GlassPanel(0.72f, 12, 14, 11));
        SetAnchorsPreset(LayoutPreset.TopLeft);
        CustomMinimumSize = new Vector2(278, 0);
        OffsetLeft = 18;
        OffsetTop = 270;
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 5);
        AddChild(content);

        _header = new Label { Text = "MISSION OBJECTIVES" };
        InterfaceTheme.ApplyMono(_header, 10);
        _header.AddThemeColorOverride("font_color", InterfaceTheme.Orbital);
        content.AddChild(_header);

        _rowBox = new VBoxContainer();
        _rowBox.AddThemeConstantOverride("separation", 3);
        content.AddChild(_rowBox);
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
        var ordered = debrief.Objectives
            .Select(result => (result, isLimit: false))
            .Concat(debrief.Limits.Select(result => (result, isLimit: true)))
            .OrderBy(entry => entry.isLimit && !entry.result.Passed ? 0
                : entry.result.Required ? 1 : 2)
            .Take(MaxRows)
            .ToList();

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
            return rule.ExpectedText;
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
