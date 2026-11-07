using System.Globalization;
using System.Text.Json;
using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: PhysicsProfileProbe <data-directory> <output-json>");
    return 2;
}
string data = Path.GetFullPath(args[0]);
var catalog = PartCatalog.LoadFromDirectory(Path.Combine(data, "parts"));
var reports = new List<object>();
bool passed = true;
foreach (string file in new[] { "falcon9_block5_standard_2025.json", "falcon9_block5_extended_2025.json", "newglenn_7x2_public_2026.json" })
{
    var variant = VehicleVariantDefinition.LoadFromJson(Path.Combine(data, "vehicles", file));
    var assembly = variant.Build(catalog);
    var metrics = assembly.ComputeMetrics();
    var vessel = assembly.ToVessel(variant.Name);
    // A repeatable engineering climb, not a reconstructed launch schedule: no
    // mission-specific pitch law, payload injection or recovery is asserted.
    var earth = CelestialBody.LoadFromJson(Path.Combine(data, "bodies", "earth.json"));
    earth.OrbitalElements = null; earth.Position = earth.Velocity = Vector3d.Zero;
    var world = new Universe(); world.AddBody(earth);
    var site = LaunchSite.LoadFromJson(Path.Combine(data, "launch_sites",
        file.StartsWith("newglenn", StringComparison.Ordinal) ? "cape_canaveral_lc36.json" : "kennedy.json"));
    vessel.Position = site.GetPosition(earth, world.CurrentTime);
    vessel.Velocity = earth.GetSurfaceVelocity(vessel.Position);
    vessel.Orientation = Quaterniond.FromTo(Vector3d.Up, earth.GetGeodeticUp(vessel.Position));
    vessel.ReferenceBodyId = earth.Id; vessel.IsGroundHeld = true;
    vessel.GroundNormal = (vessel.Position - earth.Position).Normalized; vessel.GroundOffset = site.Altitude;
    vessel.SASEnabled = false; vessel.Throttle = 1;
    world.AddVessel(vessel); world.ActiveVessel = vessel;
    double initialMass = vessel.TotalMass;
    var samples = new List<object>();
    for (int step = 0; step < 1500; step++)
    {
        if (vessel.IsGroundHeld && HoldDownReleasePolicy.CanRelease(vessel.GetThrustToWeightRatio(earth), vessel.Throttle))
            vessel.ReleaseGroundHold();
        vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(vessel.Orientation, Vector3d.Up,
            earth.GetGeodeticUp(vessel.Position), vessel.AngularVelocity, 2.6, 1.2);
        world.Tick(0.02);
        if ((step + 1) % 500 == 0)
            samples.Add(new { seconds = (step + 1) * 0.02, altitudeM = vessel.GetAltitude(earth),
                surfaceSpeedMps = vessel.GetSurfaceVelocity(earth).Magnitude, massKg = vessel.TotalMass,
                angularRateRadPerS = vessel.AngularVelocity.Magnitude,
                sloshStatus = vessel.LastSloshStatus, modeCount = vessel.PropellantSloshStates.Count });
    }
    bool finiteClimb = !vessel.IsDestroyed && !vessel.IsGroundHeld && vessel.GetAltitude(earth) > 100
        && vessel.TotalMass < initialMass && double.IsFinite(vessel.Velocity.Magnitude)
        && double.IsFinite(vessel.AngularVelocity.Magnitude);
    passed &= finiteClimb;
    reports.Add(new { variant = file, launchSite = site.Id, scope = "30 s vertical engineering climb; no historical trajectory claim",
        passed = finiteClimb, metrics, initialMassKg = initialMass, consumedKg = initialMass - vessel.TotalMass, samples });
}

// Resolve the outstanding AS-506 aggregate station directly from the same part
// mass decomposition used in production. A single aggregate does not identify
// separate CM, SM, LM, SLA and LES offsets, so this probe does not retune them.
var apollo = VehicleVariantDefinition.LoadFromJson(Path.Combine(data, "vehicles", "apollo11_saturn5_as506_1969.json"))
    .Build(catalog).ToVessel("AS-506 mass-property audit");
var positions = apollo.Parts.ComputePartLocalPositions();
var spacecraft = apollo.Parts.Parts.TakeWhile(p => !p.Definition.HasVehicleRole("instrument_unit")).ToArray();
double spacecraftMass = spacecraft.Sum(p => p.CurrentMass);
double station = spacecraft.Sum(p => p.CurrentMass * (positions[p].Y + p.MassCenterOffsetYM)) / spacecraftMass - 2.930;
var mercury = VehicleVariantDefinition.LoadFromJson(Path.Combine(data, "vehicles", "mercury_atlas6_friendship7_1962.json"))
    .Build(catalog).ToVessel("MA-6 mass-definition audit");
var orbitalParts = mercury.Parts.Parts.Where(p => p.Definition.HasVehicleRole("capsule")
    || p.Definition.HasVehicleRole("retro_pack")).ToArray();
double mercuryOrbitMass = orbitalParts.Sum(p => p.CurrentMass);
double mercuryTowerMass = mercury.Parts.Parts.Where(p => p.Definition.HasVehicleRole("escape_tower")).Sum(p => p.CurrentMass);
var result = new { engineeringChecksPassed = passed, historicalCalibrationComplete = false,
    engineeringClimbs = reports,
    as506Spacecraft = new { massKg = spacecraftMass, cgXStationM = station,
        flightCgXStationM = 91.658, cgResidualM = station - 91.658,
        limitation = "Aggregate FER station alone cannot determine all module stations or inertia tensors." },
    ma6Spacecraft = new { modelOrbitalMassKg = mercuryOrbitMass, modelLaunchMassKg = mercuryOrbitMass + mercuryTowerMass,
        flightLaunchMassKg = 4265 * 0.45359237, flightOrbitalMassKg = 2987 * 0.45359237,
        orbitalMassResidualKg = mercuryOrbitMass - 2987 * 0.45359237,
        source = "Results of the First United States Manned Orbital Space Flight (1962), printed p. 7",
        limitation = "Current catalogue confuses launch and orbital spacecraft mass; native Atlas recalibration is pending." } };
string output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine($"Engineering climbs {(passed ? "PASS" : "FAIL")}; AS-506 spacecraft CG residual {station - 91.658:F3} m; MA-6 orbital mass residual {mercuryOrbitMass - 2987 * 0.45359237:F3} kg");
return passed ? 0 : 1;
