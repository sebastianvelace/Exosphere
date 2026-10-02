namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Xunit;
using Xunit.Abstractions;

public sealed class Flight14PayloadTests(ITestOutputHelper output)
{
    [Fact]
    public void LoadedManifestAddsTwentySixMassesWithoutExtendingTheHullAndShieldingEndsAtSeparation()
    {
        string data = DataDirectory();
        var baseline = VehicleVariantDefinition.LoadFromJson(Path.Combine(data,
            "vehicles/starship_flight12_v3_2026.json")).Build(
                PartCatalog.LoadFromDirectory(Path.Combine(data, "parts"))).ToVessel("Baseline");
        var run = Flight14LaunchDiagnostic.Create(data);
        var payloads = run.Ship.Parts.Parts.Where(p => p.Definition.HasVehicleRole("payload")).ToArray();
        Assert.Equal(26, payloads.Length);
        Assert.Equal(26, payloads.Select(p => p.InstanceId).Distinct().Count());
        double manifestMass = payloads.Sum(p => p.CurrentMass);
        Assert.Equal(baseline.TotalMass+manifestMass, run.Ship.TotalMass, 8);
        Assert.Equal(baseline.VehicleLength, run.Ship.VehicleLength);
        Assert.Equal(baseline.MaximumDiameter, run.Ship.MaximumDiameter);
        Assert.Equal(baseline.NoseRadius, run.Ship.NoseRadius);
        Assert.Equal(baseline.Parts.AxialDragCoefficient, run.Ship.Parts.AxialDragCoefficient);
        Assert.NotEqual(baseline.Parts.CenterOfMass, run.Ship.Parts.CenterOfMass);
        Assert.All(payloads, p => Assert.True(run.Ship.Parts.IsEnclosedPayload(p)));
        double beforeTemperature = payloads[0].Temperature;
        StressSolver.ApplyThermalLoads(run.Ship.Parts, 1e6, 1, Vector3d.Up);
        Assert.All(payloads, p => Assert.Equal(beforeTemperature, p.Temperature));
        StressSolver.ApplyThermalLoads(run.Ship.Parts, 1e6, 1);
        Assert.All(payloads, p => Assert.Equal(beforeTemperature, p.Temperature));
        var detached = run.Ship.DeployPayload(payloads[0].InstanceId)!;
        Assert.False(detached.Parts.IsEnclosedPayload(payloads[0]));
        Assert.Equal(payloads[0].Definition.LengthM, detached.VehicleLength);
        Assert.Equal(payloads[0].Definition.DiameterM, detached.MaximumDiameter);
        StressSolver.ApplyThermalLoads(detached.Parts, 1e6, 1, Vector3d.Up);
        Assert.True(payloads[0].Temperature > beforeTemperature);
    }

    [Fact]
    public void SequentialSplitsPreserveManifestMassCenterMomentumAndOpeningVelocity()
    {
        var run = Flight14LaunchDiagnostic.Create(DataDirectory());
        var ship = run.Ship;
        // A dedicated separation fixture, not a launch-to-orbit acceptance shortcut.
        ship.Position = new Vector3d(7e6, 2e6, -1e6);
        ship.Velocity = new Vector3d(100, 7000, -1000);
        ship.Orientation = Quaterniond.FromAxisAngle(Vector3d.Up, 0.7);
        ship.AngularVelocity = new Vector3d(0.003, 0.005, -0.002);
        var parts = ship.Parts.Parts.Where(p => p.Definition.HasVehicleRole("payload")).ToArray();
        var separated = new List<Vessel>();
        double initialMass = ship.TotalMass;
        foreach (var part in parts)
        {
            double massBefore = ship.TotalMass;
            var centerBefore = ship.Position;
            var momentumBefore = ship.Velocity*massBefore;
            var payload = ship.DeployPayload(part.InstanceId, null, run.PayloadDefinition.OpeningVelocity)!;
            separated.Add(payload);
            Assert.Equal(massBefore, ship.TotalMass+payload.TotalMass, 7);
            var center = (ship.Position*ship.TotalMass+payload.Position*payload.TotalMass)/massBefore;
            var momentum = ship.Velocity*ship.TotalMass+payload.Velocity*payload.TotalMass;
            Assert.True(center.DistanceTo(centerBefore) < 1e-7);
            Assert.True(momentum.DistanceTo(momentumBefore) < 0.01);
            var relativeOpening = payload.Velocity-ship.Velocity
                -ship.AngularVelocity.Cross(payload.Position-ship.Position);
            Assert.True(relativeOpening.DistanceTo(ship.Orientation.Rotate(run.PayloadDefinition.OpeningVelocity)) < 1e-7);
            Assert.Same(part, payload.Parts.Root);
            Assert.Null(ship.DeployPayload(part.InstanceId));
        }
        Assert.Equal(initialMass, ship.TotalMass+separated.Sum(s => s.TotalMass), 7);
        Assert.DoesNotContain(ship.Parts.Parts, p => p.Definition.HasVehicleRole("payload"));
    }

    [Fact]
    public void PadToTwentySixSatellitesUsesTheSameCarrierResourcesAndPropagatesReleasedOrbits()
    {
        var run = Flight14LaunchDiagnostic.Create(DataDirectory());
        var tank = run.Ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("tank"));
        double lastFuel = tank.LiquidFuel+tank.Oxidizer, settledFuel = double.NaN;
        double initialManifestMass = run.Ship.Parts.Parts.Where(p => p.Definition.HasVehicleRole("payload")).Sum(p => p.CurrentMass);
        int previousCount = 0;
        while (run.Universe.CurrentTime < run.PayloadDefinition.MaximumMissionSeconds+5
            && run.PayloadController.Phase is not (Flight14PayloadPhase.Complete or Flight14PayloadPhase.Blocked))
        {
            run.AdvanceFrame(1.0/30);
            Assert.False(run.Ship.IsDestroyed);
            Assert.False(run.Ship.StructuralControlLost);
            double fuel = tank.LiquidFuel+tank.Oxidizer;
            Assert.True(fuel <= lastFuel+1e-6);
            lastFuel = fuel;
            int count = run.PayloadController.Releases.Count;
            if (count > previousCount)
            {
                Assert.Equal(Flight14LaunchPhase.OrbitReady, run.Controller.Phase);
                Assert.Equal(0, run.Ship.Throttle);
                Assert.InRange(run.Ship.GetCurrentThrust(run.Earth), 0, 1);
                Assert.InRange(run.Ship.AngularVelocity.Magnitude, 0, run.PayloadDefinition.MaximumReleaseAngularRateRadPerSecond);
                if (double.IsNaN(settledFuel)) settledFuel = fuel;
                else Assert.Equal(settledFuel, fuel, 7);
                var release = run.PayloadController.Releases[^1];
                Assert.Contains(release.Satellite, run.Universe.Vessels);
                Assert.InRange(System.Math.Abs(release.MassResidualKg), 0, 1e-6);
                Assert.InRange(release.CenterResidualM, 0, 1e-7);
                Assert.InRange(release.MomentumResidualKgMps, 0, 0.01);
                Assert.InRange(release.RelativeOpeningResidualMps, 0, 1e-7);
                Assert.True(release.PeriapsisAltitudeM >= run.PayloadDefinition.MinimumPeriapsisAltitudeM);
                previousCount = count;
            }
        }
        Assert.Equal(Flight14PayloadPhase.Complete, run.PayloadController.Phase);
        Assert.Equal(26, run.PayloadController.Releases.Count);
        Assert.Same(run.Ship, run.Universe.ActiveVessel);
        Assert.Equal(initialManifestMass, run.PayloadController.Releases.Sum(r => r.Satellite.TotalMass), 7);
        Assert.DoesNotContain(run.Ship.Parts.Parts, p => p.Definition.HasVehicleRole("payload"));
        var firstRelease = run.PayloadController.Releases[0];
        var firstPosition = firstRelease.Satellite.Position;
        for (int i = 0; i < 1800; i++) run.AdvanceFrame(1.0/30);
        Assert.True(firstRelease.Satellite.Position.DistanceTo(firstPosition) > 1e5);
        Assert.All(run.PayloadController.Releases, r => {
            Assert.False(r.Satellite.IsDestroyed);
            Assert.True(r.Satellite.GetAltitude(run.Earth) > run.Earth.Atmosphere!.MaxAltitude);
            Assert.Equal(r.PartInstanceId, r.Satellite.Parts.Root!.InstanceId);
        });
        output.WriteLine($"Payload-loaded launch: cutoff={run.Controller.CutoffElapsedSeconds:F2}s "
            +$"insertion={run.Controller.InsertionElapsedSeconds:F2}s releases="
            +$"{firstRelease.MissionElapsedSeconds:F2}..{run.PayloadController.Releases[^1].MissionElapsedSeconds:F2}s "
            +$"mass={initialManifestMass:F0}kg remainingFuel={lastFuel:F0}kg");
    }

    [Fact]
    public void OwnershipLossBlocksPayloadCommandsWithoutAReleaseOrPoseMutation()
    {
        var run = Flight14LaunchDiagnostic.Create(DataDirectory());
        var other = new Vessel();
        run.Universe.AddVessel(other);
        run.Universe.SetActiveVessel(other.Id);
        var position = run.Ship.Position;
        run.PayloadController.BeforePhysicsStep(run.Universe, 0.02);
        Assert.Equal(Flight14PayloadPhase.Blocked, run.PayloadController.Phase);
        Assert.Equal("active-vessel-changed", run.PayloadController.BlockReason);
        Assert.Empty(run.PayloadController.Releases);
        Assert.Equal(position, run.Ship.Position);
        Assert.Equal(26, run.Ship.Parts.Parts.Count(p => p.Definition.HasVehicleRole("payload")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidOpeningVelocityCannotAuthorizeARelease(double speed)
    {
        var definition = Flight14PayloadDefinition.LoadFromJson(Path.Combine(DataDirectory(),
            "flight_profiles/starship_flight14_payload_estimate.json"));
        definition.OpeningVelocityLocalMps = [speed, 0, 0];
        Assert.Throws<InvalidDataException>(definition.Validate);
    }

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data"))) directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("Repository data not found."), "data");
    }
}
