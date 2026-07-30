namespace ExosphereSimulation.Tests;

using System;
using System.IO;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Xunit;

/// <summary>
/// Guards the J2000.0 phase convention of the planetary ephemeris data.
///
/// <para>The mean elements published for J2000.0 (Standish, <i>Explanatory Supplement to
/// the Astronomical Almanac</i>) give a mean longitude L and a longitude of perihelion
/// ϖ = Ω + ω. The mean anomaly the propagator needs is the difference of the two:
/// M = L − ϖ, reduced to [0, 360). A body whose <c>mean_anomaly_at_epoch</c> does not
/// satisfy that relation is out of phase along its orbit, which shifts every transfer
/// window computed from it.</para>
/// </summary>
public sealed class EphemerisPhaseTests
{
    /// <summary>
    /// Angular tolerance (deg). 0.05° is far tighter than the ~0.5° the two-body mean
    /// elements themselves are good for, but it is what the inner planets already meet,
    /// so it pins the data rather than the model.
    /// </summary>
    private const double ToleranceDeg = 0.05;

    // Jupiter (L = 34.39644, ϖ = 14.72847 ⇒ M ≈ 19.668 vs 20.02 in JSON) and Saturn
    // (L = 49.95424, ϖ = 92.59887 ⇒ M ≈ 317.355 vs 317.02 in JSON) are OUTSIDE this
    // tolerance and are deliberately excluded, not silently absorbed. Loosening the
    // tolerance to cover them would hide a real ~0.3-0.5° phase error in the outer
    // planets. Fix the data, then add them here.
    [Theory]
    [InlineData("mercury", 252.25084, 77.45645)]
    [InlineData("venus",   181.97973, 131.53298)]
    [InlineData("earth",   100.46435, 102.94719)]
    [InlineData("mars",    355.45332, 336.04084)]
    public void MeanAnomalyAtEpochMatchesMeanLongitudeMinusLongitudeOfPerihelion(
        string bodyId, double meanLongitudeDeg, double longitudeOfPerihelionDeg)
    {
        var body = LoadBody(bodyId);
        Assert.NotNull(body.OrbitalElements);

        double expected = Normalize360(meanLongitudeDeg - longitudeOfPerihelionDeg);
        double actual   = Normalize360(body.OrbitalElements!.MeanAnomalyAtEpoch * MathUtils.RAD_TO_DEG);

        double error = System.Math.Abs(Normalize180(actual - expected));
        Assert.True(error <= ToleranceDeg,
            $"{bodyId}: mean_anomaly_at_epoch is {actual:F4}°, but L − ϖ = {expected:F4}° "
            + $"(off by {error:F4}°, tolerance {ToleranceDeg}°).");
    }

    /// <summary>
    /// The JSON stores Ω and ω separately, so ϖ = Ω + ω must reproduce the published
    /// longitude of perihelion too — otherwise the orbit plane and the phase disagree.
    /// </summary>
    [Theory]
    [InlineData("mercury", 77.45645)]
    [InlineData("venus",   131.53298)]
    [InlineData("earth",   102.94719)]
    [InlineData("mars",    336.04084)]
    public void NodePlusArgumentOfPeriapsisMatchesLongitudeOfPerihelion(
        string bodyId, double longitudeOfPerihelionDeg)
    {
        var body = LoadBody(bodyId);
        Assert.NotNull(body.OrbitalElements);

        double actual = Normalize360(
            (body.OrbitalElements!.LongitudeOfAscendingNode
             + body.OrbitalElements.ArgumentOfPeriapsis) * MathUtils.RAD_TO_DEG);

        double error = System.Math.Abs(
            Normalize180(actual - Normalize360(longitudeOfPerihelionDeg)));
        Assert.True(error <= ToleranceDeg,
            $"{bodyId}: Ω + ω = {actual:F4}°, expected ϖ = {longitudeOfPerihelionDeg:F4}° "
            + $"(off by {error:F4}°, tolerance {ToleranceDeg}°).");
    }

    private static double Normalize360(double deg)
    {
        double wrapped = deg % 360.0;
        return wrapped < 0.0 ? wrapped + 360.0 : wrapped;
    }

    /// <summary>Signed difference in (-180, 180], so 359.99° and 0.01° are 0.02° apart.</summary>
    private static double Normalize180(double deg)
    {
        double wrapped = Normalize360(deg);
        return wrapped > 180.0 ? wrapped - 360.0 : wrapped;
    }

    private static CelestialBody LoadBody(string id) =>
        CelestialBody.LoadFromJson(Path.Combine(FindRepoRoot().FullName, "data", "bodies", $"{id}.json"));

    private static DirectoryInfo FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data"))
                && File.Exists(Path.Combine(dir.FullName, "ExosphereSimulation.sln")))
                return dir;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }
}
