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
        double TimeToGroundS,
        double FinalSpeedMps = 0.0,
        double SpecificEnergyChangeJPerKg = 0.0,
        bool UsedEnergyPropagation = false);

    /// <summary>
    /// Vacuum time-to-ground from altitude and downward speed. This is the no-drag bound
    /// used to prove the energy propagator is a different measurement. Entry fixtures and
    /// catch guidance must use <see cref="EntryCorridorPropagation"/> so the lead and the
    /// first footprint share drag, lift and energy.
    /// </summary>
    public static double EstimateTimeToGround(
        double altitudeM,
        double downwardSpeedMps,
        double gravityMps2,
        double minHorizonS = 20.0,
        double maxHorizonS = 180.0)
    {
        double minimum = System.Math.Max(0.0, minHorizonS);
        double maximum = System.Math.Max(minimum, maxHorizonS);
        double h = System.Math.Max(0.0, altitudeM);
        double down = System.Math.Max(0.0, downwardSpeedMps);
        double g = System.Math.Max(0.1, gravityMps2);
        double discriminant = down * down + 2.0 * g * h;
        double horizon = discriminant > 0.0
            ? (System.Math.Sqrt(discriminant) - down) / g
            : maximum;
        return System.Math.Clamp(horizon, minimum, maximum);
    }

    /// <summary>
    /// Converts a footprint prediction into a bounded lift command. Cross-range error
    /// chooses the bank side; predicted downrange error chooses whether to extend or
    /// shorten the flight path. The latter is essential during a high-energy return:
    /// the vehicle can be pointed at the site while its future footprint is already
    /// beyond it.
    /// </summary>
    public static Vector3d SelectLiftDirection(
        Prediction prediction,
        Vector3d bodyLiftUp,
        double corridorMeters = 20_000.0,
        double authorityMeters = 180_000.0,
        double? downrangeCorridorMeters = null,
        double? downrangeAuthorityMeters = null)
    {
        var liftUp = bodyLiftUp.Normalized;
        if (liftUp.MagnitudeSquared < 1e-12)
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

        // Lift-up is the nominal entry state: it shapes the deceleration pulse and avoids
        // driving a shallow orbital entry into the dense atmosphere. A positive projected
        // downrange means the target remains ahead of the future footprint, so keep lift-up
        // to extend the trajectory. Only a predicted overflight reverses lift downward.
        var flightPathLift = prediction.PredictedDownrangeM >= 0.0
            ? liftUp
            : -liftUp;
        double baseWeight = System.Math.Max(crossWeight, downrangeWeight);
        var selected = liftUp * (1.0 - baseWeight)
            + prediction.LiftDirection.Normalized * crossWeight
            + flightPathLift * downrangeWeight;
        return selected.MagnitudeSquared > 1e-12
            ? selected.Normalized
            : liftUp;
    }

    /// <summary>
    /// Enforces a lower bound on the lift component in the lift-up direction while
    /// preserving as much of the requested lateral bank as possible. The bound is a
    /// load and skip guard. It is not a substitute for the energy propagator: a real
    /// overflight may still request down-lift, and this clamp keeps that request inside
    /// the vertical fraction the caller supplies.
    /// </summary>
    public static Vector3d ConstrainVerticalLift(
        Vector3d requestedLift,
        Vector3d bodyLiftUp,
        double minimumVerticalFraction = 0.0,
        Vector3d? lateralFallback = null)
    {
        var liftUp = bodyLiftUp.Normalized;
        if (liftUp.MagnitudeSquared < 1e-12)
            return requestedLift.Normalized;

        var requested = requestedLift.Normalized;
        if (requested.MagnitudeSquared < 1e-12)
            return liftUp;

        double minimum = System.Math.Clamp(minimumVerticalFraction, -1.0, 1.0);
        double vertical = requested.Dot(liftUp);
        if (vertical >= minimum)
            return requested;

        var lateral = requested - liftUp * vertical;
        if (lateral.MagnitudeSquared < 1e-12)
        {
            var fallback = lateralFallback ?? Vector3d.Zero;
            lateral = fallback - liftUp * fallback.Dot(liftUp);
            if (lateral.MagnitudeSquared < 1e-12)
                return liftUp;
        }

        double lateralFraction = System.Math.Sqrt(
            System.Math.Max(0.0, 1.0 - minimum * minimum));
        return (liftUp * minimum + lateral.Normalized * lateralFraction).Normalized;
    }

    /// <summary>
    /// Builds a lateral bank side perpendicular to the current airflow and vertical.
    /// A previous lift projected only off vertical can retain an axial component as
    /// the flow turns; using it as bank authority silently loses lift-side steering.
    /// </summary>
    public static Vector3d ComputeBankSide(Vector3d flow, Vector3d up, Vector3d previousLift)
    {
        var side = up.Cross(flow).Normalized;
        return previousLift.Dot(side) < -1e-5 ? -side : side;
    }

    /// <summary>
    /// Bounds vertical lift through descent. A climb-rate feedback command plus the
    /// local ballistic radial acceleration sets the bank ceiling; forces and state
    /// remain owned by the integrator. Full lift-up remains available in a plunge.
    /// </summary>
    public static Vector3d LimitLiftForDescent(
        Vector3d requestedLift, Vector3d liftUp, Vector3d lateralFallback,
        double verticalSpeedMps, double airspeedMps,
        double ballisticVerticalAccelerationMps2, double liftUpAccelerationMps2)
    {
        if (!double.IsFinite(verticalSpeedMps) || !double.IsFinite(airspeedMps)
            || !double.IsFinite(ballisticVerticalAccelerationMps2)
            || !double.IsFinite(liftUpAccelerationMps2)
            || liftUpAccelerationMps2 < 0.05 || airspeedMps < 1_000.0)
            return requestedLift.Normalized;

        // A shallow descending corridor, with enough look-ahead for the physical
        // roll actuator. At terminal speed the ordinary belly-flop/flip owns guidance.
        double targetVerticalSpeed = -System.Math.Max(100.0, airspeedMps * 0.035);
        double targetAcceleration = (targetVerticalSpeed - verticalSpeedMps) / 20.0;
        double ceiling = System.Math.Clamp(
            (targetAcceleration - ballisticVerticalAccelerationMps2)
                / liftUpAccelerationMps2, -0.15, 1.0);
        // Footprint steering cannot turn a steep entry into a down-lift plunge.
        // Keep a 150 m/s descent deadband for its range command below the ceiling.
        double floor = System.Math.Clamp(
            ((targetVerticalSpeed - 150.0 - verticalSpeedMps) / 20.0
                - ballisticVerticalAccelerationMps2) / liftUpAccelerationMps2, -0.15, ceiling);
        var bounded = ConstrainVerticalLift(requestedLift, liftUp,
            minimumVerticalFraction: floor, lateralFallback: lateralFallback);
        return ConstrainVerticalLift(bounded, -liftUp,
            minimumVerticalFraction: -ceiling, lateralFallback: lateralFallback);
    }

    /// <summary>
    /// Blends a bank command in only after aerodynamic control authority exists. Flaps
    /// cannot track a large roll reference in near-vacuum; integrating saturated commands
    /// there stores angular momentum and produces an artificial oscillation before entry.
    /// </summary>
    public static Vector3d BlendForAerodynamicAuthority(
        Vector3d requestedLift,
        Vector3d bodyLiftUp,
        double dynamicPressurePa,
        double onsetDynamicPressurePa = 25.0,
        double fullAuthorityDynamicPressurePa = 250.0)
    {
        var liftUp = bodyLiftUp.Normalized;
        if (liftUp.MagnitudeSquared < 1e-12)
            return requestedLift.Normalized;

        var requested = requestedLift.Normalized;
        if (requested.MagnitudeSquared < 1e-12)
            return liftUp;

        double onset = System.Math.Max(0.0, onsetDynamicPressurePa);
        double full = System.Math.Max(onset + 1e-9, fullAuthorityDynamicPressurePa);
        double x = System.Math.Clamp((dynamicPressurePa - onset) / (full - onset), 0.0, 1.0);
        double smooth = x * x * (3.0 - 2.0 * x);
        var blended = liftUp * (1.0 - smooth) + requested * smooth;
        return blended.MagnitudeSquared > 1e-12 ? blended.Normalized : requested;
    }

    /// <summary>
    /// Raises the allowable vertical-lift floor as proper acceleration approaches the
    /// severe-entry band. This is a guidance constraint, not a force clamp: the vehicle
    /// must bank lift upward early enough to flatten the trajectory before peak load.
    /// </summary>
    public static double ComputeLoadReliefVerticalFloor(
        double properLoadG,
        double nominalFloor = -0.15,
        double reliefOnsetG = EntryLoadTracker.HighG,
        double fullLiftUpG = 7.0)
    {
        double nominal = System.Math.Clamp(nominalFloor, -1.0, 1.0);
        double onset = System.Math.Max(0.0, reliefOnsetG);
        double full = System.Math.Max(onset + 1e-9, fullLiftUpG);
        double x = System.Math.Clamp((properLoadG - onset) / (full - onset), 0.0, 1.0);
        double smooth = x * x * (3.0 - 2.0 * x);
        return nominal + (1.0 - nominal) * smooth;
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
        double maxHorizonS = 180.0,
        EntryCorridorPropagation.Dynamics? dynamics = null)
    {
        var up = bodyUp.Normalized;
        if (up.MagnitudeSquared < 1e-12)
            up = Vector3d.Up;

        var horizontalOffset = targetOffsetWorld - up * targetOffsetWorld.Dot(up);
        var horizontalVehicleVelocity = vehicleSurfaceVelocity
            - up * vehicleSurfaceVelocity.Dot(up);
        var horizontalTargetVelocity = targetSurfaceVelocity
            - up * targetSurfaceVelocity.Dot(up);
        var horizontalRelativeVelocity = horizontalTargetVelocity - horizontalVehicleVelocity;

        double horizon = EstimateTimeToGround(
            altitudeM,
            downwardSpeedMps,
            gravityMps2,
            minHorizonS,
            maxHorizonS);
        var predictedOffset = horizontalOffset + horizontalRelativeVelocity * horizon;
        double finalSpeed = 0.0;
        double energyChange = 0.0;
        bool usedEnergy = false;

        var trackPreview = horizontalVehicleVelocity;
        double trackPreviewMagnitude = trackPreview.Magnitude;
        if (dynamics is EntryCorridorPropagation.Dynamics entry
            && trackPreviewMagnitude > 1e-6)
        {
            double airspeed = System.Math.Sqrt(
                trackPreviewMagnitude * trackPreviewMagnitude
                + downwardSpeedMps * downwardSpeedMps);
            double flightPath = System.Math.Atan2(
                -downwardSpeedMps,
                trackPreviewMagnitude);
            EntryCorridorPropagation.Result propagated = EntryCorridorPropagation.Propagate(
                altitudeM,
                airspeed,
                flightPath,
                entry);
            var trackDirection = trackPreview / trackPreviewMagnitude;
            var crossDirection = up.Cross(trackDirection).Normalized;
            double radius = System.Math.Max(1.0, entry.BodyRadiusM);
            // The target offset is a chord, R sin θ. The propagator advances the
            // central angle. Comparing those lengths directly makes a vehicle that is
            // still short of the target look like an overflight.
            double targetAlong = System.Math.Asin(System.Math.Clamp(
                horizontalOffset.Dot(trackDirection) / radius, -1.0, 1.0));
            double targetCross = System.Math.Asin(System.Math.Clamp(
                horizontalOffset.Dot(crossDirection) / radius, -1.0, 1.0));
            double alongRate = horizontalTargetVelocity.Dot(trackDirection) / radius;
            double crossRate = horizontalTargetVelocity.Dot(crossDirection) / radius;
            double alongError = targetAlong
                + alongRate * propagated.DurationS
                - propagated.CentralAngleRad;
            double crossError = targetCross
                + crossRate * propagated.DurationS
                - propagated.CrossAngleRad;
            double crossRange = System.Math.Abs(crossError * radius);
            var bankDirection = crossRange > 1e-6
                ? crossDirection * System.Math.Sign(crossError)
                : Vector3d.Zero;
            return new Prediction(
                bankDirection,
                crossRange,
                alongError * radius,
                propagated.DurationS,
                propagated.FinalSpeedMps,
                propagated.FinalSpecificEnergyJPerKg - propagated.InitialSpecificEnergyJPerKg,
                true);
        }

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
            return new Prediction(
                fallback, range, 0.0, horizon, finalSpeed, energyChange, usedEnergy);
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
            horizon,
            finalSpeed,
            energyChange,
            usedEnergy);
    }
}
