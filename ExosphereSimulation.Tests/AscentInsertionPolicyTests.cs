namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Xunit;

public sealed class AscentInsertionPolicyTests
{
    private const double EarthGm = 3.986004418e14;
    private const double LeoSemiMajorAxis = 6_521_000.0;

    [Fact]
    public void CoastContinuesWhenApoapsisIsOutsideGuidanceLeadTime()
    {
        var trajectory = EllipseAtMeanAnomaly(System.Math.PI - 0.25);

        Assert.False(AscentInsertionPolicy.ShouldBeginInsertion(
            trajectory,
            currentTime: 0.0,
            EarthGm,
            verticalSpeedMps: 120.0));
    }

    [Fact]
    public void InsertionBeginsBeforeApoapsisToCoverGuidanceAndEngineResponse()
    {
        double meanMotion = System.Math.Sqrt(
            EarthGm / System.Math.Pow(LeoSemiMajorAxis, 3.0));
        double anomalyLead = meanMotion
            * (AscentInsertionPolicy.GuidanceLeadTimeSeconds - 1.0);
        var trajectory = EllipseAtMeanAnomaly(System.Math.PI - anomalyLead);

        Assert.True(AscentInsertionPolicy.ShouldBeginInsertion(
            trajectory,
            currentTime: 0.0,
            EarthGm,
            verticalSpeedMps: 80.0));
    }

    [Fact]
    public void DescentAlwaysCommandsInsertionInsteadOfReturningToCoast()
    {
        var trajectory = EllipseAtMeanAnomaly(System.Math.PI + 0.1);

        Assert.True(AscentInsertionPolicy.ShouldBeginInsertion(
            trajectory,
            currentTime: 0.0,
            EarthGm,
            verticalSpeedMps: -2.0));
    }

    private static OrbitalElements EllipseAtMeanAnomaly(double meanAnomaly) =>
        new()
        {
            SemiMajorAxis = LeoSemiMajorAxis,
            Eccentricity = 0.02,
            MeanAnomalyAtEpoch = meanAnomaly,
            Epoch = 0.0,
            ReferenceBodyId = "earth",
            SpecificAngularMomentum = 1.0,
        };
}
