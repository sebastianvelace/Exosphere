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

    [Fact]
    public void SphericalSolarPathIsLongerAndMoreExtinctNearTheHorizon()
    {
        var optics = LoadBody("earth").Atmosphere!.Optics;
        var zenith = optics.OpticalDepthAlongRay(
            0.0, 1.0, 6_371_000.0, 140_000.0, sampleCount: 48);
        var lowSun = optics.OpticalDepthAlongRay(
            0.0, System.Math.Sin(5.0 * System.Math.PI / 180.0),
            6_371_000.0, 140_000.0, sampleCount: 48);

        Assert.True(lowSun.X > zenith.X * 5.0);
        Assert.True(lowSun.Z > zenith.Z * 5.0);
        Assert.True(lowSun.X < 80.0 && lowSun.Z < 80.0,
            $"spherical optical depth diverged: {lowSun}");
    }

    [Fact]
    public void SphericalSolarTransmissionUsesTheActualBodyRadius()
    {
        var optics = AtmosphereModel.Mars().Optics;
        var earthSized = optics.DirectSolarTransmittance(
            0.0, 0.05, 6_371_000.0, 100_000.0);
        var marsSized = optics.DirectSolarTransmittance(
            0.0, 0.05, 3_389_500.0, 100_000.0);

        Assert.True(marsSized.X > 0.0 && marsSized.Z > 0.0);
        Assert.True(marsSized.X > earthSized.X && marsSized.Z > earthSized.Z,
            $"smaller body should have a shorter near-horizon column: Earth={earthSized}, Mars={marsSized}");
    }

    [Fact]
    public void SphericalZenithColumnMatchesTheVerticalOpticalColumn()
    {
        var optics = LoadBody("earth").Atmosphere!.Optics;
        var analytic = optics.VerticalOpticalDepth(0.0);
        var spherical = optics.OpticalDepthAlongRay(
            0.0, 1.0, 6_371_000.0, 140_000.0, sampleCount: 128);

        // At zenith the spherical path is vertical.  The finite 140 km top omits only
        // an exponentially tiny Rayleigh/Mie tail, so both implementations should agree
        // to well below one percent in every band.
        Assert.InRange(spherical.X, analytic.X * 0.999, analytic.X * 1.001);
        Assert.InRange(spherical.Y, analytic.Y * 0.999, analytic.Y * 1.001);
        Assert.InRange(spherical.Z, analytic.Z * 0.999, analytic.Z * 1.001);
    }

    [Fact]
    public void SphericalOpticalDepthIsMonotonicAboveTheGeometricHorizon()
    {
        var optics = LoadBody("earth").Atmosphere!.Optics;
        double[] cosZenith = { 0.02, 0.10, 0.35, 0.70, 1.0 };
        var previous = optics.OpticalDepthAlongRay(
            0.0, cosZenith[0], 6_371_000.0, 140_000.0);

        foreach (double cosine in cosZenith.Skip(1))
        {
            var current = optics.OpticalDepthAlongRay(
                0.0, cosine, 6_371_000.0, 140_000.0);

            Assert.True(current.X <= previous.X && current.Y <= previous.Y
                && current.Z <= previous.Z,
                $"optical depth grew toward zenith: previous={previous}, current={current}");
            Assert.True(double.IsFinite(current.X) && double.IsFinite(current.Y)
                && double.IsFinite(current.Z));
            Assert.True(current.X >= 0.0 && current.Y >= 0.0 && current.Z >= 0.0);
            previous = current;
        }
    }

    [Fact]
    public void SphericalTransmissionConvergesWhenSampleResolutionDoubles()
    {
        var optics = LoadBody("earth").Atmosphere!.Optics;
        var coarse = optics.DirectSolarTransmittance(
            10_000.0, System.Math.Sin(2.0 * System.Math.PI / 180.0),
            6_371_000.0, 140_000.0, sampleCount: 32);
        var fine = optics.DirectSolarTransmittance(
            10_000.0, System.Math.Sin(2.0 * System.Math.PI / 180.0),
            6_371_000.0, 140_000.0, sampleCount: 128);

        Assert.InRange(System.Math.Abs(coarse.X - fine.X), 0.0, 0.002);
        Assert.InRange(System.Math.Abs(coarse.Y - fine.Y), 0.0, 0.002);
        Assert.InRange(System.Math.Abs(coarse.Z - fine.Z), 0.0, 0.002);
    }

    [Fact]
    public void NonFiniteSolarInputsNeverProduceUnattenuatedLight()
    {
        var optics = LoadBody("earth").Atmosphere!.Optics;

        Assert.Equal(Vector3d.Zero,
            optics.DirectSolarTransmittance(double.NaN, 1.0));
        Assert.Equal(Vector3d.Zero,
            optics.DirectSolarTransmittance(0.0, double.NaN));
        Assert.Equal(Vector3d.Zero,
            optics.DirectSolarTransmittance(double.PositiveInfinity, 1.0));
    }

    [Fact]
    public void EarthAirglowIsAThinUpperAtmosphereEmissionLayer()
    {
        var optics = LoadBody("earth").Atmosphere!.Optics;

        Assert.True(optics.AirglowEmission.Y > optics.AirglowEmission.X);
        Assert.True(optics.AirglowEmission.Y > optics.AirglowEmission.Z);
        Assert.True(optics.AirglowEmission.X > optics.AirglowEmission.Z);
        Assert.Equal(1.0, optics.AirglowDensity(optics.AirglowCenterAltitude), 12);
        Assert.True(optics.AirglowDensity(80_000.0) < 0.03);
        Assert.True(optics.AirglowDensity(120_000.0) < 0.001);
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
        Assert.Equal(preset.AirglowEmission, json.AirglowEmission);
        Assert.Equal(preset.AirglowCenterAltitude, json.AirglowCenterAltitude);
        Assert.Equal(preset.AirglowScaleHeight, json.AirglowScaleHeight);
        Assert.Equal(preset.RayleighScaleHeight, json.RayleighScaleHeight);
        Assert.Equal(preset.MieScaleHeight, json.MieScaleHeight);
        Assert.Equal(preset.MieAnisotropy, json.MieAnisotropy);
        Assert.Equal(preset.SunIlluminanceScale, json.SunIlluminanceScale);
        Assert.Equal(preset.SurfaceRefractivity, json.SurfaceRefractivity);
        Assert.Equal(preset.RefractiveScaleHeight, json.RefractiveScaleHeight);
        Assert.Equal(preset.LowOrderDiffuseStrength, json.LowOrderDiffuseStrength);
        Assert.Equal(preset.CloudBaseAltitude, json.CloudBaseAltitude);
        Assert.Equal(preset.CloudTopAltitude, json.CloudTopAltitude);
        Assert.Equal(preset.CloudExtinction, json.CloudExtinction);
        Assert.Equal(preset.CloudCoverage, json.CloudCoverage);
        Assert.Equal(preset.CloudWindRadiansPerSecond, json.CloudWindRadiansPerSecond);
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

    [Fact]
    public void EarthCloudLayerHasSoftFiniteVerticalSupport()
    {
        var optics = LoadBody("earth").Atmosphere!.Optics;

        Assert.True(optics.HasCloudLayer);
        Assert.Equal(0.0, optics.CloudVerticalDensity(optics.CloudBaseAltitude));
        Assert.Equal(0.0, optics.CloudVerticalDensity(optics.CloudTopAltitude));
        Assert.True(optics.CloudVerticalDensity(3_000.0) > 0.8);
        Assert.True(optics.CloudVerticalDensity(11_500.0) < 0.1);
        Assert.InRange(optics.CloudCoverage, 0.0, 1.0);
    }

    [Fact]
    public void MarsCloudLayerIsDisabledUntilAValidatedProfileExists()
    {
        var optics = LoadBody("mars").Atmosphere!.Optics;

        Assert.False(optics.HasCloudLayer);
        Assert.Equal(0.0, optics.CloudVerticalDensity(5_000.0));
    }

    [Fact]
    public void CloudWeatherAndAltitudeJointlyBoundLocalExtinction()
    {
        var optics = LoadBody("earth").Atmosphere!.Optics;
        double previous = 0.0;
        for (int i = 0; i <= 100; i++)
        {
            double density = optics.CloudWeatherDensity(i / 100.0);
            Assert.InRange(density, previous, 1.0);
            previous = density;
        }

        Assert.Equal(0.0, optics.CloudLocalExtinction(500.0, 1.0));
        Assert.Equal(0.0, optics.CloudLocalExtinction(3_000.0, 0.0));
        Assert.InRange(optics.CloudLocalExtinction(3_000.0, 1.0),
            0.0, optics.CloudExtinction);
    }

    [Fact]
    public void InvalidCloudParametersCannotEnableRendering()
    {
        var invalid = new AtmosphereOptics
        {
            CloudBaseAltitude = 1_000.0,
            CloudTopAltitude = double.NaN,
            CloudExtinction = 0.001,
            CloudCoverage = 2.0,
        };

        Assert.False(invalid.HasCloudLayer);
        Assert.Equal(0.0, invalid.CloudVerticalDensity(2_000.0));
        Assert.Equal(0.0, invalid.CloudWeatherDensity(0.5));
    }

    [Fact]
    public void TransmittanceLutMatchesTheSphericalOpticalOracleAtTexels()
    {
        var optics = LoadBody("earth").Atmosphere!.Optics;
        const double radius = 6_371_000.0;
        const double top = 140_000.0;
        var lut = AtmosphereTransmittanceLut.Build(
            optics, radius, top, width: 40, height: 24, sampleCount: 32);

        int x = 13;
        int y = 7;
        double altitude = top * AtmosphereTransmittanceLut.CoordinateValue(x, lut.Width);
        double solarSin = AtmosphereTransmittanceLut.CoordinateValue(y, lut.Height);
        var expected = optics.DirectSolarTransmittance(
            altitude, solarSin, radius, top, sampleCount: 32);

        Assert.Equal(expected, lut.GetTexel(x, y));
    }

    [Fact]
    public void TransmittanceLutRemainsBoundedAndMonotonicWithAltitude()
    {
        var optics = LoadBody("earth").Atmosphere!.Optics;
        var lut = AtmosphereTransmittanceLut.Build(
            optics, 6_371_000.0, 140_000.0, width: 48, height: 28, sampleCount: 32);

        var previous = lut.Sample(0.0, 0.08);
        for (int i = 1; i <= 20; i++)
        {
            var current = lut.Sample(i * 6_000.0, 0.08);
            Assert.InRange(current.X, 0.0, 1.0);
            Assert.InRange(current.Y, 0.0, 1.0);
            Assert.InRange(current.Z, 0.0, 1.0);
            Assert.True(current.X >= previous.X && current.Y >= previous.Y
                && current.Z >= previous.Z,
                $"transmittance decreased with altitude: previous={previous}, current={current}");
            previous = current;
        }
    }

    [Fact]
    public void TransmittanceLutResolvesTheGrazingSolarColumn()
    {
        var optics = LoadBody("earth").Atmosphere!.Optics;
        var lut = AtmosphereTransmittanceLut.Build(
            optics, 6_371_000.0, 140_000.0, width: 64, height: 40, sampleCount: 48);

        var horizon = lut.Sample(0.0, 0.01);
        var zenith = lut.Sample(0.0, 1.0);
        Assert.True(horizon.X < zenith.X && horizon.Y < zenith.Y && horizon.Z < zenith.Z);
        Assert.True(horizon.X / Math.Max(horizon.Z, 1e-12)
            > zenith.X / Math.Max(zenith.Z, 1e-12));
        Assert.Equal(Vector3d.Zero, lut.Sample(0.0, -0.01));
    }

    [Fact]
    public void TransmittanceLutPreservesPlanetRadiusDependence()
    {
        var optics = AtmosphereModel.Mars().Optics;
        var earthSized = AtmosphereTransmittanceLut.Build(
            optics, 6_371_000.0, 100_000.0, width: 32, height: 20, sampleCount: 32);
        var marsSized = AtmosphereTransmittanceLut.Build(
            optics, 3_389_500.0, 100_000.0, width: 32, height: 20, sampleCount: 32);

        var earthNearHorizon = earthSized.Sample(0.0, 0.04);
        var marsNearHorizon = marsSized.Sample(0.0, 0.04);
        Assert.True(marsNearHorizon.X > earthNearHorizon.X
            && marsNearHorizon.Z > earthNearHorizon.Z,
            $"LUT lost body curvature: Earth={earthNearHorizon}, Mars={marsNearHorizon}");
    }

    [Fact]
    public void HorizonRefractionIsFiniteAndFallsAwayWithAltitude()
    {
        var optics = LoadBody("earth").Atmosphere!.Optics;
        double surface = optics.HorizonRefractionRadians(0.0, 6_371_000.0);
        double high = optics.HorizonRefractionRadians(80_000.0, 6_371_000.0);

        Assert.InRange(surface, 0.008, 0.012);
        Assert.True(high > 0.0 && high < surface);
        Assert.Equal(0.0, optics.HorizonRefractionRadians(double.NaN, 6_371_000.0));
        Assert.Equal(0.0, optics.HorizonRefractionRadians(0.0, double.NaN));
    }

    [Fact]
    public void MultipleScatteringLutTransportsBoundedRadianceThroughTheColumn()
    {
        var optics = LoadBody("earth").Atmosphere!.Optics;
        var lut = AtmosphereMultipleScatteringLut.Build(
            optics, 6_371_000.0, 140_000.0,
            width: 24, height: 16, integrationSteps: 16, solarSampleCount: 16);

        var ground = lut.Sample(0.0, 1.0);
        var high = lut.Sample(100_000.0, 1.0);
        Assert.True(ground.X > 0.0 && ground.Y > 0.0 && ground.Z > 0.0);
        Assert.True(high.X >= 0.0 && high.Y >= 0.0 && high.Z >= 0.0);
        Assert.True(ground.X >= high.X && ground.Y >= high.Y && ground.Z >= high.Z,
            $"global second-order radiance grew above the column: ground={ground}, high={high}");
        Assert.Equal(Vector3d.Zero, lut.Sample(0.0, -0.01));
    }

    [Fact]
    public void MultipleScatteringLutRespondsToPlanetCurvature()
    {
        var optics = AtmosphereModel.Mars().Optics;
        var earthSized = AtmosphereMultipleScatteringLut.Build(
            optics, 6_371_000.0, 100_000.0,
            width: 16, height: 12, integrationSteps: 12, solarSampleCount: 16);
        var marsSized = AtmosphereMultipleScatteringLut.Build(
            optics, 3_389_500.0, 100_000.0,
            width: 16, height: 12, integrationSteps: 12, solarSampleCount: 16);

        var earthHorizon = earthSized.Sample(0.0, 0.05);
        var marsHorizon = marsSized.Sample(0.0, 0.05);
        Assert.True(earthHorizon.X > marsHorizon.X && earthHorizon.Z > marsHorizon.Z,
            $"order-two LUT lost curvature: Earth={earthHorizon}, Mars={marsHorizon}");
    }

    [Fact]
    public void RefractedOpticalDepthDiffersFromTheStraightSphericalRayNearTheHorizon()
    {
        var optics = LoadBody("earth").Atmosphere!.Optics;
        var straight = optics.OpticalDepthAlongRay(
            0.0, 0.05, 6_371_000.0, 140_000.0, sampleCount: 96);
        var refracted = optics.OpticalDepthAlongRefractedRay(
            0.0, 0.05, 6_371_000.0, 140_000.0, sampleCount: 96);

        Assert.True(double.IsFinite(refracted.X) && double.IsFinite(refracted.Y)
            && double.IsFinite(refracted.Z));
        Assert.True(refracted.X >= 0.0 && refracted.Y >= 0.0 && refracted.Z >= 0.0);
        Assert.True(Math.Abs(refracted.X - straight.X) > 1e-8
            || Math.Abs(refracted.Z - straight.Z) > 1e-8,
            $"refractive path collapsed to the straight ray: straight={straight}, refracted={refracted}");
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
