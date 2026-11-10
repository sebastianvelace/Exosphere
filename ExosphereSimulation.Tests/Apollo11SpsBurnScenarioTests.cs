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

    /// <summary>
    /// With LM-5 docked the stack's centre of mass (table B-I X_A 1038 in) lies forward of the
    /// SM RCS quads (X_A 959 in), while the CSM's own lies aft of them (934 in). Allocating the
    /// jets about the CSM's own centre picks couples whose moment reverses about the stack's,
    /// so a commanded pitch/yaw turned the stack the wrong way and the pointing law spun it up.
    /// Commands must turn the docked stack the way they turn the CSM alone.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void DockedStackTurnsTheWayItsRcsCommandAsks(int axis)
    {
        var (universe, moon, csm, lm) = CreateDockedArrival(3_000.0);
        universe.Tick(StepSeconds); // Universe publishes the stack reference before each step.
        Assert.NotNull(csm.DockedStackCenterOfMassLocal);
        Assert.True(csm.DockedStackCenterOfMassLocal!.Value.Y > csm.CenterOfMassOffsetLocal.Y);
        csm.SASEnabled = false;
        var command = axis == 0 ? new Vector3d(1.0, 0.0, 0.0) : new Vector3d(0.0, 1.0, 0.0);
        for (int i = 0; i < 50; i++)
        {
            csm.PitchYawRoll = command;
            universe.Tick(StepSeconds);
        }
        var rateLocal = csm.Orientation.Inverse().Rotate(csm.AngularVelocity);
        // PitchYawRoll (pitch, yaw, roll) drives body X, Z and Y (AttitudeGuidance).
        double turned = axis == 0 ? rateLocal.X : rateLocal.Z;
        output.WriteLine($"axis={axis} body rate {rateLocal} stack centre {csm.DockedStackCenterOfMassLocal} own {csm.CenterOfMassOffsetLocal}");
        Assert.True(turned > 0.0, $"commanded +{(axis == 0 ? "pitch" : "yaw")} turned the stack at {turned:E3} rad/s");
        Assert.Single(universe.DockingConnections);
    }

    /// <summary>
    /// IV&amp;V audit A-06. A pitch command asks the SPS gimbal for a fraction of Columbia's own
    /// authority, a moment. Docked, that same moment turns the whole stack, so the rate gained
    /// must fall by the inertia ratio and by nothing else. The allocator took its lever arm
    /// about Columbia's own centre, well aft of the stack's (CSM 934 in, CSM+LM 1038 in), and
    /// delivered the longer arm's moment instead.
    /// </summary>
    [Fact]
    public void SpsGimbalMomentIsTakenAboutTheDockedStackCentre()
    {
        Vector3d RateAfterCommand(bool dock, out double ownInertia, out double stackInertia)
        {
            var (universe, _, csm, _) = CreateDockedArrival(3_000.0, dock: dock);
            var sm = csm.Parts.Parts.Single(p => p.Definition.HasVehicleRole("service_module"));
            sm.Monopropellant = 0.0; // The engine alone turns the vehicle.
            csm.SASEnabled = false;
            csm.Throttle = 1.0;
            for (int i = 0; i < 100; i++)
            {
                csm.PitchYawRoll = new Vector3d(0.1, 0.0, 0.0);
                universe.Tick(StepSeconds);
            }
            Assert.True(csm.GetMaximumThrust(universe.GetBody("moon")!) > 0.0);
            ownInertia = csm.Parts.TransverseMomentOfInertia;
            stackInertia = dock ? csm.DockedStackTransverseInertiaKgM2 : ownInertia;
            return csm.Orientation.Inverse().Rotate(csm.AngularVelocity);
        }

        var alone = RateAfterCommand(false, out double ownAlone, out _);
        var docked = RateAfterCommand(true, out double own, out double stack);
        output.WriteLine($"alone {alone.X:E4} docked {docked.X:E4} rad/s; inertia own {own:E4} stack {stack:E4}");
        Assert.True(alone.X > 1e-3, $"the SPS gimbal turned Columbia at only {alone.X:E3} rad/s");
        Assert.InRange(stack / own, 1.5, 100.0);
        double expected = alone.X * ownAlone / stack;
        Assert.InRange(docked.X, expected * 0.9, expected * 1.1);
    }

    /// <summary>
    /// SP-238: circularization two revolutions after LOI took the 60.0 x 169.7 n mi orbit to
    /// the deliberately non-circular 53.7 x 65.7 n mi target (achieved 53.8 x 65.7) in a
    /// 16.7 s firing that gained 159.3 ft/s (table 7-II, table 8-V). The docked stack here
    /// starts on the flown LOI ellipse; the burn reshapes it on finite SPS thrust.
    /// </summary>
    [Fact]
    [Trait("FidelityGate", "F6")]
    [Trait("GateKind", "historical")]
    public void DockedStackShapesTheFlownNonCircularLunarOrbit()
    {
        const double mile = 1_852.0;
        double moonRadius = 1_737_400.0, gm = 4.9028e12;
        double rp = moonRadius + Apollo11FlightProfile.InitialLunarPeriluneAltitudeM;
        double ra = moonRadius + Apollo11FlightProfile.InitialLunarApoluneAltitudeM;
        double periluneSpeed = System.Math.Sqrt(gm * (2.0 / rp - 2.0 / (rp + ra)));
        var (universe, moon, csm, lm) = CreateDockedArrival(1_200.0, periluneSpeed);
        // Table B-I: the stack weighed 96061.6 lb at LOI ignition and 72037.6 lb at cutoff;
        // take that SPS load out at the engine's nominal 1.6 mixture ratio.
        const double pound = 0.45359237, loiPropellantLb = 96_061.6 - 72_037.6;
        var sm = csm.Parts.Parts.Single(p => p.Definition.HasVehicleRole("service_module"));
        sm.LiquidFuel -= loiPropellantLb / 2.6 * pound;
        sm.Oxidizer -= loiPropellantLb * 1.6 / 2.6 * pound;
        output.WriteLine($"stack {(csm.TotalMass + lm.TotalMass) / pound:F0} lb (table B-I circularization ignition 72019.9 lb)");
        Assert.Equal(moonRadius, moon.Radius);
        Assert.Equal(gm, moon.GM);
        double massBefore = csm.TotalMass + lm.TotalMass;
        double rcsBefore = sm.Monopropellant;
        var burn = new ApsidesTargetBurn(Apollo11FlightProfile.LunarOrbitPericynthionAltitudeM,
            Apollo11FlightProfile.LunarOrbitApocynthionAltitudeM, 120.0);
        double gained = 0.0;
        var previous = (PeriapsisCaptureBurnStatus)(-1);
        while (!burn.IsFinished && universe.CurrentTime < 2_500.0)
        {
            burn.Advance(csm, moon, StepSeconds, universe.CurrentTime);
            if (burn.Status != previous)
            {
                previous = burn.Status;
                output.WriteLine($"t={universe.CurrentTime:F2} state={burn.Status} burn={burn.BurnSeconds:F2} "
                    + $"pe={burn.PeriapsisAltitudeM / 1000:F2} ap={burn.ApoapsisAltitudeM / 1000:F2} dv={burn.RemainingDeltaVMps:F2}");
            }
            double mass = csm.TranslationalMassKg;
            universe.Tick(StepSeconds);
            if (burn.Status == PeriapsisCaptureBurnStatus.Burning)
                gained += csm.GetMaximumThrust(moon) / mass * StepSeconds;
        }
        output.WriteLine($"status={burn.Status} reason={burn.BlockReason} burn={burn.BurnSeconds:F2} "
            + $"propellant={massBefore - csm.TotalMass - lm.TotalMass:F2} gained={gained:F2} "
            + $"pe={burn.PeriapsisAltitudeM / mile:F2} x ap={burn.ApoapsisAltitudeM / mile:F2} n mi "
            + $"rcs={rcsBefore - sm.Monopropellant:F2} kg");

        Assert.Single(universe.DockingConnections);
        Assert.Equal(PeriapsisCaptureBurnStatus.Completed, burn.Status);
        // Target apsides within 1 n mi: the cutoff resolves one 0.02 s step (~0.04 m/s, ~0.1 km).
        Assert.InRange(burn.PeriapsisAltitudeM, (53.7 - 1.0) * mile, (53.7 + 1.0) * mile);
        Assert.InRange(burn.ApoapsisAltitudeM, (65.7 - 1.0) * mile, (65.7 + 1.0) * mile);
        // Velocity gained near the flown 159.3 ft/s (48.6 m/s): an ideal impulse needs about
        // 43 m/s; finite-burn and steering losses add the rest. Duration near the flown 16.7 s.
        Assert.InRange(gained, 40.0, Apollo11FlightProfile.CircularizationDeltaVMps * 1.1);
        Assert.InRange(burn.BurnSeconds, 12.0, 22.0);
        // Acquiring the burn attitude on the docked stack is an RCS slew, not a reservoir drain.
        Assert.InRange(rcsBefore - sm.Monopropellant, 0.0, 50.0);
    }

    private static (Universe Universe, CelestialBody Moon, Vessel Csm, Vessel Lm)
        CreateDockedArrival(double timeToPeriapsisSeconds, double? periluneSpeedMps = null, bool dock = true)
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
            new Vector3d(0.0, periluneSpeedMps ?? ArrivalPeriluneSpeedMps, 0.0), moon.GM, moon.Id, timeToPeriapsisSeconds);
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
        if (!dock)
        {
            lm.Position += csm.Orientation.Rotate(Vector3d.Up) * 500.0;
            return (universe, moon, csm, lm);
        }
        var docking = universe.TryDock(csm.Id, csmPort.InstanceId, lm.Id, lmPort.InstanceId, "capture-fixture");
        Assert.True(docking.Succeeded, $"Fixture docking failed: {docking.Failure}");
        return (universe, moon, csm, lm);
    }
}
