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
            var axis = ship.Orientation.Rotate(Vector3d.Up).Normalized;
            double density = body.Atmosphere!.GetDensity(state.AltitudeM);
            var drag = AerodynamicsModel.ComputeReentryDrag(density, surfaceVelocity, axis,
                ship.VehicleLength, ship.MaximumDiameter, body.Atmosphere.GetTemperature(state.AltitudeM),
                ship.Parts.AxialDragCoefficient);
            var lift = AerodynamicsModel.ComputeLift(density, surfaceVelocity, axis,
                ship.VehicleLength, ship.MaximumDiameter);
            var inertial = ship.Velocity - body.Velocity;
            var tangential = inertial - up * inertial.Dot(up);
            double radius = (ship.Position - body.Position).Magnitude;
            double ballistic = -body.GM/(radius*radius) + tangential.MagnitudeSquared/radius
                + drag.Dot(up)/ship.TotalMass;
            var side = EntryCorridorGuidance.ComputeBankSide(flow, up, priorLift);
            var limited = EntryCorridorGuidance.LimitLiftForDescent(liftUp, liftUp, side,
                verticalSpeed, speed, ballistic, lift.Magnitude/ship.TotalMass*liftUp.Magnitude,
                definition.MinimumDownwardSpeedMps, definition.DownwardSpeedFraction,
                definition.VerticalResponseSeconds, definition.DownwardSpeedDeadbandMps);
            liftReference = EntryCorridorGuidance.BlendForAerodynamicAuthority(limited, liftUp,
                state.DynamicPressurePa, definition.BankOnsetDynamicPressurePa, definition.BankFullDynamicPressurePa);
        }
        if (liftReference.MagnitudeSquared < 1e-12)
            liftReference = priorLift - flow * priorLift.Dot(flow);
        priorLift = liftReference;
        double terminalFraction = System.Math.Clamp(
            (definition.HypersonicControlThresholdMps-speed)
                / (definition.HypersonicControlThresholdMps-definition.TerminalAngleOfAttackSpeedMps), 0, 1);
        double angle = definition.EntryAngleOfAttackDegrees + terminalFraction
            * (definition.TerminalAngleOfAttackDegrees-definition.EntryAngleOfAttackDegrees);
        var axisReference = AerodynamicsModel.ComputeEntryAxisForLift(flow, liftReference, angle);
        var desired = AerodynamicsModel.ComputeBellyFirstOrientation(axisReference, flow);
        reference = AttitudeGuidance.SlewQuaternion(reference, desired,
            controlIntervalSeconds, definition.ReferenceSlewRateRadPerSecond);
        reference = AerodynamicsModel.ConstrainBellyFirstOrientationToAngle(reference, flow, angle);
        return Flight14AerodynamicAttitude.ComputeCommand(ship, body, reference,
            definition.TrimOnsetDynamicPressurePa, definition.TrimFullDynamicPressurePa);
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
