namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Integrators;
using Exosphere.Simulation.Math;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Apollo 8 lunar orbit insertion flown as a finite SPS burn: the CSM arrives on a
/// hyperbolic approach and the integrated engine, propellant and gravity must produce the
/// historical 60 x 168.5 nmi orbit, then circularize it, without any state assignment.
/// </summary>
public sealed class LunarOrbitInsertionBurnTests(ITestOutputHelper output)
{
    private const double ArrivalPeriluneSpeedMps = 2_580.0;

    [Fact]
    public void FiniteSpsBurnCapturesTheHistoricalEllipse()
    {
        var (universe, moon, csm) = ApproachingCsm();
        double wetMass = csm.TotalMass;
        var burn = new PeriapsisCaptureBurn(Apollo8FlightProfile.InitialLunarApoluneAltitudeM, 400.0);

        RunUntilFinished(universe, moon, csm, burn, maximumSeconds: 4_500.0);

        output.WriteLine($"status={burn.Status} burn={burn.BurnSeconds:F1}s pe={burn.PeriapsisAltitudeM / 1000:F1}km "
            + $"ap={burn.ApoapsisAltitudeM / 1000:F1}km propellant={(wetMass - csm.TotalMass) / 1000:F2}t");
        Assert.Equal(PeriapsisCaptureBurnStatus.Completed, burn.Status);
        Assert.InRange(burn.PeriapsisAltitudeM, 81_000.0, 141_000.0);
        Assert.InRange(burn.ApoapsisAltitudeM, 287_000.0, Apollo8FlightProfile.InitialLunarApoluneAltitudeM);
        // Historical LOI-1 burn: 246.9 s.
        Assert.InRange(burn.BurnSeconds, 200.0, 300.0);
        double idealPropellant = wetMass * (1.0 - System.Math.Exp(
            -Apollo8FlightProfile.LoiDeltaVMps / (314.8 * 9.80665)));
        Assert.InRange(wetMass - csm.TotalMass, idealPropellant * 0.85, idealPropellant * 1.2);
    }

    [Fact]
    public void SecondBurnCircularizesTheCapturedOrbit()
    {
        var (universe, moon, csm) = ApproachingCsm();
        var capture = new PeriapsisCaptureBurn(Apollo8FlightProfile.InitialLunarApoluneAltitudeM, 400.0);
        RunUntilFinished(universe, moon, csm, capture, maximumSeconds: 4_500.0);
        Assert.Equal(PeriapsisCaptureBurnStatus.Completed, capture.Status);

        // Coast past periapsis before arming the circularization burn at the next one.
        for (int step = 0; step < 50 * 1_800; step++) universe.Tick(0.02);
        var circularize = new PeriapsisCaptureBurn(
            Apollo8FlightProfile.CircularLunarOrbitAltitudeM + 5_000.0, 120.0);
        RunUntilFinished(universe, moon, csm, circularize, maximumSeconds: 9_000.0);

        output.WriteLine($"circularize status={circularize.Status} burn={circularize.BurnSeconds:F1}s "
            + $"pe={circularize.PeriapsisAltitudeM / 1000:F1}km ap={circularize.ApoapsisAltitudeM / 1000:F1}km");
        Assert.Equal(PeriapsisCaptureBurnStatus.Completed, circularize.Status);
        Assert.InRange(circularize.ApoapsisAltitudeM - circularize.PeriapsisAltitudeM, 0.0, 40_000.0);
        Assert.InRange(circularize.PeriapsisAltitudeM, 70_000.0, 150_000.0);
        // Historical LOI-2 burn: 9.2 s.
        Assert.InRange(circularize.BurnSeconds, 3.0, 40.0);
    }

    [Fact]
    public void TimeToPeriapsisMatchesKeplerPropagation()
    {
        double gm = 4.9028e12;
        var rp = new Vector3d(1_848_500.0, 0.0, 0.0);
        var vp = new Vector3d(0.0, ArrivalPeriluneSpeedMps, 0.0);
        var conic = OrbitalElements.FromStateVector(rp, vp, gm, "moon", 3_000.0);
        var (position, velocity) = KeplerPropagator.PropagateToTime(conic, 0.0, gm);
        Assert.InRange(PeriapsisCaptureBurn.TimeToPeriapsis(position, velocity, gm), 2_995.0, 3_005.0);
        // Outbound on an open conic there is no next periapsis.
        Assert.True(double.IsNaN(PeriapsisCaptureBurn.TimeToPeriapsis(position, -velocity, gm)));
    }

    [Fact]
    public void TimeToPeriapsisOnAnEllipseCountsToTheNextPassage()
    {
        double gm = 4.9028e12;
        var rp = new Vector3d(1_848_500.0, 0.0, 0.0);
        var vp = new Vector3d(0.0, 1_670.0, 0.0);
        var orbit = OrbitalElements.FromStateVector(rp, vp, gm, "moon", 0.0);
        double period = 2.0 * System.Math.PI * System.Math.Sqrt(
            System.Math.Pow(orbit.SemiMajorAxis, 3) / gm);
        var (outbound, outboundVelocity) = KeplerPropagator.PropagateToTime(orbit, 1_000.0, gm);
        Assert.InRange(PeriapsisCaptureBurn.TimeToPeriapsis(outbound, outboundVelocity, gm),
            period - 1_005.0, period - 995.0);
    }

    private void RunUntilFinished(Universe universe, CelestialBody moon, Vessel csm,
        PeriapsisCaptureBurn burn, double maximumSeconds)
    {
        double start = universe.CurrentTime;
        var last = (PeriapsisCaptureBurnStatus)(-1);
        while (!burn.IsFinished && universe.CurrentTime - start < maximumSeconds)
        {
            burn.Advance(csm, moon, 0.02, universe.CurrentTime);
            if (burn.Status != last)
            {
                last = burn.Status;
                var rel = csm.Position - moon.Position; var vel = csm.Velocity - moon.Velocity;
                output.WriteLine($"  t={universe.CurrentTime - start:F1} {burn.Status} alt={(rel.Magnitude - moon.Radius) / 1000:F1}km "
                    + $"pe={burn.PeriapsisAltitudeM / 1000:F1} ap={burn.ApoapsisAltitudeM / 1000:F1} "
                    + $"tPe={PeriapsisCaptureBurn.TimeToPeriapsis(rel, vel, moon.GM):F0}");
            }
            universe.Tick(0.02);
        }
        burn.Advance(csm, moon, 0.02, universe.CurrentTime);
    }

    private static (Universe Universe, CelestialBody Moon, Vessel Csm) ApproachingCsm()
    {
        var root = DataDirectory();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(root, "parts"));
        var assembly = new VesselAssembly(catalog);
        var current = assembly.AddRoot("apollo8_command_module_csm103");
        current = assembly.AttachPartAutomatically(current.InstanceId, "apollo8_service_module_csm103");
        assembly.AttachPartAutomatically(current.InstanceId, "apollo8_sps_engine_csm103");
        var csm = assembly.ToVessel("CSM-103", "csm-103");

        var universe = Universe.LoadFromDataDirectory(root);
        var moon = universe.GetBody("moon")!;
        // Seed the arrival hyperbola from its periapsis, then back-propagate 3000 s.
        double periapsisRadius = moon.Radius + Apollo8FlightProfile.InitialLunarPeriluneAltitudeM;
        var rp = new Vector3d(periapsisRadius, 0.0, 0.0);
        var vp = new Vector3d(0.0, ArrivalPeriluneSpeedMps, 0.0);
        var conic = OrbitalElements.FromStateVector(rp, vp, moon.GM, moon.Id, 3_000.0);
        var (position, velocity) = KeplerPropagator.PropagateToTime(conic, 0.0, moon.GM);
        csm.Position = moon.Position + position;
        csm.Velocity = moon.Velocity + velocity;
        csm.Orientation = Quaterniond.FromTo(Vector3d.Up, -velocity.Normalized);
        csm.ReferenceBodyId = moon.Id;
        universe.AddVessel(csm);
        universe.ActiveVessel = csm;
        return (universe, moon, csm);
    }

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data", "bodies")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }
}
