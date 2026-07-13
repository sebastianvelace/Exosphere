namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Xunit;

public sealed class LaunchSupportCampusSpecTests
{
    [Fact]
    public void CampusBuildingsRespectFaaLowRiseEnvelopeAndDoNotOverlap()
    {
        var buildings = LaunchSupportCampusSpec.StarbasePostDeluge;

        Assert.NotEmpty(buildings);
        Assert.All(buildings, b => Assert.True(b.Height < LaunchSupportCampusSpec.FaaLowRiseLimitM));
        Assert.Empty(LaunchSupportCampusSpec.Validate(buildings));
    }

    [Fact]
    public void CampusContainsEveryOperationalCategoryNeededNearThePad()
    {
        var uses = LaunchSupportCampusSpec.StarbasePostDeluge.Select(b => b.Use).ToArray();

        Assert.Contains(uses, u => u.Contains("Maintenance"));
        Assert.Contains(uses, u => u.Contains("Emergency"));
        Assert.Contains(uses, u => u.Contains("power"));
        Assert.Contains(uses, u => u.Contains("Water"));
        Assert.Contains(uses, u => u.Contains("LNG"));
        Assert.Contains(uses, u => u.Contains("liquefier"));
    }

    [Fact]
    public void ValidationRejectsHeightOverlapAndPadIntrusion()
    {
        CampusBuildingSpec[] invalid =
        [
            new("too_tall", "Test", 10, 0, 10, 10, LaunchSupportCampusSpec.FaaLowRiseLimitM),
            new("a", "Test", 50, 50, 20, 20, 5),
            new("b", "Test", 55, 50, 20, 20, 5),
        ];
        var errors = LaunchSupportCampusSpec.Validate(invalid);

        Assert.Contains(errors, e => e.StartsWith("FAA_HEIGHT_LIMIT"));
        Assert.Contains(errors, e => e.StartsWith("PAD_HAZARD_CLEARANCE"));
        Assert.Contains(errors, e => e.StartsWith("BUILDING_OVERLAP"));
    }
}
