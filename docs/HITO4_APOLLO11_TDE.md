# Apollo 11 — Transposition & Docking (TD&E) playable slice

**Profile id:** `apollo11-lunar-landing-return`  
**Mission id:** `mission-apollo11-1969`  
**Stops at:** Columbia ↔ Eagle hard-dock  
**Deferred:** DOI, powered descent, surface ops, ascent, rendezvous, TEI, splashdown

## Player arc

1. Launch AS-506 from LC-39A (`kennedy`).
2. Saturn V ascent → Earth parking orbit → TLI (AS-503 Saturn V schedule proxy).
3. CSM-107 separates from S-IVB/SLA (`LUNAR_APPROACH`).
4. LM-5 Eagle is extracted from the opaque SLA envelope (mass carved via `Part.MassDryOffset`).
5. Calibrated approach + `Universe.TryDock` → `apollo11-columbia-eagle-docking`.
6. `CampaignRuntime.RequestFinalize()` → Success when parking + TLI + approach + docking objectives pass.

## Key files

- `ExosphereSimulation/Flight/Apollo11FlightProfile.cs`
- `data/missions/apollo11_1969.json`
- `data/parts/apollo11_command_module_csm107.json` / `apollo11_lm5_ascent_stage.json` (docking ports)
- `scripts/SimulationBridge.EnsureApollo11EagleExtracted`
- `scripts/HistoricalFlightProfileController` (`_apollo11Tde` branch after CSM sep)

## Tests

`Apollo11DataTests`: docking ports, SLA carve-out, mission JSON/campaign wiring, evaluator success/fail, headless extract+dock mass conservation.
