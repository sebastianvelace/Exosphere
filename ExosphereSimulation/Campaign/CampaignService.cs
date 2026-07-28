namespace Exosphere.Simulation.Campaign;

using Exosphere.Simulation.Persistence;

public sealed record CampaignRewardResult(
    bool Applied,
    bool AlreadyCompleted,
    IReadOnlyDictionary<string, int> LedgerChanges,
    IReadOnlyCollection<string> NewlyUnlockedIds);

public sealed class CampaignService
{
    private readonly IReadOnlyDictionary<string, MissionDefinition> _missions;

    public CampaignSaveV2 State { get; }

    public CampaignService(
        CampaignSaveV2 state,
        IEnumerable<MissionDefinition> missions)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        _missions = missions.ToDictionary(
            mission => mission.Id,
            StringComparer.Ordinal);
        foreach (var mission in _missions.Values) mission.Validate();
        RefreshUnlocks();
    }

    public bool CanStart(string missionId) =>
        _missions.TryGetValue(missionId, out var definition)
        && State.UnlockedIds.Contains(missionId)
        && definition.PrerequisiteMissionIds.All(
            State.CompletedMissionIds.Contains);

    public CampaignRewardResult ApplyDebrief(MissionDebrief debrief)
    {
        ArgumentNullException.ThrowIfNull(debrief);
        if (!_missions.TryGetValue(debrief.MissionId, out var definition))
            throw new KeyNotFoundException(
                $"Unknown campaign mission '{debrief.MissionId}'.");
        if (!debrief.IsSuccess)
            return new CampaignRewardResult(
                false, false,
                new Dictionary<string, int>(),
                Array.Empty<string>());

        bool alreadyCompleted =
            !State.CompletedMissionIds.Add(definition.Id);
        var ledgerChanges = new Dictionary<string, int>(StringComparer.Ordinal);
        var unlockedBefore =
            new HashSet<string>(State.UnlockedIds, StringComparer.Ordinal);

        foreach (var reward in definition.Rewards)
        {
            string ledgerId = $"{definition.Id}:{reward.Id}";
            if (State.RewardLedger.TryAdd(ledgerId, reward.Amount))
                ledgerChanges.Add(ledgerId, reward.Amount);
            State.UnlockedIds.UnionWith(reward.UnlockIds);
        }
        RefreshUnlocks();
        var newlyUnlocked = State.UnlockedIds
            .Where(id => !unlockedBefore.Contains(id))
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new CampaignRewardResult(
            !alreadyCompleted || ledgerChanges.Count > 0
                || newlyUnlocked.Length > 0,
            alreadyCompleted,
            ledgerChanges,
            newlyUnlocked);
    }

    private void RefreshUnlocks()
    {
        foreach (var mission in _missions.Values)
            if (mission.PrerequisiteMissionIds.Count == 0
                || mission.PrerequisiteMissionIds.All(
                    State.CompletedMissionIds.Contains))
                State.UnlockedIds.Add(mission.Id);
    }
}
