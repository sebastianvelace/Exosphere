namespace Exosphere.Game;

using Exosphere.Simulation.Campaign;
using Godot;

public static class CampaignDebriefPanel
{
    public static void Show(
        MissionDebrief debrief,
        CampaignRewardResult rewards)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        var ui = tree?.Root.FindChild("UI", true, false) as CanvasLayer;
        if (ui == null) return;
        ui.GetNodeOrNull<Control>("CampaignDebrief")?.QueueFree();

        bool spanish =
            UserInterfaceSettings.Language == InterfaceLanguage.Spanish;
        var shade = new ColorRect
        {
            Name = "CampaignDebrief",
            Color = new Color(0.003f, 0.005f, 0.008f, 0.90f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ui.AddChild(shade);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        shade.AddChild(center);
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(720, 0),
        };
        panel.AddThemeStyleboxOverride(
            "panel", InterfaceTheme.GlassPanel(0.97f, 0, 32, 28));
        center.AddChild(panel);
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 10);
        panel.AddChild(content);

        var classification = new Label
        {
            Text = spanish ? "INFORME DE MISIÓN" : "MISSION DEBRIEF",
        };
        InterfaceTheme.ApplyMono(classification, 11);
        classification.AddThemeColorOverride(
            "font_color", InterfaceTheme.Orbital);
        content.AddChild(classification);

        var title = new Label
        {
            Text = debrief.Outcome == MissionOutcome.Success
                ? (spanish ? "MISIÓN COMPLETADA" : "MISSION COMPLETE")
                : (spanish ? "MISIÓN NO COMPLETADA" : "MISSION NOT COMPLETE"),
        };
        InterfaceTheme.ApplyDisplay(title, 34);
        title.AddThemeColorOverride(
            "font_color",
            debrief.IsSuccess ? new Color(0.55f, 0.95f, 0.65f)
                : InterfaceTheme.Warning);
        content.AddChild(title);

        foreach (var result in debrief.Objectives.Concat(debrief.Limits))
        {
            var row = new HBoxContainer();
            var status = new Label
            {
                Text = result.Passed ? "PASS" : "FAIL",
                CustomMinimumSize = new Vector2(52, 0),
            };
            InterfaceTheme.ApplyMono(status, 10);
            status.AddThemeColorOverride(
                "font_color",
                result.Passed ? new Color(0.55f, 0.95f, 0.65f)
                    : InterfaceTheme.Alert);
            row.AddChild(status);
            var objective = new Label
            {
                Text = spanish ? result.TitleEs : result.TitleEn,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            InterfaceTheme.ApplyBody(objective, 13);
            row.AddChild(objective);
            var evidence = new Label { Text = result.Evidence };
            InterfaceTheme.ApplyMono(evidence, 10);
            evidence.AddThemeColorOverride(
                "font_color", InterfaceTheme.TextMuted);
            row.AddChild(evidence);
            content.AddChild(row);
        }

        if (rewards.LedgerChanges.Count > 0
            || rewards.NewlyUnlockedIds.Count > 0)
        {
            var reward = new Label
            {
                Text = $"{(spanish ? "RECOMPENSAS" : "REWARDS")}  " +
                    $"{rewards.LedgerChanges.Values.Sum()}  •  " +
                    $"{rewards.NewlyUnlockedIds.Count} " +
                    $"{(spanish ? "DESBLOQUEOS" : "UNLOCKS")}",
            };
            InterfaceTheme.ApplyMono(reward, 11);
            reward.AddThemeColorOverride(
                "font_color", InterfaceTheme.Orbital);
            content.AddChild(reward);
        }

        var close = new Button
        {
            Text = spanish ? "CONTINUAR" : "CONTINUE",
            CustomMinimumSize = new Vector2(280, 44),
        };
        InterfaceTheme.StyleDossierButton(close, primary: true);
        close.Pressed += shade.QueueFree;
        content.AddChild(close);
        close.CallDeferred(Control.MethodName.GrabFocus);
    }
}
