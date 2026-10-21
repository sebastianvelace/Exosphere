namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Navigation;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Apollo 8 from lunar orbit to splashdown with no state assignment: a finite prograde SPS
/// transearth injection cut on the planned Moon-relative energy, the transearth coast, a
/// correction to the entry-corridor perigee, CM/SM separation, and an aerodynamically stable
/// heat-shield-first entry to a parachute splashdown.
/// </summary>
public sealed class TransearthReturnScenarioTests(ITestOutputHelper output)
{
    private const double PostLunarOrbitCsmMassKg = 20_252.0;

    [Fact]
    public void FiniteTeiCorrectionAndEntryReachSplashdown()
    {
        var (universe, earth, moon, csm) = LunarOrbitCsm();
        var lunarOrbit = OrbitalElements.FromStateVector(csm.Position - moon.Position,
            csm.Velocity - moon.Velocity, moon.GM, moon.Id, universe.CurrentTime);
        var plan = TransearthInjectionPlanner.Compute(earth.GM, moon.GM, moon.OrbitalElements!, lunarOrbit,
            universe.CurrentTime + 600.0, Apollo8FlightProfile.TransearthTimeOfFlightSeconds,
            earth.Radius + Apollo8FlightProfile.EntryCorridorVacuumPerigeeAltitudeM,
                maximumTimeOfFlight: Apollo8FlightProfile.MaximumTransearthTimeOfFlightSeconds);
        output.WriteLine($"plan: burn +{plan.BurnTime - universe.CurrentTime:F0}s dv={plan.ImpulsiveDeltaV:F0} m/s "
            + $"vinf={plan.HyperbolicExcessVelocity.Magnitude:F0} m/s asymptoteError="
            + $"{plan.AsymptoteErrorRad * MathUtils.RAD_TO_DEG:F1} deg");

        // 1. TEI: finite prograde SPS burn centred on the impulse, cut on Moon-relative energy.
        double halfBurn = 0.5 * csm.TotalMass * plan.ImpulsiveDeltaV / csm.GetMaximumThrust(moon);
        var tei = new ProgradeEnergyBurn(plan.TargetSelenocentricEnergy, plan.BurnTime - halfBurn,
            maximumBurnSeconds: 600.0);
        CoastUntil(universe, plan.BurnTime - halfBurn - 120.0, warp: 100.0);
        Fly(universe, csm, (dt, t) => tei.Advance(csm, moon, dt, t), () => tei.IsFinished, plan.BurnTime + 900.0);
        output.WriteLine($"TEI {tei.Status} burn={tei.BurnSeconds:F1}s mass left={csm.TotalMass:F0} kg "
            + $"coast={(plan.ArrivalTime - plan.BurnTime) / 3600:F1} h");
        Assert.Equal(ProgradeEnergyBurnStatus.Completed, tei.Status);

        // 2. Coast out of the Moon's sphere of influence, then the scheduled corrections to the
        //    corridor perigee: one after escape, trims 8 h and 2 h out (Apollo MCC-5/MCC-7).
        universe.TimeScale = 1_000.0;
        while (universe.GetDominantBody(csm.Position) != earth && universe.CurrentTime < plan.ArrivalTime)
            universe.Tick(1.0 / 60.0);
        Assert.Equal(earth, universe.GetDominantBody(csm.Position));
        double earthReturn = universe.CurrentTime;
        double perigeeAltitude = double.NaN;
        for (int index = 0; index < TransearthCorrection.CorrectionCount; index++)
        {
            universe.TimeScale = index == TransearthCorrection.CorrectionCount - 1 ? 100.0 : 1_000.0;
            while (!TransearthCorrection.IsDue(index, universe.CurrentTime - earthReturn,
                       PeriapsisCaptureBurn.TimeToPeriapsis(csm.Position - earth.Position,
                           csm.Velocity - earth.Velocity, earth.GM)))
                universe.Tick(1.0 / 60.0);
            var burn = TransearthCorrection.Create(csm, earth, universe.CurrentTime,
                earth.Radius + Apollo8FlightProfile.EntryCorridorVacuumPerigeeAltitudeM,
                TransearthCorrection.CutoffDeltaVFor(index));
            double initial = double.NaN;
            Fly(universe, csm, (dt, t) =>
            {
                burn.Advance(csm, earth, dt, t);
                if (double.IsNaN(initial)) initial = burn.RemainingDeltaVMps;
            }, () => burn.IsFinished, universe.CurrentTime + 1_200.0);
            csm.PitchYawRoll = Vector3d.Zero;
            var corrected = OrbitalElements.FromStateVector(csm.Position - earth.Position,
                csm.Velocity - earth.Velocity, earth.GM, earth.Id, universe.CurrentTime);
            perigeeAltitude = corrected.Periapsis - earth.Radius;
            output.WriteLine($"correction {index + 1} {burn.Status} initial dv={initial:F2} m/s vacuum perigee="
                + $"{perigeeAltitude / 1000:F1} km");
            Assert.Equal(LambertGuidedBurnStatus.Completed, burn.Status);
        }
        Assert.InRange(perigeeAltitude, 33_000.0, 43_000.0);

        // 3. Coast to the entry interface; CM/SM separation before it.
        csm.Throttle = 0.0;
        csm.PitchYawRoll = Vector3d.Zero;
        universe.TimeScale = 1_000.0;
        universe.TimeScale = 10.0;
        while (earth.GetAltitude(csm.Position) > 2_000_000.0 && universe.CurrentTime < plan.ArrivalTime + 86_400.0)
            universe.Tick(1.0 / 60.0);
        var sm = csm.Parts.Parts.First(p => p.Definition.HasVehicleRole("service_module"));
        universe.AddVessel(csm.DeployPayload(sm.InstanceId, "SM", new Vector3d(0.0, -0.5, 0.0))!);
        var cm = csm;
        universe.TimeScale = 10.0;
        while (earth.GetAltitude(cm.Position) > Apollo8FlightProfile.EntryInterfaceAltitudeM)
        {
            HoldHeatShieldForward(cm, earth);
            universe.Tick(1.0 / 50.0);
        }
        universe.TimeScale = 1.0;
        var relative = cm.Velocity - earth.Velocity;
        var up = (cm.Position - earth.Position).Normalized;
        double flightPathDeg = System.Math.Asin(relative.Normalized.Dot(up)) * MathUtils.RAD_TO_DEG;
        output.WriteLine($"entry interface: v={relative.Magnitude:F0} m/s gamma={flightPathDeg:F2} deg");
        Assert.InRange(flightPathDeg, -7.5, -5.5);

        // 4. Aerodynamically stable heat-shield-first entry, drogues at 7.3 km, splashdown.
        double peakG = 0.0, lastSpeed = cm.GetSurfaceVelocity(earth).Magnitude;
        bool chutes = false;
        for (int step = 0; step < 50 * 3_000 && !cm.IsDestroyed; step++)
        {
            double altitude = earth.GetAltitude(cm.Position);
            if (altitude <= 2.0) break;
            HoldHeatShieldForward(cm, earth);
            if (!chutes && altitude <= 7_300.0) chutes = cm.DeployParachutes(earth) > 0;
            universe.Tick(0.02);
            double speed = cm.GetSurfaceVelocity(earth).Magnitude;
            // Entry deceleration only: parachute snatch and water contact are separate loads.
            if (altitude > 10_000.0)
                peakG = System.Math.Max(peakG, System.Math.Abs(lastSpeed - speed) / 0.02 / 9.80665);
            lastSpeed = speed;
        }
        output.WriteLine($"splashdown: destroyed={cm.IsDestroyed} chutes={chutes} speed={lastSpeed:F1} m/s peak={peakG:F1} g");
        Assert.False(cm.IsDestroyed);
        Assert.True(chutes);
        Assert.InRange(peakG, 4.0, 9.0); // Apollo 8 peak: 6.8 g
        Assert.True(lastSpeed < 11.0, $"Splashdown at {lastSpeed:F1} m/s.");
    }

    /// <summary>Trim attitude before the air bites, then lift-vector bank modulation.</summary>
    private static void HoldHeatShieldForward(Vessel cm, CelestialBody earth) =>
        cm.PitchYawRoll = CapsuleEntryGuidance.ComputeCommand(cm, earth);

    private static void CoastUntil(Universe universe, double time, double warp)
    {
        universe.TimeScale = warp;
        while (universe.CurrentTime < time - 1e-6)
            universe.Tick(System.Math.Min(1.0 / 60.0, (time - universe.CurrentTime) / warp + 1e-9));
        universe.TimeScale = 1.0;
    }

    private static void Fly(Universe universe, Vessel vessel, Action<double, double> advance,
        Func<bool> finished, double timeout)
    {
        universe.TimeScale = 1.0;
        while (!finished() && universe.CurrentTime < timeout)
        {
            advance(0.02, universe.CurrentTime);
            universe.Tick(0.02);
        }
        vessel.Throttle = 0.0;
    }

    private static (Universe Universe, CelestialBody Earth, CelestialBody Moon, Vessel Csm) LunarOrbitCsm()
    {
        var root = SaturnVAscentFlight.DataDirectory();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(root, "parts"));
        var assembly = new VesselAssembly(catalog);
        var current = assembly.AddRoot("apollo8_command_module_csm103");
        current = assembly.AttachPartAutomatically(current.InstanceId, "apollo8_service_module_csm103");
        assembly.AttachPartAutomatically(current.InstanceId, "apollo8_sps_engine_csm103");
        var csm = assembly.ToVessel("Apollo 8 CSM", "apollo8-csm");
        // The SPS budget left after LOI and circularization in the flown mission (20.25 t), not a
        // full service module: TEI must close on what the real sequence leaves.
        double excess = csm.TotalMass - PostLunarOrbitCsmMassKg;
        foreach (var tank in csm.Parts.Parts.Where(p => p.LiquidFuel + p.Oxidizer > 0.0))
        {
            double propellant = tank.LiquidFuel + tank.Oxidizer;
            double removed = System.Math.Min(propellant, excess);
            tank.LiquidFuel -= removed * tank.LiquidFuel / propellant;
            tank.Oxidizer -= removed * tank.Oxidizer / propellant;
            excess -= removed;
        }

        var universe = Universe.LoadFromDataDirectory(root);
        var earth = universe.GetBody("earth")!;
        var moon = universe.GetBody("moon")!;
        // Circular retrograde lunar orbit in the Moon's orbital plane, as flown after LOI.
        var moonRelative = moon.Position - earth.Position;
        var moonNormal = moonRelative.Cross(moon.Velocity - earth.Velocity).Normalized;
        double radius = moon.Radius + Apollo8FlightProfile.CircularLunarOrbitAltitudeM;
        var up = moonRelative.Normalized;
        var tangent = up.Cross(moonNormal).Normalized;
        csm.Position = moon.Position + up * radius;
        csm.Velocity = moon.Velocity + tangent * System.Math.Sqrt(moon.GM / radius);
        csm.Orientation = Quaterniond.FromTo(Vector3d.Up, tangent);
        csm.ReferenceBodyId = moon.Id;
        csm.SASEnabled = false;
        universe.AddVessel(csm);
        universe.ActiveVessel = csm;
        return (universe, earth, moon, csm);
    }
}
