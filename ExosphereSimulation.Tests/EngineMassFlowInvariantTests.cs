namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Data;
using Exosphere.Simulation.Propulsion;
using Xunit;

/// <summary>
/// Data contracts on the four numbers that define a rocket engine's rated performance.
///
/// <para>A liquid engine runs a choked throat, so its propellant mass flow does not depend
/// on ambient pressure. Thrust does: F(p) = F_vac − p·A_e, exactly linear in p, which is the
/// law <see cref="EnginePerformanceEvaluator"/> implements. Since Isp ≡ F/(ṁ·g₀) and ṁ is
/// constant, Isp is linear in p too — and the four rated numbers are therefore NOT
/// independent. They satisfy one constraint:</para>
///
/// <code>F_vac / Isp_vac == F_sl / Isp_sl == ṁ·g₀</code>
///
/// <para>When a data file violates it, the model's own mass flow drifts with altitude:
/// <c>Part.GetMassFlow</c> divides the pressure-corrected thrust by the pressure-corrected
/// Isp, so a 9 % mismatch means the vehicle burns 9 % more or less propellant per second at
/// sea level than in vacuum for no physical reason. Raptor Vacuum (Flight 7) was in exactly
/// that state: 620.1 kg/s in vacuum against 566.5 kg/s at sea level.</para>
///
/// <para>The Starship and Super Heavy Raptors are now closed on this constraint by deriving
/// <c>specificImpulseSeaLevelS</c> from the invariant (see the <c>derived</c> provenance
/// records). The legacy catalog is not, and fixing it is a separate data-provenance exercise
/// — those engines are enumerated below with their measured error. That register is
/// shrink-only: an engine may leave it, its error may not grow, and a stale entry fails the
/// test so a fix cannot be left undocumented.</para>
/// </summary>
public sealed class EngineMassFlowInvariantTests
{
    private const double StandardGravity = 9.80665;
    private const double SeaLevelPressure = 101_325.0;

    /// <summary>Relative mass-flow mismatch a compliant engine may not exceed.</summary>
    private const double MassFlowTolerance = 0.005;

    /// <summary>
    /// Engines whose rated quartet does not yet close on one mass flow, with the error
    /// measured on 2026-09-18. These are pre-existing historical/legacy reconstructions
    /// outside the Starship data set; each needs its own provenance pass before the fourth
    /// number can be derived rather than asserted.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, double> KnownInconsistentEngines =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["be3u-newglenn-public-2026"] = 0.595506,
            ["merlin1d-vac-block5-2025"] = 0.355018,
            ["atlas-lr105-ma6-1962"] = 0.304871,
            ["apollo-sps-csm107-1969"] = 0.152094,
            ["apollo-sps-csm103-1968"] = 0.152061,
            ["lm-aps-lm5-1969"] = 0.149963,
            ["lm-dps-lm5-1969"] = 0.145943,
            ["j2-sivb-as503-1968"] = 0.139384,
            ["j2-sii-as503-1968"] = 0.128907,
            ["j2-sivb-as506-1969"] = 0.114673,
            ["j2-sii-as506-1969"] = 0.111830,
            ["titan2-lr87-glv8-1966"] = 0.044982,
            ["f1-as506-1969"] = 0.035362,
            ["titan2-lr91-glv8-1966"] = 0.020143,
            ["merlin1d-block5-2025"] = 0.019581,
            ["be4-newglenn-public-2026"] = 0.006909,
        };

    [Fact]
    public void RatedThrustAndIspImplyOnePressureInvariantMassFlow()
    {
        var failures = new List<string>();

        foreach (var model in LoadCatalog().Models.Values.OrderBy(m => m.Id, StringComparer.Ordinal))
        {
            double vacuumFlow = model.RatedThrustVacuumN
                / (model.SpecificImpulseVacuumS * StandardGravity);
            double seaLevelFlow = model.RatedThrustSeaLevelN
                / (model.SpecificImpulseSeaLevelS * StandardGravity);
            double error = System.Math.Abs(vacuumFlow - seaLevelFlow) / vacuumFlow;

            if (KnownInconsistentEngines.TryGetValue(model.Id, out double baseline))
            {
                if (error > baseline + 1e-6)
                {
                    failures.Add(
                        $"{model.Id}: known mass-flow error grew from {baseline * 100.0:F2}% "
                        + $"to {error * 100.0:F2}%. The register is shrink-only.");
                }
                else if (error <= MassFlowTolerance)
                {
                    failures.Add(
                        $"{model.Id}: now closes at {error * 100.0:F4}%. Remove it from "
                        + "KnownInconsistentEngines instead of leaving a stale exemption.");
                }

                continue;
            }

            if (error > MassFlowTolerance)
            {
                double consistentSeaLevelIsp = model.RatedThrustSeaLevelN
                    / (vacuumFlow * StandardGravity);
                failures.Add(
                    $"{model.Id}: ṁ = {vacuumFlow:F3} kg/s in vacuum but {seaLevelFlow:F3} kg/s "
                    + $"at sea level ({error * 100.0:F2}% apart). Holding the thrust pair and "
                    + $"Isp_vac, specificImpulseSeaLevelS should be {consistentSeaLevelIsp:F2} s "
                    + $"(declared {model.SpecificImpulseSeaLevelS:F2} s).");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>
    /// Every Starship/Super Heavy Raptor must satisfy the invariant, and the model must
    /// actually deliver one mass flow when evaluated at both ends of the pressure range —
    /// the rated numbers and the interpolated performance map have to agree.
    /// </summary>
    [Theory]
    [InlineData("raptor2-starship-sl-flight7")]
    [InlineData("raptor2-starship-vac-flight7")]
    [InlineData("raptor2-superheavy-flight7")]
    [InlineData("raptor3-starship-sl-flight12")]
    [InlineData("raptor3-starship-vac-flight12")]
    [InlineData("raptor3-superheavy-flight12")]
    public void StarshipRaptorsBurnTheSameMassFlowAtEveryAltitude(string modelId)
    {
        var model = LoadCatalog().Models[modelId];
        Assert.DoesNotContain(modelId, KnownInconsistentEngines.Keys);

        double vacuumFlow = EnginePerformanceEvaluator
            .Evaluate(model, 0.0, 1.0).MassFlowKgS;

        foreach (double pressure in new[] { 0.0, 25_000.0, 50_000.0, SeaLevelPressure })
        {
            var sample = EnginePerformanceEvaluator.Evaluate(model, pressure, 1.0);
            Assert.True(
                System.Math.Abs(sample.MassFlowKgS / vacuumFlow - 1.0) <= MassFlowTolerance,
                $"{modelId}: ṁ = {sample.MassFlowKgS:F3} kg/s at {pressure:F0} Pa versus "
                + $"{vacuumFlow:F3} kg/s in vacuum "
                + $"({100.0 * (sample.MassFlowKgS / vacuumFlow - 1.0):F3}% off).");
        }

        // Throttling a choked engine scales flow, it does not bend the invariant.
        var throttled = EnginePerformanceEvaluator.Evaluate(model, SeaLevelPressure, 0.4);
        Assert.True(
            System.Math.Abs(throttled.MassFlowKgS / (0.4 * vacuumFlow) - 1.0) <= MassFlowTolerance,
            $"{modelId}: ṁ at 40% throttle is {throttled.MassFlowKgS:F3} kg/s, expected "
            + $"{0.4 * vacuumFlow:F3} kg/s.");
    }

    /// <summary>
    /// The performance map is a redundant restatement of the rated points. Its full-throttle
    /// rows must reproduce the rated thrust and Isp exactly, and its partial-throttle rows
    /// must scale thrust linearly at a fixed Isp. A transcription slip here would make the
    /// interpolated engine disagree with the engine the catalog documents.
    /// </summary>
    [Fact]
    public void PerformanceMapAgreesWithTheRatedPointsItRestates()
    {
        var failures = new List<string>();

        foreach (var model in LoadCatalog().Models.Values.OrderBy(m => m.Id, StringComparer.Ordinal))
        {
            if (model.PerformanceMap.Count == 0) continue;

            foreach (var (pressure, ratedThrust, ratedIsp) in new[]
                     {
                         (0.0, model.RatedThrustVacuumN, model.SpecificImpulseVacuumS),
                         (SeaLevelPressure, model.RatedThrustSeaLevelN, model.SpecificImpulseSeaLevelS),
                     })
            {
                var rows = model.PerformanceMap
                    .Where(point => System.Math.Abs(point.AmbientPressurePa - pressure) < 1e-9)
                    .ToList();
                if (rows.Count == 0)
                {
                    failures.Add($"{model.Id}: performance map has no {pressure:F0} Pa rows.");
                    continue;
                }

                var rated = rows.FirstOrDefault(row => System.Math.Abs(row.Throttle - 1.0) < 1e-12);
                if (rated is null)
                {
                    failures.Add(
                        $"{model.Id}: performance map has no full-throttle row at {pressure:F0} Pa.");
                    continue;
                }

                if (System.Math.Abs(rated.ThrustN - ratedThrust) > 1e-6 * System.Math.Max(1.0, ratedThrust))
                    failures.Add(
                        $"{model.Id}: map thrust {rated.ThrustN:F3} N at {pressure:F0} Pa "
                        + $"disagrees with the rated {ratedThrust:F3} N.");
                if (System.Math.Abs(rated.SpecificImpulseS - ratedIsp) > 1e-9 * System.Math.Max(1.0, ratedIsp))
                    failures.Add(
                        $"{model.Id}: map Isp {rated.SpecificImpulseS:F4} s at {pressure:F0} Pa "
                        + $"disagrees with the rated {ratedIsp:F4} s.");

                foreach (var row in rows)
                {
                    double expected = rated.ThrustN * row.Throttle;
                    // 1% absorbs the rounded throttle fractions some legacy files use to
                    // express a published partial-thrust point (BE-4 at 0.3436, LM DPS at
                    // 0.10638); it is far tighter than any real throttle-dependent Isp shift.
                    if (System.Math.Abs(row.ThrustN - expected) > 0.01 * System.Math.Max(1.0, expected))
                        failures.Add(
                            $"{model.Id}: map thrust {row.ThrustN:F3} N at {pressure:F0} Pa / "
                            + $"{row.Throttle:P2} throttle is not {expected:F3} N.");
                    if (System.Math.Abs(row.SpecificImpulseS - rated.SpecificImpulseS) > 1e-9)
                        failures.Add(
                            $"{model.Id}: map Isp varies with throttle at {pressure:F0} Pa "
                            + $"({row.SpecificImpulseS:F4} s versus {rated.SpecificImpulseS:F4} s). "
                            + "Throttle-dependent Isp is not modelled yet.");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private static EngineDefinitionCatalog LoadCatalog()
    {
        string root = RepoRoot();
        return EngineDefinitionCatalog.Load(
            Path.Combine(root, "data", "engines"),
            Path.Combine(root, "data", "engine_clusters"),
            DataProvenanceRegistry.LoadFromDirectory(
                Path.Combine(root, "data", "provenance")));
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root.");
    }
}
