namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Xunit;

public sealed class OrbitQualificationPolicyTests
{
    private const double EarthRadius = 6_371_000.0;
    private const double EarthGm = 3.986004418e14;
    private const double AtmosphereTop = 140_000.0;

    [Fact]
    public void HighAndFastAtApoapsisIsNotOrbitWhenPeriapsisIntersectsEarth()
    {
        double radius = EarthRadius + 151_000.0;
        const double speed = 7_500.0;
        var trajectory = OrbitalElements.FromStateVector(
            Vector3d.Right * radius,
            Vector3d.Up * speed,
            EarthGm,
            "earth",
            epoch: 0.0);

        Assert.True(radius - EarthRadius > 150_000.0);
        Assert.True(speed >= 7_500.0);
        Assert.False(OrbitQualificationPolicy.HasSafePeriapsis(
            trajectory,
            EarthRadius,
            AtmosphereTop));
    }

    [Fact]
    public void CircularOrbitAboveAtmosphereQualifies()
    {
        double radius = EarthRadius + 150_000.0;
        double circularSpeed = System.Math.Sqrt(EarthGm / radius);
        var trajectory = OrbitalElements.FromStateVector(
            Vector3d.Right * radius,
            Vector3d.Up * circularSpeed,
            EarthGm,
            "earth",
            epoch: 0.0);

        Assert.True(OrbitQualificationPolicy.HasSafePeriapsis(
            trajectory,
            EarthRadius,
            AtmosphereTop));
    }
}
