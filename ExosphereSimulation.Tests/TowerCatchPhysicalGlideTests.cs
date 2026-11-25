namespace ExosphereSimulation.Tests;

using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Xunit;

/// <summary>
/// The physical terminal glide of the single-burn Flight 15 catch, flown from the chapter's
/// 70 km entry (<see cref="Flight15CatchBench.FromChapterEntry"/>) on the physical descent law.
/// The terminal attitude loop and the drift guidance were designed on the plant identified on
/// this bench (docs/audits/flight15_capture_and_gpu_2026-11-19.md, 2026-11-24).
/// </summary>
public sealed class TowerCatchPhysicalGlideTests
{
    private const double SeedUpstreamM = 143_140.0;
    private const double SeedNorthM = -50.0;

    [Fact]
    public void TheShapedReferenceReachesItsGoalWithinItsLimits()
    {
        double value = 88.5, rate = 0.0, peakRate = 0.0;
        const double dt = 1.0 / 60.0;
        for (int i = 0; i < 600; i++)
        {
            double acceleration = TowerCatchPhysicalGlide.ShapeStep(93.0, ref value, ref rate, 1.5, 3.0, dt);
            Assert.InRange(System.Math.Abs(acceleration), 0.0, 1.5 + 1e-9);
            peakRate = System.Math.Max(peakRate, System.Math.Abs(rate));
            Assert.True(value <= 93.0 + 1e-6, $"overshoot to {value}");
        }
        Assert.Equal(93.0, value, 6);
        Assert.InRange(peakRate, 0.0, 3.0 + 1e-9);
    }

    [Theory]
    [InlineData(1.0 / 60.0)]
    [InlineData(1.0 / 50.0)]
    public void TheTerminalLoopTracksAnAngleStepWithTheRollHeld(double dt)
    {
        var bench = GlideBench(out var definition);
        var ship = bench.Ship;
        var earth = bench.Universe.Bodies.First();
        // Hold the heading and 90° through the hand-over to the terminal loop, then step.
        while (earth.GetAltitude(ship.Position) > 12_500.0) bench.Universe.Tick(dt);
        var up = earth.GetGeodeticUp(ship.Position);
        var nose = ship.Orientation.Rotate(Vector3d.Up);
        bench.PhysicalGlide.IdentificationNoseHeading = (nose - up * nose.Dot(up)).Normalized;
        bench.PhysicalGlide.IdentificationAngleOfAttackDegrees = 90.0;
        while (earth.GetAltitude(ship.Position) > 10_500.0) bench.Universe.Tick(dt);
        double[] schedule = [90.0, 94.0, 90.0, 86.0];
        const double segmentS = 7.0;
        double start = bench.Universe.CurrentTime, maxRoll = 0.0;
        var settle = new double[schedule.Length];
        int previous = -1;
        double lastOutside = 0.0;
        while (bench.Universe.CurrentTime - start < segmentS * schedule.Length)
        {
            double t = bench.Universe.CurrentTime - start;
            int k = (int)(t / segmentS);
            if (k != previous) { if (previous >= 0) settle[previous] = lastOutside - previous * segmentS; previous = k; lastOutside = t; }
            bench.PhysicalGlide.IdentificationAngleOfAttackDegrees = schedule[k];
            bench.Universe.Tick(dt);
            up = earth.GetGeodeticUp(ship.Position);
            var axis = ship.Orientation.Rotate(Vector3d.Up);
            double angle = System.Math.Acos(System.Math.Clamp(axis.Dot(ship.GetSurfaceVelocity(earth).Normalized), -1.0, 1.0))
                * MathUtils.RAD_TO_DEG;
            if (System.Math.Abs(angle - schedule[k]) > 1.0) lastOutside = t;
            if (k > 0) maxRoll = System.Math.Max(maxRoll,
                System.Math.Abs(TowerCatchBurn.BellyRollFromVerticalRad(ship.Orientation, up)) * MathUtils.RAD_TO_DEG);
        }
        settle[^1] = lastOutside - (schedule.Length - 1) * segmentS;
        // Steps of 4° settle inside 1° in under 3 s, with the roll held under 5°.
        for (int k = 1; k < schedule.Length; k++)
            Assert.True(settle[k] < 3.0, $"step to {schedule[k]}° settled in {settle[k]:F1} s");
        Assert.True(maxRoll < 5.0, $"roll reached {maxRoll:F1}°");
        Assert.NotNull(definition);
    }

    [Theory]
    [InlineData(600.0, 300.0, 1.0 / 60.0)]
    [InlineData(-600.0, -300.0, 1.0 / 60.0)]
    [InlineData(-300.0, 300.0, 1.0 / 50.0)]
    [InlineData(600.0, -300.0, 1.0 / 50.0)]
    public void TheDriftGuidanceReachesTheIgnitionPointFromADispersedState(double alongM, double acrossM, double dt)
    {
        var bench = GlideBench(out var definition);
        var ship = bench.Ship;
        var earth = bench.Universe.Bodies.First();
        while (earth.GetAltitude(ship.Position) > 12_500.0) bench.Universe.Tick(dt);
        var cradle = ship.GetCatchTargetPositionAt(bench.Universe.CurrentTime).Normalized;
        var east = earth.RotationAxis.Cross(cradle).Normalized;
        ship.Position += east * alongM + cradle.Cross(east) * acrossM;
        while (earth.GetAltitude(ship.Position) > definition.GlideAimAltitudeM && !ship.IsDestroyed)
            bench.Universe.Tick(dt);
        var up = earth.GetGeodeticUp(ship.Position);
        var toCradle = ship.GetCatchTargetPositionAt(bench.Universe.CurrentTime) - ship.Position;
        toCradle -= up * toCradle.Dot(up);
        var axis = bench.PhysicalGlide.ApproachAxis;
        double miss = (toCradle - axis * definition.IgnitionLeadM).Magnitude;
        var nose = ship.Orientation.Rotate(Vector3d.Up);
        nose = (nose - up * nose.Dot(up)).Normalized;
        Assert.False(ship.IsDestroyed);
        Assert.True(miss < 50.0, $"missed the ignition point by {miss:F0} m");
        Assert.True(nose.Dot(axis) > 0.9, $"nose {System.Math.Acos(nose.Dot(axis)) * MathUtils.RAD_TO_DEG:F0}° off the approach axis");
    }

    private static Flight15CatchBench GlideBench(out TowerCatchBurnDefinition definition)
    {
        definition = TowerCatchBurnTests.Bench.Definition();
        definition.GlideTerminalHeadingSpeedMps = 160.0;
        definition.GlideTerminalAngleOfAttackDegrees = 85.0;
        definition.IgnitionLeadM = 150.0;
        definition.GlideNoseConeDegrees = 30.0;
        definition.GlideAimAltitudeM = 2_000.0;
        var bench = Flight15CatchBench.FromChapterEntry(SeedUpstreamM, SeedNorthM, definition);
        bench.PhysicalDescent = Flight15CatchBench.Flight15Descent();
        bench.GlideOnly = true;
        return bench;
    }
}
