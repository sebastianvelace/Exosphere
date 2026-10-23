namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Xunit;

/// <summary>Hand-flown docking: target readout and capture through the real port envelope.</summary>
public sealed class DockingTargetingTests
{
    private static (Universe universe, Vessel chaser, Vessel target) NoseToNose(double portGapM, double closingMps)
    {
        var root = SaturnVAscentFlight.DataDirectory();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(root, "parts"));
        var universe = Universe.LoadFromDataDirectory(root);
        var earth = universe.GetBody("earth")!;
        Vessel Single(string partId, string id)
        {
            var assembly = new VesselAssembly(catalog);
            assembly.AddRoot(partId);
            return assembly.ToVessel(id, id);
        }
        var target = Single("apollo11_lm5_ascent_stage", "eagle");
        var chaser = Single("apollo11_command_module_csm107", "columbia");
        double radius = earth.Radius + 185_000.0;
        target.Position = earth.Position + Vector3d.Right * radius;
        target.Velocity = earth.Velocity + Vector3d.Forward * System.Math.Sqrt(earth.GM / radius);
        target.Orientation = Quaterniond.Identity;
        chaser.Orientation = Quaterniond.FromAxisAngle(Vector3d.Right, System.Math.PI);
        Assert.True(target.Parts.TryGetAttachmentNodeLocalPosition(target.Parts.Parts[0].InstanceId, "top", out var lmPort));
        Assert.True(chaser.Parts.TryGetAttachmentNodeLocalPosition(chaser.Parts.Parts[0].InstanceId, "top", out var cmPort));
        chaser.Position = target.Position + target.Orientation.Rotate(lmPort) - chaser.Orientation.Rotate(cmPort)
            + Vector3d.Up * portGapM;
        chaser.Velocity = target.Velocity - Vector3d.Up * closingMps;
        universe.AddVessel(target);
        universe.AddVessel(chaser);
        return (universe, chaser, target);
    }

    [Fact]
    public void ReadsRangeClosingSpeedAndAlignmentOfTheNearestPort()
    {
        var (universe, chaser, target) = NoseToNose(portGapM: 40.0, closingMps: 0.5);
        var reading = DockingTargeting.FindNearest(universe, chaser)!.Value;
        Assert.Same(target, reading.Target);
        Assert.Equal(40.0, reading.RangeM, 3);
        Assert.Equal(0.5, reading.ClosingSpeedMps, 3);
        Assert.Equal(0.0, reading.LateralSpeedMps, 3);
        Assert.True(reading.AlignmentErrorDeg < 0.1);
    }

    [Fact]
    public void CoOrbitingCraftReadNoDriftInTheTargetsOrbitFrame()
    {
        var root = SaturnVAscentFlight.DataDirectory();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(root, "parts"));
        var training = Exosphere.Simulation.Training.TrainingDefinition
            .LoadAll(Path.Combine(root, "training")).Single(t => t.Id == "training-docking");
        var universe = Universe.LoadFromDataDirectory(root);
        var assembly = new VesselAssembly(catalog);
        var current = assembly.AddRoot(training.VehicleParts[0]);
        foreach (var part in training.VehicleParts.Skip(1))
            current = assembly.AttachPartAutomatically(current.InstanceId, part);
        var gemini = assembly.ToVessel("Gemini", "gemini");
        var agena = VehicleVariantDefinition.LoadFromJson(Path.Combine(root, "vehicles", training.TargetVariantFile))
            .Build(catalog).ToVessel("Agena", "agena");
        universe.AddVessel(gemini);
        universe.AddVessel(agena);
        Exosphere.Simulation.Training.TrainingSeeder.Seed(training, universe, gemini, agena);
        // Inertially they differ by v·θ ≈ 0.23 m/s; in the orbit frame they are at rest.
        Assert.True((gemini.Velocity - agena.Velocity).Magnitude > 0.2);
        var reading = DockingTargeting.FindNearest(universe, gemini)!.Value;
        Assert.True(reading.LateralSpeedMps < 0.01, $"Drift {reading.LateralSpeedMps:F3} m/s.");
        Assert.True(System.Math.Abs(reading.ClosingSpeedMps) < 0.01);
    }

    [Fact]
    public void CapturesOnlyInsideTheEnvelopeAndThenStopsTargetingTheTakenPort()
    {
        var (far, farChaser, _) = NoseToNose(portGapM: 3.0, closingMps: 0.1);
        Assert.Null(DockingTargeting.TryCapture(far, farChaser));

        var (universe, chaser, _) = NoseToNose(portGapM: 0.3, closingMps: 0.1);
        var attempt = DockingTargeting.TryCapture(universe, chaser);
        Assert.NotNull(attempt);
        Assert.True(attempt.Value.Succeeded, attempt.Value.Failure.ToString());
        Assert.Null(DockingTargeting.FindNearest(universe, chaser));
    }
}
