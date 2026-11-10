namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Navigation;
using Xunit;

/// <summary>
/// The departure hyperbola through a position with a given outgoing asymptote: a known
/// hyperbola's own state and asymptote must give back its own velocity.
/// </summary>
public sealed class HyperbolicDepartureTests
{
    private const double MoonGm = 4.9028e12;

    [Theory]
    [InlineData(1.5, -0.6, 0.3)]
    [InlineData(1.2, 0.0, 0.0)]
    [InlineData(3.0, 0.9, 1.1)]
    [InlineData(1.05, 1.2, 2.0)]
    public void KnownHyperbolaReturnsItsOwnVelocity(double eccentricity, double trueAnomaly, double inclination)
    {
        const double periapsis = 1_850_000.0;
        double p = periapsis * (1.0 + eccentricity);
        double factor = System.Math.Sqrt(MoonGm / p);
        double r = p / (1.0 + eccentricity * System.Math.Cos(trueAnomaly));
        // Perifocal frame tilted about x by the inclination.
        var x = new Vector3d(1, 0, 0);
        var y = new Vector3d(0, System.Math.Cos(inclination), System.Math.Sin(inclination));
        var position = (x * System.Math.Cos(trueAnomaly) + y * System.Math.Sin(trueAnomaly)) * r;
        var velocity = (x * -System.Math.Sin(trueAnomaly) + y * (eccentricity + System.Math.Cos(trueAnomaly))) * factor;
        double asymptoteAnomaly = System.Math.Acos(-1.0 / eccentricity);
        // Outgoing asymptote direction: the limit of the velocity direction as ν → ν∞.
        var asymptote = (x * -System.Math.Sin(asymptoteAnomaly)
            + y * (eccentricity + System.Math.Cos(asymptoteAnomaly))).Normalized;
        double vInfinity = System.Math.Sqrt(MoonGm * (eccentricity * eccentricity - 1.0) / p);

        var required = HyperbolicDeparture.RequiredVelocity(MoonGm, position, asymptote * vInfinity,
            position.Cross(velocity));

        Assert.NotNull(required);
        Assert.True((required!.Value - velocity).Magnitude < 1e-6 * velocity.Magnitude,
            $"required {required.Value} vs {velocity}");
    }

    [Fact]
    public void AnAsymptoteBehindTheBodyHasNoOutgoingBranch()
    {
        var position = new Vector3d(2_000_000.0, 0.0, 0.0);
        // Straight back along the radius toward the body: not reachable outbound.
        Assert.Null(HyperbolicDeparture.RequiredVelocity(MoonGm, position, new Vector3d(-900.0, 0.0, 0.0),
            new Vector3d(0, 0, 1)));
    }
}
