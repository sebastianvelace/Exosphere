namespace Exosphere.Simulation.Flight;

using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;

public enum Flight14DescentPhase { WaitingForEntry, HypersonicEntry, AerodynamicDescent, DescentReached, Blocked }

/// <summary>
/// Continues the same ship below the entry diagnostic's 90 km endpoint. Only controls
/// are written; no force, kinematic, thermal or resource state is imposed.
/// Does not provide geographic landing guidance or powered/water-return acceptance.
/// </summary>
public sealed class Flight14DescentController : IPhysicsStepController
{
    private readonly Vessel _ship;
    private readonly CelestialBody _body;
    private readonly Flight14ReturnController _return;
    private readonly Flight14LaunchController _launch;
    private readonly Flight14DescentDefinition _definition;
    private Quaterniond _reference;
    private Vector3d _priorLift;

    public Flight14DescentPhase Phase { get; private set; } = Flight14DescentPhase.WaitingForEntry;
    public string? BlockReason { get; private set; }
    public Flight14EntryWitness? StartWitness { get; private set; }
    public Flight14EntryWitness? EndWitness { get; private set; }
    public double PeakAerodynamicLoadG { get; private set; }
    public double PeakDynamicPressurePa { get; private set; }
    public double PeakStagnationHeatFluxWPerM2 { get; private set; }
    public double MaximumAltitudeAfterHandoffM { get; private set; }
    public double MaximumClimbRateMps { get; private set; }
    public double MaximumAngularRateRadPerSecond { get; private set; }

    public Flight14DescentController(Vessel ship, CelestialBody body,
        Flight14ReturnController returning, Flight14LaunchController launch,
        Flight14DescentDefinition definition)
    {
        definition.Validate();
        if (body.Atmosphere == null || definition.DiagnosticEndAltitudeM >= body.Atmosphere.MaxAltitude)
            throw new ArgumentException("Flight 14 descent requires an atmospheric endpoint.");
        _ship = ship; _body = body; _return = returning; _launch = launch; _definition = definition;
    }

    public bool RequiresFixedCadence(Universe universe) => Phase is not
        (Flight14DescentPhase.DescentReached or Flight14DescentPhase.Blocked);

    public void BeforePhysicsStep(Universe universe, double controlIntervalSeconds)
    {
        ArgumentNullException.ThrowIfNull(universe);
        if (!double.IsFinite(controlIntervalSeconds) || controlIntervalSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(controlIntervalSeconds));
        if (Phase is Flight14DescentPhase.DescentReached or Flight14DescentPhase.Blocked) return;
        if (!ReferenceEquals(universe.ActiveVessel, _ship)) { Block("active-vessel-changed"); return; }
        if (Phase == Flight14DescentPhase.WaitingForEntry)
        {
            _return.BeforePhysicsStep(universe, controlIntervalSeconds);
            if (_return.Phase == Flight14ReturnPhase.Blocked) { Block("return:"+_return.BlockReason); return; }
            if (_return.Phase != Flight14ReturnPhase.EntryReached) return;
            StartWitness = _return.DiagnosticEnd;
            if (StartWitness == null || _definition.DiagnosticEndAltitudeM >= StartWitness.GeodeticAltitudeM)
            { Block("descent-handoff-envelope"); return; }
            _reference = _ship.Orientation;
            _priorLift = _ship.Orientation.Rotate(Vector3d.Up);
            Phase = Flight14DescentPhase.HypersonicEntry;
        }
        if (_ship.IsDestroyed || _ship.StructuralControlLost) { Block("vehicle-damaged"); return; }
        double elapsed = universe.CurrentTime - _launch.LiftoffEpoch;
        var state = EntryFlightDiagnostics.Evaluate(_ship, _body);
        if (!double.IsFinite(elapsed) || elapsed > _definition.MaximumMissionSeconds
            || !double.IsFinite(state.AltitudeM) || !double.IsFinite(state.AtmosphereRelativeSpeedMps)
            || state.AltitudeM > _body.Atmosphere!.MaxAltitude)
        { Block("descent-state-or-duration-envelope"); return; }
        var up = _body.GetGeodeticUp(_ship.Position);
        var surfaceVelocity = _ship.GetSurfaceVelocity(_body);
        var flow = surfaceVelocity.Normalized;
        double speed = surfaceVelocity.Magnitude;
        double verticalSpeed = surfaceVelocity.Dot(up);
        PeakAerodynamicLoadG = System.Math.Max(PeakAerodynamicLoadG, state.AerodynamicLoadG);
        PeakDynamicPressurePa = System.Math.Max(PeakDynamicPressurePa, state.DynamicPressurePa);
        PeakStagnationHeatFluxWPerM2 = System.Math.Max(PeakStagnationHeatFluxWPerM2, state.StagnationHeatFluxWPerM2);
        MaximumAltitudeAfterHandoffM = System.Math.Max(MaximumAltitudeAfterHandoffM, state.AltitudeM);
        MaximumClimbRateMps = System.Math.Max(MaximumClimbRateMps, verticalSpeed);
        MaximumAngularRateRadPerSecond = System.Math.Max(MaximumAngularRateRadPerSecond, _ship.AngularVelocity.Magnitude);
        _ship.SASEnabled = false;
        _ship.PilotRateEnvelopeEnabled = false;
        _ship.Throttle = 0;
        if (Phase == Flight14DescentPhase.HypersonicEntry && speed < _definition.HypersonicControlThresholdMps)
            Phase = Flight14DescentPhase.AerodynamicDescent;
        if (state.AltitudeM <= _definition.DiagnosticEndAltitudeM)
        {
            if (Phase != Flight14DescentPhase.AerodynamicDescent || verticalSpeed >= 0
                || speed > _definition.MaximumEndSpeedMps)
            { Block("descent-end-state-envelope"); return; }
            EndWitness = Witness(universe);
            _ship.PitchYawRoll = Vector3d.Zero;
            Phase = Flight14DescentPhase.DescentReached;
            return;
        }

        _ship.PitchYawRoll = ComputeGuidanceCommand(_ship, _body, _definition, state,
            controlIntervalSeconds, ref _reference, ref _priorLift);
    }

    /// <summary>
    /// One step of the descent attitude law: lift-limited bank in the hypersonic regime,
    /// angle-of-attack schedule toward the terminal belly-flop, slew-limited belly-first
    /// reference and the flap command with static-moment trim. Pure apart from the two
    /// carried guidance states; it writes no vessel state.
    /// </summary>
    public static Vector3d ComputeGuidanceCommand(Vessel ship, CelestialBody body,
        Flight14DescentDefinition definition, EntryFlightState state, double controlIntervalSeconds,
        ref Quaterniond reference, ref Vector3d priorLift)
    {
        var up = body.GetGeodeticUp(ship.Position);
        var surfaceVelocity = ship.GetSurfaceVelocity(body);
        var flow = surfaceVelocity.Normalized;
        double speed = surfaceVelocity.Magnitude;
        double verticalSpeed = surfaceVelocity.Dot(up);
        var liftUp = up - flow * up.Dot(flow);
        Vector3d liftReference = liftUp;
        if (speed >= definition.HypersonicControlThresholdMps)
        {
            var force = EstimateAerodynamicForce(ship, body);
            var drag = flow * force.Dot(flow);
            var lift = force - drag;
            var inertial = ship.CenterOfMassVelocity - body.Velocity;
            var tangential = inertial - up * inertial.Dot(up);
            double radius = (ship.CenterOfMass - body.Position).Magnitude;
            double ballistic = -body.GM/(radius*radius) + tangential.MagnitudeSquared/radius
                + drag.Dot(up)/ship.TotalMass;
            var side = EntryCorridorGuidance.ComputeBankSide(flow, up, priorLift);
            var limited = EntryCorridorGuidance.LimitLiftForDescent(liftUp, liftUp, side,
                verticalSpeed, speed, ballistic, lift.Magnitude/ship.TotalMass*liftUp.Magnitude,
                definition.MinimumDownwardSpeedMps, definition.DownwardSpeedFraction,
                definition.VerticalResponseSeconds, definition.DownwardSpeedDeadbandMps);
            // At low pressure retain the bank already flown at handoff. Blending from
            // local lift-up instead commands an unwanted roll out of the coast's bank
            // and then a reversal as flap authority grows, adding lift and entry delay.
            liftReference = EntryCorridorGuidance.BlendForAerodynamicAuthority(limited, lift,
                state.DynamicPressurePa, definition.BankOnsetDynamicPressurePa, definition.BankFullDynamicPressurePa);
        }
        if (liftReference.MagnitudeSquared < 1e-12)
            liftReference = priorLift - flow * priorLift.Dot(flow);
        priorLift = liftReference;
        double terminalFraction = System.Math.Clamp(
            (definition.AngleOfAttackTransitionSpeedMps-speed)
                / (definition.AngleOfAttackTransitionSpeedMps-definition.TerminalAngleOfAttackSpeedMps), 0, 1);
        double angle = definition.EntryAngleOfAttackDegrees + terminalFraction
            * (definition.TerminalAngleOfAttackDegrees-definition.EntryAngleOfAttackDegrees);
        angle = TrimFeasibleAngleOfAttack(ship, body, state.AltitudeM, surfaceVelocity, liftReference, angle,
            definition.TrimMarginDegrees * MathUtils.DEG_TO_RAD);
        var axisReference = AerodynamicsModel.ComputeEntryAxisForLift(flow, liftReference, angle);
        var desired = AerodynamicsModel.ComputeBellyFirstOrientation(axisReference, flow);
        reference = AttitudeGuidance.SlewQuaternion(reference, desired,
            controlIntervalSeconds, definition.ReferenceSlewRateRadPerSecond);
        reference = AerodynamicsModel.ConstrainBellyFirstOrientationToAngle(reference, flow, angle);
        double lead = AttitudeGuidance.ErrorAngleRadians(ship.Orientation, reference);
        double leadLimit = definition.MaximumReferenceLeadDegrees * MathUtils.DEG_TO_RAD;
        if (lead > leadLimit) reference = ship.Orientation.Slerp(reference, leadLimit / lead).Normalize();
        return Flight14AerodynamicAttitude.ComputeCommand(ship, body, reference,
            definition.TrimOnsetDynamicPressurePa, definition.TrimFullDynamicPressurePa);
    }

    /// <summary>
    /// Static onboard aerodynamic estimate from the model actually flown. In particular,
    /// distributed crossflow and physical flap loads must not be replaced by the legacy
    /// aggregate lift/drag map when computing the bank needed to hold a descent corridor.
    /// Rate-dependent loads are excluded from this equilibrium estimate.
    /// </summary>
    private static Vector3d EstimateAerodynamicForce(Vessel ship, CelestialBody body)
    {
        if (!ship.FlightControlInvertsAerodynamics || !ship.Parts.UsesDistributedAerodynamics)
            return ship.ComputeDrag(body);
        var atmosphere = body.Atmosphere!;
        double altitude = body.GetAltitude(ship.CenterOfMass);
        double density = atmosphere.GetDensity(altitude);
        double temperature = System.Math.Max(1.0, atmosphere.GetTemperature(altitude));
        var air = ship.GetAirRelativeVelocity(body);
        var com = ship.CenterOfMassOffsetLocal;
        var force = DistributedAerodynamics.Evaluate(ship.Parts.GetAeroElements(), ship.Orientation,
            Vector3d.Zero, com, air, density, temperature, ship.MaximumDiameter,
            ship.Parts.AxialDragCoefficient, atmosphere.SoundSpeedConstant,
            atmosphere.GetDynamicViscosityAtTemperature(temperature)).ForceWorld;
        if (ship.PhysicalBodyFlapsActive)
            force += BodyFlapAerodynamics.Evaluate(ship.Parts.GetBodyFlaps(), ship.BodyFlapDeflectionsRad,
                ship.Orientation, Vector3d.Zero, com, air, density, temperature,
                atmosphere.SoundSpeedConstant).ForceWorld;
        return force;
    }

    /// <summary>
    /// Angle-of-attack limiter: the scheduled angle if the body flaps can trim it with
    /// <paramref name="marginRad"/> of travel to spare, otherwise the nearest angle (1° steps,
    /// 45°–95°) they can. Evaluates the vessel's own distributed hull and flap model, as an
    /// onboard trim table would; it writes no state.
    /// </summary>
    public static double TrimFeasibleAngleOfAttack(Vessel ship, CelestialBody body, double altitude,
        Vector3d surfaceVelocity, Vector3d liftReference, double scheduledDegrees, double marginRad)
    {
        var flaps = ship.Parts.GetBodyFlaps();
        var atmosphere = body.Atmosphere;
        double speed = surfaceVelocity.Magnitude;
        if (atmosphere == null || flaps.Count == 0 || speed < 1.0 || !ship.Parts.UsesDistributedAerodynamics)
            return scheduledDegrees;
        double density = atmosphere.GetDensity(altitude);
        if (density <= 0.0) return scheduledDegrees;
        double temperature = System.Math.Max(1.0, atmosphere.GetTemperature(altitude));
        double viscosity = atmosphere.GetDynamicViscosityAtTemperature(temperature);
        var com = ship.CenterOfMassOffsetLocal;
        var elements = ship.Parts.GetAeroElements();
        var flow = surfaceVelocity / speed;
        var noseUp = new double[flaps.Count];
        var noseDown = new double[flaps.Count];
        for (int i = 0; i < flaps.Count; i++)
        {
            bool forward = flaps[i].HingeLocal.Y > com.Y;
            double high = System.Math.Max(flaps[i].MinDeflectionRad, flaps[i].MaxDeflectionRad - marginRad);
            double low = System.Math.Min(flaps[i].MaxDeflectionRad, flaps[i].MinDeflectionRad + marginRad);
            noseUp[i] = forward ? high : low;
            noseDown[i] = forward ? low : high;
        }
        bool Feasible(double degrees)
        {
            var axis = AerodynamicsModel.ComputeEntryAxisForLift(flow, liftReference, degrees);
            var orientation = AerodynamicsModel.ComputeBellyFirstOrientation(axis, flow);
            double hull = DistributedAerodynamics.Evaluate(elements, orientation, Vector3d.Zero, com,
                surfaceVelocity, density, temperature, ship.MaximumDiameter, ship.Parts.AxialDragCoefficient,
                atmosphere.SoundSpeedConstant, viscosity).TorqueBody.X;
            double a = BodyFlapAerodynamics.Evaluate(flaps, noseUp, orientation, Vector3d.Zero, com,
                surfaceVelocity, density, temperature, atmosphere.SoundSpeedConstant).TorqueBody.X;
            double b = BodyFlapAerodynamics.Evaluate(flaps, noseDown, orientation, Vector3d.Zero, com,
                surfaceVelocity, density, temperature, atmosphere.SoundSpeedConstant).TorqueBody.X;
            return (hull + a) * (hull + b) <= 0.0;
        }
        if (Feasible(scheduledDegrees)) return scheduledDegrees;
        for (int step = 1; step <= 50; step++)
        {
            foreach (double candidate in new[] { scheduledDegrees + step, scheduledDegrees - step })
                if (candidate >= 45.0 && candidate <= 95.0 && Feasible(candidate)) return candidate;
        }
        return scheduledDegrees;
    }

    private Flight14EntryWitness Witness(Universe universe)
    {
        _body.GetGeodeticCoordinatesAtTime(_ship.Position, universe.CurrentTime,
            out double latitude, out double longitude, out _);
        var inertial = _ship.Velocity - _body.Velocity;
        var surface = _ship.GetSurfaceVelocity(_body);
        double radius = (_ship.Position - _body.Position).Magnitude;
        return new(universe.CurrentTime-_launch.LiftoffEpoch, _ship.GetAltitude(_body), radius-_body.Radius,
            inertial.Magnitude, surface.Magnitude, surface.Dot(_body.GetGeodeticUp(_ship.Position)),
            inertial.MagnitudeSquared*.5-_body.GM/radius,
            _ship.Parts.Parts.Sum(p => p.LiquidFuel+p.Oxidizer+p.HeaderLiquidFuel+p.HeaderOxidizer),
            -_ship.Orientation.Rotate(Vector3d.Right).Normalized.Dot(surface.Normalized),
            universe.CurrentTime, latitude, longitude);
    }

    private void Block(string reason)
    {
        _ship.Throttle = 0;
        _ship.PitchYawRoll = Vector3d.Zero;
        Phase = Flight14DescentPhase.Blocked;
        BlockReason = reason;
    }
}
