namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Xunit;

/// <summary>
/// SAS is a rate controller acting through the vehicle's actuators. It must not remove
/// angular momentum faster than the available attitude authority allows.
/// </summary>
public sealed class StabilityAugmentationAuthorityTests
{
    [Fact]
    public void SasCannotStopAFastTumbleFasterThanTheReactionControlFloor()
    {
        var (universe, vessel) = TumblingVesselInVacuum(0.3);
        for (int step = 0; step < 50; step++)
            universe.Tick(0.02);

        // 0.01 rad/s^2 floor for one second: at most 0.01 rad/s removed.
        Assert.InRange(vessel.AngularVelocity.Magnitude, 0.2895, 0.2905);
    }

    [Fact]
    public void SasStillSettlesASmallResidualRate()
    {
        var (universe, vessel) = TumblingVesselInVacuum(0.002);
        for (int step = 0; step < 50 * 3; step++)
            universe.Tick(0.02);

        Assert.True(vessel.AngularVelocity.Magnitude < 1e-5,
            $"Residual rate {vessel.AngularVelocity.Magnitude:E3} rad/s was not damped.");
    }

    [Fact]
    public void WithoutSasAnUnpoweredTumbleConservesItsRate()
    {
        var (universe, vessel) = TumblingVesselInVacuum(0.3);
        vessel.SASEnabled = false;
        for (int step = 0; step < 50; step++)
            universe.Tick(0.02);

        Assert.Equal(0.3, vessel.AngularVelocity.Magnitude, 9);
    }

    private static (Universe Universe, Vessel Vessel) TumblingVesselInVacuum(double rate)
    {
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        var earth = universe.GetBody("earth")!;
        var vessel = new Vessel("sas-tumbler") { SASEnabled = true, ReferenceBodyId = earth.Id };
        var command = new Part(new PartDefinition
        {
            Id = "sas-test-command",
            CategoryStr = "command",
            MassDry = 100_000.0,
            LengthM = 50.0,
            DiameterM = 9.0,
        });
        vessel.Parts.SetRoot(command);
        vessel.Parts.AddPart(command);
        double radius = earth.Radius + 2_000_000.0;
        vessel.Position = earth.Position + Vector3d.Right * radius;
        vessel.Velocity = earth.Velocity + new Vector3d(0.0, 0.0, -1.0) * System.Math.Sqrt(earth.GM / radius);
        vessel.AngularVelocity = new Vector3d(rate, 0.0, 0.0);
        universe.AddVessel(vessel);
        universe.ActiveVessel = vessel;
        return (universe, vessel);
    }

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data", "bodies")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }
}
