namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Systems;
using Xunit;

/// <summary>
/// Regression for the 2026-11-09 Apollo 11 ORBIT milestone checkpoint: a mission-callback
/// owner (the staged S-IC) was materialized at T+179 s, only the active runtime was ticked,
/// and the T+712 s save threw on a mixed-epoch registry. The game layer now advances every
/// non-active runtime each committed tick through <see cref="VesselSystemsRuntimeRegistry.AdvanceInactive"/>.
/// </summary>
public sealed class InactiveSystemsRuntimeAdvanceTests
{
    [Fact]
    public void MilestoneSaveAfterACallbackOwnerWasMaterializedSucceeds()
    {
        var (universe, active, staged) = TwoVesselUniverse();
        var registry = new VesselSystemsRuntimeRegistry();
        var activeRuntime = registry.Materialize(active.Id, universe.CurrentTime);

        // Several committed ticks before staging; then the callback owner is materialized
        // without becoming active (SystemsController.EnsureVesselRuntimeMaterialized).
        TickFrames(universe, registry, activeRuntime, frames: 5, advanceInactive: true);
        var stagedRuntime = registry.Materialize(staged.Id, universe.CurrentTime);
        double materializedAt = stagedRuntime.SimulationTime;

        TickFrames(universe, registry, activeRuntime, frames: 20, advanceInactive: true);

        var states = registry.CaptureStates(universe.CurrentTime);
        Assert.Equal(2, states.Count);
        Assert.Equal(universe.CurrentTime, states[staged.Id].SimulationTime, 9);
        Assert.True(states[staged.Id].SimulationTime > materializedAt);

        // The owner's runtime was fed its own vessel's environment, not the active one's.
        Assert.Equal(universe.CurrentTime, stagedRuntime.SimulationTime, 9);
    }

    [Fact]
    public void WithoutInactiveAdvanceTheSaveIsRefusedAsMixedEpoch()
    {
        var (universe, active, staged) = TwoVesselUniverse();
        var registry = new VesselSystemsRuntimeRegistry();
        var activeRuntime = registry.Materialize(active.Id, universe.CurrentTime);
        registry.Materialize(staged.Id, universe.CurrentTime);

        TickFrames(universe, registry, activeRuntime, frames: 3, advanceInactive: false);

        Assert.Throws<InvalidOperationException>(
            () => registry.CaptureStates(universe.CurrentTime));
    }

    [Fact]
    public void AdvanceInactiveSkipsTheActiveRuntimeAndUsesEachRuntimesOwnInterval()
    {
        var registry = new VesselSystemsRuntimeRegistry();
        var active = registry.Materialize("active", simulationTime: 10.0);
        var early = registry.Materialize("early", simulationTime: 10.0);
        var intervals = new Dictionary<string, double>();

        registry.AdvanceInactive(12.5, "active", (runtime, delta) =>
        {
            intervals[runtime.VesselId] = delta;
            return Input(delta, 12.5);
        });

        Assert.Equal(10.0, active.SimulationTime, 12);
        Assert.Equal(12.5, early.SimulationTime, 12);
        Assert.Equal(2.5, intervals["early"], 12);
        Assert.False(intervals.ContainsKey("active"));

        // Already current: no second consumption of the same interval.
        registry.AdvanceInactive(12.5, "active", (_, _) =>
            throw new InvalidOperationException("must not resample a current runtime"));
    }

    [Fact]
    public void RuntimeOfAVesselThatLeftTheUniverseIsReleased()
    {
        var registry = new VesselSystemsRuntimeRegistry();
        registry.Materialize("active", simulationTime: 1.0);
        registry.Materialize("gone", simulationTime: 1.0);

        var removed = registry.AdvanceInactive(2.0, "active", (_, _) => null);

        Assert.Equal(["gone"], removed);
        Assert.False(registry.Contains("gone"));
        Assert.True(registry.Contains("active"));
    }

    [Fact]
    public void RuntimeAheadOfTheCommittedEpochFailsClosed()
    {
        var registry = new VesselSystemsRuntimeRegistry();
        registry.Materialize("ahead", simulationTime: 5.0);

        Assert.Throws<InvalidOperationException>(() =>
            registry.AdvanceInactive(4.0, activeVesselId: null, (_, delta) => Input(delta, 4.0)));
        Assert.True(registry.Contains("ahead"));
    }

    [Fact]
    public void SamplerReadsTheGivenVesselsOwnPosition()
    {
        var (universe, active, staged) = TwoVesselUniverse();

        var activeSample = VesselSystemsEnvironmentSampler.Sample(
            universe, active, 1.0, 1.0, 3, SystemsMissionPhase.Active, solarVisibility: 0.25);
        var stagedSample = VesselSystemsEnvironmentSampler.Sample(
            universe, staged, 1.0, 1.0, 0, SystemsMissionPhase.Idle);

        Assert.Equal(active.Position, activeSample.VesselPosition);
        Assert.Equal(staged.Position, stagedSample.VesselPosition);
        Assert.Equal(0.25, activeSample.SolarVisibility, 12);
        Assert.InRange(stagedSample.SolarVisibility, 0.0, 1.0);
        Assert.True(activeSample.IsValid);
        Assert.True(stagedSample.IsValid);
    }

    private static void TickFrames(
        Universe universe,
        VesselSystemsRuntimeRegistry registry,
        VesselSystemsRuntime activeRuntime,
        int frames,
        bool advanceInactive)
    {
        var vessels = universe.Vessels.ToDictionary(vessel => vessel.Id);
        for (int i = 0; i < frames; i++)
        {
            double before = universe.CurrentTime;
            universe.Tick(0.1);
            double delta = universe.CurrentTime - before;
            Assert.True(delta > 0.0);

            activeRuntime.Tick(VesselSystemsEnvironmentSampler.Sample(
                universe, vessels[activeRuntime.VesselId], delta, universe.CurrentTime,
                crewCount: 3, SystemsMissionPhase.Active, solarVisibility: 1.0));
            if (advanceInactive)
            {
                registry.AdvanceInactive(universe.CurrentTime, activeRuntime.VesselId,
                    (runtime, interval) => vessels.TryGetValue(runtime.VesselId, out var owner)
                        ? VesselSystemsEnvironmentSampler.Sample(
                            universe, owner, interval, universe.CurrentTime,
                            owner.Crew.Count, runtime.CurrentPhase)
                        : null);
            }
        }
    }

    private static VesselSystemsTickInput Input(double delta, double epoch) => new(
        DeltaSeconds: delta,
        SimulationTime: epoch,
        CrewCount: 0,
        Phase: SystemsMissionPhase.Idle,
        VesselPosition: new Vector3d(7_000_000.0, 0.0, 0.0),
        EarthPosition: Vector3d.Zero,
        SunPosition: new Vector3d(1.5e11, 0.0, 0.0),
        Bodies: Array.Empty<CelestialBody>(),
        SolarVisibility: 1.0,
        InAtmosphere: false,
        AtmosphericTemperatureK: 3.0,
        AeroHeatFluxWm2: 0.0,
        EntryCondition: default);

    private static (Universe Universe, Vessel Active, Vessel Staged) TwoVesselUniverse()
    {
        var body = new CelestialBody
        {
            Id = "earth",
            Name = "Earth",
            Mass = 5.972e24,
            GM = 3.986004418e14,
            Radius = 6_371_000.0,
            SphereOfInfluence = 1.0e12,
        };
        var active = RailVessel(body, "active", altitude: 300_000.0);
        var staged = RailVessel(body, "staged-booster", altitude: 1_500_000.0);
        var universe = new Universe { ActiveVessel = active };
        universe.AddBody(body);
        universe.AddVessel(active);
        universe.AddVessel(staged);
        return (universe, active, staged);
    }

    private static Vessel RailVessel(CelestialBody body, string id, double altitude)
    {
        double radius = body.Radius + altitude;
        Vector3d position = body.Position + Vector3d.Right * radius;
        Vector3d velocity = body.Velocity + Vector3d.Up * System.Math.Sqrt(body.GM / radius);
        var vessel = new Vessel(id)
        {
            Position = position,
            Velocity = velocity,
            ReferenceBodyId = body.Id,
            IsOnRails = true,
            OrbitalState = OrbitalElements.FromStateVector(
                position - body.Position, velocity - body.Velocity, body.GM, body.Id, 0.0),
        };
        vessel.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = $"{id}-command",
            CategoryStr = "command",
            MassDry = 1_000.0,
            LengthM = 4.0,
            DiameterM = 2.0,
        }));
        return vessel;
    }
}
