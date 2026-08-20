#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"

require_pattern() {
  local file="$1"
  local pattern="$2"
  local description="$3"
  if ! rg -q --fixed-strings "$pattern" "$ROOT/$file"; then
    echo "FAIL missing $description ($file: $pattern)" >&2
    exit 1
  fi
}

bash -n "$ROOT/tools/visual_playtest.sh"

# Red is reserved for an actual FailureCode; starting or unloaded engines have
# their own HUD state and must not be presented as failed.  Accept the legacy
# inline classifier and the newer shared presentation classifier so this gameplay
# contract remains compatible with either already-reviewed HUD implementation.
if rg -q --fixed-strings 'Color c = failed' "$ROOT/scripts/EngineGridHUD.cs"; then
  require_pattern scripts/EngineGridHUD.cs 'EngineLifecycleState.Chill' 'engine startup state coloring'
else
  require_pattern scripts/EngineGridHUD.cs 'EngineHudPresentation.Classify(readout)' 'shared engine state classifier'
  require_pattern ExosphereSimulation/Presentation/EngineHudPresentation.cs 'readout.FailureCode != null' 'engine failure semantics'
fi

# A plume follows delivered engine availability, not merely the throttle command.
if rg -q --fixed-strings 'TargetVessel.ActiveEngineCount > 0' "$ROOT/scripts/VesselRenderer.cs"; then
  :
else
  require_pattern scripts/VesselRenderer.cs 'EngineHudPresentation.DeliveredThrottle(' 'delivered-thrust telemetry gate'
fi

# A map jump must clear stale conic, rails, attitude-rate and contact state.
require_pattern ExosphereSimulation/Vessel.cs 'public void PrepareForTeleport()' 'teleport state reset API'
require_pattern scripts/SimulationBridge.cs 'v.PrepareForTeleport();' 'orbit jump reset'
require_pattern scripts/SimulationBridge.cs 'v.ReferenceBodyId = body.Id;' 'body jump reference-body reset'
require_pattern ExosphereSimulation/Vessel.cs 'ResetEngineRuntimeForTeleport();' 'teleport cuts residual engine torque'
require_pattern scripts/SimulationBridge.cs 'ClearPendingGroundCommandsForTeleport();' 'body jump clears delayed ground commands'
require_pattern scripts/SimulationBridge.cs 'AscentController.Instance?.CancelGuidanceForTeleport();' 'body jump cancels ascent writer'
require_pattern scripts/SimulationBridge.cs 'HistoricalFlightProfileController.Instance?.CancelGuidanceForTeleport();' 'body jump cancels historical writer'
require_pattern scripts/AscentController.cs 'public void CancelGuidanceForTeleport()' 'ascent teleport cancellation API'
require_pattern ExosphereSimulation/Flight/StarbaseCatchPolicy.cs 'public static bool IsValidEntry(' 'pure Starbase catch eligibility policy'
require_pattern scripts/SimulationBridge.cs 'ArmValidStarbaseReentryCatch();' 'pre-EDL valid reentry catch arm'
require_pattern scripts/SimulationBridge.cs 'StarbaseCatchPolicy.IsValidEntry(' 'runtime uses fail-closed catch policy'
require_pattern scripts/EDLController.cs 'TryArmStarbaseCatchForReentry(vessel, body);' 'normal Starbase reentry catch arming'
require_pattern scripts/SimulationBridge.cs 'catchAnchorVessel' 'catch vessel anchors launch-pad presentation'
require_pattern scripts/SimulationBridge.cs 'activeCatch' 'active catch keeps launch complex visible'
require_pattern scripts/SimulationBridge.cs 'activeEarth' 'Earth launch complex visibility is body-gated'
require_pattern scripts/AutopilotController.cs 'BurnDampingGain' 'deorbit autopilot uses damped retrograde alignment'
require_pattern scripts/AutopilotController.cs '_burnCommandCommitted' 'deorbit burn does not restart engines on alignment oscillation'
require_pattern scripts/ManeuverPlanner.cs 'DefaultDeorbitTargetPeAltitudeM = 60_000.0' 'player deorbit preset has a deep atmospheric target'
require_pattern ExosphereSimulation/Physics/AerodynamicsModel.cs 'ComputeLiftDownEntryAxis' 'catch approach uses inward aerodynamic lift'
require_pattern scripts/EDLController.cs 'aeroPhase && vDown > 5.0 && _alt <= flipAlt' 'normal EDL flip gate is explicit'
require_pattern scripts/EDLController.cs 'RatedClusterThrust(engineCluster, vessel.GetAmbientPressure(body))' 'EDL flip timing uses nominal thrust before ignition'
require_pattern scripts/EDLController.cs 'engineCluster.Definition.ThrustVac' 'EDL has a legacy thrust fallback for staged runtime hydration'
require_pattern tools/visual_playtest.sh 'const double orbitalReturnReserve = 0.45;' 'orbital reentry reserves propellant for deorbit and landing'
require_pattern tools/visual_playtest.sh 'SetPropellantReserve(vessel, orbitalReturnReserve)' 'orbital reentry seeds a deterministic reserve'
require_pattern tools/visual_playtest.sh 'deorbit+landing reserve' 'orbital reserve is visible in acceptance telemetry'

# Every reentry-capable legacy Starship definition has catch pins, and visual
# acceptance recognizes the simulator's CAUGHT terminal phase.
require_pattern data/parts/starship_command.json 'catch_pin_lateral_offset_m' 'legacy Starship catch pins'
require_pattern scripts/SimulationBridge.cs 'ArmTowerCatchApproach(vessel);' 'reentry catch arming'
require_pattern scripts/EDLController.cs 'if (_phase is Edl.Catch or Edl.Final)' 'catch-specific engine selection'
require_pattern scripts/EDLController.cs 'vessel.IsAttemptingTowerCatch && vessel.HasCatchPins' 'catch trajectory guidance gate'
require_pattern scripts/EDLController.cs 'aimAxis = AerodynamicsModel.ComputeLiftDownEntryAxis(up, velDir)' 'catch inward-lift trajectory'
require_pattern scripts/EDLController.cs 'desiredHorizontalVelocity = towardTarget * closingSpeed' 'catch position/velocity guidance'
require_pattern scripts/SimulationBridge.cs 'Vector3d rotationalVelocity = earth.GetSurfaceVelocity(rotationReferencePosition);' 'catch-target rotation seed'
require_pattern scripts/EDLController.cs 'CatchAbortHorizontalMissToleranceM = 20.0' 'recoverable catch abort corridor'
require_pattern scripts/EDLController.cs 'CatchAbortHorizontalSpeedToleranceMps = 6.0' 'recoverable catch speed corridor'
require_pattern scripts/EDLController.cs 'if (_phase is Edl.Catch or Edl.Final)' 'catch engine stepdown path'
require_pattern scripts/EDLController.cs 'CatchContactPoints' 'catch pin datum descent target'
require_pattern scripts/EDLController.cs 'Edl.Catch);' 'catch-phase scripted attitude scope'
require_pattern scripts/LaunchPadController.cs 'CatchApproachArmed' 'launch pad exposes armed catch telemetry'
require_pattern scripts/LaunchPadController.cs 'CATCH_VISUAL' 'launch pad emits catch visual telemetry'
require_pattern scripts/LaunchPadController.cs 'float target = CatchCaptured ? 1f : 0f;' 'visual closes only after physical catch'
require_pattern tools/visual_playtest.sh 'Finish("CAUGHT")' 'visual catch acceptance'
require_pattern tools/visual_playtest.sh 'QueueCapture("caught")' 'visual catch capture'
require_pattern tools/visual_playtest.sh 'bridge.SetTimeScale(3.0);' 'accelerated post-flip validation'

echo "gameplay_regression_contract_test: PASS"
