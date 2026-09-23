namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Xunit;

public sealed class PhysicsParityIntegrationTests
{
    [Fact]
    public void LegacyAndCoupledPathsRemainWithinCoastTolerance()
    {
        var legacy = CreateUniverse("legacy", coupled: false, powered: false);
        var coupled = CreateUniverse("coupled", coupled: true, powered: false);

        for (int i = 0; i < 50; i++)
        {
            legacy.Tick(0.02);
            coupled.Tick(0.02);
        }

        var result = RigidBodyParityComparer.Compare(
            RigidBodyStateSnapshot.FromVessel(legacy.ActiveVessel!),
            RigidBodyStateSnapshot.FromVessel(coupled.ActiveVessel!),
            new RigidBodyParityTolerance(
                PositionMeters: 0.1,
                VelocityMetersPerSecond: 0.1,
                AttitudeRadians: 1e-6,
                AngularVelocityRadiansPerSecond: 1e-6));

        Assert.True(result.IsFinite);
        Assert.True(
            result.IsWithinTolerance,
            $"Legacy/coupled coast divergence exceeded tolerance: {result}");
        Assert.True(coupled.LastCoupled6DofTelemetry.Integrated);
    }

    [Fact]
    public void LegacyAndCoupledPathsRemainWithinPoweredAscentTolerance()
    {
        var legacy = CreateUniverse("legacy-powered", coupled: false, powered: true);
        var coupled = CreateUniverse("coupled-powered", coupled: true, powered: true);

        for (int i = 0; i < 25; i++)
        {
            legacy.Tick(0.02);
            coupled.Tick(0.02);
        }

        var result = RigidBodyParityComparer.Compare(
            RigidBodyStateSnapshot.FromVessel(legacy.ActiveVessel!),
            RigidBodyStateSnapshot.FromVessel(coupled.ActiveVessel!),
            new RigidBodyParityTolerance(
                PositionMeters: 0.5,
                VelocityMetersPerSecond: 0.5,
                AttitudeRadians: 1e-6,
                AngularVelocityRadiansPerSecond: 1e-6));

        Assert.True(result.IsFinite);
        Assert.True(
            result.IsWithinTolerance,
            $"Legacy/coupled powered divergence exceeded tolerance: {result}");
        Assert.True(coupled.ActiveVessel!.Parts.TotalLiquidFuel
            < legacy.ActiveVessel!.Parts.TotalLiquidFuel + 1e-9);
        Assert.True(coupled.LastCoupled6DofTelemetry.Integrated);
    }

    [Fact]
    public void LegacyAndCoupledPathsRemainWithinFlight7AscentTolerance()
    {
        var assembly = LoadFlight7Assembly(FindRepoRoot());
        var metrics = assembly.ComputeMetrics();
        Assert.Equal(4_800_000.0, metrics.WetMass, 5);
        Assert.Equal(300_000.0, metrics.DryMass, 5);
        Assert.Equal(4_500_000.0, metrics.PropellantMass, 5);
        Assert.Equal(5, assembly.Parts.Count);

        var legacy = CreateFlight7Universe("legacy-flight7", coupled: false);
        var coupled = CreateFlight7Universe("coupled-flight7", coupled: true);

        for (int i = 0; i < 100; i++)
        {
            legacy.Tick(0.02);
            coupled.Tick(0.02);
        }

        var result = RigidBodyParityComparer.Compare(
            RigidBodyStateSnapshot.FromVessel(legacy.ActiveVessel!),
            RigidBodyStateSnapshot.FromVessel(coupled.ActiveVessel!),
            new RigidBodyParityTolerance(
                PositionMeters: 5.0,
                VelocityMetersPerSecond: 5.0,
                AttitudeRadians: 1e-5,
                AngularVelocityRadiansPerSecond: 1e-5));

        Assert.True(result.IsFinite);
        Assert.True(
            result.IsWithinTolerance,
            $"Legacy/coupled Flight 7 divergence exceeded tolerance: {result}");
        Assert.True(coupled.ActiveVessel!.Parts.TotalLiquidFuel
            < legacy.ActiveVessel!.Parts.TotalLiquidFuel + 1e-9);
        Assert.True(coupled.LastCoupled6DofTelemetry.Integrated);
    }

    [Fact]
    public void Flight7ControlledPitchRemainsWithinAtmosphericParityTolerance()
    {
        var legacy = CreateFlight7Universe("legacy-flight7-controlled", coupled: false, useEarthData: true);
        var coupled = CreateFlight7Universe("coupled-flight7-controlled", coupled: true, useEarthData: true);
        var command = new Vector3d(0.15, 0.0, 0.0);
        legacy.ActiveVessel!.PitchYawRoll = command;
        coupled.ActiveVessel!.PitchYawRoll = command;

        for (int i = 0; i < 50; i++)
        {
            legacy.Tick(0.02);
            coupled.Tick(0.02);
        }

        var result = RigidBodyParityComparer.Compare(
            RigidBodyStateSnapshot.FromVessel(legacy.ActiveVessel),
            RigidBodyStateSnapshot.FromVessel(coupled.ActiveVessel),
            new RigidBodyParityTolerance(
                PositionMeters: 25.0,
                VelocityMetersPerSecond: 25.0,
                AttitudeRadians: 0.05,
                AngularVelocityRadiansPerSecond: 0.05));

        Assert.True(result.IsFinite);
        Assert.True(
            result.IsWithinTolerance,
            $"Legacy/coupled controlled Flight 7 divergence exceeded tolerance: {result}");
        Assert.True(coupled.ActiveVessel.AngularVelocity.Magnitude > 1e-4);
        Assert.Contains(
            coupled.ActiveVessel.Parts.ActiveEngines,
            part => part.EngineStates.Any(state => state.GimbalDeg.Magnitude > 1e-3));
        Assert.True(coupled.LastCoupled6DofTelemetry.Integrated);
    }

    [Fact]
    public void Flight7ClosedLoopElevationProgramRemainsWithinParityTolerance()
    {
        var legacy = CreateFlight7Universe("legacy-flight7-guidance", coupled: false, useEarthData: true);
        var coupled = CreateFlight7Universe("coupled-flight7-guidance", coupled: true, useEarthData: true);
        bool commandSeen = false;
        const double dt = 0.02;

        for (int i = 0; i < 250; i++)
        {
            double elapsed = i * dt;
            commandSeen |= ApplyElevationGuidance(legacy, elapsed);
            commandSeen |= ApplyElevationGuidance(coupled, elapsed);
            legacy.Tick(dt);
            coupled.Tick(dt);
        }

        var result = RigidBodyParityComparer.Compare(
            RigidBodyStateSnapshot.FromVessel(legacy.ActiveVessel!),
            RigidBodyStateSnapshot.FromVessel(coupled.ActiveVessel!),
            new RigidBodyParityTolerance(
                PositionMeters: 100.0,
                VelocityMetersPerSecond: 100.0,
                AttitudeRadians: 0.10,
                AngularVelocityRadiansPerSecond: 0.10));

        Assert.True(commandSeen);
        Assert.True(result.IsFinite);
        Assert.True(
            result.IsWithinTolerance,
            $"Legacy/coupled closed-loop Flight 7 divergence exceeded tolerance: {result}");
        Assert.True(coupled.ActiveVessel!.AngularVelocity.Magnitude > 1e-4);
        Assert.Contains(
            coupled.ActiveVessel.Parts.ActiveEngines,
            part => part.EngineStates.Any(state => state.GimbalDeg.Magnitude > 1e-3));
        Assert.True(coupled.LastCoupled6DofTelemetry.Integrated);
    }

    private static Universe CreateUniverse(string vesselId, bool coupled, bool powered)
    {
        var body = new CelestialBody
        {
            Id = "parity-body",
            Name = "Parity Body",
            Mass = 5.972e24,
            GM = 3.986004418e14,
            Radius = 6_371_000.0,
            SphereOfInfluence = 1.0e9,
            Position = Vector3d.Zero,
            Velocity = Vector3d.Zero,
        };
        var vessel = new Vessel(vesselId)
        {
            Position = Vector3d.Up * (body.Radius + (powered ? 1_000.0 : 250_000.0)),
            Velocity = powered ? Vector3d.Zero : Vector3d.Up * 7_600.0,
            ReferenceBodyId = body.Id,
            SASEnabled = false,
            Throttle = powered ? 1.0 : 0.0,
        };
        vessel.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = powered ? "parity-engine" : "parity-payload",
            CategoryStr = powered ? "engine" : "structure",
            MassDry = powered ? 1_000.0 : 1_000.0,
            LengthM = 10.0,
            DiameterM = 2.0,
            ThrustVac = powered ? 50_000.0 : 0.0,
            ThrustSL = powered ? 50_000.0 : 0.0,
            IspVac = powered ? 300.0 : 0.0,
            IspSL = powered ? 300.0 : 0.0,
            FuelTypeStr = powered ? "LiquidFuel+Oxidizer" : "",
            MixtureRatio = powered ? 2.0 : 0.0,
            FuelCapacityLF = powered ? 1_000.0 : 0.0,
            FuelCapacityOx = powered ? 2_000.0 : 0.0,
            ThrustPositionYM = powered ? -5.0 : 0.0,
        }));

        var universe = new Universe
        {
            ActiveVessel = vessel,
            Coupled6DofIntegrationEnabled = coupled,
        };
        universe.AddBody(body);
        universe.AddVessel(vessel);
        return universe;
    }

    private static Universe CreateFlight7Universe(
        string vesselId,
        bool coupled,
        bool useEarthData = false)
    {
        var root = FindRepoRoot();
        var body = useEarthData
            ? CelestialBody.LoadFromJson(Path.Combine(
                root.FullName, "data", "bodies", "earth.json"))
            : new CelestialBody
            {
                Id = "flight7-body",
                Name = "Flight 7 Body",
                Mass = 5.972e24,
                GM = 3.986004418e14,
                Radius = 6_371_000.0,
                SphereOfInfluence = 1.0e9,
                Position = Vector3d.Zero,
                Velocity = Vector3d.Zero,
            };
        var vessel = LoadFlight7Assembly(root).ToVessel("Starship Flight 7", vesselId);
        vessel.Position = Vector3d.Up * (body.Radius + 2_000.0);
        vessel.Velocity = useEarthData
            ? body.GetSurfaceVelocity(vessel.Position)
            : Vector3d.Zero;
        vessel.ReferenceBodyId = body.Id;
        vessel.SASEnabled = false;
        vessel.Throttle = 1.0;

        var universe = new Universe
        {
            ActiveVessel = vessel,
            Coupled6DofIntegrationEnabled = coupled,
        };
        universe.AddBody(body);
        universe.AddVessel(vessel);
        return universe;
    }

    private static VesselAssembly LoadFlight7Assembly(DirectoryInfo root)
    {
        var catalog = PartCatalog.LoadFromDirectory(
            Path.Combine(root.FullName, "data", "parts"));
        var variant = VehicleVariantDefinition.LoadFromJson(Path.Combine(
            root.FullName,
            "data",
            "vehicles",
            "starship_flight7_block2_2025.json"));
        return variant.Build(catalog);
    }

    private static bool ApplyElevationGuidance(Universe universe, double elapsedSeconds)
    {
        var vessel = universe.ActiveVessel!;
        var body = universe.GetBody(vessel.ReferenceBodyId!)!;
        double elevation = (90.0 - 55.0 * System.Math.Clamp(
            elapsedSeconds / 5.0, 0.0, 1.0)) * MathUtils.DEG_TO_RAD;
        var target = AttitudeGuidance.AimFromElevation(
            body.GetGeodeticUp(vessel.Position),
            body.GetEastDirection(vessel.Position),
            elevation);
        vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(
            vessel.Orientation,
            Vector3d.Up,
            target,
            vessel.AngularVelocity,
            proportionalGain: 2.6,
            dampingGain: 1.2);
        return vessel.PitchYawRoll.Magnitude > 1e-3;
    }

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
