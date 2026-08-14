namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Xunit;

/// <summary>
/// Permanent coverage for Cartesian-state → orbital-elements → Cartesian-state
/// reconstruction at the source epoch.
///
/// The exact equatorial-retrograde case is singular because the ascending node is
/// undefined.  OrbitalElements has a dedicated convention for that geometry; the
/// non-singular controls below ensure that convention does not leak into nearby or
/// prograde/inclined orbits.
/// </summary>
public sealed class OrbitalElementsRoundTripTests
{
    private const double EarthGm = 3.986004418e14;
    private const double Epoch = 123_456.75;
    private const string ReferenceBody = "earth";

    private const double PositionAbsoluteTolerance = 1e-5;
    private const double PositionRelativeTolerance = 2e-12;
    private const double VelocityAbsoluteTolerance = 1e-8;
    private const double VelocityRelativeTolerance = 2e-12;

    public static IEnumerable<object[]> EquatorialRetrogradeQuadrants => new[]
    {
        new object[] { 0.20, "near_periapsis" },
        new object[] { 1.70, "quadrant_two" },
        new object[] { 3.30, "quadrant_three" },
        new object[] { 5.40, "quadrant_four" },
    };

    [Theory]
    [MemberData(nameof(EquatorialRetrogradeQuadrants))]
    public void EquatorialRetrogradeQuadrantsRoundTripAtEpoch(double trueAnomaly, string caseName)
    {
        const double semiMajorAxis = 7_200_000.0;
        const double eccentricity = 0.18;
        const double inclination = System.Math.PI;
        const double longitudeOfAscendingNode = 0.0;
        const double argumentOfPeriapsis = 0.73;

        var expected = StateFromClassicalElements(
            semiMajorAxis,
            eccentricity,
            inclination,
            longitudeOfAscendingNode,
            argumentOfPeriapsis,
            trueAnomaly);
        var actual = OrbitalElements.FromStateVector(
            expected.position,
            expected.velocity,
            EarthGm,
            ReferenceBody,
            Epoch);

        AssertFinite(actual, caseName);
        Assert.False(actual.IsRadial, caseName);
        Assert.True(actual.Inclination > System.Math.PI - 1e-12, caseName);
        Assert.True(actual.LongitudeOfAscendingNode <= 1e-12, caseName);
        AssertClose(
            expected.position,
            actual.GetStateAtTime(Epoch, EarthGm).position,
            PositionAbsoluteTolerance,
            PositionRelativeTolerance,
            $"{caseName}.position");
        AssertClose(
            expected.velocity,
            actual.GetStateAtTime(Epoch, EarthGm).velocity,
            VelocityAbsoluteTolerance,
            VelocityRelativeTolerance,
            $"{caseName}.velocity");
    }

    [Fact]
    public void CircularEquatorialRetrogradeRoundTripsAtEpoch()
    {
        var expected = StateFromClassicalElements(
            semiMajorAxis: 7_000_000.0,
            eccentricity: 0.0,
            inclination: System.Math.PI,
            longitudeOfAscendingNode: 0.0,
            argumentOfPeriapsis: 0.0,
            trueAnomaly: 4.80);

        var actual = OrbitalElements.FromStateVector(
            expected.position,
            expected.velocity,
            EarthGm,
            ReferenceBody,
            Epoch);
        var reconstructed = actual.GetStateAtTime(Epoch, EarthGm);

        AssertFinite(actual, "circular_retrograde");
        Assert.False(actual.IsRadial);
        Assert.InRange(actual.Eccentricity, 0.0, 1e-10);
        Assert.True(actual.Inclination > System.Math.PI - 1e-12);
        AssertClose(
            expected.position,
            reconstructed.position,
            PositionAbsoluteTolerance,
            PositionRelativeTolerance,
            "circular_retrograde.position");
        AssertClose(
            expected.velocity,
            reconstructed.velocity,
            VelocityAbsoluteTolerance,
            VelocityRelativeTolerance,
            "circular_retrograde.velocity");
    }

    [Fact]
    public void EquatorialProgradeUsesNonRetrogradeConvention()
    {
        const double argumentOfPeriapsis = 0.91;
        var expected = StateFromClassicalElements(
            semiMajorAxis: 7_200_000.0,
            eccentricity: 0.18,
            inclination: 0.0,
            longitudeOfAscendingNode: 0.0,
            argumentOfPeriapsis,
            trueAnomaly: 2.40);

        var actual = OrbitalElements.FromStateVector(
            expected.position,
            expected.velocity,
            EarthGm,
            ReferenceBody,
            Epoch);
        var reconstructed = actual.GetStateAtTime(Epoch, EarthGm);

        AssertFinite(actual, "equatorial_prograde");
        Assert.False(actual.IsRadial);
        Assert.InRange(actual.Inclination, 0.0, 1e-12);
        AssertAngleClose(argumentOfPeriapsis, actual.ArgumentOfPeriapsis, 1e-12,
            "equatorial_prograde.argument_of_periapsis");
        AssertClose(
            expected.position,
            reconstructed.position,
            PositionAbsoluteTolerance,
            PositionRelativeTolerance,
            "equatorial_prograde.position");
        AssertClose(
            expected.velocity,
            reconstructed.velocity,
            VelocityAbsoluteTolerance,
            VelocityRelativeTolerance,
            "equatorial_prograde.velocity");
    }

    [Fact]
    public void SlightlyInclinedRetrogradeDoesNotUseSingularConvention()
    {
        const double inclination = System.Math.PI - 1e-4;
        const double longitudeOfAscendingNode = 0.61;
        const double argumentOfPeriapsis = 1.17;
        var expected = StateFromClassicalElements(
            semiMajorAxis: 7_200_000.0,
            eccentricity: 0.18,
            inclination,
            longitudeOfAscendingNode,
            argumentOfPeriapsis,
            trueAnomaly: 2.20);

        var actual = OrbitalElements.FromStateVector(
            expected.position,
            expected.velocity,
            EarthGm,
            ReferenceBody,
            Epoch);
        var reconstructed = actual.GetStateAtTime(Epoch, EarthGm);

        AssertFinite(actual, "slightly_inclined_retrograde");
        Assert.False(actual.IsRadial);
        AssertAngleClose(inclination, actual.Inclination, 1e-12,
            "slightly_inclined_retrograde.inclination");
        AssertAngleClose(longitudeOfAscendingNode, actual.LongitudeOfAscendingNode, 1e-12,
            "slightly_inclined_retrograde.longitude_of_ascending_node");
        AssertAngleClose(argumentOfPeriapsis, actual.ArgumentOfPeriapsis, 1e-12,
            "slightly_inclined_retrograde.argument_of_periapsis");
        Assert.True(actual.LongitudeOfAscendingNode > 1e-6,
            "the equatorial-retrograde singular convention must not collapse a non-singular node");
        AssertClose(
            expected.position,
            reconstructed.position,
            PositionAbsoluteTolerance,
            PositionRelativeTolerance,
            "slightly_inclined_retrograde.position");
        AssertClose(
            expected.velocity,
            reconstructed.velocity,
            VelocityAbsoluteTolerance,
            VelocityRelativeTolerance,
            "slightly_inclined_retrograde.velocity");
    }

    [Fact]
    public void InclinedProgradeRoundTripsWithGeneralNodeConvention()
    {
        const double inclination = 0.42;
        const double longitudeOfAscendingNode = 0.78;
        const double argumentOfPeriapsis = 1.20;
        var expected = StateFromClassicalElements(
            semiMajorAxis: 7_400_000.0,
            eccentricity: 0.23,
            inclination,
            longitudeOfAscendingNode,
            argumentOfPeriapsis,
            trueAnomaly: 4.10);

        var actual = OrbitalElements.FromStateVector(
            expected.position,
            expected.velocity,
            EarthGm,
            ReferenceBody,
            Epoch);
        var reconstructed = actual.GetStateAtTime(Epoch, EarthGm);

        AssertFinite(actual, "inclined_prograde");
        Assert.False(actual.IsRadial);
        AssertAngleClose(inclination, actual.Inclination, 1e-12,
            "inclined_prograde.inclination");
        AssertAngleClose(longitudeOfAscendingNode, actual.LongitudeOfAscendingNode, 1e-12,
            "inclined_prograde.longitude_of_ascending_node");
        AssertAngleClose(argumentOfPeriapsis, actual.ArgumentOfPeriapsis, 1e-12,
            "inclined_prograde.argument_of_periapsis");
        AssertClose(
            expected.position,
            reconstructed.position,
            PositionAbsoluteTolerance,
            PositionRelativeTolerance,
            "inclined_prograde.position");
        AssertClose(
            expected.velocity,
            reconstructed.velocity,
            VelocityAbsoluteTolerance,
            VelocityRelativeTolerance,
            "inclined_prograde.velocity");
    }

    private static (Vector3d position, Vector3d velocity) StateFromClassicalElements(
        double semiMajorAxis,
        double eccentricity,
        double inclination,
        double longitudeOfAscendingNode,
        double argumentOfPeriapsis,
        double trueAnomaly)
    {
        double p = semiMajorAxis * (1.0 - eccentricity * eccentricity);
        double radius = p / (1.0 + eccentricity * System.Math.Cos(trueAnomaly));
        double rootMuOverP = System.Math.Sqrt(EarthGm / p);

        double cosNu = System.Math.Cos(trueAnomaly);
        double sinNu = System.Math.Sin(trueAnomaly);
        double xPerifocal = radius * cosNu;
        double yPerifocal = radius * sinNu;
        double vxPerifocal = -rootMuOverP * sinNu;
        double vyPerifocal = rootMuOverP * (eccentricity + cosNu);

        double cosOmega = System.Math.Cos(longitudeOfAscendingNode);
        double sinOmega = System.Math.Sin(longitudeOfAscendingNode);
        double cosInclination = System.Math.Cos(inclination);
        double sinInclination = System.Math.Sin(inclination);
        double cosArgument = System.Math.Cos(argumentOfPeriapsis);
        double sinArgument = System.Math.Sin(argumentOfPeriapsis);

        double r11 = cosOmega * cosArgument - sinOmega * sinArgument * cosInclination;
        double r12 = -cosOmega * sinArgument - sinOmega * cosArgument * cosInclination;
        double r21 = sinOmega * cosArgument + cosOmega * sinArgument * cosInclination;
        double r22 = -sinOmega * sinArgument + cosOmega * cosArgument * cosInclination;
        double r31 = sinArgument * sinInclination;
        double r32 = cosArgument * sinInclination;

        return (
            new Vector3d(
                r11 * xPerifocal + r12 * yPerifocal,
                r21 * xPerifocal + r22 * yPerifocal,
                r31 * xPerifocal + r32 * yPerifocal),
            new Vector3d(
                r11 * vxPerifocal + r12 * vyPerifocal,
                r21 * vxPerifocal + r22 * vyPerifocal,
                r31 * vxPerifocal + r32 * vyPerifocal));
    }

    private static void AssertFinite(OrbitalElements elements, string caseName)
    {
        Assert.True(double.IsFinite(elements.SemiMajorAxis), $"{caseName}.semi_major_axis");
        Assert.True(double.IsFinite(elements.Eccentricity), $"{caseName}.eccentricity");
        Assert.True(double.IsFinite(elements.Inclination), $"{caseName}.inclination");
        Assert.True(double.IsFinite(elements.LongitudeOfAscendingNode),
            $"{caseName}.longitude_of_ascending_node");
        Assert.True(double.IsFinite(elements.ArgumentOfPeriapsis),
            $"{caseName}.argument_of_periapsis");
        Assert.True(double.IsFinite(elements.MeanAnomalyAtEpoch), $"{caseName}.mean_anomaly");
        Assert.True(double.IsFinite(elements.Epoch), $"{caseName}.epoch");
        Assert.True(double.IsFinite(elements.SpecificAngularMomentum),
            $"{caseName}.specific_angular_momentum");
        Assert.True(double.IsFinite(elements.PeriapsisRadius), $"{caseName}.periapsis_radius");
    }

    private static void AssertClose(
        Vector3d expected,
        Vector3d actual,
        double absoluteTolerance,
        double relativeTolerance,
        string label)
    {
        AssertComponentClose(expected.X, actual.X, absoluteTolerance, relativeTolerance, $"{label}.x");
        AssertComponentClose(expected.Y, actual.Y, absoluteTolerance, relativeTolerance, $"{label}.y");
        AssertComponentClose(expected.Z, actual.Z, absoluteTolerance, relativeTolerance, $"{label}.z");
    }

    private static void AssertComponentClose(
        double expected,
        double actual,
        double absoluteTolerance,
        double relativeTolerance,
        string label)
    {
        Assert.True(double.IsFinite(actual), $"{label}: actual value is not finite");
        double error = System.Math.Abs(actual - expected);
        double allowed = System.Math.Max(
            absoluteTolerance,
            relativeTolerance * System.Math.Max(System.Math.Abs(expected), System.Math.Abs(actual)));
        Assert.True(error <= allowed,
            $"{label}: expected {expected:G17}, actual {actual:G17}, "
            + $"absolute error {error:G6}, allowed {allowed:G6}");
    }

    private static void AssertAngleClose(double expected, double actual, double tolerance, string label)
    {
        double delta = System.Math.IEEERemainder(actual - expected, 2.0 * System.Math.PI);
        Assert.True(System.Math.Abs(delta) <= tolerance,
            $"{label}: expected {expected:G17}, actual {actual:G17}, delta {delta:G6}, limit {tolerance:G6}");
    }
}
