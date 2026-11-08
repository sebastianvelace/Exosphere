namespace ExosphereSimulation.Tests;

using System.Text.Json;
using Exosphere.Simulation.Environment;
using Exosphere.Simulation.Math;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Environment plan E1.2: the GCRS → ITRS rotation against two oracles at 128 instants from
/// 1962 to 2026 (fixture from tools/envdata/fixtures/earth_orientation.py), measured as the
/// displacement of a point on the equator (6378 km):
/// <list type="bullet">
/// <item>ERFA/SOFA with the same IERS chain and the same C04 interpolation: within 5 mm (the
///   remaining difference is the daily X, Y, s table's interpolation and rounding);</item>
/// <item>SPICE pxform with the NAIF high-precision kernel (ITRF93 referred to the ICRF, which
///   NAIF labels J2000; a different EOP reduction): within 3σ of the published C04 uncertainty of
///   the epoch, floored at 0.5 m from 1995 (the plan's target; measured ≤ 0.27 m since 1990) and
///   at 1.5 m before, where EOP series revisions of the early VLBI and optical era reach ~1 m.</item>
/// </list>
/// </summary>
public sealed class IersEarthOrientationTests(ITestOutputHelper output)
{
    private const double EquatorialRadiusM = 6378137.0;
    private const double ErfaToleranceM = 0.005;
    private const double SpiceFloorModernM = 0.5;
    private const double SpiceFloorEarlyM = 1.5;

    [Fact]
    [Trait("FidelityGate", "E1")]
    [Trait("GateKind", "analytic")]
    public void RotationMatchesErfaAndSpiceFrom1962To2026()
    {
        var data = Path.Combine(Root(), "data");
        var orientation = IersEarthOrientation.LoadFromDataDirectory(data);
        var scales = orientation.Scales;
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(),
            "ExosphereSimulation.Tests", "Fixtures", "environment", "earth_orientation.json")));
        double worstErfa = 0.0, worstSpice = 0.0, worstSpiceRatio = 0.0;
        int count = 0;
        foreach (var instant in fixture.RootElement.GetProperty("instants").EnumerateArray())
        {
            var c = instant.GetProperty("utc").EnumerateArray().Select(e => e.GetDouble()).ToArray();
            double utc = TimeScales.UtcSecondsFromCalendar((int)c[0], (int)c[1], (int)c[2], (int)c[3], (int)c[4], c[5]);
            double tt = TimeScales.TaiToTt(scales.UtcToTai(utc));
            var sample = orientation.Sample(tt);
            var matrix = IersEarthOrientation.CelestialToTerrestrial(sample);
            string note = instant.GetProperty("note").GetString()!;

            double erfa = Displacement(matrix, ReadMatrix(instant.GetProperty("erfa")));
            Assert.True(erfa <= ErfaToleranceM, $"{note} {string.Join('-', c)}: {erfa:F4} m from ERFA");
            worstErfa = System.Math.Max(worstErfa, erfa);

            if (instant.TryGetProperty("spice", out var spiceElement))
            {
                double distance = Displacement(matrix, ReadMatrix(spiceElement));
                double sigmaM = EquatorialRadiusM * System.Math.Sqrt(sample.PoleSigmaRad * sample.PoleSigmaRad
                    + System.Math.Pow(sample.Ut1SigmaSeconds * 7.292115e-5, 2));
                double floor = c[0] >= 1995 ? SpiceFloorModernM : SpiceFloorEarlyM;
                double bound = System.Math.Max(floor, 3.0 * sigmaM);
                Assert.True(distance <= bound, $"{note} {string.Join('-', c)}: {distance:F3} m from SPICE (bound {bound:F3} m)");
                worstSpice = System.Math.Max(worstSpice, distance);
                worstSpiceRatio = System.Math.Max(worstSpiceRatio, distance / bound);
            }
            count++;
        }
        output.WriteLine($"{count} instants: ERFA {worstErfa * 1000:F3} mm, SPICE {worstSpice:F3} m " +
            $"({worstSpiceRatio:P0} of its bound)");
        Assert.True(count >= 120);
    }

    [Fact]
    public void Apollo11LaunchSampleCarriesTheEpochUncertainty()
    {
        var orientation = IersEarthOrientation.LoadFromDataDirectory(Path.Combine(Root(), "data"));
        double utc = TimeScales.UtcSecondsFromCalendar(1969, 7, 16, 13, 32, 0.0);
        var sample = orientation.Sample(TimeScales.TaiToTt(orientation.Scales.UtcToTai(utc)));
        Assert.True(sample.WithinObservations);
        // C04 1969: pole 0.02" per axis, UT1 1.5 ms.
        Assert.InRange(sample.PoleSigmaRad / IersEarthOrientation.ArcsecondToRadian, 0.028, 0.029);
        Assert.Equal(0.0015, sample.Ut1SigmaSeconds, 9);
        // UT1 - UTC on 1969-07-16 was +0.0115 s (C04 row), so UT1 - TAI ≈ 0.0115 − 7.5635.
        Assert.InRange(sample.Ut1MinusTaiSeconds, -7.556, -7.548);
    }

    [Fact]
    public void SimulationFramePlacesThePoleWhereTheFixedAxisModelDoes()
    {
        // ε₀ about +X maps the GCRS pole to (0, sin ε₀, cos ε₀), the original fixed-axis convention.
        var pole = IersEarthOrientation.GcrsToSimulation.Multiply(new Vector3d(0, 0, 1));
        double epsilon = IersEarthOrientation.J2000ObliquityArcsec * IersEarthOrientation.ArcsecondToRadian;
        Assert.Equal(0.0, pole.X, 15);
        Assert.Equal(System.Math.Sin(epsilon), pole.Y, 15);
        Assert.Equal(System.Math.Cos(epsilon), pole.Z, 15);
    }

    [Fact]
    public void EarthRotationAngleAtJ2000()
    {
        // IERS 2010 eq. 5.15 at UT1 = J2000.0: 2π · 0.7790572732640.
        Assert.Equal(2.0 * System.Math.PI * 0.7790572732640, IersEarthOrientation.EarthRotationAngle(0.0), 14);
        double day = IersEarthOrientation.EarthRotationAngle(1.0) - IersEarthOrientation.EarthRotationAngle(0.0);
        Assert.Equal(2.0 * System.Math.PI * 0.00273781191135448, day, 12);
    }

    private static double Displacement(Matrix3x3d a, Matrix3x3d b)
    {
        // Rotation angle of aᵀ·b, as a displacement on the equator.
        var d = IersEarthOrientation.Multiply(IersEarthOrientation.Transpose(a), b);
        double cosine = System.Math.Clamp((d.M11 + d.M22 + d.M33 - 1.0) / 2.0, -1.0, 1.0);
        // acos loses precision near 1; use the antisymmetric part for small angles.
        double sine = 0.5 * System.Math.Sqrt(System.Math.Pow(d.M32 - d.M23, 2)
            + System.Math.Pow(d.M13 - d.M31, 2) + System.Math.Pow(d.M21 - d.M12, 2));
        return System.Math.Atan2(sine, cosine) * EquatorialRadiusM;
    }

    private static Matrix3x3d ReadMatrix(JsonElement element)
    {
        var r = element.EnumerateArray().Select(row => row.EnumerateArray().Select(v => v.GetDouble()).ToArray()).ToArray();
        return new Matrix3x3d(r[0][0], r[0][1], r[0][2], r[1][0], r[1][1], r[1][2], r[2][0], r[2][1], r[2][2]);
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
            directory = directory.Parent;
        return directory!.FullName;
    }
}
