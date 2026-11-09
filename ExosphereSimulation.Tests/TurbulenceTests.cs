namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Environment;
using Exosphere.Simulation.Math;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Environment plan E4.4: von Kármán turbulence with the NASA/TM-2008-215633 statistics. The
/// analytic gate is the field's second-order statistics over an ensemble of seeds at a table
/// level (10 km, severe: σ_h 7.72, σ_w 6.00 m/s, L_h 1230 m): per-component variance, and the
/// longitudinal and transverse correlation functions against von Kármán's closed forms
/// f(r) = (2^(2/3)/Γ(1/3)) ξ^(1/3) K_1/3(ξ), g(r) = f − (2^(2/3)/Γ(1/3)) (ξ^(4/3)/2) K_2/3(ξ),
/// ξ = r/(1.339 L). Tolerances are fixed before running: variance within 3 %, correlations
/// within 0.03 at lags 0.1–4 L (the ensemble's own sampling noise is ~0.01).
/// </summary>
public sealed class TurbulenceTests(ITestOutputHelper output)
{
    private const double Altitude = 10_000.0;

    [Fact]
    [Trait("FidelityGate", "E4")]
    [Trait("GateKind", "analytic")]
    public void TurbulenceHasTheVonKarmanStatistics()
    {
        var product = Product();
        const int seeds = 800, points = 80;
        double[] lags = [0.1, 0.25, 0.5, 1.0, 2.0, 4.0];
        var longitudinal = new double[lags.Length];
        var transverse = new double[lags.Length];
        double ve = 0, vn = 0, vu = 0, sigmaH = 0, sigmaW = 0, length = 0;
        for (int s = 0; s < seeds; s++)
        {
            var field = new VonKarmanTurbulence(product, TurbulenceIntensity.Severe, (ulong)(1000 + s), 28.6, -80.6);
            (sigmaH, sigmaW) = field.SigmaAt(Altitude);
            length = field.LengthAt(Altitude).Horizontal;
            var random = new Random(s); // sample points only; the field's own stream is SplitMix64
            for (int p = 0; p < points; p++)
            {
                double x = (random.NextDouble() - 0.5) * 2e6, y = (random.NextDouble() - 0.5) * 2e6;
                var (e0, n0, u0) = field.GustAt(x, y, Altitude);
                ve += e0 * e0; vn += n0 * n0; vu += u0 * u0;
                for (int l = 0; l < lags.Length; l++)
                {
                    var (e1, n1, _) = field.GustAt(x + lags[l] * length, y, Altitude);
                    longitudinal[l] += e0 * e1;
                    transverse[l] += n0 * n1;
                }
            }
        }
        double count = seeds * points;
        ve /= count; vn /= count; vu /= count;
        output.WriteLine($"σ east {System.Math.Sqrt(ve):F3}, north {System.Math.Sqrt(vn):F3} (table {sigmaH}), up {System.Math.Sqrt(vu):F3} (table {sigmaW}) m/s");
        Assert.InRange(ve / (sigmaH * sigmaH), 0.97, 1.03);
        Assert.InRange(vn / (sigmaH * sigmaH), 0.97, 1.03);
        Assert.InRange(vu / (sigmaW * sigmaW), 0.97, 1.03);
        for (int l = 0; l < lags.Length; l++)
        {
            double f = longitudinal[l] / count / ve, g = transverse[l] / count / vn;
            var (fRef, gRef) = VonKarmanCorrelation(lags[l]);
            output.WriteLine($"r = {lags[l]} L: f {f:F4} (von Kármán {fRef:F4}), g {g:F4} ({gRef:F4})");
            Assert.True(System.Math.Abs(f - fRef) < 0.03, $"f({lags[l]}) {f} vs {fRef}");
            Assert.True(System.Math.Abs(g - gRef) < 0.03, $"g({lags[l]}) {g} vs {gRef}");
        }
    }

    [Fact]
    public void TableIsTheHandbooksAndJoinsAtOneKilometre()
    {
        var field = new VonKarmanTurbulence(Product(), TurbulenceIntensity.Severe, 1, 28.6, -80.6);
        Assert.Equal((7.72, 6.00), field.SigmaAt(10_000.0));
        Assert.Equal((1230.0, 1100.0), field.LengthAt(10_000.0));
        // Table 2-70's 1000 m severe row is Table 2-71's 1 km row.
        Assert.Equal((5.70, 4.67), field.SigmaAt(1_000.0));
        var moderate = new VonKarmanTurbulence(Product(), TurbulenceIntensity.Moderate, 1, 28.6, -80.6);
        Assert.Equal((35.6, 11.4), moderate.SigmaAt(100_000.0));
        // Held above 200 km and below 10 m.
        Assert.Equal(moderate.SigmaAt(200_000.0), moderate.SigmaAt(400_000.0));
        Assert.Equal(moderate.SigmaAt(10.0), moderate.SigmaAt(0.0));
    }

    [Fact]
    public void SeedReproducesTheAirAndTheMeanWindSitsUnderneath()
    {
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        universe.UseWindProfile(DataDirectory(), "wind_as506");
        var earth = universe.GetBody("earth")!;
        var mean = earth.Wind!;
        universe.UseTurbulence(DataDirectory(), TurbulenceIntensity.Moderate, 42, 28.6084, -80.6043);
        var gusty = earth.Wind!;
        var near = earth.GetSurfacePositionAtTime(28.7, -80.4, universe.CurrentTime, 9_000.0);
        var gust = gusty.WindAt(earth, near) - mean.WindAt(earth, near);
        Assert.True(gust.Magnitude > 0.0 && gust.Magnitude < 20.0, $"{gust.Magnitude}");
        // Same seed, same air; another seed, other air.
        var again = VonKarmanTurbulence.LoadFromDataDirectory(DataDirectory(), TurbulenceIntensity.Moderate, 42, 28.6084, -80.6043, mean);
        Assert.Equal(gusty.WindAt(earth, near), again.WindAt(earth, near));
        var other = VonKarmanTurbulence.LoadFromDataDirectory(DataDirectory(), TurbulenceIntensity.Moderate, 43, 28.6084, -80.6043, mean);
        Assert.NotEqual(gusty.WindAt(earth, near), other.WindAt(earth, near));
        // Beyond the fade radius only the mean wind is left (Starbase is ~1500 km away: tapered;
        // the far side of the planet: none).
        var far = earth.GetSurfacePositionAtTime(-28.6, 99.4, universe.CurrentTime, 9_000.0);
        Assert.Equal(mean.WindAt(earth, far), gusty.WindAt(earth, far));
    }

    /// <summary>von Kármán longitudinal and transverse correlations at lag r (in units of L).</summary>
    private static (double F, double G) VonKarmanCorrelation(double r)
    {
        double xi = r / VonKarmanTurbulence.VonKarmanConstant;
        double c = System.Math.Pow(2.0, 2.0 / 3.0) / 2.6789385347077476; // Γ(1/3)
        double f = c * System.Math.Pow(xi, 1.0 / 3.0) * BesselK(1.0 / 3.0, xi);
        double g = f - c * 0.5 * System.Math.Pow(xi, 4.0 / 3.0) * BesselK(2.0 / 3.0, xi);
        return (f, g);
    }

    /// <summary>K_ν(x) = ∫₀^∞ exp(−x cosh t) cosh(νt) dt, by the trapezoid rule (spectrally accurate).</summary>
    private static double BesselK(double nu, double x)
    {
        const double step = 1e-3;
        double sum = 0.5 * System.Math.Exp(-x);
        for (int i = 1; ; i++)
        {
            double t = i * step;
            double term = System.Math.Exp(-x * System.Math.Cosh(t)) * System.Math.Cosh(nu * t);
            sum += term;
            if (term < 1e-18 * sum) break;
        }
        return sum * step;
    }

    private static EnvironmentProductFile Product() =>
        EnvironmentProductFile.Load(Path.Combine(DataDirectory(), "environment", "turbulence", "turbulence_msfc2008.bin"));

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }
}
