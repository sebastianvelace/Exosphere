namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;
using Xunit;

/// <summary>
/// R6 — body lift with angle of attack. A symmetric 9 m cylinder generates lift
/// perpendicular to the flow (CL = CLmax·sin 2α): zero flying axially and at exact
/// broadside, maximal at 45°, and L/D ≈ 0.3 at the ~70° attitude Starship flies in EDL.
/// </summary>
public sealed class AerodynamicLiftTests
{
    private const double Density = 0.02;
    private const double Speed = 2_500.0;
    private const int PartCount = 5;

    [Fact]
    public void LiftIsZeroFlyingAxiallyAndAtExactBroadside()
    {
        // Nose-first into the flow (α = 0).
        var axial = AerodynamicsModel.ComputeLift(
            Density, Vector3d.Up * Speed, Vector3d.Up, PartCount);

        // Pure broadside (α = 90°): a symmetric cylinder has no lift there.
        var broadside = AerodynamicsModel.ComputeLift(
            Density, Vector3d.Right * Speed, Vector3d.Up, PartCount);

        Assert.True(axial.Magnitude < 1e-9);
        Assert.True(broadside.Magnitude < 1e-9);
    }

    [Fact]
    public void LiftIsPerpendicularToFlowAndPointsTowardTheNoseSide()
    {
        // Flow along +X, nose pitched 45° above the velocity (toward +Y).
        var velocity = Vector3d.Right * Speed;
        var axis = (Vector3d.Right + Vector3d.Up).Normalized;

        var lift = AerodynamicsModel.ComputeLift(Density, velocity, axis, PartCount);

        Assert.True(lift.Magnitude > 0.0);
        Assert.True(System.Math.Abs(lift.Normalized.Dot(velocity.Normalized)) < 1e-9,
            "lift must be perpendicular to the flow");
        Assert.True(lift.Y > 0.0, "nose above prograde must lift up");
    }

    [Fact]
    public void LiftFlipsSideWhenFlyingTailFirst()
    {
        var velocity = Vector3d.Right * Speed;
        // Same geometric tilt toward +Y, but tail-first (axis mostly against the flow).
        var axis = (-Vector3d.Right + Vector3d.Up).Normalized;

        var lift = AerodynamicsModel.ComputeLift(Density, velocity, axis, PartCount);

        Assert.True(lift.Magnitude > 0.0);
        Assert.True(lift.Y < 0.0, "tail-first the lift must flip to the other side");
    }

    [Fact]
    public void LiftOverDragIsRealisticAtStarshipEntryAttitude()
    {
        // α ≈ 70°: the belly-flop-with-lift regime Starship actually flies.
        double alpha = 70.0 * System.Math.PI / 180.0;
        var velocity = Vector3d.Right * Speed;
        var axis = new Vector3d(System.Math.Cos(alpha), System.Math.Sin(alpha), 0.0);

        var lift = AerodynamicsModel.ComputeLift(Density, velocity, axis, PartCount);
        // Compare against hypersonic drag (Mach multiplier 1 at Mach ≥ 5, temp 220 K).
        var drag = AerodynamicsModel.ComputeReentryDrag(Density, velocity, axis, PartCount, 220.0);

        double liftOverDrag = lift.Magnitude / drag.Magnitude;
        Assert.True(liftOverDrag > 0.2 && liftOverDrag < 0.45,
            $"L/D at 70° AoA should be ≈0.3 (got {liftOverDrag:F3})");
    }

    [Fact]
    public void LiftCoefficientPeaksAtFortyFiveDegrees()
    {
        double cl45 = AerodynamicsModel.EffectiveLiftCoefficient(System.Math.Cos(System.Math.PI / 4.0));
        double cl20 = AerodynamicsModel.EffectiveLiftCoefficient(System.Math.Cos(20.0 * System.Math.PI / 180.0));
        double cl70 = AerodynamicsModel.EffectiveLiftCoefficient(System.Math.Cos(70.0 * System.Math.PI / 180.0));

        Assert.Equal(AerodynamicsModel.MaxLiftCoefficient, cl45, 10);
        Assert.True(cl20 < cl45 && cl70 < cl45);
        Assert.True(cl20 > 0.0 && cl70 > 0.0);
    }

    [Fact]
    public void LiftUpEntryAxisHoldsNominalAngleAndProducesOutwardLift()
    {
        var velocity = new Vector3d(1.0, -0.1, 0.0).Normalized;
        var localUp = Vector3d.Up;
        var axis = AerodynamicsModel.ComputeLiftUpEntryAxis(localUp, velocity);

        double angleDeg = System.Math.Acos(axis.Dot(velocity)) * 180.0 / System.Math.PI;
        var lift = AerodynamicsModel.ComputeLift(Density, velocity * Speed, axis, PartCount);
        var outward = (localUp - velocity * localUp.Dot(velocity)).Normalized;

        Assert.Equal(AerodynamicsModel.NominalEntryAngleOfAttackDegrees, angleDeg, 8);
        Assert.True(lift.Dot(outward) > 0.0, "entry guidance must command lift away from the body");
    }

    [Fact]
    public void FlapAuthorityScalesWithDynamicPressureAndOpposesNoAxisMapping()
    {
        var command = new Vector3d(1.0, 0.0, 0.0); // semantic pitch -> local X
        var slow = AerodynamicsModel.ComputeFlapControlAngularAcceleration(
            0.02, Vector3d.Right * 300.0, Quaterniond.Identity, command, 52.0, 9.0, 5.0e7);
        var fast = AerodynamicsModel.ComputeFlapControlAngularAcceleration(
            0.02, Vector3d.Right * 600.0, Quaterniond.Identity, command, 52.0, 9.0, 5.0e7);

        Assert.True(slow.X > 0.0);
        Assert.True(System.Math.Abs(slow.Y) < 1e-12 && System.Math.Abs(slow.Z) < 1e-12);
        Assert.Equal(4.0, fast.Magnitude / slow.Magnitude, 8);
    }

    [Fact]
    public void FullFlapsCanTrimNominalStarshipEntryMoment()
    {
        const double speed = 160.0;
        const double q = 12_000.0;
        double density = 2.0 * q / (speed * speed);
        double alpha = AerodynamicsModel.NominalEntryAngleOfAttackDegrees * MathUtils.DEG_TO_RAD;
        var velocity = Vector3d.Right * speed;
        var axis = new Vector3d(System.Math.Cos(alpha), System.Math.Sin(alpha), 0.0);
        var attitude = AerodynamicsModel.ComputeBellyFirstOrientation(axis, velocity.Normalized);
        const double inertia = 5.6e7;

        var staticMoment = AerodynamicsModel.ComputeAttitudeAngularAcceleration(
            density, velocity, axis, Vector3d.Zero, 52.0, 9.0, inertia, 270.0);
        var flapMoment = AerodynamicsModel.ComputeFlapControlAngularAcceleration(
            density, velocity, attitude, new Vector3d(0.0, -1.0, 0.0), 52.0, 9.0, inertia);

        Assert.True(flapMoment.Magnitude >= staticMoment.Magnitude,
            $"full flaps must be able to trim entry: flap={flapMoment.Magnitude:F3}, static={staticMoment.Magnitude:F3}");
    }

    [Fact]
    public void AerodynamicMomentRotatesNoseTowardTheVelocity()
    {
        // Nose points +Y while the vehicle travels mostly +Y with a small +X error.
        // A CP behind the CoM must produce negative-Z acceleration, which rotates +Y
        // toward +X rather than away from it.
        var velocity = new Vector3d(150.0, 1_500.0, 0.0);
        var accel = AerodynamicsModel.ComputeAttitudeAngularAcceleration(
            density: 0.18,
            surfaceVelocity: velocity,
            longitudinalAxis: Vector3d.Up,
            angularVelocity: Vector3d.Zero,
            vehicleLength: 50.0,
            vehicleDiameter: 9.0,
            transverseMomentOfInertia: 2.0e8,
            temperature: 250.0);

        Assert.True(accel.Z < 0.0, $"Expected restoring -Z acceleration, got {accel}");
        Assert.True(accel.Magnitude > 0.0);
    }

    [Fact]
    public void AerodynamicRateDampingOpposesPitchButPreservesAxisRoll()
    {
        var accel = AerodynamicsModel.ComputeAttitudeAngularAcceleration(
            density: 0.20,
            surfaceVelocity: Vector3d.Up * 900.0,
            longitudinalAxis: Vector3d.Up,
            angularVelocity: new Vector3d(0.12, 0.08, 0.0),
            vehicleLength: 50.0,
            vehicleDiameter: 9.0,
            transverseMomentOfInertia: 2.0e8,
            temperature: 260.0);

        Assert.True(accel.X < 0.0, "pitch rate should be aerodynamically damped");
        Assert.True(System.Math.Abs(accel.Y) < 1e-12,
            "an axisymmetric hull should not receive synthetic roll damping");
    }

    [Fact]
    public void BellyFirstAttitudeAlignsBothLongAxisAndVisibleHeatShield()
    {
        var longAxis = new Vector3d(0.2, 0.8, 0.3).Normalized;
        var rawVelocity = new Vector3d(-0.9, 0.1, 0.4).Normalized;
        var velocity = (rawVelocity - longAxis * rawVelocity.Dot(longAxis)).Normalized;

        var attitude = AerodynamicsModel.ComputeBellyFirstOrientation(longAxis, velocity);
        var actualAxis = attitude.Rotate(Vector3d.Up).Normalized;
        var actualBelly = attitude.Rotate(-Vector3d.Right).Normalized;

        Assert.True(actualAxis.Dot(longAxis) > 0.999999999);
        Assert.True(actualBelly.Dot(velocity) > 0.999999999,
            $"Visible -X heat shield must face velocity: belly={actualBelly}, velocity={velocity}");
        Assert.Equal(1.0, ThermalModel.WindwardFactor(attitude.Inverse().Rotate(velocity)), 10);
    }
}
