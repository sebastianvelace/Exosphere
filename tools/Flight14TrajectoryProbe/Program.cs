using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Exosphere.Simulation;
using Exosphere.Simulation.Flight;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("Usage: Flight14TrajectoryProbe <data-directory> <output-directory> [frames-per-second]");
    return 2;
}
string data = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
double fps = args.Length == 3 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 60;
if (!double.IsFinite(fps) || fps < 1 || fps > 240)
    throw new ArgumentOutOfRangeException(nameof(fps));
Directory.CreateDirectory(output);
var run = Flight14LaunchDiagnostic.Create(data);
var (universe, earth, ship, controller, guidance) = run;
double initialMass = ship.TotalMass, nextSample = 0;
double maximumAngularRate = 0, maximumAngularRateElapsed = 0;
string maximumAngularRatePhase = "";
var engines = ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("ship_engines"));
var tank = ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("tank"));
int maximumInsertionEngines = 0;
var events = new List<object>();
var previousPhase = controller.Phase;
using (var trace = new StreamWriter(Path.Combine(output, "telemetry.jsonl")))
{
    int frames = (int)System.Math.Ceiling((guidance.MaximumMissionSeconds+guidance.MaximumIgnitionSeconds+1)*fps);
    for (int i = 0; i < frames; i++)
    {
        run.AdvanceFrame(1/fps);
        double elapsed = universe.CurrentTime-controller.LiftoffEpoch;
        var rel = ship.Position-earth.Position;
        var velocity = ship.Velocity-earth.Velocity;
        var orbit = OrbitalElements.FromStateVector(rel, velocity, earth.GM, earth.Id, universe.CurrentTime);
        int running = engines.GetEngineTelemetry(0).Count(e => e.ThrustN > 1);
        if (ship.AngularVelocity.Magnitude > maximumAngularRate)
        {
            maximumAngularRate = ship.AngularVelocity.Magnitude;
            maximumAngularRateElapsed = elapsed;
            maximumAngularRatePhase = controller.Phase.ToString();
        }
        if (controller.Phase == Flight14LaunchPhase.Insertion)
            maximumInsertionEngines = System.Math.Max(maximumInsertionEngines, running);
        bool phaseChanged = controller.Phase != previousPhase;
        if (phaseChanged)
        {
            events.Add(new { phase = controller.Phase.ToString(), elapsedSeconds = Number(elapsed),
                apoapsisAltitudeM = Number(orbit.Apoapsis-earth.Radius),
                periapsisAltitudeM = Number(orbit.Periapsis-earth.Radius) });
            Console.WriteLine($"T+{elapsed:F2} {controller.Phase} "
                + $"alt={ship.GetAltitude(earth):F0}m apo={orbit.Apoapsis-earth.Radius:F0}m "
                + $"pe={orbit.Periapsis-earth.Radius:F0}m");
            previousPhase = controller.Phase;
        }
        if (double.IsFinite(elapsed) && elapsed >= 0 && (phaseChanged || elapsed >= nextSample))
        {
            trace.WriteLine(JsonSerializer.Serialize(new {
                missionElapsedSeconds = elapsed, phase = controller.Phase.ToString(),
                geodeticAltitudeM = ship.GetAltitude(earth), radialAltitudeM = rel.Magnitude-earth.Radius,
                atmosphereRelativeSpeedMps = ship.GetSurfaceVelocity(earth).Magnitude,
                inertialSpeedMps = velocity.Magnitude, radialSpeedMps = velocity.Dot(rel.Normalized),
                apoapsisAltitudeM = Number(orbit.Apoapsis-earth.Radius),
                periapsisAltitudeM = Number(orbit.Periapsis-earth.Radius),
                remainingShipPropellantKg = tank.LiquidFuel+tank.Oxidizer,
                runningShipEngines = running, angularRateRadPerSecond = ship.AngularVelocity.Magnitude
            }));
            if (elapsed >= nextSample) nextSample = System.Math.Floor(elapsed)+1;
        }
        if (controller.Phase == Flight14LaunchPhase.Blocked
            || (controller.Phase == Flight14LaunchPhase.OrbitReady
                && elapsed >= controller.OrbitElapsedSeconds+60)) break;
    }
}
var finalOrbit = OrbitalElements.FromStateVector(ship.Position-earth.Position,
    ship.Velocity-earth.Velocity, earth.GM, earth.Id, universe.CurrentTime);
var physicalDataHashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
foreach (string folder in new[] { "parts", "engines", "engine_clusters" })
    foreach (string file in Directory.EnumerateFiles(Path.Combine(data, folder), "*.json"))
        physicalDataHashes[Path.GetRelativePath(data, file)] = Hash(file);
foreach (string file in new[] { "bodies/earth.json", "launch_sites/starbase_pad2.json",
    "vehicles/"+guidance.BaselineVehicleFile })
    physicalDataHashes[file] = Hash(Path.Combine(data, file));
bool passed = controller.Phase == Flight14LaunchPhase.OrbitReady && maximumInsertionEngines == 1
    && controller.CoastMinimumAltitudeM > earth.Atmosphere!.MaxAltitude
    && controller.CutoffPeriapsisAltitudeM < earth.Atmosphere.MaxAltitude
    && finalOrbit.Periapsis-earth.Radius >= guidance.TargetInsertionPeriapsisAltitudeM
    && maximumAngularRate <= 0.05;
var summary = new {
    status = passed ? "COMPONENT_PASS" : "COMPONENT_FAIL", missionAcceptance = false,
    diagnosticOnly = true, frameModel = "isolated-Earth at simulation epoch zero",
    integrationDriver = "whole-20ms-steps-with-retained-frame-remainder",
    hardwareBaseline = guidance.BaselineVehicleFile, hardwareIsEstimated = true,
    guidanceSha256 = Hash(Path.Combine(data, "flight_profiles/starship_flight14_guidance_estimate.json")),
    referenceSha256 = Hash(Path.Combine(data, "flight_profiles/starship_flight14_2026.json")),
    physicsAssemblySha256 = Hash(typeof(Universe).Assembly.Location), physicalDataHashes,
    orbitTailSeconds = 60, framesPerSecond = fps, initialStackMassKg = initialMass,
    remainingShipMassKg = ship.TotalMass, remainingShipPropellantKg = tank.LiquidFuel+tank.Oxidizer,
    faultsAreSynthetic = true, payloadMassRepresentedKg = 0, simulatedPayloadDeployments = 0,
    controlledBoosterRecovery = false, controlledShipRecovery = false,
    phase = controller.Phase.ToString(), blockReason = controller.BlockReason,
    liftoffEpochSeconds = Number(controller.LiftoffEpoch),
    stagingElapsedSeconds = Number(controller.StagingElapsedSeconds),
    cutoffElapsedSeconds = Number(controller.CutoffElapsedSeconds),
    cutoffApoapsisAltitudeM = Number(controller.CutoffApoapsisAltitudeM),
    cutoffPeriapsisAltitudeM = Number(controller.CutoffPeriapsisAltitudeM),
    minimumCoastGeodeticAltitudeM = Number(controller.CoastMinimumAltitudeM),
    insertionElapsedSeconds = Number(controller.InsertionElapsedSeconds),
    orbitElapsedSeconds = Number(controller.OrbitElapsedSeconds),
    maximumAngularRate, maximumAngularRateElapsed = Number(maximumAngularRateElapsed), maximumAngularRatePhase,
    maximumInsertionEngines, events
};
File.WriteAllText(Path.Combine(output, "summary.json"),
    JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true })+"\n");
Console.WriteLine($"{summary.status}: {controller.BlockReason ?? "isolated launch-to-insertion only; not mission acceptance"}");
return passed ? 0 : 1;

static double? Number(double value) => double.IsFinite(value) ? value : null;
static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
