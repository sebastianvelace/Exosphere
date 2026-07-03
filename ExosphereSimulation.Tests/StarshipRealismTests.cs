namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Xunit;

public sealed class StarshipRealismTests
{
    private const double G0 = 9.80665;
    private const double SeaLevelPressure = 101_325.0;

    [Fact]
    public void Flight7StackMatchesDeclaredMassAndGeometryBaseline()
    {
        var (vessel, _, _, _, _) = BuildFlight7Stack();

        AssertClose(4_800_000.0, vessel.TotalMass, 1e-12);
        AssertClose(300_000.0, vessel.Parts.DryMass, 1e-12);
        AssertClose(4_500_000.0,
            vessel.Parts.TotalLiquidFuel + vessel.Parts.TotalOxidizer, 1e-12);
        AssertClose(123.1, vessel.VehicleLength, 1e-12);
        AssertClose(9.0, vessel.MaximumDiameter, 1e-12);

        Assert.Equal(33, vessel.Parts.ActiveEngines.Sum(
            p => System.Math.Max(1, p.Definition.EngineCount)));
    }

    [Fact]
    public void BoosterDeltaVIncludesTheUpperStageAsCarriedMass()
    {
        var (vessel, booster, _, _, _) = BuildFlight7Stack();
        booster.ThrottleLevel = 1.0;

        double isp = vessel.GetCurrentIsp(null);
        double expected = isp * G0 * System.Math.Log(4_800_000.0 / 1_500_000.0);

        AssertClose(expected, vessel.GetCurrentStageDeltaV(null), 1e-12);
        Assert.InRange(expected, 3_900.0, 4_200.0);
    }

    [Fact]
    public void DeclaredRaptorMixtureRatioControlsPropellantDrain()
    {
        var (vessel, booster, _, _, _) = BuildFlight7Stack();
        booster.ThrottleLevel = 1.0;
        double fuelBefore = booster.LiquidFuel;
        double oxidizerBefore = booster.Oxidizer;

        vessel.Parts.ConsumePropellant(1.0, SeaLevelPressure);

        double fuelUsed = fuelBefore - booster.LiquidFuel;
        double oxidizerUsed = oxidizerBefore - booster.Oxidizer;
        Assert.True(fuelUsed > 0.0);
        AssertClose(3.55, oxidizerUsed / fuelUsed, 1e-12);
    }

    [Fact]
    public void StagingSwitchesFromThirtyThreeBoosterEnginesToSixShipEngines()
    {
        var (vessel, booster, _, shipEngines, _) = BuildFlight7Stack();
        booster.ThrottleLevel = 1.0;
        Assert.Equal(33, vessel.ActiveEngineCount);

        var detachedBooster = vessel.Stage();
        Assert.NotNull(detachedBooster);
        shipEngines.ThrottleLevel = 1.0;

        Assert.Equal(6, vessel.ActiveEngineCount);
        Assert.Contains(detachedBooster!.Parts.Parts,
            p => p.Definition.Id == "super_heavy_booster");
    }

    [Theory]
    [InlineData("earth", 1.50, 1.70)]
    [InlineData("moon", 9.5, 11.5)]
    [InlineData("mercury", 4.5, 4.9)]
    [InlineData("mars", 4.3, 4.9)]
    [InlineData("jupiter", 0.60, 0.66)]
    [InlineData("saturn", 1.43, 1.55)]
    public void SameStackGetsBodySpecificWeightAndTwr(
        string bodyId, double minimumTwr, double maximumTwr)
    {
        var body = LoadBody(bodyId);
        var (vessel, booster, _, _, _) = BuildFlight7Stack();
        vessel.Position = body.Position + new Vector3d(body.Radius + 12.0, 0.0, 0.0);
        booster.ThrottleLevel = 1.0;

        double localGravity = vessel.GetLocalGravity(body);
        double expectedGravity = body.GM /
            ((body.Radius + 12.0) * (body.Radius + 12.0));
        AssertClose(expectedGravity, localGravity, 1e-12);
        AssertClose(vessel.TotalMass * localGravity, vessel.GetWeightNewtons(body), 1e-12);
        Assert.InRange(vessel.GetThrustToWeightRatio(body), minimumTwr, maximumTwr);
    }

    [Fact]
    public void VenusBackPressurePreventsRaptorFromProducingNetSurfaceThrust()
    {
        var venus = LoadBody("venus");
        var (vessel, booster, _, _, _) = BuildFlight7Stack();
        vessel.Position = venus.Position + new Vector3d(venus.Radius, 0.0, 0.0);
        booster.ThrottleLevel = 1.0;

        Assert.True(vessel.GetAmbientPressure(venus) > 9_000_000.0);
        Assert.Equal(0.0, vessel.GetCurrentThrust(venus));
        Assert.Equal(0.0, vessel.GetThrustToWeightRatio(venus));
    }

    [Fact]
    public void GravityAccelerationIsMassIndependentButWeightIsNot()
    {
        var mars = LoadBody("mars");
        var position = mars.Position + new Vector3d(mars.Radius + 1_000.0, 0.0, 0.0);
        var light = VesselWithPointMass(1_000.0, position);
        var heavy = VesselWithPointMass(10_000.0, position);

        AssertClose(
            light.ComputeGravity(new[] { mars }).Magnitude,
            heavy.ComputeGravity(new[] { mars }).Magnitude,
            1e-12);
        AssertClose(10.0, heavy.GetWeightNewtons(mars) / light.GetWeightNewtons(mars), 1e-12);
    }

    [Fact]
    public void PropellantBurnMovesCenterOfMassTowardUpperStage()
    {
        var (vessel, booster, _, _, _) = BuildFlight7Stack();
        double fueledComY = vessel.Parts.CenterOfMass.Y;

        booster.LiquidFuel = 0.0;
        booster.Oxidizer = 0.0;
        double emptyBoosterComY = vessel.Parts.CenterOfMass.Y;

        Assert.True(emptyBoosterComY > fueledComY,
            $"Expected CoM to move toward Starship: fueled={fueledComY:F2}, empty={emptyBoosterComY:F2}");
    }

    [Fact]
    public void GimbalAuthorityRespondsToMassDistributionAndMomentOfInertia()
    {
        var (vessel, booster, _, shipEngines, _) = BuildFlight7Stack();
        booster.ThrottleLevel = 1.0;
        double fullStackInertia = vessel.Parts.TransverseMomentOfInertia;
        double fullStackAuthority =
            vessel.Parts.GetPitchYawAngularAcceleration(SeaLevelPressure);

        _ = vessel.Stage();
        shipEngines.ThrottleLevel = 1.0;
        double shipInertia = vessel.Parts.TransverseMomentOfInertia;
        double shipAuthority = vessel.Parts.GetPitchYawAngularAcceleration(0.0);

        Assert.True(fullStackInertia > shipInertia);
        Assert.InRange(fullStackAuthority, 0.04, 0.10);
        Assert.InRange(shipAuthority, 0.06, 0.12);
        Assert.True(shipAuthority > fullStackAuthority);
    }

    [Fact]
    public void AerodynamicEnvelopeUsesActual123MeterStack()
    {
        var (vessel, _, _, _, _) = BuildFlight7Stack();

        double axial = AerodynamicsModel.EffectiveArea(
            vessel.VehicleLength, vessel.MaximumDiameter, 1.0);
        double broadside = AerodynamicsModel.EffectiveArea(
            vessel.VehicleLength, vessel.MaximumDiameter, 0.0);

        AssertClose(System.Math.PI * 4.5 * 4.5, axial, 1e-12);
        AssertClose(123.1 * 9.0, broadside, 1e-12);
        Assert.True(broadside / axial > 17.0);
    }

    private static (Vessel vessel, Part booster, Part ring, Part shipEngines, Part shipTank)
        BuildFlight7Stack()
    {
        var defs = PartDefinition.LoadAllFromDirectory(
            Path.Combine(FindRepoRoot().FullName, "data", "parts"));

        var command = new Part(defs["starship_command"]);
        var tank = new Part(defs["starship_tank"]);
        var engines = new Part(defs["starship_engines"]);
        var ring = new Part(defs["decoupler_heavy"]);
        var booster = new Part(defs["super_heavy_booster"]);

        var vessel = new Vessel { Name = "Starship Flight 7" };
        vessel.Parts.SetRoot(command);
        vessel.Parts.AddJoint(new Joint(command, tank, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(tank, engines, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(engines, ring, "bottom", "top"));
        vessel.Parts.AddJoint(new Joint(ring, booster, "bottom", "top"));
        return (vessel, booster, ring, engines, tank);
    }

    private static Vessel VesselWithPointMass(double mass, Vector3d position)
    {
        var vessel = new Vessel { Position = position };
        vessel.Parts.SetRoot(new Part(new PartDefinition
        {
            Id = $"mass-{mass}",
            CategoryStr = "structure",
            MassDry = mass,
            LengthM = 1.0,
            DiameterM = 1.0,
        }));
        return vessel;
    }

    private static CelestialBody LoadBody(string id) =>
        CelestialBody.LoadFromJson(
            Path.Combine(FindRepoRoot().FullName, "data", "bodies", $"{id}.json"));

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

    private static void AssertClose(double expected, double actual, double relativeTolerance)
    {
        double scale = System.Math.Max(System.Math.Abs(expected), 1.0);
        Assert.True(System.Math.Abs(expected - actual) <= scale * relativeTolerance,
            $"Expected {expected:R}, got {actual:R}.");
    }
}
