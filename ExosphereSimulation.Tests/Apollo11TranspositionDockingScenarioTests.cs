namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Apollo 11 transposition, docking and extraction with no state assignment: the CSM leaves
/// the S-IVB/SLA on the separation impulse, turns its probe toward Eagle's drogue and closes
/// on SM RCS translation alone until the real port capture docks the two vehicles.
/// </summary>
public sealed class Apollo11TranspositionDockingScenarioTests(ITestOutputHelper output)
{
    [Fact]
    public void CsmTurnsAroundAndDocksWithEagleOnSmRcs()
    {
        var root = SaturnVAscentFlight.DataDirectory();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(root, "parts"));
        var universe = Universe.LoadFromDataDirectory(root);
        var earth = universe.GetBody("earth")!;
        var columbia = VehicleVariantDefinition.LoadFromJson(Path.Combine(root, "vehicles",
            "apollo11_saturn5_as506_1969.json")).Build(catalog).ToVessel("Columbia", "apollo11-columbia");

        double radius = earth.Radius + Apollo11FlightProfile.ParkingOrbitAltitudeM;
        var tangent = Vector3d.Forward;
        columbia.Position = earth.Position + Vector3d.Right * radius;
        columbia.Velocity = earth.Velocity + tangent * System.Math.Sqrt(earth.GM / radius);
        columbia.Orientation = Quaterniond.FromTo(Vector3d.Up, tangent);
        columbia.ReferenceBodyId = earth.Id;
        string lesId = columbia.Parts.Parts.Single(part =>
            part.Definition.HasVehicleRole("launch_escape_system")).InstanceId;
        Assert.NotNull(columbia.DeployPayload(lesId, "LES"));
        Assert.NotNull(columbia.Stage());
        Assert.NotNull(columbia.Stage());
        var sivbSla = Assert.IsType<Vessel>(columbia.Stage());
        universe.AddVessel(columbia);
        universe.AddVessel(sivbSla);
        universe.SetActiveVessel(columbia.Id);

        // Eagle sits in the SLA with its drogue facing the departing CSM (SimulationBridge
        // extraction geometry).
        var eagle = VehicleVariantDefinition.LoadFromJson(Path.Combine(root, "vehicles",
            "apollo11_lm5_eagle_1969.json")).Build(catalog).ToVessel("Eagle", Apollo11FlightProfile.EagleVesselId);
        eagle.Position = sivbSla.Position + sivbSla.Orientation.Rotate(Vector3d.Up) * 12.0;
        eagle.Velocity = sivbSla.Velocity;
        eagle.Orientation = sivbSla.Orientation;
        eagle.ReferenceBodyId = earth.Id;
        eagle.SASEnabled = true;
        universe.AddVessel(eagle);

        var cmPort = columbia.Parts.Parts.Single(part => part.Definition.IsDockingPort);
        var lmPort = eagle.Parts.Parts.Single(part => part.Definition.IsDockingPort);
        double monoBefore = columbia.Parts.Parts.Sum(part => part.Monopropellant);
        double spsBefore = columbia.Parts.Parts.Sum(part => part.LiquidFuel + part.Oxidizer);
        Assert.True(monoBefore > 0.0, "The SM must carry its RCS load.");

        var approach = new ProximityApproach();
        double start = universe.CurrentTime;
        DockingAttempt? docking = null;
        double minimumAxialClearance = double.PositiveInfinity;
        while (universe.CurrentTime - start < 1_800.0)
        {
            approach.Advance(columbia, cmPort, eagle, lmPort, earth);
            universe.Tick(0.02);
            // The probe must come at the drogue from outside Eagle, never through the LM.
            Assert.True(Universe.TryGetDockingFrame(columbia, cmPort, out var probe, out _, out _));
            Assert.True(Universe.TryGetDockingFrame(eagle, lmPort, out var drogue, out var drogueAxis, out _));
            minimumAxialClearance = System.Math.Min(minimumAxialClearance, (probe - drogue).Dot(drogueAxis));
            if (!approach.IsOnFinalApproach || approach.PortDistanceM > 0.65) continue;
            var attempt = universe.TryDock(columbia.Id, cmPort.InstanceId, eagle.Id, lmPort.InstanceId,
                Apollo11FlightProfile.DockingConnectionId);
            if (attempt.Succeeded) { docking = attempt; break; }
        }

        double monoUsed = monoBefore - columbia.Parts.Parts.Sum(part => part.Monopropellant);
        output.WriteLine($"docked={docking != null} after {universe.CurrentTime - start:F0} s, "
            + $"RCS used {monoUsed:F1} kg, port distance {approach.PortDistanceM:F2} m, "
            + $"minimum axial clearance {minimumAxialClearance:F2} m");
        Assert.NotNull(docking);
        // Transposition: the CSM ends nose to nose with Eagle.
        Assert.True(columbia.Orientation.Rotate(Vector3d.Up).Dot(eagle.Orientation.Rotate(Vector3d.Up)) < -0.97);
        Assert.True(minimumAxialClearance > -0.05, $"Probe crossed the drogue plane by {-minimumAxialClearance:F2} m.");
        Assert.Contains(universe.DockingConnections, c => c.Id == Apollo11FlightProfile.DockingConnectionId);
        Assert.False(columbia.RcsPropellantDepleted);
        Assert.InRange(monoUsed, 1.0, 120.0);
        Assert.Equal(spsBefore, columbia.Parts.Parts.Sum(part => part.LiquidFuel + part.Oxidizer), 6);
        Assert.InRange(universe.CurrentTime - start, 120.0, 1_500.0);

        // Armstrong and Aldrin reach Eagle through the tunnel only while docked, and only
        // while Eagle has seats for them.
        columbia.Crew.AddRange(new[] { "neil-a-armstrong", "edwin-e-buzz-aldrin-jr", "michael-collins" }
            .Select(id => new CrewMember { Id = id }));
        Assert.False(universe.TransferCrew(columbia.Id, eagle.Id, columbia.Crew.Select(c => c.Id).ToList()));
        Assert.True(universe.TransferCrew(columbia.Id, eagle.Id, Apollo11FlightProfile.LunarModuleCrewIds.ToList()));
        Assert.Equal(["michael-collins"], columbia.Crew.Select(c => c.Id));
        Assert.Equal(2, eagle.Crew.Count);
        Assert.True(universe.Undock(Apollo11FlightProfile.DockingConnectionId, 0.3));
        Assert.False(universe.TransferCrew(eagle.Id, columbia.Id, ["neil-a-armstrong"]));
    }
}
