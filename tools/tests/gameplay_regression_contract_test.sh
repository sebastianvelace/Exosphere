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
# their own HUD state and must not be presented as failed.
require_pattern scripts/EngineGridHUD.cs 'Color c = failed' 'engine failure color gate'
require_pattern scripts/EngineGridHUD.cs 'EngineLifecycleState.Chill' 'engine startup state coloring'

# A plume follows delivered engine availability, not merely the throttle command.
require_pattern scripts/VesselRenderer.cs 'TargetVessel.ActiveEngineCount > 0' 'delivered-thrust plume gate'

# A map jump must clear stale conic, rails, attitude-rate and contact state.
require_pattern ExosphereSimulation/Vessel.cs 'public void PrepareForTeleport()' 'teleport state reset API'
require_pattern scripts/SimulationBridge.cs 'v.PrepareForTeleport();' 'orbit jump reset'
require_pattern scripts/SimulationBridge.cs 'v.ReferenceBodyId = body.Id;' 'body jump reference-body reset'
require_pattern ExosphereSimulation/Vessel.cs 'ResetEngineRuntimeForTeleport();' 'teleport cuts residual engine torque'
require_pattern scripts/SimulationBridge.cs 'ClearPendingGroundCommandsForTeleport();' 'body jump clears delayed ground commands'
require_pattern scripts/EDLController.cs 'TryArmStarbaseCatchForReentry(vessel, body);' 'normal Starbase reentry catch arming'
require_pattern scripts/SimulationBridge.cs 'catchAnchorVessel' 'catch vessel anchors launch-pad presentation'
require_pattern scripts/SimulationBridge.cs 'starshipReentryActive' 'Starship reentry keeps launch complex visible'
require_pattern scripts/SimulationBridge.cs 'earthReturnActive' 'Earth launch complex visibility is body-gated'
require_pattern scripts/AutopilotController.cs 'BurnDampingGain' 'deorbit autopilot uses damped retrograde alignment'

# Every reentry-capable legacy Starship definition has catch pins, and visual
# acceptance recognizes the simulator's CAUGHT terminal phase.
require_pattern data/parts/starship_command.json 'catch_pin_lateral_offset_m' 'legacy Starship catch pins'
require_pattern scripts/SimulationBridge.cs 'ArmTowerCatchApproach(vessel);' 'reentry catch arming'
require_pattern scripts/EDLController.cs 'if (_phase is Edl.Catch or Edl.Final)' 'catch-specific engine selection'
require_pattern scripts/EDLController.cs 'vessel.IsAttemptingTowerCatch && vessel.HasCatchPins' 'catch trajectory guidance gate'
require_pattern scripts/EDLController.cs 'aimAxis = up.Cross(velDir)' 'catch broadside trajectory'
require_pattern scripts/EDLController.cs 'desiredHorizontalVelocity = towardTarget * closingSpeed' 'catch position/velocity guidance'
require_pattern scripts/SimulationBridge.cs 'Vector3d rotationalVelocity = earth.GetSurfaceVelocity(rotationReferencePosition);' 'catch-target rotation seed'
require_pattern scripts/EDLController.cs 'CatchAbortHorizontalMissToleranceM = 20.0' 'recoverable catch abort corridor'
require_pattern scripts/EDLController.cs 'CatchAbortHorizontalSpeedToleranceMps = 6.0' 'recoverable catch speed corridor'
require_pattern scripts/EDLController.cs 'if (_phase is Edl.Catch or Edl.Final)' 'catch engine stepdown path'
require_pattern scripts/EDLController.cs 'CatchContactPoints' 'catch pin datum descent target'
require_pattern scripts/EDLController.cs 'Edl.Catch);' 'catch-phase scripted attitude scope'
require_pattern tools/visual_playtest.sh 'Finish("CAUGHT")' 'visual catch acceptance'
require_pattern tools/visual_playtest.sh 'QueueCapture("caught")' 'visual catch capture'
require_pattern tools/visual_playtest.sh 'bridge.SetTimeScale(3.0);' 'accelerated post-flip validation'

echo "gameplay_regression_contract_test: PASS"
