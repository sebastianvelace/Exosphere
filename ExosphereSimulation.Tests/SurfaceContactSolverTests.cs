namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;
using Xunit;

public sealed class SurfaceContactSolverTests
{
    private static readonly SurfaceSample FlatGround = new(
        Vector3d.Zero, Vector3d.Up, Vector3d.Zero);

    [Fact]
    public void PointAboveSurfaceProducesNoContactForce()
    {
        var input = Input(datumY: 1.0, comY: 1.0);
        var result = Solve(input, Point(localY: 0.0, radius: 0.1));

        Assert.Equal(0, result.ContactCount);
        AssertVector(Vector3d.Zero, result.ForceWorld);
        AssertVector(Vector3d.Zero, result.TorqueWorld);
    }

    [Fact]
    public void PenaltyNormalForceMatchesSpringAndDamperExactly()
    {
        // Point centre at y=0.05 with a 0.10 m radius => 0.05 m penetration.
        // Descending at 2 m/s: Fn = 1000*0.05 - 10*(-2) = 70 N.
        var input = Input(datumY: 0.05, comY: 1.0, velocity: new Vector3d(0, -2, 0));
        var result = Solve(input, Point(localY: 0.0, radius: 0.1, k: 1_000, c: 10));
        var contact = Assert.Single(result.Points);

        Assert.Equal(0.05, contact.PenetrationM, 12);
        Assert.Equal(-2.0, contact.NormalVelocityMps, 12);
        Assert.Equal(70.0, contact.NormalLoadN, 10);
        AssertVector(new Vector3d(0, 70, 0), result.ForceWorld);
    }

    [Fact]
    public void SeparatingDamperCannotCreateAdhesion()
    {
        var input = Input(datumY: 0.05, comY: 1.0, velocity: new Vector3d(0, 20, 0));
        var result = Solve(input, Point(localY: 0.0, radius: 0.1, k: 1_000, c: 100));
        var contact = Assert.Single(result.Points);

        Assert.True(contact.IsGeometricallyContacting);
        Assert.Equal(0.0, contact.NormalLoadN);
        AssertVector(Vector3d.Zero, result.ForceWorld);
    }

    [Fact]
    public void ViscousFrictionIsLimitedByCoulombLoad()
    {
        // Fn=100 N. Raw viscous friction is 1000 N, but mu*Fn caps it at 60 N.
        var input = Input(datumY: 0.0, comY: 1.0, velocity: new Vector3d(10, 0, 0));
        var result = Solve(input, Point(
            localY: 0.0, radius: 0.1, k: 1_000, tangentialC: 100, mu: 0.6));
        var contact = Assert.Single(result.Points);

        Assert.Equal(100.0, contact.NormalLoadN, 10);
        Assert.Equal(60.0, contact.FrictionForceWorld.Magnitude, 10);
        Assert.True(contact.FrictionForceWorld.X < 0.0);
        Assert.True(contact.FrictionForceWorld.Magnitude
            <= 0.6 * contact.NormalLoadN + 1e-10);
    }

    [Fact]
    public void PointVelocityIncludesAngularVelocityCrossLeverArm()
    {
        // omega=(0,0,2), r=(1,-1,0) => omega x r=(2,2,0).
        // Normal velocity is +2 m/s, reducing Fn from 100 N to 80 N.
        var input = new RigidBodyContactInput(
            DatumPositionWorld: Vector3d.Zero,
            CenterOfMassPositionWorld: Vector3d.Up,
            CenterOfMassVelocityWorld: Vector3d.Zero,
            Orientation: Quaterniond.Identity,
            AngularVelocityWorld: new Vector3d(0, 0, 2));
        var result = Solve(input, Point(localX: 1.0, radius: 0.1, k: 1_000, c: 10));
        var contact = Assert.Single(result.Points);

        Assert.Equal(2.0, contact.NormalVelocityMps, 12);
        Assert.Equal(80.0, contact.NormalLoadN, 10);
    }

    [Fact]
    public void SixSymmetricLegsProduceZeroNetTorque()
    {
        var input = Input(datumY: 0.0, comY: 2.0);
        var legs = Enumerable.Range(0, 6).Select(i =>
        {
            double angle = i * System.Math.PI / 3.0;
            return Point(
                name: $"leg-{i}",
                localX: 4.0 * System.Math.Cos(angle),
                localZ: 4.0 * System.Math.Sin(angle),
                radius: 0.1,
                k: 1_000);
        }).ToArray();

        var result = Solve(input, legs);

        Assert.Equal(6, result.ContactCount);
        Assert.Equal(600.0, result.ForceWorld.Y, 9);
        Assert.True(result.TorqueWorld.Magnitude < 1e-9,
            $"Symmetric contacts produced torque {result.TorqueWorld}");
    }

    [Fact]
    public void OverCompressionAndLoadAreReportedWithoutSilentlyCappingForce()
    {
        var input = Input(datumY: -0.2, comY: 1.0);
        var result = Solve(input, Point(
            localY: 0.0,
            radius: 0.1,
            k: 10_000,
            maxCompression: 0.2,
            maxLoad: 2_000));
        var contact = Assert.Single(result.Points);

        Assert.Equal(0.3, contact.PenetrationM, 12);
        Assert.Equal(3_000.0, contact.NormalLoadN, 9);
        Assert.Equal(0.1, contact.TravelExcessM, 12);
        Assert.True(contact.IsOverTravel);
        Assert.True(contact.IsOverloaded);
        Assert.True(result.HasOverTravel);
        Assert.True(result.HasOverload);
        Assert.Equal(0.1, result.MaxTravelExcessM, 12);
    }

    [Fact]
    public void SphereSampleIncludesTranslationAndSurfaceRotation()
    {
        var body = new CelestialBody
        {
            Radius = 10.0,
            RotationalPeriod = 20.0,
            Velocity = new Vector3d(3, 4, 5),
        };
        var query = new Vector3d(10.2, 0, 0);

        var sample = SurfaceSample.FromSphere(body, query);

        AssertVector(new Vector3d(10, 0, 0), sample.PointWorld);
        AssertVector(Vector3d.Right, sample.NormalWorld);
        AssertVector(body.Velocity + body.GetSurfaceVelocity(sample.PointWorld), sample.VelocityWorld);
        Assert.True((sample.VelocityWorld - body.Velocity).Magnitude > 0.0);
    }

    private static RigidBodyContactInput Input(
        double datumY,
        double comY,
        Vector3d? velocity = null) => new(
            DatumPositionWorld: new Vector3d(0, datumY, 0),
            CenterOfMassPositionWorld: new Vector3d(0, comY, 0),
            CenterOfMassVelocityWorld: velocity ?? Vector3d.Zero,
            Orientation: Quaterniond.Identity,
            AngularVelocityWorld: Vector3d.Zero);

    private static ContactPointDefinition Point(
        string name = "foot",
        double localX = 0.0,
        double localY = 0.0,
        double localZ = 0.0,
        double radius = 0.1,
        double k = 1_000.0,
        double c = 0.0,
        double tangentialC = 0.0,
        double mu = 0.0,
        double maxCompression = 1.0,
        double maxLoad = 1_000_000.0) => new(
            Name: name,
            LocalPositionFromDatum: new Vector3d(localX, localY, localZ),
            ContactRadiusM: radius,
            SpringStiffnessNPerM: k,
            DampingNsPerM: c,
            TangentialDampingNsPerM: tangentialC,
            FrictionCoefficient: mu,
            MaxCompressionM: maxCompression,
            MaxLoadN: maxLoad);

    private static ContactWrench Solve(
        in RigidBodyContactInput input,
        params ContactPointDefinition[] points) =>
        SurfaceContactSolver.Evaluate(input, points, _ => FlatGround);

    private static void AssertVector(Vector3d expected, Vector3d actual, double tolerance = 1e-10)
    {
        Assert.InRange((actual - expected).Magnitude, 0.0, tolerance);
    }
}
