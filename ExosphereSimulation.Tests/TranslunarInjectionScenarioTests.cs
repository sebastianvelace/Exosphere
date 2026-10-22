namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Navigation;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Apollo 8 from Earth parking orbit to the Moon with no state assignment: a finite J-2
/// injection, a perilune-targeted SPS correction, a trim of the Moon-centred approach inside the
/// sphere of influence, then a finite SPS insertion into lunar orbit.
/// </summary>
public sealed class TranslunarInjectionScenarioTests(ITestOutputHelper output)
{
    [Fact]
    public void FiniteInjectionCorrectionAndInsertionReachLunarOrbit()
    {
        var (universe, earth, moon, stack) = ParkingOrbitStack();
        var parking = OrbitalElements.FromStateVector(stack.Position - earth.Position,
            stack.Velocity - earth.Velocity, earth.GM, earth.Id, universe.CurrentTime);
        var plan = LunarTransferPlanner.Compute(earth.GM, moon.GM, moon.Radius, moon.SphereOfInfluence,
            parking, moon.OrbitalElements!, universe.CurrentTime + 3_600.0,
            Apollo8FlightProfile.LoiIgnitionSeconds - Apollo8FlightProfile.TliIgnitionSeconds,
            Apollo8FlightProfile.InitialLunarPeriluneAltitudeM);
        output.WriteLine($"plan: burn at +{plan.BurnTime - universe.CurrentTime:F0}s dv={plan.InjectionDeltaVMag:F0} m/s "
            + $"predicted perilune={(plan.PredictedLunarPeriapsisRadius - moon.Radius) / 1000:F0} km");

        // 1. TLI: finite prograde J-2 burn centred on the planned impulse, cut at the
        //    planned transfer energy.
        double halfBurn = 0.5 * stack.TotalMass * plan.InjectionDeltaVMag / stack.GetMaximumThrust(earth);
        double targetEnergy = -earth.GM / (2.0 * plan.EarthTransferOrbit.SemiMajorAxis);
        var tli = new ProgradeEnergyBurn(targetEnergy, plan.BurnTime - halfBurn, maximumBurnSeconds: 900.0);
        CoastUntil(universe, plan.BurnTime - halfBurn - 120.0, warp: 1_000.0);
        double wetMass = stack.TotalMass;
        Fly(universe, stack, earth, (dt, t) => tli.Advance(stack, earth, dt, t), () => tli.IsFinished,
            plan.BurnTime + 1_500.0);
        output.WriteLine($"TLI {tli.Status} burn={tli.BurnSeconds:F1}s propellant={(wetMass - stack.TotalMass) / 1000:F1} t");
        Assert.Equal(ProgradeEnergyBurnStatus.Completed, tli.Status);

        // 2. CSM separation, then an SPS correction aimed at the perilune altitude (Apollo MCC-2).
        var sivb = stack.Stage();
        Assert.NotNull(sivb);
        universe.AddVessel(sivb!);
        var csm = stack;
        var aim = plan.EarthTransferOrbit.GetStateAtTime(plan.ArrivalTime, earth.GM).position;
        double targetPerilune = moon.Radius + Apollo8FlightProfile.InitialLunarPeriluneAltitudeM;
        CoastUntil(universe, plan.BurnTime + 6.0 * 3_600.0, warp: 1_000.0);
        FlyCorrection(universe, csm, earth, TranslunarCorrection.Create(csm, earth, moon, universe.CurrentTime,
            plan.ArrivalTime, aim, targetPerilune), "MCC");

        // 3. Inside the Moon's sphere of influence, trim the hyperbola's perilune (MCC-4).
        csm.Throttle = 0.0;
        csm.PitchYawRoll = Vector3d.Zero;
        universe.TimeScale = 1_000.0;
        while (!TranslunarCorrection.IsTrimDue(universe, csm, moon) && universe.CurrentTime < plan.ArrivalTime + 86_400.0)
            universe.Tick(1.0 / 60.0);
        FlyCorrection(universe, csm, moon,
            TranslunarCorrection.CreateTrim(csm, moon, universe.CurrentTime, targetPerilune), "trim");

        // 4. Coast to the approach.
        csm.Throttle = 0.0;
        csm.PitchYawRoll = Vector3d.Zero;
        universe.TimeScale = 10_000.0;
        while (universe.GetDominantBody(csm.Position) != moon && universe.CurrentTime < plan.ArrivalTime + 86_400.0)
            universe.Tick(1.0 / 60.0);
        Assert.Equal(moon, universe.GetDominantBody(csm.Position));
        var approach = OrbitalElements.FromStateVector(csm.Position - moon.Position,
            csm.Velocity - moon.Velocity, moon.GM, moon.Id, universe.CurrentTime);
        double perilune = approach.Periapsis - moon.Radius;
        output.WriteLine($"lunar approach: perilune={perilune / 1000:F0} km e={approach.Eccentricity:F3}");
        Assert.InRange(perilune, 96_000.0, 126_000.0); // Apollo 8: 111 km

        // 5. LOI by finite SPS burn.
        var loi = new PeriapsisCaptureBurn(Apollo8FlightProfile.InitialLunarApoluneAltitudeM, 600.0);
        CoastUntil(universe, universe.CurrentTime, warp: 1.0);
        universe.TimeScale = 100.0;
        double timeout = universe.CurrentTime + 86_400.0;
        while (!loi.IsFinished && universe.CurrentTime < timeout)
        {
            double tPe = PeriapsisCaptureBurn.TimeToPeriapsis(csm.Position - moon.Position,
                csm.Velocity - moon.Velocity, moon.GM);
            universe.TimeScale = loi.Status == PeriapsisCaptureBurnStatus.Coasting && tPe > 900.0 ? 100.0 : 1.0;
            loi.Advance(csm, moon, 0.02 * universe.TimeScale, universe.CurrentTime);
            universe.Tick(0.02);
        }
        output.WriteLine($"LOI {loi.Status} burn={loi.BurnSeconds:F1}s orbit={loi.PeriapsisAltitudeM / 1000:F0} x {loi.ApoapsisAltitudeM / 1000:F0} km");
        Assert.Equal(PeriapsisCaptureBurnStatus.Completed, loi.Status);
        Assert.False(csm.IsDestroyed);
        Assert.InRange(loi.PeriapsisAltitudeM, 30_000.0, 300_000.0);
        Assert.InRange(loi.ApoapsisAltitudeM, 0.0, Apollo8FlightProfile.InitialLunarApoluneAltitudeM);
    }

    private void FlyCorrection(Universe universe, Vessel vessel, CelestialBody body, LambertGuidedBurn burn,
        string label)
    {
        double initial = double.NaN;
        Fly(universe, vessel, body, (dt, t) =>
        {
            burn.Advance(vessel, body, dt, t);
            if (double.IsNaN(initial)) initial = burn.RemainingDeltaVMps;
        }, () => burn.IsFinished, universe.CurrentTime + 1_200.0);
        output.WriteLine($"{label} {burn.Status} initial dv={initial:F2} m/s burn={burn.BurnSeconds:F1}s");
        Assert.Equal(LambertGuidedBurnStatus.Completed, burn.Status);
        vessel.PitchYawRoll = Vector3d.Zero;
    }

    private static void CoastUntil(Universe universe, double time, double warp)
    {
        universe.TimeScale = warp;
        while (universe.CurrentTime < time - 1e-6)
            universe.Tick(System.Math.Min(1.0 / 60.0, (time - universe.CurrentTime) / warp + 1e-9));
        universe.TimeScale = 1.0;
    }

    private static void Fly(Universe universe, Vessel vessel, CelestialBody body,
        Action<double, double> advance, Func<bool> finished, double timeout)
    {
        universe.TimeScale = 1.0;
        while (!finished() && universe.CurrentTime < timeout)
        {
            advance(0.02, universe.CurrentTime);
            universe.Tick(0.02);
        }
        vessel.Throttle = 0.0;
    }

    private static (Universe Universe, CelestialBody Earth, CelestialBody Moon, Vessel Stack) ParkingOrbitStack()
    {
        var root = DataDirectory();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(root, "parts"));
        var assembly = new VesselAssembly(catalog);
        string[] stackTopToBottom =
        [
            "apollo8_command_module_csm103", "apollo8_service_module_csm103", "apollo8_sps_engine_csm103",
            "apollo8_csm_sivb_separation_ring", "apollo8_sla_ltab", "saturn5_instrument_unit_as503",
            "saturn5_sivb_tank_as503", "saturn5_j2_sivb_engine_as503",
        ];
        var current = assembly.AddRoot(stackTopToBottom[0]);
        foreach (var id in stackTopToBottom.Skip(1))
            current = assembly.AttachPartAutomatically(current.InstanceId, id);
        var stack = assembly.ToVessel("Apollo 8 S-IVB/CSM", "apollo8-sivb");

        var universe = Universe.LoadFromDataDirectory(root);
        var earth = universe.GetBody("earth")!;
        var moon = universe.GetBody("moon")!;
        // Parking orbit in the Moon's orbital plane, as a launch window would provide.
        var moonRelative = moon.Position - earth.Position;
        var moonNormal = moonRelative.Cross(moon.Velocity - earth.Velocity).Normalized;
        double radius = earth.Radius + Apollo8FlightProfile.ParkingOrbitAltitudeM;
        var up = (-moonRelative).Normalized;
        var tangent = moonNormal.Cross(up).Normalized;
        stack.Position = earth.Position + up * radius;
        stack.Velocity = earth.Velocity + tangent * System.Math.Sqrt(earth.GM / radius);
        stack.Orientation = Quaterniond.FromTo(Vector3d.Up, tangent);
        stack.ReferenceBodyId = earth.Id;
        universe.AddVessel(stack);
        universe.ActiveVessel = stack;
        return (universe, earth, moon, stack);
    }

    private static string DataDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "data", "bodies")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "data");
    }
}
