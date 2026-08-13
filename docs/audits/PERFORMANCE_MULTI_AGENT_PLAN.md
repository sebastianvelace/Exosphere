# Plan riguroso de optimización y despliegue multiagente

Estado: fases 7–9 aplicadas; siguiente fase: buffers por tick para geometría/gimbal sólo con perfilado
Fecha de baseline: 2026-08-11; actualizaciones runtime/scheduler: 2026-08-12
Alcance: vuelo sandbox, Starship por defecto, Godot 4.6.3 mono, .NET 8

La evidencia de la fase runtime y sus límites está en
[`PERF_RUNTIME_PHASE7_REPORT.md`](PERF_RUNTIME_PHASE7_REPORT.md). El smoke visual continúa
siendo PASS, pero llvmpipe no es suficiente para declarar 60 FPS de hardware: la fase siguiente
debe conservar la separación entre medición CPU, callback de proceso y GPU real.

La evidencia del scheduler está en
[`PERF_SIMULATION_PHASE8_REPORT.md`](PERF_SIMULATION_PHASE8_REPORT.md). La fase 8 elimina
asignaciones de snapshots de flota y limita el HUD de motores a 10 Hz; no cambia la física de
la nave activa ni promueve un LOD adicional.

La evidencia del hot path de Starship está en
[`PERF_STARSHIP_PHASE9_REPORT.md`](PERF_STARSHIP_PHASE9_REPORT.md). La fase 9 reduce las
asignaciones administradas medidas del Flight 7 de 5.32 a 4.50 KiB por tick y añade un
presupuesto xUnit; todavía no elimina los generadores de geometría de gimbal porque requieren
medición separada de paridad de torque.

## 1. Diagnóstico reproducible

El bloqueo observado después de entrar al nivel era un bloqueo del hilo principal, no una
falla primaria de la física de Starship.

Comando de reproducción:

```bash
GODOT_BIN=/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64
timeout 30s "$GODOT_BIN" --headless --path . \
  --scene res://scenes/flight/Flight.tscn --quit-after 300 \
  --rendering-driver opengl3
```

Baseline previo al arreglo:

| Fase | Tiempo medido |
|---|---:|
| Cargar universo | 47.7 ms |
| Crear Starship | 650.2 ms |
| Inicializar campaña | 722.6 ms |
| Crear planetas | 3231.3 ms |
| `SimulationLoaded` | 3233.5 ms |
| LUT de densidad Earth | 4.9 ms |
| LUT de transmitancia Earth síncrona | 12625.2 ms |
| Resultado | no alcanzó 300 iteraciones en 30 s |
| Memoria residente máxima | 835064 KiB |

La prueba aisló el punto crítico: `AtmosphereTransmittanceLut.Build` se ejecutaba desde
`SkyController.BindAtmosphere` en el hilo principal. El LUT de scattering múltiple y el atlas
angular estaban detrás de esa operación y podían prolongar aún más la pausa.

La primera corrección aplicada en esta iteración mueve el cálculo CPU puro a `Task.Run`, deja
el fallback analítico activo y reserva `Image`/`ImageTexture` para el hilo de Godot. También
limita la transmitancia usada por exposición a 10 Hz, que coincide con la escala temporal de
adaptación ocular y elimina integraciones repetidas por frame.

Regresión rápida posterior:

| Métrica | Resultado |
|---|---:|
| `SimulationLoaded` | 1786.1 ms en una ejecución |
| Arranque del worker | no bloqueante, `worker=true` |
| 60 frames headless | salida normal en 3.36 s |
| Errores de script/hilo | ninguno observado |

El tiempo CPU total del worker se seguirá midiendo en `worker_complete`; no se debe confundir
ese tiempo de precálculo con el tiempo de frame. La prueba rápida sólo exige que el vuelo sea
usable mientras el worker continúa.

## 2. Reglas de coordinación entre agentes

Cada agente trabaja en un worktree y rama propia. El coordinador crea, por ejemplo:

```text
../worktrees/perf-baseline
../worktrees/perf-startup
../worktrees/perf-scheduler
../worktrees/perf-starship
../worktrees/perf-render
../worktrees/perf-assets
../worktrees/perf-qa
```

Reglas obligatorias:

1. Un agente no modifica archivos fuera de su ownership sin un contrato escrito en el PR.
2. Ningún agente cambia `project.godot`, añade autoloads temporales o versiona harnesses de
   captura.
3. Cada PR incluye: comando de benchmark, baseline, resultado, memoria, asignaciones si se
   midieron, invariantes físicas y plan de rollback.
4. No se acepta una optimización sólo porque sea más rápida: debe conservar determinismo,
   energía/momento dentro de las tolerancias existentes y el comportamiento de staging,
   entrada atmosférica, eclipse y contacto.
5. El agente QA puede rechazar una mejora por falta de medición o por una regresión visual,
   aunque el benchmark CPU sea mejor.
6. Los cambios que toquen Godot desde un worker están prohibidos. El worker sólo produce
   datos CPU inmutables; la entrega al árbol se hace en el hilo principal mediante polling y
   llamadas diferidas.

## 3. División de trabajo

### Agente 0 — laboratorio de rendimiento y contrato

Ownership: `tools/`, telemetría de rendimiento, documentación de baseline. No modifica física.

Entregables:

- Añadir un snapshot estable con: tiempo de arranque, `SimulationLoaded`, p50/p95/p99 de frame,
  tiempo de física, tiempo de script, tiempo de render, GC/allocation si está disponible,
  número de substeps, cuerpos/vessels activos, LUT en cola/completa y memoria.
- Separar explícitamente tres clases de coste: carga, spike intermitente y coste por frame.
- Integrar `tools/flight_startup_quick_check.sh` en CI local y producir artefactos comparables.
- Añadir un formato CSV/JSON versionado por commit, renderer y resolución.

Gate: dos ejecuciones consecutivas del baseline tienen una variación menor al 10 % o se
documenta el ruido del entorno. Ningún agente de optimización empieza sin este informe.

### Agente 1 — pipeline atmosférico y arranque

Ownership: `scripts/SkyController.cs`, `ExosphereSimulation/Atmosphere*Lut.cs` sólo si hace
falta, más pruebas de LUT.

Entregables:

- Mantener el cálculo puro fuera del hilo principal, como en la corrección actual.
- Añadir cache persistente por hash de perfil, versión de LUT, orden, resolución y sample
  count; invalidar con cambio de JSON o shader contract.
- Generar primero una configuración de fallback barata y hacer promoción atómica del LUT
  completo cuando esté listo.
- Medir memoria de arrays CPU, texturas y tiempo de upload por separado.
- No construir Earth, Mars y Venus a la vez: priorizar cuerpo dominante y precargar sólo el
  siguiente cuerpo si hay presupuesto.
- Definir cancelación/abandono segura al salir de escena y evitar trabajo huérfano.

Pruebas: finitud, no negatividad, monotonicidad, igualdad aproximada con el oráculo y smoke
de que `Flight` alcanza 60 frames sin un build síncrono. Gate: ningún spike de arranque >250 ms
causado por una LUT; el worker no puede modificar objetos Godot.

### Agente 2 — scheduler de simulación y LOD físico

Ownership: `ExosphereSimulation/Universe.cs`, scheduler asociado y tests de warp/rails.

Entregables:

- Mantener el vessel activo en timestep fijo y completo mientras haya thrust, atmósfera,
  contacto, calentamiento, docking o evento de misión.
- Pasar vessels distantes a propagación Kepleriana; usar frecuencias inferiores para objetos
  fuera del interés del jugador, sin saltarse periapsis, SOI, combustible ni eventos.
- Reemplazar snapshots LINQ repetidos (`ToList`, `Any`, búsquedas de cuerpo) por buffers
  reutilizables donde el profiler confirme asignación/coste.
- Construir un `PhysicsInterestSet` explícito: active, nearby, event-sensitive, on-rails,
  destroyed/anchored.
- Instrumentar substeps por vessel y razón de salida de rails.

Pruebas: paridad real-time/warp, conservación de masa/momento/energía donde aplique,
transición rails/off-rails, periapsis atmosférico, contacto y determinismo de dos ejecuciones.
Gate: cero regresión en la suite de navegación, staging, reentry y warp; ningún vessel
event-sensitive puede dormir.

### Agente 3 — hot paths de Starship y partes

Ownership: `ExosphereSimulation/Vessel.cs`, `Parts/`, runtime de motores y tests Starship.

Entregables:

- Perfilar `Vessel.Tick`, `ActiveEngines`, gimbal diferencial, consumo y aerodinámica antes
  de editar.
- Cachear lecturas derivadas por tick: presión, densidad, velocidad relativa, heat flux,
  centro de masa, inercia y readouts de motor.
- Eliminar LINQ y diccionarios temporales de rutas por frame; usar buffers estables y claves
  de engine preasignadas.
- Mantener engine-out, spool, mixture ratio, thrust vector y staging como estados explícitos.
- Evaluar SIMD sólo después de eliminar recomputación y asignaciones; no sacrificar precisión
  doble en integración o contacto.

Pruebas: 33+6 motores, engine-out asimétrico, hot-stage, consumo, TWR, gimbal, break-up y
determinismo. Gate: misma trayectoria dentro de las tolerancias actuales y mejora medible en
`Vessel.Tick`/allocations; no se acepta sólo subir el timestep.

### Agente 4 — render, UI y consumidores de estado

Ownership: `scripts/*Controller.cs`, `VesselRenderer.cs`, `VisualExposureController.cs`,
`SkyController.cs` sólo con coordinación del Agente 1.

Entregables:

- Clasificar cada `_Process` como crítico, 10–20 Hz, evento/dirty o sólo visible en una vista.
- Actualizar exposure/transmitancia, HUD, solar geometry, materiales, plumas, flaps y tren
  sólo cuando cambien los inputs o al cadence apropiado.
- Eliminar `GetEngineReadouts(...).ToDictionary()` por frame y escrituras redundantes de
  shader/materiales.
- Aplicar `ProcessMode`/thread groups sólo a nodos aislados; todo acceso cruzado usa la API
  diferida segura de Godot.
- Separar estado simulado de interpolación visual para que bajar la frecuencia de UI no
  cambie la física.

Pruebas: captures de pad, ascenso, órbita, EDL, eclipse y noche; comparación de clipping,
luminancia, terminador, estrellas y exposición. Gate: sin hitch visible >100 ms durante 10 s
de vuelo normal y sin cambios de controles por cadencia visual.

### Agente 5 — GPU, escena y memoria de recursos

Ownership: `scenes/flight/Flight.tscn`, `scripts/PlanetMaterials.cs`, mallas/materiales,
shaders y recursos visuales.

Entregables:

- Medir CPU render-prep y GPU por separado en Forward+ y renderer disponible.
- Compartir mallas/materiales realmente inmutables; revisar duplicación de texturas 8K y
  recursos de planetas.
- Añadir LOD/geometric simplification por distancia y frustum sin tocar la escala física.
- Revisar sombras, sky cubemap incremental, resolución de LUT GPU y coste de planetas fuera
  de cámara.
- Mantener memoria de GPU/CPU por debajo del presupuesto documentado, no ocultar consumo con
  liberaciones frecuentes que produzcan stutter.

Gate: mejora de frame GPU con igualdad visual aprobada; no se acepta bajar resolución sin
medir diferencia de luminancia/color ni romper eclipse/terminador.

### Agente 6 — QA, comparador y visual gate

Ownership: `ExosphereSimulation.Tests/`, `tools/tests/`, contratos de `visual_playtest.sh`,
reportes de comparación.

Entregables:

- Matriz automatizada Earth día/amanecer/atardecer/noche/eclipse a 10/30/70/120/400 km;
  Mars/Venus día en superficie/órbita/noche; Starship sandbox y staging.
- Stress de 1, 10, 50 y 100 vessels, con combinaciones active/nearby/on-rails.
- Detección de NaN, infinito, radiancia negativa, clipping amplio, stalls y pérdida de
  progreso físico.
- Comparación de commit base vs candidato en p50/p95/p99, memoria y visual diff.
- Sólo este agente puede declarar PASS de promoción.

## 4. Secuencia y dependencias

```text
Agente 0 baseline
    ├── Agente 1 startup/LUT ─────┐
    ├── Agente 2 scheduler ───────┼── Agente 6 integración y gates
    ├── Agente 3 Starship ────────┤
    ├── Agente 4 render/UI ───────┤
    └── Agente 5 GPU/recursos ────┘
```

Orden de merge:

1. Baseline/contratos y la corrección de arranque actual.
2. Agente 1, para que el resto mida sin el stall conocido. [completado en fase 7]
3. Agente 2 scheduler y parte del Agente 4 HUD, con ownership separado. [completado en fase 8]
4. Agente 3 Starship/PartGraph: fase 9 aplicada; Agente 4 render restante y Agente 5 GPU/recursos
   continúan en paralelo.
5. Agente 6 ejecuta la matriz contra el commit padre y cada candidato.
6. Integración final en una rama única; repetir baseline completo y visual playtest.

Si dos agentes necesitan la misma API, el coordinador crea primero un contrato pequeño
(interfaz, snapshot o evento) en una rama de integración; no se resuelve copiando cambios
entre archivos compartidos.

## 5. Presupuesto y criterios de aceptación

### Arranque

- `SimulationLoaded` p95 <= 4 s en el entorno de referencia.
- Vuelo usable durante cualquier precálculo: ningún LUT CPU síncrono después de entrar al
  nivel.
- Ningún frame bloqueado >250 ms por generación/upload atmosférico.
- Memoria máxima y tamaño de cada textura registrados; ningún crecimiento no acotado.

### Vuelo

- Objetivo visual: 60 FPS en hardware de referencia; reportar p50/p95/p99 y no sólo FPS medio.
- Sin spike >100 ms durante 10 s en pad, ascenso, coast y entrada, salvo transición explícita
  documentada.
- Física crítica mantiene timestep y no se actualiza por debajo de lo necesario para contacto,
  thrust, aero, thermal, docking o misión.
- Cero NaN, infinito, radiancia negativa o pérdida de progreso.

### Corrección

- `dotnet build` de ambos proyectos: 0 warnings, 0 errors.
- Suite xUnit completa sin fallos.
- `tools/atmosphere_quick_check.sh` PASS.
- `tools/flight_startup_quick_check.sh` PASS.
- Contrato visual PASS y matriz completa con reporte por escena.
- Orden 4 sigue siendo oficial; orden 5 continúa experimental hasta demostrar mejora
  consistente, monotonicidad, coste y beneficio visual.

## 6. Fuentes técnicas y límites

La estrategia sigue la recomendación de medir primero y separar coste por frame, spikes y carga
de [Godot: General optimization tips](https://docs.godotengine.org/en/4.6/tutorials/performance/general_optimization.html).
Los workers sólo procesan datos que no tocan el árbol de Godot, siguiendo las restricciones de
[Godot: Using multiple threads](https://docs.godotengine.org/en/stable/tutorials/performance/using_multiple_threads.html)
y las reglas de acceso de [Node process thread groups](https://docs.godotengine.org/en/stable/classes/class_node.html).
El profiler estándar se usará para scripting/física y el visual profiler para render, porque
no miden exactamente lo mismo: [Godot Debugger/Profiler](https://docs.godotengine.org/en/stable/tutorials/scripting/debug/debugger_panel.html).
Para CPU y memoria .NET se podrá adjuntar una traza de
[dotnet-trace](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace).

Este plan no autoriza todavía cambiar el modelo físico ni reducir la fidelidad del Starship.
Cada reducción de frecuencia debe demostrar que sólo cambia la representación o la propagación
de objetos fuera del conjunto de interés, y que los eventos físicamente sensibles siguen siendo
continuos y deterministas.
