# Flight operations menu — 2026-10-01

The project now boots into an operations menu instead of the black-hole title's
single direct launch action. The existing orbital Earth asset, restrained white
controls, large condensed headings and readable dark panels provide a cinematic
home screen. The visual reference is the [official Starship page](https://www.spacex.com/vehicles/starship);
no SpaceX wordmark or new mission-performance data is introduced.

## Routes

| Entry | Behavior |
| --- | --- |
| Explore Flight 14 | Opens the mission briefing. Launch stays disabled while the independent mission/vehicle profile is unavailable. |
| Free Flight | Select one of ten complete launcher presets, review the dated configuration and compatible pad, then start manual flight. |
| Historical Campaign | Read ready, completed, locked or planned mission entries. Briefings expose objectives, limits, historical date and unmet prerequisites before starting. Apollo 11 explicitly identifies its partial lunar profile. |
| Vehicle Assembly | Opens the existing construction scene. |
| Scenarios | Preserves prepared Falcon, New Glenn, Flight 7/12 and 70 km entry demonstrations. |
| Continue / Saved Flights | A single save can resume directly; multiple saves require selection, because slot names are alphabetical rather than timestamps. |
| Settings | Retains language, reduced motion and interface-scale controls. |

`FlightMenuCatalog` maps existing launcher configurations to compatible home pads.
LM and Agena spacecraft remain in their campaign/VAB contexts; they are not
presented as complete rockets that can launch from a pad. No rocket performance,
aerodynamic, propulsion, entry or guidance contract changes are part of this menu.
A detailed vehicle-fidelity audit remains a separate requested follow-up.

The Flight 14 briefing is a navigation foundation, not a playable reconstruction.
The existing Flight 7 and Flight 12 datasets remain named as such. Reconstruction
requirements remain in [the Flight 14 feasibility study](../research/STARSHIP_FLIGHT14_VIDEO_FEASIBILITY_2026-09-30.md).

## Responsive behavior and input

Home navigation uses one column centered horizontally and vertically on the viewport,
with centered title, description and button labels. Featured mission details live in
the Flight 14 briefing instead of a separate side panel. A symmetric background
scrim preserves contrast behind the central content. Navigation scrolls only when
the content cannot fit; typography and spacing shrink for short windows.
Logical dimensions already account for Godot's interface scale, avoiding a second
division by that scale. Mission/vehicle lists scroll independently of the dialog
heading and close action. Keyboard focus is confined to an open dialog; Escape
closes it and restores focus to the initiating home action. Initial focus waits
for container layout before computing scroll offsets.

## Verification

- `tools/ci_check.sh`: both builds, 902 xUnit tests and Flight/MainMenu/Construction smoke checks.
- `tools/menu_quick_check.py`: real framebuffer captures with Compatibility under Xvfb/llvmpipe,
  1920×1080 and 1280×720, English/Spanish, 100%/150% interface scale.
- Menu routes checked: Flight 14 unavailable launch, ten launcher entries,
  campaign briefing, settings, Escape/focus return and keyboard access to every home action.
- Actual scene transitions: construction, Freedom 7 campaign at LC-5,
  manual Flight 12 at Starbase Pad 2 and manual Falcon 9 at Kennedy LC-39A.
- `git diff --check`: clean. Visual acceptance includes inspecting the actual images;
  successful route markers alone do not establish the appearance.

Reproduce with `python3 tools/menu_quick_check.py`; use `--case NAME --skip-build`
for a focused rerun. Captures and logs are retained locally in
`exports/menu-operations-review/`, outside tracked source. Writable Godot user data
is isolated in temporary profiles. The existing save adapter may still discover
read-only legacy Linux saves; tests do not rewrite those saves.

This verifies menu navigation and scene startup, not completion of the historical
missions, a full Flight 14 flight, or hardware-GPU rendering performance.
