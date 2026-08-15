namespace ExosphereSimulation.Tests;

using System.Diagnostics;
using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

/// <summary>
/// QA/performance gates for the sandbox flight path.
///
/// These tests deliberately exercise the pure simulation boundary.  Godot frame and
/// startup timings are checked by performance_acceptance_contract_test.sh because the
/// test project must not depend on a renderer, a display, or a running scene.
/// </summary>
public sealed class PerformanceAcceptanceTests
{
    // These are watchdog limits, not a claim that every machine should target the limit.
    // The scene-level target is stricter and is documented in PERF_QA_AGENT_REPORT.md.
    private const double SimulationStartupWatchdogMs = 10_000.0;
    private const double SimulationFrameWatchdogMs = 1_000.0;
    private const int ProgressTicks = 120;

    [Fact]
    public void SimulationStartupLoadsFiniteWorldWithinWatchdog()
    {
        var stopwatch = Stopwatch.StartNew();
        var universe = Universe.LoadFromDataDirectory(DataRoot());
        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed.TotalMilliseconds <= SimulationStartupWatchdogMs,
            $"simulation startup watchdog exceeded: {stopwatch.Elapsed.TotalMilliseconds:F1} ms");
        Assert.NotEmpty(universe.Bodies);
        AssertFinite(universe);
    }

    [Fact]
    public void UniverseCollectionViewsAreStableAndAllocationFreeAfterConstruction()
    {
        var universe = new Universe();
        var bodies = universe.Bodies;
        var vessels = universe.Vessels;
        var dockingConnections = universe.DockingConnections;

        Assert.Same(bodies, universe.Bodies);
        Assert.Same(vessels, universe.Vessels);
        Assert.Same(dockingConnections, universe.DockingConnections);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            _ = universe.Bodies.Count;
            _ = universe.Vessels.Count;
            _ = universe.DockingConnections.Count;
        }
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(0, allocatedBytes);
    }

    [Fact]
    public void ActiveNearbyAndOnRailsTiersAreMutuallyExclusive()
    {
        var earth = LoadBody("earth");
        var active = CreateProbe(earth, 1_200_000.0, 0.0, 7_100.0, "perf-active");
        var nearby = CreateProbe(earth, 100_000.0, 0.0, 3_000.0, "perf-nearby");
        var onRails = CreateProbe(earth, 1_200_000.0, 0.0, 7_100.0, "perf-rails");
        onRails.IsOnRails = true;

        var universe = new Universe { ActiveVessel = active, TimeScale = 1.0 };
        universe.AddBody(earth);
        universe.AddVessel(active);
        universe.AddVessel(nearby);
        universe.AddVessel(onRails);

        Assert.Equal(PhysicsTier.Active, Classify(universe, active));
        Assert.Equal(PhysicsTier.Nearby, Classify(universe, nearby));
        Assert.Equal(PhysicsTier.OnRails, Classify(universe, onRails));
        Assert.True(universe.RequiresOffRailsPhysics(nearby));
        Assert.False(universe.RequiresOffRailsPhysics(onRails));

        var tiers = new[] { active, nearby, onRails }
            .Select(v => Classify(universe, v))
            .ToArray();
        Assert.Equal(3, tiers.Distinct().Count());
    }

    [Fact]
    public void ProgressWatchdogDetectsNoStallAndStateRemainsFinite()
    {
        var earth = LoadBody("earth");
        var vessel = CreateProbe(earth, 1_200_000.0, 0.0, 7_100.0);
        var universe = new Universe
        {
            ActiveVessel = vessel,
            TimeScale = 50.0,
        };
        universe.AddBody(earth);
        universe.AddVessel(vessel);

        double initialTime = universe.CurrentTime;
        Vector3d previousPosition = vessel.Position;
        int unchangedPositionStreak = 0;
        bool positionChanged = false;

        for (int i = 0; i < ProgressTicks; i++)
        {
            universe.Tick(0.02);

            Assert.True(
                universe.CurrentTime > initialTime + i * 0.02 * universe.TimeScale,
                $"simulation time stopped at tick {i}: t={universe.CurrentTime:R}");
            AssertFinite(universe);

            bool moved = (vessel.Position - previousPosition).Magnitude > 1e-9;
            if (moved)
            {
                positionChanged = true;
                unchangedPositionStreak = 0;
            }
            else
            {
                unchangedPositionStreak++;
            }

            Assert.True(
                unchangedPositionStreak <= 2,
                $"position progress stalled for {unchangedPositionStreak} ticks at {i}");
            previousPosition = vessel.Position;
        }

        Assert.True(positionChanged, "the orbital probe never changed position");
        Assert.Equal(
            ProgressTicks * 0.02 * universe.TimeScale,
            universe.CurrentTime,
            10);
    }

    [Fact]
    public void StarshipSizedPhysicsBurstStaysFiniteAndWithinFrameWatchdog()
    {
        var earth = LoadBody("earth");
        var vessel = CreateStarshipSizedStack();
        vessel.Position = earth.Position + Vector3d.Right * (earth.Radius + 120_000.0);
        vessel.Velocity = earth.Velocity + Vector3d.Up * 7_600.0;
        vessel.SASEnabled = false;

        var universe = new Universe { ActiveVessel = vessel, TimeScale = 1.0 };
        universe.AddBody(earth);
        universe.AddVessel(vessel);

        // Warm up JIT and catalog-backed engine state before the measured sample.
        universe.Tick(0.02);
        AssertFinite(universe);

        var frameTimes = new List<double>();
        for (int i = 0; i < 20; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            universe.Tick(0.02);
            stopwatch.Stop();
            double elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
            frameTimes.Add(elapsedMs);

            Assert.True(
                elapsedMs <= SimulationFrameWatchdogMs,
                $"Starship-sized simulation tick watchdog exceeded at {i}: {elapsedMs:F1} ms");
            AssertFinite(universe);
        }

        double p95 = Percentile(frameTimes, 0.95);
        Assert.True(
            p95 <= SimulationFrameWatchdogMs,
            $"Starship-sized p95 simulation tick exceeded watchdog: {p95:F1} ms");
    }

    [Fact]
    public void ShortOffRailsFlightPreservesPhysicalDirectionAndFiniteThermalState()
    {
        var earth = LoadBody("earth");
        var vessel = CreateProbe(earth, 100_000.0, 0.0, 3_000.0);
        vessel.Parts.Parts[0].Definition.HeatTolerance = 10_000.0;
        var initialAltitude = vessel.GetAltitude(earth);
        var initialVelocity = vessel.Velocity;
        var universe = new Universe { ActiveVessel = vessel, TimeScale = 1.0 };
        universe.AddBody(earth);
        universe.AddVessel(vessel);

        Assert.True(universe.RequiresOffRailsPhysics(vessel));
        for (int i = 0; i < 25; i++)
        {
            universe.Tick(0.02);
            AssertFinite(universe);
        }

        Assert.True(universe.CurrentTime > 0.0);
        Assert.True(vessel.GetAltitude(earth) < initialAltitude,
            "the entry probe did not descend under gravity");
        Assert.True(vessel.Velocity.X < initialVelocity.X,
            "the entry probe did not acquire downward acceleration");
        Assert.All(vessel.Parts.Parts, part =>
        {
            Assert.True(double.IsFinite(part.Temperature));
            Assert.True(double.IsFinite(part.SkinTemperature));
            Assert.InRange(part.ThermalDamage, 0.0, 1.0);
        });
    }

    [Fact]
    public void WarpPolicyLeavesAtmosphericVesselOffRailsAndVacuumVesselOnRails()
    {
        var earth = LoadBody("earth");
        var atmospheric = CreateProbe(earth, 150_000.0, 0.0, 7_700.0, "perf-atmospheric");
        var vacuum = CreateProbe(earth, 1_200_000.0, 0.0, 7_100.0, "perf-vacuum");
        vacuum.IsOnRails = true;

        var universe = new Universe { ActiveVessel = atmospheric, TimeScale = 50.0 };
        universe.AddBody(earth);
        universe.AddVessel(atmospheric);
        universe.AddVessel(vacuum);

        Assert.True(universe.RequiresOffRailsPhysics(atmospheric));
        Assert.False(universe.RequiresOffRailsPhysics(vacuum));

        for (int i = 0; i < 20; i++)
        {
            universe.Tick(0.02);
            AssertFinite(universe);
        }

        Assert.False(atmospheric.IsOnRails,
            "atmospheric vessel incorrectly entered analytic rails");
        Assert.True(vacuum.IsOnRails,
            "vacuum vessel lost its analytic-rails classification");
    }

    private enum PhysicsTier
    {
        Active,
        Nearby,
        OnRails,
    }

    private static PhysicsTier Classify(Universe universe, Vessel vessel)
    {
        if (ReferenceEquals(universe.ActiveVessel, vessel))
            return PhysicsTier.Active;
        return universe.RequiresOffRailsPhysics(vessel)
            ? PhysicsTier.Nearby
            : PhysicsTier.OnRails;
    }

    private static Vessel CreateProbe(
        CelestialBody body,
        double altitude,
        double velocityX,
        double velocityY,
        string id = "perf-probe")
    {
        var vessel = new Vessel(id);
        vessel.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = "perf-probe-part",
            CategoryStr = "command",
            MassDry = 100_000.0,
            LengthM = 50.0,
            DiameterM = 9.0,
            HeatTolerance = 10_000.0,
        }));
        vessel.Position = body.Position + Vector3d.Right * (body.Radius + altitude);
        vessel.Velocity = body.Velocity + new Vector3d(velocityX, velocityY, 0.0);
        vessel.SASEnabled = false;
        return vessel;
    }

    private static Vessel CreateStarshipSizedStack()
    {
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(DataRoot(), "parts"));
        var vessel = new Vessel("perf-starship-stack");
        var command = new Part(catalog["starship_command"]);
        var tank = new Part(catalog["starship_tank"]);
        var engines = new Part(catalog["starship_engines"]);
        var gear = new Part(catalog["starship_landing_gear"]);
        var decoupler = new Part(catalog["decoupler_heavy"]);
        var booster = new Part(catalog["super_heavy_booster"]);

        vessel.Parts.SetRoot(command);
        vessel.Parts.AddJoint(new Joint(command, tank, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(tank, engines, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(engines, gear, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(gear, decoupler, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(decoupler, booster, "bottom", "top"));
        vessel.ConfigureLandingContactsFromParts();
        vessel.ConfigureCatchContactsFromParts();
        return vessel;
    }

    private static void AssertFinite(Universe universe)
    {
        AssertFinite(universe.CurrentTime, "universe.CurrentTime");
        foreach (var body in universe.Bodies)
        {
            AssertFinite(body.Position, $"body[{body.Id}].Position");
            AssertFinite(body.Velocity, $"body[{body.Id}].Velocity");
        }

        foreach (var vessel in universe.Vessels)
        {
            AssertFinite(vessel.Position, $"vessel[{vessel.Id}].Position");
            AssertFinite(vessel.Velocity, $"vessel[{vessel.Id}].Velocity");
            AssertFinite(vessel.Orientation, $"vessel[{vessel.Id}].Orientation");
            AssertFinite(vessel.AngularVelocity, $"vessel[{vessel.Id}].AngularVelocity");
            AssertFinite(vessel.TotalMass, $"vessel[{vessel.Id}].TotalMass");
            if (vessel.OrbitalState is { } orbit)
            {
                AssertFinite(orbit.SemiMajorAxis, $"vessel[{vessel.Id}].orbit.a");
                AssertFinite(orbit.Eccentricity, $"vessel[{vessel.Id}].orbit.e");
                AssertFinite(orbit.Inclination, $"vessel[{vessel.Id}].orbit.i");
                AssertFinite(orbit.LongitudeOfAscendingNode, $"vessel[{vessel.Id}].orbit.node");
                AssertFinite(orbit.ArgumentOfPeriapsis, $"vessel[{vessel.Id}].orbit.argument");
                AssertFinite(orbit.MeanAnomalyAtEpoch, $"vessel[{vessel.Id}].orbit.mean_anomaly");
                AssertFinite(orbit.Epoch, $"vessel[{vessel.Id}].orbit.epoch");
                AssertFinite(orbit.SpecificAngularMomentum, $"vessel[{vessel.Id}].orbit.h");
                AssertFinite(orbit.PeriapsisRadius, $"vessel[{vessel.Id}].orbit.periapsis");
            }

            foreach (var part in vessel.Parts.Parts)
            {
                AssertFinite(part.CurrentMass, $"part[{part.InstanceId}].CurrentMass");
                AssertFinite(part.Temperature, $"part[{part.InstanceId}].Temperature");
                AssertFinite(part.SkinTemperature, $"part[{part.InstanceId}].SkinTemperature");
                AssertFinite(part.ThermalDamage, $"part[{part.InstanceId}].ThermalDamage");
                foreach (var engine in part.EngineStates)
                {
                    AssertFinite(engine.CommandedThrottle, $"engine[{engine.InstanceId}].command");
                    AssertFinite(engine.ActualThrottle, $"engine[{engine.InstanceId}].actual");
                    AssertFinite(engine.ChamberPressureFraction, $"engine[{engine.InstanceId}].pressure");
                    AssertFinite(engine.TemperatureK, $"engine[{engine.InstanceId}].temperature");
                }
            }
        }
    }

    private static void AssertFinite(Quaterniond value, string name)
    {
        AssertFinite(value.W, $"{name}.W");
        AssertFinite(value.X, $"{name}.X");
        AssertFinite(value.Y, $"{name}.Y");
        AssertFinite(value.Z, $"{name}.Z");
    }

    private static void AssertFinite(Vector3d value, string name)
    {
        AssertFinite(value.X, $"{name}.X");
        AssertFinite(value.Y, $"{name}.Y");
        AssertFinite(value.Z, $"{name}.Z");
    }

    private static void AssertFinite(double value, string name) =>
        Assert.True(double.IsFinite(value), $"{name} is not finite: {value:R}");

    private static double Percentile(IReadOnlyList<double> samples, double percentile)
    {
        var ordered = samples.OrderBy(value => value).ToArray();
        int index = (int)Math.Ceiling(percentile * ordered.Length) - 1;
        return ordered[Math.Clamp(index, 0, ordered.Length - 1)];
    }

    private static CelestialBody LoadBody(string id) =>
        CelestialBody.LoadFromJson(Path.Combine(DataRoot(), "bodies", $"{id}.json"));

    private static string DataRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "data"))
                && File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
                return Path.Combine(directory.FullName, "data");
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository data root.");
    }
}
