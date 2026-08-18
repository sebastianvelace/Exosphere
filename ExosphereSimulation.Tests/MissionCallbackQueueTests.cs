namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Persistence;
using System.IO;

public sealed class MissionCallbackQueueTests
{
    [Fact]
    public void PublishDeliversSynchronouslyButRetainsStableEventRecord()
    {
        var queue = new MissionCallbackQueue();
        var delivered = new List<string>();

        MissionCallbackState callback = queue.Publish(
            "PhaseChanged", "ORBIT", 12.5,
            () => delivered.Add("ORBIT"));

        Assert.Equal(1, callback.Sequence);
        Assert.True(callback.Delivered);
        Assert.Equal(["ORBIT"], delivered);
        Assert.False(queue.HasPending);
        Assert.Equal(1, queue.Count);
        Assert.Equal(2, queue.CaptureState().NextSequence);
    }

    [Fact]
    public void PendingCallbacksDispatchInSequenceAndPersistDeliveryState()
    {
        var queue = new MissionCallbackQueue();
        queue.Enqueue("PhaseChanged", "ENTRY", 20.0);
        queue.Enqueue("LaunchCommitted", "", 20.1);
        Assert.True(queue.HasPending);

        var delivered = new List<long>();
        queue.DispatchPending(callback => delivered.Add(callback.Sequence));

        Assert.Equal([1L, 2L], delivered);
        Assert.False(queue.HasPending);

        var restored = new MissionCallbackQueue();
        restored.RestoreState(queue.CaptureState());
        Assert.False(restored.HasPending);
        Assert.Equal(3, restored.CaptureState().NextSequence);
    }

    [Fact]
    public void InvalidCallbackStateIsRejectedBeforeSaveSerialization()
    {
        var save = new SaveGameV2
        {
            SimulationTime = 30.0,
            ActiveVesselId = "vessel-a",
            Vessels = [new VesselSaveV2 { Id = "vessel-a" }],
            Mission = new MissionSaveV2
            {
                NextCallbackSequence = 2,
                CallbackEvents =
                [
                    new MissionCallbackState
                    {
                        Sequence = 2,
                        EventType = "PhaseChanged",
                        Payload = "ORBIT",
                        SimulationTime = 30.0,
                    },
                ],
            },
        };

        var error = Assert.Throws<InvalidDataException>(
            () => SaveGameV2Json.Serialize(save));
        Assert.Contains("callback", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OwnerSpecificCallbacksRoundTripAndGlobalCallbacksStayVisible()
    {
        var ownerOnly = new MissionCallbackQueue();
        ownerOnly.Enqueue(
            "VesselStaged", "vessel-b", 20.0, ownerVesselId: "vessel-b");
        Assert.False(ownerOnly.HasPendingFor("vessel-a"));
        Assert.True(ownerOnly.HasPendingFor("vessel-b"));

        var queue = new MissionCallbackQueue();
        queue.Enqueue(
            "VesselStaged", "vessel-b", 20.0, ownerVesselId: "vessel-b");
        queue.Enqueue("PhaseChanged", "ORBIT", 20.1);

        Assert.True(queue.HasPendingFor("vessel-a"));
        Assert.True(queue.HasPendingFor("vessel-b"));

        var restored = new MissionCallbackQueue();
        restored.RestoreState(queue.CaptureState());
        var callbacks = restored.CaptureState().Events;
        Assert.Equal("vessel-b", callbacks[0].OwnerVesselId);
        Assert.Null(callbacks[1].OwnerVesselId);

        var save = new SaveGameV2
        {
            SimulationTime = 20.1,
            ActiveVesselId = "vessel-a",
            Vessels =
            [
                new VesselSaveV2 { Id = "vessel-a" },
                new VesselSaveV2 { Id = "vessel-b" },
            ],
            Mission = new MissionSaveV2
            {
                NextCallbackSequence = 3,
                CallbackEvents = callbacks,
            },
        };
        var decoded = SaveGameV2Json.DeserializeOrMigrate(
            SaveGameV2Json.Serialize(save));
        Assert.Equal("vessel-b", decoded.Mission.CallbackEvents[0].OwnerVesselId);
    }

    [Fact]
    public void SaveRejectsCallbackOwnerThatIsNotInTheVesselSet()
    {
        var save = new SaveGameV2
        {
            SimulationTime = 20.0,
            ActiveVesselId = "vessel-a",
            Vessels = [new VesselSaveV2 { Id = "vessel-a" }],
            Mission = new MissionSaveV2
            {
                NextCallbackSequence = 2,
                CallbackEvents =
                [
                    new MissionCallbackState
                    {
                        Sequence = 1,
                        EventType = "VesselStaged",
                        Payload = "missing-vessel",
                        OwnerVesselId = "missing-vessel",
                        SimulationTime = 20.0,
                    },
                ],
            },
        };

        var error = Assert.Throws<InvalidDataException>(
            () => SaveGameV2Json.Serialize(save));
        Assert.Contains("owner vessel", error.Message);
    }
}
