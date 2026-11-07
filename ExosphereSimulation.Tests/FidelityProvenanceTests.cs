namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Data;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Integrators;
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
        { "propellant-slosh-sixdof", "normalizedPivotBudgetPerDimension", Slosh6DofDynamics.NormalizedPivotBudgetPerDimension },
        { "starship-flight14-orbital-ignition", "maximumPointingErrorDegrees", Flight14OrbitalBurn.MaximumPointingErrorDegrees },
        { "starship-flight14-orbital-ignition", "maximumIgnitionPointingErrorDegrees", Flight14OrbitalBurn.MaximumIgnitionPointingErrorDegrees },
        { "starship-flight14-orbital-ignition", "maximumIgnitionAngularRateRadPerS", Flight14OrbitalBurn.MaximumIgnitionAngularRateRadPerS },
        { "starship-flight14-orbital-ignition", "maximumBurnAngularRateRadPerS", Flight14OrbitalBurn.MaximumBurnAngularRateRadPerS },
        { "propellant-slosh-cylinder", "firstModeRoot", PropellantSlosh.FirstModeRoot },
        { "propellant-slosh-cylinder", "shallowSeriesLimit", PropellantSlosh.ShallowSeriesLimit },
        { "rigid-body-coasting-integrator", "scaledOrientationErrorRatePerS", CoastingAttitudeIntegrator.ScaledOrientationErrorRatePerS },
        { "rigid-body-coasting-integrator", "angularRateErrorRatePerS", CoastingAttitudeIntegrator.AngularRateErrorRatePerS },
        { "rigid-body-coasting-integrator", "maximumTrialRotationRad", CoastingAttitudeIntegrator.MaximumTrialRotationRad },
        { "rigid-body-gravity-gradient", "tidalCoefficient", GravityGradient.TidalCoefficient },
        { "rigid-body-mass-flux", "uniformDiskSecondMomentFactor", MassOutflow.UniformDiskSecondMomentFactor },
        { "starship-flight14-attitude-feedback", "transverseSlowLoopOnsetSpeedMps", Flight14AerodynamicAttitude.TransverseSlowLoopOnsetSpeedMps },
        { "starship-flight14-attitude-feedback", "transverseSlowLoopFullSpeedMps", Flight14AerodynamicAttitude.TransverseSlowLoopFullSpeedMps },
        { "starship-flight14-attitude-feedback", "rollNaturalFrequencyRadPerS", Flight14AerodynamicAttitude.ScheduledNaturalFrequencyRadPerS },
        { "starship-flight14-attitude-feedback", "rollDampingRatio", Flight14AerodynamicAttitude.ScheduledDampingRatio },
        { "starship-flight14-attitude-feedback", "transverseNaturalFrequencyRadPerS", Flight14AerodynamicAttitude.TransverseNaturalFrequencyRadPerS },
        { "starship-flight14-attitude-feedback", "transverseDampingRatio", Flight14AerodynamicAttitude.TransverseDampingRatio },
        { "crossflow-drag-jorgensen", "subcriticalSectionCoefficient", CrossflowDrag.SectionCoefficient(0.1, 1.0e5) },
        { "crossflow-drag-jorgensen", "transcriticalSectionCoefficient", CrossflowDrag.SectionCoefficient(0.2, 5.0e7) },
        { "crossflow-drag-jorgensen", "transonicPeakSectionCoefficient", CrossflowDrag.SectionCoefficient(0.97, 1.0e7) },
        { "crossflow-drag-jorgensen", "hypersonicSectionCoefficient", CrossflowDrag.SectionCoefficient(12.0, 1.0e6) },
        { "crossflow-drag-jorgensen", "lowMachEtaFineness10", CrossflowDrag.Eta(0.0, 10.0) },
        { "sutherland-viscosity-air", "beta", new AtmosphereModel().SutherlandBeta },
        { "sutherland-viscosity-air", "sutherlandTemperature", new AtmosphereModel().SutherlandTemperature },
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
    public void DescentProfileMatchesItsRegisteredCalibration()
    {
        var profile = Flight14DescentDefinition.LoadFromJson(Path.Combine(Root.FullName,
            "data", "flight_profiles", "starship_flight14_descent_estimate.json"));
        var registry = Registry();
        Assert.Equal(profile.DownwardSpeedFraction,
            registry.Require("starship-flight14-descent", "downwardSpeedFraction").Value);
        Assert.Equal(profile.EntryAngleOfAttackDegrees,
            registry.Require("starship-flight14-descent", "entryAngleOfAttackDegrees").Value);
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

    [Fact]
    public void SloshReferenceRetainsTheMeasuredRangeAndItsRegisteredUncertainty()
    {
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Root.FullName,
            "data", "flight_profiles", "saturn_as501_slosh_reference.json")));
        var data = document.RootElement;
        string id = data.GetProperty("id").GetString()!;
        var registry = Registry();
        registry.Require(id, "frequencyCurve");
        foreach (var (jsonField, provenanceField) in new[]
        {
            ("relative_comparison_tolerance", "relativeComparisonTolerance"),
            ("graph_reading_uncertainty_hz", "graphReadingUncertaintyHz"),
            ("time_reading_uncertainty_s", "timeReadingUncertaintyS"),
        })
            Assert.Equal(registry.Require(id, provenanceField).Value!.Value, data.GetProperty(jsonField).GetDouble());
        double previous = -1;
        var times = new List<double>();
        foreach (var row in data.GetProperty("samples").EnumerateArray())
        {
            double time = row.GetProperty("range_time_s").GetDouble();
            Assert.True(double.IsFinite(time) && time > previous);
            foreach (string field in new[] { "lox_hz", "fuel_hz" })
            {
                double frequency = row.GetProperty(field).GetDouble();
                Assert.True(double.IsFinite(frequency) && frequency > 0);
            }
            times.Add(time); previous = time;
        }
        Assert.True(times.Count >= 4);
        Assert.Contains(130.0, times); // Keep the late falling measured branch in the gate.
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
