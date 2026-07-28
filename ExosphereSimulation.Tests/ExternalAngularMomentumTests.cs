namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

public sealed class ExternalAngularMomentumTests
{
    [Fact]
    public void ExistingFailureRateIsNotErasedByControlEnvelope()
    {
        var vessel = new Vessel();
        vessel.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = "failure-test-capsule",
            Name = "Failure test capsule",
            CategoryStr = "command",
            MassDry = 1_000.0,
            LengthM = 3.0,
            DiameterM = 2.0,
        }));
        vessel.SASEnabled = false;
        vessel.AngularVelocity = new Vector3d(
            0.0,
            Gemini8RateRadPerS,
            0.0);
        var body = new CelestialBody
        {
            Id = "vacuum-body",
            Name = "Vacuum body",
            Radius = 1_000.0,
            Mass = 1.0e10,
        };
        vessel.Position = new Vector3d(10_000.0, 0.0, 0.0);

        vessel.Tick(0.02, body);

        Assert.Equal(Gemini8RateRadPerS, vessel.AngularVelocity.Magnitude, 8);
    }

    private const double Gemini8RateRadPerS =
        296.0 * System.Math.PI / 180.0;
}
