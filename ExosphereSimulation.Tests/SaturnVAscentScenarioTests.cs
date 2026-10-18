namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Apollo 8's AS-503 flown from LC-39A to a real parking orbit: the historical S-IC pitch
/// program and staging times, then closed-loop insertion steering on the S-II and S-IVB
/// until the integrated periapsis clears the atmosphere. No state is assigned.
/// </summary>
public sealed class SaturnVAscentScenarioTests(ITestOutputHelper output)
{
    private const double MinimumParkingPeriapsisM = 180_000.0;

    [Fact]
    public void ClosedLoopUpperStagesReachTheParkingOrbit()
    {
        var root = DataDirectory();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(root, "parts"));
        var vessel = VehicleVariantDefinition.LoadFromJson(Path.Combine(root, "vehicles",
            "apollo8_saturn5_as503_1968.json")).Build(catalog).ToVessel("AS-503", "as503");
        var universe = Universe.LoadFromDataDirectory(root);
        var earth = universe.GetBody("earth")!;
        var pad = earth.GetSurfacePosition(28.608389, -80.604333, 3.0);
        var up = earth.GetGeodeticUp(pad);
        vessel.Position = pad;
        vessel.Velocity = earth.Velocity + earth.GetSurfaceVelocity(pad);
        vessel.Orientation = Quaterniond.FromTo(Vector3d.Up, up);
        vessel.ReferenceBodyId = earth.Id;
        vessel.IsGroundHeld = true;
        vessel.GroundNormal = (pad - earth.Position).Normalized;
        vessel.GroundOffset = 3.0;
        vessel.SASEnabled = false;
        vessel.Throttle = 1.0;
        universe.AddVessel(vessel);
        universe.ActiveVessel = vessel;

        double liftoff = double.NaN;
        bool sicSeparated = false, towerJettisoned = false, siiSeparated = false, inserted = false;
        double insertionTime = double.NaN;
        for (int step = 0; step < 50 * 1_200 && !vessel.IsDestroyed && !inserted; step++)
        {
            if (vessel.IsGroundHeld)
            {
                if (HoldDownReleasePolicy.CanRelease(vessel.GetThrustToWeightRatio(earth), vessel.Throttle))
                {
                    vessel.ReleaseGroundHold();
                    liftoff = universe.CurrentTime;
                }
                universe.Tick(0.02);
                continue;
            }

            double elapsed = universe.CurrentTime - liftoff;
            var localUp = earth.GetGeodeticUp(vessel.Position);
            var downrange = earth.GetEastDirection(vessel.Position);
            Vector3d aim;
            if (!sicSeparated)
            {
                aim = AttitudeGuidance.AimFromElevation(localUp, downrange,
                    Apollo8FlightProfile.ElevationDegrees(elapsed) * MathUtils.DEG_TO_RAD);
                vessel.Parts.Parts.FirstOrDefault(p => p.Definition.HasVehicleRole("sic_engine_cluster"))
                    ?.SelectEngineCount(elapsed >= Apollo8FlightProfile.SicCenterEngineCutoffSeconds ? 4 : 5);
                vessel.Throttle = elapsed < Apollo8FlightProfile.SicOutboardCutoffSeconds ? 1.0 : 0.0;
                if (elapsed >= Apollo8FlightProfile.SicSeparationSeconds)
                {
                    universe.AddVessel(vessel.Stage()!);
                    sicSeparated = true;
                    vessel.Throttle = 1.0;
                }
            }
            else
            {
                aim = ClosedLoopInsertionGuidance.ComputeAim(vessel, earth,
                    Apollo8FlightProfile.ParkingOrbitAltitudeM, downrange);
                if (!towerJettisoned && elapsed >= Apollo8FlightProfile.EscapeTowerJettisonSeconds)
                {
                    var tower = vessel.Parts.Parts.First(p => p.Definition.HasVehicleRole("launch_escape_system"));
                    universe.AddVessel(vessel.DeployPayload(tower.InstanceId, "LES", new Vector3d(0, 1.5, 0))!);
                    towerJettisoned = true;
                }
                if (!siiSeparated && elapsed >= Apollo8FlightProfile.SiiSeparationSeconds)
                {
                    universe.AddVessel(vessel.Stage()!);
                    siiSeparated = true;
                }
                vessel.Throttle = siiSeparated || elapsed < Apollo8FlightProfile.SiiCutoffSeconds ? 1.0 : 0.0;
                if (siiSeparated && ClosedLoopInsertionGuidance.IsInserted(
                        vessel, earth, MinimumParkingPeriapsisM, universe.CurrentTime))
                {
                    vessel.Throttle = 0.0;
                    inserted = true;
                    insertionTime = elapsed;
                }
            }
            vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(vessel.Orientation,
                Vector3d.Up, aim, vessel.AngularVelocity, proportionalGain: 2.3, dampingGain: 1.2);
            universe.Tick(0.02);
        }

        var orbit = OrbitalElements.FromStateVector(vessel.Position - earth.Position,
            vessel.Velocity - earth.Velocity, earth.GM, earth.Id, universe.CurrentTime);
        double sivbPropellant = vessel.Parts.Parts.Where(p => p.Definition.Id == "saturn5_sivb_tank_as503")
            .Sum(p => p.LiquidFuel + p.Oxidizer);
        output.WriteLine($"inserted={inserted} at T+{insertionTime:F0}s orbit={(orbit.Periapsis - earth.Radius) / 1000:F0} x "
            + $"{(orbit.Apoapsis - earth.Radius) / 1000:F0} km i={orbit.Inclination * 180 / System.Math.PI:F1} deg "
            + $"S-IVB propellant left={sivbPropellant / 1000:F1} t destroyed={vessel.IsDestroyed}");
        Assert.False(vessel.IsDestroyed);
        Assert.True(inserted);
        // Historical S-IVB first cutoff: T+685 s with roughly 70 t left for TLI.
        Assert.InRange(insertionTime, 560.0, 800.0);
        Assert.InRange(orbit.Apoapsis - earth.Radius, MinimumParkingPeriapsisM, 260_000.0);
        Assert.True(sivbPropellant > 50_000.0, $"Only {sivbPropellant / 1000:F1} t left for TLI.");
    }

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data", "bodies")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }
}
