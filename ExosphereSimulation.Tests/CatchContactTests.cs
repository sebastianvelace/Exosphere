namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Xunit;

public sealed class CatchContactTests
{
    [Fact]
    public void QueryWithinCaptureRadiusLocksToCradleHeight()
    {
        var cradle = new Vector3d(100, 50, 0);
        var query = new Vector3d(102, 60, 1);   // 2.24 m horizontal offset, well inside 5 m

        var sample = SurfaceSample.FromCatchCradle(
            cradle, Vector3d.Up, Vector3d.Zero, captureRadiusM: 5.0, queryWorld: query);

        Assert.Equal(50.0, sample.PointWorld.Y, 9);
        Assert.Equal(102.0, sample.PointWorld.X, 9);
        Assert.Equal(1.0, sample.PointWorld.Z, 9);
    }

    [Fact]
    public void QueryOutsideCaptureRadiusNeverProducesContactEvenAtCradleHeight()
    {
        var cradle = new Vector3d(0, 50, 0);
        // Same height as the cradle, but 40 m sideways — nowhere near the arms.
        var query = new Vector3d(40, 50, 0);

        var sample = SurfaceSample.FromCatchCradle(
            cradle, Vector3d.Up, Vector3d.Zero, captureRadiusM: 5.0, queryWorld: query);

        // Pushed far below: the query point sits nowhere near this "surface".
        Assert.True(sample.PointWorld.Y < query.Y - 1000.0);
    }

    [Fact]
    public void NominalTowerApproachSettlesAsCaughtNotAsSurfaceLanded()
    {
        var (universe, vessel) = CreateCatchCase(verticalSpeed: -1.0, lateralOffset: 0.0);

        for (int i = 0; i < 3_000 && !vessel.IsCaught && !vessel.IsDestroyed; i++)
            universe.Tick(0.005);

        Assert.False(vessel.IsDestroyed);
        Assert.True(vessel.IsCaught, "two catch pins settling in the cradle should register as caught");
        Assert.False(vessel.IsSurfaceSettled, "a tower catch must not be reported through the leg-landing gate");
        Assert.NotNull(vessel.LastCatchContact);
        Assert.Equal(2, vessel.LastCatchContact!.ContactCount);
    }

    [Fact]
    public void ApproachFarOutsideCaptureRadiusNeverRegistersAsCaught()
    {
        // 50 m off to the side of the cradle — no chopstick arm reaches that far.
        var (universe, vessel) = CreateCatchCase(verticalSpeed: -1.0, lateralOffset: 50.0);

        for (int i = 0; i < 3_000 && !vessel.IsDestroyed; i++)
            universe.Tick(0.005);

        Assert.False(vessel.IsCaught);
    }

    [Fact]
    public void CatchPinDataIsScopedToTheV3ShipsCatchEquippedNose()
    {
        var v3 = PartDefinition.LoadFromJson(Path.Combine(
            FindRepoRoot().FullName, "data", "parts", "starship_v3_command_flight12.json"));
        var legacy = PartDefinition.LoadFromJson(Path.Combine(
            FindRepoRoot().FullName, "data", "parts", "starship_command.json"));

        Assert.True(v3.CatchPinLateralOffsetM > 0.0);
        Assert.True(v3.CatchPinRadiusM > 0.0);
        Assert.Equal(0.0, legacy.CatchPinLateralOffsetM);
    }

    private static (Universe universe, Vessel vessel) CreateCatchCase(
        double verticalSpeed, double lateralOffset)
    {
        // A synthetic, non-rotating, atmosphere-free body: this test's scope is the
        // contact/settle logic, not aerodynamics or thermal loads. Reusing real earth.json
        // (as the landing-gear tests do) would give the vessel ~408 m/s of surface-relative
        // airspeed the moment its own horizontal velocity is zeroed out for this test,
        // triggering unrelated thermal destruction before the catch has a chance to settle.
        var body = new CelestialBody
        {
            Id = "catch-test-body",
            Mass = 5.972e24,
            Radius = 6.371e6,
        };
        var vessel = new Vessel { ReferenceBodyId = body.Id, SASEnabled = false };
        var nose = new Part(new PartDefinition
        {
            Id = "catch-test-nose",
            CategoryStr = "command",
            MassDry = 38_000.0,
            LengthM = 19.0,
            DiameterM = 9.0,
            CatchPinLateralOffsetM = 4.4,
            CatchPinOffsetYM = 0.0,
            CatchPinRadiusM = 0.4,
        });
        vessel.Parts.SetRoot(nose);
        vessel.Parts.AddPart(nose);
        vessel.ConfigureCatchContactsFromParts();
        Assert.True(vessel.HasCatchPins);

        // High above the ground (out of leg-contact range) so only the tower-catch path
        // is exercised; the vessel carries no landing gear at all in this test.
        //
        // Deliberately zero horizontal/rotational velocity here: a real tower target tracks
        // the body's rotation every frame (task E's job), but this test's scope is the
        // contact/settle logic in isolation, so the cradle is a point genuinely fixed in
        // this inertial frame rather than one that silently drifts away from an
        // un-refreshed target position over the run.
        var up = Vector3d.Up;
        var cradle = body.Position + up * (body.Radius + 500.0);
        // Start just above the cradle rather than exactly at it: beginning already at the
        // resting penetration depth would apply the full contact-radius force spike from
        // the very first tick, an unrealistic step input rather than an actual approach.
        vessel.Position = cradle + up * 3.0;
        vessel.Velocity = up * verticalSpeed;
        vessel.Orientation = Quaterniond.Identity;

        vessel.IsAttemptingTowerCatch = true;
        vessel.CatchTargetPositionWorld = cradle + Vector3d.Right * lateralOffset;
        vessel.CatchTargetUpWorld = up;
        vessel.CatchTargetVelocityWorld = Vector3d.Zero;

        var universe = new Universe { TimeScale = 1.0, ActiveVessel = vessel };
        universe.AddBody(body);
        universe.AddVessel(vessel);
        return (universe, vessel);
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
