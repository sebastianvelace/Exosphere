namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// GLV-8 Titan II flown from LC-19 into Gemini 8's insertion orbit: the historical stage-one
/// elevation program burned to propellant depletion (BECO), then closed-loop stage-two
/// insertion steered into the Agena's plane, cut on the flown periapsis or at burnout. The
/// open-loop stage two it replaces stalled at 115 km and fell back (vacuum periapsis −1,261 km):
/// the old rendezvous teleport had hidden that the ascent never reached orbit.
/// </summary>
public sealed class Gemini8AscentScenarioTests(ITestOutputHelper output)
{
    [Fact]
    public void TitanFliesIntoAnOrbitInTheAgenaPlane()
    {
        var root = SaturnVAscentFlight.DataDirectory();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(root, "parts"));
        var vessel = VehicleVariantDefinition.LoadFromJson(Path.Combine(root, "vehicles", "gemini8_titan2_1966.json"))
            .Build(catalog).ToVessel("GLV-8", "glv8");
        var universe = Universe.LoadFromDataDirectory(root);
        var earth = universe.GetBody("earth")!;
        var site = LaunchSite.LoadFromJson(Path.Combine(root, "launch_sites", "cape_canaveral_lc19.json"));
        var pad = site.GetPosition(earth, universe.CurrentTime);
        var up = earth.GetGeodeticUp(pad);
        var frame = site.GetLocalFrame(earth, universe.CurrentTime);
        double heading = site.Heading * MathUtils.DEG_TO_RAD;
        var tangent = (frame.North * System.Math.Cos(heading) + frame.East * System.Math.Sin(heading)).Normalized;
        // The Agena's plane as the director places it: through the pad along the launch heading.
        var planeNormal = (pad - earth.Position).Normalized.Cross(tangent).Normalized;
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

        double liftoff = double.NaN, insertion = double.NaN, beco = double.NaN;
        bool staged = false, inserted = false;
        for (int step = 0; step < 50 * 700 && !vessel.IsDestroyed && !inserted; step++)
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
            var downrange = planeNormal.Cross(earth.GetGeodeticUp(vessel.Position)).Normalized;
            Vector3d aim;
            if (!staged)
            {
                aim = AttitudeGuidance.AimFromElevation(earth.GetGeodeticUp(vessel.Position), downrange,
                    Gemini8FlightProfile.ElevationDegrees(elapsed) * MathUtils.DEG_TO_RAD);
                // BECO was propellant depletion, not a clock: burn stage one out, then separate.
                vessel.Throttle = 1.0;
                if (double.IsNaN(beco) && Gemini8FlightProfile.IsStageOneDepleted(vessel, earth, elapsed))
                    beco = elapsed;
                if (!double.IsNaN(beco) && elapsed >= beco + Gemini8FlightProfile.StagingDelaySeconds)
                {
                    universe.AddVessel(vessel.Stage()!);
                    staged = true;
                    vessel.Throttle = 1.0;
                }
            }
            else
            {
                aim = ClosedLoopInsertionGuidance.ComputeAim(vessel, earth,
                    Gemini8FlightProfile.InsertionAltitudeM, downrange, planeNormal);
                if (ClosedLoopInsertionGuidance.IsInserted(vessel, earth,
                        Gemini8FlightProfile.MinimumInsertionPeriapsisM, universe.CurrentTime)
                    || Gemini8FlightProfile.IsStageTwoBurnedOut(vessel, earth, elapsed - beco))
                {
                    vessel.Throttle = 0.0;
                    inserted = true;
                    insertion = elapsed;
                }
            }
            double damping = staged
                ? Gemini8FlightProfile.StageTwoDampingGain(vessel.GetPitchYawAngularAuthority(earth))
                : 1.2;
            vessel.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(vessel.Orientation, Vector3d.Up,
                aim, vessel.AngularVelocity, proportionalGain: 2.3, dampingGain: damping);
            universe.Tick(0.02);
        }

        var orbit = OrbitalElements.FromStateVector(vessel.Position - earth.Position,
            vessel.Velocity - earth.Velocity, earth.GM, earth.Id, universe.CurrentTime);
        var flownNormal = (vessel.Position - earth.Position).Cross(vessel.Velocity - earth.Velocity).Normalized;
        double planeError = System.Math.Acos(System.Math.Clamp(flownNormal.Dot(planeNormal), -1, 1)) * MathUtils.RAD_TO_DEG;
        output.WriteLine($"inserted={inserted} at T+{insertion:F0}s orbit={(orbit.Periapsis - earth.Radius) / 1000:F0} x "
            + $"{(orbit.Apoapsis - earth.Radius) / 1000:F0} km plane error={planeError:F2} deg BECO T+{beco:F1}s destroyed={vessel.IsDestroyed}");
        Assert.False(vessel.IsDestroyed);
        Assert.True(inserted);
        Assert.InRange(insertion, 280.0, 380.0); // historical SECO T+337.5 s
        Assert.InRange(orbit.Periapsis - earth.Radius, Gemini8FlightProfile.MinimumInsertionPeriapsisM - 5_000.0, 230_000.0);
        Assert.True(planeError < 0.5, $"{planeError:F2} deg out of the Agena plane");
    }
}
