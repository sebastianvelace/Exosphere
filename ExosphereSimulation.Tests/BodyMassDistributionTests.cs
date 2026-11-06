namespace Exosphere.Simulation.Tests;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;

public sealed class BodyMassDistributionTests
{
    [Fact]
    public void AsymmetricDepletionHasTheAnalyticCentralPropertiesAndDerivatives()
    {
        var sample = Pair().Sample(1);
        Assert.Equal(4, sample.MassKg);
        Assert.Equal(-1, sample.MassRateKgPerS);
        Assert.Equal(new Vector3d(2, 0, 0), sample.CenterOfMassBodyM);
        Assert.Equal(new Vector3d(-0.5, 0, 0), sample.CenterOfMassRateBodyMps);
        Assert.Equal(new Vector3d(-0.25, 0, 0), sample.CenterOfMassAccelerationBodyMps2);
        Assert.Equal(Matrix3x3d.Diagonal(2, 18, 18), sample.InertiaBodyKgM2);
        Assert.Equal(Matrix3x3d.Diagonal(0, -4, -4), sample.InertiaRateBodyKgM2PerS);
    }

    [Fact]
    public void NonCollinearMassesHaveTheFullCentralTensor()
    {
        var distribution = new BodyMassDistribution(2,
        [
            Point("x", new(1, 0, 0), new MassKnot(0, 2)),
            Point("y", new(0, 2, 0), new MassKnot(0, 1)),
            Point("z", new(0, 0, 3), new MassKnot(0, 3)),
        ]);
        var sample = distribution.Sample(0);
        Assert.InRange((new Vector3d(1.0 / 3, 1.0 / 3, 1.5) - sample.CenterOfMassBodyM).Magnitude, 0, 1e-15);
        // Independently shift diag(31,29,6), the datum tensor of these three points.
        MatrixClose(new(101.0 / 6, 2.0 / 3, 3, 2.0 / 3, 89.0 / 6, 3,
            3, 3, 14.0 / 3), sample.InertiaBodyKgM2, 1e-13);
        Assert.Equal(default, sample.InertiaRateBodyKgM2PerS);
    }

    [Fact]
    public void DerivativesAgreeWithIndependentCenteredDifferencesAwayFromEvents()
    {
        var distribution = new BodyMassDistribution(2,
        [
            new("a", new(1, 0, 0), Matrix3x3d.Diagonal(0.2, 0.3, 0.4), [new MassKnot(0, 2)]),
            Point("b", new(0, 2, 0), new MassKnot(0, 1), new MassKnot(2, 0.4)),
            Point("c", new(0, 0, 3), new MassKnot(0, 3), new MassKnot(2, 4)),
        ]);
        const double time = 0.75, h = 1e-5;
        var before = distribution.Sample(time - h);
        var at = distribution.Sample(time);
        var after = distribution.Sample(time + h);
        Assert.InRange(((after.CenterOfMassBodyM - before.CenterOfMassBodyM) / (2 * h)
            - at.CenterOfMassRateBodyMps).Magnitude, 0, 1e-9);
        Assert.InRange(((after.CenterOfMassRateBodyMps - before.CenterOfMassRateBodyMps) / (2 * h)
            - at.CenterOfMassAccelerationBodyMps2).Magnitude, 0, 1e-9);
        var beforeValues = Values(before.InertiaBodyKgM2);
        var afterValues = Values(after.InertiaBodyKgM2);
        var rate = Values(at.InertiaRateBodyKgM2PerS);
        for (int i = 0; i < rate.Length; i++)
            Assert.InRange(System.Math.Abs((afterValues[i] - beforeValues[i]) / (2 * h) - rate[i]), 0, 1e-8);
    }

    [Fact]
    public void FlowCutsPreserveMassAndInertiaButSelectDifferentOneSidedRates()
    {
        var distribution = new BodyMassDistribution(2,
        [
            new("dry", Vector3d.Zero, Matrix3x3d.Identity, [new MassKnot(0, 2)]),
            Point("propellant", new(4, 0, 0), new MassKnot(0, 3), new MassKnot(0.5, 1.5)),
        ]);
        var left = distribution.Sample(0.5, MassBoundarySide.Left);
        var right = distribution.Sample(0.5, MassBoundarySide.Right);
        Assert.Equal(new[] { 0, 0.5, 2 }, distribution.BoundariesSeconds);
        Assert.Equal(left.MassKg, right.MassKg);
        Assert.Equal(left.CenterOfMassBodyM, right.CenterOfMassBodyM);
        Assert.Equal(left.InertiaBodyKgM2, right.InertiaBodyKgM2);
        Assert.Equal(-3, left.MassRateKgPerS);
        Assert.True(left.CenterOfMassRateBodyMps.Magnitude > 0);
        Assert.Equal(0, right.MassRateKgPerS);
        Assert.Equal(Vector3d.Zero, right.CenterOfMassRateBodyMps);
        Assert.Equal(Vector3d.Zero, right.CenterOfMassAccelerationBodyMps2);
        Assert.Equal(default, right.InertiaRateBodyKgM2PerS);
        Assert.Equal(right, distribution.Sample(0.5));
        Assert.Equal(right, distribution.Sample(1.5));
    }

    [Fact]
    public void MainHeaderAndCoastEventsRetainTheirOwnIntervals()
    {
        var distribution = new BodyMassDistribution(2,
        [
            new("dry", Vector3d.Zero, Matrix3x3d.Identity, [new MassKnot(0, 10)]),
            Point("main", new(2, 0, 0), new MassKnot(0, 10), new MassKnot(0.4, 0)),
            Point("header", new(8, 0, 0), new MassKnot(0, 20), new MassKnot(0.4, 20), new MassKnot(1, 10)),
        ]);
        Assert.Equal(new[] { 0, 0.4, 1, 2 }, distribution.BoundariesSeconds);
        Assert.Equal(35, distribution.Sample(0.2).MassKg);
        Assert.Equal(30, distribution.Sample(0.4).MassKg);
        Assert.Equal(-25, distribution.Sample(0.4, MassBoundarySide.Left).MassRateKgPerS);
        Assert.Equal(-10 / 0.6, distribution.Sample(0.4, MassBoundarySide.Right).MassRateKgPerS, 12);
        var coast = distribution.Sample(1.5);
        Assert.Equal(20, coast.MassKg);
        Assert.Equal(new Vector3d(4, 0, 0), coast.CenterOfMassBodyM);
        Assert.Equal(0, coast.MassRateKgPerS);
        Assert.Equal(Vector3d.Zero, coast.CenterOfMassRateBodyMps);
    }

    [Fact]
    public void InternalRedistributionMovesTheCenterWithoutChangingTotalMass()
    {
        var distribution = new BodyMassDistribution(1,
        [
            Point("left", Vector3d.Zero, new MassKnot(0, 3), new MassKnot(1, 2)),
            Point("right", new(4, 0, 0), new MassKnot(0, 1), new MassKnot(1, 2)),
        ]);
        var sample = distribution.Sample(0.5);
        Assert.Equal(4, sample.MassKg);
        Assert.Equal(0, sample.MassRateKgPerS);
        Assert.Equal(new Vector3d(1.5, 0, 0), sample.CenterOfMassBodyM);
        Assert.Equal(Vector3d.Right, sample.CenterOfMassRateBodyMps);
        Assert.Equal(Vector3d.Zero, sample.CenterOfMassAccelerationBodyMps2);
    }

    [Fact]
    public void CentralTensorsDoNotLosePrecisionUnderALargeBodyDatumTranslation()
    {
        var shift = new Vector3d(1e12, -1e12, 1e12);
        var near = Pair().Sample(0.75);
        var far = Pair(shift).Sample(0.75);
        Assert.Equal(near.CenterOfMassBodyM + shift, far.CenterOfMassBodyM);
        Assert.Equal(near.CenterOfMassRateBodyMps, far.CenterOfMassRateBodyMps);
        Assert.Equal(near.CenterOfMassAccelerationBodyMps2, far.CenterOfMassAccelerationBodyMps2);
        Assert.Equal(near.InertiaBodyKgM2, far.InertiaBodyKgM2);
        Assert.Equal(near.InertiaRateBodyKgM2PerS, far.InertiaRateBodyKgM2PerS);
    }

    [Fact]
    public void AnEmptyInactiveComponentDoesNotContaminateThePopulatedBody()
    {
        var distribution = new BodyMassDistribution(1,
        [
            Point("empty", new(1e308, -1e308, 1e308), new MassKnot(0, 0)),
            new("dry", Vector3d.Zero, Matrix3x3d.Identity, [new MassKnot(0, 2)]),
        ]);
        Assert.Equal(Matrix3x3d.Diagonal(2, 2, 2), distribution.Sample(0.5).InertiaBodyKgM2);
    }

    [Fact]
    public void ATinyLoadNearExhaustionDoesNotLosePrecisionBySubtractingBulkMasses()
    {
        const double initial = 1e9;
        var distribution = new BodyMassDistribution(1,
        [
            new("dry", Vector3d.Zero, Matrix3x3d.Identity, [new MassKnot(0, 1)]),
            Point("propellant", Vector3d.Right, new MassKnot(0, initial), new MassKnot(1, 0)),
        ]);
        double time = double.BitDecrement(1);
        double remaining = initial * (1 - time);
        var sample = distribution.Sample(time);
        Assert.Equal(1 + remaining, sample.MassKg);
        Assert.Equal(remaining / (1 + remaining), sample.CenterOfMassBodyM.X, 15);
    }

    [Fact]
    public void CoLocatedMassHasZeroCenterDerivativesEvenWhenTheRelativeRateOverflows()
    {
        var distribution = new BodyMassDistribution(1e100,
        [new("body", Vector3d.Zero, Matrix3x3d.Identity,
            [new MassKnot(0, 1e-300), new MassKnot(1e100, 1e200)])]);
        var sample = distribution.Sample(0);
        Assert.True(double.IsFinite(sample.MassRateKgPerS));
        Assert.Equal(Vector3d.Zero, sample.CenterOfMassBodyM);
        Assert.Equal(Vector3d.Zero, sample.CenterOfMassRateBodyMps);
        Assert.Equal(Vector3d.Zero, sample.CenterOfMassAccelerationBodyMps2);
    }

    [Fact]
    public void ARepresentableGrowingLoadDoesNotDisappearWhenItsTimeFractionUnderflows()
    {
        var distribution = new BodyMassDistribution(1e308,
        [
            new("dry", Vector3d.Zero, Matrix3x3d.Identity, [new MassKnot(0, 1e-300)]),
            Point("added", Vector3d.Zero, new MassKnot(0, 0), new MassKnot(1e308, 1e308)),
        ]);
        Assert.Equal(0, 1e-300 / distribution.DurationSeconds);
        var sample = distribution.Sample(1e-300);
        Assert.Equal(2e-300, sample.MassKg);
        Assert.Equal(1, sample.MassRateKgPerS);
    }

    [Fact]
    public void ComponentsReproduceUnshiftedPartGraphEndpointProperties()
    {
        var part = new Part(new PartDefinition
        {
            Id = "fixture", MassDry = 10, LengthM = 6, DiameterM = 4, FuelCapacityLF = 30,
            HeaderTanks = [new() { Id = "header", Propellant = "oxidizer", CapacityKg = 40, CenterYM = 2 }],
        });
        var graph = new PartGraph(); graph.SetRoot(part);
        Assert.True(graph.TryGetPartLocalPosition(part, out var station));
        var cylinder = Matrix3x3d.Diagonal(4, 2, 4);
        var distribution = new BodyMassDistribution(2,
        [
            new("dry", station, cylinder, [new MassKnot(0, 10)]),
            new("main", station, cylinder, [new MassKnot(0, 30), new MassKnot(2, 10)]),
            Point("header", station + Vector3d.Up * 2, new MassKnot(0, 40), new MassKnot(2, 20)),
        ]);
        Compare(graph.GetMassProperties(), distribution.Sample(0).ToRigidBodyMassProperties());
        part.LiquidFuel = 10;
        part.SetHeaderPropellant([20]);
        Compare(graph.GetMassProperties(), distribution.Sample(2).ToRigidBodyMassProperties());
    }

    [Fact]
    public void SeparateDryAndPropellantStationsKeepTheParallelAxisVariance()
    {
        var part = new Part(new PartDefinition
        {
            Id = "offset-fixture", MassDry = 10, LengthM = 6, DiameterM = 4,
            DryMassCenterYM = 2, FuelCapacityLF = 30,
        });
        var graph = new PartGraph(); graph.SetRoot(part);
        Assert.True(graph.TryGetPartLocalPosition(part, out var station));
        var cylinder = Matrix3x3d.Diagonal(4, 2, 4);
        var distribution = new BodyMassDistribution(1,
        [
            new("dry", station + Vector3d.Up * 2, cylinder, [new MassKnot(0, 10)]),
            new("main", station, cylinder, [new MassKnot(0, 30)]),
        ]);
        var sample = distribution.Sample(0);
        var graphProperties = graph.GetMassProperties();
        Assert.Equal(graphProperties.CenterOfMassBody, sample.CenterOfMassBodyM);
        // 160 kg m² of cylinder inertia plus 10*30/(10+30) * 2² = 30 kg m² of spread between the
        // dry centre and the propellant; the former shifted-lump model dropped the 30.
        Assert.Equal(190, graphProperties.InertiaBody.M11, 12);
        Assert.Equal(190, sample.InertiaBodyKgM2.M11, 12);
        Assert.Equal(sample.InertiaBodyKgM2.M33, graphProperties.InertiaBody.M33, 12);
        Assert.Equal(sample.InertiaBodyKgM2.M22, graphProperties.InertiaBody.M22, 12);
    }

    [Fact]
    public void SamplingIsAllocationFreeAfterPreparation()
    {
        var distribution = Pair();
        for (int i = 0; i < 1000; i++) distribution.Sample(0.75);
        long before = GC.GetAllocatedBytesForCurrentThread();
        double checksum = 0;
        for (int i = 0; i < 10000; i++) checksum += distribution.Sample(0.75).InertiaBodyKgM2.M11;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(checksum > 0);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void PreparationCopiesTheComponentAndKnotContainers()
    {
        MassKnot[] knots = [new MassKnot(0, 3), new MassKnot(2, 1)];
        var part = new FixedShapeMassComponent("body", Vector3d.Zero, Matrix3x3d.Identity, knots);
        FixedShapeMassComponent[] components = [part];
        var distribution = new BodyMassDistribution(2, components);
        var expected = distribution.Sample(1);
        knots[1] = new MassKnot(2, 1000);
        components[0] = Point("replacement", Vector3d.Right, new MassKnot(0, 1000));
        Assert.Equal(expected, distribution.Sample(1));
        Assert.Throws<NotSupportedException>(() => ((IList<MassKnot>)part.Knots)[0] = default);
        Assert.Throws<NotSupportedException>(() => ((IList<FixedShapeMassComponent>)distribution.Components)[0] = part);
        Assert.Throws<NotSupportedException>(() => ((IList<double>)distribution.BoundariesSeconds)[0] = 42);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void SamplingRejectsTimesOutsideItsDeclaredWindow(double time) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Pair().Sample(time));

    [Fact]
    public void OuterBoundaryDerivativesAreNotInvented()
    {
        var distribution = Pair();
        Assert.Equal(-1, distribution.Sample(0).MassRateKgPerS);
        Assert.Equal(-1, distribution.Sample(2).MassRateKgPerS);
        Assert.Throws<ArgumentException>(() => distribution.Sample(0, MassBoundarySide.Left));
        Assert.Throws<ArgumentException>(() => distribution.Sample(2, MassBoundarySide.Right));
        Assert.Throws<ArgumentOutOfRangeException>(() => distribution.Sample(1, (MassBoundarySide)42));
    }

    [Fact]
    public void InvalidProfilesAndDiscontinuousRefillsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Point("a", default));
        Assert.Throws<ArgumentException>(() => Point("a", default, new MassKnot(1, 1)));
        Assert.Throws<ArgumentException>(() => Point("a", default, new MassKnot(0, -1)));
        Assert.Throws<ArgumentException>(() => Point("a", default, new MassKnot(0, 1), new MassKnot(1, 0), new MassKnot(1, 1)));
        Assert.Throws<ArgumentException>(() => Point("a", default, new MassKnot(0, 0), new MassKnot(1e-300, 1e308)));
        Assert.Throws<ArgumentException>(() => Point("a", default, new MassKnot(0, 1e-300), new MassKnot(1e100, 0)));
        Assert.Throws<ArgumentException>(() => Point("a", default, new MassKnot(0, double.NaN)));
        Assert.Throws<ArgumentException>(() => Point("a", new(double.NaN, 0, 0), new MassKnot(0, 1)));
    }

    [Fact]
    public void ADistributionCannotBecomeEmptyOrHideAnEventBeyondTheWindow()
    {
        Assert.Throws<ArgumentException>(() => new BodyMassDistribution(1, []));
        Assert.Throws<ArgumentException>(() => new BodyMassDistribution(1, [Point("a", default, new MassKnot(0, 0))]));
        Assert.Throws<ArgumentException>(() => new BodyMassDistribution(1, [Point("a", default, new MassKnot(0, 1), new MassKnot(1, 0))]));
        Assert.Throws<ArgumentException>(() => new BodyMassDistribution(1, [Point("a", default, new MassKnot(0, 1), new MassKnot(2, 0))]));
        Assert.Throws<ArgumentException>(() => new BodyMassDistribution(1,
            [Point("same", default, new MassKnot(0, 1)), Point("same", default, new MassKnot(0, 1))]));
        Assert.Throws<ArgumentException>(() => new BodyMassDistribution(1,
            [Point("a", default, new MassKnot(0, 1e308)), Point("b", default, new MassKnot(0, 1e308))]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BodyMassDistribution(double.NaN, []));
    }

    [Fact]
    public void CentralShapeTensorsMustBePhysicallyRealizable()
    {
        Assert.Throws<ArgumentException>(() => new FixedShapeMassComponent("negative", default,
            Matrix3x3d.Diagonal(-1e-16, 1, 1), [new MassKnot(0, 1)]));
        Assert.Throws<ArgumentException>(() => new FixedShapeMassComponent("triangle", default,
            Matrix3x3d.Diagonal(1, 1, 3), [new MassKnot(0, 1)]));
        Assert.Throws<ArgumentException>(() => new FixedShapeMassComponent("indefinite", default,
            new(1, 2, 0, 2, 1, 0, 0, 0, 1), [new MassKnot(0, 1)]));
        Assert.Throws<ArgumentException>(() => new FixedShapeMassComponent("asymmetric", default,
            new(1, 0.1, 0, 0, 1, 0, 0, 0, 1), [new MassKnot(0, 1)]));
        // Symmetric points at ±(1,2,3) have this rank-one second-moment matrix.
        var valid = new FixedShapeMassComponent("line", default,
            new(13, -2, -3, -2, 10, -6, -3, -6, 5), [new MassKnot(0, 1)]);
        Assert.Equal(13, valid.SpecificInertiaBodyM2.M11);
    }

    [Fact]
    public void PointComponentsAreAllowedButRigidRotationRequiresInvertibleInertia()
    {
        var sample = new BodyMassDistribution(1, [Point("point", Vector3d.Zero, new MassKnot(0, 1))]).Sample(0);
        Assert.Equal(default, sample.InertiaBodyKgM2);
        Assert.Throws<InvalidOperationException>(() => sample.ToRigidBodyMassProperties());
    }

    [Fact]
    public void RigidRotationRejectsAnInvertibleButIndefiniteCandidateTensor()
    {
        var sample = Pair().Sample(1) with { InertiaBodyKgM2 = Matrix3x3d.Diagonal(-1, -1, 1) };
        Assert.Equal(1, sample.InertiaBodyKgM2.Determinant);
        Assert.Throws<InvalidOperationException>(() => sample.ToRigidBodyMassProperties());
    }

    private static BodyMassDistribution Pair(Vector3d shift = default) => new(2,
    [
        new("dry", shift, Matrix3x3d.Identity, [new MassKnot(0, 2)]),
        Point("propellant", shift + Vector3d.Right * 4, new MassKnot(0, 3), new MassKnot(2, 1)),
    ]);

    private static FixedShapeMassComponent Point(string id, Vector3d station, params MassKnot[] knots) =>
        new(id, station, default, knots);

    private static void Compare(RigidBodyMassProperties expected, RigidBodyMassProperties actual)
    {
        Assert.Equal(expected.Mass, actual.Mass);
        Assert.Equal(expected.CenterOfMassBody, actual.CenterOfMassBody);
        Assert.Equal(expected.InertiaBody, actual.InertiaBody);
        Assert.Equal(expected.InverseInertiaBody, actual.InverseInertiaBody);
    }

    private static double[] Values(Matrix3x3d matrix) => [matrix.M11, matrix.M12, matrix.M13,
        matrix.M21, matrix.M22, matrix.M23, matrix.M31, matrix.M32, matrix.M33];

    private static void MatrixClose(Matrix3x3d expected, Matrix3x3d actual, double tolerance)
    {
        var a = Values(expected); var b = Values(actual);
        for (int i = 0; i < a.Length; i++) Assert.InRange(System.Math.Abs(a[i] - b[i]), 0, tolerance);
    }
}
