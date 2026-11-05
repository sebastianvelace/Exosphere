namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;
using Exosphere.Simulation.Persistence;
using Xunit;

public sealed class AttitudeActuatorBlendingTests
{
    [Fact]
    public void ShutdownEnginesDoNotMakeJetsDuplicateTheFlapsAerodynamicTrim()
    {
        var (universe, ship, earth) = CreateWindTunnel();
        ship.PitchYawRoll = new Vector3d(0.05, 0, 0);
        double gas = ship.Parts.TotalMonopropellant;

        for (int i = 0; i < 20; i++)
        {
            universe.Tick(0.02);
            Assert.NotEmpty(ship.Parts.ActiveEngines);
            Assert.Equal(0.0, ship.Parts.GetCurrentThrust(earth.Atmosphere!.GetPressure(ship.GetAltitude(earth))));
            Assert.Equal(Vector3d.Zero, ship.RcsThrusterTorqueBody);
            Assert.Equal(0.0, ship.RcsThrusterMassFlowKgPerS);
        }
        Assert.Equal(gas, ship.Parts.TotalMonopropellant);
        Assert.True(ship.PhysicalBodyFlapsActive);
        Assert.Contains(ship.BodyFlapDeflectionsRad, a => System.Math.Abs(a - System.Math.PI / 4) > 0.01);
    }

    [Fact]
    public void CoupledBankReversalKeepsTheTrimLimitedTransverseLoopStable()
    {
        var (universe, ship, earth) = CreateWindTunnel();
        var position = ship.Position;
        var velocity = ship.Velocity;
        var initial = ship.Orientation;
        var flow = ship.GetSurfaceVelocity(earth).Normalized;
        double peak = 0;
        Quaterniond reference = initial;
        for (int i = 0; i < 10_000; i++)
        {
            var target = Quaterniond.FromAxisAngle(flow, i < 5_000 ? 0 : 0.5) * initial;
            double lead = AttitudeGuidance.ErrorAngleRadians(ship.Orientation, target);
            const double leadLimit = 6 * MathUtils.DEG_TO_RAD;
            reference = lead > leadLimit ? ship.Orientation.Slerp(target, leadLimit / lead).Normalize() : target;
            ship.PitchYawRoll = Flight14AerodynamicAttitude.ComputeCommand(ship, earth, reference, 250, 1_000);
            universe.Tick(0.02);
            peak = System.Math.Max(peak, ship.AngularVelocity.Magnitude);
            // Constant-wind actuator bench: retain coupled attitude dynamics and finite
            // servos, holding only the wind/altitude fixed between steps.
            ship.Position = position;
            ship.Velocity = velocity;
        }
        Assert.InRange(peak, 0, 0.15);
        Assert.InRange(AttitudeGuidance.ErrorAngleRadians(ship.Orientation, reference), 0, MathUtils.DEG_TO_RAD);
    }

    [Fact]
    public void RestoredAuthorityMatchesAWarmCacheWhileTheGasLoadChanges()
    {
        var (universe, ship, earth) = CreateWindTunnel();
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(Flight14EntryCheckpointFixture.DataDirectory(), "parts"));
        ship.PitchYawRoll = new Vector3d(0.02, -0.01, 0.03);
        ship.FlapCommandOverride = Vector3d.Zero;
        double gas = ship.Parts.TotalMonopropellant;
        for (int i = 0; i < 100; i++)
        {
            universe.Tick(0.02);
            var snapshot = SaveGameV2Json.DeserializeOrMigrate(
                SaveGameV2Json.Serialize(SaveGameV2Codec.Capture(universe)));
            var restored = new Universe();
            restored.AddBody(earth);
            SaveGameV2Codec.Restore(restored, snapshot, catalog);
            var copy = restored.ActiveVessel!;
            Assert.Equal(ship.Orientation, copy.Orientation);
            Assert.Equal(ship.ComputeBodyFlapAngularAuthority(earth), copy.ComputeBodyFlapAngularAuthority(earth));
            Assert.Equal(ship.GetAttitudeAuthorityBody(earth), copy.GetAttitudeAuthorityBody(earth));
        }
        Assert.True(ship.Parts.TotalMonopropellant < gas);
    }

    [Fact]
    public void LowPressureDescentHandoffRetainsTheBankAlreadyFlown()
    {
        var (universe, ship, earth) = CreateWindTunnel();
        universe.Tick(0.02); // Activate the production physical-flap path before seeding the bench.
        ship.Position = earth.GetSurfacePositionAtTime(23, -178, 0, 110_000);
        var up = earth.GetGeodeticUp(ship.Position);
        var flow = up.Cross(Vector3d.Up).Normalized;
        var side = flow.Cross(up).Normalized;
        ship.Velocity = earth.GetSurfaceVelocity(ship.Position) + flow * 7_400;
        ship.Orientation = AerodynamicsModel.ComputeBellyFirstOrientation(
            AerodynamicsModel.ComputeEntryAxisForLift(flow, side, 59.5), flow);
        ship.AngularVelocity = Vector3d.Zero;
        var definition = Flight14DescentDefinition.LoadFromJson(Path.Combine(
            Flight14EntryCheckpointFixture.DataDirectory(), "flight_profiles/starship_flight14_descent_estimate.json"));
        var reference = ship.Orientation;
        var priorLift = side;
        var state = EntryFlightDiagnostics.Evaluate(ship, earth);
        Assert.InRange(state.DynamicPressurePa, 0, definition.BankOnsetDynamicPressurePa);
        Flight14DescentController.ComputeGuidanceCommand(ship, earth, definition, state, 0.02,
            ref reference, ref priorLift);
        Assert.InRange(AttitudeGuidance.ErrorAngleRadians(ship.Orientation, reference), 0, 1e-6);
    }

    private static (Universe Universe, Vessel Ship, CelestialBody Earth) CreateWindTunnel()
    {
        string data = Flight14EntryCheckpointFixture.DataDirectory();
        var earth = CelestialBody.LoadFromJson(Path.Combine(data, "bodies/earth.json"));
        earth.Position = Vector3d.Zero;
        earth.Velocity = Vector3d.Zero;
        earth.OrbitalElements = null;
        var ship = VehicleVariantDefinition.LoadFromJson(Path.Combine(data,
            "vehicles/starship_flight12_v3_2026.json"))
            .Build(PartCatalog.LoadFromDirectory(Path.Combine(data, "parts"))).ToVessel("Unpowered trim bench");
        Assert.NotNull(ship.Stage());
        foreach (var part in ship.Parts.Parts) { part.LiquidFuel = 0; part.Oxidizer = 0; }
        ship.Position = earth.GetSurfacePositionAtTime(23, -178, 0, 74_000);
        var up = earth.GetGeodeticUp(ship.Position);
        var flow = up.Cross(Vector3d.Up).Normalized;
        ship.Velocity = earth.GetSurfaceVelocity(ship.Position) + flow * 6_000;
        ship.Orientation = AerodynamicsModel.ComputeBellyFirstOrientation(
            AerodynamicsModel.ComputeEntryAxisForLift(flow, up, 59.5), flow);
        ship.Throttle = 0;
        ship.SASEnabled = false;
        ship.PilotRateEnvelopeEnabled = false;
        var universe = new Universe();
        universe.AddBody(earth);
        universe.AddVessel(ship);
        universe.ActiveVessel = ship;
        return (universe, ship, earth);
    }
}
