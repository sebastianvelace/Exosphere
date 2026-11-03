namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Data;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Xunit;

/// <summary>
/// Sources and estimates stay separate: every model constant of the fidelity track is
/// registered with its status and source, and the code must hold exactly the value its
/// record vouches for. A part may only declare a fidelity field with a provenance record.
/// </summary>
public sealed class FidelityProvenanceTests
{
    private static readonly DirectoryInfo Root = FindRepoRoot();

    private static DataProvenanceRegistry Registry() =>
        DataProvenanceRegistry.LoadFromDirectory(Path.Combine(Root.FullName, "data", "provenance"));

    public static TheoryData<string, string, double> CodeConstants() => new()
    {
        { "distributed-aerodynamics", "crossflowDragCoefficient", DistributedAerodynamics.CrossflowDragCoefficient },
        { "distributed-aerodynamics", "ogivePlanformFraction", DistributedAerodynamics.OgivePlanformFraction },
        { "distributed-aerodynamics", "ogiveCenterOfPressureFraction", DistributedAerodynamics.OgiveCenterOfPressureFraction },
        { "distributed-aerodynamics", "slenderBodyMinimumFineness", PartGraph.SlenderBodyMinimumFineness },
        { "body-flap-aerodynamics", "subsonicBroadsideCoefficient", BodyFlapAerodynamics.SubsonicBroadsideCoefficient },
        { "body-flap-aerodynamics", "subsonicMachLimit", BodyFlapAerodynamics.SubsonicMachLimit },
        { "body-flap-aerodynamics", "newtonianMachOnset", BodyFlapAerodynamics.NewtonianMachOnset },
        { "mercury-atlas6-pitch-tape", "boosterCutoffElevationDeg", MercuryAtlasFlightProfile.BoosterCutoffElevationDegrees },
        { "mercury-atlas6-pitch-tape", "insertionElevationDeg", MercuryAtlasFlightProfile.InsertionElevationDegrees },
    };

    [Theory]
    [MemberData(nameof(CodeConstants))]
    public void CodeConstantMatchesItsProvenanceRecord(string dataId, string field, double codeValue)
    {
        var record = Registry().Require(dataId, field);
        Assert.NotNull(record.Value);
        Assert.Equal(record.Value!.Value, codeValue, 12);
        if (record.Status != ProvenanceStatus.Published)
            Assert.False(string.IsNullOrWhiteSpace(record.Uncertainty));
    }

    [Fact]
    public void EveryFidelityFieldInThePartCatalogCitesItsSource()
    {
        var registry = Registry();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(Root.FullName, "data", "parts"));
        int checkedFields = 0;
        foreach (var part in catalog.AllParts)
        {
            if (part.NoseLengthM > 0.0)
            {
                var record = registry.Require(part.Id, "noseLengthM");
                Assert.Equal(record.Value!.Value, part.NoseLengthM, 12);
                checkedFields++;
            }
            if (part.BodyFlaps.Count > 0)
            {
                registry.Require(part.Id, "bodyFlaps");
                checkedFields++;
            }
        }
        Assert.True(checkedFields >= 11, $"expected the fidelity fields of the catalogue, saw {checkedFields}");
    }

    [Fact]
    public void AFidelityFieldWithoutProvenanceIsRejected()
    {
        var data = Directory.CreateTempSubdirectory("exosphere-provenance-");
        try
        {
            foreach (var directory in new[] { "parts", "engines", "engine_clusters", "provenance" })
                Directory.CreateDirectory(Path.Combine(data.FullName, directory));
            File.WriteAllText(Path.Combine(data.FullName, "parts", "nose.json"), """
                {"id": "unsourced_nose", "name": "Nose", "category": "structure", "mass_dry": 100,
                 "length_m": 4, "diameter_m": 2, "nose_length_m": 3, "attachment_nodes": []}
                """);
            File.WriteAllText(Path.Combine(data.FullName, "provenance", "empty.json"),
                """{"manifestId": "empty", "records": []}""");
            var error = Assert.Throws<InvalidDataException>(() =>
                PartCatalog.LoadFromDirectory(Path.Combine(data.FullName, "parts")));
            Assert.Contains("unsourced_nose.noseLengthM", error.Message);
        }
        finally
        {
            data.Delete(recursive: true);
        }
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
