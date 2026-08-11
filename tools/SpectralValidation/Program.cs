using Exosphere.Simulation;

string repositoryRoot = FindRepositoryRoot();
string outputDirectory = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.Combine(Path.GetTempPath(), "exo_spectral_validation");
Directory.CreateDirectory(outputDirectory);

var options = new SpectralComparisonOptions
{
    OracleSampleCount = 16,
    LutWidth = 10,
    LutHeight = 8,
    LutIntegrationSteps = 12,
    LutSolarSamples = 10,
    BuildAngularAtlas = true,
    AngularWidth = 6,
    AngularSolarHeight = 6,
    AngularViewHeight = 6,
    AngularMuWidth = 6,
    AngularOpticalDepthSamples = 10,
};

var cases = new[]
{
    new SpectralEvaluationCoordinate(20.0, 45.0 * Math.PI / 180.0, 1.0, 0.5, "surface_day"),
    new SpectralEvaluationCoordinate(10_000.0, 12.0 * Math.PI / 180.0, 0.75, 0.2, "10km_day"),
    new SpectralEvaluationCoordinate(30_000.0, 2.0 * Math.PI / 180.0, 0.55, -0.2, "30km_terminator"),
    new SpectralEvaluationCoordinate(70_000.0, -0.01, 0.45, -0.4, "70km_twilight"),
    new SpectralEvaluationCoordinate(120_000.0, -35.0 * Math.PI / 180.0, 0.8, 0.0, "120km_night"),
    new SpectralEvaluationCoordinate(400_000.0, 45.0 * Math.PI / 180.0, 1.0, 0.0, "400km_day"),
    new SpectralEvaluationCoordinate(120_000.0, 35.0 * Math.PI / 180.0, 0.8, 0.7, "eclipse_partial"),
};

var bodyPaths = new[] { "earth", "mars", "venus" }
    .Select(id => Path.Combine(repositoryRoot, "data", "bodies", id + ".json"));
bool allFinite = true;
bool allMonotonic = true;
bool allOrder4NoWorse = true;
foreach (string bodyPath in bodyPaths)
{
    var body = CelestialBody.LoadFromJson(bodyPath);
    var bodyCases = cases.Where(coordinate =>
        coordinate.Altitude <= (body.Atmosphere?.MaxAltitude ?? 0.0)
        || coordinate.Altitude >= 300_000.0).ToArray();
    var report = SpectralAtmosphereComparator.Compare(body, bodyCases, options);
    string csvPath = Path.Combine(outputDirectory, $"{body.Id}-spectral-comparison.csv");
    File.WriteAllText(csvPath, report.ToCsv());
    Console.WriteLine(
        $"SPECTRAL body={body.Id} provenance={report.DataProvenance} "
        + $"samples={report.Samples.Count} abs3={report.MeanAbsoluteErrorOrder3:E4} "
        + $"abs4={report.MeanAbsoluteErrorOrder4:E4} rel4={report.MeanRelativeErrorOrder4:E4} "
        + $"chromatic4={report.MeanChromaticErrorOrder4:E4} "
        + $"finite={report.AllFiniteAndNonNegative} monotonic={report.AllOrdersMonotonic} "
        + $"order4NoWorse={report.Order4NotWorseThanOrder3} csv={csvPath}");
    allFinite &= report.AllFiniteAndNonNegative;
    allMonotonic &= report.AllOrdersMonotonic;
    allOrder4NoWorse &= report.Order4NotWorseThanOrder3;
}

Console.WriteLine(
    $"SPECTRAL_SUMMARY finite={allFinite} monotonic={allMonotonic} "
    + $"order4NoWorse={allOrder4NoWorse} officialOrder={SpectralAtmosphereOracle.OfficialRendererOrder} "
    + $"experimentalOrder={SpectralAtmosphereOracle.ExperimentalOrder} "
    + "decision=order4-official-order5-diagnostic");
if (!allFinite || !allMonotonic)
    Environment.ExitCode = 1;

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "ExosphereSimulation.sln")))
            return directory.FullName;
        directory = directory.Parent;
    }
    throw new InvalidOperationException("Could not locate the Exosphere repository root.");
}
