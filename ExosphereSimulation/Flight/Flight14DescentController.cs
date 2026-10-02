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

        var liftUp = up - flow * up.Dot(flow);
        Vector3d liftReference = liftUp;
        if (speed >= _definition.HypersonicControlThresholdMps)
        {
            var axis = _ship.Orientation.Rotate(Vector3d.Up).Normalized;
            double density = _body.Atmosphere!.GetDensity(state.AltitudeM);
            var drag = AerodynamicsModel.ComputeReentryDrag(density, surfaceVelocity, axis,
                _ship.VehicleLength, _ship.MaximumDiameter, _body.Atmosphere.GetTemperature(state.AltitudeM),
                _ship.Parts.AxialDragCoefficient);
            var lift = AerodynamicsModel.ComputeLift(density, surfaceVelocity, axis,
                _ship.VehicleLength, _ship.MaximumDiameter);
            var inertial = _ship.Velocity - _body.Velocity;
            var tangential = inertial - up * inertial.Dot(up);
            double radius = (_ship.Position - _body.Position).Magnitude;
            double ballistic = -_body.GM/(radius*radius) + tangential.MagnitudeSquared/radius
                + drag.Dot(up)/_ship.TotalMass;
            var side = EntryCorridorGuidance.ComputeBankSide(flow, up, _priorLift);
            var limited = EntryCorridorGuidance.LimitLiftForDescent(liftUp, liftUp, side,
                verticalSpeed, speed, ballistic, lift.Magnitude/_ship.TotalMass*liftUp.Magnitude);
            liftReference = EntryCorridorGuidance.BlendForAerodynamicAuthority(limited, liftUp,
                state.DynamicPressurePa, _definition.BankOnsetDynamicPressurePa, _definition.BankFullDynamicPressurePa);
        }
        if (liftReference.MagnitudeSquared < 1e-12)
            liftReference = _priorLift - flow * _priorLift.Dot(flow);
        _priorLift = liftReference;
        double terminalFraction = System.Math.Clamp(
            (_definition.HypersonicControlThresholdMps-speed)
                / (_definition.HypersonicControlThresholdMps-_definition.TerminalAngleOfAttackSpeedMps), 0, 1);
        double angle = _definition.EntryAngleOfAttackDegrees + terminalFraction
            * (_definition.TerminalAngleOfAttackDegrees-_definition.EntryAngleOfAttackDegrees);
        var axisReference = AerodynamicsModel.ComputeEntryAxisForLift(flow, liftReference, angle);
        var desired = AerodynamicsModel.ComputeBellyFirstOrientation(axisReference, flow);
        _reference = AttitudeGuidance.SlewQuaternion(_reference, desired,
            controlIntervalSeconds, _definition.ReferenceSlewRateRadPerSecond);
        _reference = AerodynamicsModel.ConstrainBellyFirstOrientationToAngle(_reference, flow, angle);
        _ship.PitchYawRoll = AttitudeGuidance.ComputeCommand(_ship.Orientation, _reference,
            _ship.AngularVelocity, 2, 25);
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
            _ship.Parts.Parts.Sum(p => p.LiquidFuel+p.Oxidizer),
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
