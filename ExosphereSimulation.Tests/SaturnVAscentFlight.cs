namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;

/// <summary>Outcome of one headless AS-503 ascent.</summary>
internal sealed record SaturnVAscentResult(
    Universe Universe, Vessel Vessel, CelestialBody Earth, bool Inserted, double InsertionElapsedSeconds,
    double SivbPropellantKg, OrbitalElements Orbit);

/// <summary>
/// AS-503 flown from LC-39A the way the historical director flies it: S-IC pitch-over and
/// gravity turn on the historical staging clock, then closed-loop S-II/S-IVB insertion that
/// cuts off on the flown periapsis. Only controls are written; no state is assigned.
/// </summary>
internal static class SaturnVAscentFlight
{
    public const double MinimumParkingPeriapsisM = 180_000.0;

    /// <param name="liftoffEpoch">Simulation time at which the pad sequence starts.</param>
    /// <param name="planeNormal">Target orbit plane; null flies due east with no plane steering.</param>
    /// <param name="separationErrorDeg">Attitude error injected at S-II separation.</param>
    /// <param name="separationRateRadPerS">Body rate injected at S-II separation.</param>
    /// <param name="variantFile">Vehicle variant under data/vehicles (Apollo 8 by default).</param>
    public static SaturnVAscentResult Fly(double liftoffEpoch = 0.0, Vector3d? planeNormal = null,
        double separationErrorDeg = 0.0, double separationRateRadPerS = 0.0,
        string variantFile = "apollo8_saturn5_as503_1968.json", SaturnVAscentSchedule? scheduleOverride = null)
    {
        var root = DataDirectory();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(root, "parts"));
        var vessel = VehicleVariantDefinition.LoadFromJson(Path.Combine(root, "vehicles",
            variantFile)).Build(catalog).ToVessel("Saturn V", "saturn-v");
        var schedule = scheduleOverride ?? (variantFile.Contains("as506", StringComparison.Ordinal)
            ? SaturnVAscentSchedule.As506 : SaturnVAscentSchedule.As503);
        var universe = Universe.LoadFromDataDirectory(root);
        universe.SetCurrentTime(liftoffEpoch);
        var earth = universe.GetBody("earth")!;
        var site = LaunchSite.LoadFromJson(Path.Combine(root, "launch_sites", "kennedy.json"));
        var pad = site.GetPosition(earth, liftoffEpoch);
        var up = earth.GetGeodeticUp(pad);
        vessel.Position = pad;
        vessel.Velocity = earth.Velocity + earth.GetSurfaceVelocity(pad);
        vessel.Orientation = Quaterniond.FromTo(Vector3d.Up, up);
        vessel.ReferenceBodyId = earth.Id;
        vessel.IsGroundHeld = true;
        vessel.GroundNormal = (pad - earth.Position).Normalized;
        vessel.GroundOffset = site.Altitude;
        vessel.SASEnabled = false;
        vessel.Throttle = 1.0;
        universe.AddVessel(vessel);
        universe.ActiveVessel = vessel;

        double liftoff = double.NaN;
        bool sicSeparated = false, towerJettisoned = false, siiSeparated = false, inserted = false;
        double insertionTime = double.NaN, sicCutoff = double.NaN;
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
            var downrange = planeNormal is { } n ? n.Cross(localUp).Normalized : earth.GetEastDirection(vessel.Position);
            Vector3d aim;
            if (!sicSeparated)
            {
                aim = GravityTurnGuidance.ComputeAim(vessel, earth, downrange, elapsed,
                    schedule.PitchOverDegrees, Apollo8FlightProfile.PitchOverEndSeconds);
                vessel.Parts.Parts.FirstOrDefault(p => p.Definition.HasVehicleRole("sic_engine_cluster"))
                    ?.SelectEngineCount(elapsed >= schedule.SicCenterEngineCutoffSeconds ? 4 : 5);
                if (double.IsNaN(sicCutoff) && schedule.IsSicCutoff(vessel, elapsed)) sicCutoff = elapsed;
                vessel.Throttle = double.IsNaN(sicCutoff) ? 1.0 : 0.0;
                if (elapsed >= sicCutoff + schedule.SicSeparationDelaySeconds)
                {
                    universe.AddVessel(vessel.Stage()!);
                    sicSeparated = true;
                    vessel.Throttle = 1.0;
                }
            }
            else
            {
                aim = ClosedLoopInsertionGuidance.ComputeAim(vessel, earth,
                    Apollo8FlightProfile.ParkingOrbitAltitudeM, downrange, planeNormal);
                if (!towerJettisoned && elapsed >= schedule.EscapeTowerJettisonSeconds)
                {
                    var tower = vessel.Parts.Parts.First(p => p.Definition.HasVehicleRole("launch_escape_system"));
                    universe.AddVessel(vessel.DeployPayload(tower.InstanceId, "LES", new Vector3d(0, 1.5, 0))!);
                    towerJettisoned = true;
                }
                if (!siiSeparated)
                    vessel.Parts.Parts.FirstOrDefault(p => p.Definition.HasVehicleRole("sii_engine_cluster"))
                        ?.SelectEngineCount(schedule.SiiEngineCount(elapsed));
                if (!siiSeparated && schedule.IsSiiSeparationDue(vessel, earth, elapsed))
                {
                    universe.AddVessel(vessel.Stage()!);
                    siiSeparated = true;
                    vessel.Orientation = (Quaterniond.FromAxisAngle(vessel.Orientation.Rotate(Vector3d.Right),
                        separationErrorDeg * MathUtils.DEG_TO_RAD) * vessel.Orientation).Normalize();
                    vessel.AngularVelocity += vessel.Orientation.Rotate(
                        new Vector3d(separationRateRadPerS, 0.0, 0.5 * separationRateRadPerS));
                }
                vessel.Throttle = siiSeparated || elapsed < schedule.SiiCutoffSeconds ? 1.0 : 0.0;
                if (siiSeparated && ClosedLoopInsertionGuidance.IsInserted(
                        vessel, earth, MinimumParkingPeriapsisM, universe.CurrentTime))
                {
                    vessel.Throttle = 0.0;
                    inserted = true;
                    insertionTime = elapsed;
                }
            }
            double damping = sicSeparated
                ? Apollo8FlightProfile.UpperStageDampingGain(vessel.GetPitchYawAngularAuthority(earth))
                : 1.2;
            vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(vessel.Orientation,
                Vector3d.Up, aim, vessel.AngularVelocity, proportionalGain: 2.3, dampingGain: damping);
            universe.Tick(0.02);
        }

        var orbit = OrbitalElements.FromStateVector(vessel.Position - earth.Position,
            vessel.Velocity - earth.Velocity, earth.GM, earth.Id, universe.CurrentTime);
        double sivbPropellant = vessel.Parts.Parts.Where(p => p.Definition.HasVehicleRole("sivb_tank"))
            .Sum(p => p.LiquidFuel + p.Oxidizer);
        return new SaturnVAscentResult(universe, vessel, earth, inserted, insertionTime, sivbPropellant, orbit);
    }

    public static string DataDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data", "bodies")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }
}
