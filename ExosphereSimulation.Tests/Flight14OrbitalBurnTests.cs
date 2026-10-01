namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Xunit;
using Xunit.Abstractions;

public sealed class Flight14OrbitalBurnTests(ITestOutputHelper output)
{
    private static readonly string Root = RepositoryRoot();

    [Fact]
    public void ReferenceKeepsEarlyReturnSeparateFromPlannedNineHourTimeline()
    {
        var profile = Flight14MissionDefinition.LoadFromJson(Path.Combine(
            Root, "data/flight_profiles/starship_flight14_2026.json"));
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 12, 48, 0, TimeSpan.Zero), profile.LaunchUtc);
        Assert.Equal("northern-pacific", profile.ReturnRegion);
        Assert.Equal(26, profile.PayloadCount);
        Assert.Equal([11, 5, 3], profile.BoosterLandingEngineSequence);
        Assert.Equal(31, profile.BoosterBoostbackEngines);
        Assert.Equal(1, profile.InsertionSeaLevelEngines);
        Assert.Equal(1, profile.DeorbitSeaLevelEngines);
        Assert.Equal("main-tank-lox-exhaustion", profile.BoosterBoostbackResourceLimit);
        Assert.Equal(31957, profile.PlannedEvents.Single(e => e.Id == "deorbit-start").ElapsedSeconds);
        Assert.Equal(7904, profile.ReferenceWindows.Single(e => e.Id == "early-deorbit-interval").EndSeconds);
        Assert.True(profile.ComparisonOnly);
        Assert.NotEmpty(profile.Unknowns);
    }

    [Theory]
    [InlineData(Flight14BurnKind.Insertion)]
    [InlineData(Flight14BurnKind.Deorbit)]
    public void RealForceIntegrationChangesPeriapsisWithOneSeaLevelEngine(Flight14BurnKind kind)
    {
        var (universe, ship, earth, engines) = Fixture(kind);
        double initialPeriapsis = Elements(ship, earth).Periapsis - earth.Radius;
        double initialEnergy = Energy(ship, earth), initialMass = ship.TotalMass;
        var burn = Burn(kind);
        var controller = new BurnController(burn, ship, earth);
        universe.PhysicsStepController = controller;
        int maximumRunning = 0;
        for (int i = 0; i < 6000 && burn.Status is not (Flight14BurnStatus.Completed
            or Flight14BurnStatus.Blocked or Flight14BurnStatus.TargetAlreadySatisfied); i++)
        {
            universe.Tick(0.02);
            var delivered = engines.GetEngineTelemetry(0).Where(e => e.ThrustN > 1).ToArray();
            maximumRunning = System.Math.Max(maximumRunning, delivered.Length);
            Assert.All(delivered, e => Assert.Equal("raptor3-starship-sl-flight12", engines.EngineStates.Single(s => s.InstanceId == e.InstanceId).EngineModelId));
        }
        Assert.Equal(Flight14BurnStatus.Completed, burn.Status);
        Assert.True(burn.HasDeliveredThrust);
        Assert.Equal(1, maximumRunning);
        Assert.False(ship.IsDestroyed);
        Assert.False(ship.StructuralControlLost);
        Assert.True(ship.TotalMass < initialMass);
        double finalPeriapsis = Elements(ship, earth).Periapsis - earth.Radius;
        if (kind == Flight14BurnKind.Insertion)
        {
            Assert.True(initialPeriapsis < earth.Atmosphere!.MaxAltitude);
            Assert.True(finalPeriapsis >= 200000);
            Assert.True(Energy(ship, earth) > initialEnergy);
        }
        else
        {
            Assert.True(initialPeriapsis > earth.Atmosphere!.MaxAltitude);
            Assert.InRange(finalPeriapsis, 55000, 60000);
            Assert.True(Energy(ship, earth) < initialEnergy);
        }
        output.WriteLine($"{kind}: actual burn elapsed={universe.CurrentTime:F2}s "
            + $"periapsis={initialPeriapsis:F1}->{finalPeriapsis:F1}m "
            + $"propellantUsed={initialMass-ship.TotalMass:F1}kg maxRunning={maximumRunning}");
    }

    [Fact]
    public void InsertionCoastAndDeorbitShareContinuousStateAndPropellant()
    {
        var (universe, ship, earth, engines) = Fixture(Flight14BurnKind.Insertion);
        double initialMass = ship.TotalMass;
        var insertion = Burn(Flight14BurnKind.Insertion);
        universe.PhysicsStepController = new BurnController(insertion, ship, earth);
        for (int i = 0; i < 6000 && insertion.Status != Flight14BurnStatus.Completed; i++)
            universe.Tick(0.02);
        Assert.Equal(Flight14BurnStatus.Completed, insertion.Status);
        double postInsertionMass = ship.TotalMass;
        // No state reseed between burns. Keep the same part runtimes, cooling and tanks.
        universe.PhysicsStepController = null;
        for (int i = 0; i < 3000; i++) universe.Tick(0.02);
        Assert.True(Elements(ship, earth).Periapsis-earth.Radius > earth.Atmosphere!.MaxAltitude);
        var deorbit = Burn(Flight14BurnKind.Deorbit);
        universe.PhysicsStepController = new BurnController(deorbit, ship, earth);
        for (int i = 0; i < 15000 && deorbit.Status is not (Flight14BurnStatus.Completed
            or Flight14BurnStatus.Blocked); i++) universe.Tick(0.02);
        Assert.Equal(Flight14BurnStatus.Completed, deorbit.Status);
        Assert.True(insertion.HasDeliveredThrust && deorbit.HasDeliveredThrust);
        Assert.True(initialMass > postInsertionMass && postInsertionMass > ship.TotalMass);
        Assert.Equal(1, engines.SelectedEngineCount);
        Assert.True(Elements(ship, earth).Periapsis-earth.Radius < earth.Atmosphere.MaxAltitude);
        output.WriteLine($"Continuous insertion/coast/deorbit: elapsed={universe.CurrentTime:F2}s "
            + $"totalPropellantUsed={initialMass-ship.TotalMass:F1}kg");
    }

    [Fact]
    public void FailedCentreEngineBlocksGoForOrbitalOperationsWithoutVacuumSubstitution()
    {
        var (_, ship, earth, engines) = Fixture(Flight14BurnKind.Insertion);
        Assert.True(engines.FailEngine(engines.EngineStates[0].InstanceId, "TEST_SL_OUT"));
        var burn = Burn(Flight14BurnKind.Insertion);
        var position = ship.Position;
        var velocity = ship.Velocity;
        var orientation = ship.Orientation;
        burn.Advance(ship, earth, 0.02, 0);
        Assert.Equal(Flight14BurnStatus.Blocked, burn.Status);
        Assert.Equal("sea-level-engine-health-or-selection", burn.BlockReason);
        Assert.Equal(0, ship.Throttle);
        Assert.Equal(position, ship.Position);
        Assert.Equal(velocity, ship.Velocity);
        Assert.Equal(orientation, ship.Orientation);
    }

    [Fact]
    public void ChangedMountOrderCannotSelectAVacuumEngineEvenWithThreeHealthyCentreEngines()
    {
        var (_, ship, earth, _) = Fixture(Flight14BurnKind.Insertion, vacuumFirst: true);
        var burn = Burn(Flight14BurnKind.Insertion);
        burn.Advance(ship, earth, 0.02, 0);
        Assert.Equal(Flight14BurnStatus.Blocked, burn.Status);
        Assert.Equal(0, ship.Throttle);
    }

    [Fact]
    public void AlignmentCannotWaitIndefinitelyWithoutControlAuthority()
    {
        var (_, ship, earth, _) = Fixture(Flight14BurnKind.Insertion);
        ship.Orientation = Quaterniond.Identity;
        var burn = new Flight14OrbitalBurn(Flight14BurnKind.Insertion, 200000,
            "raptor3-starship-sl-flight12", 3, 250000, 0.01);
        burn.Advance(ship, earth, 0.02, 0);
        Assert.Equal(Flight14BurnStatus.Blocked, burn.Status);
        Assert.Equal("attitude-settling-envelope-exceeded", burn.BlockReason);
        Assert.Equal(0, ship.Throttle);
    }

    [Fact]
    public void FailedVacuumEngineDoesNotBlockHealthyCentreEngineBurn()
    {
        var (_, ship, earth, engines) = Fixture(Flight14BurnKind.Insertion);
        Assert.True(engines.FailEngine(engines.EngineStates[3].InstanceId, "TEST_VAC_OUT"));
        var burn = Burn(Flight14BurnKind.Insertion);
        burn.Advance(ship, earth, 0.02, 0);
        Assert.Equal(Flight14BurnStatus.Burning, burn.Status);
        Assert.Equal(1, engines.SelectedEngineCount);
    }

    [Fact]
    public void AlreadyStableOrbitIsNotCreditedAsADeliveredInsertionBurn()
    {
        var (_, ship, earth, _) = Fixture(Flight14BurnKind.Deorbit);
        var burn = Burn(Flight14BurnKind.Insertion);
        burn.Advance(ship, earth, 0.02, 0);
        Assert.Equal(Flight14BurnStatus.TargetAlreadySatisfied, burn.Status);
        Assert.False(burn.HasDeliveredThrust);
        Assert.Equal(0, ship.Throttle);
    }

    [Fact]
    public void MisalignedEngineWaitsForPhysicalAttitudeResponse()
    {
        var (_, ship, earth, _) = Fixture(Flight14BurnKind.Insertion);
        ship.Orientation = Quaterniond.Identity;
        var initial = ship.Orientation;
        var burn = Burn(Flight14BurnKind.Insertion);
        burn.Advance(ship, earth, 0.02, 0);
        Assert.Equal(Flight14BurnStatus.Aligning, burn.Status);
        Assert.Equal(initial, ship.Orientation);
        Assert.Equal(0, ship.Throttle);
        Assert.True(ship.PitchYawRoll.Magnitude > 0);
    }

    private static Flight14OrbitalBurn Burn(Flight14BurnKind kind) => new(kind,
        kind == Flight14BurnKind.Insertion ? 200000 : 60000,
        "raptor3-starship-sl-flight12", 3, 250000, 120);

    private static (Universe Universe, Vessel Ship, CelestialBody Earth, Part Engines) Fixture(Flight14BurnKind kind, bool vacuumFirst = false)
    {
        var earth = CelestialBody.LoadFromJson(Path.Combine(Root, "data/bodies/earth.json"));
        earth.Position = Vector3d.Zero;
        earth.Velocity = Vector3d.Zero;
        // Isolated central-body test: do not propagate an absent Sun parent or its orbit.
        earth.OrbitalElements = null;
        var universe = new Universe();
        universe.AddBody(earth);
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(Root, "data/parts"));
        if (vacuumFirst)
        {
            var mounts = catalog["starship_v3_engines_flight12"].ResolvedEngineCluster!.Engines;
            (mounts[0], mounts[3]) = (mounts[3], mounts[0]);
        }
        var ship = VehicleVariantDefinition.LoadFromJson(Path.Combine(
            Root, "data/vehicles/starship_flight12_v3_2026.json")).Build(catalog).ToVessel("Engineering burn fixture");
        Assert.NotNull(ship.Stage());
        double radius = earth.Radius + 276000;
        double periapsis = earth.Radius + (kind == Flight14BurnKind.Insertion ? 60000 : 276000);
        double axis = (radius + periapsis) * 0.5;
        double speed = System.Math.Sqrt(earth.GM * (2 / radius - 1 / axis));
        ship.Position = Vector3d.Right * radius;
        ship.Velocity = Vector3d.Forward * speed;
        ship.Orientation = Quaterniond.FromTo(Vector3d.Up,
            ship.Velocity.Normalized * (kind == Flight14BurnKind.Insertion ? 1 : -1));
        ship.SASEnabled = false;
        ship.ReferenceBodyId = earth.Id;
        var tank = ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("tank"));
        // Diagnostic reserve estimate, not a Flight 14 loading measurement.
        tank.LiquidFuel = 66000;
        tank.Oxidizer = 234000;
        var engines = ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("ship_engines"));
        universe.AddVessel(ship);
        universe.ActiveVessel = ship;
        return (universe, ship, earth, engines);
    }

    private static OrbitalElements Elements(Vessel ship, CelestialBody earth) =>
        OrbitalElements.FromStateVector(ship.Position - earth.Position, ship.Velocity - earth.Velocity, earth.GM, earth.Id, 0);
    private static double Energy(Vessel ship, CelestialBody earth) =>
        (ship.Velocity-earth.Velocity).MagnitudeSquared * 0.5 - earth.GM / (ship.Position-earth.Position).Magnitude;

    private sealed class BurnController(Flight14OrbitalBurn burn, Vessel ship, CelestialBody body) : IPhysicsStepController
    {
        public bool RequiresFixedCadence(Universe universe) => true;
        public void BeforePhysicsStep(Universe universe, double interval) =>
            burn.Advance(ship, body, interval, universe.CurrentTime);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository data not found.");
    }
}
