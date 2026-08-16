namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Flight;

public sealed class SimulationInterestPolicyTests
{
    [Fact]
    public void DefaultPolicyIsOffAndNoRuntimeStateIsRequired()
    {
        Assert.False(SimulationInterestPolicy.EnabledByDefault);

        var decision = SimulationInterestPolicy.Classify(BaseInputs());

        Assert.Equal(SimulationInterestTier.Dormant, decision.Tier);
        Assert.True(decision.AllowsDeferredWork);
    }

    [Fact]
    public void ActivePrecedenceCoversControlSelectionAndMissionCriticalState()
    {
        var cases = new[]
        {
            BaseInputs() with { IsActiveVessel = true },
            BaseInputs() with { IsPilotControlled = true },
            BaseInputs() with { IsMissionControlled = true },
            BaseInputs() with { IsSelected = true },
            BaseInputs() with { IsMissionCriticalState = true },
        };

        foreach (var inputs in cases)
        {
            var decision = SimulationInterestPolicy.Classify(inputs);

            Assert.Equal(SimulationInterestTier.Active, decision.Tier);
        }
    }

    [Fact]
    public void SelectionAndMissionReasonsAreRetainedOnActiveDecision()
    {
        var inputs = BaseInputs() with
        {
            IsSelected = true,
            IsMissionCriticalState = true,
        };

        var reasons = SimulationInterestPolicy.GetWakeUpReasons(inputs);

        Assert.Equal(
            SimulationWakeReason.Selection | SimulationWakeReason.MissionCriticalState,
            reasons);
    }

    [Fact]
    public void EachSafetyEventFailsClosedToProximity()
    {
        var cases = new[]
        {
            (BaseInputs() with { HasThrust = true }, SimulationWakeReason.Thrust),
            (BaseInputs() with { HasPendingCommand = true }, SimulationWakeReason.Command),
            (BaseInputs() with { HasDockingOrContact = true }, SimulationWakeReason.DockingContact),
            (BaseInputs() with { IsAtmosphereOrReentry = true }, SimulationWakeReason.AtmosphereReentry),
            (BaseInputs() with { HasPendingSoiTransition = true }, SimulationWakeReason.SoiDeadline),
        };

        foreach (var (inputs, expectedReason) in cases)
        {
            var decision = SimulationInterestPolicy.Classify(inputs);

            Assert.Equal(SimulationInterestTier.Proximity, decision.Tier);
            Assert.Equal(expectedReason, decision.WakeReasons);
            Assert.False(decision.AllowsDeferredWork);
        }
    }

    [Fact]
    public void ExternalSystemsAlertWakesADeferredVesselWithoutPromotingItToActive()
    {
        var external = SimulationExternalInterestInputs.None with
        {
            HasSystemsAlert = true,
        };

        var decision = SimulationInterestPolicy.Classify(BaseInputs(), external);

        Assert.Equal(SimulationInterestTier.Proximity, decision.Tier);
        Assert.Equal(SimulationWakeReason.SystemsAlert, decision.WakeReasons);
        Assert.False(decision.AllowsDeferredWork);
    }

    [Fact]
    public void ExternalMissionCriticalStateHasActivePrecedenceAndRetainsReasons()
    {
        var external = SimulationExternalInterestInputs.None with
        {
            IsMissionControlled = true,
            IsMissionCriticalState = true,
            HasPendingMissionCallback = true,
        };

        var decision = SimulationInterestPolicy.Classify(BaseInputs(), external);

        Assert.Equal(SimulationInterestTier.Active, decision.Tier);
        Assert.Equal(
            SimulationWakeReason.MissionCriticalState | SimulationWakeReason.MissionCallback,
            decision.WakeReasons);
        Assert.False(decision.AllowsDeferredWork);
    }

    [Fact]
    public void ExternalEntryStateComposesWithAUserCommand()
    {
        var external = SimulationExternalInterestInputs.None with
        {
            IsAtmosphereOrReentry = true,
        };
        var inputs = BaseInputs() with { HasPendingCommand = true };

        var decision = SimulationInterestPolicy.Classify(inputs, external);

        Assert.Equal(SimulationInterestTier.Proximity, decision.Tier);
        Assert.Equal(
            SimulationWakeReason.Command | SimulationWakeReason.AtmosphereReentry,
            decision.WakeReasons);
    }

    [Fact]
    public void ExternalSystemsDeadlineUsesEventDrivenUntilTheWakeWindow()
    {
        var farDeadline = SimulationExternalInterestInputs.None with
        {
            SecondsUntilNextSystemsDeadline = 600.0,
        };
        var nearDeadline = farDeadline with
        {
            SecondsUntilNextSystemsDeadline =
                SimulationInterestPolicyOptions.Default.DeadlineWakeWindowSeconds,
        };

        var deferred = SimulationInterestPolicy.Classify(BaseInputs(), farDeadline);
        var wake = SimulationInterestPolicy.Classify(BaseInputs(), nearDeadline);

        Assert.Equal(SimulationInterestTier.EventDriven, deferred.Tier);
        Assert.Equal(SimulationWakeReason.None, deferred.WakeReasons);
        Assert.True(deferred.AllowsDeferredWork);
        Assert.Equal(SimulationInterestTier.Proximity, wake.Tier);
        Assert.Equal(SimulationWakeReason.SystemsDeadline, wake.WakeReasons);
        Assert.False(wake.AllowsDeferredWork);
    }

    [Fact]
    public void InvalidExternalSystemsDeadlineFailsClosed()
    {
        var invalid = SimulationExternalInterestInputs.None with
        {
            SecondsUntilNextSystemsDeadline = -0.001,
        };

        var decision = SimulationInterestPolicy.Classify(BaseInputs(), invalid);

        Assert.Equal(SimulationInterestTier.Active, decision.Tier);
        Assert.Equal(SimulationWakeReason.InvalidInput, decision.WakeReasons);
        Assert.True(decision.IsFailClosed);
        Assert.Equal(
            SimulationWakeReason.InvalidInput,
            SimulationInterestPolicy.GetWakeUpReasons(BaseInputs(), invalid));
        Assert.Throws<ArgumentOutOfRangeException>(invalid.Validate);
    }

    [Fact]
    public void WakeReasonsComposeDeterministically()
    {
        var inputs = BaseInputs() with
        {
            HasThrust = true,
            HasPendingCommand = true,
            HasDockingOrContact = true,
            IsAtmosphereOrReentry = true,
            HasPendingSoiTransition = true,
        };

        var expected = SimulationWakeReason.Thrust
            | SimulationWakeReason.Command
            | SimulationWakeReason.DockingContact
            | SimulationWakeReason.AtmosphereReentry
            | SimulationWakeReason.SoiDeadline;

        Assert.Equal(expected, SimulationInterestPolicy.GetWakeUpReasons(inputs));
        Assert.Equal(expected, SimulationInterestPolicy.Classify(inputs).WakeReasons);
    }

    [Fact]
    public void ProximityRadiusIsInclusiveAndZeroDistanceIsValid()
    {
        var options = SimulationInterestPolicyOptions.Default;
        var atBoundary = BaseInputs() with
        {
            DistanceToActiveVesselM = options.ProximityRadiusM,
        };
        var atZero = BaseInputs() with { DistanceToInteractionM = 0.0 };

        Assert.Equal(
            SimulationInterestTier.Proximity,
            SimulationInterestPolicy.Classify(atBoundary, options).Tier);
        Assert.Equal(
            SimulationInterestTier.Proximity,
            SimulationInterestPolicy.Classify(atZero, options).Tier);
    }

    [Fact]
    public void JustOutsideProximityRadiusCanUseEventDrivenOrDormantWork()
    {
        var options = SimulationInterestPolicyOptions.Default;
        var justOutside = BaseInputs() with
        {
            DistanceToActiveVesselM = options.ProximityRadiusM + 1.0,
            SecondsUntilNextDeadline = 600.0,
        };
        var noDeadline = justOutside with { SecondsUntilNextDeadline = null };

        Assert.Equal(
            SimulationInterestTier.EventDriven,
            SimulationInterestPolicy.Classify(justOutside, options).Tier);
        Assert.Equal(
            SimulationInterestTier.Dormant,
            SimulationInterestPolicy.Classify(noDeadline, options).Tier);
    }

    [Fact]
    public void DeadlineWakeWindowIsInclusive()
    {
        var options = SimulationInterestPolicyOptions.Default;
        var atBoundary = BaseInputs() with
        {
            SecondsUntilNextDeadline = options.DeadlineWakeWindowSeconds,
        };
        var justOutside = atBoundary with
        {
            SecondsUntilNextDeadline = options.DeadlineWakeWindowSeconds + 1.0,
        };

        Assert.Equal(
            SimulationInterestTier.Proximity,
            SimulationInterestPolicy.Classify(atBoundary, options).Tier);
        Assert.Equal(SimulationWakeReason.SoiDeadline,
            SimulationInterestPolicy.Classify(atBoundary, options).WakeReasons);
        Assert.Equal(
            SimulationInterestTier.EventDriven,
            SimulationInterestPolicy.Classify(justOutside, options).Tier);
    }

    [Fact]
    public void InvalidNumericInputFailsClosedInsteadOfBecomingDeferred()
    {
        var invalidInputs = new[]
        {
            BaseInputs() with { DistanceToActiveVesselM = -1.0 },
            BaseInputs() with { DistanceToInteractionM = double.NaN },
            BaseInputs() with { SecondsUntilNextDeadline = double.PositiveInfinity },
            BaseInputs() with { SecondsUntilNextDeadline = double.NegativeInfinity },
        };

        foreach (var inputs in invalidInputs)
        {
            var decision = SimulationInterestPolicy.Classify(inputs);

            Assert.Equal(SimulationInterestTier.Active, decision.Tier);
            Assert.Equal(SimulationWakeReason.InvalidInput, decision.WakeReasons);
            Assert.True(decision.IsFailClosed);
            Assert.False(decision.AllowsDeferredWork);
            Assert.Equal(SimulationWakeReason.InvalidInput,
                SimulationInterestPolicy.GetWakeUpReasons(inputs));
        }
    }

    [Fact]
    public void ValidateRejectsInvalidInputAndOptions()
    {
        var invalidInputs = BaseInputs() with { DistanceToInteractionM = -0.001 };
        var invalidOptions = new SimulationInterestPolicyOptions(
            ProximityRadiusM: double.NaN,
            DeadlineWakeWindowSeconds: 0.0);

        Assert.Throws<ArgumentOutOfRangeException>(invalidInputs.Validate);
        Assert.Throws<ArgumentOutOfRangeException>(invalidOptions.Validate);
        Assert.False(SimulationInterestPolicy.Classify(invalidInputs).AllowsDeferredWork);
        Assert.Equal(SimulationWakeReason.InvalidInput,
            SimulationInterestPolicy.Classify(BaseInputs(), invalidOptions).WakeReasons);
    }

    private static SimulationInterestInputs BaseInputs() => new(
        IsActiveVessel: false,
        IsPilotControlled: false,
        IsMissionControlled: false,
        IsSelected: false,
        HasThrust: false,
        HasPendingCommand: false,
        HasDockingOrContact: false,
        IsAtmosphereOrReentry: false,
        HasPendingSoiTransition: false,
        SecondsUntilNextDeadline: null,
        IsMissionCriticalState: false,
        DistanceToActiveVesselM: null,
        DistanceToInteractionM: null);
}
