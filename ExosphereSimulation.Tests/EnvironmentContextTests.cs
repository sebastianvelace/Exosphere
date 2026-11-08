namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Environment;
using Exosphere.Simulation.Math;
using Xunit;

/// <summary>
/// Environment plan E0.3: the universe reads body states, spin and gravity through providers.
/// The defaults must reproduce the original models exactly; these tests pin the formulas the
/// simulation used before the refactor and the consistency between the ephemeris's two paths.
/// </summary>
public sealed class EnvironmentContextTests
{
    [Fact]
    public void DefaultsAreTheOriginalModels()
    {
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        Assert.Equal("kepler", universe.Environment.Ephemeris.Id);
        foreach (var body in universe.Bodies)
        {
            Assert.Same(FixedAxisRotation.Instance, body.Orientation);
            Assert.Same(ZonalJ2Gravity.Instance, body.Gravity);
        }
        string description = universe.Environment.Describe(universe.Bodies);
        Assert.StartsWith("ephemeris=kepler; ", description);
        Assert.Contains("earth:orientation=fixed-axis,gravity=zonal-j2", description);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-962_582_400.0)]   // 1969-07-16, a dated-mission epoch
    [InlineData(812_345_678.9)]
    public void StateAtAgreesWithPropagateAllAcrossTheBodyChain(double time)
    {
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        var bodies = universe.Bodies.ToList();
        var ephemeris = universe.Environment.Ephemeris;
        var states = bodies.ToDictionary(b => b.Id,
            b => ephemeris.StateAt(b, time, id => bodies.FirstOrDefault(x => x.Id == id)));
        ephemeris.PropagateAll(bodies, time);
        Assert.True(bodies.Count >= 3);
        foreach (var body in bodies)
        {
            Assert.Equal(body.Position, states[body.Id].Position);
            Assert.Equal(body.Velocity, states[body.Id].Velocity);
        }
        var moon = bodies.Single(b => b.Id == "moon");
        var earth = bodies.Single(b => b.Id == "earth");
        Assert.InRange((moon.Position - earth.Position).Magnitude, 3.5e8, 4.1e8);
    }

    [Fact]
    public void FixedAxisRotationIsTheOriginalSpinModel()
    {
        var earth = CelestialBody.LoadFromJson(Path.Combine(DataDirectory(), "bodies/earth.json"));
        double tilt = earth.AxialTilt * MathUtils.DEG_TO_RAD;
        var axis = new Vector3d(0.0, System.Math.Sin(tilt), System.Math.Cos(tilt)).Normalized;
        double omega = 2.0 * System.Math.PI / earth.RotationalPeriod;
        Assert.Equal(axis, earth.RotationAxis);
        Assert.Equal(omega, earth.AngularSpeed);
        Assert.Equal(omega * 12345.6, earth.Orientation.RotationAngleAt(earth, 12345.6));
        // A slow rotator below 1 s period magnitude is treated as non-rotating, as before.
        var still = new CelestialBody { Id = "still", RotationalPeriod = 0.5 };
        Assert.Equal(0.0, still.AngularSpeed);
    }

    [Fact]
    public void ZonalJ2GravityIsTheOriginalField()
    {
        var earth = CelestialBody.LoadFromJson(Path.Combine(DataDirectory(), "bodies/earth.json"));
        earth.Position = new Vector3d(1.0e11, 2.0e10, -3.0e6);
        Assert.True(earth.J2 > 0.0);
        var position = earth.Position + new Vector3d(4.2e6, -3.1e6, 3.9e6);

        // The pre-refactor expression, written out.
        var r = position - earth.Position;
        double d2 = r.MagnitudeSquared, d = System.Math.Sqrt(d2);
        var north = earth.RotationAxis;
        var seed = System.Math.Abs(north.Z) < 0.9 ? new Vector3d(0, 0, 1) : new Vector3d(1, 0, 0);
        var prime = seed.Cross(north).Normalized;
        var east = north.Cross(prime).Normalized;
        double x = r.Dot(prime), y = r.Dot(east), z = r.Dot(north);
        double mu = earth.GM / (d2 * d), k = earth.EquatorialRadius / d, j = 1.5 * earth.J2 * k * k;
        double zr2 = (z / d) * (z / d);
        var expected = prime * (-mu * x * (1.0 - j * (5.0 * zr2 - 1.0)))
            + east * (-mu * y * (1.0 - j * (5.0 * zr2 - 1.0)))
            + north * (-mu * z * (1.0 - j * (5.0 * zr2 - 3.0)));
        Assert.Equal(expected, earth.GetGravityAt(position));
        Assert.Equal(Vector3d.Zero, earth.GetGravityAt(earth.Position));
    }

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }
}
