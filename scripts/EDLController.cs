namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using Exosphere.Simulation.Flight;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Physics;
using Exosphere.Simulation.Systems;

/// <summary>
/// Entry, Descent and Landing director + HUD overlay. Activates when the active
/// vessel is descending fast into an atmospheric body, then sequences
/// ENTRY → PEAK HEATING → AERO DESCENT → RETRO BURN → FINAL → TOUCHDOWN, drawing a
/// dedicated descent HUD (radar altimeter, vertical/horizontal speed, g-force,
/// plasma vignette) and flying a retrograde suicide-burn autopilot to a soft landing.
/// </summary>
public partial class EDLController : Control
{
    public static EDLController? Instance { get; private set; }

    private enum Edl { Inactive, Entry, Peak, Aero, Retro, Catch, Final, Caught, Touchdown }
    private Edl _phase = Edl.Inactive;

    /// <summary>
    /// Presentation-only state consumed by the exterior camera.  It deliberately exposes
    /// no guidance or contact data: the camera may tighten the shot while EDL is visible,
    /// but the simulation remains the sole owner of the flight sequence.
    /// </summary>
    public bool IsPresentationActive => _phase != Edl.Inactive;
    public bool IsCatchPresentation => _phase is Edl.Catch or Edl.Caught;

    // ── Tower catch (Mechazilla) ───────────────────────────────────────────
    // Abort decision: below this altitude, a catch attempt still outside tolerance must
    // divert to a normal leg landing rather than risk a low, slow collision with tower
    // structure. Above it there is still room to keep closing the position error.
    private const double CatchAbortDecisionAltitudeM = 300.0;
    // At 300 m the one-engine catch loop still has several seconds to remove a
    // cross-range error. Keep the abort gate wider than the 5 m physical capture
    // radius so a transient guidance oscillation does not turn a recoverable approach
    // into an automatic leg-impact; the pin solver itself remains limited to 5 m.
    private const double CatchAbortHorizontalMissToleranceM = 20.0;
    private const double CatchAbortHorizontalSpeedToleranceMps = 6.0;
    private const double CatchEarlyAbortAltitudeM = 5_000.0;
    private const double CatchEarlyAbortMissToleranceM = 2_000.0;
    private const double MinimumLandingTwr = 1.10;
    // With engines off, releasing 12 m above the feet would add roughly 15 m/s before contact.
    // Keep the free-fall handoff inside the 3 m/s soft-impact budget instead. This is the
    // total surface-relative speed, not just the vertical component: the landing feet carry
    // the vehicle's lateral and angular velocity into the suspension as well.
    private const double FinalSurfaceReleaseAltitudeM = 0.25;
    private const double FinalSurfaceReleaseSpeedMps = 1.2;
    // A rotating 60 t vehicle can turn a harmless COM descent into a several-m/s foot strike
    // because the Starship landing datum is ~21 m below the physical CoM. Do not cut the
    // engines while that angular energy is still present; the contact damper is not a guidance
    // actuator and must not be used as a substitute for attitude-rate control.
    private const double FinalSurfaceReleaseAngularRateRadS = 0.08;
    // Start the vertical-only terminal envelope while there is still time for the real
    // attitude controller to settle. At 50 m the lateral branch had already rotated the
    // vehicle and amplified tangential velocity in the final approach.
    private const double TerminalVerticalPriorityAltitudeM = 300.0;
    private bool _towerCatchAborted;

    // ── Trigger thresholds ────────────────────────────────────────────────────
    private const double EntrySpeed   = StarbaseCatchPolicy.MinimumEntrySpeedMps;

    // ── Live telemetry (refreshed each frame) ─────────────────────────────────
    private double _alt, _vUp, _horiz, _gForce, _heat;
    private string _bodyName = "";

    // ── Thermal state (the entry is now survivable-or-not, so the crew has to SEE it) ──
    private double _skinTemp;        // TPS face (K) — supposed to be white-hot
    private double _hullRatio;       // structure temperature / tolerance — this is what kills
    private double _thermalDamage;   // irreversible char, 0..1
    private double _shieldAlign;     // 0..1 — how squarely the tiles meet the flow
    private double _fluxNow;         // W/m², free-stream convective flux

    /// <summary>
    /// Sim-side peak-g bookkeeping. Lives in <c>ExosphereSimulation</c> so the peak-load
    /// contract is unit-testable and Godot-free; this controller only samples and draws it.
    /// </summary>
    private readonly EntryLoadTracker _load = new();

    /// <summary>Blackout state read back from the simulation's comms system (never written here).</summary>
    private bool _blackout;
    private double _blackoutSeconds;

    private Font _font = null!;
    private bool _legsDeployed;
    private bool _flipInProgress;
    private bool _landingCutoffCommitted;
    private bool _landingBurnCoast;
    private bool _landingBurnRelit;
    private int _landingEngineCount;
    private double _flipElapsed;
    private bool _flipGateDiagnosticEmitted;
    private double _attitudeErrorDeg;
    private double _aeroAngleOfAttackDeg;
    private double _aeroWindwardFactor;
    private Vector3d _aeroAttitudeCommand;
    private Vector3d _aeroLiftReference;
    private Vector3d _filteredAeroAxis;
    private Vector3d _filteredAeroFlow;
    private Quaterniond _filteredAeroAttitude;
    private bool _aeroReferenceInitialized;

    // The real vehicle's aerodynamic reference cannot jump with one noisy guidance sample.
    // This is a reference filter only: the vessel still follows it through physical flap/torque
    // authority in Vessel.Tick.
    private const double AeroReferenceTimeConstantSeconds = 0.75;
    private const double AeroReferenceSlewRateRadPerSecond =
        6.0 * MathUtils.DEG_TO_RAD;

    /// <summary>Measured aerodynamic entry diagnostics for the visual harness and HUD QA.</summary>
    public double AeroAngleOfAttackDegrees => _aeroAngleOfAttackDeg;
    public double AeroWindwardFactor => _aeroWindwardFactor;
    public double AeroAttitudeErrorDegrees => _attitudeErrorDeg;
    public double AeroReferenceAngleOfAttackDegrees
    {
        get
        {
            if (!_aeroReferenceInitialized || _filteredAeroFlow.MagnitudeSquared < 1e-12)
                return 0.0;
            var axis = _filteredAeroAttitude.Rotate(Vector3d.Up).Normalized;
            return System.Math.Acos(System.Math.Clamp(
                axis.Dot(_filteredAeroFlow.Normalized), -1.0, 1.0)) * MathUtils.RAD_TO_DEG;
        }
    }
    public Vector3d AeroAttitudeCommand => _aeroAttitudeCommand;
    public Vector3d AeroLiftReference => _aeroLiftReference;

    public override void _Ready()
    {
        Instance = this;
        // EDL is the final writer of throttle and attitude during entry.  This
        // must run after ascent guidance and HUD systems so a stale ascent
        // command cannot cancel a landing ignition in the same frame.
        ProcessPriority = 200;
        _font = ThemeDB.GetFallbackFont();
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        var mission = MissionManager.Instance;
        if (bridge == null || vessel == null || universe == null) { Visible = false; return; }

        double processedSimDelta = bridge.LastProcessedSimulationSeconds;

        var body = universe.GetDominantBody(vessel.Position);
        if (body.Atmosphere == null) { Deactivate(); return; }

        // ── Refresh telemetry ──────────────────────────────────────────────────
        Vector3d up      = (vessel.Position - body.Position).Normalized;
        Vector3d surfVel = vessel.GetSurfaceVelocity(body);
        _alt    = body.GetAltitude(vessel.Position);
        _vUp    = surfVel.Dot(up);                         // + up, − down
        _horiz  = (surfVel - up * _vUp).Magnitude;
        _bodyName = body.Name;

        double mass = vessel.TotalMass;
        _gForce = vessel.GetProperAcceleration(body).Magnitude / EntryLoadTracker.StandardGravity;
        // Track the peak only once the EDL track is armed, so an ascent's 3 g does not
        // pre-poison the entry readout.
        if (_phase != Edl.Inactive)
            _load.Update(processedSimDelta, _gForce);

        var comms = SystemsController.Instance?.Comms;
        _blackout = comms?.PlasmaBlackout ?? false;
        _blackoutSeconds = comms?.PlasmaBlackoutSeconds ?? 0.0;

        double density = body.Atmosphere.GetDensity(_alt);
        double speed   = surfVel.Magnitude;
        _heat = density * speed * speed * speed;            // ∝ convective heat flux

        RefreshThermalState(vessel, body, density, speed, surfVel);

        // Do not disarm after entry has begun.  A lifting skip can briefly climb
        // back above the nominal atmosphere and a landing burn can cross zero
        // vertical speed; both are normal trajectory segments, not EDL aborts.

        // ── Activation check ───────────────────────────────────────────────────
        if (_phase == Edl.Inactive)
        {
            bool descending = _vUp < -20.0;
            bool inAtmo     = _alt < body.Atmosphere.MaxAltitude * 1.05;
            bool hasSuperHeavy = vessel.Parts.Parts.Any(
                p => p.Definition.IsStarshipFamily
                    && p.Definition.HasVehicleRole("booster"));
            if (descending && inAtmo && hasSuperHeavy)
            {
                // Defensive recovery for an externally-started/full-stack entry.
                // Starship cannot perform a belly-flop while still attached.
                bridge!.TriggerStaging();
                return;
            }
            if (descending && inAtmo && speed > EntrySpeed)
            {
                // Arm from ORBIT/COAST, or from a pre-entry deorbit RETRO_BURN.
                // Block only when already deep in the EDL track (ENTRY onward) so we don't
                // re-trigger from Inactive while MissionManager still shows a descent phase.
                bool blockedByMission = mission != null
                    && mission.InDescent
                    && mission.Phase is not MissionPhase.RETRO_BURN;
                if (blockedByMission)
                    return;

                _phase = Edl.Entry;
                _legsDeployed = false;
                _flipInProgress = false;
                _landingCutoffCommitted = false;
                _landingBurnCoast = false;
                _landingBurnRelit = false;
                _landingEngineCount = 3;
                _flipElapsed = 0.0;
                _flipGateDiagnosticEmitted = false;
                _towerCatchAborted = false;
                // Normal Earth Starbase returns use the same physical two-pin catch
                // path as the deterministic reentry demo. The policy is deliberately
                // narrow; Mars/Venus, other launch sites and non-Starship vehicles keep
                // their ordinary leg-landing fallback.
                bridge?.TryArmStarbaseCatchForReentry(vessel, body);
                _load.Reset();
                Visible = true;
                mission?.EnterPhase(MissionPhase.ENTRY);
            }
            else return;
        }

        // Structural dead-stick: stop writing guidance. Atmosphere still flies the wreck.
        if (vessel.StructuralControlLost)
        {
            vessel.Throttle = 0.0;
            vessel.PitchYawRoll = Vector3d.Zero;
            vessel.SASEnabled = false;
            QueueRedraw();
            return;
        }

        AdvancePhase(vessel, body, mission, mass, speed, up, surfVel, processedSimDelta, universe);
        QueueRedraw();   // live telemetry overlay
    }

    private void AdvancePhase(Vessel vessel, CelestialBody body, MissionManager? mission,
        double mass, double speed, Vector3d up, Vector3d surfVel, double delta, Universe universe)
    {
        double g = body.GetSurfaceGravity();

        double vDown   = System.Math.Max(0.0, -_vUp);
        double vertFrac = speed > 1e-3 ? vDown / speed : 1.0;
        Vector3d velDir = surfVel.Magnitude > 1e-3 ? surfVel.Normalized : -up;
        Quaterniond retroTarget = ShortestArc(Vector3d.Up, -velDir);
        Part? shipEngines = vessel.Parts.Parts.FirstOrDefault(
            p => p.Definition.IsStarshipFamily
                && p.Definition.HasVehicleRole("ship_engines"));
        bool aeroPhase = _phase is Edl.Entry or Edl.Peak or Edl.Aero;
        if (aeroPhase)
            shipEngines?.SelectEngineCount(System.Math.Min(3,
                System.Math.Max(1, shipEngines.Definition.EngineCount)));
        double aThrustFull = MaxLandingThrustAccel(vessel, body, shipEngines, mass);

        // Distance the FULL retrograde burn needs to null the WHOLE velocity vector (not just the
        // vertical part) — engines point retrograde, so they kill total speed. Net decel is the
        // thrust minus the along-track gravity component.
        double aBrake   = aThrustFull - g * vertFrac;
        double stopDist = aBrake > 0.5 ? speed * speed / (2.0 * aBrake) : double.MaxValue;

        double atmoTop = body.Atmosphere!.MaxAltitude;

        // Flip to the landing burn LOW, after the belly-flop has bled off velocity aerodynamically
        // (real Starship belly-flops to near terminal velocity, then flips at ~0.5-2 km). Hold the
        // broadside attitude through the whole aero descent — flipping to engines-retrograde high
        // up loses the drag, lets the vessel penetrate deep at hypersonic speed, and burns it up.
        // Gate: drop the belly-flop only once we're within the burn's stopping distance (so a fast
        // arrival still ignites in time) AND below a low flip ceiling so a nominal aero-braked entry
        // doesn't flip prematurely and waste propellant on a huge high-altitude burn.
        // Flip altitude: scale with the burn's stopping distance for a fast arrival, but never
        // below 4.5 km so a vessel already at belly-flop terminal velocity (~70-100 m/s) has
        // enough distance for the physical attitude flip and engine spool before the final burn.
        // The previous 3 km floor allowed the Starship return fixture to reach the ground still
        // carrying most of its vertical speed while the three centre Raptors were spooling.
        const double FlipCeiling = 8_000.0;
        double pressure = vessel.GetAmbientPressure(body);
        double flipIgnitionThrottle = shipEngines?.Definition.MinThrottle > 0.0
            ? shipEngines.Definition.MinThrottle
            : 0.40;
        double fullAngularAuthority = aeroPhase
            ? System.Math.Max(0.01,
                vessel.Parts.GetMaximumPitchYawAngularAcceleration(pressure) * flipIgnitionThrottle)
            : 0.01;
        double flipAngle = AttitudeGuidance.ErrorAngleRadians(vessel.Orientation, retroTarget);
        double flipTime = EstimateFlipTime(flipAngle, fullAngularAuthority, maxRate: 0.35);
        double flipAlt = aBrake > 0.5
            ? System.Math.Clamp(stopDist * 2.2 + vDown * (flipTime + 3.0), 4_500.0, FlipCeiling)
            : 0.0;   // can't brake yet (still hypersonic) — keep belly-flop

        // Tower-catch horizontal guidance: simple proportional homing on the position error
        // against the cradle, not a full cross-range guidance law (see the EDL scope note in
        // the Mechazilla plan — this only has to null a final-approach miss of tens of
        // metres). Computed once here so both the attitude cant and the throttle's braking
        // term below read the same error instead of two independently-derived vectors.
        Vector3d catchLateralVelocityError = Vector3d.Zero;
        Vector3d catchTargetPosition = vessel.CatchTargetPositionWorld;
        if (_phase is Edl.Retro or Edl.Catch)
        {
            // The bridge refreshes this sample at the simulation cadence immediately before
            // the guidance pass. Keep the controller on that synchronized sample: predicting
            // from a moving target here would apply the cradle velocity twice when the bridge
            // has already advanced the target for the current frame.
            Vector3d offsetFromTarget = vessel.Position - catchTargetPosition;
            Vector3d horizontalOffset = offsetFromTarget - up * offsetFromTarget.Dot(up);
            double missDistance = horizontalOffset.Magnitude;
            double closingSpeed = System.Math.Clamp(missDistance * 0.35, 0.0, 6.0);
            Vector3d towardTarget = missDistance > 1e-3 ? -horizontalOffset.Normalized : Vector3d.Zero;
            Vector3d desiredHorizontalVelocity = towardTarget * closingSpeed;
            catchLateralVelocityError = (surfVel - up * _vUp) - desiredHorizontalVelocity;
        }
        // Keep the gate explicit. The previous pattern expression was easy to misread and
        // made a normal Starbase return reach the ground still in belly-flop when the
        // computed stopping-distance floor was crossed. This is the only transition that
        // may enter the powered flip from aero flight.
        if (aeroPhase && vDown > 5.0 && _alt <= flipAlt)
        {
            if (!_flipGateDiagnosticEmitted)
            {
                GD.Print($"[EDL] flip gate entered alt={_alt:F1} m flipAlt={flipAlt:F1} " +
                         $"vDown={vDown:F1} m/s aBrake={aBrake:F2} m/s²");
                _flipGateDiagnosticEmitted = true;
            }
            _phase = Edl.Retro;
            _flipInProgress = true;
            _flipElapsed = 0.0;
            mission?.EnterPhase(MissionPhase.RETRO_BURN);
        }

        // A tower catch is a narrow terminal corridor, not a long-range homing mode. Once
        // the vehicle is below the flip ceiling, a multi-kilometre miss cannot be repaired
        // by the remaining bounded thrust-vector authority. Divert early to the ordinary
        // legs path so the controller does not spend the last propellant on an impossible
        // catch and then hover or fall through the cradle.
        Vector3d currentCatchOffset = catchTargetPosition - vessel.Position;
        double currentCatchMiss = (currentCatchOffset
            - up * currentCatchOffset.Dot(up)).Magnitude;
        if (vessel.IsAttemptingTowerCatch
            && !_towerCatchAborted
            && _alt <= CatchEarlyAbortAltitudeM
            && currentCatchMiss > CatchEarlyAbortMissToleranceM)
        {
            _towerCatchAborted = true;
            vessel.IsAttemptingTowerCatch = false;
            GD.Print($"[EDL] early tower catch divert at {_alt:F0} m " +
                $"(miss={currentCatchMiss:F1} m) — continuing with leg landing");
        }

        switch (_phase)
        {
            case Edl.Entry:
                if (_heat > 4.0e7) { _phase = Edl.Peak; mission?.EnterPhase(MissionPhase.PEAK_HEATING); }
                break;
            case Edl.Peak:
                // Heating subsides once we've descended through the dense layer.
                if (_alt < atmoTop * 0.40) { _phase = Edl.Aero; mission?.EnterPhase(MissionPhase.AERO_DESCENT); }
                break;
            case Edl.Aero:
                break;   // retro ignition handled by the physics gate above
            case Edl.Retro:
                // Once the return is below 2.5 km, enter the staged terminal catch regime
                // while there is still enough time to close the measured position error before
                // the 300 m abort decision. The previous 1.5 km handoff left a 65 m residual
                // miss at the abort floor even though the synchronized velocity was stable.
                if (_alt < 2500.0
                    || (_towerCatchAborted
                        && !vessel.IsAttemptingTowerCatch
                        && _alt < 2_700.0
                        && vDown < 20.0))
                {
                    bool attemptCatch = vessel.IsAttemptingTowerCatch
                        && !_towerCatchAborted && vessel.HasCatchPins;
                    _phase = attemptCatch ? Edl.Catch : Edl.Final;
                    mission?.EnterPhase(MissionPhase.FINAL_DESCENT);
                }
                break;
            case Edl.Catch:
            {
                Vector3d offset = vessel.Position - catchTargetPosition;
                double missDistance = (offset - up * offset.Dot(up)).Magnitude;
                if (_alt < CatchAbortDecisionAltitudeM
                    && (missDistance > CatchAbortHorizontalMissToleranceM
                        || _horiz > CatchAbortHorizontalSpeedToleranceMps))
                {
                    // Diverting rather than forcing the catch: a miss this low and slow is
                    // exactly the case a real abort-to-legs guards against — see the EDL scope
                    // note in the Mechazilla plan. The vessel keeps whatever legs it has;
                    // a V3 ship built catch-only would still need its own legs data to survive
                    // this path, which is a known gap, not one this pass papers over.
                    _towerCatchAborted = true;
                    vessel.IsAttemptingTowerCatch = false;
                    _phase = Edl.Final;
                    body.GetGeodeticCoordinates(catchTargetPosition,
                        out double targetLatitude, out double targetLongitude, out _);
                    body.GetGeodeticCoordinates(vessel.Position,
                        out double vehicleLatitude, out double vehicleLongitude, out _);
                    Vector3d targetDelta = catchTargetPosition - vessel.Position;
                    GD.Print($"[EDL] tower catch aborted at {_alt:F0} m " +
                        $"(miss={missDistance:F1} m, horiz={_horiz:F1} m/s, " +
                        $"targetDelta={targetDelta}, " +
                        $"vehicleLatLon={vehicleLatitude:F5},{vehicleLongitude:F5}, " +
                        $"targetLatLon={targetLatitude:F5},{targetLongitude:F5}) " +
                        "— diverting to leg landing");
                    break;
                }
                if (vessel.IsCaught)
                {
                    _phase = Edl.Caught;
                    Caught(vessel, body, mission);
                    return;
                }
                break;
            }
            case Edl.Final:
                if (_alt < 500.0)
                {
                    _legsDeployed = true;
                    foreach (var gear in vessel.Parts.Parts.Where(
                                 p => p.Definition.Category == PartCategory.Landing))
                        gear.IsDeployed = true;
                }
                if (vessel.IsSurfaceSettled)
                {
                    _phase = Edl.Touchdown;
                    Touchdown(vessel, body, mission);
                    return;
                }
                break;
            case Edl.Caught:
            case Edl.Touchdown:
                return;
        }

        // ── Attitude: belly-flop in the aero phases, flip-and-burn for the descent ─
        // Entry/Peak/Aero: present the long axis broadside to the airflow (max drag,
        // heat-shield windward) to bleed velocity aerodynamically like real Starship.
        // Retro/Final: flip so the engines (local +Y thrust) point retrograde.
        Vector3d aimAxis;
        _aeroLiftReference = Vector3d.Zero;
        if (_phase is Edl.Entry or Edl.Peak or Edl.Aero)
        {
            // Fly a lift-up high-drag AoA instead of exact 90° broadside. Exact broadside has
            // CL=0 for a symmetric body and degenerates into a steep ballistic entry; the
            // nominal 70° target retains nearly all projected drag while generating
            // Starship-like L/D. Catch guidance may adjust it inside a bounded corridor.
            if (vessel.IsAttemptingTowerCatch && vessel.HasCatchPins)
            {
                // A catch return needs a bounded cross-range lift bias. A fixed down-lift
                // vector can enter the atmosphere safely yet miss the rotating tower by tens
                // of kilometres because small entry-state changes alter the ballistic ground
                // track. Project the target corridor into the lift plane and blend it with the
                // inward/downward bias; the normal aerodynamic integrator remains authoritative.
                Vector3d targetOffset = catchTargetPosition - vessel.Position;
                // Match the vehicle's surface-velocity frame. The cradle velocity is
                // inertial, while surfVel is relative to the body's local rotation at
                // the vehicle position. Subtract the cradle's local rotational velocity
                // before comparing the two; mixing these frames creates a false
                // cross-range command of several hundred metres per second.
                Vector3d targetSurfaceVelocity = vessel.CatchTargetVelocityWorld
                    - body.Velocity
                    - body.GetSurfaceVelocity(catchTargetPosition);
                Vector3d bodyDownLift = -(up - velDir * up.Dot(velDir));
                if (bodyDownLift.Magnitude > 1e-6)
                {
                    var prediction = EntryCorridorGuidance.Predict(
                        targetOffset,
                        surfVel,
                        targetSurfaceVelocity,
                        up,
                        _alt,
                        vDown,
                        g);
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
                        bool terminalCatchCorridor = _alt < 25_000.0;
                        double downrangeCorridorMeters = terminalCatchCorridor
                            ? System.Math.Clamp(_alt * 0.01, 5.0, 250.0)
                            : 20_000.0;
                        double downrangeAuthorityMeters = terminalCatchCorridor
                            ? System.Math.Clamp(_alt * 0.12, 1_500.0, 4_000.0)
                            : 180_000.0;
                        Vector3d guidedLift = EntryCorridorGuidance.SelectLiftDirection(
                            prediction,
                            bodyDownLift,
                            corridorMeters: 20_000.0,
                            authorityMeters: 180_000.0,
                            downrangeCorridorMeters: downrangeCorridorMeters,
                            downrangeAuthorityMeters: downrangeAuthorityMeters);
                        _aeroLiftReference = guidedLift;
                        aimAxis = AerodynamicsModel.ComputeEntryAxisForLift(
                            velDir, guidedLift);
                    }
                    else
                    {
                        _aeroLiftReference = bodyDownLift.Normalized;
                        aimAxis = AerodynamicsModel.ComputeLiftDownEntryAxis(
                            up, velDir);
                    }
                }
                else
                {
                    _aeroLiftReference = bodyDownLift.Normalized;
                    aimAxis = AerodynamicsModel.ComputeLiftDownEntryAxis(
                        up, velDir);
                }
            }
            else
            {
                _aeroLiftReference = (up - velDir * up.Dot(velDir)).Normalized;
                aimAxis = AerodynamicsModel.ComputeLiftUpEntryAxis(
                    up, velDir);
            }
        }
        else if (_phase == Edl.Catch
            || (_phase == Edl.Final && _horiz < 12.0
                && !(_towerCatchAborted && _alt <= TerminalVerticalPriorityAltitudeM)))
        {
            // Stay primarily upright but cant into the lateral error so the same thrust
            // command can actually remove it. A perfectly vertical axis cannot satisfy a
            // horizontal error and otherwise turns that error into an endless hover.
            // In the last 30 m above the feet, blend that cant back to vertical: arriving
            // tilted consumes suspension stroke geometrically before impact and overloads the
            // downhill foot even at a gentle vertical speed. A catch approach reuses the same
            // shape of blend even though it has no feet, so the final metres still arrive
            // upright into the cradle rather than canted.
            Vector3d lateralVelocity = _phase == Edl.Catch
                ? catchLateralVelocityError
                : surfVel - up * _vUp;
            const double contactDatumAlt = 7.85;
            double flareBlend = System.Math.Clamp(
                (_alt - contactDatumAlt) / 30.0, 0.0, 1.0);
            double tiltRatio = System.Math.Min(
                System.Math.Tan(20.0 * MathUtils.DEG_TO_RAD), lateralVelocity.Magnitude * 0.04)
                * flareBlend;
            aimAxis = lateralVelocity.Magnitude > 1e-3
                ? (up - lateralVelocity.Normalized * tiltRatio).Normalized
                : up;
        }
        else if (_phase == Edl.Retro && vessel.IsAttemptingTowerCatch && vessel.HasCatchPins)
        {
            // Keep retrograde braking as the primary command, but spend bounded thrust-vector
            // authority on a large lateral corridor error.  A pure retrograde burn preserves
            // the wrong ground track once aero descent has left a several-kilometre miss; by
            // the Catch phase there is not enough altitude to recover it.  The correction uses
            // the already synchronized target-relative velocity error and is capped at 15° so
            // it remains a controlled divert, not an attitude snap or direct position write.
            Vector3d retroAxis = -velDir;
            Vector3d lateralCorrection = -catchLateralVelocityError;
            lateralCorrection -= retroAxis * lateralCorrection.Dot(retroAxis);
            double catchMiss = (catchTargetPosition - vessel.Position
                - up * (catchTargetPosition - vessel.Position).Dot(up)).Magnitude;
            double lateralAngle = System.Math.Clamp(catchMiss / 5_000.0, 0.0, 15.0)
                * MathUtils.DEG_TO_RAD;
            aimAxis = lateralCorrection.Magnitude > 1e-3 && lateralAngle > 1e-6
                ? (retroAxis * System.Math.Cos(lateralAngle)
                    + lateralCorrection.Normalized * System.Math.Sin(lateralAngle)).Normalized
                : retroAxis;
        }
        else
        {
            // Once the catch has been diverted, the landing burn is a vertical leg landing,
            // not a generic retrograde burn. Using -velDir here becomes singular when the
            // vertical speed approaches zero: the target axis swings through the horizontal
            // velocity and the vehicle starts chasing its own attitude error. That oscillation
            // was the direct cause of the v22 propellant-starvation crash. Keep the thrust axis
            // upright and spend only bounded tilt authority on horizontal velocity damping.
            // Below the terminal gate, do not trade vertical landing margin for cross-range
            // correction. The v26 run reached 24.7 m with only 0.8 m/s down but 6.4 m/s total
            // speed because the lateral tilt kept injecting horizontal velocity. Landing gear
            // contact has no lateral catch mechanism, so a clean vertical release is safer and
            // more physical than chasing the remaining site offset at the last instant.
            if (_towerCatchAborted && _alt <= TerminalVerticalPriorityAltitudeM)
            {
                // Preserve vertical landing margin while gently bleeding residual lateral
                // velocity. The correction fades out before foot contact so it cannot inject a
                // late sideways component into the suspension.
                Vector3d terminalLateralVelocity = surfVel - up * _vUp;
                const double terminalContactDatumAlt = 7.85;
                double terminalHeight = System.Math.Max(0.0, _alt - terminalContactDatumAlt);
                double terminalBlend = System.Math.Clamp((terminalHeight - 1.0) / 15.0, 0.0, 1.0);
                const double TerminalMaxTiltDeg = 20.0;
                double terminalTilt = System.Math.Clamp(
                    terminalLateralVelocity.Magnitude * 0.20,
                    0.0,
                    System.Math.Tan(TerminalMaxTiltDeg * MathUtils.DEG_TO_RAD)) * terminalBlend;
                aimAxis = terminalLateralVelocity.Magnitude > 1e-3 && terminalTilt > 1e-6
                    ? (up - terminalLateralVelocity.Normalized * terminalTilt).Normalized
                    : up;
            }
            else
            {
                Vector3d landingLateralVelocity = surfVel - up * _vUp;
            const double MaxLandingTiltDeg = 15.0;
            double landingTilt = System.Math.Clamp(
                landingLateralVelocity.Magnitude * 0.04,
                0.0,
                System.Math.Tan(MaxLandingTiltDeg * MathUtils.DEG_TO_RAD));
            aimAxis = landingLateralVelocity.Magnitude > 1e-3
                ? (up - landingLateralVelocity.Normalized * landingTilt).Normalized
                : up;
            }
        }
        // In the aero phases pitch is not enough: roll the vehicle so the actual tiled
        // local -X belly faces the velocity vector. This keeps rendering, heating and drag
        // on the same physical side of the Ship. During the landing burn only the thrust
        // axis matters, so use the shortest rotation.
        bool aeroAttitude = _phase is Edl.Entry or Edl.Peak or Edl.Aero;
        Quaterniond desiredAttitude;
        if (aeroAttitude)
        {
            double filterDelta = System.Math.Clamp(delta, 0.0, 0.20);
            if (!_aeroReferenceInitialized)
            {
                // Seed from the physical vehicle state. The orbital return has already
                // prepared a belly-first attitude during coast; seeding from aimAxis here
                // would introduce an artificial 90–180° roll step when corridor guidance
                // selects a different lift side on the first atmospheric frame.
                _filteredAeroAxis = vessel.Orientation.Rotate(Vector3d.Up).Normalized;
                _filteredAeroFlow = velDir.Normalized;
                _filteredAeroAttitude = vessel.Orientation;
                _aeroReferenceInitialized = true;
            }
            else
            {
                _filteredAeroAxis = AttitudeGuidance.SmoothDirection(
                    _filteredAeroAxis, aimAxis, filterDelta, AeroReferenceTimeConstantSeconds);
                _filteredAeroFlow = AttitudeGuidance.SmoothDirection(
                    _filteredAeroFlow, velDir, filterDelta, AeroReferenceTimeConstantSeconds);
            }

            // Independent reference filters can change the mutual angle between the axis and
            // flow. Rebuild the axis from its filtered lift side so alpha remains the nominal
            // physical entry target instead of collapsing into a nose-first dive.
            var constrainedAeroAxis = AerodynamicsModel.ConstrainEntryAxisToAngle(
                _filteredAeroFlow, _filteredAeroAxis);
            var targetAeroAttitude = AerodynamicsModel.ComputeBellyFirstOrientation(
                constrainedAeroAxis, _filteredAeroFlow);
            var slewedAeroAttitude = AttitudeGuidance.SlewQuaternion(
                _filteredAeroAttitude,
                targetAeroAttitude,
                filterDelta,
                AeroReferenceSlewRateRadPerSecond);
            // Slerp smooths roll and lift-side changes, but its intermediate axis can
            // temporarily leave the nominal entry cone. Re-project that axis before the
            // command is published; otherwise a corridor correction silently turns the
            // filtered reference into a nose-first dive even while attitude error is small.
            _filteredAeroAttitude = AerodynamicsModel.ConstrainBellyFirstOrientationToAngle(
                slewedAeroAttitude, _filteredAeroFlow);
            desiredAttitude = _filteredAeroAttitude;
        }
        else
        {
            _aeroReferenceInitialized = false;
            desiredAttitude = ShortestArc(Vector3d.Up, aimAxis);
        }
        bool catchAero = vessel.IsAttemptingTowerCatch && vessel.HasCatchPins
            && _phase is Edl.Entry or Edl.Peak or Edl.Aero;
        bool catchDemoAttitude = vessel.IsTowerCatchDemonstration
            && (_phase is Edl.Entry or Edl.Peak or Edl.Aero or Edl.Retro or Edl.Catch);
        _aeroAttitudeCommand = _phase is Edl.Entry or Edl.Peak or Edl.Aero
            ? AttitudeGuidance.ComputeCommand(
                vessel.Orientation,
                desiredAttitude,
                vessel.AngularVelocity,
                proportionalGain: catchAero ? 5.5 : 2.6,
                dampingGain: catchAero ? 2.4 : 1.2,
                allowRoll: true)
            : AttitudeGuidance.ComputeAxisPointingCommand(
                vessel.Orientation,
                Vector3d.Up,
                aimAxis,
                vessel.AngularVelocity,
                proportionalGain: 2.2,
                dampingGain: 6.0);
        vessel.PitchYawRoll = _aeroAttitudeCommand;
        _attitudeErrorDeg = AttitudeGuidance.ErrorAngleRadians(
            vessel.Orientation, desiredAttitude) * MathUtils.RAD_TO_DEG;

        if (aeroAttitude && velDir.MagnitudeSquared > 1e-12)
        {
            var actualAxis = vessel.Orientation.Rotate(Vector3d.Up).Normalized;
            _aeroAngleOfAttackDeg = System.Math.Acos(System.Math.Clamp(
                actualAxis.Dot(velDir), -1.0, 1.0)) * MathUtils.RAD_TO_DEG;
            _aeroWindwardFactor = ThermalModel.WindwardFactor(
                vessel.Orientation.Inverse().Rotate(velDir));
        }
        else
        {
            _aeroAngleOfAttackDeg = 0.0;
            _aeroWindwardFactor = 0.0;
        }

        if (vessel.IsTowerCatchDemonstration && catchDemoAttitude)
        {
            // This is deliberately scoped to the scripted HUD demonstration. It keeps the
            // presentation-only broadside/retro/catch attitude transitions from turning the
            // minimum-throttle guidance into an uncommanded cross-range miss. The real
            // translational drag, propulsion, catch-pin contact and settle solver continue to
            // run normally; manual and unscripted reentries retain the physical controller.
            vessel.Orientation = desiredAttitude;
            vessel.AngularVelocity = Vector3d.Zero;
            vessel.PitchYawRoll = Vector3d.Zero;
        }

        if (_flipInProgress)
        {
            _flipElapsed += delta;
            if (_attitudeErrorDeg < 5.0)
            {
                _flipInProgress = false;
                GD.Print($"[EDL] physical flip complete in {_flipElapsed:F1}s");
            }
        }

        // ── Throttle: closed-loop descent-rate profile to a soft touchdown ──────
        // By the time we flip (low, post-belly-flop) the velocity is mostly vertical. Track a
        // target descent rate that follows a constant-deceleration profile easing to 1.2 m/s at
        // first foot contact. Reserve braking authority (use 60%
        // of thrust for the profile) so the closed loop has headroom and the engine spool can keep
        // up — the old minimum-energy "stop exactly at the ground" burn commanded almost no thrust
        // until the last instant and touched down hot.
        if (_phase is Edl.Retro or Edl.Catch or Edl.Final)
        {
            // Once a foot has touched, commit to the compliant gear: shut the engines down
            // and let spring/damper/friction settle the body. Relighting against a loaded foot
            // would hide the contact dynamics and can overload one leg.
            if (_phase == Edl.Final && vessel.HasSurfaceContact)
                _landingCutoffCommitted = true;
            if (_phase == Edl.Final && _landingCutoffCommitted)
            {
                vessel.Throttle = 0.0;
                vessel.PitchYawRoll = Vector3d.Zero;
                shipEngines?.SelectEngineCount(0);
                return;
            }

            // The Raptor minimum throttle is still a substantial landing impulse. After an
            // abort-to-legs, the closed-loop profile can cross zero vertical speed at the apex;
            // immediately commanding the floor again then causes a shutdown/restart chatter
            // and consumes the finite engine restart budget. Latch a single coast segment above
            // the low restart gate, then relight once on the way down for the final burn.
            bool fallbackLegLanding = _towerCatchAborted && !vessel.IsAttemptingTowerCatch;
            if (fallbackLegLanding)
            {
                // Leave enough altitude for one full guidance frame plus the three-engine
                // arrest. At 1,200 m the llvmpipe E2E cadence could cross the entire handoff
                // window and relight only at ~172 m with ~135 m/s down; 2,500 m preserves a
                // physically recoverable burn corridor without changing the orbital seed.
                const double LowRelightAltitudeM = 2_500.0;
                const double CoastEntryVerticalSpeedMps = 0.5;
                // Coast is a one-shot handoff. Once the landing cluster has relit, never
                // re-enter coast: a second shutdown would consume the finite Raptor restart
                // budget while the vehicle is already committed to its terminal burn.
                if (!_landingBurnCoast && !_landingBurnRelit && _phase == Edl.Retro
                    && _vUp >= -CoastEntryVerticalSpeedMps
                    && _alt > LowRelightAltitudeM)
                {
                    _landingBurnCoast = true;
                    GD.Print($"[EDL] fallback landing coast entered alt={_alt:F0}m vUp={_vUp:F1}m/s");
                }

                if (_landingBurnCoast)
                {
                    if (_alt > LowRelightAltitudeM || _vUp >= 0.0)
                    {
                        vessel.Throttle = 0.0;
                        vessel.PitchYawRoll = Vector3d.Zero;
                        shipEngines?.SelectEngineCount(0);
                        return;
                    }

                    _landingBurnCoast = false;
                    _landingBurnRelit = true;
                    GD.Print($"[EDL] fallback landing burn relit alt={_alt:F0}m vUp={_vUp:F1}m/s");
                }
            }

            const double contactDatumAlt = 7.85; // 7.50 m leg offset + 0.35 m foot radius
            const double touchdownRate = 1.20;
            // A catch approach's "ground" is the cradle's height up the tower, not the planet
            // surface below it — the descent profile must arrest at the arms, not fly through
            // them toward the ground the tower stands on.
            double effectiveContactDatumAlt = _phase == Edl.Catch
                ? body.GetAltitude(catchTargetPosition)
                    - vessel.CatchContactPoints
                        .Select(point => point.LocalPositionFromDatum.Y)
                        .DefaultIfEmpty(0.0)
                        .Max()
                : contactDatumAlt;
            // Target descent rate: a gentle LINEAR profile that eases to 1.2 m/s at first
            // physical foot contact and
            // is already below the post-belly-flop terminal velocity (~70 m/s) at the flip, so the
            // burn starts braking immediately. Cap it by a constant-deceleration limit so a faster
            // arrival is still braked hard enough. Close the loop with gravity feed-forward.
            double heightToContact = System.Math.Max(0.0, _alt - effectiveContactDatumAlt);
            double vTargetLin = touchdownRate + heightToContact * 0.035;
            double vTargetMax = System.Math.Sqrt(2.0 * 0.60 * aThrustFull * heightToContact)
                + touchdownRate;
            double vTarget    = System.Math.Min(vTargetLin, vTargetMax);
            double horizontalTarget = System.Math.Max(0.5, _alt * 0.02);
            double verticalError = vDown - vTarget;
            double horizontalError = _phase == Edl.Catch
                ? catchLateralVelocityError.Magnitude
                : _horiz - horizontalTarget;
            double coupledHorizontalError = _phase is Edl.Retro or Edl.Catch ? horizontalError : 0.0;
            // Divide by the commanded thrust axis, not by -velocity. In final vertical flight
            // the velocity can pass through zero while the engine remains upright; using
            // -velocity there creates a singular 5 g command and launches the vehicle upward.
            double thrustUpComponent = System.Math.Max(0.20, aimAxis.Dot(up));
            // Engines with a substantial minimum throttle cannot hover gently while the
            // vehicle is below its target descent rate. Coast through that part of the
            // profile, then light only once braking is actually required. This prevents the
            // catch path from hovering hundreds of metres above the cradle and falling through
            // the 500 m contact-evaluation gate too fast for the pins to capture it.
            double aCmd = PoweredLandingGuidance.ComputeAccelerationCommand(
                g, thrustUpComponent, verticalError, coupledHorizontalError, _vUp);

            // A single Raptor cannot command below its physical minimum throttle. Near the
            // cradle that minimum is enough to hover a catch-only Ship a few decimetres above
            // the pin plane indefinitely (the observed ~52 m FINAL_DESCENT plateau). Once the
            // approach is centred and inside the last 1.5 m of the contact datum, release the
            // engine for the short final drop; the pin springs then absorb the bounded impact.
            // This is a narrow physical hand-off, not a scripted catch or teleport.
            bool finalCatchRelease = _phase == Edl.Catch
                && heightToContact <= 1.5
                && vDown < 0.65
                && horizontalError <= 2.0;
            if (finalCatchRelease)
            {
                shipEngines?.SelectEngineCount(0);
                vessel.Throttle = 0.0;
                _landingEngineCount = 0;
                return;
            }

            // A normal leg landing has no pin solver to announce contact before the feet touch.
            // Once the vehicle is inside the final 12 m and already below the contract's soft
            // touchdown speed, close the engines and let the contact integrator settle it. This
            // prevents a minimum-throttle Raptor from hovering just above the surface.
            Vector3d landingAxis = vessel.Orientation.Rotate(Vector3d.Up).Normalized;
            double uprightAlignment = landingAxis.Dot(up);
            bool finalSurfaceRelease = _phase == Edl.Final
                && heightToContact <= FinalSurfaceReleaseAltitudeM
                && surfVel.Magnitude <= FinalSurfaceReleaseSpeedMps
                && vessel.AngularVelocity.Magnitude <= FinalSurfaceReleaseAngularRateRadS
                && uprightAlignment >= System.Math.Cos(8.0 * MathUtils.DEG_TO_RAD);
            if (finalSurfaceRelease)
            {
                _landingCutoffCommitted = true;
                shipEngines?.SelectEngineCount(0);
                vessel.Throttle = 0.0;
                vessel.PitchYawRoll = Vector3d.Zero;
                _landingEngineCount = 0;
                GD.Print($"[EDL] final surface release alt={_alt:F2}m vDown={vDown:F2}m/s "
                    + $"surfaceSpeed={surfVel.Magnitude:F2}m/s "
                    + $"omega={vessel.AngularVelocity.Magnitude:F3}rad/s upright={uprightAlignment:F3}");
                return;
            }

            bool alignedForBurn = vessel.Orientation.Rotate(Vector3d.Up).Normalized.Dot(aimAxis)
                > System.Math.Cos(15.0 * MathUtils.DEG_TO_RAD);
            if (!alignedForBurn && _phase == Edl.Retro)
            {
                // Flight-proven Starship sequence: ignite three centre Raptors as
                // the flip begins, then let gimbal authority rotate the vehicle.
                shipEngines?.SelectEngineCount(3);
                vessel.Throttle = shipEngines?.ApplyThrottleFloor(flipIgnitionThrottle)
                    ?? flipIgnitionThrottle;
            }
            else
            {
                CommandLandingEngines(vessel, body, shipEngines, aCmd, mass);
            }
        }
        else
        {
            vessel.Throttle = 0.0;                          // unpowered aero entry
        }
    }

    private void Touchdown(Vessel vessel, CelestialBody body, MissionManager? mission)
    {
        vessel.Throttle = 0.0;
        vessel.PitchYawRoll = Vector3d.Zero;
        mission?.EnterPhase(MissionPhase.LANDED);
        GD.Print($"[EDL] TOUCHDOWN settled on {body.Name}  vUp={_vUp:F1} m/s " +
            $"contacts={vessel.LastSurfaceContact?.ContactCount ?? 0}");
    }

    private void Caught(Vessel vessel, CelestialBody body, MissionManager? mission)
    {
        vessel.Throttle = 0.0;
        vessel.PitchYawRoll = Vector3d.Zero;
        mission?.EnterPhase(MissionPhase.CAUGHT);
        GD.Print($"[EDL] CAUGHT by the tower at {_bodyName}  vUp={_vUp:F1} m/s " +
            $"contacts={vessel.LastCatchContact?.ContactCount ?? 0}");
    }

    private void Deactivate()
    {
        if (_phase != Edl.Inactive)
        {
            var vessel = SimulationBridge.Instance?.ActiveVessel;
            foreach (var engine in vessel?.Parts.Parts.Where(
                         p => p.Definition.Category == PartCategory.Engine)
                     ?? Enumerable.Empty<Part>())
                engine.SelectEngineCount(System.Math.Max(1, engine.Definition.EngineCount));
            foreach (var gear in vessel?.Parts.Parts.Where(
                         p => p.Definition.Category == PartCategory.Landing)
                     ?? Enumerable.Empty<Part>())
                gear.IsDeployed = false;
            if (vessel != null)
            {
                vessel.Throttle = 0.0;
                vessel.PitchYawRoll = Vector3d.Zero;
            }
            _phase = Edl.Inactive;
            _flipInProgress = false;
            _landingCutoffCommitted = false;
            _landingBurnCoast = false;
            _landingBurnRelit = false;
            _landingEngineCount = 0;
            _aeroReferenceInitialized = false;
            Visible = false;
        }
    }


    // ── HUD overlay ─────────────────────────────────────────────────────────────

    public override void _Draw()
    {
        if (_phase == Edl.Inactive) return;
        var vp = GetViewportRect().Size;

        // Plasma vignette during high heating.
        if (_phase is Edl.Entry or Edl.Peak)
        {
            float intensity = (float)System.Math.Clamp(_heat / 8.0e7, 0.0, 1.0);
            DrawPlasma(vp, intensity);
        }

        var layout = EdlOverlayLayout.Build(vp);
        DrawAltimeter(layout);
        DrawTelemetry(layout);

        // The aero phases are the ones that can burn the vehicle; below them the panel would
        // just be noise on a descent that is already thermally over.
        if (_phase is Edl.Entry or Edl.Peak or Edl.Aero)
            DrawThermal(layout);

        if (_blackout)
            DrawBlackoutBanner(vp);
    }

    private void DrawPlasma(Vector2 vp, float k)
    {
        // Layered translucent bands from screen edge — brighter at the bottom (windward).
        var hot = new Color(1.0f, 0.45f, 0.12f);
        int bands = 7;
        for (int i = 0; i < bands; i++)
        {
            float t = i / (float)(bands - 1);
            float thick = vp.Y * 0.10f * (1f - t);
            float a = k * 0.08f * (1f - t);
            DrawRect(new Rect2(0, vp.Y - thick, vp.X, thick), new Color(hot, a));   // bottom glow
            DrawRect(new Rect2(0, 0, vp.X, thick * 0.5f), new Color(hot, a * 0.4f)); // top
        }
    }

    /// <summary>
    /// Screen-space reservation for the EDL readouts.  The columns intentionally do not
    /// share x coordinates: telemetry (including HIGH G) occupies the middle column and
    /// thermal occupies the next column.  Keeping the reservation in one value object makes
    /// it impossible for a later readout adjustment to silently reintroduce an overlap.
    /// </summary>
    private readonly struct EdlOverlayLayout
    {
        public readonly float Scale;
        public readonly Rect2 AltimeterRect;
        public readonly Rect2 TelemetryRect;
        public readonly Rect2 ThermalRect;
        public readonly Vector2 TelemetryOrigin;
        public readonly Vector2 GLoadOrigin;
        public readonly Vector2 HighGOrigin;

        private EdlOverlayLayout(
            float scale,
            Rect2 altimeterRect,
            Rect2 telemetryRect,
            Rect2 thermalRect,
            Vector2 telemetryOrigin,
            Vector2 gLoadOrigin,
            Vector2 highGOrigin)
        {
            Scale = scale;
            AltimeterRect = altimeterRect;
            TelemetryRect = telemetryRect;
            ThermalRect = thermalRect;
            TelemetryOrigin = telemetryOrigin;
            GLoadOrigin = gLoadOrigin;
            HighGOrigin = highGOrigin;
        }

        public static EdlOverlayLayout Build(Vector2 viewport)
        {
            // The reference layout is authored at 1280x720.  Growing every panel to 1.4x
            // on a 1920x1080 framebuffer moved THERMAL over the vehicle in the EDL captures.
            // Keep the readable reference size on larger viewports; this is a HUD-only
            // composition bound and does not affect simulation resolution or timing.
            float scale = Mathf.Clamp(
                Mathf.Min(viewport.X / 1280f, viewport.Y / 720f), 0.85f, 1.00f);
            float margin = 56f * scale;

            // Side rails reserve the center for the vehicle. At 720p telemetry
            // ends at y=370, above the attitude cluster; thermal hugs the right edge.
            float top = 180f * scale;
            Rect2 altimeter = new(
                new Vector2(margin, top),
                new Vector2(30f * scale, 190f * scale));
            Rect2 telemetry = new(
                new Vector2(margin + 120f * scale, top),
                new Vector2(230f * scale, 190f * scale));
            Rect2 thermal = new(
                new Vector2(viewport.X - 266f * scale, top),
                new Vector2(250f * scale, 250f * scale));

            Vector2 telemetryOrigin = telemetry.Position + new Vector2(12f, 28f) * scale;
            Vector2 gLoadOrigin = telemetryOrigin + new Vector2(0f, 104f) * scale;
            Vector2 highGOrigin = gLoadOrigin + new Vector2(138f, 20f) * scale;
            return new EdlOverlayLayout(
                scale, altimeter, telemetry, thermal,
                telemetryOrigin, gLoadOrigin, highGOrigin);
        }
    }

    private void DrawAltimeter(EdlOverlayLayout layout)
    {
        float maxAlt = AltimeterMaxAltitude(_phase);
        // Entry may start above 70 km. Keep the marker on-scale without altering
        // the underlying altitude or the tighter landing reference range.
        maxAlt = Mathf.Max(maxAlt, Mathf.Ceil((float)_alt / 10_000f) * 10_000f);
        Rect2 rail = layout.AltimeterRect;
        float x = rail.Position.X;
        float top = rail.Position.Y;
        float h = rail.Size.Y;
        float w = rail.Size.X;
        DrawRect(new Rect2(x, top, w, h), new Color(0.05f, 0.07f, 0.10f, 0.75f));
        DrawRect(new Rect2(x, top, w, h), new Color(0.45f, 0.65f, 0.95f, 0.6f), false, 1.4f);

        float frac = (float)System.Math.Clamp(_alt / maxAlt, 0, 1);
        float markY = top + h * (1f - frac);
        var col = _legsDeployed ? new Color(0.45f, 1f, 0.6f) : new Color(1f, 0.8f, 0.25f);
        DrawRect(new Rect2(x - 5, markY - 2, w + 10, 4), col);
        Text(FormatAltitudeReadout(_alt),
            new Vector2(x + w + 10f * layout.Scale, markY + 5f), col, 16);

        // Five fixed ticks keep the rail readable at both the 70 km entry scale and the
        // 5 km landing scale without changing the physical altitude readout.
        for (int i = 0; i <= 5; i++)
        {
            float ty = top + h * (i / 5f);
            DrawLine(new Vector2(x, ty), new Vector2(x + 6, ty), new Color(0.5f, 0.6f, 0.7f, 0.7f), 1f);
            float tickAltitude = maxAlt * (1f - i / 5f);
            Text(FormatAltitudeTick(tickAltitude),
                new Vector2(x - 48f * layout.Scale, ty + 5f),
                new Color(0.55f, 0.62f, 0.72f), 11);
        }
    }

    private static float AltimeterMaxAltitude(Edl phase) =>
        phase is Edl.Entry or Edl.Peak or Edl.Aero ? 70_000f : 5_000f;

    private static string FormatAltitudeReadout(double altitude) =>
        altitude >= 10_000.0 ? $"{altitude / 1000.0:F1} km" : $"{altitude:F0} m";

    private static string FormatAltitudeTick(float altitude) =>
        altitude >= 10_000f ? $"{altitude / 1000f:F0} km" : $"{altitude:F0} m";

    /// <summary>
    /// Reads the same thermal state the simulation is actually integrating, so the crew sees
    /// the numbers that decide their survival rather than a decorative gauge.
    /// </summary>
    private void RefreshThermalState(
        Vessel vessel, CelestialBody body, double density, double speed, Vector3d surfVel)
    {
        _fluxNow = vessel.ComputeStagnationHeatFlux(density, surfVel);

        var flowLocal = speed > 1e-6
            ? vessel.Orientation.Inverse().Rotate(surfVel.Normalized)
            : Vector3d.Zero;
        _shieldAlign = ThermalModel.WindwardFactor(flowLocal);

        _skinTemp = 0.0;
        _hullRatio = 0.0;
        _thermalDamage = 0.0;

        foreach (var part in vessel.Parts.Parts)
        {
            // Only the tiled parts say anything about the heat shield. The engine cluster
            // carries no tiles by design and runs hot without meaning anything is wrong.
            if (!part.Definition.HasHeatShield) continue;

            if (part.SkinTemperature > _skinTemp)   _skinTemp = part.SkinTemperature;
            if (part.ThermalRatio    > _hullRatio)  _hullRatio = part.ThermalRatio;
            if (part.ThermalDamage   > _thermalDamage) _thermalDamage = part.ThermalDamage;
        }
    }

    private void DrawThermal(EdlOverlayLayout layout)
    {
        // Thermal owns a separate column to the right of telemetry.  Its bounds are
        // reserved by EdlOverlayLayout so the warning cannot collide with HIGH G.
        Rect2 panel = layout.ThermalRect;
        float scale = layout.Scale;
        float px = panel.Position.X + 12f * scale;
        float py = panel.Position.Y + 50f * scale;

        DrawRect(panel, new Color(0.04f, 0.06f, 0.09f, 0.78f));
        DrawRect(panel, new Color(0.45f, 0.65f, 0.95f, 0.35f), false, 1.2f);
        Text("THERMAL", new Vector2(panel.Position.X + 8f * scale,
            panel.Position.Y + 20f * scale), new Color(0.55f, 0.68f, 0.85f), 13);

        float x = px, y = py + 14f * scale;
        var label = new Color(0.6f, 0.7f, 0.82f);

        Text("TPS FACE", new Vector2(x, y), label, 13);
        Text($"{_skinTemp:F0} K", new Vector2(x, y + 20f * scale),
            _skinTemp > 1200 ? new Color(1f, 0.62f, 0.25f) : new Color(0.9f, 0.95f, 1f), 20);

        // The hull bar is the one that matters: at 1.0 the structure is failing.
        Text("HULL", new Vector2(x, y + 52f * scale), label, 13);
        float ratio = (float)System.Math.Clamp(_hullRatio, 0.0, 1.2);
        Color hullCol = _hullRatio > 0.9 ? new Color(1f, 0.3f, 0.25f)
                      : _hullRatio > 0.65 ? new Color(1f, 0.8f, 0.3f)
                      : new Color(0.45f, 1f, 0.6f);

        float barW = 130f * scale, barH = 12f * scale;
        DrawRect(new Rect2(x, y + 60f * scale, barW, barH), new Color(0.05f, 0.07f, 0.10f, 0.8f));
        DrawRect(new Rect2(x, y + 60f * scale, barW * ratio / 1.2f, barH), hullCol);
        DrawRect(new Rect2(x, y + 60f * scale, barW, barH), new Color(0.45f, 0.65f, 0.95f, 0.5f), false, 1.2f);
        Text($"{_hullRatio * 100.0:F0}%", new Vector2(x + barW + 10f * scale, y + 71f * scale), hullCol, 16);

        // Shield alignment is the ACTIONABLE number — it is the one the pilot can fix.
        Text("SHIELD", new Vector2(x, y + 92f * scale), label, 13);
        Color alignCol = _shieldAlign > 0.85 ? new Color(0.45f, 1f, 0.6f)
                       : _shieldAlign > 0.5  ? new Color(1f, 0.8f, 0.3f)
                       : new Color(1f, 0.3f, 0.25f);
        Text($"{_shieldAlign * 100.0:F0}%", new Vector2(x, y + 112f * scale), alignCol, 20);

        // Only shout when it actually matters: a shield off the flow with real heat behind it.
        if (_shieldAlign < 0.7 && _fluxNow > 5.0e4)
            Text("SHIELD OFF FLOW", new Vector2(x, y + 140f * scale), new Color(1f, 0.3f, 0.25f), 18);

        if (_thermalDamage > 0.0)
            Text($"TPS DAMAGE {_thermalDamage * 100.0:F0}%",
                new Vector2(x, y + 164f * scale), new Color(1f, 0.45f, 0.3f), 16);
    }

    private void DrawTelemetry(EdlOverlayLayout layout)
    {
        DrawRect(layout.TelemetryRect, new Color(0.04f, 0.06f, 0.09f, 0.56f));
        DrawRect(layout.TelemetryRect, new Color(0.45f, 0.65f, 0.95f, 0.30f), false, 1.2f);

        float x = layout.TelemetryOrigin.X;
        float y = layout.TelemetryOrigin.Y;
        float scale = layout.Scale;
        double vDown = -_vUp;
        Color vsCol = System.Math.Abs(vDown) > 50 ? new Color(1f, 0.35f, 0.3f)
                    : System.Math.Abs(vDown) > 10 ? new Color(1f, 0.82f, 0.3f)
                    : new Color(0.4f, 1f, 0.5f);
        Text("VERTICAL (DOWN +)", new Vector2(x, y), new Color(0.6f, 0.7f, 0.82f), 13);
        Text($"{vDown:+0;-0} m/s", new Vector2(x, y + 20f * scale), vsCol, 22);
        Text("HORIZONTAL", new Vector2(x, y + 52f * scale), new Color(0.6f, 0.7f, 0.82f), 13);
        Text($"{_horiz:F0} m/s", new Vector2(x, y + 72f * scale), new Color(0.9f, 0.95f, 1f), 20);
        DrawGLoad(layout.GLoadOrigin, layout);
    }

    /// <summary>
    /// Instantaneous load, the held peak, and a bar that escalates through the bands a real
    /// entry walks up. The 4–5 g wall is the whole point, so the bar is scaled to
    /// <see cref="EntryLoadTracker.SevereG"/> and switches to the shared Warning / Alert
    /// tokens exactly at the band edges instead of fading continuously.
    /// </summary>
    private void DrawGLoad(Vector2 origin, EdlOverlayLayout layout)
    {
        float x = origin.X, y = origin.Y;
        float scale = layout.Scale;
        var label = new Color(0.6f, 0.7f, 0.82f);
        Color gCol = BandColor(_load.Band);

        Text("G-FORCE", new Vector2(x, y), label, 13);
        Text($"{_gForce:F1} g", new Vector2(x, y + 20f * scale), gCol, 20);

        // Escalation bar: full scale is the severe band, with a tick at the 4 g wall.
        float barW = 130f * scale, barH = 8f * scale;
        float barY = y + 28f * scale;
        float frac = (float)System.Math.Clamp(_gForce / EntryLoadTracker.SevereG, 0.0, 1.0);
        DrawRect(new Rect2(x, barY, barW, barH), new Color(0.05f, 0.07f, 0.10f, 0.8f));
        DrawRect(new Rect2(x, barY, barW * frac, barH), gCol);
        float wallX = x + barW * (float)(EntryLoadTracker.HighG / EntryLoadTracker.SevereG);
        DrawLine(new Vector2(wallX, barY - 2f * scale), new Vector2(wallX, barY + barH + 2f * scale),
            InterfaceTheme.Alert, 1.4f);
        DrawRect(new Rect2(x, barY, barW, barH), new Color(0.45f, 0.65f, 0.95f, 0.45f), false, 1.1f);

        // The held peak is what the crew debriefs on, so it never disappears once recorded.
        if (_load.HasSample && _load.PeakG > 0.05)
        {
            Color peakCol = BandColor(_load.PeakBand);
            Text($"PEAK {_load.PeakG:F1} g",
                new Vector2(x, barY + barH + 16f * scale), peakCol, 15);
            // Only shout while the wall is actually being felt.
            if (_load.Band >= GLoadBand.High)
                Text("HIGH G", layout.HighGOrigin, InterfaceTheme.Alert, 15);
        }
    }

    private static Color BandColor(GLoadBand band) => band switch
    {
        GLoadBand.Severe => InterfaceTheme.Alert,
        GLoadBand.High => InterfaceTheme.Alert,
        GLoadBand.Elevated => InterfaceTheme.Warning,
        _ => InterfaceTheme.Text,
    };

    /// <summary>
    /// Comms blackout callout. The state itself is owned by the simulation
    /// (<c>CommsSystem.PlasmaBlackout</c>); this only renders it, centred low on the screen
    /// so it stays clear of the phase/telemetry blocks the rest of the HUD owns.
    /// </summary>
    private void DrawBlackoutBanner(Vector2 vp)
    {
        const string headline = "SIGNAL LOST — PLASMA BLACKOUT";
        string sub = $"IONISED SHEATH · {_blackoutSeconds:F0}s ELAPSED";
        const string recovery = "Signal returns as speed decreases";

        var headSize = _font.GetStringSize(headline, HorizontalAlignment.Center, -1, 18);
        var subSize = _font.GetStringSize(sub, HorizontalAlignment.Center, -1, 12);
        var recoverySize = _font.GetStringSize(recovery, HorizontalAlignment.Center, -1, 12);
        // Right half of the lower lane, clear of the complete left attitude cluster.
        float cx = vp.X * 0.75f;
        float top = vp.Y - 182f;

        var panel = new Rect2(cx - headSize.X * 0.5f - 16f, top - 22f,
            headSize.X + 32f, 64f);
        DrawRect(panel, new Color(0.04f, 0.06f, 0.09f, 0.72f));
        DrawRect(panel, new Color(InterfaceTheme.Alert, 0.55f), false, 1.2f);

        DrawString(_font, new Vector2(cx - headSize.X * 0.5f, top), headline,
            HorizontalAlignment.Left, -1, 18, InterfaceTheme.Alert);
        DrawString(_font, new Vector2(cx - subSize.X * 0.5f, top + 17f), sub,
            HorizontalAlignment.Left, -1, 12, InterfaceTheme.TextMuted);
        DrawString(_font, new Vector2(cx - recoverySize.X * 0.5f, top + 33f), recovery,
            HorizontalAlignment.Left, -1, 12, InterfaceTheme.TextMuted);
    }

    /// <summary>
    /// UX-014: the EDL phase title duplicated the HUD's mission-phase banner, so this
    /// controller no longer draws one. It publishes the EDL-only remainder and
    /// <see cref="HUDController"/> renders it inside the single banner.
    /// </summary>
    public string? BannerStatus => _phase == Edl.Inactive
        ? null
        : $"EDL · {_bodyName.ToUpperInvariant()}" + (_legsDeployed ? "  ·  LEGS DOWN" : "");

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private void Text(string s, Vector2 pos, Color c, int size) =>
        DrawString(_font, pos, s, HorizontalAlignment.Left, -1, size, c);

    private static double MaxThrustAccel(Vessel vessel, CelestialBody body, double mass)
    {
        if (mass <= 0) return 0;
        return vessel.GetMaximumThrust(body) / mass;
    }

    private static double MaxLandingThrustAccel(
        Vessel vessel, CelestialBody body, Part? engineCluster, double mass)
    {
        if (mass <= 0.0) return 0.0;
        if (engineCluster == null) return MaxThrustAccel(vessel, body, mass);
        int represented = System.Math.Max(1, engineCluster.Definition.EngineCount);
        int landingCount = System.Math.Min(3, represented);
        double rated = RatedClusterThrust(engineCluster, vessel.GetAmbientPressure(body));
        return rated * landingCount / represented / mass;
    }

    /// <summary>
    /// Returns nominal cluster thrust for guidance timing, independent of current chamber
    /// pressure and selected-engine runtime state. The runtime model is authoritative when
    /// resolved; the legacy part rating is a compatibility fallback for a staged vessel whose
    /// engine instances have not been hydrated yet. A zero throttle/empty chamber must never
    /// collapse the flip gate to altitude zero.
    /// </summary>
    private static double RatedClusterThrust(Part engineCluster, double ambientPressure)
    {
        double rated = engineCluster.GetRatedFullThrottleThrustMagnitude(ambientPressure);
        if (rated > 0.0 && double.IsFinite(rated)) return rated;

        double pressureFraction = System.Math.Clamp(
            ambientPressure / 101_325.0, 0.0, 1.0);
        double legacyRated = engineCluster.Definition.ThrustVac
            + (engineCluster.Definition.ThrustSL - engineCluster.Definition.ThrustVac)
                * pressureFraction;
        return double.IsFinite(legacyRated) ? System.Math.Max(0.0, legacyRated) : 0.0;
    }

    private void CommandLandingEngines(
        Vessel vessel, CelestialBody body, Part? engineCluster, double accelerationCmd, double mass)
    {
        if (engineCluster == null || mass <= 0.0)
        {
            vessel.Throttle = 0.0;
            return;
        }

        int represented = System.Math.Max(1, engineCluster.Definition.EngineCount);
        int maxLandingEngines = System.Math.Min(3, represented);
        double ratedCluster = RatedClusterThrust(
            engineCluster, vessel.GetAmbientPressure(body));
        double perEngine = ratedCluster / represented;
        double desiredThrust = System.Math.Max(0.0, accelerationCmd * mass);
        if (perEngine <= 1.0)
        {
            engineCluster.SelectEngineCount(0);
            vessel.Throttle = 0.0;
            return;
        }

        // Once a Starship fallback leg has been committed, do not issue a transient zero command
        // merely because the profile briefly crosses the minimum-throttle deadband. The abort can
        // happen after the normal coast/relight handoff, so tying this latch to the relight flag
        // would still allow an unintended shutdown in the terminal corridor. Raptor shutdown /
        // restart is not continuous thrust modulation; the surface-release gate remains the only
        // way to end this committed burn.
        bool committedStarshipBurn = _towerCatchAborted
            && !vessel.IsAttemptingTowerCatch
            && engineCluster.Definition.IsStarshipFamily
            && engineCluster.Definition.HasVehicleRole("ship_engines");
        if (desiredThrust <= 1.0 && !committedStarshipBurn)
        {
            engineCluster.SelectEngineCount(0);
            vessel.Throttle = 0.0;
            return;
        }

        int requested = maxLandingEngines;
        for (int count = 1; count <= maxLandingEngines; count++)
        {
            if (desiredThrust <= perEngine * count)
            {
                requested = count;
                break;
            }
        }

        // A landing burn is a hysteretic engine-count sequence, not a bank that may chatter on
        // and off with every guidance correction. Start all three centre Raptors during the flip.
        // A candidate count must retain a real hover margin; the previous monotonic 3→2→1 rule
        // could select one engine while the demand was low and then never restore it when the
        // vehicle arrived hot. That left the fallback with ~2 MN against a multi-meganewton
        // vehicle weight and produced the observed 21 m/s ground impact.
        double hoverThrust = System.Math.Max(0.0, mass * body.GetSurfaceGravity() * MinimumLandingTwr);
        int minimumSafeEngines = System.Math.Clamp(
            (int)System.Math.Ceiling(hoverThrust / perEngine), 1, maxLandingEngines);
        // Starship's landing burn is the three-engine centre cluster. Keep those engines
        // selected throughout a real Starship EDL sequence; using the nominal full-throttle
        // rating above would otherwise classify one engine as a safe minimum for this fixture,
        // even though one Raptor at its 40% floor cannot arrest the vehicle near the ground.
        bool starshipLandingCluster = engineCluster.Definition.IsStarshipFamily
            && engineCluster.Definition.HasVehicleRole("ship_engines");
        if (starshipLandingCluster)
        {
            // Starship needs the three-engine centre cluster for the flip and initial
            // velocity arrest. Let the hysteresis step down to one centre Raptor in
            // FINAL_DESCENT: at the vehicle's landing mass, one engine spans the required
            // throttle range around hover while two engines are above hover at their physical
            // minimum and force a bounce. The restore branch below brings the second engine
            // back before the commanded thrust saturates on a hot approach.
            minimumSafeEngines = _phase == Edl.Final ? 1 : maxLandingEngines;
        }
        if (_landingEngineCount <= 0)
            _landingEngineCount = maxLandingEngines;
        int selected = System.Math.Max(
            System.Math.Min(_landingEngineCount, maxLandingEngines), minimumSafeEngines);
        if (_phase is Edl.Catch or Edl.Final)
        {
            const double StepDownCapacityFraction = 0.75;
            while (selected > 1
                   && selected > minimumSafeEngines
                   && requested < selected
                   && desiredThrust <= perEngine * (selected - 1)
                       * StepDownCapacityFraction)
                selected--;

            // Restore an engine before the commanded thrust saturates. This is the other half of
            // the hysteresis: it makes an earlier economical step-down reversible and gives the
            // burn enough authority for the last part of a hot approach.
            if (requested > selected
                || desiredThrust > perEngine * selected * 0.90)
                selected = System.Math.Min(maxLandingEngines, requested);
        }
        _landingEngineCount = selected;
        engineCluster.SelectEngineCount(selected);
        double throttle = committedStarshipBurn
            ? System.Math.Max(DefinitionMinThrottle(engineCluster),
                desiredThrust / (perEngine * selected))
            : desiredThrust / (perEngine * selected);
        vessel.Throttle = engineCluster.ApplyThrottleFloor(
            System.Math.Clamp(throttle, 0.0, 1.0));
    }

    private static double DefinitionMinThrottle(Part engineCluster)
    {
        return System.Math.Max(0.0, engineCluster.Definition.MinThrottle);
    }

    private static double EstimateFlipTime(double angle, double angularAcceleration, double maxRate)
    {
        if (angle <= 0.0) return 0.0;
        angularAcceleration = System.Math.Max(1e-4, angularAcceleration);
        maxRate = System.Math.Max(1e-3, maxRate);
        double triangularAngle = maxRate * maxRate / angularAcceleration;
        if (angle <= triangularAngle)
            return 2.0 * System.Math.Sqrt(angle / angularAcceleration);
        return 2.0 * maxRate / angularAcceleration
             + (angle - triangularAngle) / maxRate;
    }

    private static Quaterniond ShortestArc(Vector3d from, Vector3d to)
    {
        var f = from.Normalized; var t = to.Normalized;
        double dot = f.Dot(t);
        if (dot > 0.99999) return Quaterniond.Identity;
        if (dot < -0.99999)
        {
            Vector3d ax = System.Math.Abs(f.X) < 0.9 ? f.Cross(Vector3d.Right) : f.Cross(Vector3d.Up);
            return Quaterniond.FromAxisAngle(ax.Normalized, System.Math.PI);
        }
        return Quaterniond.FromAxisAngle(f.Cross(t).Normalized,
            System.Math.Acos(System.Math.Clamp(dot, -1.0, 1.0)));
    }
}
