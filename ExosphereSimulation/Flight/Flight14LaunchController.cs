namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

public enum Flight14LaunchPhase { Ignition, BoosterAscent, HotStage, ShipAscent, SuborbitalCoast, Insertion, OrbitReady, Blocked }

/// <summary>
/// Launch-to-insertion diagnostic. Controls act through production forces and staging.
/// No pose/velocity reseeds, telemetry tracking forces or automatic tower recovery.
/// This engineering estimate is not the complete Flight 14 mission.
/// </summary>
public sealed class Flight14LaunchController : IPhysicsStepController
{
    private readonly Vessel _ship;
    private readonly CelestialBody _body;
    private readonly Flight14LaunchGuidanceDefinition _guidance;
    private readonly Part _booster;
    private readonly Part _engines;
    private readonly Part _tank;
    private double _ignitionSeconds;
    private bool _boosterFault, _vacuumFault;
    private Flight14OrbitalBurn? _insertion;
    private Vector3d _poweredAim;
    private bool _initialPoweredTurnComplete;
    private readonly double _targetRadius, _targetRadialSpeed, _targetMomentum, _targetAxis;

    public Flight14LaunchPhase Phase { get; private set; } = Flight14LaunchPhase.Ignition;
    public string? BlockReason { get; private set; }
    public double LiftoffEpoch { get; private set; } = double.NaN;
    public double StagingElapsedSeconds { get; private set; } = double.NaN;
    public double OrbitElapsedSeconds { get; private set; } = double.NaN;
    public double CutoffElapsedSeconds { get; private set; } = double.NaN;
    public double InsertionElapsedSeconds { get; private set; } = double.NaN;
    public double CutoffApoapsisAltitudeM { get; private set; } = double.NaN;
    public double CutoffPeriapsisAltitudeM { get; private set; } = double.NaN;
    public double CoastMinimumAltitudeM { get; private set; } = double.PositiveInfinity;
    public Vessel? DetachedBooster { get; private set; }

    public Flight14LaunchController(Vessel stack, CelestialBody body, Flight14LaunchGuidanceDefinition guidance)
    {
        guidance.Validate();
        _ship = stack;
        _body = body;
        _guidance = guidance;
        _booster = stack.Parts.Parts.Single(p => p.Definition.HasVehicleRole("booster"));
        _engines = stack.Parts.Parts.Single(p => p.Definition.HasVehicleRole("ship_engines"));
        _tank = stack.Parts.Parts.Single(p => p.Definition.HasVehicleRole("tank"));
        if (!stack.IsGroundHeld || body.Atmosphere == null
            || guidance.TargetSuborbitalPeriapsisAltitudeM >= body.Atmosphere.MaxAltitude
            || guidance.TargetInsertionPeriapsisAltitudeM <= body.Atmosphere.MaxAltitude)
            throw new ArgumentException("Flight 14 launch must start held on an atmospheric body with a suborbital cutoff target.");
        if (_booster.EngineStates.Count != 33 || _engines.EngineStates.Count != 6
            || _engines.EngineStates.Take(3).Any(e => e.EngineModelId != guidance.SeaLevelEngineModelId)
            || _engines.EngineStates[3].EngineModelId == guidance.SeaLevelEngineModelId)
            throw new ArgumentException("Flight 14 diagnostic requires the declared 33/6 engine topology.");
        double ra = body.Radius+guidance.TargetApoapsisAltitudeM;
        double rp = body.Radius+guidance.TargetSuborbitalPeriapsisAltitudeM;
        _targetAxis = (ra+rp)*0.5;
        _targetMomentum = System.Math.Sqrt(2*body.GM*ra*rp/(ra+rp));
        double eccentricity = (ra-rp)/(ra+rp);
        double meanMotion = System.Math.Sqrt(body.GM/(_targetAxis*_targetAxis*_targetAxis));
        double mean = System.Math.PI-meanMotion*guidance.NominalCoastSeconds;
        if (mean <= 0) throw new ArgumentException("Nominal coast exceeds the ascending half-orbit.");
        double eccentric = mean;
        for (int i = 0; i < 6; i++)
            eccentric -= (eccentric-eccentricity*System.Math.Sin(eccentric)-mean)/(1-eccentricity*System.Math.Cos(eccentric));
        _targetRadius = _targetAxis*(1-eccentricity*System.Math.Cos(eccentric));
        _targetRadialSpeed = System.Math.Sqrt(body.GM*_targetAxis)/_targetRadius*eccentricity*System.Math.Sin(eccentric);
    }

    public bool RequiresFixedCadence(Universe universe) => Phase is not (Flight14LaunchPhase.OrbitReady or Flight14LaunchPhase.Blocked);

    public void BeforePhysicsStep(Universe universe, double controlIntervalSeconds)
    {
        ArgumentNullException.ThrowIfNull(universe);
        if (!double.IsFinite(controlIntervalSeconds) || controlIntervalSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(controlIntervalSeconds));
        if (Phase is Flight14LaunchPhase.OrbitReady or Flight14LaunchPhase.Blocked) return;
        if (!ReferenceEquals(universe.ActiveVessel, _ship)) { Block("active-vessel-changed"); return; }
        if (_ship.IsDestroyed || _ship.StructuralControlLost) { Block("vehicle-damaged"); return; }
        var rel = _ship.Position-_body.Position;
        var velocity = _ship.Velocity-_body.Velocity;
        double radius = rel.Magnitude;
        var up = rel.Normalized;
        double radialSpeed = velocity.Dot(up);
        var tangentVelocity = velocity-up*radialSpeed;
        var east = _body.GetEastDirection(_ship.Position);
        _ship.SASEnabled = false;
        if (Phase == Flight14LaunchPhase.Ignition)
        {
            _ignitionSeconds += controlIntervalSeconds;
            if (_ignitionSeconds > _guidance.MaximumIgnitionSeconds) { Block("ignition-envelope"); return; }
            _ship.Throttle = System.Math.Clamp(_ignitionSeconds/_guidance.IgnitionRampSeconds, 0, 1);
            Aim(_body.GetGeodeticUp(_ship.Position), 2.4, 1.1);
            if (HoldDownReleasePolicy.CanRelease(_ship.GetThrustToWeightRatio(_body), _ship.Throttle))
            {
                _ship.ReleaseGroundHold();
                LiftoffEpoch = universe.CurrentTime;
                Phase = Flight14LaunchPhase.BoosterAscent;
            }
            return;
        }
        double elapsed = universe.CurrentTime-LiftoffEpoch;
        if (elapsed > _guidance.MaximumMissionSeconds) { Block("launch-duration-envelope"); return; }
        if (Phase is Flight14LaunchPhase.BoosterAscent or Flight14LaunchPhase.HotStage)
        {
            if (!_boosterFault && elapsed >= _guidance.BoosterFaultSeconds)
            {
                // Synthetic failed outer mount, not a claimed Flight 14 fault identity.
                _booster.FailEngine(_booster.EngineStates[^1].InstanceId, "F14_ESTIMATED_ASCENT_OUT");
                _boosterFault = true;
            }
            var command = _guidance.BoosterCommand(elapsed);
            Aim(AttitudeGuidance.AimFromElevation(_body.GetGeodeticUp(_ship.Position), east,
                command.ElevationDeg*MathUtils.DEG_TO_RAD), 2.4, 1.1);
            _ship.Throttle = command.Throttle;
            if (Phase == Flight14LaunchPhase.BoosterAscent && elapsed >= _guidance.NominalMecoSeconds)
            {
                if (_ship.GetAltitude(_body) < _guidance.MinimumStagingAltitudeM) { Block("meco-altitude-envelope"); return; }
                _booster.SelectEngineCount(3);
                _ship.BeginHotStageOverlap(_guidance.HotStageSeconds);
                Phase = Flight14LaunchPhase.HotStage;
                _ship.Throttle = 1;
            }
            if (Phase == Flight14LaunchPhase.HotStage && _ship.HotStageOverlapCompletedPending)
            {
                DetachedBooster = _ship.Stage();
                if (DetachedBooster == null) { Block("hot-stage-separation-failed"); return; }
                DetachedBooster.Throttle = 0;
                universe.AddVessel(DetachedBooster);
                _poweredAim = _ship.Orientation.Rotate(Vector3d.Up).Normalized;
                StagingElapsedSeconds = elapsed;
                Phase = Flight14LaunchPhase.ShipAscent;
            }
            return;
        }
        var orbit = OrbitalElements.FromStateVector(rel, velocity, _body.GM, _body.Id, universe.CurrentTime);
        if (orbit.IsHyperbolic || !double.IsFinite(orbit.Periapsis)) { Block("invalid-launch-conic"); return; }
        double apo = orbit.Apoapsis-_body.Radius, pe = orbit.Periapsis-_body.Radius;
        if (Phase == Flight14LaunchPhase.ShipAscent)
        {
            if (!_vacuumFault && elapsed >= _guidance.VacuumFaultSeconds)
            {
                _engines.FailEngine(_engines.EngineStates[3].InstanceId, "F14_ESTIMATED_VAC_OUT");
                _vacuumFault = true;
            }
            if (_tank.LiquidFuel+_tank.Oxidizer < _guidance.MinimumShipReserveKg)
            { Block("ship-reserve-exhausted-before-cutoff"); return; }
            if (elapsed > _guidance.MaximumAscentSeconds) { Block("ascent-duration-envelope"); return; }
            double toApo = AscentInsertionPolicy.TimeToApoapsisSeconds(orbit, universe.CurrentTime, _body.GM);
            if (pe >= _guidance.TargetSuborbitalPeriapsisAltitudeM && pe < _body.Atmosphere!.MaxAltitude
                && apo >= _guidance.MinimumInsertionAltitudeM && apo <= _guidance.TargetApoapsisAltitudeM+_guidance.CutoffApoapsisToleranceM
                && radialSpeed > 0 && toApo >= _guidance.MinimumCoastSeconds)
            {
                CutoffElapsedSeconds = elapsed;
                CutoffApoapsisAltitudeM = apo;
                CutoffPeriapsisAltitudeM = pe;
                _ship.Throttle = 0;
                Phase = Flight14LaunchPhase.SuborbitalCoast;
                return;
            }
            // A nominal cutoff design state is derived from a suborbital conic and its
            // coast duration. Cubic radial guidance commands acceleration toward it;
            // actual cutoff still requires the integrated conic above, never the clock.
            double remaining = System.Math.Max(30, _guidance.NominalShipCutoffSeconds-elapsed);
            double acceleration = 6*(_targetRadius-radius)/(remaining*remaining)
                -(4*radialSpeed+2*_targetRadialSpeed)/remaining;
            // As horizontal energy approaches insertion, trim radial speed against the
            // desired suborbital conic. Otherwise a fast late burn can cross the target
            // periapsis with too much radial energy and an excessively high apoapsis.
            double targetTangentialSpeed = _targetMomentum/radius;
            double targetRadialSquared = 2*_body.GM/radius-_body.GM/_targetAxis
                -targetTangentialSpeed*targetTangentialSpeed;
            double energyBlend = System.Math.Clamp((tangentVelocity.Magnitude/targetTangentialSpeed
                -_guidance.TerminalEnergyFraction)/_guidance.TerminalBlendWidth, 0, 1);
            energyBlend = energyBlend*energyBlend*(3-2*energyBlend);
            double shipThrottle = _guidance.ShipThrottle
                +energyBlend*(_guidance.TerminalShipThrottle-_guidance.ShipThrottle);
            if (targetRadialSquared > 0)
            {
                // A hard switch here can demand a sudden pitch reversal. Blend the
                // acceleration references continuously as horizontal energy builds.

                double terminalAcceleration = _guidance.TerminalRadialGainPerSecond
                    *(System.Math.Sqrt(targetRadialSquared)-radialSpeed);
                acceleration += energyBlend*(terminalAcceleration-acceleration);
            }
            double gravity = _body.GM/(radius*radius);
            double centrifugal = tangentVelocity.MagnitudeSquared/radius;
            double thrustAcceleration = _ship.GetMaximumThrust(_body)*shipThrottle/_ship.TotalMass;
            double sine = System.Math.Clamp((gravity-centrifugal+acceleration)/System.Math.Max(0.1, thrustAcceleration), -0.6, 0.85);
            var tangent = tangentVelocity.Magnitude > 1 ? tangentVelocity.Normalized : east;
            var desiredAim = (tangent*System.Math.Sqrt(1-sine*sine)+up*sine).Normalized;
            var cross = _poweredAim.Cross(desiredAim);
            double angle = System.Math.Atan2(cross.Magnitude,
                System.Math.Clamp(_poweredAim.Dot(desiredAim), -1, 1));
            if (cross.Magnitude < 1e-9 && angle > 1)
                cross = _poweredAim.Cross(System.Math.Abs(_poweredAim.Dot(Vector3d.Up)) < 0.9
                    ? Vector3d.Up : Vector3d.Right);
            double stepAngle = System.Math.Min(angle,
                _guidance.MaximumInitialTurnReferenceRateRadPerSecond*controlIntervalSeconds);
            // Slew only the command reference. The physical attitude remains actuator-driven.
            if (!_initialPoweredTurnComplete)
            {
                _poweredAim = angle < 1e-9 ? desiredAim
                    : Quaterniond.FromAxisAngle(cross.Normalized, stepAngle).Rotate(_poweredAim).Normalized;
                if (angle <= stepAngle+1e-9 && elapsed-StagingElapsedSeconds > 5)
                    _initialPoweredTurnComplete = true;
            }
            else _poweredAim = desiredAim;
            Aim(_poweredAim, _guidance.PoweredPointingGain, _guidance.PoweredPointingDamping);
            _ship.Throttle = shipThrottle;
            return;
        }
        if (Phase == Flight14LaunchPhase.SuborbitalCoast)
        {
            CoastMinimumAltitudeM = System.Math.Min(CoastMinimumAltitudeM, _ship.GetAltitude(_body));
            _ship.Throttle = 0;
            Aim(velocity.Normalized, 2, 25);
            if (CoastMinimumAltitudeM <= _body.Atmosphere!.MaxAltitude) { Block("coast-atmospheric-encounter"); return; }
            if (AscentInsertionPolicy.ShouldBeginInsertion(orbit, universe.CurrentTime, _body.GM,
                radialSpeed, _guidance.InsertionLeadSeconds))
            {
                if (_ship.GetAltitude(_body) < _guidance.MinimumInsertionAltitudeM)
                { Block("insertion-altitude-envelope"); return; }
                _insertion = new Flight14OrbitalBurn(Flight14BurnKind.Insertion,
                    _guidance.TargetInsertionPeriapsisAltitudeM, _guidance.SeaLevelEngineModelId,
                    3, _guidance.MinimumInsertionAltitudeM, _guidance.MaximumInsertionSeconds);
                InsertionElapsedSeconds = elapsed;
                Phase = Flight14LaunchPhase.Insertion;
            }
            return;
        }
        if (Phase == Flight14LaunchPhase.Insertion)
        {
            _insertion!.Advance(_ship, _body, controlIntervalSeconds, universe.CurrentTime);
            if (_insertion.Status == Flight14BurnStatus.Completed)
            {
                OrbitElapsedSeconds = elapsed;
                Phase = Flight14LaunchPhase.OrbitReady;
            }
            else if (_insertion.Status is Flight14BurnStatus.Blocked or Flight14BurnStatus.TargetAlreadySatisfied)
                Block(_insertion.BlockReason ?? "no-delivered-insertion");
        }
    }

    private void Aim(Vector3d direction, double gain, double damping) =>
        _ship.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(_ship.Orientation,
            Vector3d.Up, direction, _ship.AngularVelocity, gain, damping);

    private void Block(string reason)
    {
        _ship.Throttle = 0;
        _ship.PitchYawRoll = Vector3d.Zero;
        Phase = Flight14LaunchPhase.Blocked;
        BlockReason = reason;
    }
}
