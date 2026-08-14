# Functional flight audit — phase 22

Status: ascent and Starship tower-catch verification passed; navigation-teleport guard added  
Date: 2026-08-14  
Host renderer: Godot 4.6.3 / OpenGL compatibility / Mesa llvmpipe

## Engine HUD finding

`EngineGridHUD` treats a red engine dot as `FailureCode != null`. It is not the normal
"engine is running" colour. The centre counter is the number of runtime engine instances
whose chamber-pressure fraction is above the active threshold, over the nominal engine
count declared by the part graph.

The focused ascent run reproduced the intended state transitions:

- pre-launch spool: `runningEngines=0`, `failedEngines=0`;
- liftoff: `33/33`, `failedEngines=0`;
- hot-stage and separation: `39/39`, `failedEngines=0`;
- post-separation Ship: `6/6`, `failedEngines=0`;
- stable orbit: `ASCENT_ORBIT_OK`.

Therefore the supplied `0/33` plus all-red state is not the normal running presentation. It
means the HUD believed the instances had failure codes at that instant, or the capture was
from a pre-ignition/failure transition. The authoritative trace to use alongside a frame is
`runningEngines`/`failedEngines`, not the dot colour alone.

Evidence:

```text
ASCENT_METRICS samples=48 insertObserved=True minInsertionVSpeed=-97.5 maxInsertionDescent=97.5
SUMMARY reason=ASCENT_ORBIT_OK frames=1993
```

## Navigation jump guard

`J` changes the vessel's position, velocity, reference body and attitude discontinuously.
Before this phase, the simulation state was reset but a map-owned `AutopilotController` or
`ManeuverExecutor` could remain armed and write its old attitude/throttle command on the next
Godot frame. That is a credible cause of the reported post-jump tumbling.

The fix now:

1. clears map planner, transfer node, local autopilot and maneuver executor before both
   `JumpToBody` and `JumpToOrbit`;
2. makes `PrepareForTeleport` force throttle to zero as part of the transient-state reset;
3. restores the destination state only after the old command sources are invalidated.

The existing teleport regression now also asserts that throttle cannot survive the reset.

## Starship reentry and chopsticks

The deterministic EDL run completed the full path through entry, peak heating, aero descent,
retro burn and tower approach. The catch path ended with:

```text
CHECK tower_catch caught=True pins=2 relativeSpeed=0.030 angularSpeed=0.0000
SUMMARY reason=CAUGHT frames=616
```

The captured `caught` frame shows the Starship at the Mechazilla tower with the chopstick
assembly visible and the `CAUGHT` state banner. The catch solver requires both configured
pin contacts, low relative speed and low angular speed; the visual arms close from
`Vessel.IsCaught`, so rendering does not invent a catch that physics rejected.

The run also exposed a non-blocking diagnostic: the final approach can hover near the cradle
plane for several seconds before the two-point solver receives penetration. It eventually
settled and passed, but this is a follow-up optimization target for a faster, more fuel-safe
catch approach. It must be improved only with a regression preserving the two-pin contact
and abort-to-legs safety gates.

## Commands

```bash
bash tools/visual_playtest.sh --ascent --flight7 --run-id phase22-ascent-audit --skip-build
bash tools/visual_playtest.sh --edl --run-id phase22-edl-catch --skip-build
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-restore
```

The visual runs used isolated `/tmp` output directories. The GPU matrix remains blocked on
this host because only llvmpipe is available; no physical-GPU performance conclusion is
made here.
