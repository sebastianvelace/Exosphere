# Apollo 11 — TD&E, docked LOI and lunar landing

**Profile id:** `apollo11-lunar-landing-return`  
**Mission id:** `mission-apollo11-1969`  
**Stops at:** Eagle on the lunar surface (`LANDED`), Columbia in low lunar orbit  
**Deferred:** surface ops, APS ascent, rendezvous, TEI, splashdown

## Player arc

1. Launch AS-506 from LC-39A (`kennedy`) in the lunar launch window.
2. Saturn V ascent → Earth parking orbit → TLI (AS-503 Saturn V schedule proxy).
3. CSM-107 separates from S-IVB/SLA (`LUNAR_APPROACH`).
4. LM-5 Eagle extracted from the opaque SLA envelope (`Part.MassDryOffset` carve-out).
5. Transposition and docking on SM RCS: `ProximityApproach` turns the CSM end over end and
   closes on Eagle's drogue until `Universe.TryDock` captures
   `apollo11-columbia-eagle-docking`. No position or velocity is assigned.
6. Docked stack coasts to the Moon → finite SPS LOI → elliptical LLO → circularization
   (`LUNAR_ORBIT`).
7. Armstrong and Aldrin transfer to Eagle through the tunnel (`Universe.TransferCrew`), Eagle
   undocks on the docking springs and takes control (`SimulationBridge.TransferControlTo`).
8. `LunarPoweredDescent`: DOI to a 15 km perilune, PDI there, braking guidance to the 150 m
   low gate, vertical let-down to contact (`LANDED`).
9. `CampaignRuntime` finalizes on `LANDED` → Success when parking + TLI + approach + docking +
   LOI + lunar orbit + landing pass.

## Key files

- `ExosphereSimulation/Flight/Apollo11FlightProfile.cs`
- `ExosphereSimulation/Flight/ProximityApproach.cs`
- `ExosphereSimulation/Flight/LunarPoweredDescent.cs`
- `data/missions/apollo11_1969.json`
- `scripts/HistoricalFlightProfileController` (`ProcessApollo11TranspositionAndDocking`,
  `ProcessApollo11LunarOrbitInsertion`, `ProcessApollo11Descent`)
- `scripts/SimulationBridge` (`EnsureApollo11EagleExtracted`, `TransferControlTo`)

## Tests

- `Apollo11DataTests`: docking ports, SLA carve-out, mission JSON, evaluator success/fail,
  headless extract+dock, headless docked LOI circularization.
- `Apollo11TranspositionDockingScenarioTests`: RCS transposition and real capture, crew
  transfer only while docked.
- `Apollo11PoweredDescentScenarioTests`: circular LLO → DOI → PDI → touchdown under 3 m/s.

## Godot check

```bash
bash tools/visual_playtest.sh --apollo11-mission --max-runtime 7200   # default --until-phase LUNAR_ORBIT
bash tools/visual_playtest.sh --apollo11-mission --until-phase LANDED --max-runtime 9000
```
