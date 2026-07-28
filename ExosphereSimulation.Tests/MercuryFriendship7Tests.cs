namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Campaign;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Data;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;
using Exosphere.Simulation.Propulsion;
using Xunit;

public sealed class MercuryFriendship7Tests
{
    private static readonly DirectoryInfo Root = FindRepoRoot();

    [Fact]
    public void HistoricalVariantClosesAtlasAndSpacecraftPublishedMasses()
    {
        var variant = LoadVariant();
        var catalog = LoadParts();
        var assembly = variant.Build(catalog);
        var metrics = assembly.ComputeMetrics();
        var parts = assembly.Parts;

        Assert.Equal("Mercury-Atlas", variant.Family);
        Assert.Equal(new DateOnly(1962, 2, 20), variant.AsOfDate);
        Assert.Equal(8, parts.Count);
        Assert.True(assembly.ValidateForLaunch().CanLaunch);
        Assert.Equal(120_373.7, metrics.WetMass, 5);
        Assert.Equal(1_934.7, parts
            .Where(part => part.DefinitionId is
                "mercury_capsule_friendship7"
                or "mercury_retro_pack_friendship7")
            .Sum(part =>
            {
                var definition = catalog[part.DefinitionId];
                return definition.MassDry
                    + definition.FuelCapacityLF
                    + definition.FuelCapacityOx
                    + definition.FuelCapacitySolid
                    + definition.FuelCapacityMono;
            }), 5);
        Assert.Equal(117_979.0, parts
            .Where(part => part.DefinitionId is
                "mercury_atlas_separation_ring_ma6"
                or "mercury_atlas_adapter_ma6"
                or "atlas_109d_propellant_stage_ma6"
                or "atlas_lr105_sustainer_ma6"
                or "atlas_lr89_booster_package_ma6")
            .Sum(part =>
            {
                var definition = catalog[part.DefinitionId];
                return definition.MassDry
                    + definition.FuelCapacityLF
                    + definition.FuelCapacityOx
                    + definition.FuelCapacitySolid
                    + definition.FuelCapacityMono;
            }), 5);
        Assert.Equal(1_601_280.0, metrics.SeaLevelThrust, 5);
        Assert.InRange(metrics.SeaLevelTwr, 1.35, 1.36);
        Assert.Equal(29.03, assembly.ToPartGraph().VehicleLength, 8);

        var engines = LoadEngines();
        Assert.Equal(
            667_200.0,
            engines.Models["atlas-lr89-ma6-1962"].RatedThrustSeaLevelN);
        Assert.Equal(
            266_880.0,
            engines.Models["atlas-lr105-ma6-1962"].RatedThrustSeaLevelN);
    }

    [Fact]
    public void StageAndHalfJettisonLeavesSustainerAndConservesMass()
    {
        var vessel = LoadVariant().Build(LoadParts()).ToVessel("Friendship 7");
        double initialMass = vessel.TotalMass;
        var package = vessel.Parts.Parts.Single(part =>
            part.Definition.HasVehicleRole("booster_engine_package"));

        Vessel? boosterPackage = vessel.DeployPayload(
            package.InstanceId,
            "Atlas booster package",
            new Vector3d(0.0, -0.8, 0.0));

        Assert.NotNull(boosterPackage);
        Assert.Equal(initialMass, vessel.TotalMass + boosterPackage!.TotalMass, 8);
        Assert.DoesNotContain(
            vessel.Parts.Parts,
            part => part.Definition.HasVehicleRole("booster_engine_package"));
        Assert.Contains(
            vessel.Parts.ActiveEngines,
            part => part.Definition.HasVehicleRole("sustainer_engine"));
        Assert.Single(vessel.Parts.ActiveEngines);
    }

    [Fact]
    public void MissionCrewSiteScheduleAndProvenanceAreHistorical()
    {
        var mission = LoadMission();
        var crew = CrewDefinition.LoadFromJson(Path.Combine(
            Root.FullName, "data", "crew", "john_h_glenn_jr_1962.json"));
        var sites = LaunchSite.LoadAllFromDirectory(
            Path.Combine(Root.FullName, "data", "launch_sites"));
        var provenance = DataProvenanceRegistry.LoadFromDirectory(
            Path.Combine(Root.FullName, "data", "provenance"));

        Assert.Equal(2, mission.Sequence);
        Assert.Equal(["mission-freedom7-1961"], mission.PrerequisiteMissionIds);
        Assert.Equal("John H. Glenn Jr.", crew.CreateMember().FullName);
        Assert.Equal(28.4911, sites["cape_canaveral_lc14"].Latitude, 4);
        Assert.Equal(-80.5465, sites["cape_canaveral_lc14"].Longitude, 4);
        Assert.Equal(129.6, MercuryAtlasFlightProfile.BoosterEngineCutoffSeconds);
        Assert.Equal(153.3, MercuryAtlasFlightProfile.TowerJettisonSeconds);
        Assert.Equal(301.4, MercuryAtlasFlightProfile.SustainerEngineCutoffSeconds);
        Assert.Equal(303.6, MercuryAtlasFlightProfile.SpacecraftSeparationSeconds);
        Assert.Equal(16_388.0, MercuryAtlasFlightProfile.RetroSequenceSeconds);
        Assert.Equal(17_723.0, MercuryAtlasFlightProfile.HistoricalSplashdownSeconds);
        Assert.Equal(90.0, MercuryAtlasFlightProfile.ElevationDegrees(0.0));
        Assert.Equal(17.0, MercuryAtlasFlightProfile.ElevationDegrees(129.6), 8);
        Assert.Equal(-20.0, MercuryAtlasFlightProfile.ElevationDegrees(301.4), 8);

        provenance.RequireFields(
            "mercury-atlas6-friendship7-1962-02-20",
            "atlasLaunchMassKg",
            "completeStackMassKg",
            "dimensionsM",
            "stageAndHalfSequence",
            "componentMassAllocation");
        provenance.RequireFields(
            "mission-friendship7-1962",
            "missionIdentity",
            "historicalOrbit",
            "historicalDurationS",
            "historicalSpeedMps",
            "historicalEvents",
            "objectiveEnvelope");
        Assert.Equal(
            ProvenanceStatus.Published,
            provenance.Require(
                "mission-friendship7-1962", "historicalOrbit").Status);
    }

    [Fact]
    public void OrbitMetricRoundTripAndEvaluatorRequireThreeRevolutions()
    {
        var director = new MissionDirector(LoadMission());
        director.Observe(Snapshot(0, "PRE_LAUNCH"));
        director.Observe(Snapshot(
            304,
            "ORBIT",
            altitudeM: 161_000,
            speedMps: 7_843,
            completedOrbits: 0.0));
        director.Observe(Snapshot(
            16_388,
            "RETRO_BURN",
            altitudeM: 261_140,
            speedMps: 7_843,
            completedOrbits: 3.02));

        var restored = new MissionDirector(
            LoadMission(), director.CaptureState());
        restored.Observe(Snapshot(
            17_723,
            "LANDED",
            gForce: 7.7,
            completedOrbits: 3.02,
            splashdown: true));
        MissionDebrief debrief = restored.FinalizeMission();

        Assert.Equal(3.02, restored.Evidence.CompletedOrbits, 8);
        Assert.Equal(3.02, debrief.NumericEvidence["completedOrbits"], 8);
        Assert.Equal(MissionOutcome.Success, debrief.Outcome);
        Assert.True(debrief.Objectives.Single(
            result => result.Id == "complete-three-orbits").Passed);
    }

    [Fact]
    public void HeadlessFriendship7FliesAtlasOrbitRetrofireAndSplashdown()
    {
        string data = Path.Combine(Root.FullName, "data");
        var universe = Universe.LoadFromDataDirectory(data);
        var earth = universe.GetBody("earth")!;
        var site = LaunchSite.LoadFromJson(Path.Combine(
            data, "launch_sites", "cape_canaveral_lc14.json"));
        var vessel = LoadVariant().Build(LoadParts()).ToVessel("Friendship 7");
        vessel.Crew.Add(CrewDefinition.LoadFromJson(Path.Combine(
            data, "crew", "john_h_glenn_jr_1962.json")).CreateMember());
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
        var frame = site.GetLocalFrame(earth, universe.CurrentTime);
        double heading = site.Heading * MathUtils.DEG_TO_RAD;
        Vector3d downrange = (
            frame.North * System.Math.Cos(heading)
            + frame.East * System.Math.Sin(heading)).Normalized;

        bool boosterJettisoned = false;
        bool towerJettisoned = false;
        bool spacecraftSeparated = false;
        bool retrofired = false;
        bool chutes = false;
        double insertionApoapsis = double.NaN;
        double insertionPeriapsis = double.NaN;
        double insertionAltitude = double.NaN;
        double insertionRadialSpeed = double.NaN;
        double insertionTangentialSpeed = double.NaN;
        double peakAltitude = 0.0;
        double peakSpeed = 0.0;
        double peakG = 0.0;
        double becoSpeed = double.NaN;
        double becoMass = double.NaN;
        double becoAltitude = double.NaN;
        double becoRadialSpeed = double.NaN;
        double secoMass = double.NaN;
        double orbitalRadians = 0.0;
        Vector3d previousOrbitalDirection = Vector3d.Zero;
        bool hasOrbitalDirection = false;
        double splashdownTime = double.NaN;
        double postRetroPeriapsis = double.NaN;
        double peakEntryHeatFlux = 0.0;
        double peakHeatShieldAlignment = 1.0;
        double chuteDeployAltitude = double.NaN;
        double chuteDeploySpeed = double.NaN;
        OrbitalElements? coastOrbit = null;

        for (int step = 0; step < 100_000 && !vessel.IsDestroyed; step++)
        {
            double elapsed = universe.CurrentTime - startTime;
            double retroApproach =
                MercuryAtlasFlightProfile.RetroSequenceSeconds - 60.0;
            if (coastOrbit != null && elapsed < retroApproach)
            {
                Vector3d before =
                    (vessel.Position - earth.Position).Normalized;
                double targetTime = System.Math.Min(
                    startTime + retroApproach,
                    universe.CurrentTime + 30.0);
                var (relativePosition, relativeVelocity) =
                    coastOrbit.GetStateAtTime(targetTime, earth.GM);
                universe.SetCurrentTime(targetTime);
                vessel.Position = earth.Position + relativePosition;
                vessel.Velocity = earth.Velocity + relativeVelocity;
                Vector3d coastSurfaceVelocity =
                    relativeVelocity
                    - earth.GetSurfaceVelocity(vessel.Position);
                if (coastSurfaceVelocity.MagnitudeSquared > 1e-12)
                    vessel.Orientation = Quaterniond.FromTo(
                        Vector3d.Up,
                        -coastSurfaceVelocity.Normalized);
                vessel.AngularVelocity = Vector3d.Zero;
                vessel.IsOnRails = false;
                vessel.OrbitalState = null;

                Vector3d after = relativePosition.Normalized;
                orbitalRadians += System.Math.Acos(System.Math.Clamp(
                    before.Dot(after), -1.0, 1.0));
                previousOrbitalDirection = after;
                hasOrbitalDirection = true;
                peakAltitude = System.Math.Max(
                    peakAltitude, relativePosition.Magnitude - earth.Radius);
                peakSpeed = System.Math.Max(
                    peakSpeed, relativeVelocity.Magnitude);
                continue;
            }

            double altitude = vessel.GetAltitude(earth);
            up = (vessel.Position - earth.Position).Normalized;
            Vector3d surfaceVelocity = vessel.GetSurfaceVelocity(earth);
            double radialSpeed = surfaceVelocity.Dot(up);
            if (altitude < MercuryAtlasFlightProfile.EntryInterfaceAltitudeM
                && earth.GetAtmosphericDensity(vessel.Position) > 1e-8
                && surfaceVelocity.MagnitudeSquared > 1e-12)
            {
                Vector3d flowLocal = vessel.Orientation.Inverse().Rotate(
                    surfaceVelocity.Normalized);
                double shieldAlignment = ThermalModel.WindwardFactor(
                    flowLocal, new Vector3d(0.0, -1.0, 0.0));
                double heatFlux = ThermalModel.ComputeHeatFlux(
                    earth.GetAtmosphericDensity(vessel.Position),
                    surfaceVelocity.Magnitude,
                    System.Math.Max(0.1, vessel.MaximumDiameter * 0.5));
                if (heatFlux > peakEntryHeatFlux)
                {
                    peakEntryHeatFlux = heatFlux;
                    peakHeatShieldAlignment = shieldAlignment;
                }
            }

            if (elapsed < MercuryAtlasFlightProfile
                    .SustainerEngineCutoffSeconds)
            {
                double elevation = MercuryAtlasFlightProfile
                    .ElevationDegrees(elapsed) * MathUtils.DEG_TO_RAD;
                Vector3d target = (
                    downrange * System.Math.Cos(elevation)
                    + up * System.Math.Sin(elevation)).Normalized;
                vessel.PitchYawRoll =
                    AttitudeGuidance.ComputeAxisPointingCommand(
                        vessel.Orientation,
                        Vector3d.Up,
                        target,
                        vessel.AngularVelocity,
                        2.6,
                        1.2);
                vessel.Throttle = boosterJettisoned
                    ? MercuryAtlasFlightProfile.SustainerThrottle(vessel.TotalMass)
                    : 1.0;
            }
            else
            {
                vessel.Throttle = elapsed
                        >= MercuryAtlasFlightProfile.RetroSequenceSeconds
                    && elapsed < MercuryAtlasFlightProfile.RetroSequenceSeconds
                        + MercuryAtlasFlightProfile.RetroBurnDurationSeconds
                    ? 1.0
                    : 0.0;
                if (spacecraftSeparated
                    && surfaceVelocity.MagnitudeSquared > 1e-12)
                {
                    vessel.PitchYawRoll =
                        AttitudeGuidance.ComputeAxisPointingCommand(
                            vessel.Orientation,
                            Vector3d.Up,
                            -surfaceVelocity.Normalized,
                            vessel.AngularVelocity,
                            2.2,
                            1.15);
                }
            }

            if (!boosterJettisoned
                && elapsed >= MercuryAtlasFlightProfile
                    .BoosterEngineCutoffSeconds)
            {
                becoSpeed = (vessel.Velocity - earth.Velocity).Magnitude;
                becoMass = vessel.TotalMass;
                becoAltitude = vessel.GetAltitude(earth);
                Vector3d becoUp =
                    (vessel.Position - earth.Position).Normalized;
                becoRadialSpeed =
                    (vessel.Velocity - earth.Velocity).Dot(becoUp);
                var package = vessel.Parts.Parts.Single(part =>
                    part.Definition.HasVehicleRole(
                        "booster_engine_package"));
                Assert.NotNull(vessel.DeployPayload(
                    package.InstanceId,
                    "Atlas booster package",
                    new Vector3d(0.0, -0.8, 0.0)));
                boosterJettisoned = true;
            }
            if (!towerJettisoned
                && elapsed >= MercuryAtlasFlightProfile.TowerJettisonSeconds)
            {
                var tower = vessel.Parts.Parts.Single(part =>
                    part.Definition.HasVehicleRole("escape_tower"));
                Assert.NotNull(vessel.DeployPayload(
                    tower.InstanceId,
                    "Escape Tower",
                    new Vector3d(0.0, 1.5, 0.0)));
                towerJettisoned = true;
            }
            if (!spacecraftSeparated
                && elapsed >= MercuryAtlasFlightProfile
                    .SpacecraftSeparationSeconds)
            {
                secoMass = vessel.TotalMass;
                Vessel? atlas = vessel.Stage();
                Assert.NotNull(atlas);
                Vector3d axis =
                    vessel.Orientation.Rotate(Vector3d.Up).Normalized;
                vessel.Position += axis * atlas!.VehicleLength;
                spacecraftSeparated = true;
                Assert.Equal(1_934.7, vessel.TotalMass, 5);
                Assert.DoesNotContain(vessel.Parts.Parts, part =>
                    part.Definition.HasVehicleRole(
                        "spacecraft_separation"));
                Assert.Contains(atlas.Parts.Parts, part =>
                    part.Definition.HasVehicleRole(
                        "spacecraft_separation"));
                coastOrbit = OrbitalElements.FromStateVector(
                    vessel.Position - earth.Position,
                    vessel.Velocity - earth.Velocity,
                    earth.GM,
                    earth.Id,
                    universe.CurrentTime);
                insertionApoapsis = coastOrbit.Apoapsis - earth.Radius;
                insertionPeriapsis = coastOrbit.Periapsis - earth.Radius;
                Vector3d insertionUp =
                    (vessel.Position - earth.Position).Normalized;
                Vector3d insertionVelocity =
                    vessel.Velocity - earth.Velocity;
                insertionAltitude = vessel.GetAltitude(earth);
                insertionRadialSpeed = insertionVelocity.Dot(insertionUp);
                insertionTangentialSpeed = (
                    insertionVelocity
                    - insertionUp * insertionRadialSpeed).Magnitude;
            }

            if (spacecraftSeparated
                && elapsed < MercuryAtlasFlightProfile.RetroSequenceSeconds)
            {
                Vector3d direction =
                    (vessel.Position - earth.Position).Normalized;
                if (hasOrbitalDirection)
                    orbitalRadians += System.Math.Acos(System.Math.Clamp(
                        previousOrbitalDirection.Dot(direction), -1.0, 1.0));
                previousOrbitalDirection = direction;
                hasOrbitalDirection = true;
            }
            if (elapsed >= MercuryAtlasFlightProfile.RetroSequenceSeconds)
                retrofired = true;
            double retroEnd = MercuryAtlasFlightProfile.RetroSequenceSeconds
                + MercuryAtlasFlightProfile.RetroBurnDurationSeconds;
            if (double.IsNaN(postRetroPeriapsis) && elapsed >= retroEnd)
            {
                var postRetroOrbit = OrbitalElements.FromStateVector(
                    vessel.Position - earth.Position,
                    vessel.Velocity - earth.Velocity,
                    earth.GM,
                    earth.Id,
                    universe.CurrentTime);
                postRetroPeriapsis =
                    postRetroOrbit.Periapsis - earth.Radius;
                if (postRetroPeriapsis > 100_000.0)
                    break;
            }
            if (!chutes
                && radialSpeed < 0.0
                && altitude <= MercuryAtlasFlightProfile
                    .DrogueDeployAltitudeM)
            {
                chutes = vessel.DeployParachutes(earth) > 0;
                if (chutes)
                {
                    chuteDeployAltitude = altitude;
                    chuteDeploySpeed = surfaceVelocity.Magnitude;
                }
            }

            universe.TimeScale = 1.0;
            universe.Tick(0.1);

            altitude = System.Math.Max(0.0, vessel.GetAltitude(earth));
            peakAltitude = System.Math.Max(peakAltitude, altitude);
            peakSpeed = System.Math.Max(
                peakSpeed,
                (vessel.Velocity - earth.Velocity).Magnitude);
            peakG = System.Math.Max(
                peakG,
                vessel.GetProperAcceleration(earth).Magnitude / 9.80665);
            if (towerJettisoned
                && altitude <= 1.1
                && vessel.GetSurfaceVelocity(earth).Magnitude < 1.0)
            {
                splashdownTime = universe.CurrentTime - startTime;
                break;
            }
        }

        string evidence =
            $"insertion={insertionPeriapsis:F0}x{insertionApoapsis:F0}m "
            + $"at={insertionAltitude:F0}m "
            + $"vr/vt={insertionRadialSpeed:F0}/{insertionTangentialSpeed:F0} "
            + $"peak={peakAltitude:F0}m speed={peakSpeed:F1}m/s "
            + $"orbits={orbitalRadians / System.Math.Tau:F3} "
            + $"duration={splashdownTime:F1}s maxG={peakG:F2} "
            + $"BECO={becoSpeed:F0}m/s@{becoMass:F0}kg "
            + $"/{becoAltitude:F0}m vr={becoRadialSpeed:F0} "
            + $"SECOmass={secoMass:F0}kg "
            + $"postRetroPe={postRetroPeriapsis:F0}m "
            + $"peakHeat={peakEntryHeatFlux:F0}Wm2@"
            + $"{peakHeatShieldAlignment:F3} "
            + $"chute={chuteDeployAltitude:F0}m/"
            + $"{chuteDeploySpeed:F1}mps "
            + $"destroyed={vessel.IsDestroyed}/{vessel.DestructionCause} "
            + "thermal=["
            + string.Join(
                ",",
                vessel.Parts.Parts.Select(part =>
                    $"{part.Definition.Id}:{part.Temperature:F0}/"
                    + $"{part.SkinTemperature:F0}/"
                    + $"{part.Definition.HeatTolerance:F0}"))
            + "]";
        Assert.True(boosterJettisoned, evidence);
        Assert.True(towerJettisoned, evidence);
        Assert.True(spacecraftSeparated, evidence);
        Assert.True(retrofired, evidence);
        Assert.True(chutes, evidence);
        Assert.False(vessel.IsDestroyed,
            $"{evidence}; destroyed at {vessel.CrashImpactSpeed:F1}m/s");
        Assert.True(double.IsFinite(splashdownTime), evidence);
        Assert.True(vessel.IsSurfaceSettled, evidence);
        Assert.True(postRetroPeriapsis < 100_000.0, evidence);
        Assert.True(peakEntryHeatFlux > 0.0, evidence);
        Assert.InRange(peakHeatShieldAlignment, 0.95, 1.0);
        Assert.InRange(chuteDeployAltitude, 8_000.0, 8_600.0);
        Assert.True(
            insertionPeriapsis is >= 120_000.0 and <= 210_000.0
            && insertionApoapsis is >= 220_000.0 and <= 310_000.0
            && peakSpeed is >= 7_650.0 and <= 8_050.0
            && orbitalRadians / System.Math.Tau >= 2.9
            && splashdownTime is >= 17_200.0 and <= 18_250.0
            && peakG <= 8.2,
            evidence);
    }

    private static MissionTelemetrySnapshot Snapshot(
        double time,
        string phase,
        double altitudeM = 0.0,
        double speedMps = 0.0,
        double gForce = 1.0,
        double completedOrbits = 0.0,
        bool splashdown = false) => new()
    {
        MissionTimeSeconds = time,
        Phase = phase,
        AltitudeM = altitudeM,
        SurfaceSpeedMps = speedMps,
        InertialSpeedMps = speedMps,
        GForce = gForce,
        CompletedOrbits = completedOrbits,
        CrewAlive = true,
        Splashdown = splashdown,
    };

    private static VehicleVariantDefinition LoadVariant() =>
        VehicleVariantDefinition.LoadFromJson(Path.Combine(
            Root.FullName,
            "data",
            "vehicles",
            "mercury_atlas6_friendship7_1962.json"));

    private static MissionDefinition LoadMission() =>
        MissionDefinition.LoadFromJson(Path.Combine(
            Root.FullName,
            "data",
            "missions",
            "friendship7_1962.json"));

    private static PartCatalog LoadParts() =>
        PartCatalog.LoadFromDirectory(Path.Combine(
            Root.FullName, "data", "parts"));

    private static EngineDefinitionCatalog LoadEngines()
    {
        string data = Path.Combine(Root.FullName, "data");
        return EngineDefinitionCatalog.Load(
            Path.Combine(data, "engines"),
            Path.Combine(data, "engine_clusters"),
            DataProvenanceRegistry.LoadFromDirectory(
                Path.Combine(data, "provenance")));
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
