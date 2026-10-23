namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Training;
using Xunit;

/// <summary>Training drills: data, seeded situations and goals.</summary>
public sealed class TrainingTests
{
    private static string Root => SaturnVAscentFlight.DataDirectory();

    private static Vessel Build(PartCatalog catalog, TrainingDefinition training)
    {
        if (!string.IsNullOrWhiteSpace(training.VehicleVariantFile))
            return VehicleVariantDefinition.LoadFromJson(Path.Combine(Root, "vehicles", training.VehicleVariantFile))
                .Build(catalog).ToVessel(training.Id, "player");
        var assembly = new VesselAssembly(catalog);
        var current = assembly.AddRoot(training.VehicleParts[0]);
        foreach (var part in training.VehicleParts.Skip(1))
            current = assembly.AttachPartAutomatically(current.InstanceId, part);
        return assembly.ToVessel(training.Id, "player");
    }

    private static (Universe universe, Vessel player, Vessel? target, TrainingDefinition training) Seed(string id)
    {
        var catalog = PartCatalog.LoadFromDirectory(Path.Combine(Root, "parts"));
        var training = TrainingDefinition.LoadAll(Path.Combine(Root, "training")).Single(t => t.Id == id);
        var universe = Universe.LoadFromDataDirectory(Root);
        var player = Build(catalog, training);
        Vessel? target = string.IsNullOrWhiteSpace(training.TargetVariantFile) ? null
            : VehicleVariantDefinition.LoadFromJson(Path.Combine(Root, "vehicles", training.TargetVariantFile))
                .Build(catalog).ToVessel("Agena", "target");
        universe.AddVessel(player);
        if (target != null) universe.AddVessel(target);
        TrainingSeeder.Seed(training, universe, player, target);
        return (universe, player, target, training);
    }

    [Fact]
    public void FourDrillsLoadInOrderWithBilingualInstructions()
    {
        var all = TrainingDefinition.LoadAll(Path.Combine(Root, "training"));
        Assert.Equal(["training-circularize", "training-docking", "training-lunar-orbit", "training-lunar-landing"],
            all.Select(t => t.Id));
    }

    [Fact]
    public void CircularizeStartsAtApogeeAndIsMetOnlyOnceTheOrbitIsCircular()
    {
        var (universe, player, _, training) = Seed("training-circularize");
        var earth = universe.GetBody("earth")!;
        Assert.InRange(player.GetAltitude(earth), 990_000.0, 1_010_000.0);
        Assert.False(TrainingGoals.IsMet(training, universe, player));
        double radius = (player.Position - earth.Position).Magnitude;
        player.Velocity = earth.Velocity + (player.Velocity - earth.Velocity).Normalized * System.Math.Sqrt(earth.GM / radius);
        Assert.True(TrainingGoals.IsMet(training, universe, player));
    }

    [Fact]
    public void DockingStartsTwoHundredMetresBehindTheTargetFacingIt()
    {
        var (_, player, target, _) = Seed("training-docking");
        var offset = target!.Position - player.Position;
        Assert.InRange(offset.Magnitude, 195.0, 205.0);
        Assert.True(player.Orientation.Rotate(Vector3d.Up).Dot(offset.Normalized) > 0.999);
        Assert.True((player.Velocity - target.Velocity).Magnitude < 0.5);
    }

    [Fact]
    public void LunarArrivalIsInsideTheSphereAndBecomesAnOrbitAfterCapture()
    {
        var (universe, player, _, training) = Seed("training-lunar-orbit");
        var moon = universe.GetBody("moon")!;
        Assert.Equal("moon", universe.GetDominantBody(player.Position).Id);
        Assert.False(TrainingGoals.IsMet(training, universe, player));
        double radius = (player.Position - moon.Position).Magnitude;
        var radial = (player.Position - moon.Position).Normalized;
        var along = (player.Velocity - moon.Velocity);
        var horizontal = (along - radial * along.Dot(radial)).Normalized;
        player.Velocity = moon.Velocity + horizontal * System.Math.Sqrt(moon.GM / radius);
        Assert.False(TrainingGoals.IsMet(training, universe, player)); // circular, but far above 500 km
        player.Position = moon.Position + radial * (moon.Radius + 111_000.0);
        player.Velocity = moon.Velocity + horizontal * System.Math.Sqrt(moon.GM / (moon.Radius + 111_000.0));
        Assert.True(TrainingGoals.IsMet(training, universe, player));
    }

    [Fact]
    public void LandingDrillStartsInTheDescentOrbit()
    {
        var (universe, player, _, training) = Seed("training-lunar-landing");
        var moon = universe.GetBody("moon")!;
        var orbit = OrbitalElements.FromStateVector(player.Position - moon.Position, player.Velocity - moon.Velocity,
            moon.GM, moon.Id, universe.CurrentTime);
        Assert.InRange(orbit.Periapsis - moon.Radius, 14_000.0, 16_000.0);
        Assert.InRange(orbit.Apoapsis - moon.Radius, 105_000.0, 115_000.0);
        Assert.False(TrainingGoals.IsMet(training, universe, player));
    }
}
