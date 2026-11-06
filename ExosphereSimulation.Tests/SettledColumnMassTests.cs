namespace Exosphere.Simulation.Tests;

using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;

/// <summary>
/// The shared mass decomposition with declared tanks: a settled liquid column standing on its
/// tank's aft end, whose centroid and shape follow its mass. Composite properties, the part's own
/// centre and the continuous candidate sampler all read that one decomposition.
/// </summary>
[Trait("FidelityGate", "F4")]
[Trait("GateKind", "analytic")]
public sealed class SettledColumnMassTests
{
    private const double Density = 1000.0;
    private const double Radius = 1.0;
    private static readonly double Lambda = Density * System.Math.PI * Radius * Radius;

    private static Part TankPart(double oxidizerKg, double dryKg = 0.0) => new(new PartDefinition
    {
        Id = "tank", CategoryStr = "fuel_tank", MassDry = dryKg, LengthM = 10, DiameterM = 2,
        FuelCapacityOx = oxidizerKg,
        PropellantTanks = [new PropellantTankDef { Resource = "oxidizer", BottomYM = -4, TopYM = 4, RadiusM = Radius, DensityKgPerM3 = Density }],
    }, "tank");

    [Fact]
    public void AColumnStandsOnItsTankFloorWithTheSolidCylinderInertia()
    {
        double mass = 2.0 * Lambda; // a 2 m column
        var part = TankPart(mass);
        var graph = new PartGraph(); graph.SetRoot(part);
        Assert.True(graph.TryGetPartLocalPosition(part, out var station));
        var properties = graph.GetMassProperties();
        // Floor at -4 m from the part centre: the 2 m column's centroid is 1 m above it.
        Assert.Equal(station.Y - 3.0, properties.CenterOfMassBody.Y, 12);
        Assert.Equal(-3.0, part.MassCenterOffsetYM, 12);
        Assert.Equal(mass * (3 * Radius * Radius + 4.0) / 12.0, properties.InertiaBody.M11, 6);
        Assert.Equal(mass * Radius * Radius / 2.0, properties.InertiaBody.M22, 6);
    }

    [Fact]
    public void DryStructureAndColumnKeepTheirSeparation()
    {
        double mass = 2.0 * Lambda;
        var part = TankPart(mass, dryKg: 1000);
        var graph = new PartGraph(); graph.SetRoot(part);
        var properties = graph.GetMassProperties();
        Assert.True(graph.TryGetPartLocalPosition(part, out var station));
        double reduced = 1000.0 * mass / (1000.0 + mass);
        // Dry cylinder (L 10, R 1) at the centre, column centroid 3 m below it.
        double expected = 1000.0 * (3 + 100) / 12.0 + mass * (3 + 4.0) / 12.0 + reduced * 9.0;
        Assert.Equal(expected, properties.InertiaBody.M11, 5);
        Assert.Equal(station.Y + mass * -3.0 / (1000.0 + mass), properties.CenterOfMassBody.Y, 12);
    }

    [Fact]
    public void ADrainingColumnHasAnalyticCentroidAndInertiaRates()
    {
        // 4 m column draining to 2 m over 2 s, beside a fixed 1000 kg point at the origin.
        var column = FixedShapeMassComponent.SettledColumn("lox", new Vector3d(0, -4, 0), Vector3d.Up, Lambda, Radius,
            [new MassKnot(0, 4 * Lambda), new MassKnot(2, 2 * Lambda)]);
        var point = new FixedShapeMassComponent("dry", Vector3d.Zero, Matrix3x3d.Diagonal(0, 0, 0), [new MassKnot(0, 1000)]);
        var distribution = new BodyMassDistribution(2, [column, point]);
        const double time = 0.8, h = 1e-5;
        var before = distribution.Sample(time - h);
        var at = distribution.Sample(time);
        var after = distribution.Sample(time + h);

        double m = (4 - time) * Lambda, height = m / Lambda;
        Assert.Equal(m + 1000, at.MassKg, 9);
        Assert.Equal(m * (-4 + height / 2) / (m + 1000), at.CenterOfMassBodyM.Y, 12);
        Assert.InRange(((after.CenterOfMassBodyM - before.CenterOfMassBodyM) / (2 * h)
            - at.CenterOfMassRateBodyMps).Magnitude, 0, 1e-8);
        Assert.InRange(((after.CenterOfMassRateBodyMps - before.CenterOfMassRateBodyMps) / (2 * h)
            - at.CenterOfMassAccelerationBodyMps2).Magnitude, 0, 1e-7);
        var rate = at.InertiaRateBodyKgM2PerS;
        double[] difference =
        [
            (after.InertiaBodyKgM2.M11 - before.InertiaBodyKgM2.M11) / (2 * h),
            (after.InertiaBodyKgM2.M22 - before.InertiaBodyKgM2.M22) / (2 * h),
            (after.InertiaBodyKgM2.M33 - before.InertiaBodyKgM2.M33) / (2 * h),
        ];
        Assert.Equal(difference[0], rate.M11, 2);
        Assert.Equal(difference[1], rate.M22, 4);
        Assert.Equal(difference[2], rate.M33, 2);
        Assert.Equal(rate.M11, rate.M33, 9);
    }

    [Fact]
    public void TheSamplerMatchesTheGraphAtEveryLoad()
    {
        double full = 6.0 * Lambda;
        var part = TankPart(full, dryKg: 2000);
        var graph = new PartGraph(); graph.SetRoot(part);
        Assert.True(graph.TryGetPartLocalPosition(part, out var station));
        var dry = new FixedShapeMassComponent("dry", station,
            Matrix3x3d.Diagonal((3 + 100) / 12.0, 0.5, (3 + 100) / 12.0), [new MassKnot(0, 2000)]);
        var column = FixedShapeMassComponent.SettledColumn("lox", station + new Vector3d(0, -4, 0), Vector3d.Up,
            Lambda, Radius, [new MassKnot(0, full), new MassKnot(3, full / 3)]);
        var distribution = new BodyMassDistribution(3, [dry, column]);
        foreach (double time in new[] { 0.0, 1.3, 3.0 })
        {
            part.Oxidizer = full - time * (full - full / 3) / 3;
            var graphProperties = graph.GetMassProperties();
            var sample = distribution.Sample(time);
            Assert.Equal(graphProperties.Mass, sample.MassKg, 6);
            Assert.InRange((graphProperties.CenterOfMassBody - sample.CenterOfMassBodyM).Magnitude, 0, 1e-9);
            Assert.Equal(graphProperties.InertiaBody.M11, sample.InertiaBodyKgM2.M11, 3);
            Assert.Equal(graphProperties.InertiaBody.M22, sample.InertiaBodyKgM2.M22, 3);
        }
    }

    [Fact]
    public void ATankTooSmallForItsLoadIsRejected()
    {
        var definition = TankPart(9.0 * Lambda).Definition;
        Assert.Throws<InvalidDataException>(() => PartCatalog.ValidatePropellantTanks(definition));
        Assert.Null(Record.Exception(() => PartCatalog.ValidatePropellantTanks(TankPart(7.9 * Lambda).Definition)));
    }
}
