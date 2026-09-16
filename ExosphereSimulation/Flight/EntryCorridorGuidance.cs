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
