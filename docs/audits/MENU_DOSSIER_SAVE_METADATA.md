# Main-menu save dossier

The central menu dossier now reflects the same valid slot selected by `Continue`.
It is populated from the existing `SaveGameV2` fields through
`SaveDossierView`; no save-schema fields were added and no save files are rewritten.

## Contract

- `SaveSystem.ReadMostRecentDossier()` parses the valid `*.json` slots using the
  same timestamp and ordinal-name ordering as `FindMostRecentSaveSlot()`.
- Invalid or empty files are ignored, so one broken slot cannot hide a usable
  continuation.
- The adapter exposes only existing data: slot name, serialized timestamp,
  mission/phase, active vessel, simulation time scale, vessel count, and mission
  progress counters.
- Missing optional values use deterministic display fallbacks (`SANDBOX FLIGHT`,
  `SAVED`, `NO ACTIVE VESSEL`, `NONE`). IDs are trimmed, flattened to one line,
  and bounded to 64 characters before reaching a Godot label.
- When no valid save exists, the curated Falcon 9 launch dossier remains the
  first-run presentation.

## Verification

`SaveDossierViewTests` covers active-vessel selection, mission/campaign progress,
fresh-save fallbacks, and bounded single-line values. The game project build is
also required because the adapter is consumed by `MainMenu` and `SaveSystem`.
