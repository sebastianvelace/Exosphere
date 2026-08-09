# VAB payload integration audit — 2026-08-09

## Scope

The construction scene already exposed `CraftDocumentV2.PayloadManifest`, but the
interactive VAB only built a fresh document from the part tree. As a result, a player
could not declare a satellite/payload, and a manifest loaded from a craft file was
silently discarded when the assembly was rebuilt or an undo snapshot was taken.

## Delivered slice

- `VesselAssembly.MarkPayload` records a payload against a real part instance.
- The default declared mass is the part's dry mass; an integration can provide a
  measured mass explicitly. The API rejects non-finite/non-positive masses and limits
  names to 80 characters.
- `RemovePayload` removes only the declaration. Deleting a part/subtree also removes
  declarations that would otherwise point at missing hardware.
- `VesselCraftDefinition` and `CraftDocumentMigration` now carry the manifest through
  legacy-compatible craft JSON and `CraftDocumentV2`.
- The VAB EDIT row has a `Mark payload` action. It toggles the selected stack/preview
  part, and because undo snapshots use the same craft definition, the operation is
  reversible. Save and launch both serialize the resulting manifest.

## Verification contract

The construction regression suite adds coverage for:

1. measured payload mass, name, independence flag, instance id and stable payload id
   surviving JSON round-trip;
2. deleting payload hardware removing the stale manifest entry;
3. rejecting zero and `NaN` payload masses.

The simulation and Godot projects build with zero warnings/errors. The full xUnit
invocation was temporarily blocked by unrelated in-progress `AtmosphereModel` changes
in the shared worktree (`AtmosphereAerosolOpticsTests` uses `with` against that class),
so this slice must be rerun with the full suite once that branch is repaired.

## Deliberate boundary

This change declares and persists integration intent; it does not yet detach the payload
in flight or synthesize a new vessel. That next slice needs a staging/action-group
consumer, mass/centre-of-mass transfer, and a visual separation E2E gate rather than
guessing at flight behaviour inside the VAB.
