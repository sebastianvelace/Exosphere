# Unresolved realism fronts — execution plan (2026-08-21)

**Base:** `main` at `4420ae5` plus a live visual agent on `tools/visual_playtest.sh`.
**Goal:** advance physics fronts that are still open without colliding with visual work.
**Do not enable** `SimulationInterestPolicy` / deferred physics in this pass.

## Parallel lanes

| Agent | Front | Why now | Exclusive files | Forbidden |
|---|---|---|---|---|
| Visual (already running) | Daylight / Earth / EDL plasma / harness | Player-facing weak point | `tools/visual_playtest.sh`, `tools/tests/visual_playtest_contract_test.sh`, shaders, `scripts/*Controller.cs` visual, `VesselRenderer.cs` | Physics JSON / `CelestialBody` |
| A — J2 gravity | Point-mass ceiling | Highest realism ROI per line; RK4 already calls `GetGravityAt` | See Agent A | Visual, warp scheduler, EDL, Sutton-Graves |
| B — R15b ephemeris | Jupiter/Saturn J2000 phase | Data-only; tests already document the bug | See Agent B | `earth.json`, `CelestialBody.cs`, visual |
| C — physics-reviewer | Adversarial check of Agent A | After A lands | Read-only | Any edits |

Agent C starts only after A reports a green sim test suite.

Out of this wave (need their own re-baseline or visual-quiet time):
- R18b Sutton-Graves broadside 1/√2
- EDL GNC / flap actuators (`EDLController.cs` is in the visual spec)
- Dated lunar ephemerides / executable LOI
- Thermosphere F10.7 (would collide with `earth.json` if A is also editing it)
- Apollo 11 DOI / landing

## Agent A — J2 zonal gravity

### Scope
First-order **J2** in `CelestialBody.GetGravityAt`, data-driven, default off.

- Add optional JSON `j2` (dimensionless) and `equatorial_radius` (m). Missing `j2` ⇒ 0 (point mass, bit-identical to today).
- Earth values: J2 = `1.08262668e-3` (EGM96/IERS), equatorial radius `6378137` m. Do not reuse mean radius `6371000` as Re.
- Mars/Jupiter/Saturn/Moon: publish published J2 if the loader is generic; Earth is the acceptance body.
- Express J2 in the **body equatorial frame** whose +Z is `RotationAxis`. Zonal J2 does not need sidereal spin phase.
- Vector form (Vallado): in equatorial coordinates
  - `ax = −μ x / r³ [1 − (3/2) J2 (Re/r)² (5 (z/r)² − 1)]`
  - same for `y`
  - `az = −μ z / r³ [1 − (3/2) J2 (Re/r)² (5 (z/r)² − 3)]`
- `j2 == 0` must keep the current `−GM r̂ / r²` path (no extra trig).

### On-rails
Do **not** invent Kepler-with-J2 in this slice. Document in the test file: osculating Kepler on-rails ignores J2 (same class as “no third body”). Live RK4 vessels feel J2 because `Vessel` already sums `body.GetGravityAt(pos)`.

Do **not** force extra RK4 in LEO under warp. That is a later scheduler change.

### Tests (required)
New `ExosphereSimulation.Tests/J2GravityTests.cs`:
1. `j2 == 0` matches current magnitude/direction at a non-equatorial test point (pin `PhysicsRegressionTests.GravityAtEarthRadiusMatchesGmOverRSquared` semantics).
2. Polar `|g|` > equatorial `|g|` at equal radial distance for Earth J2.
3. Acceleration is purely radial on the equator of the body frame; polar acceleration is stronger and still toward the centre plus the known J2 axial term.
4. Finite, no NaN at surface and at 400 km.
5. RK4 circular equatorial LEO with J2: specific two-body energy is **not** conserved (document), but J2 potential + kinetic stays bounded; RAAN/argument rates for an inclined orbit have the **sign** of the analytic J2 rates (Vallado 9-41 / 9-42). Do not require percent-level SSO matching in v1.

Update `PhysicsRegressionTests.GravityAtEarthRadiusMatchesGmOverRSquared` so it still passes: either construct a J2=0 body or compare against the analytic point-mass + J2 formula instead of raw `GM/R²` if the loaded Earth now has J2.

Existing `Rk4CircularLeoConservesRadiusAndSpecificEnergy` uses an inline `GM/r²` lambda — leave it alone.

### Exclusive files
- `ExosphereSimulation/CelestialBody.cs`
- `data/bodies/earth.json` (and other bodies **only** to add `j2` / `equatorial_radius`)
- `ExosphereSimulation.Tests/J2GravityTests.cs`
- `ExosphereSimulation.Tests/PhysicsRegressionTests.cs` (only the gravity fact if it breaks)
- `PLAN_REALISM.md` (mark J2 v1 landed, keep “no rails J2” as known limit)
- `docs/audits/REALISM_UNRESOLVED_FRONTS_AUG2026.md` (Agent A status)

### Forbidden
`scripts/`, `assets/`, `tools/visual_playtest.sh`, `Universe.cs` scheduler, `Vessel.cs` force loop, `EDLController.cs`, `ThermalModel.cs`, `AerodynamicsModel.cs`, `jupiter.json`/`saturn.json` orbital elements (Agent B).

### Verify
```bash
dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --nologo --filter "FullyQualifiedName~J2GravityTests|FullyQualifiedName~PhysicsRegressionTests"
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --nologo
```
0 warnings, 0 errors. Do not run Godot visual playtests.

## Agent B — R15b Jupiter / Saturn J2000 phase

### Scope
Fix `mean_anomaly_at_epoch` (and `argument_of_periapsis` if `Ω + ω` is off `ϖ`) so they match the Standish J2000 mean elements already cited in `EphemerisPhaseTests`.

Published anchors already in that test file:
- Jupiter: `L = 34.39644°`, `ϖ = 14.72847°` ⇒ `M = L − ϖ`
- Saturn: `L = 49.95424°`, `ϖ = 92.59887°` ⇒ `M = L − ϖ`
- `ω = ϖ − Ω` reduced to `[0, 360)` if the existing `Ω + ω` test would fail.

Then add `jupiter` and `saturn` to **both** theories in `EphemerisPhaseTests`. Do **not** loosen `ToleranceDeg = 0.05`.

Do not change semi-major axis, eccentricity, inclination, or node unless required for `ϖ = Ω + ω`. Prefer adjusting `argument_of_periapsis` and `mean_anomaly_at_epoch` only.

### Exclusive files
- `data/bodies/jupiter.json`
- `data/bodies/saturn.json`
- `ExosphereSimulation.Tests/EphemerisPhaseTests.cs`
- `PLAN_REALISM.md` (R15b checkbox only)
- `docs/audits/REALISM_UNRESOLVED_FRONTS_AUG2026.md` (Agent B status)

### Forbidden
`earth.json`, `moon.json`, `CelestialBody.cs`, any `scripts/`, visual tools.

### Verify
```bash
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --nologo --filter "FullyQualifiedName~EphemerisPhaseTests"
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --nologo
```

## Agent C — physics-reviewer (after A)

Read-only. Attempt to refute J2 sign, equatorial-frame basis, Re vs mean radius, and energy-test updates. No style comments. No visual scope.

## Integration (coordinator)

1. A and B run in isolated worktrees.
2. Merge B first (tiny data). Re-run full xUnit.
3. Merge A. Re-run full xUnit + `dotnet build` of `Exosphere.csproj`.
4. Launch Agent C against the J2 diff.
5. Godot smoke only if game-layer files changed (they must not).
6. Do not commit to `main` from this plan unless the user asks. Agent branches may commit locally.

## Agent A status — 2026-08-21 (updated)

Tierra WGS84 + J2 **armados** en `earth.json`. Superficie geodésica, pads, contacto y
`GetGravityAt` Vallado. Hold reconstruye desde un rayo **geocéntrico** intersectado con
el elipsoide (no `a + offset`, que caminaba ~14 m en el Cabo). Kepler on-rails sigue
dos-cuerpos; el jugador en LEO (RK4, incl. warp bajo 1000 km) siente J2. Reconstrucción
MA-6 retuneada al Cape a cota geodética (tape 22° BECO / −17° inserción; LR-105 vacío
calibrado 625 kN / 350 s, no química publicada). Periapsis en la banda publicada
(~158 km vs mean radius); el apogeo circulariza más que el 261 km histórico.
Planner lunar Lambert vs J2 bajo 1000 km queda como deuda: el coast de SOI arranca
fuera de `ThermosphereTopAltitude`.

## Agent B status — 2026-08-21

R15b (Jupiter/Saturn J2000 phase) lives on `fix/r15b-jupiter-saturn-j2000-phase` (`cbafed5`), not this Earth WGS84 branch. `EphemerisPhaseTests` still excludes those bodies at 0.05° so a ~0.3° error cannot hide inside a looser pin.

## Agent C status — 2026-08-21

[Physics Review](54e57158-02b3-4e69-a7b3-8d015a6752cd): Vallado J2 **correcto** (−∇U ~1e-9; signos RAAN/ω OK).
**INCORRECTO armarlo en Tierra** hasta: geoide oblato (no radio medio), rieles/Kepler con J2
o J2 suprimido de forma explícita, y planner lunar iterando el mismo campo. Suite actual
(JSON desarmado) 708/708.

## Success for this wave
