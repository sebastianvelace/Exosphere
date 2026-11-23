namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;

/// <summary>Turn the glide should fly now, and where the chosen manoeuvre ends at ignition.</summary>
public readonly record struct GlidePlan(
    double TurnRateRadPerS, double PredictedRangeAtIgnitionM, double PredictedHeadingErrorRad, double Cost);

/// <summary>
/// Predictive steering of the belly-flop glide toward a single-burn ignition. The belly flop
/// glides at a nearly fixed speed and turns slowly (about 0.1 rad/s), so a feedback law on the
/// current state swings around its target. This planner instead propagates candidate
/// manoeuvres to the moment of ignition: turn away by a chosen angle at the full rate to spend
/// range when early, turn back at a chosen rate until the glide points at the tower, then fly
/// straight in (an S-turn, as a glider pilot loses height on final). It keeps the
/// candidate that ends the requested lead short of the tower flying head-on at it, and flies
/// only that candidate's present turn, re-planning as the state evolves.
/// </summary>
public static class TowerCatchGlidePredictor
{
    private const double StepS = 0.5;
    private const double AlignedRad = 2.0 * System.Math.PI / 180.0;
    private static readonly double[] RateFractions = [0.15, 0.3, 0.45, 0.6, 0.75, 0.9, 1.0];
    private static readonly double[] AwayAnglesDeg = [0.0, 20.0, 40.0, 60.0, 90.0, 120.0, 150.0, 180.0];

    /// <param name="offsetFromTower">Horizontal ship position relative to the tower (m).</param>
    /// <param name="horizontalVelocity">Horizontal glide velocity relative to the tower (m/s).</param>
    /// <param name="upWorld">Local vertical; turns are about it.</param>
    public static GlidePlan Plan(Vector3d offsetFromTower, Vector3d horizontalVelocity, Vector3d upWorld,
        double timeToIgnitionS, double leadM, double maximumTurnRateRadPerS)
    {
        var up = upWorld.Normalized;
        double speed = horizontalVelocity.Magnitude;
        if (speed < 1.0 || !double.IsFinite(timeToIgnitionS) || timeToIgnitionS <= 0.0)
            return new GlidePlan(0.0, offsetFromTower.Magnitude, 0.0, double.PositiveInfinity);

        var heading = horizontalVelocity / speed;
        var (straightRange, straightError) = Propagate(offsetFromTower, heading, speed, up,
            timeToIgnitionS, 0.0, 0.0, 0.0);
        GlidePlan best = new(0.0, straightRange, straightError, Cost(straightRange, straightError, leadM));
        foreach (int side in new[] { 1, -1 })
        foreach (double awayDeg in AwayAnglesDeg)
        foreach (double fraction in RateFractions)
        {
            double awayRate = side * maximumTurnRateRadPerS;
            double awayTime = awayDeg * System.Math.PI / 180.0 / maximumTurnRateRadPerS;
            double backRate = fraction * maximumTurnRateRadPerS;
            var (range, error) = Propagate(offsetFromTower, heading, speed, up, timeToIgnitionS,
                awayRate, awayTime, backRate);
            double cost = Cost(range, error, leadM);
            // The turn flown now is the away turn, or the turn back when there is none.
            double now = awayTime > 0.0 ? awayRate : BackTurnSign(offsetFromTower, heading, up) * backRate;
            if (cost < best.Cost) best = new GlidePlan(now, range, error, cost);
        }
        return best;
    }

    // Range error at ignition plus the sideways miss a heading error would carry.
    private static double Cost(double range, double headingError, double leadM) =>
        System.Math.Abs(range - leadM) + range * System.Math.Abs(headingError);

    private static double BackTurnSign(Vector3d position, Vector3d heading, Vector3d up) =>
        HeadingError(position, heading, up) >= 0.0 ? 1.0 : -1.0;

    /// <summary>Horizontal acceleration that turns a glide of this velocity at <paramref name="turnRateRadPerS"/>.</summary>
    public static Vector3d TurnAcceleration(Vector3d horizontalVelocity, Vector3d upWorld, double turnRateRadPerS) =>
        upWorld.Normalized.Cross(horizontalVelocity) * turnRateRadPerS;

    private static (double Range, double HeadingError) Propagate(Vector3d position, Vector3d heading,
        double speed, Vector3d up, double duration, double awayRate, double awayTime, double backRate)
    {
        bool aligned = backRate == 0.0 && awayTime == 0.0;
        for (double t = 0.0; t < duration; t += StepS)
        {
            double dt = System.Math.Min(StepS, duration - t);
            if (t < awayTime)
                heading = Rotate(heading, up, awayRate * dt);
            else if (!aligned)
            {
                double error = HeadingError(position, heading, up);
                if (System.Math.Abs(error) <= AlignedRad) aligned = true;
                else
                {
                    double step = System.Math.Min(System.Math.Abs(error), backRate * dt);
                    heading = Rotate(heading, up, System.Math.Sign(error) * step);
                }
            }
            position += heading * (speed * dt);
        }
        return (position.Magnitude, HeadingError(position, heading, up));
    }

    /// <summary>Signed angle (rad) from the glide heading to the direction of the tower.</summary>
    private static double HeadingError(Vector3d position, Vector3d heading, Vector3d up)
    {
        var toTower = -position;
        if (toTower.MagnitudeSquared < 1e-6) return 0.0;
        toTower = toTower.Normalized;
        return System.Math.Atan2(heading.Cross(toTower).Dot(up), heading.Dot(toTower));
    }

    private static Vector3d Rotate(Vector3d vector, Vector3d axis, double angle) =>
        Quaterniond.FromAxisAngle(axis, angle).Rotate(vector);
}
