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
    new Scenario(
        "wake_catchup",
        () => BuildWakeCatchUpUniverse(repositoryRoot),
        100.0,
        0.005,
        BeforeTick: (universe, sample) =>
        {
            if (sample == samples / 2)
                universe.Vessels.Single(vessel => vessel.Id == "wake-rails").Throttle = 0.1;
        },
        RequiresCatchUp: true),
};

var report = new List<string>
{
    "format_version=scheduler_phase23_v1",
    $"samples={samples}",
    $"warmup={warmup}",
    $"runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}",
};
bool allFinite = true;
bool allValid = true;

foreach (var scenario in scenarios)
{
    ScenarioResult result = Measure(scenario, samples, warmup);
    AllocationBreakdown breakdown = MeasureAllocationBreakdown(
        repositoryRoot,
        scenario,
        samples,
        warmup);
    report.AddRange(result.ToLines());
    report.AddRange(breakdown.ToLines());
    allFinite &= result.Finite;
    allValid &= result.Valid && breakdown.Valid;
    Console.WriteLine(
        $"SCHEDULER scenario={result.Name} samples={result.SampleCount} "
        + $"p50_ms={result.P50Ms:F4} p95_ms={result.P95Ms:F4} p99_ms={result.P99Ms:F4} "
        + $"cpu_ms={result.ProcessCpuMs:F2} alloc_per_tick={result.ManagedBytesPerTick:F1} "
        + $"dispatches_per_tick={result.Totals.TotalWorkDispatches / (double)result.SampleCount:F3} "
        + $"projections_per_tick={result.Totals.DeadlineProjectedDispatches / (double)result.SampleCount:F3} "
        + $"catchup_per_tick={result.Totals.DeadlineCatchUpDispatches / (double)result.SampleCount:F3} "
        + $"event_contract={result.EventContractPass} finite={result.Finite}");
    Console.WriteLine(
        $"ALLOCATIONS scenario={result.Name} "
        + $"vessel_tick_status={breakdown.VesselTick.Status} "
        + $"vessel_tick_bytes={breakdown.VesselTick.ManagedBytesPerOperation:F1} "
        + $"flight7_tick_bytes={breakdown.Flight7VesselTick.ManagedBytesPerOperation:F1} "
        + $"telemetry_snapshot_bytes={breakdown.TelemetrySnapshot.ManagedBytesPerOperation:F1} "
        + $"scheduler_empty_bytes={breakdown.SchedulerEmpty.ManagedBytesPerOperation:F1} "
        + $"engine_snapshot_bytes={breakdown.EngineSnapshot.ManagedBytesPerOperation:F1} "
        + $"valid={breakdown.Valid}");
}

report.Add($"summary_finite={allFinite.ToString().ToLowerInvariant()}");
report.Add($"summary_valid={allValid.ToString().ToLowerInvariant()}");
if (outputPath is null)
    Console.WriteLine(string.Join(Environment.NewLine, report));
else
{
    string absoluteOutput = Path.GetFullPath(outputPath);
    Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutput)!);
    File.WriteAllLines(absoluteOutput, report);
    Console.WriteLine($"SCHEDULER_REPORT path={absoluteOutput}");
}
if (!allValid)
    Environment.ExitCode = 1;

static ScenarioResult Measure(Scenario scenario, int samples, int warmup)
{
    Universe universe = scenario.Build();
    universe.TimeScale = scenario.TimeScale;

    for (int i = 0; i < warmup; i++)
        universe.Tick(scenario.RealDeltaTime);

    StabilizeManagedRuntime();
    var samplesMs = new double[samples];
    Process process = Process.GetCurrentProcess();
    process.Refresh();
    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    int gen0Before = GC.CollectionCount(0);
    int gen1Before = GC.CollectionCount(1);
    int gen2Before = GC.CollectionCount(2);
    TimeSpan cpuBefore = process.TotalProcessorTime;
    var totals = new TelemetryTotalsBuilder();

    for (int i = 0; i < samples; i++)
    {
        scenario.BeforeTick?.Invoke(universe, i);
        long start = Stopwatch.GetTimestamp();
        universe.Tick(scenario.RealDeltaTime);
        long end = Stopwatch.GetTimestamp();
        samplesMs[i] = (end - start) * 1000.0 / Stopwatch.Frequency;
        totals.Add(universe.LastSchedulerTelemetry);
    }

    process.Refresh();
    TimeSpan cpuAfter = process.TotalProcessorTime;
    long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    var telemetry = universe.LastSchedulerTelemetry;
    bool finite = double.IsFinite(universe.CurrentTime)
        && telemetry.Branch != PhysicsSchedulerBranch.None
        && double.IsFinite(telemetry.SimulatedSeconds)
        && universe.Vessels.All(v => IsFinite(v.Position) && IsFinite(v.Velocity));
    TelemetryTotals aggregate = totals.Build();
    bool eventContractPass = !scenario.RequiresCatchUp
        || aggregate.DeadlineCatchUpDispatches > 0;

    double standardDeviationMs = StandardDeviation(samplesMs);
    Array.Sort(samplesMs);
    return new ScenarioResult(
        scenario.Name,
        samples,
        Percentile(samplesMs, 0.50),
        Percentile(samplesMs, 0.95),
        Percentile(samplesMs, 0.99),
        (cpuAfter - cpuBefore).TotalMilliseconds,
        allocated / (double)samples,
        standardDeviationMs,
        GC.CollectionCount(0) - gen0Before,
        GC.CollectionCount(1) - gen1Before,
        GC.CollectionCount(2) - gen2Before,
        telemetry,
        aggregate,
        finite,
        eventContractPass,
        finite && eventContractPass);
}

static AllocationBreakdown MeasureAllocationBreakdown(
    string repositoryRoot,
    Scenario scenario,
    int samples,
    int warmup)
{
    AllocationMeasurement vesselTick = MeasureDirectVesselTick(
        scenario,
        samples,
        warmup);
    AllocationMeasurement flight7VesselTick = scenario.Name == "full_single"
        ? MeasureFlight7VesselTick(repositoryRoot, samples, warmup)
        : AllocationMeasurement.NotApplicable("flight7_vessel_tick");
    AllocationMeasurement telemetrySnapshot = MeasureTelemetrySnapshot(
        scenario,
        samples,
        warmup);
    AllocationMeasurement schedulerEmpty = MeasureEmptyScheduler(
        scenario,
        samples,
        warmup);
    // Engine readouts are a presentation snapshot, not a scheduler workload. Measure the
    // real Flight 7 buffer once and mark it N/A for the fleet rows to avoid duplicating the
    // same instrumentation fixture five times.
    AllocationMeasurement engineSnapshot = scenario.Name == "full_single"
        ? MeasureEngineTelemetrySnapshot(repositoryRoot, samples, warmup)
        : AllocationMeasurement.NotApplicable("engine_readout_snapshot");
    return new AllocationBreakdown(
        scenario.Name,
        vesselTick,
        flight7VesselTick,
        telemetrySnapshot,
        schedulerEmpty,
        engineSnapshot);
}

static AllocationMeasurement MeasureEmptyScheduler(
    Scenario scenario,
    int samples,
    int warmup)
{
    Universe universe = new() { TimeScale = scenario.TimeScale };
    for (int i = 0; i < warmup; i++)
        universe.Tick(scenario.RealDeltaTime);

    StabilizeManagedRuntime();
    return MeasureOperations(
        "scheduler_empty",
        samples,
        1,
        _ => universe.Tick(scenario.RealDeltaTime),
        () => double.IsFinite(universe.CurrentTime)
            && universe.LastSchedulerTelemetry.Branch != PhysicsSchedulerBranch.None);
}

static AllocationMeasurement MeasureFlight7VesselTick(
    string repositoryRoot,
    int samples,
    int warmup)
{
    CelestialBody earth = LoadBody(repositoryRoot, "earth");
    Vessel vessel = BuildFlight7Stack(repositoryRoot);
    vessel.Position = earth.Position + Vector3d.Right * (earth.Radius + 100_000.0);
    vessel.PitchYawRoll = new Vector3d(0.35, -0.2, 0.15);
    vessel.Throttle = 1.0;
    for (int i = 0; i < System.Math.Max(100, warmup); i++)
        vessel.Tick(0.02, earth);

    StabilizeManagedRuntime();
    return MeasureOperations(
        "flight7_vessel_tick",
        samples,
        1,
        _ => vessel.Tick(0.02, earth),
        () => double.IsFinite(vessel.TotalMass)
            && IsFinite(vessel.Position)
            && IsFinite(vessel.Velocity)
            && vessel.Parts.Parts.All(part => double.IsFinite(part.LiquidFuel)));
}

static AllocationMeasurement MeasureDirectVesselTick(
    Scenario scenario,
    int samples,
    int warmup)
{
    Universe universe = scenario.Build();
    universe.TimeScale = scenario.TimeScale;
    CelestialBody body = universe.Bodies[0];
    Vessel[] vessels = universe.Vessels
        .Where(vessel => !vessel.IsOnRails && !vessel.IsDestroyed)
        .ToArray();
    if (vessels.Length == 0)
        return AllocationMeasurement.NotApplicable("vessel_tick");

    for (int i = 0; i < warmup; i++)
    {
        scenario.BeforeTick?.Invoke(universe, i);
        foreach (Vessel vessel in vessels)
            vessel.Tick(scenario.RealDeltaTime, body);
    }

    StabilizeManagedRuntime();
    return MeasureOperations(
        "vessel_tick",
        samples,
        vessels.Length,
        sample =>
        {
            scenario.BeforeTick?.Invoke(universe, sample);
            foreach (Vessel vessel in vessels)
                vessel.Tick(scenario.RealDeltaTime, body);
        },
        () => vessels.All(vessel => IsFinite(vessel.Position) && IsFinite(vessel.Velocity)));
}

static AllocationMeasurement MeasureTelemetrySnapshot(
    Scenario scenario,
    int samples,
    int warmup)
{
    Universe universe = scenario.Build();
    universe.TimeScale = scenario.TimeScale;
    for (int i = 0; i < warmup; i++)
        universe.Tick(scenario.RealDeltaTime);

    const int readsPerSample = 4_096;
    for (int i = 0; i < 8; i++)
        ReadTelemetrySnapshot(universe, readsPerSample);
    StabilizeManagedRuntime();
    return MeasureOperations(
        "scheduler_telemetry_snapshot",
        samples,
        readsPerSample,
        _ => ReadTelemetrySnapshot(universe, readsPerSample),
        () => universe.LastSchedulerTelemetry.Branch != PhysicsSchedulerBranch.None);
}

static void ReadTelemetrySnapshot(Universe universe, int reads)
{
    int checksum = 0;
    for (int i = 0; i < reads; i++)
    {
        PhysicsSchedulerTelemetry telemetry = universe.LastSchedulerTelemetry;
        checksum = unchecked(
            checksum * 31
            + telemetry.FullPhysicsDispatches
            + telemetry.OnRailsDispatches
            + telemetry.DeadlineCatchUpDispatches);
    }
    AllocationBenchmarkSink.TelemetryChecksum = checksum;
}

static AllocationMeasurement MeasureEngineTelemetrySnapshot(
    string repositoryRoot,
    int samples,
    int warmup)
{
    CelestialBody earth = LoadBody(repositoryRoot, "earth");
    Vessel vessel = BuildFlight7Stack(repositoryRoot);
    vessel.Position = earth.Position + Vector3d.Right * (earth.Radius + 100_000.0);
    vessel.Throttle = 1.0;
    for (int i = 0; i < System.Math.Max(100, warmup); i++)
        vessel.Tick(0.02, earth);

    var buffer = new List<EngineReadout>(39);
    for (int i = 0; i < 8; i++)
        vessel.FillEngineReadouts(earth, buffer);

    StabilizeManagedRuntime();
    return MeasureOperations(
        "engine_readout_snapshot",
        samples,
        1,
        _ =>
        {
            vessel.FillEngineReadouts(earth, buffer);
            AllocationBenchmarkSink.EngineRowCount = buffer.Count;
        },
        () => buffer.Count > 0
            && buffer.All(row => double.IsFinite(row.ThrustN)
                && double.IsFinite(row.MassFlowKgS)));
}

static AllocationMeasurement MeasureOperations(
    string name,
    int samples,
    int operationsPerSample,
    Action<int> operation,
    Func<bool> finite)
{
    // Warm the delegate/JIT/static sink and let the GC settle before the allocation counter.
    // Without this, a one-time type/JIT allocation is incorrectly charged to every sample.
    for (int i = 0; i < 8; i++)
        operation(-1);
    StabilizeManagedRuntime();
    var samplesMs = new double[samples];
    Process process = Process.GetCurrentProcess();
    process.Refresh();
    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    int gen0Before = GC.CollectionCount(0);
    int gen1Before = GC.CollectionCount(1);
    int gen2Before = GC.CollectionCount(2);
    TimeSpan cpuBefore = process.TotalProcessorTime;

    for (int i = 0; i < samples; i++)
    {
        long start = Stopwatch.GetTimestamp();
        operation(i);
        long end = Stopwatch.GetTimestamp();
        samplesMs[i] = (end - start) * 1000.0 / Stopwatch.Frequency
            / operationsPerSample;
    }

    process.Refresh();
    TimeSpan cpuAfter = process.TotalProcessorTime;
    long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    double standardDeviationMs = StandardDeviation(samplesMs);
    Array.Sort(samplesMs);
    long operationCount = (long)samples * operationsPerSample;
    bool finiteState = finite()
        && samplesMs.All(double.IsFinite)
        && allocated >= 0;
    return new AllocationMeasurement(
        name,
        "PASS",
        samples,
        operationCount,
        operationsPerSample,
        Percentile(samplesMs, 0.50),
        Percentile(samplesMs, 0.95),
        Percentile(samplesMs, 0.99),
        standardDeviationMs,
        (cpuAfter - cpuBefore).TotalMilliseconds,
        allocated / (double)operationCount,
        GC.CollectionCount(0) - gen0Before,
        GC.CollectionCount(1) - gen1Before,
        GC.CollectionCount(2) - gen2Before,
        finiteState,
        finiteState);
}

static void StabilizeManagedRuntime()
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
}

static double StandardDeviation(double[] values)
{
    if (values.Length == 0) return 0.0;
    double mean = values.Average();
    double variance = values
        .Select(value => (value - mean) * (value - mean))
        .Average();
    return System.Math.Sqrt(variance);
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

static Universe BuildWakeCatchUpUniverse(string root)
{
    var earth = LoadBody(root, "earth");
    var universe = new Universe { TimeScale = 100.0 };
    universe.AddBody(earth);

    // Keep the active vessel force-sensitive so the secondary rail vessel can be
    // projected for several samples before the benchmark injects a throttle wake-up.
    var active = CoastVessel(earth, "wake-active", 10_000.0);
    universe.AddVessel(active);
    universe.ActiveVessel = active;

    var rails = CoastVessel(earth, "wake-rails", 1_500_000.0);
    rails.IsOnRails = true;
    universe.AddVessel(rails);
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

static Vessel BuildFlight7Stack(string root)
{
    var defs = PartDefinition.LoadAllFromDirectory(Path.Combine(root, "data", "parts"));
    var command = new Part(defs["starship_command"]);
    var tank = new Part(defs["starship_tank"]);
    var engines = new Part(defs["starship_engines"]);
    var ring = new Part(defs["decoupler_heavy"]);
    var booster = new Part(defs["super_heavy_booster"]);

    var vessel = new Vessel("allocation-flight7");
    vessel.Parts.SetRoot(command);
    vessel.Parts.AddJoint(new Joint(command, tank, "bottom", "top"));
    vessel.Parts.AddJoint(new Joint(tank, engines, "bottom", "top"));
    vessel.Parts.AddJoint(new Joint(engines, ring, "bottom", "top"));
    vessel.Parts.AddJoint(new Joint(ring, booster, "bottom", "top"));
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

record Scenario(
    string Name,
    Func<Universe> Build,
    double TimeScale,
    double RealDeltaTime,
    Action<Universe, int>? BeforeTick = null,
    bool RequiresCatchUp = false);

record AllocationBreakdown(
    string ScenarioName,
    AllocationMeasurement VesselTick,
    AllocationMeasurement Flight7VesselTick,
    AllocationMeasurement TelemetrySnapshot,
    AllocationMeasurement SchedulerEmpty,
    AllocationMeasurement EngineSnapshot)
{
    public bool Valid => VesselTick.Valid
        && Flight7VesselTick.Valid
        && TelemetrySnapshot.Valid
        && SchedulerEmpty.Valid
        && EngineSnapshot.Valid;

    public IEnumerable<string> ToLines()
    {
        yield return $"allocation_scenario={ScenarioName}";
        foreach (string line in VesselTick.ToLines($"{ScenarioName}.vessel_tick"))
            yield return line;
        foreach (string line in Flight7VesselTick.ToLines(
                     $"{ScenarioName}.flight7_vessel_tick"))
            yield return line;
        foreach (string line in TelemetrySnapshot.ToLines(
                     $"{ScenarioName}.scheduler_telemetry_snapshot"))
            yield return line;
        foreach (string line in SchedulerEmpty.ToLines(
                     $"{ScenarioName}.scheduler_empty"))
            yield return line;
        foreach (string line in EngineSnapshot.ToLines(
                     $"{ScenarioName}.engine_readout_snapshot"))
            yield return line;
        yield return $"{ScenarioName}.allocation_valid={Valid.ToString().ToLowerInvariant()}";
    }
}

record AllocationMeasurement(
    string Name,
    string Status,
    int SampleCount,
    long OperationCount,
    int OperationsPerSample,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double StandardDeviationMs,
    double ProcessCpuMs,
    double ManagedBytesPerOperation,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    bool Finite,
    bool Valid)
{
    public double CoefficientOfVariationPercent => P50Ms > 0.0
        ? StandardDeviationMs / P50Ms * 100.0
        : 0.0;

    public static AllocationMeasurement NotApplicable(string name) => new(
        name,
        "NOT_APPLICABLE",
        0,
        0,
        0,
        0.0,
        0.0,
        0.0,
        0.0,
        0.0,
        0.0,
        0,
        0,
        0,
        true,
        true);

    public IEnumerable<string> ToLines(string prefix)
    {
        yield return $"{prefix}.status={Status}";
        yield return $"{prefix}.sample_count={SampleCount}";
        yield return $"{prefix}.operation_count={OperationCount}";
        yield return $"{prefix}.operations_per_sample={OperationsPerSample}";
        yield return $"{prefix}.ms_p50={P50Ms:F6}";
        yield return $"{prefix}.ms_p95={P95Ms:F6}";
        yield return $"{prefix}.ms_p99={P99Ms:F6}";
        yield return $"{prefix}.ms_stddev={StandardDeviationMs:F6}";
        yield return $"{prefix}.ms_cv_percent={CoefficientOfVariationPercent:F3}";
        yield return $"{prefix}.process_cpu_ms={ProcessCpuMs:F6}";
        yield return $"{prefix}.managed_alloc_bytes_per_operation={ManagedBytesPerOperation:F3}";
        yield return $"{prefix}.gc_gen0={Gen0Collections}";
        yield return $"{prefix}.gc_gen1={Gen1Collections}";
        yield return $"{prefix}.gc_gen2={Gen2Collections}";
        yield return $"{prefix}.finite={Finite.ToString().ToLowerInvariant()}";
        yield return $"{prefix}.valid={Valid.ToString().ToLowerInvariant()}";
    }
}

record ScenarioResult(
    string Name,
    int SampleCount,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double ProcessCpuMs,
    double ManagedBytesPerTick,
    double StandardDeviationMs,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    PhysicsSchedulerTelemetry Telemetry,
    TelemetryTotals Totals,
    bool Finite,
    bool EventContractPass,
    bool Valid)
{
    public double CoefficientOfVariationPercent => P50Ms > 0.0
        ? StandardDeviationMs / P50Ms * 100.0
        : 0.0;

    public IEnumerable<string> ToLines()
    {
        yield return $"scenario={Name}";
        yield return $"{Name}.sample_count={SampleCount}";
        yield return $"{Name}.tick_ms_p50={P50Ms:F6}";
        yield return $"{Name}.tick_ms_p95={P95Ms:F6}";
        yield return $"{Name}.tick_ms_p99={P99Ms:F6}";
        yield return $"{Name}.tick_ms_stddev={StandardDeviationMs:F6}";
        yield return $"{Name}.tick_ms_cv_percent={CoefficientOfVariationPercent:F3}";
        yield return $"{Name}.process_cpu_ms={ProcessCpuMs:F6}";
        yield return $"{Name}.managed_alloc_bytes_per_tick={ManagedBytesPerTick:F3}";
        yield return $"{Name}.gc_gen0={Gen0Collections}";
        yield return $"{Name}.gc_gen1={Gen1Collections}";
        yield return $"{Name}.gc_gen2={Gen2Collections}";
        yield return $"{Name}.sample_window_dispatches={Totals.TotalWorkDispatches}";
        yield return $"{Name}.sample_window_full_physics_dispatches={Totals.FullPhysicsDispatches}";
        yield return $"{Name}.sample_window_on_rails_dispatches={Totals.OnRailsDispatches}";
        yield return $"{Name}.sample_window_outer_substeps={Totals.OuterSubsteps}";
        yield return $"{Name}.sample_window_rails_slices={Totals.RailsSlices}";
        yield return $"{Name}.sample_window_deadline_eligible={Totals.DeadlineEligibleEvaluations}";
        yield return $"{Name}.sample_window_deadline_skips={Totals.DeadlineDeferredSkips}";
        yield return $"{Name}.sample_window_deadline_projections={Totals.DeadlineProjectedDispatches}";
        yield return $"{Name}.sample_window_deadline_catchup={Totals.DeadlineCatchUpDispatches}";
        yield return $"{Name}.sample_window_docked_secondary_skips={Totals.DockedSecondarySkips}";
        yield return $"{Name}.sample_window_docking_constraints={Totals.DockingConstraintApplications}";
        yield return $"{Name}.dispatches_per_tick={Totals.TotalWorkDispatches / (double)SampleCount:F6}";
        yield return $"{Name}.projections_per_tick={Totals.DeadlineProjectedDispatches / (double)SampleCount:F6}";
        yield return $"{Name}.catchup_per_tick={Totals.DeadlineCatchUpDispatches / (double)SampleCount:F6}";
        yield return $"{Name}.branch={Telemetry.Branch}";
        yield return $"{Name}.simulated_seconds={Telemetry.SimulatedSeconds:F6}";
        yield return $"{Name}.effective_step_cap={Telemetry.EffectiveStepCap:F6}";
        yield return $"{Name}.outer_substeps={Telemetry.OuterSubsteps}";
        yield return $"{Name}.full_physics_dispatches={Telemetry.FullPhysicsDispatches}";
        yield return $"{Name}.on_rails_dispatches={Telemetry.OnRailsDispatches}";
        yield return $"{Name}.rails_slices={Telemetry.RailsSlices}";
        yield return $"{Name}.deadline_eligible_evaluations={Telemetry.DeadlineEligibleEvaluations}";
        yield return $"{Name}.deadline_deferred_skips={Telemetry.DeadlineDeferredSkips}";
        yield return $"{Name}.deadline_projected_dispatches={Telemetry.DeadlineProjectedDispatches}";
        yield return $"{Name}.deadline_catchup_dispatches={Telemetry.DeadlineCatchUpDispatches}";
        yield return $"{Name}.docked_secondary_skips={Telemetry.DockedSecondarySkips}";
        yield return $"{Name}.docking_constraint_applications={Telemetry.DockingConstraintApplications}";
        yield return $"{Name}.finite={Finite.ToString().ToLowerInvariant()}";
        yield return $"{Name}.event_contract={EventContractPass.ToString().ToLowerInvariant()}";
        yield return $"{Name}.valid={Valid.ToString().ToLowerInvariant()}";
    }
}

static class AllocationBenchmarkSink
{
    public static int TelemetryChecksum;
    public static int EngineRowCount;
}

record TelemetryTotals(
    long TotalWorkDispatches,
    long FullPhysicsDispatches,
    long OnRailsDispatches,
    long OuterSubsteps,
    long RailsSlices,
    long DeadlineEligibleEvaluations,
    long DeadlineDeferredSkips,
    long DeadlineProjectedDispatches,
    long DeadlineCatchUpDispatches,
    long DockedSecondarySkips,
    long DockingConstraintApplications);

sealed class TelemetryTotalsBuilder
{
    private long _totalWorkDispatches;
    private long _fullPhysicsDispatches;
    private long _onRailsDispatches;
    private long _outerSubsteps;
    private long _railsSlices;
    private long _deadlineEligibleEvaluations;
    private long _deadlineDeferredSkips;
    private long _deadlineProjectedDispatches;
    private long _deadlineCatchUpDispatches;
    private long _dockedSecondarySkips;
    private long _dockingConstraintApplications;

    public void Add(PhysicsSchedulerTelemetry telemetry)
    {
        _totalWorkDispatches += telemetry.TotalWorkDispatches;
        _fullPhysicsDispatches += telemetry.FullPhysicsDispatches;
        _onRailsDispatches += telemetry.OnRailsDispatches;
        _outerSubsteps += telemetry.OuterSubsteps;
        _railsSlices += telemetry.RailsSlices;
        _deadlineEligibleEvaluations += telemetry.DeadlineEligibleEvaluations;
        _deadlineDeferredSkips += telemetry.DeadlineDeferredSkips;
        _deadlineProjectedDispatches += telemetry.DeadlineProjectedDispatches;
        _deadlineCatchUpDispatches += telemetry.DeadlineCatchUpDispatches;
        _dockedSecondarySkips += telemetry.DockedSecondarySkips;
        _dockingConstraintApplications += telemetry.DockingConstraintApplications;
    }

    public TelemetryTotals Build() => new(
        _totalWorkDispatches,
        _fullPhysicsDispatches,
        _onRailsDispatches,
        _outerSubsteps,
        _railsSlices,
        _deadlineEligibleEvaluations,
        _deadlineDeferredSkips,
        _deadlineProjectedDispatches,
        _deadlineCatchUpDispatches,
        _dockedSecondarySkips,
        _dockingConstraintApplications);
}
