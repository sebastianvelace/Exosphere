namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Xunit;

public sealed class LaunchComplexSpecTests
{
    [Fact]
    public void StarbaseUsesOneDatumAndFaaVerticalEnvelope()
    {
        var spec = LaunchComplexSpec.StarbasePostDeluge;

        Assert.Equal(0.0, spec.GradeElevation);
        Assert.Equal(19.812, spec.VehicleInterfaceElevation, 6);
        Assert.Equal(146.304, spec.OlitHeight, 6);
        Assert.Equal(149.352, spec.OlitHeight + spec.LightningRodHeight, 6);
        Assert.True(spec.VehicleInterfaceElevation < spec.OlitHeight);
    }

    [Fact]
    public void OlmOpeningClearsNineMetreVehicleAndTowerStaysSeparated()
    {
        var spec = LaunchComplexSpec.StarbasePostDeluge;
        double towerDistance = System.Math.Sqrt(
            spec.OlitEast * spec.OlitEast + spec.OlitNorth * spec.OlitNorth);
        double towerBoundingRadius = spec.OlitWidth / System.Math.Sqrt(2.0);

        Assert.InRange(spec.OlmInnerClearRadius - 4.5, 0.20, 0.30);
        Assert.True(towerDistance - spec.OlmOuterRadius - towerBoundingRadius > 15.0);
    }

    [Fact]
    public void InvalidEngineeringProfilesAreRejected()
    {
        var invalidDatum = LaunchComplexSpec.StarbasePostDeluge with
        {
            VehicleInterfaceElevation = 0.0,
        };
        var invalidOpening = LaunchComplexSpec.StarbasePostDeluge with
        {
            OlmInnerClearRadius = 4.0,
        };
        var invalidNumber = LaunchComplexSpec.StarbasePostDeluge with
        {
            OlitHeight = double.NaN,
        };

        Assert.Contains("INVALID_INTERFACE_DATUM", invalidDatum.Validate());
        Assert.Contains("INVALID_OLM_CLEARANCE", invalidOpening.Validate());
        Assert.Contains("NON_FINITE_DIMENSION", invalidNumber.Validate());
    }
}
