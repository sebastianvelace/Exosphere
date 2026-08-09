# VAB and progression gameplay audit — 2026-08-09

## Current capabilities

The main menu already exposes campaign, sandbox, VAB, scenarios, re-entry, saves and
settings. The VAB has a parts catalogue, filters, presets, a 3D preview, picking, orbital
camera, undo/redo, save and basic validation. Falcon 9, New Glenn and Starship Flight 7/12
variants are available.

## Gaps that block an extensible Spaceflight-Simulator-style loop

- The menu dossier is static (Falcon 9 / Kennedy / 200 km) instead of reflecting the
  selected craft, mass, TWR and launch site.
- `Continue` does not reliably mean “most recently saved”: save slots are alphabetically
  ordered, so the last list item is not necessarily the newest `SavedAtUtc`.
- `CraftDocumentV2` declares stages, action groups and a payload manifest, but the VAB does
  not edit or round-trip those fields. Legacy migration can discard them.
- There is no stage organizer, separation configuration, action-group editor, payload mass/
  centre-of-mass/volume editor, rover flow or static-fire test stand.
- `Vessel.DeployPayload()` exists in the simulation but is not connected to a user-facing
  payload workflow. A saved craft and the craft flown from the VAB can therefore diverge.
- Launch-site selection is still variant-driven and hard-coded rather than a persisted
  mission/site choice.

## Ordered implementation tranche

1. Bind menu dossier and Continue to save metadata (`SavedAtUtc`, craft, mass, TWR, site),
   with tests for corrupt and empty slots.
2. Add stage ordering, action groups and round-trip persistence before adding new parts.
3. Add payload manifests for satellites and rovers, including mass, volume, centre of mass,
   separation event and a validation rule for invalid payloads.
4. Add a static-fire/test-stand mode that reuses the runtime engine cycle and records thrust,
   chamber pressure, mixture, Isp, flow and thermal margins.
5. Add vehicle-family metadata (manufacturer, family, unlock requirements) and then expand
   Falcon, New Glenn and other vehicles without duplicating launch logic.
6. Add campaign contracts and progression only after craft/payload/save schemas are stable.

## E2E acceptance target

The first complete gameplay contract is: menu → VAB → choose vehicle → add payload → configure
stage/action group → save/load → static-fire test → launch → staging → stable orbit → re-entry
→ landing or a physically classified crash. Each state must emit deterministic telemetry and
at least one framebuffer milestone in the visual harness.
