# Hito 3 — Starship Flight 12 V3 / Raptor 3

Exosphere treats Flight 12 as a dated historical variant, separate from the
Flight 7 Block 2 baseline:

- `starship-flight-12-v3-2026-05-22` uses its own five part definitions, two
  engine clusters and three Raptor 3 engine models.
- The flown configuration is fixed at 33 booster engines and six ship engines,
  following SpaceX's post-flight report.
- Each engine has an independent lifecycle, feed branch, gimbal state and
  telemetry. Deterministic engine-out remains available for testing.
- Part `vehicle_family` and `vehicle_role` metadata drive Starship rendering,
  ascent staging, EDL and cockpit behavior. Flight 12 does not depend on Flight
  7 part IDs.

## Evidence boundary

The primary mission source is SpaceX's
[Starship's Twelfth Flight Test](https://www.spacex.com/launches/starship-flight-12).
It establishes the date, first V3/Raptor 3 flight, Pad 2, 33+6 engine counts and
the reported mission sequence. It does not publish Flight 12 acceptance thrust,
Isp, throttle, mass or loading.

Those missing operational values are therefore labelled `estimated`,
`derived` or `calibrated` in
`data/provenance/starship_flight12_v3_2026.json`. The model is intentionally
described in-game as a **restricted public-data engineering model**, not a
perfect Raptor 3 replica.

The FAA KSC LC-39A environmental-analysis configuration allows up to 35 booster
and nine ship engines at 103 MN and 28.7 MN. Exosphere records it only as
`regulatory_envelope`; it does not use that future 35+9 envelope as the flown
Flight 12 configuration.

## Playtest

From the main menu, open **Scenarios** and choose
**STARSHIP FLIGHT 12 / V3 + RAPTOR 3**. The scenario uses the stable
`starbase_pad2` launch-site ID; its mission use is published and its pad-center
coordinate is explicitly estimated. The dated preset is also available in the
VAB as **Starship F12**.

For a deterministic real-framebuffer capture:

```bash
tools/visual_playtest.sh --flight12 --smoke
tools/visual_playtest.sh --flight12 --launch
```

The data regression suite freezes Flight 7 mass, length and Raptor 2 IDs while
checking the Flight 12 33+6 clusters, engine-out behavior, thrust aggregation
and provenance separation.
