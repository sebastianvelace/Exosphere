# Fase 4 — auditoría del scheduler de simulación

Fecha: 2026-08-11
Alcance: `ExosphereSimulation/Universe.cs`, `Vessel.cs`, `Parts/PartGraph.cs`, `Physics/*`, integradores y el punto de entrada de `SimulationBridge`.
Restricción: **sólo documentación**. No se modificó código de runtime, tests, escenas, configuración ni datos.

## Veredicto ejecutivo

El simulador tiene tres rutas reales:

1. RK4 completo para física fuera de rails.
2. Propagación analítica Kepleriana para naves en rails.
3. Anclaje de superficie/ground hold para estados que no necesitan resolver movimiento libre.

También existe una clasificación `Active/Nearby/OnRails/Hibernated`, pero es una clasificación sin efectos temporales: no decide qué nave recibe un tick, no avanza el reloj de una nave dormida y no contiene un estado de wake-up. `Universe.cs:26-46` y `648-699` lo declaran explícitamente.

El hallazgo prioritario antes de introducir sleep/wake es una asimetría de caps en la rama mixta:

- `Universe.Tick` decide el cap global mirando `ActiveVessel` (`Universe.cs:535-555`).
- `anyForceSensitive` puede ser verdadero por una nave no activa (`517-520`).
- Esa nave puede entrar a `IntegrateVesselOffRails` con `MaxCoastStep = 2 s` (`1111-1119`, `144`), aunque por atmósfera, contacto o térmica debería recibir un paso de física/contacto menor.

No lo corrijo aquí porque el encargo prohíbe modificar código. Debe ser el primer test de regresión y el primer gate de cualquier scheduler nuevo.

La propuesta segura es incremental: mantener siempre activa la nave controlada; usar rails sólo cuando la nave esté libre de fuerzas y eventos; usar proximidad como filtro conservador, no como permiso de apagar física por distancia; y no implementar hibernación por `continue` hasta disponer de estado temporal explícito, eventos pendientes y pruebas de equivalencia.

## Evidencia de verificación

Comandos read-only ejecutados:

```text
dotnet build ExosphereSimulation/ExosphereSimulation.csproj --no-restore --nologo -v quiet
  Build succeeded — 0 Warning(s), 0 Error(s)

dotnet build Exosphere.csproj --no-restore --nologo -v quiet
  Build succeeded — 0 Warning(s), 0 Error(s)

dotnet build ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-restore --nologo -v quiet
  Build succeeded — 0 Warning(s), 0 Error(s)

dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-build --nologo
  Failed: 0, Passed: 549, Skipped: 0, Total: 549, Duration: 1 m 15 s
```

La primera tentativa de compilar los tres proyectos en paralelo chocó con el PDB compartido; al repetir en serie pasó con 0/0. No es un fallo de código y no quedó ningún proceso `dotnet` activo.

Los tests existentes cubren finitud, progreso, política de warp, continuidad de SOI, impactos, térmica y clasificación. No exponen contadores de trabajo por nave, caps efectivos ni un presupuesto p95 del simulador separado del render.

## Mapa de llamadas real

### Entrada desde Godot

```text
SimulationBridge._Process(delta)                         scripts/SimulationBridge.cs:246
  ├─ GetDominantBody(active.Position)                    Universe.cs:254 / 485-503
  ├─ RequiresOffRailsPhysics(active)                     Universe.cs:255 / 570-600
  ├─ RequiresBoundedWarpPropagation(active)              Universe.cs:256 / 607-630
  ├─ recalcula MaxAllowedWarpIndex y puede bajar warp
  └─ Universe.Tick(delta)                                SimulationBridge.cs:294
       ├─ simDelta = delta * TimeScale                    Universe.cs:512-515
       ├─ prepass global: Any(RequiresBoundedWarpPropagation)
       │    + Any(landing gear cerca de superficie)       Universe.cs:517-520
       ├─ TimeScale <= 4       → TickPhysics(step)
       ├─ 4 < TimeScale <= 1000
       │       o cualquier nave requiere límites → TickPhysicsMixed(step)
       └─ TimeScale > 1000 y nadie requiere límites → TickRails(simDelta)
```

`SimulationBridge` consulta body/warp safety antes del tick y `Universe.Tick` vuelve a consultar datos equivalentes. Es correcto como defensa, pero duplica escaneos y debe medirse antes de consolidarlo.

### Ruta RK4 completa: `TickPhysics`

```text
TickPhysics(dt)                                           Universe.cs:751-815
  ├─ PropagateAllBodies(CurrentTime + dt)                 KeplerPropagator.cs:54+
  ├─ snapshot _vessels.ToList()                           Universe.cs:758
  └─ por nave, salvo secondary docked:
       ├─ destroyed → AdvanceAnchoredWreck                 761-765
       ├─ surface settled → AdvanceSurfaceAnchor + Tick    767-780
       ├─ ground held → frame de cuerpo + Vessel.Tick      783-795
       ├─ IsOnRails y no requiere fuerzas → rails          797-809
       └─ resto → IntegrateVesselOffRails                   811-813
  └─ ApplyDockingConstraints                                814
```

`IntegrateVesselOffRails` (`Universe.cs:1125-1187`) obtiene el estado inicial del cuerpo, evalúa landing/catch, llama una vez a `Vessel.Tick`, ejecuta `RK4Integrator.StepPosVel` y luego aplica contactos, estrés, ruptura estructural, térmica y superficie.

El integrador RK4 hace cuatro evaluaciones de derivada (`RK4Integrator.cs:69-89`): gravedad de todos los cuerpos, empuje, drag/lift y contactos. No es correcto reducir las cuatro llamadas quitando `Vessel.Tick`, ni llamar `Vessel.Tick` cuatro veces: `Vessel.Tick` avanza spool, consumo, hot-stage, crew y actuadores una sola vez por tick.

### Ruta mixta: `TickPhysicsMixed`

```text
TickPhysicsMixed(dt)                                     Universe.cs:1024-1123
  ├─ PropagateAllBodies(CurrentTime + dt)                 1026-1027
  └─ por nave, salvo secondary docked:
       ├─ ClassifyMixedPhysicsWorkload                     1031-1037
       │    ├─ Destroyed → anchored wreck
       │    ├─ SurfaceSettled → anchor + Vessel.Tick
       │    ├─ GroundHeld → frame + Vessel.Tick
       │    ├─ OnRails → PropagateVesselOnRails
       │    └─ FullPhysics → IntegrateVesselOffRails
       └─ ApplyDockingConstraints                          1122
```

La nave activa puede entrar en rails desde `TimeScale >= 10` si no requiere fuerzas (`1082-1109`). La no activa puede integrar off-rails si `requiresForces`; de lo contrario usa rails aunque `IsOnRails` no siempre se actualice (`1042-1053`, `1111-1119`). Por ello `IsOnRails` no es un contador completo del trabajo realizado.

### Ruta de rails pura

`TickRails(simDelta)` (`1344-1373`) propaga cuerpos, salta secondary docked, ancla wrecks/surface-settled y propaga el resto por conica.

`PropagateVesselOnRails` (`1377-1517`):

- crea o reutiliza `OrbitalState`;
- reevalúa el cuerpo dominante al epoch actual;
- comprueba conica radial;
- divide el intervalo en slices `<= MaxCoastStep` (2 s);
- detecta superficie bajo cada slice;
- usa `BodyStateAt` en el instante del slice;
- detecta cambios de SOI y reencuadra la conica en el mismo instante;
- escribe posición y velocidad inerciales al final.

La seguridad es buena, pero el coste es proporcional a `vessels × ceil(dt/2 s) × cuerpos`; a warp alto el número de slices puede dominar aunque cada nave sea “barata”.

## Estado actual del scheduler

| Mecanismo | Evidencia | Alcance real |
|---|---|---|
| RK4 capado | `Universe.cs:111-113`, `522-533` | Ruta completa para naves no destruidas/no ancladas; hay excepciones settled, ground-held, docked y rails válidos. No es proximity scheduling. |
| Cap de costa | `MaxCoastStep = 2 s`, `144`, `545-555` | Cap de la rama mixta y slices de rails. |
| Cap de burn | `MaxThrustStep = 0.1 s`, `148`, `541-548` | Se decide por throttle de la nave activa. |
| Cap de contacto | `MaxContactStep = 0.005 s`, `113`, `518-520`, `545` | Global si alguna nave está cerca del suelo. |
| Rails | `TickRails`, `PropagateVesselOnRails` | Válidos sólo con guardas de fuerzas/SOI/superficie. |
| Wake activo | `RequiresOffRailsPhysics`, `570-600` | throttle, spool, ground, atmósfera, q y heat flux. |
| Wake de entrada | `RequiresBoundedWarpPropagation`, `607-630` | periapsis atmosférica y estados degenerados. |
| Surface sleep | `IsSurfaceSettled`, `1260-1342` | Estado físico explícito; throttle lo despierta. |
| Docking | `441-474`, `ApplyDockingConstraints` | La secondary no integra independientemente. |
| Tier API | `ClassifySimulationTier`, `648-699` | Clasifica; no omite ticks ni guarda tiempo diferido. |
| Cache PartGraph | `PartGraph.cs:54-85`, `206-268`, `330-368` | Reduce reconstrucciones dentro de `Vessel.Tick`. |

No existen cola de deadlines, broadphase usado por el dispatcher, `lastSimulatedTime` por vessel, snapshot de motores/térmica/crew/eventos, wake por intersección de trayectoria, contadores internos de slices o métricas de allocation.

La conclusión es importante: activar `if (tier == Hibernated) continue` sería una regresión física, no una optimización segura.

## Auditoría de `Vessel.Tick` y física dependiente

`Vessel.Tick` (`Vessel.cs:592-821`) realiza, en orden:

1. abre caches temporales de `PartGraph`;
2. avanza el spool de engines una vez;
3. invalida el snapshot si hay una transición de fallo;
4. consume propelante;
5. avanza hot-stage y crew EVA;
6. aplica autoridad de control, solve de gimbal, torque por mount y SAS;
7. aplica torque aero y flaps;
8. integra orientación y velocidad angular;
9. cierra caches en `finally`.

`ComputeNetAccelerationAt` (`Vessel.cs:551-568`) suma N cuerpos, thrust y aero. `PartGraph` ya cachea stage, engines activos, posiciones, masa, CoM e inercias por tick, pero sigue recorriendo engines/mounts para envolvente TVC, thrust, torque, solve diferencial y consumo (`PartGraph.cs:430-660`, `819-838`). Ese cache no puede compartirse entre vessels ni asumir que un salto de tiempo grande sea equivalente.

`ApplyPostIntegrationPhysics` (`Universe.cs:879-906`) añade:

- `StressSolver.ComputeLoads`, cuyo `CollectSubtreeMass` recorre subárboles para cada joint (`StressSolver.cs:10-47`), potencialmente O(joints × parts);
- búsqueda de joints rotos;
- térmica por part cuando hay densidad;
- `ThermalModel.StepTwoNode`, subdividido a 0.02 s (`ThermalModel.cs:77-109`, `155-173`).

La documentación térmica deja claro que `MaxCoastStep = 2 s` puede implicar 100 substeps térmicos por llamada. Aumentar un cap no sólo cambia RK4: también cambia coste y fidelidad térmica.

`SurfaceContactSolver.Evaluate` (`SurfaceContactSolver.cs:132-215`) calcula por punto penetración, velocidad `v + omega × r`, fricción, torque, sobrecarga y travel. En off-rails aparece pre-RK4, en las cuatro derivadas y después (`Universe.cs:1145-1186`). El catch tiene una puerta barata de 500 m (`1193-1213`), pero landing/catch siguen siendo eventos que no deben apagarse por distancia al jugador.

## Hallazgos priorizados

### P0 — cap incorrecto para una nave no activa sensible a fuerzas

`anyForceSensitive` mira todas las naves, pero `cap` usa sólo `ActiveVessel` (`Universe.cs:517-555`). Si la activa está en vacuum coast y una secundaria entra en atmósfera, la secundaria puede recibir `dt=2 s`. Riesgos: drag/contacto/thermal RK4 más grueso, penetración, temperatura o destrucción desplazada.

**Acción obligatoria antes de optimizar:** test que construya active vacuum + secondary atmosférica/contact-sensitive y observe el `dt` efectivo por vessel.

### P1 — tier hibernated no controla dispatch

`ClassifySimulationTier` es sólo etiqueta. `TickPhysics`, `TickPhysicsMixed` y `TickRails` siguen recorriendo `_vessels`. Un `continue` rompería recursos, epoch orbital, thermal, crew, staging y wake-up.

### P1 — scans repetidos

`SimulationBridge` y `Universe` consultan safety; `RequiresBoundedWarpPropagation` llama a `RequiresOffRailsPhysics`; ésta consulta engines, cuerpo dominante, atmósfera, q y heat flux. El coste aproximado es O(vessels × bodies) antes de integrar y vuelve por substep. Un snapshot de política es candidato seguro sólo después de medir y mantener una fuente authoritative.

### P1 — rails por slices fijos

`MaxCoastStep=2 s` evita túneles, pero en warp alto puede crear miles de slices por nave y re-evaluaciones de cuerpo/SOI. Candidato: próximos eventos con bracket/refinamiento, conservando cap como fallback.

### P1 — `TickRails` entrega `dt` grande a settled

`TickRails` llama `vessel.Tick(dt, body)` para `IsSurfaceSettled` (`1355-1367`), con `dt=simDelta`. Aunque la posición está anclada, `Vessel.Tick` avanza spool, crew y hot-stage. Debe existir un test antes de entregar dt grande a estados de superficie.

### P2 — `IsOnRails` no describe siempre el workload

La ruta no activa puede usar propagación analítica sin cambiar el flag. Un wake-up basado sólo en esa propiedad puede no invalidar correctamente `OrbitalState`.

### P2 — estrés y allocations

`CollectSubtreeMass` puede ser O(joints × parts); `RK4Integrator.StepPosVel` crea arrays y closures por llamada. Ambos son candidatos de optimización medibles, no autorizan apagar física.

### P2 — docking

`ApplyDockingConstraints` busca vessels por ID en cada conexión y substep. Un índice por tick puede ser seguro, pero una secondary docked no debe recibir integración independiente.

## Candidatos seguros de scheduler

### Proximidad: filtro conservador de wake-up

Una nave sólo podría dormir si no es activa, no tiene throttle/spool/engine, no tiene contacto/landing/catch/docking/ground state, no está en atmósfera o termosfera relevante, no tiene periapsis próxima a superficie/atmósfera, no tiene staging/hot-stage/crew/timer/maniobra pendiente, no entra en un corredor de wake de nave/infraestructura y su estado es finito con conica válida.

La distancia debe evaluarse en el marco del SOI y con histéresis (`wakeRadius > sleepRadius`) y margen `speed × lookahead + errorBound`. Las constantes actuales de 250 km y 5 Mm (`Universe.cs:120-126`) son umbrales de clasificación, no una prueba de seguridad.

### Rails por próximos eventos

```text
estado actual + OrbitalState
  → superficie/atmósfera/SOI/q/heat/wake/timer deadlines
  → min(deadline, safetyCap)
  → materializar en epoch exacto
  → despertar, reencuadrar SOI o resolver evento
```

El crossing necesita bracket y refinamiento; si no se puede demostrar el intervalo se conserva `MaxCoastStep`. No se pueden quitar las guardas radial, superficie y SOI de `PropagateVesselOnRails`.

### Warp con estado temporal explícito

Un futuro `DeferredVesselState` debe contener `LastSimulatedTime`, posición/velocidad o conica en ese epoch, `ReferenceBodyId`, recursos, spool/engine state, térmica, crew, contactos, docking, catch, destrucción y próximo evento. Al despertar debe materializar desde ese epoch y procesar eventos ordenados; no basta con poner `LastSimulatedTime = CurrentTime` ni llamar `Vessel.Tick(largeDt)`.

### Sin multithreading inmediato

`Vessel.Tick` muta parts, recursos, joints y estados de engine. `Universe` puede crear debris y luego aplica docking/catch. Paralelizar sin snapshots inmutables y command buffers puede cambiar consumo, ruptura, docking y orden de eventos. La primera optimización debe ser determinista y de un hilo.

## Métricas que faltan

Se necesita baseline CPU de `Universe.Tick`, separado del Godot frame:

| Métrica | Desglose |
|---|---|
| Tiempo CPU | total y p50/p95/p99 de `Universe.Tick` |
| Tiempo sim | `realDelta`, `simDelta`, `CurrentTime` inicial/final |
| Branch | full/mixed/rails y substeps por branch |
| Workload | active/full/offrails/rails/settled/ground/destroyed/docked |
| Rails | slices, SOI checks, `BodyStateAt`, impacts, radial |
| RK4 | vessels × steps × 4 stages y tiempo de derivada |
| Contacto | evaluaciones pre/stage/post, puntos y contactos |
| Atmosfera | density/pressure/Mach/heat/thermal substeps |
| Estructura | parts, joints, subtree mass, breaks, debris |
| Propulsión | engines/instances, gimbal solves, consumo, fallos |
| Docking | conexiones, búsquedas y constraints |
| Memoria | allocations por tick y GC collections, además de RSS |
| Exactitud | error pos/vel, energía/momento, masa, thermal, eventos y SOI |

No usar un único epsilon: contacto de milímetros, órbita LEO y conica heliocéntrica necesitan presupuestos distintos.

## Tests e invariantes propuestos

Estos tests quedan propuestos; no se añadieron por la restricción de esta fase.

### Scheduler y caps

1. **Cap por vessel:** active vacuum + secondary atmosférica a warp alto; ningún off-rails `dt` excede `MaxPhysicsStep` y contacto no excede `MaxContactStep`.
2. **Burn:** throttle bajo warp; cada paso `<= MaxThrustStep`, spool una vez y consumo igual a referencia real-time.
3. **Settled wake:** `IsSurfaceSettled` a warp alto no salta timers; throttle despierta inmediatamente.
4. **Tier no mutante:** consultar tier no cambia posición, velocidad, rails, conica, recursos ni flags.

### Rails y wake

5. **Equivalencia vacuum:** rails/event-driven contra referencia RK4 al mismo epoch.
6. **SOI continuo:** Earth→Moon y Earth→Sun sin salto de posición/velocidad y con epoch correcto.
7. **Entrada sin túnel:** periapsis atmosférica y radial despiertan antes de impacto y ejecutan térmica/contacto.
8. **Wake por trayectoria:** nave lejana que entra al corredor de wake se actualiza antes del encuentro.
9. **Histéresis:** cruzar el borde no produce chatter ni reconstituciones repetidas.
10. **Fallback inválido:** NaN/inf, conica degenerada o body missing fuerzan ruta conservadora.

### Eventos y conservación

11. **Staging/hot-stage:** separación no omitida; debris, masa e impulso se conservan.
12. **Térmica/estructura:** mismo flujo dividido produce misma parte rota, causa y debris.
13. **Docking:** secondary no integra aparte; constraint una vez; undock reactiva ambos estados.
14. **Catch/landing:** catch válido permanece activo, miss no hace settle, wreck sigue anclado.
15. **Save/load:** conserva epoch diferido, conica, próximo evento, recursos y engine state.
16. **Orden:** invertir inserción produce mismo estado/eventos, salvo IDs deliberadamente distintos.

### Invariantes por tick

- `CurrentTime` aumenta exactamente `realDeltaTime × TimeScale` si ambos son válidos.
- Posiciones, velocidades, orientaciones, masas, temperaturas y recursos son finitos.
- La nave activa nunca se hiberna; throttle, spool, contacto, catch, docking, staging y eventos invalidan deferred.
- Ningún off-rails `dt` supera el cap de su propio vessel.
- La masa sólo cambia por consumo/etapa/ruptura declarados.
- Handoff rails↔RK4 conserva posición/velocidad inerciales en el mismo epoch.
- Impacto, atmósfera y SOI no se saltan por distancia entre frames.
- Secondary docked no recibe integración independiente.
- El resultado no depende del orden de vessels ni de workers.
- Una clasificación no muta estado.

## Orden futuro recomendado

### Fase 4a — instrumentación

Añadir contadores CPU y escenarios multi-vessel sin cambiar física. Reproducir el P0 y medir substeps, tiempo y allocations.

### Fase 4b — caps y snapshots

Corregir el cap por vessel no activo, consolidar snapshot de body/policy y medir equivalencia real-time/warp, térmica y contacto.

### Fase 4c — rails por eventos

Aplicar próximos deadlines sólo a estados force-free; conservar `MaxCoastStep` como fallback y todos los guardas.

### Fase 4d — deferred state/proximidad

Introducir `LastSimulatedTime`, eventos ordenados, histéresis y recuperación conservadora sólo después de la equivalencia.

### Fase 4e — paralelización opcional

Sólo con snapshots inmutables, command buffers y commit determinista para consumo, ruptura, docking, catch y debris.

## Decisión

No se recomienda activar todavía ningún “hibernated tick skip”, scheduler por distancia sin eventos ni paralelización de `Vessel.Tick`. La base actual es defendible cuando usa RK4/rails con sus guardas, pero necesita métricas y la corrección del cap per-vessel antes de añadir otra capa.

El siguiente gate será: **P0 reproducido y cubierto por test, métricas CPU p50/p95/p99 de `Universe.Tick`, equivalencia de trayectoria y matriz de eventos verde**. Hasta entonces, los cambios deben limitarse a instrumentación o documentación.

## Validación Flight 7 con el benchmark de fase 4

La corrida framebuffer-backed se ejecutó con el perfil atmosférico interactivo v21 y
se reprocesó mediante `--replay-dir` después de corregir un defecto del generador
de reportes que omitía los nombres de 14 estadísticas. Resultado validado:

```text
status=PASS
summary_reason=ASCENT_ORBIT_OK
summary_frames=1993
trace_count=48
transition_sequence=Ignition>Ascent>Coast>Insert>Done
trace_time_monotonic=true
trace_progress_detected=true
trace_stall_detected=false
trace_max_gap_sec=10.300
trace_time_span_sec=479.100
trace_last_alt_m=150180.800
trace_last_apo_m=150394.700
capture_count=6
wall_seconds=873.990000
rss_max_kib=1314968
```

La órbita alcanzada fue 163×149 km con `e=0.001` según el log del harness. Esto
valida la ruta actual y el benchmark, pero no resuelve el P0 del cap por vessel:
no se debe inferir de una única Starship que una flota multi-vessel con atmósfera,
contacto y thermal haya conservado el mismo error. El siguiente cambio de código
debe reproducir primero ese P0 con dos naves y comparar trayectoria, temperatura,
contactos, rupturas y consumo antes/después.
