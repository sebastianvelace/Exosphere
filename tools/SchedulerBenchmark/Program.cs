using System.Diagnostics;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

string repositoryRoot = FindRepositoryRoot();
int samples = ReadIntOption(args, "--samples", 80);
int warmup = ReadIntOption(args, "--warmup", 10);
string? outputPath = ReadStringOption(args, "--out");
if (samples < 5 || warmup < 0)
    throw new ArgumentOutOfRangeException("samples/warmup", "samples must be >= 5 and warmup >= 0");

var scenarios = new[]
{
    new Scenario("full_single", () => BuildFullFleet(repositoryRoot, 1), 1.0, 0.02),
    new Scenario("full_fleet", () => BuildFullFleet(repositoryRoot, 4), 1.0, 0.02),
    new Scenario("rails_fleet", () => BuildRailsFleet(repositoryRoot, 32), 2_000.0, 0.02),
    new Scenario("mixed_fleet", () => BuildMixedFleet(repositoryRoot, 16), 100.0, 0.005),
};

var report = new List<string>
{
    "format_version=scheduler_phase6_v1",
    $"samples={samples}",
    $"warmup={warmup}",
    $"runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}",
};
bool allFinite = true;

foreach (var scenario in scenarios)
{
    ScenarioResult result = Measure(scenario, samples, warmup);
    report.AddRange(result.ToLines());
    allFinite &= result.Finite;
    Console.WriteLine(
        $"SCHEDULER scenario={result.Name} samples={result.SampleCount} "
        + $"p50_ms={result.P50Ms:F4} p95_ms={result.P95Ms:F4} p99_ms={result.P99Ms:F4} "
        + $"cpu_ms={result.ProcessCpuMs:F2} alloc_per_tick={result.ManagedBytesPerTick:F1} "
        + $"branch={result.Telemetry.Branch} work={result.Telemetry.TotalWorkDispatches} "
        + $"outer={result.Telemetry.OuterSubsteps} rails_slices={result.Telemetry.RailsSlices} "
        + $"finite={result.Finite}");
}

report.Add($"summary_finite={allFinite.ToString().ToLowerInvariant()}");
if (outputPath is null)
    Console.WriteLine(string.Join(Environment.NewLine, report));
else
{
    string absoluteOutput = Path.GetFullPath(outputPath);
    Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutput)!);
    File.WriteAllLines(absoluteOutput, report);
    Console.WriteLine($"SCHEDULER_REPORT path={absoluteOutput}");
}
if (!allFinite)
    Environment.ExitCode = 1;

static ScenarioResult Measure(Scenario scenario, int samples, int warmup)
{
    Universe universe = scenario.Build();
    universe.TimeScale = scenario.TimeScale;

    for (int i = 0; i < warmup; i++)
        universe.Tick(scenario.RealDeltaTime);

    var samplesMs = new double[samples];
    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    int gen0Before = GC.CollectionCount(0);
    int gen1Before = GC.CollectionCount(1);
    int gen2Before = GC.CollectionCount(2);
    Process process = Process.GetCurrentProcess();
    process.Refresh();
    TimeSpan cpuBefore = process.TotalProcessorTime;

    for (int i = 0; i < samples; i++)
    {
        long start = Stopwatch.GetTimestamp();
        universe.Tick(scenario.RealDeltaTime);
        long end = Stopwatch.GetTimestamp();
        samplesMs[i] = (end - start) * 1000.0 / Stopwatch.Frequency;
    }

    process.Refresh();
    TimeSpan cpuAfter = process.TotalProcessorTime;
    long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    var telemetry = universe.LastSchedulerTelemetry;
    bool finite = double.IsFinite(universe.CurrentTime)
        && telemetry.Branch != PhysicsSchedulerBranch.None
        && double.IsFinite(telemetry.SimulatedSeconds)
        && universe.Vessels.All(v => IsFinite(v.Position) && IsFinite(v.Velocity));

    Array.Sort(samplesMs);
    return new ScenarioResult(
        scenario.Name,
        samples,
        Percentile(samplesMs, 0.50),
        Percentile(samplesMs, 0.95),
        Percentile(samplesMs, 0.99),
        (cpuAfter - cpuBefore).TotalMilliseconds,
        allocated / (double)samples,
        GC.CollectionCount(0) - gen0Before,
        GC.CollectionCount(1) - gen1Before,
        GC.CollectionCount(2) - gen2Before,
        telemetry,
        finite);
}

static Universe BuildFullFleet(string root, int count)
{
    var earth = LoadBody(root, "earth");
    var universe = new Universe { ActiveVessel = null };
    universe.AddBody(earth);
    for (int i = 0; i < count; i++)
    {
        var vessel = CoastVessel(earth, $"full-{i}", 800_000.0 + i * 150_000.0);
        universe.AddVessel(vessel);
        if (i == 0) universe.ActiveVessel = vessel;
    }
    return universe;
}

static Universe BuildRailsFleet(string root, int count)
{
    var earth = LoadBody(root, "earth");
    var universe = new Universe();
    universe.AddBody(earth);
    for (int i = 0; i < count; i++)
    {
        var vessel = CoastVessel(earth, $"rails-{i}", 1_500_000.0 + i * 250_000.0);
        vessel.IsOnRails = true;
        universe.AddVessel(vessel);
    }
    return universe;
}

static Universe BuildMixedFleet(string root, int railCount)
{
    var earth = LoadBody(root, "earth");
    var universe = new Universe();
    universe.AddBody(earth);

    var active = CoastVessel(earth, "mixed-active", 1_000_000.0);
    universe.AddVessel(active);
    universe.ActiveVessel = active;

    var atmospheric = CoastVessel(earth, "mixed-atmosphere", 150_000.0);
    atmospheric.Velocity = earth.Velocity + Vector3d.Up * 7_700.0;
    universe.AddVessel(atmospheric);

    for (int i = 0; i < railCount; i++)
    {
        var vessel = CoastVessel(earth, $"mixed-rails-{i}", 1_500_000.0 + i * 250_000.0);
        vessel.IsOnRails = true;
        universe.AddVessel(vessel);
    }
    return universe;
}

static Vessel CoastVessel(CelestialBody earth, string id, double altitude)
{
    double orbitalRadius = earth.Radius + altitude;
    var vessel = new Vessel(id)
    {
        Position = earth.Position + Vector3d.Right * orbitalRadius,
        Velocity = earth.Velocity + Vector3d.Up * System.Math.Sqrt(earth.GM / orbitalRadius),
        ReferenceBodyId = earth.Id,
        SASEnabled = false,
    };
    vessel.Parts.SetRoot(new Part(new PartDefinition
    {
        Id = "scheduler-benchmark",
        CategoryStr = "command",
        MassDry = 1_000.0,
        LengthM = 5.0,
        DiameterM = 2.0,
    }));
    return vessel;
}

static CelestialBody LoadBody(string root, string id) =>
    CelestialBody.LoadFromJson(Path.Combine(root, "data", "bodies", id + ".json"));

static bool IsFinite(Vector3d value) =>
    double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);

static double Percentile(double[] sortedValues, double percentile)
{
    int index = System.Math.Clamp(
        (int)System.Math.Ceiling(sortedValues.Length * percentile) - 1,
        0,
        sortedValues.Length - 1);
    return sortedValues[index];
}

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

static int ReadIntOption(string[] arguments, string name, int fallback)
{
    string? value = ReadStringOption(arguments, name);
    return value is null ? fallback : int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}

static string? ReadStringOption(string[] arguments, string name)
{
    string prefix = name + "=";
    string? inline = arguments.FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.Ordinal));
    if (inline is not null) return inline[prefix.Length..];
    int index = Array.IndexOf(arguments, name);
    return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
}

record Scenario(string Name, Func<Universe> Build, double TimeScale, double RealDeltaTime);

record ScenarioResult(
    string Name,
    int SampleCount,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double ProcessCpuMs,
    double ManagedBytesPerTick,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    PhysicsSchedulerTelemetry Telemetry,
    bool Finite)
{
    public IEnumerable<string> ToLines()
    {
        yield return $"scenario={Name}";
        yield return $"{Name}.sample_count={SampleCount}";
        yield return $"{Name}.tick_ms_p50={P50Ms:F6}";
        yield return $"{Name}.tick_ms_p95={P95Ms:F6}";
        yield return $"{Name}.tick_ms_p99={P99Ms:F6}";
        yield return $"{Name}.process_cpu_ms={ProcessCpuMs:F6}";
        yield return $"{Name}.managed_alloc_bytes_per_tick={ManagedBytesPerTick:F3}";
        yield return $"{Name}.gc_gen0={Gen0Collections}";
        yield return $"{Name}.gc_gen1={Gen1Collections}";
        yield return $"{Name}.gc_gen2={Gen2Collections}";
        yield return $"{Name}.branch={Telemetry.Branch}";
        yield return $"{Name}.simulated_seconds={Telemetry.SimulatedSeconds:F6}";
        yield return $"{Name}.effective_step_cap={Telemetry.EffectiveStepCap:F6}";
        yield return $"{Name}.outer_substeps={Telemetry.OuterSubsteps}";
        yield return $"{Name}.full_physics_dispatches={Telemetry.FullPhysicsDispatches}";
        yield return $"{Name}.on_rails_dispatches={Telemetry.OnRailsDispatches}";
        yield return $"{Name}.rails_slices={Telemetry.RailsSlices}";
        yield return $"{Name}.docked_secondary_skips={Telemetry.DockedSecondarySkips}";
        yield return $"{Name}.docking_constraint_applications={Telemetry.DockingConstraintApplications}";
        yield return $"{Name}.finite={Finite.ToString().ToLowerInvariant()}";
    }
}
