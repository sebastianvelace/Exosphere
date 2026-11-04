namespace ExosphereSimulation.Tests;

using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// Enforces docs/physics/fidelity_gates.md: a closed fidelity phase must have at least one
/// analytic and one historical gate, every listed gate must be a real test carrying the
/// matching traits, and every tagged test must be listed.
/// </summary>
public sealed partial class FidelityGateRegistryTests
{
    private const string GateTrait = "FidelityGate";
    private const string KindTrait = "GateKind";
    private static readonly string[] Kinds = ["analytic", "historical"];

    private sealed record GateRow(string Phase, string Kind, string Test);

    [Fact]
    public void EveryClosedPhaseHasAnalyticAndHistoricalGates()
    {
        var (phases, gates) = ReadRegistry();
        Assert.NotEmpty(phases);
        foreach (var (phase, status) in phases)
        {
            Assert.Contains(status, new[] { "closed", "open", "planned" });
            if (status != "closed") continue;
            foreach (string kind in Kinds)
                Assert.True(gates.Any(g => g.Phase == phase && g.Kind == kind),
                    $"closed phase {phase} has no {kind} gate");
        }
        foreach (var gate in gates)
            Assert.True(phases.ContainsKey(gate.Phase), $"gate {gate.Test} names unknown phase {gate.Phase}");
    }

    [Fact]
    public void RegistryAndTestTraitsAgree()
    {
        var (_, rows) = ReadRegistry();
        var tagged = TaggedTests();
        foreach (var row in rows)
        {
            Assert.True(tagged.TryGetValue(row.Test, out var traits), $"{row.Test} is not a tagged test");
            Assert.Contains((row.Phase, row.Kind), traits);
        }
        foreach (var (test, traits) in tagged)
            foreach (var (phase, kind) in traits)
                Assert.True(rows.Any(r => r.Test == test && r.Phase == phase && r.Kind == kind),
                    $"{test} is tagged {phase}/{kind} but missing from fidelity_gates.md");
    }

    private static Dictionary<string, List<(string Phase, string Kind)>> TaggedTests()
    {
        var result = new Dictionary<string, List<(string, string)>>(StringComparer.Ordinal);
        foreach (var type in typeof(FidelityGateRegistryTests).Assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttribute<FactAttribute>() == null) continue; // TheoryAttribute derives from it.
                var attributes = method.GetCustomAttributesData().Concat(type.GetCustomAttributesData())
                    .Where(a => a.AttributeType == typeof(TraitAttribute))
                    .Select(a => ((string)a.ConstructorArguments[0].Value!, (string)a.ConstructorArguments[1].Value!))
                    .ToList();
                var phases = attributes.Where(a => a.Item1 == GateTrait).Select(a => a.Item2).ToList();
                var kinds = attributes.Where(a => a.Item1 == KindTrait).Select(a => a.Item2).ToList();
                if (phases.Count == 0 && kinds.Count == 0) continue;
                string name = $"{type.Name}.{method.Name}";
                Assert.True(phases.Count == 1 && kinds.Count == 1,
                    $"{name} must carry exactly one {GateTrait} and one {KindTrait} trait");
                Assert.Contains(kinds[0], Kinds);
                result[name] = [(phases[0], kinds[0])];
            }
        }
        return result;
    }

    private static (Dictionary<string, string> Phases, List<GateRow> Gates) ReadRegistry()
    {
        string path = Path.Combine(FindRepoRoot().FullName, "docs", "physics", "fidelity_gates.md");
        var phases = new Dictionary<string, string>(StringComparer.Ordinal);
        var gates = new List<GateRow>();
        foreach (string line in File.ReadLines(path))
        {
            var phase = PhaseRow().Match(line);
            if (phase.Success)
            {
                Assert.True(phases.TryAdd(phase.Groups[1].Value, phase.Groups[2].Value), $"duplicate phase row {line}");
                continue;
            }
            var gate = GateRowPattern().Match(line);
            if (gate.Success)
                gates.Add(new GateRow(gate.Groups[1].Value, gate.Groups[2].Value, gate.Groups[3].Value));
        }
        Assert.Equal(gates.Count, gates.Select(g => (g.Phase, g.Test)).Distinct().Count());
        return (phases, gates);
    }

    [GeneratedRegex(@"^\| (F\d) \| (closed|open|planned) \|")]
    private static partial Regex PhaseRow();

    [GeneratedRegex(@"^\| (F\d) \| (analytic|historical) \| `([A-Za-z0-9_]+\.[A-Za-z0-9_]+)` \|")]
    private static partial Regex GateRowPattern();

    private static DirectoryInfo FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
