namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Gemini 8's OAMS thruster 8 failure as physics: a stuck-on thruster torque that spins the
/// undocked spacecraft and drains OAMS propellant, re-entry RCS rings armed by the crew, the
/// thruster isolated, and the roll damped out by reaction control. No angular velocity is
/// assigned at any point.
/// </summary>
public sealed class Gemini8AnomalyScenarioTests(ITestOutputHelper output)
{
    [Fact]
    public void StuckThrusterSpinsTheSpacecraftAndReentryRcsRecoversIt()
    {
        var (universe, earth, gemini) = Undocked();
        var adapter = gemini.Parts.Parts.First(p => p.Definition.HasVehicleRole("gemini_equipment_adapter"));
        var reentry = gemini.Parts.Parts.First(p => p.Definition.HasVehicleRole("gemini_reentry_capsule"));
        double oamsStart = gemini.Parts.TotalMonopropellant;
        gemini.DisturbanceTorqueBody = new Vector3d(0.0, Gemini8FlightProfile.StuckThrusterTorqueNm, 0.0);
        gemini.DisturbanceMassFlowKgPerS = Gemini8FlightProfile.StuckThrusterMassFlowKgPerS;

        double start = universe.CurrentTime, peak = 0.0, recovered = double.NaN;
        double armAt = start + (Gemini8FlightProfile.ReentryControlActivationSeconds - Gemini8FlightProfile.UndockingSeconds);
        double isolateAt = start + (Gemini8FlightProfile.ThrusterIsolationSeconds - Gemini8FlightProfile.UndockingSeconds);
        bool armed = false;
        while (universe.CurrentTime < isolateAt + 900.0 && double.IsNaN(recovered))
        {
            if (!armed && universe.CurrentTime >= armAt)
            {
                // The crew hands attitude control from the OAMS to the re-entry RCS rings.
                adapter.RcsAttitudeEnabled = false;
                reentry.RcsAttitudeEnabled = true;
                armed = true;
            }
            if (universe.CurrentTime >= isolateAt)
            {
                gemini.DisturbanceTorqueBody = Vector3d.Zero;
                gemini.DisturbanceMassFlowKgPerS = 0.0;
            }
            gemini.PitchYawRoll = armed ? RateDamping(gemini) : Vector3d.Zero;
            universe.Tick(0.02);
            double rate = gemini.AngularVelocity.Magnitude;
            peak = System.Math.Max(peak, rate);
            if (universe.CurrentTime > isolateAt && rate < Gemini8FlightProfile.ControlRecoveredRateRadPerS)
                recovered = universe.CurrentTime;
        }
        double oamsUsed = oamsStart - gemini.Parts.TotalMonopropellant;
        output.WriteLine($"peak={peak * MathUtils.RAD_TO_DEG:F0} deg/s recovered {(recovered - isolateAt):F0} s after "
            + $"isolation, OAMS burned by the stuck thruster={oamsUsed:F1} kg");
        Assert.InRange(peak * MathUtils.RAD_TO_DEG, 100.0, 320.0); // flight: 296 deg/s
        Assert.False(double.IsNaN(recovered), "Re-entry RCS never damped the roll.");
        Assert.InRange(oamsUsed, 5.0, 15.0);
    }

    /// <summary>Pure rate damping on all three axes, sized from the live RCS authority.</summary>
    internal static Vector3d RateDamping(Vessel vessel)
    {
        var rate = vessel.Orientation.Inverse().Rotate(vessel.AngularVelocity);
        const double gain = 4.0;
        return new Vector3d(System.Math.Clamp(-gain * rate.X, -1.0, 1.0),
            System.Math.Clamp(-gain * rate.Z, -1.0, 1.0), System.Math.Clamp(-gain * rate.Y, -1.0, 1.0));
    }

    private static (Universe, CelestialBody, Vessel) Undocked()
    {
        var root = SaturnVAscentFlight.DataDirectory();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(root, "parts"));
        var assembly = new VesselAssembly(catalog);
        var current = assembly.AddRoot("gemini8_docking_nose");
        foreach (var id in new[] { "gemini8_reentry_module", "gemini8_retro_module", "gemini8_equipment_adapter" })
            current = assembly.AttachPartAutomatically(current.InstanceId, id);
        var gemini = assembly.ToVessel("Spacecraft 8", "gemini8");
        var universe = Universe.LoadFromDataDirectory(root);
        var earth = universe.GetBody("earth")!;
        double radius = earth.Radius + Gemini8FlightProfile.HistoricalTargetOrbitM;
        gemini.Position = earth.Position + Vector3d.Right * radius;
        gemini.Velocity = earth.Velocity + new Vector3d(0.0, 0.0, -1.0) * System.Math.Sqrt(earth.GM / radius);
        gemini.Orientation = Quaterniond.FromTo(Vector3d.Up, new Vector3d(0.0, 0.0, -1.0));
        // Undocked carrying the docked-phase roll (flight: ~20 deg/s).
        gemini.AngularVelocity = gemini.Orientation.Rotate(Vector3d.Up)
            * (Gemini8FlightProfile.DockedTumbleRateDegPerS * MathUtils.DEG_TO_RAD);
        gemini.ReferenceBodyId = earth.Id;
        gemini.SASEnabled = false;
        universe.AddVessel(gemini);
        universe.ActiveVessel = gemini;
        return (universe, earth, gemini);
    }
}
