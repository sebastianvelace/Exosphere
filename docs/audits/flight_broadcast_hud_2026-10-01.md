# Broadcast flight telemetry

Status: implemented and validated in production launch/camera transitions.

## Telemetry changes

`FlightBroadcastHUD` follows the supplied monochrome reference: independent engine
boards, curved observed-event timeline, centred clock, speed dial and compact
flight values. It reads delivered engine states by stable instance ID across both
stages after separation. Commanded throttle does not substitute for delivered
chamber pressure. Unobserved milestones stay dim; planned times cannot mark an
event reached. Restart/load resets event history and unknown launch time uses `SIM`.
The current vehicle keeps its actual name; no Flight 12 preset is labelled Flight 14.

Minimal and Full exterior layouts use this band and the existing piloting navball.
Clean retains the expanded attitude cluster. Alerts, objectives, diagnostics,
keyboard controls and cockpit/map policies remain available. An actual Flight 12
launch at 1920×1080 verified engine ignition and `T+` after liftoff. The first
collapsed-width band was caught in image review and corrected. The maintained
menu capture tool additionally checks actual band bounds and visibility through
F3, cockpit and map transitions, rather than accepting PNG existence alone.
All eight 1280×720 transitions passed and were reviewed, including return from
the map. The map itself retains an unrelated fixed-layout clipping issue at this
resolution; this check only validates HUD visibility, not the complete map layout.

## Flight 14 boundary

The official post-flight account describes a separate safe suborbital ascent,
one sea-level engine for insertion, 26 satellites and an early northern-Pacific
return. Its approximate nine-hour timeline is the original plan, not the observed
return schedule. The current mission button remains unavailable until distinct
vehicle data, mission sequencing and continuous physical propagation are verified.
See the research feasibility report for source alignment and unresolved parameters.

The final aggregate passed with zero C# warnings/errors, 902 simulation tests,
and flight/menu/construction smoke checks. Reproduce the production UI matrix:

```bash
python3 tools/menu_quick_check.py --flight-hud
```
