namespace ExosphereSimulation.Tests;

using System.Text.Json;
using Exosphere.Simulation;
using Exosphere.Simulation.Environment;
using Exosphere.Simulation.Math;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Environment plan E3.2: the Cunningham V/W recursion against a method-independent reference
/// (direct 4π-normalized Legendre sums at 40 digits, gradient by central differences; fixture from
/// tools/envdata/fixtures/spherical_harmonics.py) for fields of degree 12 and 60 at 100 points
/// each, poles included. The bound is 1e-12 of the point-mass acceleration.
/// </summary>
public sealed class SphericalHarmonicGravityTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("FidelityGate", "E3")]
    [Trait("GateKind", "analytic")]
    public void RecursionMatchesTheDirectLegendreReference()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(),
            "ExosphereSimulation.Tests", "Fixtures", "environment", "spherical_harmonics.json")));
        foreach (var field in fixture.RootElement.GetProperty("fields").EnumerateArray())
        {
            int degree = field.GetProperty("degree").GetInt32();
            double gm = field.GetProperty("gm").GetDouble(), radius = field.GetProperty("radius").GetDouble();
            var c = field.GetProperty("c").EnumerateArray().Select(e => e.GetDouble()).ToArray();
            var s = field.GetProperty("s").EnumerateArray().Select(e => e.GetDouble()).ToArray();
            var model = new SphericalHarmonicGravity("test", gm, radius, degree, c, s);
            double worstA = 0.0, worstU = 0.0;
            foreach (var point in field.GetProperty("points").EnumerateArray())
            {
                var p = Read(point.GetProperty("position_m"));
                var expected = Read(point.GetProperty("acceleration_mps2"));
                double pointMass = gm / p.MagnitudeSquared;
                double errorA = (model.AccelerationBodyFixed(p) - expected).Magnitude / pointMass;
                double errorU = System.Math.Abs(model.PotentialBodyFixed(p) - point.GetProperty("potential").GetDouble())
                    / (gm / p.Magnitude);
                worstA = System.Math.Max(worstA, errorA);
                worstU = System.Math.Max(worstU, errorU);
            }
            output.WriteLine($"degree {degree}: acceleration {worstA:E2}, potential {worstU:E2} (relative to point mass)");
            Assert.True(worstA < 1e-12, $"degree {degree}: acceleration {worstA:E3}");
            Assert.True(worstU < 1e-13, $"degree {degree}: potential {worstU:E3}");
        }
    }

    [Fact]
    public void DegreeTwoZonalFieldReproducesTheJ2Model()
    {
        // A C̄₂₀ = −J₂/√5 field equals the zonal J2 model the simulation has always used.
        var earth = CelestialBody.LoadFromJson(Path.Combine(Root(), "data", "bodies", "earth.json"));
        earth.Position = Vector3d.Zero;
        var c = new double[6];
        var s = new double[6];
        c[0] = 1.0;
        c[SphericalHarmonicGravity.Index(2, 0)] = -earth.J2 / System.Math.Sqrt(5.0);
        var field = new SphericalHarmonicGravity("j2", earth.GM, earth.EquatorialRadius, 2, c, s);
        var rng = new Random(2);
        double worst = 0.0;
        for (int i = 0; i < 200; i++)
        {
            var p = new Vector3d(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5).Normalized
                * (earth.EquatorialRadius * (1.01 + 3.0 * rng.NextDouble()));
            var zonal = ZonalJ2Gravity.Instance.AccelerationAt(earth, p);
            var harmonic = field.AccelerationAt(earth, p);
            worst = System.Math.Max(worst, (harmonic - zonal).Magnitude / zonal.Magnitude);
        }
        output.WriteLine($"degree-2 field vs zonal J2: {worst:E2} relative");
        Assert.True(worst < 1e-13, $"{worst}");
    }

    [Fact]
    public void J2NodalPrecessionMatchesTheClassicalRate()
    {
        // Ω̇ = −(3/2) n J₂ (R/p)² cos i: integrate a 500 km, 51.6° orbit for one day on the
        // degree-2 field and compare the node drift (secular formula, first order in J2).
        var earth = CelestialBody.LoadFromJson(Path.Combine(Root(), "data", "bodies", "earth.json"));
        earth.Position = Vector3d.Zero;
        earth.Velocity = Vector3d.Zero;
        var c = new double[6];
        c[0] = 1.0;
        c[SphericalHarmonicGravity.Index(2, 0)] = -earth.J2 / System.Math.Sqrt(5.0);
        var field = new SphericalHarmonicGravity("j2", earth.GM, earth.EquatorialRadius, 2, c, new double[6]);
        earth.GetRotatingBasis(out var xAxis, out var yAxis, out var north);
        double a = earth.EquatorialRadius + 500e3, inc = 51.6 * System.Math.PI / 180.0;
        double v = System.Math.Sqrt(earth.GM / a);
        var r = xAxis * a;
        var vel = (yAxis * System.Math.Cos(inc) + north * System.Math.Sin(inc)) * v;
        double Node(Vector3d rr, Vector3d vv)
        {
            var h = rr.Cross(vv);
            var n = north.Cross(h);
            return System.Math.Atan2(n.Dot(yAxis), n.Dot(xAxis));
        }
        double start = Node(r, vel), dt = 5.0, duration = 86400.0;
        for (double t = 0; t < duration; t += dt)
        {
            // RK4 on the field (no rotation: the zonal field is axisymmetric).
            Vector3d A(Vector3d p) => field.AccelerationBodyFixed(new Vector3d(p.Dot(xAxis), p.Dot(yAxis), p.Dot(north))) is var b
                ? xAxis * b.X + yAxis * b.Y + north * b.Z : default;
            var k1v = A(r); var k1r = vel;
            var k2v = A(r + k1r * (dt / 2)); var k2r = vel + k1v * (dt / 2);
            var k3v = A(r + k2r * (dt / 2)); var k3r = vel + k2v * (dt / 2);
            var k4v = A(r + k3r * dt); var k4r = vel + k3v * dt;
            r += (k1r + k2r * 2 + k3r * 2 + k4r) * (dt / 6);
            vel += (k1v + k2v * 2 + k3v * 2 + k4v) * (dt / 6);
        }
        double drift = Node(r, vel) - start;
        double meanMotion = System.Math.Sqrt(earth.GM / (a * a * a));
        double expected = -1.5 * meanMotion * earth.J2 * System.Math.Pow(earth.EquatorialRadius / a, 2) * System.Math.Cos(inc) * duration;
        output.WriteLine($"node drift {drift * 180 / System.Math.PI:F4}° vs {expected * 180 / System.Math.PI:F4}°/day");
        // First-order secular theory vs a numerically integrated (osculating) node: 1 % covers
        // the short-period terms at the sampled instant.
        Assert.InRange(drift / expected, 0.99, 1.01);
    }

    private static Vector3d Read(JsonElement e) => new(e[0].GetDouble(), e[1].GetDouble(), e[2].GetDouble());

    private static string Root()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
            directory = directory.Parent;
        return directory!.FullName;
    }
}
