namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Xunit;

public sealed class LandingContactIntegrationTests
{
    [Fact]
    public void SixLegStarshipDropSettlesOnSpringsWithoutGroundHoldSnap()
    {
        var (universe, body, vessel) = CreateLandingCase(verticalSpeed: -1.5, lateralSpeed: 0.8);

        for (int i = 0; i < 2_000 && !vessel.IsSurfaceSettled && !vessel.IsDestroyed; i++)
            universe.Tick(0.005);

        Assert.False(vessel.IsDestroyed);
        Assert.True(vessel.IsSurfaceSettled, "six-foot gear should reach the persistent settled gate");
        Assert.False(vessel.IsGroundHeld, "landing contact must not reuse the launch hold clamp");
        Assert.NotNull(vessel.LastSurfaceContact);
        Assert.True(vessel.LastSurfaceContact!.ContactCount >= 3);
        Assert.InRange(body.GetAltitude(vessel.Position), 6.5, 8.5);
        Assert.InRange(vessel.GetSurfaceVelocity(body).Magnitude, 0.0, 0.55);
        Assert.InRange(vessel.GetProperAcceleration(body).Magnitude / 9.80665, 0.85, 1.15);

        double sleepingAltitude = body.GetAltitude(vessel.Position);
        for (int i = 0; i < 500; i++) universe.Tick(0.005);
        Assert.True(vessel.IsSurfaceSettled);
        Assert.False(vessel.IsGroundHeld);
        Assert.InRange(System.Math.Abs(body.GetAltitude(vessel.Position) - sleepingAltitude), 0.0, 0.01);
    }

    [Fact]
    public void SevereImpactExceedsUltimateLoadInsteadOfClampingTheVehicle()
    {
        var (universe, _, vessel) = CreateLandingCase(verticalSpeed: -7.0, lateralSpeed: 0.0);

        for (int i = 0; i < 1_000 && !vessel.IsDestroyed; i++)
            universe.Tick(0.005);

        Assert.True(vessel.IsDestroyed);
        Assert.Equal(VesselDestructionCause.GroundImpact, vessel.DestructionCause);
        Assert.False(vessel.IsGroundHeld);
        Assert.True(vessel.LastSurfaceContact?.HasOverload);
    }

    [Fact]
    public void HighSpeedContactCannotBecomeLandingSuccessByAltitudeAlone()
    {
        // Start just above the foot datum with a deliberately unsafe approach speed. The
        // first contact is a real spring event, not a touchdown: the mission must not be
        // considered landed merely because the vessel is already at pad altitude.
        var (universe, body, vessel) = CreateLandingCase(
            verticalSpeed: -10.0, lateralSpeed: 0.0);

        bool observedFastContact = false;
        for (int i = 0; i < 200 && !vessel.IsSurfaceSettled && !vessel.IsDestroyed; i++)
        {
            universe.Tick(0.005);
            if (vessel.LastSurfaceContact?.ContactCount >= 3)
            {
                observedFastContact = true;
                double contactSpeed = vessel.GetSurfaceVelocity(body).Magnitude;
                if (contactSpeed > AscentStagingPolicy.SoftLandingSpeedMps)
                {
                    Assert.False(vessel.IsSurfaceSettled,
                        "an unsafe first contact must not be accepted by altitude alone");
                    break;
                }
            }
        }

        Assert.True(observedFastContact,
            "the scenario must exercise the multi-foot spring contact path");
    }

    [Fact]
    public void EdlFinalApproachKeepsTwoEngineFloorUntilSafeMultiLegContact()
    {
        // EDLController is a Godot scene script and is intentionally not referenced by this
        // pure-simulation test project. Keep a narrow source-level contract beside the physical
        // contact regression: the production controller must not reintroduce a 2→1 engine step
        // before the same three-leg/low-speed gate used to commit engine cutoff.
        string source = File.ReadAllText(Path.Combine(
            FindRepoRoot().FullName, "scripts", "EDLController.cs"));

        Assert.Contains("MinimumFinalApproachEngines = 2", source);
        Assert.Contains("if (!safeContact)", source);
        Assert.Contains("selected = System.Math.Min(selected, requested)", source);
        Assert.Contains("selected = System.Math.Max(MinimumFinalApproachEngines, selected)", source);
        Assert.Contains("IsSafeMultiLegContact(vessel, body)", source);
    }

    [Fact]
    public void EdlFinalApproachHasGatedSingleEngineLowEnergyState()
    {
        // A Raptor's documented deep-throttle floor makes two engines too much thrust for
        // the final metres at this vehicle mass. The production controller may therefore use
        // the centre sea-level engine, but only after the discrete demand, altitude and
        // lateral-speed gates agree. This source contract protects that physical envelope
        // from being replaced with an unconditional one-engine landing shortcut.
        string source = File.ReadAllText(Path.Combine(
            FindRepoRoot().FullName, "scripts", "EDLController.cs"));

        Assert.Contains("FinalSingleEngineAltitudeM = 160.0", source);
        Assert.Contains("FinalSingleEngineLateralSpeedMps = 18.0", source);
        Assert.Contains("FinalSingleEngineReacquireLateralSpeedMps = 10.0", source);
        Assert.Contains("FinalSingleEngineContactGuardAltitudeM = 350.0", source);
        Assert.Contains("&& _alt <= FinalSingleEngineContactGuardAltitudeM", source);
        Assert.Contains("requested <= 1", source);
        Assert.Contains("FinalSingleEngineMinVerticalSpeedMps = -8.0", source);
        Assert.Contains("FinalSingleEngineMaxVerticalSpeedMps = 4.0", source);
        Assert.Contains("FinalSingleEngineDescentBiasMps2 = 6.0", source);
        Assert.Contains("FinalSingleEngineDescentBiasGain = 3.0", source);
        Assert.Contains("FinalSingleEngineDescentBiasLimitMps2 = 9.0", source);
        Assert.Contains("? 0.010", source);
        Assert.Contains("FinalSingleEngineHorizontalBrakeErrorMps = 1.0", source);
        Assert.Contains("FinalSingleEngineAcquireDescentSpeedMps = -1.5", source);
        Assert.Contains("FinalSingleEngineRetainDescentSpeedMps = -0.5", source);
        Assert.Contains("FinalSingleEngineLowLateralSpeedMps = 3.0", source);
        Assert.Contains("FinalTwoEngineReboundAltitudeM = 240.0", source);
        Assert.Contains("FinalTwoEngineReboundVerticalSpeedMps = 0.5", source);
        Assert.Contains("FinalTwoEngineReboundLateralSpeedMps = 20.0", source);
        Assert.Contains("FinalSingleEngineReboundMinVerticalSpeedMps = -25.0", source);
        Assert.Contains("_singleEngineReboundMode", source);
        Assert.Contains("_phase == Edl.Final && _landingEngineCount == 1", source);
        Assert.Contains("selected = System.Math.Min(selected, 1)", source);
        Assert.Contains("lateralVelocity.Magnitude * 0.08", source);
    }

    [Fact]
    public void EdlFinalApproachKeepsAProgradeLandingAxisDuringLateralRecovery()
    {
        // At v6 the vehicle crossed the old 12 m/s lateral threshold while already
        // climbing. Falling through to -velDir inverted the landing thrust component and
        // saturated both engines, producing a 263 m rebound and a 106 m/s impact. Final
        // guidance must therefore retain the bounded upright/canted axis for the complete
        // final phase, and must include the horizontal error in its bounded burn demand.
        string source = File.ReadAllText(Path.Combine(
            FindRepoRoot().FullName, "scripts", "EDLController.cs"));

        Assert.Contains("else if (_phase == Edl.Catch || _phase == Edl.Final)", source);
        Assert.Contains("Edl.Retro or Edl.Catch or Edl.Final", source);
        Assert.Contains("FinalHorizontalBrakeErrorMps = 4.0", source);
        Assert.Contains("coupledHorizontalError, 0.0, FinalHorizontalBrakeErrorMps", source);
        Assert.Contains("Math.Tan(20.0 * MathUtils.DEG_TO_RAD)", source);
        Assert.DoesNotContain("(_phase == Edl.Final && _horiz < 12.0)", source);
    }

    [Fact]
    public void DeterministicEdlContactStateRemainsInsideStructuralEnvelope()
    {
        // Regression for the full EDL playtest's measured pre-contact state. This is deliberately
        // less tidy than the nominal drop: the controller arrives with lateral velocity and the
        // first compression briefly loads each foot above its purely static share of weight.
        var (universe, _, vessel) = CreateLandingCase(verticalSpeed: -2.2, lateralSpeed: 1.3);
        vessel.Orientation = Quaterniond.FromAxisAngle(
            Vector3d.Forward, 2.5 * MathUtils.DEG_TO_RAD);

        for (int i = 0; i < 2_000 && !vessel.IsSurfaceSettled && !vessel.IsDestroyed; i++)
            universe.Tick(0.005);

        Assert.False(vessel.IsDestroyed);
        Assert.True(vessel.IsSurfaceSettled);
        Assert.NotNull(vessel.LastSurfaceContact);
        Assert.True(vessel.LastSurfaceContact!.Points.Max(p => p.NormalLoadN) < 2_500_000.0);
    }

    [Fact]
    public void LandingGearDataUsesExplicitSiContactParameters()
    {
        var path = Path.Combine(FindRepoRoot().FullName, "data", "parts", "starship_landing_gear.json");
        var definition = PartDefinition.LoadFromJson(path);

        Assert.Equal(6, definition.ContactPointCount);
        Assert.Equal(2_350_000.0, definition.SpringStrength);
        Assert.Equal(550_000.0, definition.DamperStrength);
        Assert.Equal(0.60, definition.SuspensionTravelM);
        Assert.Equal(2_500_000.0, definition.MaxLoad);
        Assert.Equal(4.20, definition.ContactRingRadiusM);
        Assert.True(definition.ContactComOffsetYM < definition.ContactOffsetYM);
    }

    private static (Universe universe, CelestialBody body, Vessel vessel) CreateLandingCase(
        double verticalSpeed,
        double lateralSpeed)
    {
        var body = CelestialBody.LoadFromJson(Path.Combine(
            FindRepoRoot().FullName, "data", "bodies", "earth.json"));
        var vessel = new Vessel { ReferenceBodyId = body.Id, SASEnabled = false };
        var command = new Part(new PartDefinition
        {
            Id = "landing-test-mass",
            CategoryStr = "command",
            MassDry = 241_000.0,
            LengthM = 50.0,
            DiameterM = 9.0,
        });
        var gearDefinition = PartDefinition.LoadFromJson(Path.Combine(
            FindRepoRoot().FullName, "data", "parts", "starship_landing_gear.json"));
        var gear = new Part(gearDefinition) { IsDeployed = true };
        vessel.Parts.SetRoot(command);
        vessel.Parts.AddPart(command);
        vessel.Parts.AddPart(gear);
        vessel.ConfigureLandingContactsFromParts();

        var up = Vector3d.Up;
        vessel.Position = body.Position + up * (body.Radius + 8.1);
        var surfaceVelocity = body.Velocity + body.GetSurfaceVelocity(vessel.Position);
        vessel.Velocity = surfaceVelocity
            + up * verticalSpeed
            + Vector3d.Right * lateralSpeed;
        vessel.Orientation = Quaterniond.Identity;

        var universe = new Universe { TimeScale = 1.0, ActiveVessel = vessel };
        universe.AddBody(body);
        universe.AddVessel(vessel);
        return (universe, body, vessel);
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data"))
                && File.Exists(Path.Combine(dir.FullName, "ExosphereSimulation.sln")))
                return dir;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }
}
