# Shared broadcast telemetry — 2026-10-01

## Behavior

The exterior band now derives propulsion groups from the live part graph's
separation boundaries, ordered by their physical stack positions. Generic vehicles
show the current and next attached propulsion group. The next group becomes the
current group after mechanical staging; a final group leaves the altitude dial on
the right. Starship preserves independent Super Heavy/Ship boards through staging.

Dots project actual engine mount X/Z positions. Single motors sit at the center;
Falcon's Octaweb, the Titan pair, Atlas's pair plus sustainer, New Glenn and Saturn
use their catalog geometry rather than a count-based Raptor template. Nozzle sizes
use model exit area when available; otherwise they are schematic. Stable runtime
IDs link each dot to delivered telemetry. Deselected engines remain installed dots;
broken parts and confirmed failures remain visible. Legacy aggregate propulsion
(such as Mercury's retro pack) is labeled as groups, not independent motors.

Simulation reloads, rewinds and active-vessel changes reset instrument identity and
observed events. Unknown liftoff epochs remain SIM time. Redstone's suborbital
track uses COAST; stacks without a decoupler omit STAGE SEP. No solver, mass,
thrust, aerodynamic, guidance or trajectory equations were changed.

## Verification

- Real Godot Compatibility/Xvfb matrix: all ten menu launchers at 960×540;
  80 checks covering Minimal/Full/Clean/restore, cockpit/exterior, map/return.
  Vehicle name, compatible pad, manual profile and sandbox intent were asserted.
- Live instrument identities/counts: Starship 33+6, Falcon 9+1, New Glenn 7+2,
  Redstone 1+retro group, Atlas 3+retro group, Titan 2+1, Saturn 5+5 initially.
- Fourteen mechanical stage captures: generic boards advance to attached groups;
  both Saturn variants reach S-II, S-IVB and SPS. Starship retains its two boards.
- Sixteen xUnit cases cover all twelve vehicle definitions, stable identities,
  Saturn stage ordering, Atlas half-stage removal, engine deselection/breakage,
  actual Octaweb mounts and a centered single vacuum engine.
- Initial and selected staged captures were inspected for every launcher.
  Artifacts: `exports/all-rocket-hud-review/<variant>/hud-*.png` (gitignored).
- Full `bash tools/ci_check.sh`: both builds at zero warnings/errors, all 918 xUnit
  tests passed, Flight reached 60 frames and MainMenu/VAB smoke checks passed.

## Limits

Mechanical staging captures are presentation fixtures on the pad, not powered
ascent or end-to-end mission acceptance. The band projects the catalog's geometry;
this work does not claim that every rocket's historical model has been audited.
LM and Agena use the shared logic and have catalog tests; they are mission/VAB
spacecraft rather than standalone pad entries. The global MissionManager still
supplies observed phases; mission-specific historical timelines are separate work.
Flight 14 remains unimplemented and its menu launch button remains disabled.
