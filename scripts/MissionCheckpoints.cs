namespace Exosphere.Game;

using Godot;

/// <summary>
/// Automatic checkpoints for campaign missions (KSP's revert, by milestone): the flight is
/// saved on entering each milestone phase, and [F6] or the debrief's button reloads the
/// last one as a fresh flight scene, which also restores the mission evidence as it was
/// then. A new mission clears the previous mission's checkpoint.
/// </summary>
public static class MissionCheckpoints
{
    public const string Slot = "checkpoint";

    public static MissionPhase? LastPhase { get; private set; }

    public static bool IsMilestone(MissionPhase phase) => phase is MissionPhase.ORBIT or MissionPhase.TLI
        or MissionPhase.LUNAR_APPROACH or MissionPhase.LOI or MissionPhase.LUNAR_ORBIT
        or MissionPhase.TEI or MissionPhase.ENTRY;

    public static bool Exists => SaveSystem.HasSlot(Slot);

    public static void Save(MissionPhase phase)
    {
        if (CampaignRuntime.Instance?.HasActiveMission != true) return;
        SaveSystem.SaveGame(Slot);
        LastPhase = phase;
        GD.Print($"[Checkpoint] Saved at {phase}.");
    }

    public static void Clear()
    {
        SaveSystem.DeleteSlot(Slot);
        LastPhase = null;
    }

    /// <summary>Reloads the last checkpoint; false when there is none.</summary>
    public static bool Revert(SceneTree tree)
    {
        if (!Exists) return false;
        CraftLaunchRequest.Set(new LaunchIntent { Mode = "continue", SaveSlot = Slot });
        tree.ChangeSceneToFile("res://scenes/flight/Flight.tscn");
        return true;
    }
}
