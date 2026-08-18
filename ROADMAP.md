# Exosphere Roadmap

Este es el roadmap vivo del proyecto. Los planes viejos `PLAN_MEJORAS.md`,
`PLAN_MEJORAS_R15.md` y `PLAN_MEJORAS_R16.md` fueron cerrados/retirados. La
auditoria tecnica de fisica vive en `PLAN_REALISM.md`; el proximo frente
visual vive en `PLAN_VISUAL_REALISM.md`.

## Estado Actual

Base tecnica cerrada en `main`:

- Builds .NET/Godot esperados: 0 warnings, 0 errores.
- `ExosphereSimulation.Tests` cubre gravedad, RK4, Kepler, radial/suborbital,
  rails-impact, motores individuales, termica de escudo, aerodinamica, SOI,
  navegacion, persistencia V2, payloads, variantes y VAB.
- Godot headless carga la escena principal y la escena de construccion.
- CI descarga Godot 4.6.3 mono, compila la capa Godot C#, corre smoke headless y
  mantiene un guard contra harnesses temporales commiteados.
- VAB 2.0 esta conectado al vuelo: catálogo con búsqueda/filtros, doble-click y
  drag/ghost auto-attach, snap, rotación, simetría, preview/picking 3D, undo/redo,
  timeline de etapas, analizador, payload bay, checklist, save/load y launch al pad.
- `CraftDocumentV2`, `SaveGameV2`, IDs estables, multi-vessel y
  `SetActiveVessel` preservan crafts, sistemas, navegación, payloads y estado de
  campaña; los payloads desplegados se vuelven vehículos controlables.
- Docking físico one-to-one ya tiene puertos data-driven, captura limitada por
  distancia/velocidad/alineación, conservación de momento, vínculo rígido,
  desacople y round-trip en `SaveGameV2`. Es la base de Gemini 8; estaciones
  multimódulo e inercia agregada permanecen en su hito posterior.
- Falcon 9 Block 5 y New Glenn 7x2 tienen presets fechados, motores/clusters
  data-driven, procedencia obligatoria y escenarios propios.
- Starship Flight 7 Block 2/Raptor 2 y Flight 12 V3/Raptor 3 son variantes
  históricas separadas. Flight 12 usa 33+6 motores según SpaceX; empuje, Isp,
  masas y transitorios no publicados están marcados como modelo de ingeniería
  restringido. El sobre FAA futuro 35+9 permanece `regulatory_envelope`.
- La campaña histórica tiene manifiesto data-driven de 16 misiones,
  `CampaignService`, `MissionDirector`, `MissionEvaluator`, debrief numérico,
  recompensas idempotentes y persistencia V2. Freedom 7, Friendship 7, Gemini 8
  y Apollo 8 tienen variantes históricas jugables. Apollo 11 ya dispone del
  hardware fechado AS-506/CSM-107/LM-5, Eagle operativo, TD&E y LOI docked
  hasta órbita lunar circular; alunizaje/retorno son el siguiente corte.
- Starship/Super Heavy tiene malla procedural semántica por familia/rol, diámetro
  de 9 m, hot-stage
  ring, grid fins, flaps, tiles windward, motores 33/6 visuales, acero procedural,
  charring termico, bordes de heat shield, patron de tiles, payload-door cues,
  seams longitudinales, pluma liftoff mas densa y Super Heavy separado con anillo
  expuesto/quemado. Los motores 33/6 tienen estado, feed, gimbal, telemetría,
  fallos y pluma individual; ya no son solamente una multiplicación visual. El
  torque por geometría real de cada mount ya se calcula (`PartGraph.GetTotalTorque`) y
  el TVC diferencial por motor (R5b: `PartGraph.SolveDifferentialGimbal`) comanda cada
  mount gimballed hacia el torque pedido; el torque real por mount se aplica tanto con
  input como sin él (R5c); `GetThrustVector` suma vectores por mount (R5d) sin diluir
  mounts fijos al promediar gimbal del cluster.
- El entorno de lanzamiento tiene una primera pasada costera/industrial con
  caminos, relleno, juntas, bermas y detalles de deluge visibles desde pad.
- Ascenso [G] usa gravity turn mas realista y hot-staging en MECO.
- Reentry/EDL Starship esta validado por telemetria: belly-flop sostenido,
  flip-and-burn bajo y touchdown sobre seis patas físicas con resorte, damping,
  fricción, torque, límites de carga/recorrido y asentamiento persistente.
- Interplanetario incluye Hohmann, patched-conic SOI transitions, encounter
  prediction, marcador/readout de encuentro y readout de maniobra. La base lunar
  ya resuelve Lambert geocéntrico contra la efeméride móvil, busca una ventana
  sobre la órbita de estacionamiento, corrige enfoque gravitatorio en el B-plane
  y verifica entrada continua a la SOI lunar a warp máximo.
- Audio de vuelo derivado de física real: airflow por presion dinamica, brillo por
  Mach, buffet transonico/max-Q/entrada, rugido de plasma con el mismo flujo termico
  que la bola de fuego, y motor airborne vs structure-borne segun densidad ambiente.
- Marco de lanzamiento real (RF-01): el pad usa lat/lon de `data/launch_sites`, hereda
  ω·R·cos φ (Kennedy: 408 m/s al este, antes 185) y el gravity turn persigue el este del
  eje de giro. Staging pasa de 2156 a 2308 m/s, entrando en la banda 2,2–2,5 km/s.
- Atmosferas por planeta (RF-06): gravedad y masa molar propias (Marte/Venus usaban aire
  terrestre bajo g terrestre), altitud geopotencial, capas USSA-76 completas (T/P/rho dentro
  de 0,02%) y termosfera de escala creciente (1,14x vs NRLMSISE). El flujo termico en la
  interfaz de entrada se duplica: ahora hay atmosfera real por encima de 86 km.
- TPS de dos nodos (RF-07): piel de losetas en equilibrio radiativo (~1420 K) sobre una
  estructura acoplada por conduccion. La reentrada era **imposible de fallar** (la nave
  llegaba a 292 K tras 400 s de flujo pico); ahora la actitud decide: belly-flop sobrevive,
  tumbando se quema. Las losetas ademas por fin brillan y se carbonizan.
- Panel THERMAL en el HUD del EDL: cara del TPS, barra de casco contra tolerancia y
  alineacion del escudo con el flujo (el unico numero accionable), con aviso SHIELD OFF FLOW
  solo cuando hay flujo real detras. Sin el, la reentrada letal mataba a ciegas.
- Impactos de superficie anclados al cuerpo ✅: un wreck destruido ya no queda
  congelado en el marco heliocentrico mientras la Tierra se aleja, ni parece
  rebotar a ~30 km/s. El anclaje rotante persiste en `SaveGameV2` y bajo warp.

## Prioridad Inmediata

La siguiente etapa no debe abrir un sistema grande nuevo. Primero hay que subir la
fidelidad visual y asegurar que lo existente se pueda validar con capturas:

0. **Optimización multiagente fase 23**
   - El plan operativo, ownership, worktrees, baselines y gates viven en
     `docs/audits/OPTIMIZATION_MULTI_AGENT_PHASE23_PLAN.md`.
   - Oleada 1 integrada en `25f6bde`: cache de telemetría de motores, cobertura de wake-up,
     early-out térmico de render y gates de workers/QA. La suite integrada queda en 571 tests.
   - El orden obligatorio es medir → optimizar una superficie → probar equivalencia →
     integrar. No se activa hibernación física por distancia ni se declara una mejora GPU
     mientras el host sólo exponga llvmpipe.
   - El primer baseline reproducido el 2026-08-14 deja `mixed_fleet` en p95 3.7288 ms y
     718,566 B/tick; estos valores son comparativos de la máquina, no un objetivo universal.
   - **Fase 45 en curso:** la política `SimulationInterestPolicy` y el snapshot externo de
     misión/sistemas ya tienen adapter observacional, razones de wake para actitud, callbacks
     y alertas de soporte vital/energía/térmica/comms, y fixtures de paridad. Commit integrado
     `b3afcf3`; 656 tests, CI y smoke visual pasan. El scheduler sigue en FullPhysics hasta
     comparar `EventDriven/Dormant` contra la referencia con deadlines, recursos y eventos.
   - **Fase 46:** la matriz `FullPhysics` vs `DeferredRails` ya cubre proyección, deadlines,
     SOI, atmósfera, contacto, staging, actitud y consumo de propelente. El gate focalizado
     pasa 6/6; informe en `docs/audits/PERF_DISPATCH_PARITY_PHASE46_REPORT.md`. Aún no se
     promueve `EventDriven/Dormant`: en esa matriz todavía faltaban deadlines de sistemas,
     callbacks y save/load; la fase 47 cerró la parte de deadlines sin activar el scheduler.
   - **Fase 47 en curso:** soporte vital, batería y térmica ya proyectan sus próximos umbrales de
     alerta en el snapshot observacional. La térmica reutiliza el balance del último tick
     comprometido y devuelve `null` si no hay muestra válida. La política conserva `EventDriven`
     fuera de 60 s y despierta en `SystemsDeadline` dentro de la ventana; comunicaciones
     geométricas, callbacks pendientes y save/load siguen bloqueados hasta tener fuentes
     autoritativas.
   - **Fase 48:** auditoría de persistencia/eventos en
     `docs/audits/PERF_PERSISTENCE_EVENT_PARITY_PHASE48_REPORT.md`. `SaveGameV2.Systems` conserva
     extensiones JSON y `VesselSystems` añade un snapshot tipado por nave; el controlador de
     juego actualmente captura/restaura sólo la nave activa en el mismo epoch. La cola de
     comandos y callbacks aún no es persistente; el runtime diferido sigue bloqueado.
   - **Fase 49 en curso:** snapshot de soporte vital, energía, térmica y comunicaciones integrado
     en save/load con validación de identidad, epoch y límites físicos; además, los cambios de
     fase y `LaunchCommitted` usan un log ordenado de callbacks con entrega síncrona compatible.
     El estado tipado de systems y la cola de callbacks ya están integrados; el controlador
     actual todavía sólo materializa la nave activa. La promoción del scheduler sigue bloqueada.
   - **Fase 50:** matriz CPU de promoción en
     `docs/audits/PERF_PROMOTION_PARITY_PHASE50_REPORT.md`. Los seis escenarios de coast,
     save/resume, staging, docking, SOI y EDL/catch pasan contra una referencia FullPhysics
     al mismo epoch. El caso staged sin control queda en `Active` por seguridad y el catch
     EDL nunca entra en trabajo diferido. `SimulationInterestPolicy.EnabledByDefault` y el
     dispatcher runtime permanecen sin cambios; faltan systems por nave, ruta diferida real,
     medición de flota y validación visual antes de promover.
   - **Fase 51 en HOLD controlado:** A–D materializan systems por vessel, persisten el mapa
     y enrutan callbacks por owner; E añade un candidate de física diferida y F cierra su
     telemetría/contrato de paridad detrás de
     `simulation/deferred_physics_candidate_enabled=false`. La evidencia está en
     `docs/audits/PERF_PER_VESSEL_SYSTEMS_PHASE51_PLAN.md` y
     `docs/audits/PERF_DEFERRED_CANDIDATE_PHASE51_REPORT.md`; G confirma que el scheduler es
     <1% del frame bajo llvmpipe, pero la matriz completa no alcanzó órbita dentro de 1200 s.
     H confirma el mismo cuello en smoke y deja pendiente corregir el wrapper de medición.
     Falta catch-up temporal de consumibles por vessel y perfilado render/UI con GPU real antes
     de promover cualquier hibernación.
   - **Fase 52:** `SystemsHUD` redibuja a 10 Hz y `AttitudeNavball`/`AttitudeDataStrip` a
     30 Hz, manteniendo redraw inmediato al aparecer y sin tocar la física. Builds, suite
     `696/696`, startup y contratos pasan. El A/B framebuffer queda pendiente porque Xvfb
     perdió el display antes de cargar Godot; no se declara una ganancia de FPS. Ver
     `docs/audits/PERF_HUD_CADENCE_PHASE52_REPORT.md`.
   - **Fase 53:** la captura del HUD principal se limita a 30 Hz; input, throttle y toast
     siguen por frame, y cambios de nave/fase/vista fuerzan actualización. Baseline CPU:
     `0.019567 ms` p50 y `922.2 B` por captura; suite `696/696`, build, startup y contratos
     pasan. El A/B GPU sigue bloqueado por Xvfb. Ver
     `docs/audits/PERF_HUD_MAIN_CADENCE_PHASE53_REPORT.md`.
   - **Fase 54:** `TimeToPeriapsisS` se calcula una vez dentro del snapshot y el HUD elimina
     su segunda resolución orbital. La paridad, suite `696/696`, builds, startup y contratos
     pasan; no se declara FPS hasta disponer de framebuffer/GPU válido. Ver
     `docs/audits/PERF_HUD_ORBITAL_DEDUP_PHASE54_REPORT.md`.

1. **Visual fidelity Starship/Super Heavy**
   - Primera pasada cerrada: acero inoxidable, weld lines, tile layout, heat-shield
     edge, soot/frost, vents, raceways, grid fins, flaps y engine bay.
   - Grid fins close-up V1 implementado con placa trapezoidal, hinge/lattice y diagonales.
   - Starship close-up cues V1 implementado con access panels, vent/drain ports,
     markings discretos, flap leading edges y tile seams.
   - Proporciones finas flaps/nariz V1.1 ✅ (`feat/visual-realism-a`): forward cortos,
     aft elevons largos, tip redondo, tile seams densos. Pendiente: compare IFT lado-a-lado.
   - Startup/ramp y hot-staging VFX implementados y verificados con trigger local multiframe.
   - Pluma de vacio ahora atenúa smoke/soot con expansion alta.
   - Harness de captura de hot-staging en ascenso real ya existe (`tools/visual_playtest.sh --hotstage`,
     vuela un ascenso `[G]` Flight 7 real y captura la ventana de overlap gateada en
     `Vessel.IsHotStageOverlapping`; verificado con xvfb → `exo_play_hotstage.png`). Siguiente:
     la comparacion contra referencia y la validacion orbital de pluma vacio limpia, que ya no
     dependen de tooling.

2. **Reentry visual**
   - Plasma/shock layer mas fisico, ligado a heat flux y densidad atmosferica.
   - Primera pasada de glow localizado en nose, belly y flap leading edges ya implementada.
   - Alpha/timing por fase EDL ✅ (`ReentryPlasmaVisualIntensity`: ENTRY soft → PEAK → AERO fade).
   - Harness de comparacion ya existe (`tools/visual_playtest.sh --reentry-compare`, captura
     belly-flop nominal vs EDL con mala actitud forzada via `SimulationBridge.BeginReentryDemonstration(bellyFirst:...)`;
     verificado con xvfb → `exo_play_reentry_nominal.png` / `exo_play_reentry_bad_attitude.png`).
     Pendiente: el ajuste/comparacion visual en si (alpha, timing, zone charring) contra IFT.

3. **Entorno y camaras**
   - Pad costero ya tiene primera pasada visual.
   - Siguiente: iluminacion solar, exposicion, sky/atmosfera y camaras para que
     launch/orbit/reentry/cockpit se lean como escalas reales.

4. **Capturas de aceptacion**
   - Automatizar capturas con framebuffer real para pad, liftoff, Max-Q, staging,
     orbit/map, belly-flop reentry, flip-and-burn, touchdown/crash y cockpit.
   - `--hotstage` y `--reentry-compare` ya cubren hot-stage overlap y comparacion
     nominal/mala-actitud de EDL; falta el resto de la matriz de capturas listada arriba.

## Sistemas Cerrados Que No Se Deben Rehacer Sin Motivo

- RK4/Kepler/on-rails y patched conics.
- Guardas radial/suborbital y destruccion por impacto.
- Heat-shield data-driven con orientacion de flujo.
- Ascenso [G] y EDL R13. Cualquier cambio debe preservar sus telemetrias.
- VAB catalog/assembly/export y picking actual, incluido el rediseño de UI con
  el tema glass compartido.
- Detalle visual dedicado de Falcon 9 Block 5 y New Glenn 7x2 (`rocket-visual-design`).
- Catch de la torre (Mechazilla): Ship (`EDLController` Catch/Caught) y booster
  (`BoosterReturnController` boostback→entry burn→catch) reutilizan cuna/pines/
  `Universe.EvaluateCatchContact`. Cradle refresh y chopsticks son multi-vessel
  (el booster puede atraparse mientras Ship sigue activo). HUD muestra `BOOSTER …`
  sin alterar `MissionPhase` del Ship.

## Pendientes Reales

### VAB / Construccion

- Mejorar el feedback visual de nodos compatibles/incompatibles.
- Completar navegación con mando en cada modal y edición avanzada de action groups.
- Añadir goldens de crafts guardados y migrados antes de launch.

### Visual Starship/Super Heavy

- Plan detallado: `PLAN_VISUAL_REALISM.md`.
- Capturas de aceptacion con framebuffer real.
- Engine-out por instancia y plumas individuales están implementados. Pendiente:
  benchmark/golden sostenido con 39 motores y comparación visual de Raptor 3.

### Reentry Fisico/Visual

- Per-piece structural breakup ✅ (oleada B1: overloaded joints → debris vessels).
- Control-loss consequences ✅ (`ControlAuthority`: dead-stick / flaps-only / engines-only; HUD + EDL/ascent abort).
- Perdida de control si falla una pieza critica.
- Lift/AoA en sim ✅ (R6); guiado EDL lift-up ~70° ✅ (`EDLController` + `ComputeLiftUpEntryAxis`).
- Decaimiento orbital LEO ✅ (R7 termosfera residual + B3: warp/on-rails ya no congela LEO).

### Interplanetario

- Tests de cruise muy largo ✅ (crucero interplanetario y transición SOI a warp).
- Fundación de transferencia lunar geocéntrica ✅ (`LambertSolver` +
  `LunarTransferPlanner`): TLI, encuentro, B-plane, perilunio lunar y prueba
  end-to-end sin teleport. El mapa ya selecciona esta ruta para `moon`, centra
  la quema TLI finita sobre su época impulsiva, muestra la trayectoria Tierra–Luna, SOI, perilunio y
  estimación LOI, y rechaza ventanas de plano que exigirían TLI >4,5 km/s.
  Pendiente: maniobra LOI ejecutable, ventana multi-día y dataset lunar fechado.
- Nodos de maniobra arrastrables con mouse.

### Gameplay

- Control ownership jugable ✅ — stick a bordo crewed (`PilotCommandRouting`),
  `[G]` cede a WASD, cue MANUAL/ASCENT/EDL/HISTORICAL; hold-down unificado
  (TWR>1.05 + throttle≥0.95); cluster Minimal motores–navball–strip.
- Save/load V2 de misión y multi-vessel — `SaveGameV2` + F5/F9 quicksave +
  MainMenu Continue; round-trip y migración probados.
- Flujo jugable orbita → deorbit → ENTRY (oleada C2) ✅ — mapa `[B]`
  (`DeorbitPlanner` + `ManeuverPlanner.PlanDeorbit`); EDL arma `ENTRY` sin teleport demo.
- Cues/track de fases EDL (oleada C3) ✅ — `MissionPhaseTrack` + HUD dots
  ORBIT→COAST→RETRO→ENTRY…; cue “ENTRY INTERFACE in ~Xm” / “DEORBIT BURN”.
- Fundación de misiones/objetivos/progresión ✅ — catálogo estricto, evaluación
  pura, evidencia persistente, debrief y ledger idempotente.
- Freedom 7 / Mercury-Redstone 3 ✅ — misión headless completa y capturas de
  pad/liftoff.
- Friendship 7 / Mercury-Atlas 6 ✅ — misión headless de tres órbitas, retrofire,
  reentrada, splashdown y capturas de pad/liftoff.
- Fundación de docking Gemini 8 ✅ — puertos data-driven, hard dock conservativo,
  desacople y persistencia V2. Pendiente: hardware Titan II/Gemini/Agena e
  incidente OAMS de la misión.
- Gemini 8 ✅ — Spacecraft 8, Titan II GLV-8, LC-19, Armstrong/Scott y Agena
  5003 cierran las masas/dimensiones publicadas; el perfil ejecuta staging,
  rendezvous, docking, anomalía OAMS-8, desacople, recuperación de control y
  retorno de emergencia. Evidencia, procedencia, prueba headless y captura
  orbital de tasa angular están automatizadas.
- Apollo 8 ✅ — AS-503/CSM-103/LTA-B, tripulación, LC-39A, doce motores
  individuales y SPS cierran masas, staging, empuje y procedencia. El director
  ejecuta TLI Lambert geocéntrico, cruce patched-conic de SOI, LOI,
  circularización, diez órbitas lunares, TEI Lambert, entrada y splashdown.
  Evidencia lunar persistente, debrief, captura de launch y CSM en órbita lunar
  están automatizados con `--apollo8` / `--apollo8-lunar`. Pendiente:
  misiones jugables 5–16.
- Apollo 11 hardware ✅ — AS-506, Columbia CSM-107 y Eagle LM-5 cierran las
  6.484.280 lb de ignición y las 33.205 lb del LM con procedencia NASA. F-1,
  J-2, SPS, DPS y APS son modelos fechados separados; Eagle conserva masa al
  separar descenso y activa APS tras DPS. `--apollo11` valida pad/liftoff.
- Apollo 11 TD&E + LOI ✅ — `apollo11-lunar-landing-return` + `mission-apollo11-1969`:
  parking→TLI→CSM sep→extract Eagle→hard-dock→LOI→órbita lunar circular;
  finalize de campaña en `LUNAR_ORBIT`. Ver `docs/HITO4_APOLLO11_TDE.md`.
  Pendiente: DOI, alunizaje, ascenso LM, rendezvous, TEI, entrada y amerizaje.
- Recursos de vida, energia, comunicaciones y termica conectados a fases reales.
- Fallos, damage consequences y recuperacion.

### CI / Visual Testing

- Captura PNG end-to-end con Xvfb y harness temporal para menú, VAB, Falcon 9,
  New Glenn, Flight 7, Flight 12, launch, ship, cockpit, atmósfera y EDL.
- Métricas mínimas de screenshots detectan pantallas negras, UI rota o render sin nave.
- Mantener el guard anti-harness: no commitear `scripts/_*Shot.cs`,
  `scripts/*VerifyShot.cs`, `scenes/*VerifyShot.tscn` ni autoloads temporales.

## Orden Recomendado

1. Ejecutar `bash tools/ci_check.sh` antes de tocar visuales.
2. Cerrar el siguiente bloque visual real:
   - comparacion contra referencia del hot-staging en ascenso real (harness `--hotstage`
     ya listo; falta el juicio de comparacion/ajuste);
   - comparacion de startup/ramp contra referencia real;
   - validacion de pluma de vacio contra captura orbital;
   - reentry visual avanzado solo en lo pendiente: nose/leading edges, capturas
     nominal/fallo y legibilidad HUD (harness `--reentry-compare` ya listo; falta el
     ajuste de alpha/timing/zone charring).
3. Agregar capturas de aceptacion reproducibles con matriz V0.5.
4. Mejorar camara/luz/atmosfera.
5. Recien despues: Apollo 11 alunizaje (DOI→surface→APS→rendezvous→TEI).
   (R11 ✅; R12 ✅; Apollo 11 TD&E + LOI ✅)
