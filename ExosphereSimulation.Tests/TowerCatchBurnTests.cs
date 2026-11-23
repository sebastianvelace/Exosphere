namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Xunit;
using Xunit.Abstractions;

public sealed class TowerCatchBurnTests(ITestOutputHelper output)
{
    private const string SeaLevelModel = "raptor3-starship-sl-flight12";
    private const double CatchMassKg = 215_000.0;
    /// <summary>Belly-flop long-axis tilt from vertical at ignition: nose about 10° up.</summary>
    public const double BellyFlopTiltDeg = 80.0;
    /// <summary>Ignition this far short of the tower, nose toward it: the flip throw and the
    /// braking that follows bring the ship to rest over the cradle.</summary>
    private const double NominalUpstreamM = 380.0;

    [Fact]
    public void SingleBurnIgnitesNearTheFlight14HeightAndOnlyStepsEnginesDown()
    {
        var bench = Bench.Create(upstreamM: NominalUpstreamM, glideMps: 0.0, crossrangeM: 0.0);
        var burn = bench.Burn;
        for (int i = 0; i < 1500 && burn.Phase is not (TowerCatchBurnPhase.Caught or TowerCatchBurnPhase.Blocked); i++)
            bench.Step(0.02);

        output.WriteLine($"ignition h={burn.IgnitionClearanceM:F0}m flip={burn.FlipCompletedS:F1}s "
            + $"steps={string.Join(" ", burn.EngineSteps.Select(s => $"{s.EngineCount}@{s.TimeS:F1}"))}");
        // Flight 14 broadcast: ignition at about 1.1-1.2 km at ~101 m/s.
        Assert.InRange(burn.IgnitionClearanceM, 1_000.0, 1_250.0);
        Assert.Equal(0, burn.Relights);
        var counts = burn.EngineSteps.Select(s => s.EngineCount).ToArray();
        Assert.Equal(3, counts[0]);
        Assert.True(counts.Zip(counts.Skip(1)).All(p => p.Second < p.First), string.Join(",", counts));
        Assert.Equal(1, counts[^1]);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-15.0)]
    [InlineData(15.0)]
    public void SingleBurnFlipsBrakesAndSettlesOnThePins(double crossrangeM) => FlyToCatch(crossrangeM);

    private void FlyToCatch(double crossrangeM)
    {
        var bench = Bench.Create(upstreamM: NominalUpstreamM, glideMps: 0.0, crossrangeM: crossrangeM);
        var burn = bench.Burn;
        for (int i = 0; i < 7000 && burn.Phase is not (TowerCatchBurnPhase.Caught or TowerCatchBurnPhase.Blocked); i++)
            bench.Step(0.02);

        output.WriteLine($"phase={burn.Phase} {burn.BlockReason} cutoff={burn.CutoffTimeS:F1}s caught={burn.CaughtTimeS:F1}s "
            + $"steps={string.Join(" ", burn.EngineSteps.Select(s => $"{s.EngineCount}@{s.TimeS:F1}"))} "
            + $"peak={bench.Ship.CatchPeakCompressionM:F2}m/{bench.Ship.CatchPeakPinLoadN / 1e6:F2}MN "
            + $"predicted={burn.LastCutoffDecision.Prediction.PeakCompressionM:F2}m");
        Assert.Equal(TowerCatchBurnPhase.Caught, burn.Phase);
        Assert.Equal(0, burn.Relights);
        Assert.Equal([3, 2, 1, 0], burn.EngineSteps.Select(s => s.EngineCount).ToArray());
        Assert.False(bench.Ship.CatchLimitExceeded);
        var pin = bench.Ship.CatchContactPoints[0];
        Assert.True(bench.Ship.CatchPeakCompressionM <= TowerCatchCutoff.LimitUtilization * pin.MaxCompressionM);
        // The ship lands tilted on one off-axis Raptor; the rigid two-pin prediction made at
        // cutoff must bound the peak the contact solver then produced.
        Assert.True(bench.Ship.CatchPeakCompressionM <= burn.LastCutoffDecision.Prediction.PeakCompressionM + 0.005,
            $"actual {bench.Ship.CatchPeakCompressionM:F3} m > predicted {burn.LastCutoffDecision.Prediction.PeakCompressionM:F3} m");
        // Flight 15 caught 18 s after ignition; this controller still hovers on one engine for
        // much longer, inside its 120 s burn budget.
        Assert.InRange(burn.CaughtTimeS, 14.0, 120.0);
    }

    [Fact]
    public void IgnitionWaitsForTheProfileInsteadOfAFixedAltitude()
    {
        var definition = Bench.Definition();

        Assert.False(definition.ShouldIgnite(3_000.0, 100.0));
        Assert.True(definition.ShouldIgnite(800.0, 100.0));
        Assert.False(definition.ShouldIgnite(800.0, 0.0));
    }

    internal sealed class Bench
    {
        public required Universe Universe { get; init; }
        public required Vessel Ship { get; init; }
        public required CelestialBody Earth { get; init; }
        public required LaunchSite Site { get; init; }
        public required TowerCatchBurn Burn { get; init; }
        public required Vector3d East { get; init; }

        public static TowerCatchBurnDefinition Definition() =>
            TowerCatchBurnDefinition.LoadFromJson(Path.Combine(Data(), "flight_profiles",
                "starship_flight15_catch_burn_estimate.json"));

        public void Step(double dt)
        {
            Refresh(Ship, Earth, Site, Universe.CurrentTime);
            Burn.Advance(Universe, dt);
            Universe.Tick(dt);
        }

        /// <summary>
        /// Belly-flop ignition state over Starbase. The long axis is
        /// <see cref="BellyFlopTiltDeg"/> from vertical (nose slightly up), the nose points
        /// <paramref name="noseTowardTower"/> or away from it, the ship is
        /// <paramref name="upstreamM"/> short of the cradle along the approach, and it moves
        /// toward the tower at <paramref name="glideMps"/>.
        /// </summary>
        public static Bench Create(double upstreamM, double glideMps, double crossrangeM,
            bool noseTowardTower = true, double descentMps = 100.0)
        {
            string data = Data();
            var earth = CelestialBody.LoadFromJson(Path.Combine(data, "bodies/earth.json"));
            earth.Position = Vector3d.Zero; earth.Velocity = Vector3d.Zero; earth.OrbitalElements = null;
            var site = LaunchSite.LoadFromJson(Path.Combine(data, "launch_sites/starbase.json"));
            var ship = VehicleVariantDefinition.LoadFromJson(Path.Combine(data, "vehicles/starship_flight12_v3_2026.json"))
                .Build(PartCatalog.LoadFromDirectory(Path.Combine(data, "parts"))).ToVessel("Tower catch bench");
            Assert.NotNull(ship.Stage());
            var tank = ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("tank"));
            // The game's catch chapter arrives with about 200 t; fill the main tank to that mass.
            tank.LiquidFuel = 0; tank.Oxidizer = 0;
            double capacity = tank.Definition.FuelCapacityLF + tank.Definition.FuelCapacityOx;
            double fill = System.Math.Clamp((CatchMassKg - ship.TotalMass) / capacity, 0.0, 1.0);
            tank.LiquidFuel = tank.Definition.FuelCapacityLF * fill;
            tank.Oxidizer = tank.Definition.FuelCapacityOx * fill;
            ship.ConfigureCatchContactsFromParts();
            ship.ReferenceBodyId = earth.Id; ship.SASEnabled = false;
            ship.IsAttemptingTowerCatch = true;
            var universe = new Universe(); universe.AddBody(earth); universe.AddVessel(ship); universe.ActiveVessel = ship;
            Refresh(ship, earth, site, 0.0);

            var definition = Definition();
            double clearance = 3_000.0;
            while (!definition.ShouldIgnite(clearance, descentMps)) clearance -= 1.0;
            var up = ship.CatchTargetUpWorld;
            var east = earth.RotationAxis.Cross(up).Normalized;
            var north = up.Cross(east).Normalized;
            var cradle = ship.CatchTargetPositionWorld;
            // Approach from the west toward the tower (+east).
            ship.Position = cradle + up * (clearance - ship.CatchPinContactHeightAboveDatumM)
                - east * upstreamM + north * crossrangeM;
            var velocity = -up * descentMps + east * glideMps;
            ship.Velocity = ship.CatchTargetVelocityWorld + velocity;
            var nose = noseTowardTower ? east : -east;
            double tilt = BellyFlopTiltDeg * MathUtils.DEG_TO_RAD;
            var axis = (up * System.Math.Cos(tilt) + nose * System.Math.Sin(tilt)).Normalized;
            ship.Orientation = AerodynamicsModel.ComputeBellyFirstOrientation(axis, -up);
            var burn = new TowerCatchBurn(ship, earth, definition, SeaLevelModel);
            return new Bench { Universe = universe, Ship = ship, Earth = earth, Site = site, Burn = burn, East = east };
        }

        private static void Refresh(Vessel ship, CelestialBody earth, LaunchSite site, double time)
        {
            var spec = LaunchComplexSpec.StarbasePostDeluge;
            var cradle = spec.GetCatchCradlePosition(site, earth, time, ship);
            ship.CatchTargetPositionWorld = cradle;
            ship.CatchTargetUpWorld = site.GetLocalFrame(earth, time).Up;
            ship.CatchTargetVelocityWorld = earth.Velocity + earth.GetSurfaceVelocity(cradle);
            ship.CatchTargetEpochSeconds = time;
            ship.CatchTargetRails = spec.GetCatchArmRails(site, earth, time);
        }

        private static string Data()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data"))) directory = directory.Parent;
            return Path.Combine(directory!.FullName, "data");
        }
    }
}
