namespace ExosphereSimulation.Tests;

using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;
using Xunit;

public sealed class WaterContactSolverTests
{
    private static readonly WaterContactDefinition Water = new(4.5, 52, -3.78, 22,
        1025, 0.8, 0.5, 20, 35, -165, -140);

    [Fact]
    public void DryHullHasNoHydrodynamicLoad()
    {
        var body = new CelestialBody { Id = "earth", Radius = 6371000, GM = 3.986004418e14 };
        var ship = new Vessel();
        var position = Vector3d.Up*(body.Radius+10);
        var wrench = WaterContactSolver.Evaluate(ship, body, Water, position, -Vector3d.Up*2);
        Assert.Equal(Vector3d.Zero, wrench.ForceWorld);
        Assert.Equal(0, wrench.SubmergedVolumeM3);
        Assert.InRange(wrench.LowestPointAltitudeM, 6.219, 6.221);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(2)]
    public void DragDissipatesRelativeMotionAndBuoyancyMatchesDisplacedWater(double speed)
    {
        var body = new CelestialBody { Id = "earth", Radius = 6371000, GM = 3.986004418e14,
            Position = new Vector3d(1e10, 2e10, 3e10), Velocity = Vector3d.Right*30000 };
        var ship = new Vessel();
        var position = body.Position+Vector3d.Up*(body.Radius-2);
        var relative = Vector3d.Up*speed;
        var wrench = WaterContactSolver.Evaluate(ship, body, Water, position,
            body.Velocity+body.GetSurfaceVelocity(position)+relative);
        double volume = System.Math.PI*4.5*4.5*2;
        double support = 1025*volume*body.GetGravityAt(position).Magnitude;
        Assert.Equal(volume, wrench.SubmergedVolumeM3, 6);
        var drag = wrench.ForceWorld-Vector3d.Up*support;
        Assert.True(drag.Dot(relative) < 0);
        Assert.InRange(wrench.TorqueWorld.Magnitude, 0, 1e-6);
    }
    [Fact]
    public void HorizontalHalfSubmergedCylinderHasAnalyticVolumeAndCentroid()
    {
        var body = Earth();
        var ship = new Vessel { Orientation = Quaterniond.FromAxisAngle(Vector3d.Forward, System.Math.PI/2) };
        var wrench = WaterContactSolver.Evaluate(ship, body, Water, Vector3d.Up*body.Radius, Vector3d.Zero);
        Assert.Equal(System.Math.PI*Water.RadiusM*Water.RadiusM*Water.CylinderHeightM/2,
            wrench.SubmergedVolumeM3, 7);
        Assert.InRange(wrench.BuoyancyCenterOffsetWorld.X, 25.999999, 26.000001);
        Assert.Equal(-4*Water.RadiusM/(3*System.Math.PI), wrench.BuoyancyCenterOffsetWorld.Y, 7);
        Assert.InRange(wrench.HullLowestAltitudeM, -4.500001, -4.499999);
        // Hull touches water even though the axial nozzle witness is above the spherical sea.
        Assert.True(wrench.LowestPointAltitudeM >= 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(89.999)]
    [InlineData(90)]
    [InlineData(120)]
    [InlineData(180)]
    public void FullySubmergedCylinderRetainsVolumeAndMidpointAtEveryInclination(double angle)
    {
        var body = Earth();
        var ship = new Vessel { Orientation = Quaterniond.FromAxisAngle(Vector3d.Forward, angle*MathUtils.DEG_TO_RAD) };
        var wrench = WaterContactSolver.Evaluate(ship, body, Water, Vector3d.Up*(body.Radius-100), Vector3d.Zero);
        Assert.Equal(System.Math.PI*Water.RadiusM*Water.RadiusM*Water.CylinderHeightM,
            wrench.SubmergedVolumeM3, 7);
        Assert.InRange((wrench.BuoyancyCenterOffsetWorld-ship.Orientation.Rotate(Vector3d.Up)*26).Magnitude, 0, 1e-10);
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(35)]
    [InlineData(89.999)]
    [InlineData(90)]
    [InlineData(145)]
    [InlineData(179.999)]
    public void ComplementaryHalfDepthCutsPartitionTheFullCylinder(double angle)
    {
        var body = Earth();
        var axis = Quaterniond.FromAxisAngle(Vector3d.Forward, angle*MathUtils.DEG_TO_RAD).Rotate(Vector3d.Up);
        var ship = new Vessel { Orientation = Quaterniond.FromAxisAngle(Vector3d.Forward, angle*MathUtils.DEG_TO_RAD) };
        double midpointAltitude = -26*axis.Y;
        var a = WaterContactSolver.Evaluate(ship, body, Water, Vector3d.Up*(body.Radius+midpointAltitude+1), Vector3d.Zero);
        var b = WaterContactSolver.Evaluate(ship, body, Water, Vector3d.Up*(body.Radius+midpointAltitude-1), Vector3d.Zero);
        double fullVolume = System.Math.PI*Water.RadiusM*Water.RadiusM*Water.CylinderHeightM;
        Assert.InRange(System.Math.Abs(a.SubmergedVolumeM3+b.SubmergedVolumeM3-fullVolume), 0, 1e-5);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(90)]
    [InlineData(170)]
    public void DistributedDragDissipatesTranslationAndPitchAboutTheMassCenter(double angle)
    {
        var body = Earth();
        var ship = new Vessel {
            Orientation = Quaterniond.FromAxisAngle(Vector3d.Forward, angle*MathUtils.DEG_TO_RAD),
            AngularVelocity = Vector3d.Forward*0.1 };
        var comOffset = ship.Orientation.Rotate(Vector3d.Up)*Water.CenterOfMassYM;
        var comVelocity = new Vector3d(1, -2, 0.5);
        var datumVelocity = comVelocity-ship.AngularVelocity.Cross(comOffset);
        var wrench = WaterContactSolver.Evaluate(ship, body, Water, Vector3d.Up*(body.Radius-3), datumVelocity);
        double power = wrench.DragForceWorld.Dot(comVelocity)+wrench.DragTorqueWorld.Dot(ship.AngularVelocity);
        Assert.True(power < 0, $"Hydrodynamic power {power}");
        var staticWrench = WaterContactSolver.Evaluate(ship, body, Water, Vector3d.Up*(body.Radius-3),
            -ship.AngularVelocity.Cross(comOffset));
        Assert.True(staticWrench.DragTorqueWorld.Dot(ship.AngularVelocity) < 0);
    }

    [Fact]
    public void InclinedLoadsAreInvariantUnderTranslatingTheBodyFrame()
    {
        var body = Earth();
        var ship = new Vessel { Orientation = Quaterniond.FromAxisAngle(Vector3d.Forward, 0.8),
            AngularVelocity = Vector3d.Forward*0.02 };
        var position = Vector3d.Up*(body.Radius-2);
        var velocity = new Vector3d(1, -2, 0.5);
        var original = WaterContactSolver.Evaluate(ship, body, Water, position, velocity);
        body.Position = new Vector3d(1e10, 2e10, 3e10); body.Velocity = Vector3d.Right*30000;
        var translated = WaterContactSolver.Evaluate(ship, body, Water, body.Position+position, body.Velocity+velocity);
        Assert.InRange((original.ForceWorld-translated.ForceWorld).Magnitude, 0, 0.01);
        Assert.InRange((original.TorqueWorld-translated.TorqueWorld).Magnitude, 0, 0.1);
    }

    [Theory]
    [InlineData(22.5)]
    [InlineData(45)]
    [InlineData(70)]
    public void ObliqueMidpointWaterlineMatchesAnalyticFirstMoment(double angle)
    {
        var body = Earth();
        var ship = new Vessel { Orientation = Quaterniond.FromAxisAngle(Vector3d.Forward, angle*MathUtils.DEG_TO_RAD) };
        var axis = ship.Orientation.Rotate(Vector3d.Up);
        double c = axis.Dot(Vector3d.Up), sin = System.Math.Sqrt(1-c*c), waterline = Water.CylinderHeightM/2;
        var radial = (Vector3d.Up-axis*c)/sin;
        var position = Vector3d.Up*(body.Radius-waterline*c);
        var wrench = WaterContactSolver.Evaluate(ship, body, Water, position, Vector3d.Zero);
        // Integrate y <= waterline - (sin/c) z over the complete circular disk.
        // Its second moment is πR^4/4; both end caps are outside the waterline cut.
        double axialCentroid = waterline/2+Water.RadiusM*Water.RadiusM*sin*sin/(8*c*c*waterline);
        double radialCentroid = -Water.RadiusM*Water.RadiusM*sin/(4*c*waterline);
        var exactCentroid = axis*axialCentroid+radial*radialCentroid;
        Assert.InRange((wrench.BuoyancyCenterOffsetWorld-exactCentroid).Magnitude, 0, 0.00005);
        Assert.InRange(System.Math.Abs(wrench.SubmergedVolumeM3-System.Math.PI*Water.RadiusM*Water.RadiusM*waterline), 0, 1e-5);
    }

    [Theory]
    [InlineData(86164)]
    [InlineData(-86164)]
    public void HullCorotatingWithTranslatedWaterHasNoDrag(double rotationPeriod)
    {
        var body = new CelestialBody { Id = "earth", Radius = 6371000, GM = 3.986004418e14,
            RotationalPeriod = rotationPeriod, AxialTilt = 23.4,
            Position = new Vector3d(1e10, 2e10, 3e10), Velocity = Vector3d.Right*30000 };
        var ship = new Vessel { Orientation = Quaterniond.FromAxisAngle(Vector3d.Forward, 0.8),
            AngularVelocity = body.RotationAxis*body.AngularSpeed };
        var position = body.Position+Vector3d.Right*(body.Radius-3);
        var velocity = body.Velocity+body.GetSurfaceVelocity(position);
        var wrench = WaterContactSolver.Evaluate(ship, body, Water, position, velocity);
        Assert.True(wrench.SubmergedVolumeM3 > 0);
        Assert.InRange(wrench.EntrySpeedMps, 0, 1e-8);
        Assert.InRange(wrench.DragForceWorld.Magnitude, 0, 1e-8);
        Assert.InRange(wrench.DragTorqueWorld.Magnitude, 0, 1e-8);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(0, false)]
    [InlineData(4, true)]
    [InlineData(4, false)]
    public void HorizontalWetStripDragMatchesItsProjectedSilhouette(double altitude, bool verticalFlow)
    {
        var body = Earth();
        var ship = new Vessel { Orientation = Quaterniond.FromAxisAngle(Vector3d.Forward, System.Math.PI/2) };
        var velocity = (verticalFlow ? Vector3d.Up : Vector3d.Forward)*2;
        var wrench = WaterContactSolver.Evaluate(ship, body, Water, Vector3d.Up*(body.Radius+altitude), velocity);
        // Vertical projection is the wet segment's chord; horizontal projection is its depth.
        double width = verticalFlow ? 2*System.Math.Sqrt(Water.RadiusM*Water.RadiusM-altitude*altitude)
            : Water.RadiusM-altitude;
        var expected = velocity*(-0.5*Water.DensityKgPerM3*Water.DragCoefficient*width*Water.CylinderHeightM*2);
        Assert.InRange((wrench.DragForceWorld-expected).Magnitude, 0, 1e-7);
    }

    private static CelestialBody Earth() => new() { Id = "earth", Radius = 6371000, GM = 3.986004418e14 };
}
