namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// A vessel integrated in the Moon's translating frame must subtract the Moon's full
/// inertial acceleration (Earth's pull plus Earth's own acceleration toward the Sun).
/// Leaving the solar term in produced a ~6 mm/s² spurious push that moved low lunar
/// orbit apsides by about 100 km per revolution.
/// </summary>
public sealed class LunarFrameAccelerationTests(ITestOutputHelper output)
{
    [Fact]
    public void LowLunarOrbitHoldsItsShapeOverOneRevolutionAtRealTime()
    {
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        var moon = universe.GetBody("moon")!;
        var probe = new Vessel("lunar-probe") { SASEnabled = false, ReferenceBodyId = moon.Id };
        var hull = new Part(new PartDefinition
        {
            Id = "lunar-probe-hull", CategoryStr = "command", MassDry = 1_000.0, LengthM = 2.0, DiameterM = 2.0,
        });
        probe.Parts.SetRoot(hull);
        probe.Parts.AddPart(hull);
        double radius = moon.Radius + 111_120.0;
        probe.Position = moon.Position + Vector3d.Right * radius;
        probe.Velocity = moon.Velocity + Vector3d.Up * System.Math.Sqrt(moon.GM / radius);
        universe.AddVessel(probe);
        universe.ActiveVessel = probe;

        double period = 2.0 * System.Math.PI * System.Math.Sqrt(radius * radius * radius / moon.GM);
        double maximumDeviation = 0.0;
        for (int step = 0; step < (int)(period / 0.02); step++)
        {
            universe.Tick(0.02);
            double altitude = (probe.Position - moon.Position).Magnitude - moon.Radius;
            maximumDeviation = System.Math.Max(maximumDeviation, System.Math.Abs(altitude - 111_120.0));
        }

        output.WriteLine($"max altitude deviation over one revolution = {maximumDeviation:F0} m");
        // Earth's tide moves a 111 km lunar orbit by well under a kilometre per revolution.
        Assert.True(maximumDeviation < 2_000.0, $"Lunar orbit drifted {maximumDeviation:F0} m.");
    }

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data", "bodies")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }
}
