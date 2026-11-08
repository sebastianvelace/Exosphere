namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Xunit;

/// <summary>
/// Environment plan E0.1: one point defines the air's velocity. Until wind is modelled the
/// atmosphere co-rotates rigidly, so the air-relative velocity must equal the surface-relative
/// one bit for bit (the refactor changes no trajectory); wind (E4) will split them.
/// </summary>
public sealed class AirRelativeVelocityTests
{
    [Fact]
    public void CoRotatingAirMatchesTheSurfaceBitForBit()
    {
        var earth = CelestialBody.LoadFromJson(Path.Combine(DataDirectory(), "bodies/earth.json"));
        earth.Position = new Vector3d(1.5e11, -2.0e7, 3.0e9);
        earth.Velocity = new Vector3d(-2.9e4, 1.2e3, 7.0);
        Assert.True(earth.AngularSpeed > 0.0);

        var position = earth.Position + new Vector3d(4.1e6, 3.3e6, -2.9e6);
        var velocity = earth.Velocity + new Vector3d(5100.0, -2200.0, 1300.0);
        var air = earth.GetAirRelativeVelocity(position, velocity);
        var surface = velocity - earth.Velocity - earth.GetSurfaceVelocity(position);
        Assert.Equal(earth.GetSurfaceVelocity(position), earth.GetAirVelocity(position));
        Assert.Equal(surface.X, air.X);
        Assert.Equal(surface.Y, air.Y);
        Assert.Equal(surface.Z, air.Z);

        // The co-rotating air moves at ω × r: eastward ~465 m/s at the equator scale.
        double airSpeed = earth.GetAirVelocity(position).Magnitude;
        double expected = earth.AngularSpeed
            * earth.RotationAxis.Cross(position - earth.Position).Magnitude;
        Assert.Equal(expected, airSpeed, 9);
    }

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }
}
