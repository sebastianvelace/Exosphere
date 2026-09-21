namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;

/// <summary>
/// Predicts the cross-range direction an atmospheric entry vehicle should bias its
/// body lift toward when returning to a moving landing site.
/// </summary>
public static class EntryCorridorGuidance
{
    public readonly record struct Prediction(
        Vector3d LiftDirection,
        double PredictedCrossRangeM,
        double PredictedDownrangeM,
        double TimeToGroundS);

    /// <summary>
    /// Converts a footprint prediction into a bounded lift command. Cross-range error
    /// chooses the bank side; predicted downrange error chooses whether to extend or
    /// shorten the flight path. The latter is essential during a high-energy return:
    /// the vehicle can be pointed at the site while its future footprint is already
    /// beyond it.
    /// </summary>
    public static Vector3d SelectLiftDirection(
        Prediction prediction,
        Vector3d bodyDownLift,
        double corridorMeters = 20_000.0,
        double authorityMeters = 180_000.0,
        double? downrangeCorridorMeters = null,
        double? downrangeAuthorityMeters = null)
    {
        var downLift = bodyDownLift.Normalized;
        if (downLift.MagnitudeSquared < 1e-12)
            return prediction.LiftDirection.Normalized;

        double crossWeight = System.Math.Clamp(
            (prediction.PredictedCrossRangeM - corridorMeters) / authorityMeters,
            0.0,
            1.0);
        double downrangeCorridor = downrangeCorridorMeters ?? corridorMeters;
        double downrangeAuthority = downrangeAuthorityMeters ?? authorityMeters;
        double downrangeWeight = System.Math.Clamp(
            (System.Math.Abs(prediction.PredictedDownrangeM) - downrangeCorridor)
                / downrangeAuthority,
            0.0,
            1.0);

        // A positive projected downrange means the target remains ahead of the future
        // footprint, so lift toward the sky to extend the trajectory. A negative value
        // means the footprint is beyond the target, so retain down-lift and shorten it.
        var flightPathLift = prediction.PredictedDownrangeM >= 0.0
            ? -downLift
            : downLift;
        double baseWeight = System.Math.Max(crossWeight, downrangeWeight);
        var selected = downLift * (1.0 - baseWeight)
            + prediction.LiftDirection.Normalized * crossWeight
            + flightPathLift * downrangeWeight;
        return selected.MagnitudeSquared > 1e-12
            ? selected.Normalized
            : downLift;
    }

    /// <summary>
    /// Projects the current site error into the landing horizon. Position-only guidance
    /// can command the vehicle toward a corridor it is already crossing at hypersonic
    /// speed; the velocity term makes that same state command lift away from the target
    /// and bleed the overflight before the low-altitude flip.
    /// </summary>
    public static Prediction Predict(
        Vector3d targetOffsetWorld,
        Vector3d vehicleSurfaceVelocity,
        Vector3d targetSurfaceVelocity,
        Vector3d bodyUp,
        double altitudeM,
        double downwardSpeedMps,
        double gravityMps2,
        double minHorizonS = 20.0,
        double maxHorizonS = 180.0)
    {
        var up = bodyUp.Normalized;
        if (up.MagnitudeSquared < 1e-12)
            up = Vector3d.Up;

        var horizontalOffset = targetOffsetWorld - up * targetOffsetWorld.Dot(up);
        var relativeVelocity = targetSurfaceVelocity - vehicleSurfaceVelocity;
        var horizontalRelativeVelocity = relativeVelocity
            - up * relativeVelocity.Dot(up);

        double h = System.Math.Max(0.0, altitudeM);
        double down = System.Math.Max(0.0, downwardSpeedMps);
        double g = System.Math.Max(0.1, gravityMps2);
        double discriminant = down * down + 2.0 * g * h;
        double horizon = discriminant > 0.0
            ? (System.Math.Sqrt(discriminant) - down) / g
            : maxHorizonS;
        horizon = System.Math.Clamp(horizon, minHorizonS, maxHorizonS);

        var predictedOffset = horizontalOffset + horizontalRelativeVelocity * horizon;

        // Body lift can change flight-path energy and cross-range, but it must not chase the
        // entire ground-track error as though downrange were a lateral miss. Project the
        // predicted footprint onto the vehicle's current horizontal track and its normal. The
        // old total-range vector made a shallow entry bank toward a target that was simply
        // ahead of the vehicle, which is the source of an artificial side-to-side weave.
        var track = vehicleSurfaceVelocity - up * vehicleSurfaceVelocity.Dot(up);
        double trackMagnitude = track.Magnitude;
        if (trackMagnitude < 1e-6)
        {
            track = horizontalRelativeVelocity;
            trackMagnitude = track.Magnitude;
        }

        if (trackMagnitude < 1e-6)
        {
            double range = predictedOffset.Magnitude;
            var fallback = range > 1e-6
                ? predictedOffset / range
                : Vector3d.Zero;
            return new Prediction(fallback, range, 0.0, horizon);
        }

        track /= trackMagnitude;
        var crossTrack = up.Cross(track).Normalized;
        double signedCrossRange = predictedOffset.Dot(crossTrack);
        double predictedCrossRange = System.Math.Abs(signedCrossRange);
        double predictedDownrange = predictedOffset.Dot(track);
        var liftDirection = predictedCrossRange > 1e-6
            ? crossTrack * System.Math.Sign(signedCrossRange)
            : Vector3d.Zero;

        return new Prediction(
            liftDirection,
            predictedCrossRange,
            predictedDownrange,
            horizon);
    }
}
