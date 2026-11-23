namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;

/// <summary>Terminal glide state toward a single-burn ignition, for tracing.</summary>
public readonly record struct TowerCatchGlideTrace(double ToIgnitionPointM, double ToTowerM,
    double HorizontalSpeedMps, double TowardPointMps, GlidePlan Plan, double TimeToIgnitionS);

/// <summary>Lift reference and attitude axis the belly flop should fly this step.</summary>
public readonly record struct TowerCatchGlideSteering(Vector3d LiftReference, Vector3d AimAxis, TowerCatchGlideTrace? Trace);

/// <summary>
/// Belly-flop lift guidance for a return to the catch tower. It keeps lift up as the neutral
/// state that shapes the deceleration pulse, banks toward crossrange, and reverses only for a
/// predicted downrange overflight. With a single-burn definition it steers the terminal glide
/// to the ignition point and, just before ignition, along the line to the tower. The
/// aerodynamic integrator remains authoritative: this only chooses the attitude reference.
/// The game's EDL director and the glide-and-burn bench fly the same law.
/// </summary>
public sealed class TowerCatchGlideGuidance
{
    /// <summary>
    /// The reduced-order footprint predictor may lower periapsis with only a shallow down-lift
    /// component. Full lift reversal produced a measured 26-28 g plunge; zero vertical lift
    /// preserved the ~77 km periapsis and produced a skip-entry. cos(bank) = -0.15 is a
    /// bounded bank of about 98.6 degrees.
    /// </summary>
    public const double ReducedOrderMinimumVerticalLiftFraction = -0.15;

    private readonly EntryPredictionRefresh _entryPredictionRefresh = new();
    private GlidePlan _glidePlan;
    private double _nextGlidePlanEpoch;

    public ulong PredictionCount => _entryPredictionRefresh.SampleCount;

    /// <summary>Forgets the glide plan for a new descent.</summary>
    public void Reset()
    {
        _nextGlidePlanEpoch = 0.0;
        _glidePlan = default;
    }

    /// <param name="singleBurnDefinition">Single-burn catch estimate, or null for the powered-descent catch.</param>
    public TowerCatchGlideSteering Steer(Vessel vessel, CelestialBody body, double time, Vector3d catchTargetPosition,
        Vector3d up, Vector3d velDir, Vector3d surfVel, double altitude, double verticalSpeed, double gravity,
        double gForce, Vector3d priorLiftReference, TowerCatchBurnDefinition? singleBurnDefinition)
    {
        Vector3d lift;
        Vector3d aimAxis;
        TowerCatchGlideTrace? trace = null;
        // A catch return needs a bounded cross-range lift bias. Keep lift-up as the
        // neutral state that shapes the deceleration pulse, bank it toward crossrange,
        // and reverse it only for a predicted downrange overflight. The normal
        // aerodynamic integrator remains authoritative.
        Vector3d targetOffset = catchTargetPosition - vessel.Position;
        // The single burn lights short of the tower along the approach: the flip throws
        // the ship along its leading nose and the braking carries it on to the cradle.
        var leadDefinition = vessel.CatchTargetRails.IsDefined ? singleBurnDefinition : null;
        Vector3d ignitionPoint = catchTargetPosition;
        Vector3d glideHorizontalVelocity = Vector3d.Zero;
        if (leadDefinition != null)
        {
            var cradleUp = vessel.CatchTargetUpWorld.Normalized;
            var relative = vessel.Velocity - vessel.CatchTargetVelocityWorld;
            glideHorizontalVelocity = relative - cradleUp * relative.Dot(cradleUp);
            var lineOfSight = catchTargetPosition - vessel.Position;
            lineOfSight -= cradleUp * lineOfSight.Dot(cradleUp);
            double glideTowardTower = lineOfSight.MagnitudeSquared > 1e-6
                ? glideHorizontalVelocity.Dot(lineOfSight.Normalized) : 0.0;
            ignitionPoint = TowerCatchApproachPlanner.IgnitionPoint(catchTargetPosition, cradleUp,
                vessel.Position, leadDefinition.IgnitionLeadDistanceM(glideTowardTower));
            targetOffset = ignitionPoint - vessel.Position;
        }
        // Match the vehicle's surface-velocity frame. The cradle velocity is
        // inertial, while surfVel is relative to the body's local rotation at
        // the vehicle position. Subtract the cradle's local rotational velocity
        // before comparing the two; mixing these frames creates a false
        // cross-range command of several hundred metres per second.
        Vector3d targetSurfaceVelocity = vessel.CatchTargetVelocityWorld
            - body.Velocity
            - body.GetSurfaceVelocity(catchTargetPosition);
        Vector3d bodyLiftUp = up - velDir * up.Dot(velDir);
        if (bodyLiftUp.Magnitude > 1e-6)
        {
            double predictionTime = time;
            Vector3d relativePosition = vessel.Position - body.Position;
            if (_entryPredictionRefresh.NeedsRefresh(predictionTime, altitude,
                    vessel.Id, body.Id, relativePosition))
            {
                var refreshed = EntryCorridorGuidance.Predict(
                    targetOffset,
                    surfVel,
                    targetSurfaceVelocity,
                    up,
                    altitude,
                    -verticalSpeed,
                    gravity,
                    dynamics: EntryCorridorPropagation.ForVessel(vessel, body));
                _entryPredictionRefresh.Store(predictionTime, vessel.Id, body.Id,
                    relativePosition, refreshed);
            }
            var prediction = _entryPredictionRefresh.Prediction;
            if (prediction.LiftDirection.MagnitudeSquared > 1e-12
                || System.Math.Abs(prediction.PredictedDownrangeM) > 1e-6)
            {
                // Crossrange chooses the bank side while the predicted downrange
                // miss controls flight-path energy. Long-range entry guidance uses
                // a deliberately wide deadband to avoid banking at hypersonic speed;
                // once the Starbase return is inside the terminal corridor, that same
                // deadband would ignore a 1-2 km error and hand it to the powered burn.
                // Tighten the corridor progressively below 25 km, while keeping both
                // limits bounded so this remains a physical lift command rather than
                // a direct position correction.
                bool terminalCatchCorridor = altitude < 25_000.0;
                double downrangeCorridorMeters = terminalCatchCorridor
                    ? System.Math.Clamp(altitude * 0.01, 5.0, 250.0)
                    : 20_000.0;
                double downrangeAuthorityMeters = terminalCatchCorridor
                    ? System.Math.Clamp(altitude * 0.12, 1_500.0, 4_000.0)
                    : 180_000.0;
                // The single burn needs the crossrange closed as well before ignition: the
                // flip throws the ship along its nose, which follows the glide.
                bool tightCrossrange = terminalCatchCorridor && singleBurnDefinition != null;
                Vector3d guidedLift = EntryCorridorGuidance.SelectLiftDirection(
                    prediction,
                    bodyLiftUp,
                    corridorMeters: tightCrossrange ? downrangeCorridorMeters : 20_000.0,
                    authorityMeters: tightCrossrange ? downrangeAuthorityMeters : 180_000.0,
                    downrangeCorridorMeters: downrangeCorridorMeters,
                    downrangeAuthorityMeters: downrangeAuthorityMeters);
                // Low in the descent the single burn steers the glide itself: the fall is
                // nearly vertical, so the lift lies almost horizontal and turning the belly
                // flop points it at any heading. Close on the ignition point from here.
                if (leadDefinition != null && altitude < leadDefinition.TerminalGlideAltitudeM)
                {
                    var glideUp = vessel.CatchTargetUpWorld.Normalized;
                    double glideDescent = -(vessel.Velocity - vessel.CatchTargetVelocityWorld).Dot(glideUp);
                    double timeToIgnition = TowerCatchApproachPlanner.TimeToIgnitionS(
                        TowerCatchBurn.UprightPinClearanceM(vessel, time),
                        glideDescent, leadDefinition);
                    // Predict the manoeuvre to ignition and fly its present turn. Re-plan
                    // at 2 Hz: the belly flop turns at about 0.1 rad/s, far slower than that.
                    double now = time;
                    if (now >= _nextGlidePlanEpoch)
                    {
                        _nextGlidePlanEpoch = now + 0.5;
                        var offsetFromTower = vessel.Position - catchTargetPosition;
                        offsetFromTower -= glideUp * offsetFromTower.Dot(glideUp);
                        _glidePlan = TowerCatchGlidePredictor.Plan(offsetFromTower, glideHorizontalVelocity,
                            glideUp, timeToIgnition,
                            leadDefinition.IgnitionLeadDistanceM(glideHorizontalVelocity.Magnitude),
                            leadDefinition.GlidePlanningTurnRateRadPerS);
                    }
                    var glideCommand = TowerCatchGlidePredictor.TurnAcceleration(
                        glideHorizontalVelocity, glideUp, _glidePlan.TurnRateRadPerS);
                    double sideFraction = System.Math.Clamp(
                        glideCommand.Magnitude / leadDefinition.GlideLiftAccelerationMps2, 0.0, 1.0);
                    var sideLift = TowerCatchApproachPlanner.LiftDirection(velDir, glideCommand, guidedLift);
                    guidedLift = sideFraction > 1e-3
                        ? (guidedLift.Normalized * System.Math.Sqrt(1.0 - sideFraction * sideFraction)
                            + sideLift * sideFraction).Normalized
                        : guidedLift;
                    // Just before ignition, lift along the line to the tower: the nose leans
                    // toward the lift, and the flip throws the ship along the nose.
                    double alignment = TowerCatchApproachPlanner.FinalAlignmentWeight(
                        timeToIgnition, leadDefinition.GlideFinalAlignmentSeconds);
                    if (alignment > 0.0)
                        guidedLift = (guidedLift.Normalized * (1.0 - alignment)
                            + TowerCatchApproachPlanner.TowerLineLift(velDir, glideUp, vessel.Position,
                                catchTargetPosition, guidedLift) * alignment).Normalized;
                    {
                        var toPoint = ignitionPoint - vessel.Position;
                        var cradleUp = vessel.CatchTargetUpWorld.Normalized;
                        toPoint -= cradleUp * toPoint.Dot(cradleUp);
                        var toTower = catchTargetPosition - vessel.Position;
                        toTower -= cradleUp * toTower.Dot(cradleUp);
                        trace = new TowerCatchGlideTrace(toPoint.Magnitude, toTower.Magnitude,
                            glideHorizontalVelocity.Magnitude,
                            toPoint.MagnitudeSquared > 1 ? glideHorizontalVelocity.Dot(toPoint.Normalized) : 0.0,
                            _glidePlan, timeToIgnition);
                    }
                }
                // The footprint now carries drag, lift and energy at the nominal entry
                // angle of attack. The vertical floor remains a load guard: a true
                // overflight may bank toward the body, but a saturated down-lift
                // command becomes a 90-degree bank on the established side instead
                // of a full lift-up skip or a plunge. Sign noise inside the cross-range
                // deadband must not reverse that bank.
                Vector3d neutralBankSide = EntryCorridorGuidance.ComputeBankSide(
                    velDir, up, priorLiftReference);
                double verticalLiftFloor = EntryCorridorGuidance
                    .ComputeLoadReliefVerticalFloor(
                        gForce,
                        ReducedOrderMinimumVerticalLiftFraction);
                guidedLift = EntryCorridorGuidance.ConstrainVerticalLift(
                    guidedLift,
                    bodyLiftUp,
                    minimumVerticalFraction: verticalLiftFloor,
                    lateralFallback: neutralBankSide);
                guidedLift = EntryCorridorGuidance.BlendForAerodynamicAuthority(
                    guidedLift,
                    bodyLiftUp,
                    vessel.GetDynamicPressure(body));
                lift = guidedLift;
                aimAxis = AerodynamicsModel.ComputeEntryAxisForLift(
                    velDir, guidedLift);
            }
            else
            {
                lift = bodyLiftUp.Normalized;
                aimAxis = AerodynamicsModel.ComputeLiftUpEntryAxis(
                    up, velDir);
            }
        }
        else
        {
            lift = bodyLiftUp.Normalized;
            aimAxis = AerodynamicsModel.ComputeLiftUpEntryAxis(
                up, velDir);
        }
        return new TowerCatchGlideSteering(lift, aimAxis, trace);
    }
}
