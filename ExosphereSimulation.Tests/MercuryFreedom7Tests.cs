namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Data;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Propulsion;
using Xunit;

public sealed class MercuryFreedom7Tests
{
    private static readonly DirectoryInfo Root = FindRepoRoot();

    [Fact]
    public void HistoricalVariantClosesPublishedMassDimensionsAndA7Thrust()
    {
        var variant = LoadVariant();
        var assembly = variant.Build(LoadParts());
        var metrics = assembly.ComputeMetrics();
        var graph = assembly.ToPartGraph();

        Assert.Equal("Mercury-Redstone", variant.Family);
        Assert.Equal(new DateOnly(1961, 5, 5), variant.AsOfDate);
        Assert.Equal(7, assembly.Parts.Count);
        Assert.True(assembly.ValidateForLaunch().CanLaunch);
        Assert.Equal(29_931.0, metrics.WetMass, 5);
        Assert.Equal(6_184.0, metrics.DryMass, 5);
        Assert.Equal(23_747.0, metrics.PropellantMass, 5);
        Assert.Equal(346_944.0, metrics.SeaLevelThrust, 5);
        Assert.InRange(metrics.SeaLevelTwr, 1.181, 1.183);
        Assert.Equal(25.3, graph.VehicleLength, 8);
        Assert.Equal(2.1, graph.MaximumDiameter, 8);

        var engine = LoadEngines().Models["redstone-a7-mr3-1961"];
        Assert.Equal(346_944.0, engine.RatedThrustSeaLevelN);
        Assert.Equal("RP-1", engine.Fuel);
        Assert.Equal("LOX", engine.Oxidizer);
    }

    [Fact]
    public void CrewSiteAndEveryMercuryEstimateAreTraceable()
    {
        var crew = CrewDefinition.LoadFromJson(Path.Combine(
            Root.FullName, "data", "crew", "alan_b_shepard_jr_1961.json"));
        var sites = LaunchSite.LoadAllFromDirectory(
            Path.Combine(Root.FullName, "data", "launch_sites"));
        var provenance = LoadProvenance();

        Assert.Equal("Alan B. Shepard Jr.", crew.CreateMember().FullName);
        Assert.Equal(28.4399, sites["cape_canaveral_lc5"].Latitude, 4);
        Assert.Equal(-80.5736, sites["cape_canaveral_lc5"].Longitude, 4);
        provenance.RequireFields(
            "redstone-a7-mr3-1961",
            "ratedThrustSeaLevelN",
            "ratedThrustVacuumN",
            "specificImpulseSeaLevelS",
            "specificImpulseVacuumS",
            "minimumThrottle",
            "performanceMap",
            "restartEnvelope",
            "thermalEnvelope",
            "gimbalEnvelope",
            "startupTransient",
            "shutdownTransient");
        provenance.RequireFields(
            "mercury-redstone3-freedom7-1961-05-05",
            "launchMassKg",
            "dimensionsM",
            "componentMassAllocation");
        provenance.RequireFields(
            "mercury_capsule_freedom7",
            "massDryKg",
            "integratedSpacecraftMassKg",
            "parachuteSequence",
            "parachuteDragAreaM2",
            "axialDragCoefficient");
        provenance.RequireFields(
            "mercury_retro_pack_freedom7",
            "massAndPropellantKg",
            "thrustAndBurn");
        provenance.RequireFields(
            "mercury_escape_tower_freedom7",
            "axialDragCoefficient");
        Assert.Equal(
            ProvenanceStatus.Published,
            provenance.Require(
                "redstone-a7-mr3-1961", "ratedThrustSeaLevelN").Status);
        Assert.Equal(
            ProvenanceStatus.Estimated,
            provenance.Require(
                "cape_canaveral_lc5", "geodeticLocation").Status);
    }

    [Fact]
    public void HistoricalScheduleUsesPublishedEventsAndSmoothPitchProgram()
    {
        Assert.Equal(142.0, MercuryRedstoneFlightProfile.MainEngineCutoffSeconds);
        Assert.Equal(152.5, MercuryRedstoneFlightProfile.SpacecraftSeparationSeconds);
        Assert.Equal(284.0, MercuryRedstoneFlightProfile.RetroSequenceSeconds);
        Assert.Equal(6_705.6, MercuryRedstoneFlightProfile.DrogueDeployAltitudeM);
        Assert.Equal(3_048.0, MercuryRedstoneFlightProfile.MainDeployAltitudeM);
        Assert.Equal(90.0, MercuryRedstoneFlightProfile.ElevationDegrees(0.0));
        Assert.Equal(90.0, MercuryRedstoneFlightProfile.ElevationDegrees(12.0));
        Assert.InRange(
            MercuryRedstoneFlightProfile.ElevationDegrees(77.0),
            69.9,
            70.1);
        Assert.Equal(
            50.0,
            MercuryRedstoneFlightProfile.ElevationDegrees(142.0),
            10);
    }

    [Fact]
    public void BoosterAndEscapeTowerSeparationPreserveHistoricalCapsule()
    {
        var vessel = LoadVariant().Build(LoadParts()).ToVessel("Freedom 7");
        double initialMass = vessel.TotalMass;

        Vessel? booster = vessel.Stage();
        Assert.NotNull(booster);
        Assert.Equal(initialMass, vessel.TotalMass + booster!.TotalMass, 8);
        Assert.Contains(
            vessel.Parts.Parts,
            part => part.Definition.Id == "mercury_capsule_freedom7");

        var tower = vessel.Parts.Parts.Single(
            part => part.Definition.HasVehicleRole("escape_tower"));
        Vessel? jettisoned = vessel.DeployPayload(
            tower.InstanceId,
            "Escape Tower",
            Vector3d.Up * 1.5);

        Assert.NotNull(jettisoned);
        Assert.Equal(
            "mercury_capsule_freedom7",
            vessel.Parts.Root?.Definition.Id);
        Assert.Equal(3, vessel.Parts.Parts.Count);
        Assert.Contains(
            vessel.Parts.Parts,
            part => part.Definition.Id == "mercury_redstone_separation_ring");
        Assert.Equal(
            initialMass,
            vessel.TotalMass + booster.TotalMass + jettisoned!.TotalMass,
            8);
    }

    [Fact]
    public void ReefedAndMainChutesProducePhysicalDragAndSurvivableSplashdown()
    {
        var earth = LoadBody("earth");
        var capsuleDefinition = LoadParts()["mercury_capsule_freedom7"];
        var vessel = new Vessel();
        var capsule = new Part(capsuleDefinition);
        vessel.Parts.SetRoot(capsule);
        var up = Vector3d.Right;
        vessel.Position = earth.Position + up * (earth.Radius + 6_000.0);
        vessel.Velocity = earth.Velocity
            + earth.GetSurfaceVelocity(vessel.Position)
            - up * 150.0;

        Assert.Equal(1, vessel.DeployParachutes(earth));
        Assert.True(vessel.HasDeployedParachute);
        Assert.Equal(4.0, vessel.GetDeployedParachuteDragArea(earth), 8);
        Vector3d reefedDrag = vessel.ComputeDrag(earth);

        vessel.Position = earth.Position + up * (earth.Radius + 2_000.0);
        Assert.Equal(300.0, vessel.GetDeployedParachuteDragArea(earth), 8);
        Vector3d mainDrag = vessel.ComputeDrag(earth);
        Assert.True(mainDrag.Magnitude > reefedDrag.Magnitude * 20.0);

        vessel.Position = earth.Position + up * (earth.Radius + 0.001);
        vessel.Velocity = earth.Velocity
            + earth.GetSurfaceVelocity(vessel.Position)
            - up * 10.0;
        var universe = new Universe { ActiveVessel = vessel };
        universe.AddBody(earth);
        universe.AddVessel(vessel);
        universe.Tick(0.02);

        Assert.False(vessel.IsDestroyed);
        Assert.True(earth.GetAltitude(vessel.Position) >= 0.0);
    }

    [Fact]
    public void HeadlessFreedom7FliesPoweredArcSeparatesAndSplashdowns()
    {
        string data = Path.Combine(Root.FullName, "data");
        var universe = Universe.LoadFromDataDirectory(data);
        var earth = universe.GetBody("earth")!;
        var site = LaunchSite.LoadFromJson(Path.Combine(
            data, "launch_sites", "cape_canaveral_lc5.json"));
        var vessel = LoadVariant().Build(LoadParts()).ToVessel("Freedom 7");
        vessel.Crew.Add(CrewDefinition.LoadFromJson(Path.Combine(
            data, "crew", "alan_b_shepard_jr_1961.json")).CreateMember());
        Vector3d pad = site.GetPosition(earth);
        Vector3d up = (pad - earth.Position).Normalized;
        vessel.Position = pad + up * 4.0;
        vessel.Velocity = site.GetVelocity(earth);
        vessel.Orientation = Quaterniond.FromTo(Vector3d.Up, up);
        vessel.IsGroundHeld = true;
        vessel.GroundNormal = up;
        vessel.GroundOffset = 4.0;
        universe.AddVessel(vessel);
        universe.SetActiveVessel(vessel.Id);

        vessel.Throttle = 1.0;
        for (int i = 0; i < 200; i++) universe.Tick(0.02);
        Assert.True(vessel.GetThrustToWeightRatio(earth) > 1.02);
        vessel.ReleaseGroundHold();
        double startTime = universe.CurrentTime;
        Vector3d launchDirection = earth.ToBodyFixedDirection(
            (vessel.Position - earth.Position).Normalized,
            universe.CurrentTime).Normalized;
        var frame = site.GetLocalFrame(earth, universe.CurrentTime);
        double heading = site.Heading * MathUtils.DEG_TO_RAD;
        Vector3d downrange = (
            frame.North * System.Math.Cos(heading)
            + frame.East * System.Math.Sin(heading)).Normalized;

        bool staged = false;
        bool towerJettisoned = false;
        bool chutes = false;
        double peakAltitude = 0.0;
        double peakSpeed = 0.0;
        double peakG = 0.0;
        double peakGAltitude = 0.0;
        double peakGTime = 0.0;
        double peakGCosAlpha = 0.0;
        double maxDownrange = 0.0;
        double splashdownTime = double.NaN;

        for (int step = 0; step < 12_000 && !vessel.IsDestroyed; step++)
        {
            double elapsed = universe.CurrentTime - startTime;
            double altitude = vessel.GetAltitude(earth);
            up = (vessel.Position - earth.Position).Normalized;
            Vector3d surfaceVelocity = vessel.GetSurfaceVelocity(earth);
            double radialSpeed = surfaceVelocity.Dot(up);

            if (elapsed < MercuryRedstoneFlightProfile.MainEngineCutoffSeconds)
            {
                double elevation = MercuryRedstoneFlightProfile
                    .ElevationDegrees(elapsed) * MathUtils.DEG_TO_RAD;
                Vector3d target = (
                    downrange * System.Math.Cos(elevation)
                    + up * System.Math.Sin(elevation)).Normalized;
                vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(
                    vessel.Orientation,
                    Vector3d.Up,
                    target,
                    vessel.AngularVelocity,
                    2.8,
                    1.25);
                vessel.Throttle = 1.0;
            }
            else
            {
                double retroEnd =
                    MercuryRedstoneFlightProfile.RetroSequenceSeconds
                    + MercuryRedstoneFlightProfile.RetroBurnDurationSeconds;
                vessel.Throttle =
                    elapsed >= MercuryRedstoneFlightProfile.RetroSequenceSeconds
                    && elapsed < retroEnd
                        ? 1.0
                        : 0.0;
                if (towerJettisoned)
                {
                    Vector3d entryTarget = -surfaceVelocity.Normalized;
                    vessel.PitchYawRoll =
                        AttitudeGuidance.ComputeAxisPointingCommand(
                            vessel.Orientation,
                            Vector3d.Up,
                            entryTarget,
                            vessel.AngularVelocity);
                }
            }

            if (!staged
                && elapsed >= MercuryRedstoneFlightProfile
                    .SpacecraftSeparationSeconds)
            {
                Vessel? booster = vessel.Stage();
                Assert.NotNull(booster);
                staged = true;
            }
            if (!towerJettisoned
                && elapsed >= MercuryRedstoneFlightProfile
                    .EscapeTowerJettisonSeconds)
            {
                var tower = vessel.Parts.Parts.Single(part =>
                    part.Definition.HasVehicleRole("escape_tower"));
                Assert.NotNull(vessel.DeployPayload(
                    tower.InstanceId,
                    "Escape Tower",
                    Vector3d.Up * 1.5));
                towerJettisoned = true;
            }
            if (!chutes
                && radialSpeed < 0.0
                && altitude <= MercuryRedstoneFlightProfile
                    .DrogueDeployAltitudeM)
                chutes = vessel.DeployParachutes(earth) > 0;

            universe.Tick(0.1);

            altitude = System.Math.Max(0.0, vessel.GetAltitude(earth));
            surfaceVelocity = vessel.GetSurfaceVelocity(earth);
            peakAltitude = System.Math.Max(peakAltitude, altitude);
            peakSpeed = System.Math.Max(
                peakSpeed,
                (vessel.Velocity - earth.Velocity).Magnitude);
            double currentG =
                vessel.GetProperAcceleration(earth).Magnitude / 9.80665;
            if (currentG > peakG)
            {
                peakG = currentG;
                peakGAltitude = altitude;
                peakGTime = universe.CurrentTime - startTime;
                peakGCosAlpha = System.Math.Abs(
                    vessel.Orientation.Rotate(Vector3d.Up).Normalized.Dot(
                        vessel.GetSurfaceVelocity(earth).Normalized));
            }
            Vector3d currentSurface = earth.ToBodyFixedDirection(
                (vessel.Position - earth.Position).Normalized,
                universe.CurrentTime).Normalized;
            maxDownrange = System.Math.Max(
                maxDownrange,
                System.Math.Acos(System.Math.Clamp(
                    currentSurface.Dot(launchDirection), -1.0, 1.0))
                    * earth.Radius);
            if (towerJettisoned
                && altitude <= 1.1
                && surfaceVelocity.Magnitude < 1.0)
            {
                splashdownTime = universe.CurrentTime - startTime;
                break;
            }
        }

        Assert.True(staged);
        Assert.True(towerJettisoned);
        Assert.True(chutes);
        Assert.False(vessel.IsDestroyed,
            $"capsule destroyed at {vessel.CrashImpactSpeed:F1} m/s");
        Assert.True(double.IsFinite(splashdownTime), "capsule never splashed down");
        string evidence =
            $"apogee={peakAltitude:F1}m speed={peakSpeed:F1}m/s " +
            $"range={maxDownrange:F1}m duration={splashdownTime:F1}s " +
            $"maxG={peakG:F2}@{peakGAltitude:F0}m/{peakGTime:F1}s " +
            $"cosA={peakGCosAlpha:F3}";
        Assert.True(
            peakAltitude is >= 160_000.0 and <= 220_000.0
            && peakSpeed is >= 2_100.0 and <= 2_600.0
            && maxDownrange is >= 400_000.0 and <= 560_000.0
            && splashdownTime is >= 800.0 and <= 1_050.0
            && peakG is >= 0.0 and <= 13.0,
            evidence);
    }

    private static VehicleVariantDefinition LoadVariant() =>
        VehicleVariantDefinition.LoadFromJson(Path.Combine(
            Root.FullName,
            "data",
            "vehicles",
            "mercury_redstone3_freedom7_1961.json"));

    private static PartCatalog LoadParts() =>
        PartCatalog.LoadFromDirectory(Path.Combine(
            Root.FullName, "data", "parts"));

    private static EngineDefinitionCatalog LoadEngines() =>
        EngineDefinitionCatalog.Load(
            Path.Combine(Root.FullName, "data", "engines"),
            Path.Combine(Root.FullName, "data", "engine_clusters"),
            LoadProvenance());

    private static DataProvenanceRegistry LoadProvenance() =>
        DataProvenanceRegistry.LoadFromDirectory(Path.Combine(
            Root.FullName, "data", "provenance"));

    private static CelestialBody LoadBody(string id) =>
        CelestialBody.LoadFromJson(Path.Combine(
            Root.FullName, "data", "bodies", $"{id}.json"));

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
