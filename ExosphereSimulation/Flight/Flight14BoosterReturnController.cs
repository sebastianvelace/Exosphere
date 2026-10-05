namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;

public enum Flight14BoosterReturnPhase { WaitingForSeparation, Flip, Boostback, Coast, Landing, WaterEntry, ContactObserved, Blocked }
public sealed record Flight14BoosterWitness(Flight14BoosterReturnPhase Phase, double MissionElapsedSeconds,
    double AltitudeM, double VerticalSpeedMps, double AirspeedMps, double LatitudeDegrees,
    double LongitudeDegrees, double PropellantKg, double MainOxidizerKg, int RunningEngines, int SelectedEngines);

/// <summary>
/// Independent detached-stage authority. Uses existing thrust, attitude, fuel and water forces.
/// Reference counts drive engine selection; clocks never assign kinematics or certify recovery.
/// </summary>
public sealed class Flight14BoosterReturnController : IPhysicsStepController
{
    private readonly Flight14LaunchController _launch;
    private readonly CelestialBody _body;
    private readonly Flight14BoosterReturnDefinition _definition;
    private readonly Flight14MissionDefinition _reference;
    private Part? _engine;
    private double _separationTime, _waterTime, _boostbackStartMass, _boostbackEndMass;
    private int _landingSequenceIndex;
    private bool _deliveredLandingIgnition;
    private bool _landingFeedOpened;
    private Vector3d _boostbackHorizontalAim;
    public double BoostbackDeltaVProgress { get; private set; }
    public double BoostbackElevationDegrees { get; private set; }
    private readonly List<Flight14BoosterWitness> _events = new();
    public Vessel? Booster { get; private set; }
    public Flight14BoosterReturnPhase Phase { get; private set; } = Flight14BoosterReturnPhase.WaitingForSeparation;
    public string? BlockReason { get; private set; }
    public IReadOnlyList<Flight14BoosterWitness> Events => _events;
    public bool IsStopped => Phase is Flight14BoosterReturnPhase.ContactObserved or Flight14BoosterReturnPhase.Blocked;
    public bool HasDeliveredBoostbackEngines { get; private set; }
    public bool HasDeliveredLandingIgnition => _deliveredLandingIgnition;
    public bool FlightTerminationTriggered { get; private set; }

    public Flight14BoosterReturnController(Vessel stack, CelestialBody body, Flight14LaunchController launch,
        Flight14BoosterReturnDefinition definition, Flight14MissionDefinition reference)
    {
        definition.Validate(); reference.Validate();
        if (reference.BoosterRecovery != "gulf-splashdown"
            || reference.BoosterBoostbackResourceLimit != "main-tank-lox-exhaustion"
            || !reference.BoosterLandingEngineSequence.SequenceEqual(new[] { 11, 5, 3 }))
            throw new ArgumentException("Unsupported Flight 14 booster recovery sequence.");
        _launch = launch; _body = body; _definition = definition; _reference = reference;
        var engine = stack.Parts.Parts.Single(p => p.Definition.HasVehicleRole("booster"));
        if (engine.EngineStates.Count != reference.BoosterAscentEngines || engine.Definition.MixtureRatio <= 0)
            throw new ArgumentException("Booster return requires the declared engine topology and mixture ratio.");
        double fuelFraction = 1/(1+engine.Definition.MixtureRatio);
        if (!stack.IsGroundHeld || double.IsFinite(launch.LiftoffEpoch))
            throw new ArgumentException("Booster reserve must be set before launch.");
        // Partition the inherited inventory; changing launch loading here would
        // also change the carrier's ascent and subsequent return trajectory.
        double fuel = definition.LandingReserveKg*fuelFraction;
        engine.ReserveLandingPropellant(fuel, definition.LandingReserveKg-fuel);
    }

    public bool RequiresFixedCadence(Universe universe) => !IsStopped;

    public void BeforePhysicsStep(Universe universe, double interval)
    {
        if (!double.IsFinite(interval) || interval <= 0) throw new ArgumentOutOfRangeException(nameof(interval));
        if (IsStopped) return;
        if (Booster == null)
        {
            if (_launch.DetachedBooster is not { } detached) return;
            if (!universe.Vessels.Contains(detached)) { Block("detached-booster-missing"); return; }
            Booster = detached;
            _engine = detached.Parts.Parts.Single(p => p.Definition.HasVehicleRole("booster"));
            Booster.Name = "Super Heavy / Flight 14 engineering return";
            Booster.IsAttemptingTowerCatch = false;
            _separationTime = universe.CurrentTime;
            Transition(Flight14BoosterReturnPhase.Flip, universe);
        }
        var booster = Booster;
        var engine = _engine!;
        if (booster.IsDestroyed || booster.StructuralControlLost || engine.IsBroken || booster.IsGroundHeld)
        { Block("booster-vehicle-unavailable"); return; }
        if (universe.CurrentTime-_separationTime > _definition.MaximumReturnSeconds)
        { Block("booster-return-duration-envelope"); return; }
        booster.SASEnabled = false;
        var up = _body.GetGeodeticUp(booster.Position);
        var velocity = booster.GetSurfaceVelocity(_body);
        double altitude = booster.GetAltitude(_body), vertical = velocity.Dot(up);
        var horizontal = velocity-up*vertical;
        double gravity = _body.GM/(booster.Position-_body.Position).MagnitudeSquared;
        if (!double.IsFinite(altitude) || !double.IsFinite(velocity.Magnitude))
        { Block("booster-nonfinite-state"); return; }

        if (Phase is Flight14BoosterReturnPhase.Flip or Flight14BoosterReturnPhase.Boostback)
        {
            if (engine.FuelDepleted || engine.AvailableOxidizer <= 1e-6)
            {
                booster.Throttle = 0; engine.SelectEngineCount(0);
                if (!HasDeliveredBoostbackEngines) { Block("boostback-thrust-not-delivered"); return; }
                Transition(Flight14BoosterReturnPhase.Coast, universe); return;
            }
            if (_boostbackStartMass == 0)
            {
                double available = engine.AvailableLiquidFuel+engine.AvailableOxidizer;
                if (available <= 1 || available >= booster.TotalMass)
                { Block("boostback-feed-budget-unavailable"); return; }
                _boostbackStartMass = booster.TotalMass;
                _boostbackEndMass = booster.TotalMass-available;
                _boostbackHorizontalAim = ComputeBoostbackHorizontalAim(universe);
            }
            // Sweep a control reference in proportion to ideal rocket
            // delta-v progress, not elapsed time. Fuel consumption and the physical attitude
            // response determine the trajectory; the sweep never assigns a pose.
            BoostbackDeltaVProgress = System.Math.Clamp(System.Math.Log(_boostbackStartMass/booster.TotalMass)
                /System.Math.Log(_boostbackStartMass/_boostbackEndMass), 0, 1);
            BoostbackElevationDegrees = _definition.BoostbackInitialElevationDegrees+BoostbackDeltaVProgress
                *(_definition.BoostbackFinalElevationDegrees-_definition.BoostbackInitialElevationDegrees);
            double elevation = BoostbackElevationDegrees*MathUtils.DEG_TO_RAD;
            var direction = (_boostbackHorizontalAim-up*_boostbackHorizontalAim.Dot(up)).Normalized;
            var aim = (up*System.Math.Sin(elevation)+direction*System.Math.Cos(elevation)).Normalized;
            Aim(aim);
            if (Phase == Flight14BoosterReturnPhase.Flip)
            {
                // Retain the centre cluster's powered flip; commanding all 31 before
                // alignment would deliver a large prograde impulse during the turn.
                engine.SelectEngineCount(3); booster.Throttle = engine.Definition.MinThrottle;
                if (booster.Orientation.Rotate(Vector3d.Up).Dot(aim) < _definition.MinimumFlipAlignment) return;
                Transition(Flight14BoosterReturnPhase.Boostback, universe);
            }
            if (HealthyEngines() < _reference.BoosterBoostbackEngines)
            { Block("boostback-engine-count-unavailable"); return; }
            engine.SelectEngineCount(_reference.BoosterBoostbackEngines);
            booster.Throttle = engine.ApplyThrottleFloor(_definition.BoostbackThrottle);
            if (RunningEngines() == _reference.BoosterBoostbackEngines) HasDeliveredBoostbackEngines = true;
            return;
        }
        if (Phase == Flight14BoosterReturnPhase.Coast)
        {
            booster.Throttle = 0; engine.SelectEngineCount(0); Aim(up);
            if (vertical >= 0) return;
            engine.SelectEngineCount(_reference.BoosterLandingEngineSequence[0]);
            double maximumBrake = engine.GetFullThrottleThrustMagnitude(booster.GetAmbientPressure(_body))
                /booster.TotalMass-gravity;
            double stoppingDistance = vertical*vertical/(2*System.Math.Max(1, maximumBrake));
            // A nominal altitude cannot overrule the stopping distance of a
            // faster descent. Include startup travel before the engines deliver.
            double armAltitude = System.Math.Max(_definition.LandingArmAltitudeM,
                stoppingDistance+System.Math.Abs(vertical)*1.0);
            if (altitude > armAltitude) return;
            if (!CanStartSelectedEngines(_reference.BoosterLandingEngineSequence[0]))
            { Block("landing-restart-budget-unavailable"); return; }
            if (HealthyEngines() < _reference.BoosterLandingEngineSequence[0])
            { Block("landing-engine-count-unavailable"); return; }
            if (engine.ReservedLiquidFuel <= 0 || engine.ReservedOxidizer <= 0)
            { Block("landing-reserve-unavailable"); return; }
            engine.OpenLandingPropellantFeed(); _landingFeedOpened = true;
            var positions = booster.Parts.ComputePartLocalPositions();
            double bottom = positions.Min(p => p.Value.Y-p.Key.Definition.LengthM*0.5);
            booster.WaterContact = new(booster.MaximumDiameter*0.5, booster.VehicleLength,
                _definition.LowestPointYM, booster.Parts.CenterOfMass.Y-bottom,
                _definition.WaterDensityKgPerM3, _definition.WaterDragCoefficient, _definition.WettingDepthM,
                _definition.MinimumWaterLatitudeDegrees, _definition.MaximumWaterLatitudeDegrees,
                _definition.MinimumWaterLongitudeDegrees, _definition.MaximumWaterLongitudeDegrees, bottom);
            Transition(Flight14BoosterReturnPhase.Landing, universe);
        }
        if (Phase == Flight14BoosterReturnPhase.WaterEntry)
        {
            booster.Throttle = 0; booster.PitchYawRoll = Vector3d.Zero; engine.SelectEngineCount(0);
            if (universe.CurrentTime-_waterTime >= _definition.WaterObservationSeconds)
            {
                Transition(Flight14BoosterReturnPhase.ContactObserved, universe);
                // Published FTS demonstration closes the expendable stage's mission.
                // The observation delay is estimated; no explosive energy or fragments
                // are fabricated. Retain the terminal witness, stop propagating the wreck.
                FlightTerminationTriggered = true;
                booster.IsDestroyed = true;
                booster.DestructionCause = VesselDestructionCause.FlightTermination;
            }
            return;
        }
        if (Phase != Flight14BoosterReturnPhase.Landing) return;
        if (engine.FuelDepleted) { Block("landing-propellant-exhausted"); return; }
        if (RunningEngines() == _reference.BoosterLandingEngineSequence[0]) _deliveredLandingIgnition = true;
        var contact = WaterContactSolver.Evaluate(booster, _body, booster.WaterContact!, booster.Position, booster.Velocity);
        if (contact.LowestPointAltitudeM <= 0)
        {
            if (!booster.WaterContact!.Covers(_body, booster.Position, universe.CurrentTime))
            { Block("booster-contact-outside-gulf-envelope"); return; }
            if (!_deliveredLandingIgnition || vertical >= 0 || contact.EntrySpeedMps > _definition.MaximumContactSpeedMps
                || booster.Orientation.Rotate(Vector3d.Up).Dot(up) < System.Math.Cos(10*MathUtils.DEG_TO_RAD))
            { Block("booster-water-entry-state-envelope"); return; }
            booster.Throttle = 0; booster.PitchYawRoll = Vector3d.Zero; engine.SelectEngineCount(0);
            booster.WaterMotionEnabled = true; _waterTime = universe.CurrentTime;
            Transition(Flight14BoosterReturnPhase.WaterEntry, universe); return;
        }
        double clearance = System.Math.Max(0, contact.LowestPointAltitudeM);
        double alignment = System.Math.Max(0.2, booster.Orientation.Rotate(Vector3d.Up).Dot(up));
        // Aerodynamic braking is already delivered by the production force model.
        // Asking the engines to supply that deceleration again causes an early
        // stop, excess propellant consumption and a climb before water contact.
        double aerodynamicUpForce = booster.ComputeDrag(_body).Dot(up);
        double forceWithoutAerodynamics = booster.TotalMass*(gravity
            + _definition.LandingBrakingMargin*System.Math.Max(0, vertical*vertical-_definition.TargetContactSpeedMps*_definition.TargetContactSpeedMps)
                /(2*System.Math.Max(1, clearance)))/alignment;
        double force = System.Math.Max(0, forceWithoutAerodynamics-aerodynamicUpForce/alignment);
        // One continuous ignition, monotone 11 -> 5 -> 3. Require delivered thrust
        // before dropping another set, so a requested count cannot certify a relight.
        int count = _reference.BoosterLandingEngineSequence[_landingSequenceIndex];
        if (_deliveredLandingIgnition && RunningEngines() == count
            && _landingSequenceIndex+1 < _reference.BoosterLandingEngineSequence.Count)
        {
            int next = _reference.BoosterLandingEngineSequence[_landingSequenceIndex+1];
            engine.SelectEngineCount(next);
            // Keep capacity for the end of the burn, when the current high-speed
            // drag has disappeared. A transient drag peak cannot justify an
            // irreversible reduction to an insufficient engine cluster.
            if (forceWithoutAerodynamics <= engine.GetFullThrottleThrustMagnitude(booster.GetAmbientPressure(_body)))
            { _landingSequenceIndex++; count = next; _events.Add(Witness(universe)); }
        }
        engine.SelectEngineCount(count);
        double rated = engine.GetFullThrottleThrustMagnitude(booster.GetAmbientPressure(_body));
        booster.Throttle = engine.ApplyThrottleFloor(System.Math.Max(engine.Definition.MinThrottle, force/System.Math.Max(1, rated)));
        double tiltLimit = System.Math.Tan(_definition.MaximumLandingTiltDegrees*MathUtils.DEG_TO_RAD)
            *System.Math.Clamp(clearance/100, 0, 1);
        // Lateral damping acts through engine thrust, not gravity. Using g
        // here overcommands tilt during the high-thrust braking segment and
        // can reverse the horizontal error faster than attitude can follow.
        double thrustAcceleration = System.Math.Max(gravity, rated*booster.Throttle/booster.TotalMass);
        double tilt = System.Math.Min(tiltLimit,
            _definition.HorizontalDampingPerSecond*horizontal.Magnitude/thrustAcceleration);
        Aim((up-horizontal.Normalized*tilt).Normalized);
    }

    private Vector3d ComputeBoostbackHorizontalAim(Universe universe)
    {
        var booster = Booster!; var engine = _engine!;
        var target = _body.GetSurfacePositionAtTime(_definition.TargetLatitudeDegrees,
            _definition.TargetLongitudeDegrees, 0, universe.CurrentTime);
        var up = _body.GetGeodeticUp(booster.Position);
        var velocity = booster.GetSurfaceVelocity(_body);
        double vertical = velocity.Dot(up), altitude = booster.GetAltitude(_body);
        double gravity = _body.GM/(booster.Position-_body.Position).MagnitudeSquared;
        var horizontal = velocity-up*vertical;
        var offset = target-booster.Position;
        var horizontalOffset = offset-up*offset.Dot(up);
        engine.SelectEngineCount(_reference.BoosterBoostbackEngines);
        double available = engine.AvailableLiquidFuel+engine.AvailableOxidizer;
        double exhaustSpeed = engine.Definition.IspVac*9.80665;
        double thrust = engine.GetFullThrottleThrustMagnitude(booster.GetAmbientPressure(_body))
            *engine.ApplyThrottleFloor(_definition.BoostbackThrottle);
        double duration = available*exhaustSpeed/System.Math.Max(1, thrust);
        double totalDeltaV = exhaustSpeed*System.Math.Log(_boostbackStartMass/_boostbackEndMass);
        double upwardDeltaV = 0, upwardTravel = 0;
        // Bearing forecast only: constant ambient pressure/thrust, ideal attitude,
        // variable mass, local constant gravity, no drag or body rotation in the
        // predicted coast. Production propagation retains all existing forces.
        const int samples = 32;
        for (int i = 0; i < samples; i++)
        {
            double fraction = (i+0.5)/samples;
            double mass = _boostbackStartMass-available*fraction;
            double progress = exhaustSpeed*System.Math.Log(_boostbackStartMass/mass)/totalDeltaV;
            double elevation = (_definition.BoostbackInitialElevationDegrees+progress
                *(_definition.BoostbackFinalElevationDegrees-_definition.BoostbackInitialElevationDegrees))*MathUtils.DEG_TO_RAD;
            double verticalDeltaV = thrust/mass*duration/samples*System.Math.Sin(elevation);
            upwardDeltaV += verticalDeltaV;
            upwardTravel += verticalDeltaV*duration*(1-fraction);
        }
        double endVertical = vertical+upwardDeltaV-gravity*duration;
        double endAltitude = altitude+vertical*duration+upwardTravel-0.5*gravity*duration*duration;
        double coastTime = System.Math.Max(1, (endVertical+System.Math.Sqrt(endVertical*endVertical
            +2*gravity*System.Math.Max(0, endAltitude)))/gravity);
        var desiredHorizontal = (horizontalOffset-horizontal*duration*0.5)/(coastTime+duration*0.5);
        var direction = (desiredHorizontal-horizontal).Normalized;
        return direction.MagnitudeSquared > 1e-12 ? direction : _body.GetEastDirection(booster.Position);
    }

    private int HealthyEngines() => _engine!.EngineStates.Count(e => e.FailureCode == null
        && e.State != Exosphere.Simulation.Propulsion.EngineLifecycleState.Failed);
    private int RunningEngines()
    {
        int count = 0; double pressure = Booster!.GetAmbientPressure(_body);
        for (int i = 0; i < _engine!.EngineStates.Count; i++)
            if (_engine.EngineStates[i].State == Exosphere.Simulation.Propulsion.EngineLifecycleState.Running
                && _engine.GetEngineInstancePerformance(i, pressure).ThrustN > 1) count++;
        return count;
    }
    private bool CanStartSelectedEngines(int count)
    {
        int selected = 0;
        foreach (var state in _engine!.EngineStates)
        {
            if (state.FailureCode != null || state.State == Exosphere.Simulation.Propulsion.EngineLifecycleState.Failed) continue;
            if (++selected > count) break;
            if (!_engine.Definition.ResolvedEngineModels.TryGetValue(state.EngineModelId, out var model)) return false;
            if (state.StartAttempts >= model.RestartLimit+1) return false;
        }
        return selected >= count;
    }
    private void Aim(Vector3d aim) => Booster!.PitchYawRoll = AttitudeGuidance.ComputeAxisPointingCommand(
        Booster.Orientation, Vector3d.Up, aim, Booster.AngularVelocity, _definition.PointingGain, _definition.PointingDamping);
    private Flight14BoosterWitness Witness(Universe universe)
    {
        _body.GetGeodeticCoordinatesAtTime(Booster!.Position, universe.CurrentTime, out double lat, out double lon, out _);
        var velocity = Booster.GetSurfaceVelocity(_body);
        return new(Phase, universe.CurrentTime-_launch.LiftoffEpoch, Booster.GetAltitude(_body),
            velocity.Dot(_body.GetGeodeticUp(Booster.Position)), velocity.Magnitude, lat, lon,
            Booster.Parts.Parts.Sum(p => p.LiquidFuel+p.Oxidizer), _landingFeedOpened ? 0 : _engine!.AvailableOxidizer, RunningEngines(), _engine!.SelectedEngineCount);
    }
    private void Transition(Flight14BoosterReturnPhase phase, Universe universe)
    { Phase = phase; _events.Add(Witness(universe)); }
    private void Block(string reason)
    {
        if (Booster != null) { Booster.Throttle = 0; Booster.PitchYawRoll = Vector3d.Zero; _engine?.SelectEngineCount(0); }
        Phase = Flight14BoosterReturnPhase.Blocked; BlockReason = reason;
    }
}

/// <summary>Two independent authorities share the same pre-integration control boundary.</summary>
public sealed class Flight14IndependentReturnController(IPhysicsStepController ship,
    Flight14BoosterReturnController booster) : IPhysicsStepController
{
    public bool RequiresFixedCadence(Universe universe) => ship.RequiresFixedCadence(universe) || booster.RequiresFixedCadence(universe);
    public void BeforePhysicsStep(Universe universe, double interval)
    { ship.BeforePhysicsStep(universe, interval); booster.BeforePhysicsStep(universe, interval); }
}
