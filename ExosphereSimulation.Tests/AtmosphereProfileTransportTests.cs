namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Xunit;

public sealed class AtmosphereProfileTransportTests
{
    [Fact]
    public void ProfileVerticalColumnIsFiniteAndDecreasesWithAltitude()
    {
        var profile = new AtmosphereDensityProfile(BuildLapseAtmosphere());

        var sea = profile.VerticalOpticalDepth(0.0, sampleCount: 32);
        var high = profile.VerticalOpticalDepth(30_000.0, sampleCount: 32);

        Assert.True(double.IsFinite(sea.X) && double.IsFinite(sea.Y)
            && double.IsFinite(sea.Z));
        Assert.True(sea.X > high.X && sea.Y > high.Y && sea.Z >= high.Z);
        Assert.True(sea.X > 0.0);
    }

    [Fact]
    public void ProfileTransmittanceLutMatchesTheProfileRayOracle()
    {
        var profile = new AtmosphereDensityProfile(BuildLapseAtmosphere());
        const double radius = 6_371_000.0;
        const double top = 100_000.0;

        var lut = AtmosphereTransmittanceLut.Build(
            profile, radius, top, width: 12, height: 10, sampleCount: 32);
        var expected = profile.Optics.DirectSolarTransmittance(
            profile, 0.0, 1.0, radius, top, sampleCount: 32);
        var actual = lut.GetTexel(0, lut.Height - 1);

        Assert.Equal(expected.X, actual.X, 12);
        Assert.Equal(expected.Y, actual.Y, 12);
        Assert.Equal(expected.Z, actual.Z, 12);
    }

    [Fact]
    public void ProfileSolarTransportRetainsARefractedNearHorizonBeam()
    {
        var atmosphere = AtmosphereModel.Earth();
        var profile = new AtmosphereDensityProfile(atmosphere);

        var belowHorizon = atmosphere.Optics.DirectSolarTransmittance(
            profile, 0.0, -0.005, 6_371_000.0, 140_000.0, sampleCount: 32);

        Assert.True(belowHorizon.X > 0.0 && belowHorizon.Y > 0.0
            && belowHorizon.Z > 0.0,
            $"profile refractive lift clipped the near-horizon beam: {belowHorizon}");
    }

    [Fact]
    public void ProfileDirectBeamMatchesItsProfileRefractedPathOracle()
    {
        var atmosphere = AtmosphereModel.Earth();
        var profile = new AtmosphereDensityProfile(atmosphere);
        const double radius = 6_371_000.0;
        const double top = 140_000.0;
        const double geometricSin = -0.005;

        Assert.True(profile.Optics.TrySolveRefractedSolarElevation(
            profile, 0.0, geometricSin, radius, top,
            out double apparentSin, sampleCount: 48));

        var tau = profile.Optics.OpticalDepthAlongRefractedRay(
            profile, 0.0, apparentSin, radius, top, sampleCount: 48);
        var expected = new Vector3d(
            System.Math.Exp(-tau.X),
            System.Math.Exp(-tau.Y),
            System.Math.Exp(-tau.Z));
        var actual = profile.Optics.DirectSolarTransmittance(
            profile, 0.0, geometricSin, radius, top, sampleCount: 48);

        Assert.Equal(expected.X, actual.X, 12);
        Assert.Equal(expected.Y, actual.Y, 12);
        Assert.Equal(expected.Z, actual.Z, 12);
    }

    [Fact]
    public void ProfileRefractedPathRespondsToThermodynamicLapse()
    {
        var atmosphere = AtmosphereModel.Earth();
        var profile = new AtmosphereDensityProfile(atmosphere);
        const double radius = 6_371_000.0;
        const double top = 140_000.0;

        var legacy = atmosphere.Optics.OpticalDepthAlongRefractedRay(
            12_000.0, 0.05, radius, top, sampleCount: 96);
        var thermodynamic = atmosphere.Optics.OpticalDepthAlongRefractedRay(
            profile, 12_000.0, 0.05, radius, top, sampleCount: 96);

        Assert.True(double.IsFinite(thermodynamic.X)
            && double.IsFinite(thermodynamic.Y)
            && double.IsFinite(thermodynamic.Z));
        Assert.True(System.Math.Abs(legacy.X - thermodynamic.X) > 1e-10
            || System.Math.Abs(legacy.Y - thermodynamic.Y) > 1e-10
            || System.Math.Abs(legacy.Z - thermodynamic.Z) > 1e-10,
            $"profile ray unexpectedly collapsed to legacy envelope: legacy={legacy}, profile={thermodynamic}");
    }

    [Fact]
    public void ProfileMultipleScatteringSeedDiffersFromLegacyEnvelope()
    {
        var atmosphere = BuildLapseAtmosphere();
        var profile = new AtmosphereDensityProfile(atmosphere);
        const double radius = 6_371_000.0;
        const double top = 100_000.0;

        var legacy = AtmosphereMultipleScatteringLut.Build(
            atmosphere.Optics, radius, top,
            width: 8, height: 6, integrationSteps: 12, solarSampleCount: 12);
        var thermodynamic = AtmosphereMultipleScatteringLut.Build(
            profile, radius, top,
            width: 8, height: 6, integrationSteps: 12, solarSampleCount: 12);

        var legacySea = legacy.GetTexel(0, legacy.Height - 1);
        var profileSea = thermodynamic.GetTexel(0, thermodynamic.Height - 1);
        Assert.True(System.Math.Abs(legacySea.X - profileSea.X) > 1e-12
            || System.Math.Abs(legacySea.Y - profileSea.Y) > 1e-12
            || System.Math.Abs(legacySea.Z - profileSea.Z) > 1e-12,
            $"profile and legacy scattering unexpectedly match: {legacySea} / {profileSea}");
    }

    private static AtmosphereModel BuildLapseAtmosphere() => new()
    {
        MaxAltitude = 100_000.0,
        SeaLevelDensity = 1.225,
        SeaLevelPressure = 101_325.0,
        SeaLevelTemperature = 288.15,
        ScaleHeight = 8_500.0,
        Optics = new AtmosphereOptics
        {
            RayleighScattering = new Vector3d(1.0e-5, 2.0e-5, 3.0e-5),
            MieScattering = new Vector3d(1.0e-6, 1.0e-6, 1.0e-6),
            MieAbsorption = new Vector3d(0.5e-6, 0.5e-6, 0.5e-6),
            OzoneAbsorption = new Vector3d(0.2e-6, 0.2e-6, 0.2e-6),
            SurfaceRefractivity = 0.0,
        },
        Layers = new List<AtmosphereLayer>
        {
            new(0.0, 100_000.0, 288.15, 0.0015),
        },
    };
}
