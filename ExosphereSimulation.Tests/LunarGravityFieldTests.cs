namespace ExosphereSimulation.Tests;

using System.Diagnostics;
using System.Text.Json;
using Exosphere.Simulation;
using Exosphere.Simulation.Environment;
using Exosphere.Simulation.Math;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Environment plan E3.1 and E3.3: the DE440 lunar principal-axes orientation against SPICE
/// (fixture from tools/envdata/fixtures/lunar_orientation.py), the GRGM1200A and EGM2008 fields
/// loaded from their products, and the truncation analysis that sets the lunar degree.
/// </summary>
public sealed class LunarGravityFieldTests(ITestOutputHelper output)
{
    private const double MoonRadiusM = 1737400.0;

    [Fact]
    public void LunarPrincipalAxesMatchSpice()
    {
        var orientation = LunarPrincipalAxesOrientation.LoadFromDataDirectory(DataDirectory());
        var moon = new CelestialBody { Id = "moon", Orientation = orientation };
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(),
            "ExosphereSimulation.Tests", "Fixtures", "environment", "lunar_orientation.json")));
        double worstSurface = 0.0, worstOmega = 0.0;
        foreach (var instant in fixture.RootElement.GetProperty("instants").EnumerateArray())
        {
            double t = instant.GetProperty("tdb_s").GetDouble();
            var expected = ReadMatrix(instant.GetProperty("icrf_to_pa"));
            double surface = Angle(orientation.IcrfToPrincipalAxes(t), expected) * MoonRadiusM;
            worstSurface = System.Math.Max(worstSurface, surface);
            moon.StateTime = t;
            var omega = orientation.SpinAxis(moon) * orientation.AngularSpeed(moon);
            var expectedOmega = IersEarthOrientation.GcrsToSimulation.Multiply(Read(instant.GetProperty("omega_icrf_rad_s")));
            worstOmega = System.Math.Max(worstOmega, (omega - expectedOmega).Magnitude / expectedOmega.Magnitude);
        }
        output.WriteLine($"lunar PA: {worstSurface * 1000:F3} mm on the surface, ω {worstOmega:E2} relative");
        Assert.True(worstSurface < 0.001, $"{worstSurface} m");
        Assert.True(worstOmega < 1e-6, $"{worstOmega}");
    }

    [Fact]
    public void FieldsLoadWithTheirPublishedConstants()
    {
        var moonField = GravityFieldProduct.Load(Path.Combine(DataDirectory(), "environment", "gravity", "grgm1200a_d120.bin"), 120);
        Assert.Equal(4.9028001224453e12, moonField.GravitationalParameter, 1);
        Assert.Equal(1738000.0, moonField.ReferenceRadius);
        var earthField = GravityFieldProduct.Load(Path.Combine(DataDirectory(), "environment", "gravity", "egm2008_d70.bin"), 70);
        Assert.Equal(3.986004415e14, earthField.GravitationalParameter, 0);
        Assert.Equal(6378136.3, earthField.ReferenceRadius);

        // Lunar surface gravity ~1.62 m/s² and EGM2008's J2 effect at the equator.
        var moonSurface = moonField.AccelerationBodyFixed(new Vector3d(1738000.0, 0, 0)).Magnitude;
        Assert.InRange(moonSurface, 1.60, 1.64);
        var earth2 = GravityFieldProduct.Load(Path.Combine(DataDirectory(), "environment", "gravity", "egm2008_d70.bin"), 2);
        var equator = earth2.AccelerationBodyFixed(new Vector3d(6378136.3, 0, 0)).Magnitude;
        var pole = earth2.AccelerationBodyFixed(new Vector3d(0, 0, 6378136.3)).Magnitude;
        // J2 makes gravity at the same radius stronger at the equator by (9/2)·J2·g ≈ 0.048 m/s².
        Assert.InRange(equator - pole, 0.045, 0.051);
    }

    [Fact]
    public void LunarTruncationAt100KilometresSetsTheDegree()
    {
        // The gravity anomaly a 100 km Apollo-style orbit feels, by truncation degree, against the
        // product's degree 120, and the cost of one evaluation.
        string path = Path.Combine(DataDirectory(), "environment", "gravity", "grgm1200a_d120.bin");
        var reference = GravityFieldProduct.Load(path, 120);
        var rng = new Random(11);
        var points = Enumerable.Range(0, 200).Select(_ =>
            new Vector3d(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, (rng.NextDouble() - 0.5) * 0.2).Normalized
            * (1738000.0 + 100000.0)).ToArray();
        var truth = points.Select(reference.AccelerationBodyFixed).ToArray();
        double anomaly = points.Select((p, i) => (truth[i] - p * (-reference.GravitationalParameter / System.Math.Pow(p.Magnitude, 3))).Magnitude).Max();
        output.WriteLine($"largest non-point-mass acceleration at 100 km: {anomaly * 1e3:F3} mm/s²");
        foreach (int degree in new[] { 20, 50, 70, 100 })
        {
            var field = GravityFieldProduct.Load(path, degree);
            double worst = points.Select((p, i) => (field.AccelerationBodyFixed(p) - truth[i]).Magnitude).Max();
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 2000; i++) field.AccelerationBodyFixed(points[i % points.Length]);
            double micros = watch.Elapsed.TotalMilliseconds * 1000.0 / 2000;
            output.WriteLine($"degree {degree}: truncation {worst * 1e6:F2} µm/s² ({worst / anomaly:P2} of the anomaly), {micros:F1} µs/evaluation");
        }
        var chosen = GravityFieldProduct.Load(path, 70);
        double chosenError = points.Select((p, i) => (chosen.AccelerationBodyFixed(p) - truth[i]).Magnitude).Max();
        // Degree 70 keeps the truncation below 2 % of the anomaly at 100 km.
        Assert.True(chosenError < 0.02 * anomaly, $"{chosenError / anomaly:P2}");
    }

    private static double Angle(Matrix3x3d a, Matrix3x3d b)
    {
        var d = IersEarthOrientation.Multiply(IersEarthOrientation.Transpose(a), b);
        double sine = 0.5 * System.Math.Sqrt(System.Math.Pow(d.M32 - d.M23, 2) + System.Math.Pow(d.M13 - d.M31, 2)
            + System.Math.Pow(d.M21 - d.M12, 2));
        return System.Math.Atan2(sine, (d.M11 + d.M22 + d.M33 - 1.0) / 2.0);
    }

    private static Matrix3x3d ReadMatrix(JsonElement e)
    {
        var r = e.EnumerateArray().Select(row => row.EnumerateArray().Select(v => v.GetDouble()).ToArray()).ToArray();
        return new Matrix3x3d(r[0][0], r[0][1], r[0][2], r[1][0], r[1][1], r[1][2], r[2][0], r[2][1], r[2][2]);
    }

    private static Vector3d Read(JsonElement e) => new(e[0].GetDouble(), e[1].GetDouble(), e[2].GetDouble());

    private static string Root()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
            directory = directory.Parent;
        return directory!.FullName;
    }

    private static string DataDirectory() => Path.Combine(Root(), "data");
}
