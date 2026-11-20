namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Parts;
using Xunit;

public sealed class CatchGeometryTests
{
    [Theory]
    [InlineData("starship_v3_command_flight12.json")]
    [InlineData("starship_command.json")]
    [InlineData("super_heavy_v3_booster_flight12.json")]
    [InlineData("super_heavy_booster.json")]
    public void CatchPinsStandOnTheCylindricalHullByAboutAFoot(string file)
    {
        var part = PartDefinition.LoadFromJson(Path.Combine(RepoRoot(), "data", "parts", file));
        double ogiveStart = 0.5 * part.LengthM - part.NoseLengthM;

        Assert.True(part.CatchPinOffsetYM <= ogiveStart,
            $"pin station {part.CatchPinOffsetYM} m lies in the nose ogive starting at {ogiveStart} m");
        Assert.True(System.Math.Abs(part.CatchPinOffsetYM) <= 0.5 * part.LengthM);
        double protrusion = part.CatchPinLateralOffsetM + part.CatchPinRadiusM - 0.5 * part.DiameterM;
        Assert.InRange(protrusion, 0.05, 1.0);
    }

    [Fact]
    public void ShipFittingsSitDirectlyUnderTheForwardFlaps()
    {
        var part = PartDefinition.LoadFromJson(Path.Combine(RepoRoot(), "data", "parts",
            "starship_v3_command_flight12.json"));
        double flapLowerEnd = part.BodyFlaps.Min(f => f.HingeYM - 0.5 * f.LengthM);

        Assert.InRange(flapLowerEnd - (part.CatchPinOffsetYM + part.CatchPinRadiusM), 0.0, 0.5);
    }

    [Theory]
    [InlineData(37.1)]
    [InlineData(65.6)]
    public void CarriageRidesUpSoTheCaughtTailClearsTheMount(double pinContactHeight)
    {
        var spec = LaunchComplexSpec.StarbasePostDeluge;

        double carriage = spec.GetCatchCarriageHeightM(pinContactHeight);

        Assert.Equal(spec.VehicleInterfaceElevation + spec.CatchTailClearanceM,
            carriage - pinContactHeight, 9);
        Assert.True(carriage > spec.ArmHeightM);
        Assert.True(carriage <= spec.OlitHeight - spec.ArmTopMarginM);
        Assert.Empty(spec.Validate());
    }

    [Fact]
    public void CarriageNeverDropsBelowItsIdleHeightOrLeavesTheTower()
    {
        var spec = LaunchComplexSpec.StarbasePostDeluge;

        Assert.Equal(spec.ArmHeightM, spec.GetCatchCarriageHeightM(0.0));
        Assert.Equal(spec.OlitHeight - spec.ArmTopMarginM, spec.GetCatchCarriageHeightM(500.0));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ExosphereSimulation.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
