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
        double predictedRange = predictedOffset.Magnitude;
        var liftDirection = predictedRange > 1e-6
            ? predictedOffset / predictedRange
            : horizontalOffset.Magnitude > 1e-6
                ? horizontalOffset.Normalized
                : Vector3d.Zero;

        return new Prediction(liftDirection, predictedRange, horizon);
    }
}
