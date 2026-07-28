namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Campaign;
using Exosphere.Simulation.Data;
using Exosphere.Simulation.Persistence;
using Xunit;

public sealed class CampaignMissionTests
{
    private static readonly DirectoryInfo Root = FindRepoRoot();

    [Fact]
    public void Freedom7DefinitionIsHistoricalBilingualAndStrictlyValidated()
    {
        var mission = LoadFreedom7();

        Assert.Equal("mission-freedom7-1961", mission.Id);
        Assert.Equal(1, mission.Sequence);
        Assert.Equal(new DateOnly(1961, 5, 5), mission.HistoricalDate);
        Assert.Equal(
            "mercury-redstone3-freedom7-1961-05-05",
            mission.VehicleVariantId);
        Assert.Equal("cape_canaveral_lc5", mission.LaunchSiteId);
        Assert.Equal(["alan-b-shepard-jr"], mission.CrewIds);
        Assert.NotEqual(mission.SummaryEn, mission.SummaryEs);
        Assert.Equal(6, mission.Objectives.Count);
        Assert.Equal(3, mission.Limits.Count);
        Assert.Equal(2, mission.Sources.Count);

        var apogee = mission.Objectives.Single(
            objective => objective.Id == "reach-suborbital-apogee");
        Assert.Equal(MissionMetric.PeakAltitudeM, apogee.Metric);
        Assert.Equal(MissionComparison.AtLeast, apogee.Comparison);
        Assert.Equal(180_000.0, apogee.Target);
    }

    [Fact]
    public void HistoricalCampaignExposesTheRequiredSixteenMissionSequence()
    {
        var campaign = CampaignDefinition.LoadFromJson(Path.Combine(
            Root.FullName,
            "data",
            "campaigns",
            "historical_nasa_spacex.json"));

        Assert.Equal("historical-nasa-spacex", campaign.Id);
        Assert.Equal(16, campaign.Missions.Count);
        Assert.Equal(
            Enumerable.Range(1, 16),
            campaign.Missions.OrderBy(item => item.Sequence)
                .Select(item => item.Sequence));
        Assert.Equal(
            "mission-freedom7-1961",
            campaign.Missions.Single(item => item.Sequence == 1).MissionId);
        Assert.Equal(
            "mission-starship-flight12-2026",
            campaign.Missions.Single(item => item.Sequence == 16).MissionId);
    }

    [Fact]
    public void PureEvaluatorProducesNumericSuccessfulFreedom7Debrief()
    {
        var director = new MissionDirector(LoadFreedom7());
        director.Observe(Snapshot(0, "PRE_LAUNCH"));
        director.Observe(Snapshot(
            180, "ASCENT_SH",
            altitudeM: 180_000,
            speedMps: 2_300,
            dynamicPressurePa: 27_770,
            gForce: 6.0,
            downrangeM: 100_000));
        director.Observe(Snapshot(
            920, "LANDED",
            altitudeM: 0,
            speedMps: 0,
            dynamicPressurePa: 0,
            gForce: 11.0,
            downrangeM: 487_260,
            splashdown: true));

        var debrief = director.FinalizeMission();

        Assert.Equal(MissionOutcome.Success, debrief.Outcome);
        Assert.All(
            debrief.Objectives.Concat(debrief.Limits)
                .Where(result => result.Required),
            result => Assert.True(result.Passed, result.Evidence));
        Assert.Equal(180_000.0, debrief.NumericEvidence["peakAltitudeM"]);
        Assert.Equal(2_300.0, debrief.NumericEvidence["peakSurfaceSpeedMps"]);
        Assert.Equal(11.0, debrief.NumericEvidence["maximumGForce"]);
        Assert.Equal(487_260.0, debrief.NumericEvidence["maximumDownrangeM"]);
    }

    [Fact]
    public void DirectorReportsIrreversibleCrewLoadFailureBeforeFinalization()
    {
        var director = new MissionDirector(LoadFreedom7());

        var debrief = director.Observe(Snapshot(
            140, "ASCENT_SH",
            altitudeM: 50_000,
            speedMps: 1_500,
            gForce: 13.0));

        Assert.Equal(MissionOutcome.Failure, debrief.Outcome);
        var limit = debrief.Limits.Single(
            result => result.Id == "crew-load-envelope");
        Assert.False(limit.Passed);
        Assert.Equal(13.0, limit.ActualValue);
        Assert.Equal(12.5, limit.TargetValue);
    }

    [Fact]
    public void DirectorStateRoundTripPreservesExtremaAndReachedPhases()
    {
        var mission = LoadFreedom7();
        var original = new MissionDirector(mission);
        original.Observe(Snapshot(
            200, "ASCENT_SH",
            altitudeM: 187_420,
            speedMps: 2_295,
            dynamicPressurePa: 27_770,
            gForce: 6.0,
            downrangeM: 120_000));
        original.Observe(Snapshot(
            500, "ENTRY",
            altitudeM: 70_000,
            speedMps: 1_800,
            gForce: 11.0,
            downrangeM: 350_000));

        MissionSaveV2 saved = original.CaptureState();
        saved.LaunchSurfaceDirection = new VectorSaveV2
        {
            X = 1.0,
        };
        string json = SaveGameV2Json.Serialize(new SaveGameV2
        {
            Mission = saved,
        });
        MissionSaveV2 decoded = SaveGameV2Json
            .DeserializeOrMigrate(json).Mission;
        var restored = new MissionDirector(mission, decoded);

        Assert.Equal(500.0, restored.Evidence.ElapsedSeconds);
        Assert.Equal(187_420.0, restored.Evidence.PeakAltitudeM);
        Assert.Equal(2_295.0, restored.Evidence.PeakSurfaceSpeedMps);
        Assert.Equal(11.0, restored.Evidence.MaximumGForce);
        Assert.Contains("ASCENT_SH", restored.Evidence.ReachedPhases);
        Assert.Contains("ENTRY", restored.Evidence.ReachedPhases);
        Assert.Equal(1.0, decoded.LaunchSurfaceDirection?.X);
    }

    [Fact]
    public void CampaignRewardsAndUnlocksAreIdempotent()
    {
        var mission = LoadFreedom7();
        var state = new CampaignSaveV2 { ProfileId = "test-pilot" };
        var campaign = new CampaignService(state, [mission]);
        Assert.True(campaign.CanStart(mission.Id));

        MissionDebrief success = SuccessfulDebrief(mission);
        CampaignRewardResult first = campaign.ApplyDebrief(success);
        CampaignRewardResult second = campaign.ApplyDebrief(success);

        Assert.True(first.Applied);
        Assert.False(first.AlreadyCompleted);
        Assert.Equal(100, first.LedgerChanges[
            "mission-freedom7-1961:historical-completion"]);
        Assert.Contains(mission.Id, state.CompletedMissionIds);
        Assert.Contains("mission-friendship7-1962", state.UnlockedIds);
        Assert.Contains("vehicle-family-mercury-atlas", state.UnlockedIds);
        Assert.Single(state.RewardLedger);

        Assert.False(second.Applied);
        Assert.True(second.AlreadyCompleted);
        Assert.Empty(second.LedgerChanges);
        Assert.Single(state.RewardLedger);
    }

    [Fact]
    public void Freedom7NumericEvidenceHasMandatoryProvenance()
    {
        var provenance = DataProvenanceRegistry.LoadFromDirectory(
            Path.Combine(Root.FullName, "data", "provenance"));
        provenance.RequireFields(
            "mission-freedom7-1961",
            "missionIdentity",
            "historicalApogeeM",
            "historicalRangeM",
            "historicalDurationS",
            "historicalSpeedMps",
            "objectiveEnvelope");

        Assert.Equal(
            ProvenanceStatus.Published,
            provenance.Require(
                "mission-freedom7-1961", "historicalApogeeM").Status);
        Assert.Equal(
            ProvenanceStatus.Derived,
            provenance.Require(
                "mission-freedom7-1961", "objectiveEnvelope").Status);
    }

    [Fact]
    public void SaveValidationRejectsNonFiniteMissionProgressAndNegativeRewards()
    {
        var invalidProgress = new SaveGameV2();
        invalidProgress.Mission.ObjectiveProgress["peakAltitudeM"] =
            double.NaN;
        Assert.Throws<InvalidDataException>(
            () => SaveGameV2Codec.Validate(invalidProgress));

        var invalidReward = new SaveGameV2();
        invalidReward.Campaign.RewardLedger["mission:reward"] = -1;
        Assert.Throws<InvalidDataException>(
            () => SaveGameV2Codec.Validate(invalidReward));
    }

    private static MissionDebrief SuccessfulDebrief(
        MissionDefinition mission)
    {
        var director = new MissionDirector(mission);
        director.Observe(Snapshot(0, "PRE_LAUNCH"));
        director.Observe(Snapshot(
            200, "ASCENT_SH",
            altitudeM: 187_420,
            speedMps: 2_295,
            dynamicPressurePa: 27_770,
            gForce: 6.0,
            downrangeM: 100_000));
        director.Observe(Snapshot(
            922, "LANDED",
            downrangeM: 487_260,
            gForce: 11.0,
            splashdown: true));
        return director.FinalizeMission();
    }

    private static MissionTelemetrySnapshot Snapshot(
        double time,
        string phase,
        double altitudeM = 0.0,
        double speedMps = 0.0,
        double dynamicPressurePa = 0.0,
        double gForce = 1.0,
        double downrangeM = 0.0,
        bool splashdown = false) => new()
    {
        MissionTimeSeconds = time,
        Phase = phase,
        AltitudeM = altitudeM,
        SurfaceSpeedMps = speedMps,
        DynamicPressurePa = dynamicPressurePa,
        GForce = gForce,
        DownrangeM = downrangeM,
        CrewAlive = true,
        VesselDestroyed = false,
        Splashdown = splashdown,
    };

    private static MissionDefinition LoadFreedom7() =>
        MissionDefinition.LoadFromJson(Path.Combine(
            Root.FullName, "data", "missions", "freedom7_1961.json"));

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName, "ExosphereSimulation.sln")))
                return directory;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }
}
