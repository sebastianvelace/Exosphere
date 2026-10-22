namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Navigation;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Gemini 8 from its insertion orbit to a real docking with Agena 5003, on OAMS translation
/// thrusters alone: a terminal-phase-initiation Lambert intercept, a mid-transfer correction,
/// then braking and a final axial approach to the docking-port capture envelope. No state is
/// assigned; propellant comes out of the equipment section's OAMS load.
/// </summary>
public sealed class Gemini8RendezvousScenarioTests(ITestOutputHelper output)
{
    [Fact]
    public void OamsInterceptAndProximityOperationsDockWithTheAgena()
    {
        var (universe, earth, gemini, agena) = InsertionGeometry();
        double oamsStart = gemini.Parts.TotalMonopropellant;
        var chaserOrbit = OrbitalElements.FromStateVector(gemini.Position - earth.Position,
            gemini.Velocity - earth.Velocity, earth.GM, earth.Id, universe.CurrentTime);
        var targetOrbit = OrbitalElements.FromStateVector(agena.Position - earth.Position,
            agena.Velocity - earth.Velocity, earth.GM, earth.Id, universe.CurrentTime);
        var plan = RendezvousPlanner.PlanIntercept(earth.GM, chaserOrbit, targetOrbit,
            universe.CurrentTime + 600.0, 6.0 * 3_600.0, standoffM: 500.0);
        output.WriteLine($"TPI at +{(plan.BurnTime - universe.CurrentTime) / 60:F0} min: dv={plan.InitiationDeltaV.Magnitude:F1} "
            + $"+ braking {plan.BrakingDeltaV.Magnitude:F1} m/s");

        // 1. TPI replanned from the flown states just before it (the coast is integrated with
        //    oblateness and drag, the plan with two-body conics), then a correction halfway
        //    through that re-aims at the Agena's position propagated from its current state.
        Coast(universe, plan.BurnTime - 900.0, warp: 100.0);
        plan = RendezvousPlanner.PlanIntercept(earth.GM, Osculating(gemini, earth, universe.CurrentTime),
            Osculating(agena, earth, universe.CurrentTime), universe.CurrentTime + 300.0, 1_800.0, standoffM: 500.0);
        output.WriteLine($"replanned TPI: dv={plan.InitiationDeltaV.Magnitude:F1} m/s");
        // Low OAMS thrust makes the TPI a several-minute burn: centre it on the impulsive epoch.
        double tpiIgnition = plan.BurnTime - RendezvousPlanner.HalfBurnSeconds(gemini, plan.InitiationDeltaV.Magnitude);
        foreach (double ignition in new[] { tpiIgnition, 0.5 * (plan.BurnTime + plan.ArrivalTime) })
        {
            Coast(universe, ignition, warp: 100.0);
            var aim = RendezvousPlanner.AimPosition(Osculating(agena, earth, universe.CurrentTime), earth.GM,
                plan.ArrivalTime, standoffM: 500.0);
            var normal = (gemini.Position - earth.Position).Cross(gemini.Velocity - earth.Velocity);
            var burn = new TranslationLambertBurn(aim, plan.ArrivalTime, ignition, normal);
            Fly(universe, () => burn.Advance(gemini, earth, 0.02, universe.CurrentTime), () => burn.IsFinished,
                universe.CurrentTime + 900.0);
            output.WriteLine($"burn {burn.Status} {burn.BurnSeconds:F1} s {burn.BlockReason}");
            Assert.Equal(TranslationBurnStatus.Completed, burn.Status);
        }

        // 2. Proximity operations from the braking hand-over range: brake to the axis waypoint, then dock.
        universe.TimeScale = 10.0;
        while ((gemini.Position - agena.Position).Magnitude > ProximityApproach.HandoverRangeM(gemini, plan.BrakingDeltaV.Magnitude)
               && universe.CurrentTime < plan.ArrivalTime + 1_800.0)
            universe.Tick(1.0 / 50.0);
        universe.TimeScale = 1.0;
        var geminiPort = gemini.Parts.Parts.First(p => p.Definition.HasVehicleRole("gemini_docking_port"));
        var agenaPort = agena.Parts.Parts.First(p => p.Definition.HasVehicleRole("agena_target_docking_port"));
        var approach = new ProximityApproach();
        DockingAttempt? docking = null;
        double deadline = universe.CurrentTime + 3_600.0;
        while (universe.CurrentTime < deadline && docking?.Succeeded != true)
        {
            approach.Advance(gemini, geminiPort, agena, agenaPort, earth);
            if (approach.IsOnFinalApproach && approach.PortDistanceM < 0.65)
                docking = universe.TryDock(agena.Id, agenaPort.InstanceId, gemini.Id, geminiPort.InstanceId,
                    "gemini8-agena-docking");
            universe.Tick(0.02);
        }
        double oamsUsed = oamsStart - gemini.Parts.TotalMonopropellant;
        output.WriteLine($"docking {docking?.Succeeded} failure={docking?.Failure} port gap={approach.PortDistanceM:F2} m "
            + $"OAMS used={oamsUsed:F1} kg of {oamsStart:F0}");
        Assert.True(docking?.Succeeded, $"Docking failed: {docking?.Failure}");
        Assert.True(oamsUsed < oamsStart, "OAMS propellant exhausted.");
    }

    private static OrbitalElements Osculating(Vessel vessel, CelestialBody earth, double epoch) =>
        OrbitalElements.FromStateVector(vessel.Position - earth.Position, vessel.Velocity - earth.Velocity,
            earth.GM, earth.Id, epoch);

    private static void Coast(Universe universe, double time, double warp)
    {
        universe.TimeScale = warp;
        while (universe.CurrentTime < time - 1e-6)
            universe.Tick(System.Math.Min(1.0 / 60.0, (time - universe.CurrentTime) / warp + 1e-9));
        universe.TimeScale = 1.0;
    }

    private static void Fly(Universe universe, Action advance, Func<bool> finished, double timeout)
    {
        universe.TimeScale = 1.0;
        while (!finished() && universe.CurrentTime < timeout)
        {
            advance();
            universe.Tick(0.02);
        }
    }

    private static (Universe, CelestialBody, Vessel Gemini, Vessel Agena) InsertionGeometry()
    {
        var root = SaturnVAscentFlight.DataDirectory();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(root, "parts"));
        var assembly = new VesselAssembly(catalog);
        var current = assembly.AddRoot("gemini8_docking_nose");
        foreach (var id in new[] { "gemini8_reentry_module", "gemini8_retro_module", "gemini8_equipment_adapter" })
            current = assembly.AttachPartAutomatically(current.InstanceId, id);
        var gemini = assembly.ToVessel("Spacecraft 8", "gemini8");
        var agena = VehicleVariantDefinition.LoadFromJson(Path.Combine(root, "vehicles",
            "agena8_target_5003_1966.json")).Build(catalog).ToVessel("Agena 5003", "agena5003");

        var universe = Universe.LoadFromDataDirectory(root);
        var earth = universe.GetBody("earth")!;
        var normal = new Vector3d(0.0, 0.0, 1.0);
        // Agena on its 298.7 km circular orbit.
        double targetRadius = earth.Radius + Gemini8FlightProfile.HistoricalTargetOrbitM;
        var targetUp = Vector3d.Right;
        var targetTangent = normal.Cross(targetUp);
        agena.Position = earth.Position + targetUp * targetRadius;
        agena.Velocity = earth.Velocity + targetTangent * System.Math.Sqrt(earth.GM / targetRadius);
        agena.Orientation = Quaterniond.FromTo(Vector3d.Up, targetTangent);
        agena.ReferenceBodyId = earth.Id;
        // Spacecraft 8 at the perigee of the 145 x 152 km orbit the flown Titan ascent reaches
        // (Gemini8AscentScenarioTests), ~16.5 deg behind the Agena.
        double perigee = earth.Radius + 145_000.0;
        double apogee = earth.Radius + 152_000.0;
        var chaserUp = Quaterniond.FromAxisAngle(normal, -16.5 * MathUtils.DEG_TO_RAD).Rotate(targetUp);
        var chaserTangent = normal.Cross(chaserUp);
        gemini.Position = earth.Position + chaserUp * perigee;
        gemini.Velocity = earth.Velocity + chaserTangent
            * System.Math.Sqrt(earth.GM * (2.0 / perigee - 2.0 / (perigee + apogee)));
        gemini.Orientation = Quaterniond.FromTo(Vector3d.Up, chaserTangent);
        gemini.ReferenceBodyId = earth.Id;
        gemini.SASEnabled = false;
        universe.AddVessel(agena);
        universe.AddVessel(gemini);
        universe.ActiveVessel = gemini;
        return (universe, earth, gemini, agena);
    }
}
