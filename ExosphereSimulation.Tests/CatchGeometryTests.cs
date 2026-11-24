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
    public void CatchPinsStandProudOfTheLocalHullByAboutAFoot(string file)
    {
        var part = PartDefinition.LoadFromJson(Path.Combine(RepoRoot(), "data", "parts", file));

        Assert.True(System.Math.Abs(part.CatchPinOffsetYM) <= 0.5 * part.LengthM);
        // Measured against the hull radius at the pin station, so a fitting may sit near the
        // nosecone base, but one deep in the ogive (where the hull narrows) fails here.
        double protrusion = part.CatchPinLateralOffsetM + part.CatchPinRadiusM
            - HullRadius(part, part.CatchPinOffsetYM);
        Assert.InRange(protrusion, 0.05, 1.0);
    }

    /// <summary>Tangent-ogive hull radius at station <paramref name="y"/> from the part centre.</summary>
    private static double HullRadius(PartDefinition part, double y)
    {
        double radius = 0.5 * part.DiameterM, nose = part.NoseLengthM;
        double fromTip = 0.5 * part.LengthM - y;
        if (nose <= 0.0 || fromTip >= nose) return radius;
        double rho = (radius * radius + nose * nose) / (2.0 * radius);
        return System.Math.Sqrt(rho * rho - (nose - fromTip) * (nose - fromTip)) + radius - rho;
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
