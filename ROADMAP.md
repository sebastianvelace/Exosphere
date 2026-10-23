# Exosphere Roadmap

> **Reconciled:** 2026-09-22 · **Current physics front:** legacy-versus-coupled 6-DoF parity.
> The historical phase waves below remain useful evidence, but the active source of truth is
> [`docs/README.md`](docs/README.md), [`docs/physics/PHYSICS_MODEL.md`](docs/physics/PHYSICS_MODEL.md)
> and this roadmap.

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
- Flight operations menu restored and reorganized (2026-10-01): orbital Earth
  backdrop replaces the black-hole title, Free Flight selects ten launchable
  presets and their compatible pads, and campaign briefings expose objectives,
  limits, completion and prerequisites. Responsive scrolling, keyboard focus
  isolation and Escape return are covered by real-framebuffer menu checks.
  Home content is centered horizontally and vertically; mission information lives
  in its briefing rather than a side panel.
  Flight 14 now exposes an automatic engineering exploration from the featured
  briefing: loaded ascent, insertion, 26 payload releases and estimated ship water entry.
  A presentation shortcut becomes available after all 26 payload releases: **SKIP TO
  ENTRY** advances the continuous physical mission and pauses at its entry interface.
  Bounded work keeps cancel/menu usable; the 3D view hides during advancement and
  returns at cancellation/arrival. Passive payload coast integrates gravity and
  residual drag at <=0.5 s, while both original stages retain their 50 Hz control steps.
  The independent booster estimate now reaches Gulf water contact with 31-engine
  boostback and 11/5/3 landing; camera observation preserves ship ownership.
  The earlier guidance calibration reduced the 207 km / T+582 s baseline to
  approximately 97 km / T+418 s. Resource-driven boostback pitch refinement now
  yields about 101.4 km / T+414.36 s, with lower aggregate altitude and
  atmosphere-relative speed residuals at five shared broadcast coast anchors.
  Retired-booster HUD values use the final witness instead of a moving-Earth
  recomputation. Control angles, protected inventory and exact speed frame remain
  unvalidated. See `docs/audits/flight14_booster_coast_calibration_2026-10-04.md`. Exact
  ocean targeting, post-entry flooding/capsize, booster timing/trajectory calibration
  and exact mission acceptance remain pending. Vehicle fidelity and further cloud work remain separate.
- VAB 2.0 esta conectado al vuelo: catálogo con búsqueda/filtros, doble-click
  auto-attach, preview/picking 3D, undo/redo, templates Starter/Starship,
  validación de launch, save/load, entrada desde MainMenu y launch al pad.
  Aún faltan gizmos de drag/rotar; drag/ghost, simetría y rotación de pieza
  no están en la UI de construcción.
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
  hardware fechado AS-506/CSM-107/LM-5, TD&E por RCS con captura real, LOI
  acoplado, DOI y descenso propulsado hasta el alunizaje; el ascenso del LM, el
  rendezvous y el retorno son el siguiente corte.
- Starship/Super Heavy tiene malla procedural semántica por familia/rol, diámetro
  de 9 m, hot-stage
  ring, grid fins, flaps, tiles windward, clusters 33/6, acero procedural,
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
- The leg-equipped Starship EDL fixtures cover belly-flop, flip-and-burn and six
  physical landing legs with spring/damper response, friction, torque, load/travel
  limits and persistent settling. This does not certify the no-legs V3 return:
  the 2026-09-30 continuous V3 run reached `LANDED` through simplified surface
  support, without a confirmed tower catch or stable multi-point contact.
- Atmosphere/entry audit (2026-09-30): continuous automated ascent→orbit→deorbit→entry,
  902 passing simulation tests, cloud/horizon corrections and hull-aligned plasma.
  Valid ~28 m/s deorbits now retain planner intent; high-entry footprint forecasting
  runs at bounded 5 Hz while control stays at 50 Hz. Full rendered mission acceptance
  remains open. See `docs/audits/atmosphere_reentry_visual_physics_2026-09-30.md`.
- Ascent surface compositing corrected (2026-09-30): geographic fragment depth,
  opaque Earth coverage and shared camera-to-ground optics replace the occluding
  proxy surface, gray LOD substitution and fixed cyan shell.
  See `docs/audits/ascent_compositing_fix_2026-09-30.md`.
- Starbase regional continuity: Landsat/BMNG context spans approximately 330 km,
  corrected raster extents and observed footprint masks replace the circular NAIP
  cutout. Geographic shore-distance blending retires offshore acquisition seams.
  See `docs/audits/starbase_regional_coverage_2026-09-30.md`.
  Spectral/exposure calibration, richer clouds and final reference acceptance
  remain open; historical context is not Flight 14 weather or current buildings.
- Cloud transport shares the Earth texture basis, simulation clock and elevated
  density field across the sky, local terrain and scaled Earth (2026-09-30).
  Starbase has an explicitly estimated coastal-cumulus appearance profile;
  foreground hull/plume cloud occlusion is implemented; cloud morphology, target-hardware performance and exact Flight 14
  camera/exposure matching remain open. See
  `docs/audits/cloud_foreground_2026-10-01.md` and
  `docs/audits/cloud_weather_lighting_2026-09-30.md`.
- Integrated-GPU presentation preset (2026-10-02): Settings persists Auto /
  Integrated GPU / Quality. Auto detects integrated adapters; the lighter profile
  scales only 3D to 75%, reduces solar cloud quadrature to two samples and disables
  screen-space lighting/reflections. Native telemetry and simulation are retained.
  Paired Radeon Forward+ cloud fixtures pass all six views with 55–64% lower GPU
  frame time in occupied/above-cloud views; dense cells still take approximately
  380 ms, so smooth gameplay performance remains open. See
  `docs/audits/integrated_gpu_preset_2026-10-02.md`.
- Near-pad cloud density reuse (2026-10-09): double-buffered, rendered-slice
  completion and stale-clock fallback retain shared foreground/terrain/sky transport.
  The same paused Radeon Quality fixture improves GPU time from 594 to 134 ms;
  smooth target-hardware performance remains open. See
  `docs/audits/cloud_density_cache_2026-10-09.md`.
- The exterior broadcast HUD has independent live engine boards, observed events,
  mission clock and piloting telemetry (2026-10-01). F3 density/cockpit/map
  transitions passed at 1280×720; actual ignition/liftoff passed at 1920×1080.
  See `docs/audits/flight_broadcast_hud_2026-10-01.md`.
  The shared band now follows the current/next propulsion stage for every catalog
  launcher, uses actual engine mounts, retains installed off/failed engines and
  resets on vessel switches. Atlas half-stage jettison and Saturn's four propulsion
  groups have topology tests; Mercury retro packs retain aggregate telemetry.
  See `docs/audits/all_vehicle_broadcast_hud_2026-10-01.md`.
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
- Auditoría física 2026-10-18 — cerrados con tests de escenario integrados: eje de giro
  planetario inclinado desde la normal eclíptica (estaciones, luz diurna y la Luna a
  18–29° del ecuador; antes el eje yacía en el plano orbital), paracaídas de Apollo 8/11 y
  Gemini VIII que nunca abrían (unidad Pa vs fracción), SAS limitado a la autoridad real
  de RCS/TVC y cargas de joints según el camino de carga real con compresión. Segunda
  etapa: warp alto en LEO y pad (coast termosférico a paso de 2 s, mismo decaimiento),
  masa acoplada en el empuje del docking y Mach con el gas de cada planeta. Pendientes:
  inercia combinada del stack acoplado, datum render/masas inconsistente (bloquea el
  6-DoF) y Cd transónico >2 (requiere recalibrar EDL contra telemetría IFT).
  Tercera etapa (jugabilidad): paracaídas armables con `[P]` en vuelo libre, el director
  EDL solo engancha vehículos Starship, ascenso `[G]`/`[H]` en el reloj fijo de 20 ms y
  sin que el stick ocioso pise al autopiloto, catálogo genérico del VAB coherente (tanques
  llenos a su volumen, geometría declarada, motor chico de 60 kN; el Starter sube a
  ~119 km en vez de estrellarse a 48 m) e inercia combinada del stack acoplado.
- Impactos de superficie anclados al cuerpo ✅: un wreck destruido ya no queda
  congelado en el marco heliocentrico mientras la Tierra se aleja, ni parece
  rebotar a ~30 km/s. El anclaje rotante persiste en `SaveGameV2` y bajo warp.

## Objetivo Activo (2026-10-21)

**Meta:** que toda misión de campaña se vuele de la plataforma al final por física, sin
asignar estado, y que cada etapa tenga un test headless y una corrida Godot verificable.
Cada ítem se cierra solo con su criterio cumplido, `tools/ci_check.sh` en verde y el CI de
GitHub en verde. Orden por impacto para el jugador:

1. ✅ **Cerrar el lote en curso (Apollo 8 de la plataforma al amerizaje).** Cerrado en
   e7a7c7c: amerizaje en Godot en 93 min de reloj, CI local y de GitHub en verde.
   Criterio: `visual_playtest.sh --apollo8-mission --until-phase LANDED` pasa en Godot con
   ventana de lanzamiento, TEI finita, dos correcciones transtierra y entrada sustentadora;
   suite completa, commit coherente y push; CI de GitHub verde con el paso de importación.
2. ✅ **Rendezvous de Gemini 8 sin teletransporte.** RCS de traslación, TPI por Lambert
   replanificada desde el estado volado, corrección a mitad de transferencia, aproximación
   por el eje y captura real; la falla del propulsor 8 es un torque físico con recuperación
   por el RCS de reentrada, y el retrofuego usa los cohetes sólidos (desprendidos al agotarse
   para exponer el escudo). El Titan II ahora sí llega a órbita (BECO por agotamiento, segunda
   etapa en lazo cerrado al plano de la Agena): antes el teletransporte ocultaba que el ascenso
   caía. Queda: la Agena nace sobre la plataforma (geometría de escenario), inserción en
   145 × 152 km (vuelo: 160 × 272) y reentrada balística (sin el L/D 0,2 de Gemini).
   Objetivos de campaña en dos niveles: obligatorios (esencia de la misión y límites de
   tripulación) y "Histórico:" opcionales (réplica exacta); el piloto automático de Freedom 7
   vuela ahora 187 km / 487 km / 15:22 como Shepard.
   Antes: Hoy `gemini.Position = target.Position + …`
   coloca la cápsula junto a la Agena. Criterio: fases físicas (NC/NSR/TPI con
   `LambertGuidedBurn`, aproximación final con RCS) hasta el contacto de docking real;
   test headless del encuentro y corrida Godot del modo `--gemini-docking`.
3. ✅ **Apollo 11 TD&E y descenso.** `--apollo11-mission --until-phase LANDED` vuela en Godot
   de la plataforma a la Base Tranquilidad sin asignar estado:
   - TD&E por RCS del SM con captura real;
   - LOI y circularización acopladas;
   - transferencia de tripulación por el túnel y desacople;
   - DOI y descenso `LunarPoweredDescent` (E-guidance hasta 150 m, descenso vertical), con
     contacto a 1,00 m/s.

   Quitar los teletransportes expuso otra vez fallas aguas arriba:
   - el AS-506 volaba con el reloj del AS-503 y llegaba al TLI sin propelente;
   - los J-2 tenían el Isp de catálogo en lugar del de vuelo;
   - la SLA cargaba 4,3 t de una asignación derivada;
   - el puerto del LM apuntaba hacia adentro.

   Queda: perilunio LOI 52 km (vuelo: 114) y la cuenta regresiva de la ventana lunar es de 9 h
   de espera.
4. **Persistir la ventana de lanzamiento y los burns guiados en `SaveGameV2`.** Hoy reanudar un
   ascenso guardado cae en la corrección de plano por script. Criterio: un save en ascenso,
   en costa translunar y en órbita lunar reanuda con el mismo plan y sin corrección.
5. **Datum render/masa inconsistente** (bloquea el 6-DoF acoplado). Criterio: un único origen
   de piezas compartido por `VesselRenderer`, inercia y contacto, con test de coincidencia de
   centro de masa render/física por familia de vehículo.
6. **Cd transónico de costado > 2.** Criterio: curva calibrada contra datos publicados y la
   telemetría IFT, sin romper los tests EDL de Starship ni el amerizaje del Flight 14.
7. **Paridad del integrador acoplado** (ver Prioridad Inmediata): ascenso controlado largo,
   SAS/flaps/RCS equivalentes, EDL con contactos; recién entonces evaluar activarlo.
8. **Harness.** Corridas paralelas (sin el lock global: autoload por variable de entorno o
   worktree), telemetría por cuadro opcional en misiones largas, y un modo de reproducción de
   misiones históricas en CI nocturno con `--until-phase`.

## Experiencia de Jugador (2026-10-22)

El jugador reporta que Apollo 8 y Gemini 8 son "realmente complicadas": no sabe dónde está la
Luna, cómo aterrizar ni cómo cumplir los objetivos. Un inventario del código lo confirma y va
más allá. **Volando a mano, los objetivos obligatorios no se pueden cumplir.** Solo el
director histórico marca las fases TLI, LUNAR_ORBIT y TEI; fuera de él, `LANDED` lo marca
únicamente el EDL de Starship (amerizaje de cápsula incluido). La Agena solo existe si el
director vuela, y el director es todo o nada: [L] le entrega la misión entera y no hay forma
de retomar el control. Tampoco hay teclas de traslación RCS ni lectura de rango/velocidad
relativa, y el navball solo trae vectores de superficie. El panel de objetivos corta en 6
filas (Apollo 8 tiene 11) y no existe guía paso a paso. Referencia de diseño: KSP
(checklists de misión del KSP 2, nodos de maniobra, modos SAS, marcadores de encuentro,
indicador de alineación de docking, ayudas de aterrizaje tipo KER/MechJeb).

Orden por impacto (cada corte con tests headless y verificación jugando en Godot). Primer
corte (2026-10-23): 1–5 entregados y probados jugando por Xvfb con teclado simulado. Al jugar
aparecieron bugs que los tests no ven, y ya están corregidos:

- soltar [K] en el ascenso cortaba el acelerador y el cohete caía;
- el panel TIME tapaba la guía;
- la línea lunar se dibujaba en la esquina;
- retomar con [K] usaba el reloj de campaña, que incluye la cuenta regresiva (10 s adelantado).

Quedan del 5 la fase NC/NSR guiada para el jugador; el 6, el 7 y el 8 están completos por
hacer.

1. ✅ **Reconocer lo que hace el jugador.** Detector físico de fases (órbita, TLI, aproximación
   lunar, órbita lunar, TEI, entrada, amerizaje/alunizaje) para vuelo manual, de modo que
   cualquier forma de llegar cuente para los objetivos.
2. ✅ **"Mostrame / Tomá el control" por fase.** Una tecla delega la fase actual al director
   (que ya sabe reanudar desde un estado) y otra devuelve el control; las fases asistidas se
   marcan como tales en el debrief, no fallan.
3. ✅ **Guía de misión.** Panel con el paso actual en lenguaje llano, la tecla a usar y los
   siguientes pasos (procedimiento por misión en el JSON, EN/ES); objetivos sin tope de filas
   y visibles en todas las vistas.
4. ✅ **¿Dónde está la Luna?** Marcador de Luna/objetivo en HUD y navball, distancia, cuenta
   regresiva al encuentro y estado "SOI / MISS" sin abrir el mapa.
5. ✅ **Kit de rendezvous manual (Gemini).** Agena siempre presente, objetivo seleccionable,
   rango y velocidad relativa, teclas de traslación RCS, indicador de alineación del puerto
   y captura real al entrar en el sobre.
6. **Herramientas de vuelo.** Modos SAS (pro/retro/normal/radial/objetivo/nodo), nodo de
   maniobra con los tres ejes, tiempo de quemado y "warp al nodo", ejecutores de LOI/TEI,
   ayudas de aterrizaje (altitud radar, tiempo al impacto, inicio de quemado).
7. **Práctica y dificultad.** Volver al inicio de la fase, quickload visible, asistencias
   opcionales (propelente infinito, sin fallas estructurales) puntuadas aparte.
8. **Entrenamiento.** Misiones cortas aisladas (circularizar, interceptar en LEO, acoplar
   desde 200 m, alunizar desde 15 km).

Visual en paralelo: ✅ mallas procedurales detalladas de Mercury-Redstone, Atlas, Titan II/Gemini,
Agena y Saturn V/Apollo/LM (`HistoricalVehicleMeshes`). Pendiente: contrastar libreas contra
fotos (bandas del Titan, franjas de la Agena, roll pattern del Redstone).

## Prioridad Inmediata

La prioridad inmediata es cerrar la paridad física antes de activar el integrador acoplado en
vuelos reales. El switch `Universe.Coupled6DofIntegrationEnabled` sigue desactivado por defecto.

1. **Paridad legacy vs. coupled 6-DoF**
   - Coast determinista: cerrado con 50 pasos de 20 ms.
   - Ascenso propulsado simplificado: cerrado con 25 pasos de 20 ms, consumo de propelente y
     gravedad.
   - Fixture Flight 7 de datos reales: cerrado para 100 pasos de 20 ms en ascenso abierto,
     con masas, 33+6 motores, spool y consumo de propelente.
   - Gate controlado inicial: cerrado para 50 pasos de 20 ms con pitch fijo, atmósfera Earth,
     rotación superficial y gimbal observable.
   - Programa cerrado inicial: cerrado para 250 pasos de 20 ms con rampa de elevación y
     feedback de actitud/velocidad angular.
   - Engine-out asimétrico inicial: cerrado con fallo de un Raptor booster fuera de eje,
     torque geométrico y respuesta angular legacy/6-DoF equivalentes.
   - Recuperación engine-out determinista: cerrado con detección del estado activo, 100 ms de
     latencia onboard, comando de actitud con feedback, rate limit y reducción de velocidad
     angular conservando la paridad legacy/6-DoF.
   - Siguiente gate: extenderlo a ascenso controlado y de mayor duración con diagnóstico de
     fallos, navegación, SAS, flaps, RCS y autoridad de recuperación equivalentes.
   - Gate posterior: EDL controlado con contactos y telemetría de captura.
   - No activar el switch ni declarar fidelidad completa hasta que los escenarios controlados
     pasen y se conserve evidencia numérica y de framebuffer.

2. **Controles aún no equivalentes en el camino acoplado**
   - Flaps ya tienen contrato de estado, rate limit, saturación y torque compartido; falta que el
     renderer consuma ese estado y cerrar la campaña V3 de mayor duración.
   - SAS/rate limiting y RCS todavía requieren contratos explícitos con el evaluador de torques.
   - Cada diferencia debe tener una prueba de paridad, no un ajuste visual de tolerancia.

3. **Validación visual posterior**
   - Solo después de la paridad numérica: ascenso, hot-stage, órbita, reentrada y touchdown con
     `tools/visual_playtest.sh` y framebuffer real.

### Backlog histórico y frentes paralelos

Los bloques de optimización y fidelidad visual que siguen son planes de trabajo históricos o
paralelos. Sus informes conservan resultados fechados; al cerrar un gate deben actualizarse los
documentos activos anteriores, no solo añadir otra nota de fase.

Frente paralelo de física (no pisa visual; plan en
`docs/audits/REALISM_UNRESOLVED_FRONTS_AUG2026.md`): Tierra WGS84 + J2 armado
en `earth.json`; Kepler on-rails sigue dos-cuerpos. No activar física diferida.

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
   - **Fase 55:** caches de invalidación evitan reescrituras de navegación, phase track y
     vista/densidad cuando no cambian; F3 invalida la densidad de inmediato. Suite `696/696`,
     build, startup y contratos pasan. El A/B GPU continúa bloqueado por Xvfb. Ver
     `docs/audits/PERF_HUD_INVALIDATION_CACHE_PHASE55_REPORT.md`.

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
   - El gate ahora exige también el volumen procedural `HotStagePlume` visible y anclado
     durante el overlap; validado en framebuffer real a 640x360 como evidencia reproducible.

   - Flight 14 early-flight reference pass (2026-09-30): delivered-thrust deluge,
     fixed-site shaded water volumes, a slender rose booster column, neutral PBR
     steel and low-angle camera framing. Real 1920x1080 launch-to-1 km gate passed;
     897 simulation tests passed. Scope and residual sky/Pad 2/onboard gaps:
     `docs/audits/starship_flight14_visual_reference_2026-09-30.md`.

   - Flight 14 reconstruction: reference extraction and source-backed feasibility
     are available in `docs/research/STARSHIP_FLIGHT14_VIDEO_FEASIBILITY_2026-09-30.md`.
     A comparison-only mission reference now separates the planned long timeline
     from observed early-return windows. Single-sea-level-engine insertion and
     deorbit components are tested with continuous propagated state and resources;
     display-comparison tooling preserves frame and clock uncertainty. Evidence:
     `docs/audits/flight14_orbital_components_2026-10-01.md`. A continuous isolated-
     Earth pad-to-insertion diagnostic now runs its own estimated ascent/hot-stage/
     coast policy through the production physics controller; source and limits:
     `docs/audits/flight14_continuous_launch_2026-10-01.md`. The diagnostic now carries
     26 estimated-mass Starlink V3 parts from the pad and releases them as independent
     propagated vessels with conservation witnesses; see
      `docs/audits/flight14_payload_deployment_2026-10-01.md`. The same loaded-state
      diagnostic now continues through a single-engine deorbit and descending
      atmospheric entry to 90 km, retaining tank contents and deployed vessels;
      return estimates and remaining reference discrepancies are documented in
      `docs/audits/flight14_continuous_return_2026-10-02.md`. A conditional geodetic
      HUD calibration now refines the shallow deorbit target and independently
      bounds radial commands; see `docs/audits/flight14_entry_timing_correction_2026-10-02.md`.
      Read-only ground-track telemetry now records rotating-body geodetic coordinates
      and their absolute epochs; see `docs/audits/flight14_ground_track_2026-10-02.md`.
      A composed aerodynamic-descent diagnostic now continues the original reserve
      to a first 3 km crossing through physical bank/flap control. Actual attitude
      tracking error, deep-entry load/range response and terminal broadcast coverage
      remain open; see `docs/audits/flight14_aerodynamic_descent_2026-10-02.md`.
      Estimated model-based flap trim now counters the existing static moment;
      a composed three-sea-level-engine flip/terminal-burn diagnostic continues to
      a first 100 m crossing, consuming the original reserve with finite engine
      spool/restart and gimbal response. Water contact and source timing/range
      agreement remain open; see `docs/audits/flight14_trim_terminal_2026-10-02.md`.
      A Flight 14-specific shallow-entry/55° control estimate improves the reviewed
      descent comparisons; residual timing, speed and altitude errors remain explicit
      in `docs/audits/flight14_shallow_corridor_2026-10-02.md`.
      Actual mass/release
     timing, both water returns and exact reconstruction acceptance remain open.
     The featured action now runs a labelled engineering exploration with exclusive
     guidance, moving-body ephemerides, whole 20 ms physics steps and an explicit
     100 m stop. Its estimated V3 hardware is disclosed; existing tower-catch policies
     do not own this preview. See `docs/audits/flight14_gameplay_exploration_2026-10-02.md`.

2. **Reentry visual**
   - Plasma/shock layer mas fisico, ligado a heat flux y densidad atmosferica.
     Flight 14 now has a hull-conforming sheath, live flap proxies and bounded x200
     exploration; final reference/CFD fidelity remains open. See
     `docs/audits/flight14_plasma_warp_2026-10-02.md`.
   - Primera pasada de glow localizado en nose, belly y flap leading edges ya implementada.
   - Alpha/timing por fase EDL ✅ (`ReentryPlasmaVisualIntensity`: ENTRY soft → PEAK → AERO fade).
   - Harness de comparacion ya existe (`tools/visual_playtest.sh --reentry-compare`, captura
     belly-flop nominal vs EDL con mala actitud forzada via `SimulationBridge.BeginReentryDemonstration(bellyFirst:...)`;
     verificado con xvfb → `exo_play_reentry_nominal.png` / `exo_play_reentry_bad_attitude.png`).
     Pendiente: el ajuste/comparacion visual en si (alpha, timing, zone charring) contra IFT.

3. **Entorno y camaras**
   - Pad costero ya tiene primera pasada visual.
   - Torre Starbase V1.1 agrega rieles de carriage, sheaves, rub rails/rollers
     de chopsticks, cables y cues del Ship QD upper arm.
   - Primera pasada de iluminacion solar, exposicion, sky/atmosfera y camaras ya esta
     implementada para que launch/orbit/reentry/cockpit se lean como escalas reales.
     Pendiente: comparacion lado-a-lado contra referencias y ajuste en hardware GPU.
   - Relieve global Earth con ETOPO 2022 ya generado y enlazado al shader: procedencia
     CC0, conversión reproducible, fail-closed y matriz Starbase 1920×1080 a 2–40 km
     pasan. Pendiente: captura land-facing y comparación 20–500 km; sigue siendo una
     capa visual, nunca autoridad de física o colisión.

4. **Capturas de aceptacion**
   - Automatizar capturas con framebuffer real para pad, liftoff, Max-Q, staging,
     orbit/map, belly-flop reentry, flip-and-burn, touchdown/crash y cockpit.
   - `--smoke`, `--cockpit`, `--hotstage`, `--reentry-compare` y `--edl` ya tienen
     gates de framebuffer; `--edl` alcanza el catch físico de dos pines a cadencia x1.
     La matriz completa aun requiere capturas reproducibles de liftoff, Max-Q, staging,
     orbita y crash/abort.

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
- Retorno orbital normal a Starbase 🟡 — el seed ya considera la rotación del sitio. El guiado de captura predice la huella con `EntryCorridorPropagation` (energía, ángulo de trayectoria, sustentación y resistencia del mismo `AerodynamicsModel`, banco de referencia lift-up), no con la caída balística. El handoff lift-up pasa el gate físico 120 km→30 km sin recalibrar `Cd`/`CL`. La cadencia de guiado ya es independiente del render. Sigue abierta la captura física completa en framebuffer y el 6-DoF acoplado. Ver `docs/audits/ORBITAL_REENTRY_PHYSICS_AUDIT_2026-09-25.md`.

### Interplanetario

- Tests de cruise muy largo ✅ (crucero interplanetario y transición SOI a warp).
- Fundación de transferencia lunar geocéntrica ✅ (`LambertSolver` +
  `LunarTransferPlanner`): TLI, encuentro, B-plane, perilunio lunar y prueba
  end-to-end sin teleport. El mapa ya selecciona esta ruta para `moon`, centra
  la quema TLI finita sobre su época impulsiva, muestra la trayectoria Tierra–Luna, SOI, perilunio y
  estimación LOI, y rechaza ventanas de plano que exigirían TLI >4,5 km/s.
  Apollo 8/11 vuelan TLI finita (J-2), MCC con guiado Lambert, LOI y circularización
  finitas (SPS) sin asignar estado; corregido además el marco lunar, que omitía la
  aceleración de la Tierra hacia el Sol (73 km de deriva por órbita lunar a x1).
  Lanzamientos históricos: los hold-downs vuelven a soltarse (desde 217a01d ninguna misión
  de campaña despegaba con [L]); S-II/S-IVB con guiado de inserción en lazo cerrado hasta
  una órbita real (S-IC con giro gravitatorio, S-II/S-IVB con tiempo-a-ir y amortiguamiento
  según autoridad TVC). Verificado en Godot con `--apollo8-mission`: inserción T+717 s,
  TLI 307 s, MCC 27 s, LOI 107×310 km y circularización 107×110 km, en 55 min de reloj.
  Warp en LEO baja (<225 km) ya no queda en x3. Ventana de lanzamiento lunar: [L] espera en
  la plataforma hasta que el azimut que pone la Luna (a la llegada) en el plano orbital entra
  en 72°–108°, y la inserción vuela ese plano; la TLI se planea desde la órbita volada, sin
  rotación por script (salvo al reanudar un ascenso guardado). Regreso físico: TEI finita con
  el SPS (planificador transtierra por asíntota, costa de regreso hasta 1,5× la histórica para
  cerrar con el SPS que deja la misión volada), tres correcciones al perigeo del corredor de
  38 km y entrada sustentadora del CM (aerodinámica de cápsula con trim de 16,7°, L/D≈0,3,
  control por alabeo): pico de 7,1 g (Apollo 8: 6,8 g) en vez de 16 g balístico. Pendiente:
  ventana multi-día y dataset lunar fechado.
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
  la campaña se cierra en `LANDED`, con el alunizaje. Ver `docs/HITO4_APOLLO11_TDE.md`.
  Pendiente: ascenso LM, rendezvous, TEI, entrada y amerizaje.
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
5. Recien despues: Apollo 11 retorno (APS→rendezvous→TEI).
   (R11 ✅; R12 ✅; Apollo 11 TD&E + LOI ✅; DOI + alunizaje ✅)

### Flight 14 ship water-entry increment (2026-10-02)

- Gameplay continues the original loaded ship beyond the retained 100 m witness,
  tapers lateral braking tilt near sea level, and commands shutdown on nozzle contact.
- Opt-in calm-water cylinder buoyancy and dissipative drag act in the translating,
  rotating Earth frame. The declared open-water envelope is 25..35 N, 165..140 W,
  an engineering region, not a source-backed target or global water classifier.
- Inclined sealed-cylinder displacement and projected wet-section drag cover horizontal/inverted load geometry. Water foam follows the projected wet hull segment; spray follows its displaced centre and relative motion.
- Water-only coupled mass-centre integration now advances translation and attitude together after accepted contact. Free-motion conservation and production 20/10/5 ms convergence checks support a ten-second observation of estimated sealed-hull heeling. The global ascent/EDL mode remains unchanged. Independent booster return is now playable as an engineering estimate; its resource/trajectory calibration, exact targeting, flooding, slamming, physical waves and measured capsize timing remain open. See `docs/audits/flight14_water_motion_2026-10-03.md`.
- Near-sea ripples, spray and exhaust illumination follow actual delivered thrust/contact;
  the launch-site procedural fallback is now geographically anchored. Visual waves
  do not change the physical calm-water plane. No GPU performance certification.
