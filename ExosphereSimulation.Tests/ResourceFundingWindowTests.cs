namespace Exosphere.Simulation.Tests;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;

public sealed class ResourceFundingWindowTests
{
    [Fact]
    public void ConcurrentEngineAndVentReservationsHaveIndependentCutsAndExactEndpoints()
    {
        var part = Engine(2.25, 6.75);
        part.ReserveLandingPropellant(0.25, 0.75);
        var vessel = Ship(part);
        vessel.PropellantVentRateKgPerS = 14;
        vessel.Tick(1, Body());
        var window = Assert.IsType<ResourceFundingWindow>(vessel.LastResourceFunding);
        Assert.True(window.HasCompleteCoverage, window.CoverageError);
        Assert.Null(vessel.LastLiquidEngineFunding);
        Assert.Null(vessel.LastMainVentFunding);
        Assert.NotNull(window.LiquidEngines);
        Assert.NotNull(window.Vent);
        Assert.Equal(4, window.InitialStores.Count);
        Assert.Equal(4, window.Debits.Count);
        Assert.Equal(1, window.DurationSeconds);
        var mass = Distribution(window);
        Assert.Equal(new[] { 0.0, 0.5, 1.0 }, mass.BoundariesSeconds);
        Assert.Equal(1009, mass.Sample(0).MassKg);
        Assert.Equal(1005.25, mass.Sample(0.25).MassKg);
        Assert.Equal(1001.5, mass.Sample(0.5).MassKg);
        Assert.Equal(1001, mass.Sample(1).MassKg);
        Assert.Equal(-15, mass.Sample(0.5, MassBoundarySide.Left).MassRateKgPerS);
        Assert.Equal(-1, mass.Sample(0.5, MassBoundarySide.Right).MassRateKgPerS);
        Assert.Equal(0.25, part.LiquidFuel);
        Assert.Equal(0.75, part.Oxidizer);
        Assert.Equal(part.CurrentMass, mass.Sample(1).MassKg);
        Assert.All(window.Debits.Where(draw => draw.Source == ResourceWithdrawalSource.MainVent),
            draw => Assert.Equal(0.5, draw.EndTimeSeconds));
    }

    [Fact]
    public void HeaderTransitionAndEngineCutAreRetainedInTheCompleteInventory()
    {
        var part = Engine(0.375, 2, header: 0.125);
        part.ReserveLandingPropellant(0.125, 0.5);
        var vessel = Ship(part);
        vessel.Tick(2, Body());
        var window = vessel.LastResourceFunding!;
        Assert.True(window.HasCompleteCoverage, window.CoverageError);
        var mass = Distribution(window);
        Assert.Equal(new[] { 0.0, 1, 1.5, 2 }, mass.BoundariesSeconds);
        Assert.Equal(1002.5, mass.Sample(0).MassKg);
        Assert.Equal(1001.25, mass.Sample(1.25).MassKg);
        Assert.Equal(part.CurrentMass, mass.Sample(2).MassKg);
        Assert.Equal(0, mass.Sample(1.75).MassRateKgPerS);
        Assert.Single(window.InitialStores.Where(load => load.Store.HeaderIndex.HasValue));
    }

    [Theory]
    [InlineData("solid", PropellantResourceKind.SolidFuel)]
    [InlineData("monopropellant", PropellantResourceKind.Monopropellant)]
    public void NonLiquidEngineWithdrawalsCannotDisappearFromTheLedger(string fuelType, PropellantResourceKind resource)
    {
        var part = Engine(0, 0);
        part.Definition.FuelTypeStr = fuelType;
        part.SolidFuel = part.Monopropellant = 2;
        var vessel = Ship(part);
        vessel.Tick(1, Body());
        var window = vessel.LastResourceFunding!;
        Assert.True(window.HasCompleteCoverage, window.CoverageError);
        var debit = Assert.Single(window.Debits);
        Assert.Equal(resource, debit.Store.Resource);
        Assert.Equal(ResourceWithdrawalSource.OtherEngine, debit.Source);
        Assert.Equal(1, debit.MassKg);
        Assert.Equal(1004, Distribution(window).Sample(0).MassKg);
        Assert.Equal(1003, Distribution(window).Sample(1).MassKg);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DisturbanceAndRcsDebitsComposeOnOneMonopropellantStore(bool coupled)
    {
        var part = new Part(new PartDefinition
        {
            Id = "command", CategoryStr = "command", MassDry = 1000, LengthM = 1, DiameterM = 1,
            FuelCapacityMono = 10, RcsAttitudeStartsEnabled = true,
            RcsThrusters = [new() { Id = "jet", Direction = [0, 1, 0], ThrustN = 9.80665, IspS = 1 },
                new() { Id = "reverse", Direction = [0, -1, 0], ThrustN = 9.80665, IspS = 1 }],
        }, "command") { RcsAttitudeEnabled = true };
        var vessel = Ship(part);
        vessel.TranslationCommand = Vector3d.Up;
        vessel.DisturbanceMassFlowKgPerS = 0.5;
        var universe = new Universe { ActiveVessel = vessel, Coupled6DofIntegrationEnabled = coupled };
        universe.AddBody(Body()); universe.AddVessel(vessel);
        universe.Tick(0.02);
        var window = vessel.LastResourceFunding!;
        Assert.True(window.HasCompleteCoverage, window.CoverageError);
        var disturbance = Assert.Single(window.Debits.Where(draw => draw.Source == ResourceWithdrawalSource.Disturbance));
        var rcs = Assert.Single(window.Debits.Where(draw => draw.Source == ResourceWithdrawalSource.ReactionControl));
        Assert.Equal(0.01, disturbance.MassKg, 12);
        Assert.Equal(disturbance.AfterKg, rcs.BeforeKg);
        Assert.Equal(part.Monopropellant, rcs.AfterKg);
        Assert.NotNull(window.ReactionControl);
        Assert.Null(vessel.LastRcsFunding);
        double q = 0.5 + window.ReactionControl.OutflowMassKg / 0.02;
        Assert.Equal(1010 - q * 0.01, Distribution(window).Sample(0.01).MassKg, 11);
        Assert.Equal(part.CurrentMass, Distribution(window).Sample(0.02).MassKg);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompleteCapturePreservesAllPhysicalResults(bool coupled)
    {
        var plain = Ship(Engine(1, 3));
        var captured = Ship(Engine(1, 3));
        plain.CaptureResourceFunding = false;
        plain.PropellantVentRateKgPerS = captured.PropellantVentRateKgPerS = 0.25;
        plain.DisturbanceMassFlowKgPerS = captured.DisturbanceMassFlowKgPerS = 0.5;
        plain.Parts.Root!.Monopropellant = captured.Parts.Root!.Monopropellant = 1;
        Universe World(Vessel ship)
        {
            var world = new Universe { Coupled6DofIntegrationEnabled = coupled, ActiveVessel = ship };
            world.AddBody(Body()); world.AddVessel(ship); return world;
        }
        var a = World(plain); var b = World(captured);
        for (int i = 0; i < 3; i++)
        {
            a.Tick(0.02); b.Tick(0.02);
            Assert.True(captured.LastResourceFunding!.HasCompleteCoverage);
            Assert.Equal(plain.Position, captured.Position);
            Assert.Equal(plain.Velocity, captured.Velocity);
            Assert.Equal(plain.Orientation, captured.Orientation);
            Assert.Equal(plain.AngularVelocity, captured.AngularVelocity);
            Assert.Equal(plain.Parts.TotalMass, captured.Parts.TotalMass);
            Assert.Equal(plain.Parts.Root.LiquidFuel, captured.Parts.Root.LiquidFuel);
            Assert.Equal(plain.Parts.Root.Oxidizer, captured.Parts.Root.Oxidizer);
            Assert.Equal(plain.Parts.Root.Monopropellant, captured.Parts.Root.Monopropellant);
        }
        Assert.Null(plain.LastResourceFunding);
    }

    [Fact]
    public void AssistedCaptureRetainsGrossInventoryAndRequiresAReplenishmentTransition()
    {
        var part = Engine(1, 3);
        var vessel = Ship(part); vessel.InfinitePropellant = true;
        vessel.Tick(0.5, Body());
        var window = vessel.LastResourceFunding!;
        Assert.True(window.HasCompleteCoverage);
        Assert.True(window.UsesInfinitePropellantAssist);
        Assert.Equal(1, part.LiquidFuel);
        var store = window.InitialStores.First().Store;
        Assert.Throws<InvalidOperationException>(() => window.CreateStoreMassComponent(store, "store",
            Vector3d.Zero, Matrix3x3d.Identity));
        Assert.Equal(0.875, window.FinalStores.Single(load => load.Store == store).MassKg);
    }

    [Fact]
    public void PublishedInventoryIsImmutableAndDisabledCaptureClearsIt()
    {
        var vessel = Ship(Engine(1, 3)); vessel.Tick(0.5, Body());
        var window = vessel.LastResourceFunding!;
        var start = window.InitialStores.ToArray(); var end = window.FinalStores.ToArray();
        var debits = window.Debits.ToArray();
        vessel.CaptureResourceFunding = false; vessel.Tick(0.5, Body());
        Assert.Null(vessel.LastResourceFunding);
        Assert.Equal(start, window.InitialStores); Assert.Equal(end, window.FinalStores);
        Assert.Equal(debits, window.Debits);
        Assert.Throws<NotSupportedException>(() => ((IList<FundedStoreDebit>)window.Debits)[0] = default);
        Assert.Throws<NotSupportedException>(() => ((IList<ResourceStoreLoad>)window.InitialStores)[0] = default);
        Assert.Throws<NotSupportedException>(() => ((IList<ResourceStoreLoad>)window.FinalStores)[0] = default);
    }

    [Fact]
    public void InvalidInitialInventoryCannotBeCertifiedAsComplete()
    {
        var part = Engine(1, 3); part.Monopropellant = -1;
        var vessel = Ship(part); vessel.Tick(0.5, Body());
        var window = vessel.LastResourceFunding!;
        Assert.False(window.HasCompleteCoverage);
        Assert.Contains("Invalid initial", window.CoverageError);
        Assert.Throws<InvalidOperationException>(() => Distribution(window));
    }

    [Fact]
    public void UndeclaredHeaderSlotsRemainExplicitUnconsumedMass()
    {
        var part = Engine(1, 3, header: 0.125); part.Definition.HeaderTanks.Clear();
        var vessel = Ship(part); vessel.Tick(0.5, Body());
        var window = vessel.LastResourceFunding!;
        Assert.True(window.HasCompleteCoverage);
        var header = Assert.Single(window.InitialStores.Where(load => load.Store.HeaderIndex.HasValue));
        Assert.Equal(PropellantResourceKind.OtherHeader, header.Store.Resource);
        Assert.Null(header.HeaderId);
        Assert.DoesNotContain(window.Debits, debit => debit.Store == header.Store);
        Assert.Equal(part.CurrentMass, Distribution(window).Sample(0.5).MassKg);
    }

    private static BodyMassDistribution Distribution(ResourceFundingWindow window)
    {
        var components = new List<FixedShapeMassComponent>
        { new("dry", Vector3d.Zero, Matrix3x3d.Identity, [new(0, 1000)]) };
        for (int i = 0; i < window.InitialStores.Count; i++)
            components.Add(window.CreateStoreMassComponent(window.InitialStores[i].Store, $"store-{i}",
                Vector3d.Zero, Matrix3x3d.Identity));
        return new(window.DurationSeconds, components);
    }

    private static Vessel Ship(Part part)
    {
        var ship = new Vessel("window") { CaptureResourceFunding = true, SASEnabled = false,
            Position = Vector3d.Right * 10000, ReferenceBodyId = "body", Throttle = 1 };
        ship.Parts.SetRoot(part); return ship;
    }
    private static CelestialBody Body() => new() { Id = "body", Radius = 1, SphereOfInfluence = 1e9 };
    private static Part Engine(double fuel, double oxidizer, double header = 0) => new(new PartDefinition
    {
        Id = "engine", CategoryStr = "engine", MassDry = 1000, LengthM = 1, DiameterM = 1,
        FuelCapacityLF = fuel, FuelCapacityOx = oxidizer, ThrustVac = 980.665, ThrustSL = 980.665,
        IspVac = 100, IspSL = 100, MixtureRatio = 3, FuelTypeStr = "liquidfuel",
        HeaderTanks = header > 0 ? [new() { Id = "header", Propellant = "liquid_fuel", CapacityKg = header }] : [],
    }, "engine") { IsStagingActive = true, ThrottleLevel = 1 };
}
