# Apollo 11 — TD&E + docked LOI playable slice

**Profile id:** `apollo11-lunar-landing-return`  
**Mission id:** `mission-apollo11-1969`  
**Stops at:** circular low lunar orbit (docked Columbia↔Eagle)  
**Deferred:** DOI, powered descent, surface ops, APS ascent, rendezvous, TEI, splashdown

## Player arc

1. Launch AS-506 from LC-39A (`kennedy`).
2. Saturn V ascent → Earth parking orbit → TLI (AS-503 Saturn V schedule proxy).
3. CSM-107 separates from S-IVB/SLA (`LUNAR_APPROACH`).
4. LM-5 Eagle extracted from the opaque SLA envelope (`Part.MassDryOffset` carve-out).
5. Calibrated approach + `Universe.TryDock` → `apollo11-columbia-eagle-docking`.
6. Docked stack coasts to the Moon → impulsive LOI → elliptical LLO → circularization (`LUNAR_ORBIT`).
7. `CampaignRuntime.RequestFinalize()` → Success when parking + TLI + approach + docking + LOI + lunar orbit pass.

## Key files

- `ExosphereSimulation/Flight/Apollo11FlightProfile.cs`
- `data/missions/apollo11_1969.json`
- `scripts/HistoricalFlightProfileController` (`ProcessApollo11TranspositionAndDocking`, `ProcessApollo11LunarOrbitInsertion`)
- `scripts/SimulationBridge.EnsureApollo11EagleExtracted`

## Tests

`Apollo11DataTests`: docking ports, SLA carve-out, mission JSON, evaluator success/fail (dock + LOI required), headless extract+dock, headless docked LOI circularization.
