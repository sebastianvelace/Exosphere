namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Xunit;

/// <summary>
/// Mach number must use each atmosphere's own gas: CO2 on Mars carries sound about 22%
/// slower than Earth air at the same temperature, which moves the transonic drag rise.
/// </summary>
public sealed class PlanetaryGasMachTests
{
    [Fact]
    public void EarthSpeedOfSoundIsUnchanged()
    {
        var earth = Load("earth");
        double t = earth.Atmosphere!.GetTemperature(0.0);
        double sound = 300.0 / AerodynamicsModel.ComputeMach(300.0, t, earth.Atmosphere.SoundSpeedConstant);
        Assert.InRange(sound, 340.1, 340.5);
    }

    [Fact]
    public void MarsSpeedOfSoundFollowsCarbonDioxide()
    {
        var mars = Load("mars");
        double t = mars.Atmosphere!.GetTemperature(0.0);
        double sound = 300.0 / AerodynamicsModel.ComputeMach(300.0, t, mars.Atmosphere.SoundSpeedConstant);
        Assert.InRange(sound, 222.0, 231.0);
    }

    [Fact]
    public void MarsDragAtTrueMachOnePointOneIncludesTheTransonicRise()
    {
        var mars = Load("mars");
        var atmosphere = mars.Atmosphere!;
        var vessel = new Vessel("mars-drag") { ReferenceBodyId = mars.Id };
        var hull = new Part(new PartDefinition
        {
            Id = "mars-drag-hull", CategoryStr = "command", MassDry = 1_000.0, LengthM = 4.0, DiameterM = 4.0,
        });
        vessel.Parts.SetRoot(hull);
        vessel.Parts.AddPart(hull);
        var position = mars.GetSurfacePosition(0.0, 0.0, 1_000.0);
        double t = atmosphere.GetTemperature(mars.GetAltitude(position));
        double speed = 1.1 * System.Math.Sqrt(atmosphere.SoundSpeedConstant * t);
        var flow = vessel.Orientation.Rotate(Vector3d.Up);
        var velocity = mars.Velocity + mars.GetSurfaceVelocity(position) - flow * speed;

        double drag = vessel.ComputeDragAt(position, velocity, mars).Magnitude;
        double density = mars.GetAtmosphericDensity(position);
        double subsonic = 0.5 * density * speed * speed * 0.6 * System.Math.PI * 4.0;
        Assert.InRange(drag / subsonic, 1.95, 2.05);
    }

    private static CelestialBody Load(string id)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data", "bodies")))
            directory = directory.Parent;
        return CelestialBody.LoadFromJson(Path.Combine(directory!.FullName, "data", "bodies", id + ".json"));
    }
}
