namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Integrators;
using Exosphere.Simulation.Math;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Controlled hyperbolic arrival with CSM-107 and wet LM-5 hard-docked. This extends
/// the common Apollo 8 controller fixture; it is not a measured Apollo 11 trajectory replay.
/// </summary>
public sealed class Apollo11SpsBurnScenarioTests(ITestOutputHelper output)
{
    private const double ArrivalPeriluneSpeedMps = 2_580.0;
    private const double StepSeconds = 0.02;

    [Fact]
    public void DockedLmMassOpensTheFullStackIgnitionWindow()
    {
        var (universe, moon, csm, lm) = CreateDockedArrival(180.0);
        // Universe refreshes attached mass before each committed integration step.
        universe.Tick(StepSeconds);
        double timeToPeriapsis = PeriapsisCaptureBurn.TimeToPeriapsis(
            csm.CenterOfMass - moon.Position, csm.CenterOfMassVelocity - moon.Velocity, moon.GM);
        double rp = moon.Radius + Apollo11FlightProfile.InitialLunarPeriluneAltitudeM;
        double ra = moon.Radius + Apollo11FlightProfile.InitialLunarApoluneAltitudeM;
        double targetSpeed = System.Math.Sqrt(moon.GM * (2.0 / rp - 2.0 / (rp + ra)));
        double deltaV = ArrivalPeriluneSpeedMps - targetSpeed;
        double thrust = csm.GetMaximumThrust(moon);
        double ownHalfBurn = 0.5 * csm.TotalMass * deltaV / thrust;
        double stackHalfBurn = 0.5 * (csm.TotalMass + lm.TotalMass) * deltaV / thrust;
        Assert.InRange(timeToPeriapsis, ownHalfBurn + 20.0, stackHalfBurn - 20.0);
        Assert.Equal(csm.TotalMass + lm.TotalMass, csm.TranslationalMassKg, 6);
        var burn = new PeriapsisCaptureBurn(Apollo11FlightProfile.InitialLunarApoluneAltitudeM, 500.0);

        burn.Advance(csm, moon, StepSeconds, universe.CurrentTime);

        Assert.Equal(PeriapsisCaptureBurnStatus.Burning, burn.Status);
        Assert.Equal(1.0, csm.Throttle);
    }

    [Fact]
    public void DockedColumbiaAndEagleCaptureOnFiniteSpsThrust()
    {
        var (universe, moon, csm, lm) = CreateDockedArrival(3_000.0);
        double massBefore = csm.TotalMass + lm.TotalMass;
        var burn = new PeriapsisCaptureBurn(Apollo11FlightProfile.InitialLunarApoluneAltitudeM, 500.0);
        var previous = (PeriapsisCaptureBurnStatus)(-1);
        while (!burn.IsFinished && universe.CurrentTime < 4_500.0)
        {
            burn.Advance(csm, moon, StepSeconds, universe.CurrentTime);
            if (burn.Status != previous)
            {
                previous = burn.Status;
                output.WriteLine($"t={universe.CurrentTime:F2} state={burn.Status} "
                    + $"burn={burn.BurnSeconds:F2} pe={burn.PeriapsisAltitudeM / 1000:F2} "
                    + $"ap={burn.ApoapsisAltitudeM / 1000:F2}");
            }
            universe.Tick(StepSeconds);
        }
        output.WriteLine($"status={burn.Status} reason={burn.BlockReason} burn={burn.BurnSeconds:F2} "
            + $"propellant={massBefore - csm.TotalMass - lm.TotalMass:F2} "
            + $"pe={burn.PeriapsisAltitudeM / 1000:F2} ap={burn.ApoapsisAltitudeM / 1000:F2} "
            + $"remainingSPS={csm.Parts.Parts.Sum(p => p.LiquidFuel + p.Oxidizer):F2}");

        Assert.Single(universe.DockingConnections);
        Assert.False(csm.IsDestroyed);
        Assert.False(lm.IsDestroyed);
        Assert.Equal(PeriapsisCaptureBurnStatus.Completed, burn.Status);
        // Retain the existing controlled-arrival orbit bounds. The heavier stack
        // has its own duration envelope; no change to the Apollo 8 fixture's bounds.
        Assert.InRange(burn.PeriapsisAltitudeM, 81_000.0, 141_000.0);
        Assert.InRange(burn.ApoapsisAltitudeM, 287_000.0, Apollo11FlightProfile.InitialLunarApoluneAltitudeM);
        Assert.InRange(burn.BurnSeconds, 300.0, 430.0);
    }

    private static (Universe Universe, CelestialBody Moon, Vessel Csm, Vessel Lm)
        CreateDockedArrival(double timeToPeriapsisSeconds)
    {
        string data = SaturnVAscentFlight.DataDirectory();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(data, "parts"));
        var csm = VehicleVariantDefinition.LoadFromJson(Path.Combine(data, "vehicles",
            "apollo11_saturn5_as506_1969.json")).Build(catalog).ToVessel("Columbia", "csm107");
        var les = csm.Parts.Parts.Single(p => p.Definition.HasVehicleRole("launch_escape_system"));
        Assert.NotNull(csm.DeployPayload(les.InstanceId, "LES"));
        Assert.NotNull(csm.Stage());
        Assert.NotNull(csm.Stage());
        Assert.NotNull(csm.Stage());
        var lm = VehicleVariantDefinition.LoadFromJson(Path.Combine(data, "vehicles",
            "apollo11_lm5_eagle_1969.json")).Build(catalog).ToVessel("Eagle", "lm5");
        var universe = Universe.LoadFromDataDirectory(data);
        var moon = universe.GetBody("moon")!;
        double rp = moon.Radius + Apollo11FlightProfile.InitialLunarPeriluneAltitudeM;
        var conic = OrbitalElements.FromStateVector(new Vector3d(rp, 0.0, 0.0),
            new Vector3d(0.0, ArrivalPeriluneSpeedMps, 0.0), moon.GM, moon.Id, timeToPeriapsisSeconds);
        var (position, velocity) = KeplerPropagator.PropagateToTime(conic, 0.0, moon.GM);
        csm.Position = moon.Position + position;
        csm.Velocity = moon.Velocity + velocity;
        csm.Orientation = Quaterniond.FromTo(Vector3d.Up, -velocity.Normalized);
        csm.ReferenceBodyId = moon.Id;
        lm.ReferenceBodyId = moon.Id;
        lm.Orientation = csm.Orientation * Quaterniond.FromAxisAngle(Vector3d.Right, System.Math.PI);
        lm.Position = csm.Position;
        lm.Velocity = csm.Velocity;
        universe.AddVessel(csm);
        universe.AddVessel(lm);
        universe.SetActiveVessel(csm.Id);
        var csmPort = csm.Parts.Parts.Single(p => p.Definition.IsDockingPort);
        var lmPort = lm.Parts.Parts.Single(p => p.Definition.IsDockingPort);
        Assert.True(Universe.TryGetDockingFrame(csm, csmPort, out var probe, out _, out _));
        Assert.True(Universe.TryGetDockingFrame(lm, lmPort, out var drogue, out _, out _));
        lm.Position += probe - drogue;
        var docking = universe.TryDock(csm.Id, csmPort.InstanceId, lm.Id, lmPort.InstanceId, "capture-fixture");
        Assert.True(docking.Succeeded, $"Fixture docking failed: {docking.Failure}");
        return (universe, moon, csm, lm);
    }
}
