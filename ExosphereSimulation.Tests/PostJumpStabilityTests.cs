namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Systems;
using Xunit;

/// <summary>
/// CPU regression coverage for the state boundary used by
/// <c>SimulationBridge.JumpToBody</c>.  The Godot project is intentionally not a
/// dependency of this test assembly, so the setup below uses only the existing
/// public simulation APIs and mirrors the bridge's current jump sequence.
/// </summary>
public sealed class PostJumpStabilityTests
{
    private const double JumpAltitudeM = 300_000.0;
    private const double TickSeconds = 0.02;

    [Theory]
    [InlineData("earth")]
    [InlineData("mars")]
    [InlineData("venus")]
    public void JumpToBodyStateStaysFiniteAndBodyReferencedAfterTeleport(string bodyId)
    {
        var universe = Universe.LoadFromDataDirectory(DataDirectory());
        var body = universe.GetBody(bodyId);
        Assert.NotNull(body);

        var vessel = new Vessel($"post-jump-{bodyId}")
        {
            // Seed stale state so this test proves that the teleport boundary, rather
            // than the initial values, determines the first destination frame.
            ReferenceBodyId = "earth",
            IsOnRails = true,
            OrbitalState = new OrbitalElements
            {
                ReferenceBodyId = "earth",
                SemiMajorAxis = 7_000_000.0,
                Eccentricity = 0.02,
            },
            AngularVelocity = new Vector3d(0.25, -0.15, 0.10),
            PitchYawRoll = new Vector3d(0.8, -0.4, 0.2),
            Throttle = 1.0,
            SASEnabled = false,
        };

        // This is the same circular-orbit placement used by the existing bridge
        // JumpToBody implementation for non-Saturn bodies.
        Vector3d up = Vector3d.Right;
        double radius = body!.Radius + System.Math.Max(
            JumpAltitudeM,
            body.Radius * 0.6);
        Vector3d tangent = new Vector3d(0.0, 1.0, 0.0)
            .Cross(up)
            .Normalized;
        double circularSpeed = System.Math.Sqrt(body.GM / radius);

        vessel.Position = body.Position + up * radius;
        vessel.Velocity = body.Velocity + tangent * circularSpeed;
        vessel.PrepareForTeleport();
        vessel.ReferenceBodyId = body.Id;
        vessel.Orientation = Quaterniond.FromTo(Vector3d.Up, tangent);
        vessel.SASEnabled = true;
        vessel.Throttle = 0.0;
        universe.AddVessel(vessel);

        Assert.Equal(body.Id, vessel.ReferenceBodyId);
        Assert.Null(vessel.OrbitalState);
        Assert.Equal(0.0, vessel.Throttle);
        Assert.Equal(Vector3d.Zero, vessel.PitchYawRoll);
        Assert.Equal(Vector3d.Zero, vessel.AngularVelocity);
        Assert.True(vessel.SASEnabled);
        AssertFinite(vessel, body);

        // A short real-time window catches stale conics, bad body frames, NaN geometry,
        // and residual control state without making this a long-running orbital test.
        for (int i = 0; i < 150; i++)
        {
            universe.Tick(TickSeconds);

            Assert.False(vessel.IsDestroyed,
                $"{bodyId} jump became destroyed at frame {i}.");
            Assert.Equal(body.Id, vessel.ReferenceBodyId);
            Assert.Equal(0.0, vessel.Throttle);
            Assert.Equal(Vector3d.Zero, vessel.PitchYawRoll);
            Assert.Equal(Vector3d.Zero, vessel.AngularVelocity);
            AssertFinite(vessel, body);
        }
    }

    [Fact]
    public void GroundCommandRelayClearPreventsPreJumpCommandsFromReapplying()
    {
        var relay = new GroundCommandRelay();
        var appliedAttitude = Vector3d.Zero;
        double appliedThrottle = 0.0;

        relay.SubmitAttitude(
            now: 10.0,
            delaySeconds: 30.0,
            pyr: new Vector3d(1.0, -1.0, 0.5),
            linkUp: true);
        relay.SubmitThrottleDelta(
            now: 10.0,
            delaySeconds: 30.0,
            delta: 0.75,
            linkUp: true);

        Assert.True(relay.HasPending);
        relay.Clear();

        relay.Tick(
            now: 10.0 + 300.0,
            applyAttitude: value => appliedAttitude = value,
            applyThrottleDelta: delta => appliedThrottle += delta);

        Assert.False(relay.HasPending);
        Assert.Equal(Vector3d.Zero, appliedAttitude);
        Assert.Equal(0.0, appliedThrottle);
    }

    private static void AssertFinite(Vessel vessel, CelestialBody body)
    {
        AssertFinite(vessel.Position);
        AssertFinite(vessel.Velocity);
        Assert.True(double.IsFinite(vessel.Orientation.W));
        Assert.True(double.IsFinite(vessel.Orientation.X));
        Assert.True(double.IsFinite(vessel.Orientation.Y));
        Assert.True(double.IsFinite(vessel.Orientation.Z));
        Assert.True(double.IsFinite(vessel.GetAltitude(body)));
        Assert.True(vessel.GetAltitude(body) > 0.0);
    }

    private static void AssertFinite(Vector3d value)
    {
        Assert.True(double.IsFinite(value.X));
        Assert.True(double.IsFinite(value.Y));
        Assert.True(double.IsFinite(value.Z));
    }

    private static string DataDirectory() =>
        Path.Combine(FindRepoRoot().FullName, "data");

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "data"))
                && File.Exists(Path.Combine(
                    directory.FullName,
                    "ExosphereSimulation.sln")))
                return directory;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
