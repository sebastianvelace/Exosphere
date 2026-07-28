# Hito 1 — Falcon 9 Block 5 / VAB foundation

## Playable path

1. Open **Vehicle Assembly** from the main menu.
2. Select **Falcon 9 B5** or **F9 Extended**.
3. Inspect wet/dry mass, TWR and active-stage delta-v in Flight Readiness.
4. Save the vehicle. New files use `CraftDocumentV2`; legacy craft JSON is migrated on load.
5. Press **Launch**. `LaunchIntent` selects Kennedy and the Falcon ascent profile instead
   of inheriting Starbase/Starship defaults.

The standard and extended presets are dated 2025-05-09. Their primary reference is the
SpaceX Falcon User's Guide from that date. Provenance for published, derived and estimated
fields is in `data/provenance/falcon9_block5_2025.json`.

## Headless acceptance

Run:

```sh
dotnet test ExosphereSimulation.sln --no-restore
./tools/vab_quick_check.sh
```

The regression suite covers:

- stable vessel and part IDs through craft/save round-trips;
- legacy craft and save migration;
- complete part resource, engine, thermal, joint and crew restoration;
- Falcon preset mass, TWR and published Merlin ratings;
- required provenance records;
- deterministic Merlin acceptance test with thrust–flow–Isp identity;
- payload separation mass, aggregate centre and linear-momentum conservation;
- switching control to the deployed payload.

## Commercial-release gate

`data/licenses/assets_manifest.json` deliberately blocks a commercial release until the
pre-existing textures, project artwork, contributor rights and NASA/SpaceX mark usage receive
legal review. Unknown licenses are not treated as permission.
