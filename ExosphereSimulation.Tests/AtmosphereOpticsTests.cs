namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Xunit;

public sealed class AtmosphereOpticsTests
{
    [Fact]
    public void EarthOpticalDepthScattersBlueMoreThanRed()
    {
        var optics = LoadBody("earth").Atmosphere!.Optics;
        var depth = optics.VerticalOpticalDepth(0.0);

        Assert.True(depth.Z > depth.Y);
        Assert.True(depth.Y > depth.X);
        Assert.InRange(depth.X, 0.04, 0.15);
        Assert.InRange(depth.Z, 0.20, 0.45);
    }

    [Fact]
    public void VerticalTransmittanceApproachesVacuumContinuouslyWithAltitude()
    {
        var optics = LoadBody("earth").Atmosphere!.Optics;
        var sea = optics.VerticalTransmittance(0.0);
        var tenKm = optics.VerticalTransmittance(10_000.0);
        var orbit = optics.VerticalTransmittance(150_000.0);

        Assert.True(tenKm.X > sea.X && tenKm.Y > sea.Y && tenKm.Z > sea.Z);
        Assert.True(orbit.X > 0.999 && orbit.Y > 0.999 && orbit.Z > 0.999);
    }

    [Fact]
    public void OzoneLayerPeaksInTheStratosphereAndVanishesOutsideIt()
    {
        var optics = LoadBody("earth").Atmosphere!.Optics;

        Assert.Equal(0.0, optics.OzoneDensity(0.0), 12);
        Assert.Equal(1.0, optics.OzoneDensity(25_000.0), 12);
        Assert.Equal(0.0, optics.OzoneDensity(50_000.0), 12);
    }

    [Fact]
    public void LowSunIsDimmerAndRedderThanZenithSun()
    {
        var optics = LoadBody("earth").Atmosphere!.Optics;
        var zenith = optics.DirectSolarTransmittance(0.0, 1.0);
        var low = optics.DirectSolarTransmittance(0.0, 0.05);

        Assert.True(low.X < zenith.X);
        Assert.True(low.Y < zenith.Y);
        Assert.True(low.Z < zenith.Z);
        Assert.True(low.X / low.Z > zenith.X / zenith.Z);
        Assert.Equal(Vector3d.Zero, optics.DirectSolarTransmittance(0.0, -0.01));
    }

    [Theory]
    [InlineData("earth")]
    [InlineData("mars")]
    [InlineData("venus")]
    public void TerrestrialAtmospheresLoadDataDrivenOpticalProfiles(string bodyId)
    {
        var body = LoadBody(bodyId);

        Assert.NotNull(body.Atmosphere);
        Assert.True(body.Atmosphere!.Optics.IsEnabled);
        Assert.True(body.Atmosphere.Optics.RayleighScaleHeight > 0.0);
        Assert.InRange(body.Atmosphere.Optics.MieAnisotropy, 0.0, 0.95);
    }

    [Theory]
    [InlineData("earth")]
    [InlineData("mars")]
    [InlineData("venus")]
    public void JsonAndPresetOpticsRemainIdentical(string bodyId)
    {
        var json = LoadBody(bodyId).Atmosphere!.Optics;
        var preset = bodyId switch
        {
            "earth" => AtmosphereModel.Earth().Optics,
            "mars" => AtmosphereModel.Mars().Optics,
            "venus" => AtmosphereModel.Venus().Optics,
            _ => throw new ArgumentOutOfRangeException(nameof(bodyId)),
        };

        Assert.Equal(preset.RayleighScattering, json.RayleighScattering);
        Assert.Equal(preset.MieScattering, json.MieScattering);
        Assert.Equal(preset.MieAbsorption, json.MieAbsorption);
        Assert.Equal(preset.OzoneAbsorption, json.OzoneAbsorption);
        Assert.Equal(preset.RayleighScaleHeight, json.RayleighScaleHeight);
        Assert.Equal(preset.MieScaleHeight, json.MieScaleHeight);
        Assert.Equal(preset.LowOrderDiffuseStrength, json.LowOrderDiffuseStrength);
    }

    [Fact]
    public void LowOrderDiffuseSourceIsBoundedAndRespectsPlanetShadow()
    {
        var optics = LoadBody("earth").Atmosphere!.Optics;
        var density = new Vector3d(1.0, 1.0, 0.0);
        var clear = optics.LowOrderDiffuseSource(density, new Vector3d(1.0, 1.0, 1.0));
        var attenuated = optics.LowOrderDiffuseSource(density, new Vector3d(0.2, 0.2, 0.2));
        var shadow = optics.LowOrderDiffuseSource(
            density, Vector3d.Zero, planetOccluded: true);

        Assert.Equal(Vector3d.Zero, clear);
        Assert.Equal(Vector3d.Zero, shadow);
        Assert.True(attenuated.X > 0.0 && attenuated.Y > 0.0 && attenuated.Z > 0.0);
        double bound = (optics.RayleighScattering.X + optics.MieScattering.X)
            * optics.LowOrderDiffuseStrength / (4.0 * System.Math.PI);
        Assert.InRange(attenuated.X, 0.0, bound);
    }

    private static CelestialBody LoadBody(string id) => CelestialBody.LoadFromJson(
        Path.Combine(FindRepoRoot().FullName, "data", "bodies", $"{id}.json"));

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "data"))
                && File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
                return directory;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }
}
