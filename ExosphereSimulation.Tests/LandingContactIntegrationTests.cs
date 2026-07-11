namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
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
