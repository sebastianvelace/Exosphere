# Phase 51 — materialización de systems por vessel y despliegue multiagente

Fecha de planificación: 2026-08-17
Estado: **A–E implementados; candidate experimental en HOLD y runtime diferido aún desactivado**

Estado de integración verificado:

- A runtime puro: `011d568`.
- B registry/ownership: `5281e6e`, `b87189d`.
- C save/load materializado: `c104e8e`.
- D callbacks/wake boundary: `f974583`.
- E candidate opt-in: `9504874`.
- Suite posterior a E: `693/693 PASS`; ambos builds en `0 warnings / 0 errors`.
- La decisión y los bloqueos de promoción están en
  `docs/audits/PERF_DEFERRED_CANDIDATE_PHASE51_REPORT.md`.

## Objetivo

Eliminar el supuesto de que sólo existe un `SystemsController` global sin activar todavía
la hibernación física. Cada vessel que pueda cambiar de ownership debe tener un estado de
life support, energía, térmica y comunicaciones identificado por su `VesselId`, con epoch
común y una política explícita para estados no materializados.

La fase parte de este baseline reproducido:

- suite xUnit: `677/677 PASS`;
- build `Exosphere.csproj`: `0 warnings / 0 errors`;
- build `ExosphereSimulation.csproj`: `0 warnings / 0 errors`;
- smoke framebuffer: `SMOKE_OK`;
- `SimulationInterestPolicy.EnabledByDefault == false`;
- matriz de promoción CPU fase 50: `6/6 PASS`.

## Restricciones no negociables

1. Ningún sistema consume `wall-clock delta`; sólo puede consumir los segundos realmente
   comprometidos por `Universe.LastSchedulerTelemetry.ProcessedSimulationSeconds`.
2. Un snapshot de systems sólo es válido si `VesselId` y `SimulationTime` coinciden con el
   snapshot físico del mismo save. No se aceptan estados “por defecto” inventados para una
   nave que ya tenga historial.
3. El estado no materializado de una nave debe bloquear `EventDriven/Dormant` o provocar una
   materialización completa antes de cualquier tick diferido. El fail-closed es obligatorio.
4. `GroundCommandRelay` no se serializa como comandos vivos hasta tener eventos con epoch y
   vínculo de vessel; al cambiar de nave o hacer `J`, la cola anterior se descarta.
5. Callbacks de misión conservan secuencia global y entrega síncrona actual. Una nave no
   puede recibir callbacks de otra por reutilización de un controller.
6. El dispatcher oficial no cambia durante esta fase. La flag experimental, si se crea,
   debe tener valor por defecto `false` y no debe modificar el coste del frame normal.

## División de agentes y ownership

Cada agente trabaja en un worktree aislado desde el commit base `1aa689c`. Los IDs de
playtest son obligatorios (`--run-id phase51-<agent>`) y ningún agente puede editar un
archivo fuera de su ownership sin un commit de coordinación.

### Agente A — núcleo puro de systems

Ownership: `ExosphereSimulation/Systems/` y tests puros de systems.

Entregables:

- `VesselSystemsRuntime` o equivalente puro, sin Godot, que agrupe los cuatro sistemas y
  exponga `Tick`, `CaptureState`, `RestoreState`, `Reset` y deadline mínimo;
- estado de fase/crew y muestras térmicas explícitas, sin acceso estático al vessel activo;
- validación de identidad/epoch y valores finitos;
- pruebas de determinismo: dos runtimes con el mismo snapshot y el mismo delta producen
  estado byte-a-byte equivalente en recursos y aproximadamente equivalente en alertas.

No puede cambiar fórmulas de consumo, blackout ni umbrales existentes.

### Agente B — registry y ownership de nave

Ownership: `scripts/SystemsController.cs` y un nuevo registry del game layer.

Entregables:

- mapa estable `VesselId -> runtime materializado`;
- transición atómica al cambiar `Universe.ActiveVessel`:
  `capture(old, epoch) -> select(new) -> restore/reset(new)`, sin tick intermedio;
- HUD, EDL, relay y autopiloto consultan sólo el runtime del vessel activo;
- si el vessel no tiene snapshot autoritativo, la API devuelve “unmaterialized” y la
  decisión de interés queda fail-closed;
- no duplicar `LifeSupportSystem`/`PowerSystem` en cada frame ni crear controllers Godot
  por nave.

### Agente C — save/load y migración

Ownership: `scripts/SaveSystem.cs`, `ExosphereSimulation/Persistence/` y tests de persistencia.

Entregables:

- capturar todos los runtimes materializados con el mismo `Universe.CurrentTime`;
- restaurar el mapa antes de permitir callbacks o cambio de nave;
- saves legacy sin `VesselSystems` restauran sólo defaults para naves nuevas y marcan como
  no materializadas las demás;
- rechazar snapshots duplicados, epoch incorrecto, vessel inexistente y estados nulos;
- round-trip con dos naves, cambio de active vessel, staging y docking.

### Agente D — deadlines, callbacks y wake boundary

Ownership: `MissionManager`, `SimulationBridge` y adaptadores de `SimulationExternalInterestInputs`.

Entregables:

- deadlines de systems calculados desde el runtime correcto, no desde el singleton anterior;
- callback pendiente global conserva orden y activa la nave propietaria si el evento tiene
  owner; eventos sin owner permanecen en la cola global;
- staging, docking, undock, SOI, `J`, destrucción y catch invalidan/marcan el runtime
  correcto;
- pruebas de que una nave sin systems materializados nunca entra en `Dormant`.

### Agente E — scheduler experimental

Ownership: `ExosphereSimulation/Universe.cs`, `SimulationInterestPolicy` y pruebas de
paridad. Depende de A–D; no debe empezar a conectar el switch antes de recibir sus contratos.

Entregables:

- sólo una ruta candidate detrás de opción explícita de desarrollo;
- presupuesto/deuda temporal exactos, sin descartar segundos;
- materialización antes de despertar y `CatchUp` antes de fuerza, SOI, contacto o docking;
- telemetría por vessel: tier, wake reason, último epoch materializado, deadline y motivo
  de fallback;
- comparación contra FullPhysics para coast, systems deadline, callback, save/resume,
  staging, docking, SOI y EDL.

### Agente F — pruebas y harness visual

Ownership: `ExosphereSimulation.Tests/`, `tools/visual_playtest.sh` y contratos de CI.

Entregables:

- matriz CPU parametrizada por nave activa/no activa y materializada/no materializada;
- pruebas de dos naves intercambiando ownership varias veces;
- playtest con salto `J`, Flight 7, staging, docking, deorbit, totalidad y catch;
- métricas: `ProcessedSimulationSeconds`, deuda pendiente, dispatches, alert deadlines,
  callbacks pendientes, allocations/tick, memoria de LUT y frame time;
- artefactos separados por `--run-id`; ningún harness temporal queda en git.

### Agente G — auditoría y merge gate

Ownership: sólo reportes `docs/audits/`, checklist y revisión de diffs.

Entregables:

- tabla de invariantes antes/después por commit;
- revisión de que cada agente cambió únicamente su ownership;
- ejecución limpia de tests, builds, smoke y contratos visuales;
- decisión explícita `PROMOTE`, `HOLD` o `ROLLBACK CANDIDATE`.

## Secuencia de integración

```text
A (runtime puro)
  └── B (registry/active switch) ── C (save/load)
                                  └── D (events/deadlines)
                                           └── E (candidate scheduler)
F (tests/harness) ─────────────────────────┘
G (audit + merge gate) ────────────────────┘
```

Commits separados y ordenados:

1. `feat: add per-vessel systems runtime`
2. `feat: materialize systems on vessel ownership switch`
3. `feat: persist materialized systems map`
4. `test: cover systems wake and callback ownership`
5. `perf: add opt-in deferred systems candidate`
6. `test: run phase51 parity and visual matrix`
7. `docs: record phase51 promotion decision`

Cada commit debe poder compilar y pasar sus tests; sólo el commit 5 puede introducir una
flag candidate y su valor por defecto debe seguir desactivado.

## Gates de aceptación

### Gate 1 — estado puro

- snapshot/restore exacto en dos epochs;
- deadlines idénticos a los sistemas existentes;
- ninguna asignación por tick en el camino activo medida por el benchmark vigente;
- suite y build sin warnings.

### Gate 2 — ownership y persistencia

- cambiar A→B→A no mezcla O2, batería, temperatura, blackout ni callbacks;
- save/load conserva ambos mapas a un epoch idéntico;
- un vessel no materializado no puede clasificarse `Dormant`;
- staging/docking/undock no dejan referencias al runtime anterior.

### Gate 3 — candidate CPU

- error de posición/velocidad frente a FullPhysics dentro de las tolerancias de fase 50;
- energía, recursos y alerts monotónicos donde corresponde;
- `NaN`, deuda temporal, SOI, catch, docking y callbacks sin pérdida;
- coste y memoria documentados con al menos 32 naves coast y 4 force-sensitive.

### Gate 4 — visual y promoción

- smoke, ascent, ship/cockpit, orbital reentry y EDL/catch PASS;
- sin clipping amplio, nave negra, pérdida de motores, `ENG` inconsistente o giro después
  de `J`;
- `neonGreenFrac`, visibilidad estelar y exposición sin regresión;
- sólo entonces se evalúa activar el candidate en una build de desarrollo. La build normal
  permanece con FullPhysics hasta que G pueda marcar `PROMOTE`.

## Criterio de bloqueo

Si falta un snapshot de systems, el callback tiene owner desconocido, el epoch difiere, o
la ruta visual no puede demostrar continuidad, el resultado es `HOLD`, no una aproximación.
La optimización debe reducir trabajo sin cambiar qué nave recibe un comando, un deadline,
un contacto, una transferencia de SOI o un estado de misión.
