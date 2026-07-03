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
}
