using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Exosphere.Simulation;
using Exosphere.Simulation.Flight;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Length is < 2 or > 4)
{
    Console.Error.WriteLine("Usage: Flight14TrajectoryProbe <data-directory> <output-directory> [frames-per-second] [--return]");
    return 2;
}
string data = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
bool includeReturn = args.Contains("--return");
var options = args.Skip(2).Where(a => a != "--return").ToArray();
if (options.Length > 1) throw new ArgumentException("Expected one frame-rate argument.");
double fps = options.Length == 1 ? double.Parse(options[0], CultureInfo.InvariantCulture) : 60;
if (!double.IsFinite(fps) || fps < 1 || fps > 240)
    throw new ArgumentOutOfRangeException(nameof(fps));
Directory.CreateDirectory(output);
var run = includeReturn ? Flight14LaunchDiagnostic.CreateWithReturn(data) : Flight14LaunchDiagnostic.Create(data);
var returning = run.ReturnController;
var returnDefinition = includeReturn ? Flight14ReturnDefinition.LoadFromJson(Path.Combine(data,
    "flight_profiles/starship_flight14_return_estimate.json")) : null;
var (universe, earth, ship, controller, guidance, payloadDefinition, deployment) = run;
double initialMass = ship.TotalMass, nextSample = 0;
double maximumAngularRate = 0, maximumAngularRateElapsed = 0, maximumPreReturnAngularRate = 0;
string maximumAngularRatePhase = "";
var engines = ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("ship_engines"));
var tank = ship.Parts.Parts.Single(p => p.Definition.HasVehicleRole("tank"));
int maximumInsertionEngines = 0, maximumDeorbitEngines = 0;
var events = new List<object>();
string previousPhase = "Ignition";
using (var trace = new StreamWriter(Path.Combine(output, "telemetry.jsonl")))
{
    double duration = returnDefinition?.MaximumMissionSeconds ?? payloadDefinition.MaximumMissionSeconds + 61;
    int frames = (int)System.Math.Ceiling((duration + guidance.MaximumIgnitionSeconds + 1)*fps);
    for (int i = 0; i < frames; i++)
    {
        run.AdvanceFrame(1/fps);
        double elapsed = universe.CurrentTime-controller.LiftoffEpoch;
        var rel = ship.Position-earth.Position;
        var velocity = ship.Velocity-earth.Velocity;
        var orbit = OrbitalElements.FromStateVector(rel, velocity, earth.GM, earth.Id, universe.CurrentTime);
        int running = engines.GetEngineTelemetry(0).Count(e => e.ThrustN > 1);
        if (deployment.Phase != Flight14PayloadPhase.Complete)
            maximumPreReturnAngularRate = System.Math.Max(maximumPreReturnAngularRate, ship.AngularVelocity.Magnitude);
        if (ship.AngularVelocity.Magnitude > maximumAngularRate)
        {
            maximumAngularRate = ship.AngularVelocity.Magnitude;
            maximumAngularRateElapsed = elapsed;
            maximumAngularRatePhase = returning != null && returning.Phase != Flight14ReturnPhase.WaitingForPayload
                ? "Return" + returning.Phase : controller.Phase.ToString();
        }
        if (controller.Phase == Flight14LaunchPhase.Insertion)
            maximumInsertionEngines = System.Math.Max(maximumInsertionEngines, running);
        string phase = controller.Phase == Flight14LaunchPhase.OrbitReady
            ? "Payload"+deployment.Phase : controller.Phase.ToString();
        if (returning != null && returning.Phase != Flight14ReturnPhase.WaitingForPayload)
            phase = "Return" + returning.Phase;
        if (returning?.Phase == Flight14ReturnPhase.DeorbitBurn)
            maximumDeorbitEngines = System.Math.Max(maximumDeorbitEngines, running);
        bool phaseChanged = phase != previousPhase;
        if (phaseChanged)
        {
            events.Add(new { phase, elapsedSeconds = Number(elapsed),
                apoapsisAltitudeM = Number(orbit.Apoapsis-earth.Radius),
                periapsisAltitudeM = Number(orbit.Periapsis-earth.Radius) });
            Console.WriteLine($"T+{elapsed:F2} {phase} "
                + $"alt={ship.GetAltitude(earth):F0}m apo={orbit.Apoapsis-earth.Radius:F0}m "
                + $"pe={orbit.Periapsis-earth.Radius:F0}m");
            previousPhase = phase;
        }
        if (double.IsFinite(elapsed) && elapsed >= 0 && (phaseChanged || elapsed >= nextSample))
        {
            trace.WriteLine(JsonSerializer.Serialize(new {
                missionElapsedSeconds = elapsed, phase,
                deployedPayloadCount = deployment.Releases.Count,
                attachedPayloadMassKg = ship.Parts.Parts.Where(p => p.Definition.HasVehicleRole("payload")).Sum(p => p.CurrentMass),
                geodeticAltitudeM = ship.GetAltitude(earth), radialAltitudeM = rel.Magnitude-earth.Radius,
                atmosphereRelativeSpeedMps = ship.GetSurfaceVelocity(earth).Magnitude,
                inertialSpeedMps = velocity.Magnitude, radialSpeedMps = velocity.Dot(rel.Normalized),
                apoapsisAltitudeM = Number(orbit.Apoapsis-earth.Radius),
                periapsisAltitudeM = Number(orbit.Periapsis-earth.Radius),
                specificEnergyJPerKg = velocity.MagnitudeSquared * 0.5 - earth.GM / rel.Magnitude,
                verticalSpeedMps = ship.GetSurfaceVelocity(earth).Dot(earth.GetGeodeticUp(ship.Position)),
                remainingShipPropellantKg = tank.LiquidFuel+tank.Oxidizer,
                runningShipEngines = running, angularRateRadPerSecond = ship.AngularVelocity.Magnitude
            }));
            if (elapsed >= nextSample) nextSample = System.Math.Floor(elapsed)+1;
        }
        if (controller.Phase == Flight14LaunchPhase.Blocked || deployment.Phase == Flight14PayloadPhase.Blocked
            || returning?.Phase is Flight14ReturnPhase.Blocked or Flight14ReturnPhase.EntryReached
            || (!includeReturn && deployment.Phase == Flight14PayloadPhase.Complete
                && elapsed >= deployment.Releases[^1].MissionElapsedSeconds+60)) break;
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
bool passed = controller.Phase == Flight14LaunchPhase.OrbitReady
    && deployment.Phase == Flight14PayloadPhase.Complete && deployment.Releases.Count == payloadDefinition.Count
    && deployment.Releases.All(r => System.Math.Abs(r.MassResidualKg) < 1e-6
        && r.CenterResidualM < 1e-7 && r.MomentumResidualKgMps < 0.01
        && r.RelativeOpeningResidualMps < 1e-7 && !r.Satellite.IsDestroyed
        && r.Satellite.GetAltitude(earth) > earth.Atmosphere!.MaxAltitude)
    && maximumInsertionEngines == 1
    && controller.CoastMinimumAltitudeM > earth.Atmosphere!.MaxAltitude
    && controller.CutoffPeriapsisAltitudeM < earth.Atmosphere.MaxAltitude
    && maximumPreReturnAngularRate <= 0.05
    && !ship.IsDestroyed && !ship.StructuralControlLost
    && (includeReturn
        ? returning!.Phase == Flight14ReturnPhase.EntryReached && returning.HasDeliveredDeorbitThrust
            && maximumDeorbitEngines == 1 && returning.EntryInterface != null && returning.DiagnosticEnd != null
            && returning.PostDeorbitEnergyJPerKg < returning.PreDeorbitEnergyJPerKg
            && returning.PostDeorbitPropellantKg < returning.PreDeorbitPropellantKg
            && returning.DiagnosticEnd.VerticalSpeedMps < 0 && maximumAngularRate <= 0.15
            && returning.EntryInterface.WindwardShieldDotVelocity > 0.85
            && returning.DiagnosticEnd.WindwardShieldDotVelocity > 0.85
        : finalOrbit.Periapsis-earth.Radius >= payloadDefinition.MinimumPeriapsisAltitudeM && maximumAngularRate <= 0.05);
var summary = new {
    status = passed ? "COMPONENT_PASS" : "COMPONENT_FAIL", missionAcceptance = false,
    diagnosticOnly = true, componentScope = includeReturn ? "pad-to-26-payload-release-deorbit-entry" : "pad-to-26-payload-release", frameModel = "isolated-Earth at simulation epoch zero",
    integrationDriver = "whole-20ms-steps-with-retained-frame-remainder",
    hardwareBaseline = guidance.BaselineVehicleFile, hardwareIsEstimated = true,
    guidanceSha256 = Hash(Path.Combine(data, "flight_profiles/starship_flight14_guidance_estimate.json")),
    referenceSha256 = Hash(Path.Combine(data, "flight_profiles/starship_flight14_2026.json")),
    physicsAssemblySha256 = Hash(typeof(Universe).Assembly.Location), physicalDataHashes,
    postDeploymentTailSeconds = includeReturn ? 0 : 60,
    returnProfileSha256 = includeReturn ? Hash(Path.Combine(data, "flight_profiles/starship_flight14_return_estimate.json")) : null,
    returnPhase = returning?.Phase.ToString(), returnBlockReason = returning?.BlockReason,
    maximumDeorbitEngines, returnRegionTargeted = false,
    returnWitness = returning == null ? null : new { postDeploymentMassKg = Number(returning.PostDeploymentMassKg),
        postDeploymentPropellantKg = Number(returning.PostDeploymentPropellantKg),
        deorbitCommandElapsedSeconds = Number(returning.DeorbitCommandElapsedSeconds),
        deorbitTargetElapsedSeconds = Number(returning.DeorbitTargetElapsedSeconds),
        deorbitShutdownElapsedSeconds = Number(returning.DeorbitShutdownElapsedSeconds),
        preDeorbitEnergyJPerKg = Number(returning.PreDeorbitEnergyJPerKg),
        postDeorbitEnergyJPerKg = Number(returning.PostDeorbitEnergyJPerKg),
        preDeorbitPropellantKg = Number(returning.PreDeorbitPropellantKg),
        postDeorbitPropellantKg = Number(returning.PostDeorbitPropellantKg),
        postDeorbitPeriapsisAltitudeM = Number(returning.PostDeorbitPeriapsisAltitudeM),
        returning.EntryInterface, returning.DiagnosticEnd },
    payloadProfileSha256 = Hash(Path.Combine(data, "flight_profiles/starship_flight14_payload_estimate.json")), framesPerSecond = fps, initialStackMassKg = initialMass,
    remainingShipMassKg = ship.TotalMass, remainingShipPropellantKg = tank.LiquidFuel+tank.Oxidizer,
    faultsAreSynthetic = true,
    payloadMassIsEstimated = true,
    payloadMassRepresentedKg = deployment.Releases.Sum(r => r.Satellite.TotalMass)
        +ship.Parts.Parts.Where(p => p.Definition.HasVehicleRole("payload")).Sum(p => p.CurrentMass),
    simulatedPayloadDeployments = deployment.Releases.Count,
    payloadPhase = deployment.Phase.ToString(),
    payloadBlockReason = deployment.BlockReason,
    releases = deployment.Releases.Select(r => new { r.MissionElapsedSeconds, r.PartInstanceId,
        satelliteId = r.Satellite.Id, massKg = r.Satellite.TotalMass, r.CarrierMassBeforeKg, r.CarrierMassAfterKg,
        r.MassResidualKg, r.CenterResidualM, r.MomentumResidualKgMps, r.RelativeOpeningResidualMps, r.PeriapsisAltitudeM }),
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
    maximumAngularRate, maximumPreReturnAngularRate, maximumAngularRateElapsed = Number(maximumAngularRateElapsed), maximumAngularRatePhase,
    maximumInsertionEngines, events
};
File.WriteAllText(Path.Combine(output, "summary.json"),
    JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true })+"\n");
Console.WriteLine($"{summary.status}: {controller.BlockReason ?? deployment.BlockReason ?? returning?.BlockReason ?? "isolated component only; not mission acceptance"}");
return passed ? 0 : 1;

static double? Number(double value) => double.IsFinite(value) ? value : null;
static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
