namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Xunit;

public sealed class AtmosphereRaySamplingTests
{
    private const double EarthRadius = 6_371_000.0;
    private const double AtmosphereRadius = EarthRadius + 140_000.0;

    [Fact]
    public void SurfaceObserverLookingUpTraversesExactlyTheVerticalAtmosphere()
    {
        var segment = AtmosphereRaySampling.IntersectShell(
            Vector3d.Right * EarthRadius, Vector3d.Right, EarthRadius, AtmosphereRadius);

        Assert.NotNull(segment);
        Assert.Equal(0.0, segment.Value.StartDistance, 6);
        Assert.Equal(140_000.0, segment.Value.Length, 5);
        Assert.Equal(0.0, segment.Value.ClosestApproachDistance, 6);
        Assert.False(segment.Value.HitsSurface);
    }

    [Fact]
    public void NadirRayFromSpaceStopsAtTheOpaqueSurface()
    {
        const double cameraAboveAtmosphere = 100_000.0;
        var origin = Vector3d.Right * (AtmosphereRadius + cameraAboveAtmosphere);
        var segment = AtmosphereRaySampling.IntersectShell(
            origin, -Vector3d.Right, EarthRadius, AtmosphereRadius);

        Assert.NotNull(segment);
        Assert.Equal(cameraAboveAtmosphere, segment.Value.StartDistance, 5);
        Assert.Equal(cameraAboveAtmosphere + 140_000.0, segment.Value.EndDistance, 5);
        Assert.True(segment.Value.HitsSurface);
    }

    [Fact]
    public void ExactSurfaceTangentHasTheAnalyticLimbLengthWithoutFalseOcclusion()
    {
        var segment = AtmosphereRaySampling.IntersectShell(
            Vector3d.Right * EarthRadius, Vector3d.Up, EarthRadius, AtmosphereRadius);
        double expected = System.Math.Sqrt(
            AtmosphereRadius * AtmosphereRadius - EarthRadius * EarthRadius);

        Assert.NotNull(segment);
        Assert.Equal(expected, segment.Value.Length, 5);
        Assert.Equal(0.0, segment.Value.ClosestApproachDistance, 6);
        Assert.False(segment.Value.HitsSurface);
    }

    [Fact]
    public void RayThatMissesTheAtmosphereHasNoSegment()
    {
        var origin = Vector3d.Right * (AtmosphereRadius + 10_000.0);

        Assert.Null(AtmosphereRaySampling.IntersectShell(
            origin, Vector3d.Right, EarthRadius, AtmosphereRadius));
        Assert.Null(AtmosphereRaySampling.IntersectShell(
            origin, Vector3d.Up, EarthRadius, AtmosphereRadius));
    }

    [Fact]
    public void SurfaceObserverLookingIntoThePlanetSeesNoAtmosphericSegment()
    {
        Assert.Null(AtmosphereRaySampling.IntersectShell(
            Vector3d.Right * EarthRadius, -Vector3d.Right, EarthRadius, AtmosphereRadius));
    }

    [Fact]
    public void TangentBiasedSamplesAreOrderedBoundedAndConcentratedAtClosestApproach()
    {
        var segment = AtmosphereRaySampling.IntersectShell(
            Vector3d.Right * EarthRadius, Vector3d.Up, EarthRadius, AtmosphereRadius)!.Value;
        var samples = Enumerable.Range(0, 16)
            .Select(i => AtmosphereRaySampling.TangentBiasedSampleDistance(segment, i, 16))
            .ToArray();

        Assert.All(samples, sample => Assert.InRange(sample,
            segment.StartDistance, segment.EndDistance));
        Assert.True(samples.Zip(samples.Skip(1), (a, b) => b > a).All(increasing => increasing));

        double nearSpacing = samples[1] - samples[0];
        double farSpacing = samples[^1] - samples[^2];
        Assert.True(nearSpacing < farSpacing * 0.2,
            $"tangent sampling should resolve dense air: near spacing {nearSpacing:F0}, far {farSpacing:F0} m");
    }

    [Fact]
    public void InvalidGeometryAndSamplingArgumentsFailDeterministically()
    {
        Assert.Null(AtmosphereRaySampling.IntersectShell(
            Vector3d.Zero, Vector3d.Up, EarthRadius, AtmosphereRadius));
        Assert.Null(AtmosphereRaySampling.IntersectShell(
            Vector3d.Right * EarthRadius, Vector3d.Zero, EarthRadius, AtmosphereRadius));
        Assert.Null(AtmosphereRaySampling.IntersectShell(
            Vector3d.Right * EarthRadius, Vector3d.Up, EarthRadius, EarthRadius));

        var valid = new AtmosphereRaySegment(0.0, 10.0, 0.0, false);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AtmosphereRaySampling.TangentBiasedSampleDistance(valid, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AtmosphereRaySampling.TangentBiasedSampleDistance(valid, 2, 2));
    }
}
