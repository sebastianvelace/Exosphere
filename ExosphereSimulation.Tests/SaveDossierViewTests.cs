namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Persistence;

public sealed class SaveDossierViewTests
{
    [Fact]
    public void FromSave_UsesActiveVesselAndMissionProgress()
    {
        var save = new SaveGameV2
        {
            SavedAtUtc = new DateTimeOffset(2026, 8, 9, 14, 30, 0, TimeSpan.Zero),
            SimulationTime = 1234.5,
            TimeScale = 3.0,
            ActiveVesselId = "ship-7",
            Vessels =
            [
                new VesselSaveV2 { Id = "ship-7", Name = "Starship 33" },
            ],
            Mission = new MissionSaveV2
            {
                MissionId = "starship-flight7-ascent",
                Phase = "ORBIT",
                CompletedObjectives = ["launch", "insert"],
                ReachedPhases = ["LIFTOFF", "ORBIT"],
            },
            Campaign = new CampaignSaveV2
            {
                CompletedMissionIds = ["mission-01"],
            },
        };

        SaveDossierView dossier = SaveDossierView.FromSave("flight7", save);

        Assert.Equal("FLIGHT7", dossier.SlotName.ToUpperInvariant());
        Assert.Equal("starship-flight7-ascent", dossier.MissionId);
        Assert.Equal("ORBIT", dossier.MissionPhase);
        Assert.Equal("Starship 33", dossier.ActiveVesselName);
        Assert.Equal("ship-7", dossier.ActiveVesselId);
        Assert.Equal(1234.5, dossier.SimulationTime);
        Assert.Equal(3.0, dossier.TimeScale);
        Assert.Equal(1, dossier.VesselCount);
        Assert.Equal(2, dossier.CompletedObjectiveCount);
        Assert.Equal(1, dossier.CompletedCampaignMissionCount);
        Assert.Equal(2, dossier.ReachedPhaseCount);
    }

    [Fact]
    public void FromSave_ProvidesBoundedSingleLineFallbacks()
    {
        var save = new SaveGameV2
        {
            ActiveVesselId = "missing-vessel",
            Mission = new MissionSaveV2
            {
                MissionId = "  line one\nline two " + new string('x', 100),
                Phase = "  ",
            },
        };

        SaveDossierView dossier = SaveDossierView.FromSave("  slot\nname  ", save);

        Assert.Equal("slot name", dossier.SlotName);
        Assert.DoesNotContain('\n', dossier.MissionId);
        Assert.DoesNotContain('\r', dossier.MissionId);
        Assert.Equal(64, dossier.MissionId.Length);
        Assert.Equal("SAVED", dossier.MissionPhase);
        Assert.Equal("NO ACTIVE VESSEL", dossier.ActiveVesselName);
        Assert.Equal("missing-vessel", dossier.ActiveVesselId);
    }

    [Fact]
    public void FromSave_UsesStableDefaultsForFreshSave()
    {
        SaveDossierView dossier = SaveDossierView.FromSave(
            "new-flight",
            new SaveGameV2());

        Assert.Equal("SANDBOX FLIGHT", dossier.MissionId);
        Assert.Equal("SAVED", dossier.MissionPhase);
        Assert.Equal("NO ACTIVE VESSEL", dossier.ActiveVesselName);
        Assert.Equal("NONE", dossier.ActiveVesselId);
        Assert.Equal(0, dossier.VesselCount);
        Assert.Equal(0, dossier.CompletedObjectiveCount);
        Assert.Equal(0, dossier.CompletedCampaignMissionCount);
        Assert.Equal(0, dossier.ReachedPhaseCount);
    }
}
