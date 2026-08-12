# Agente 0 — baseline de rendimiento de Flight/Starship

Estado: cerrado con diagnóstico y commit aislado  
Rama: `agent-0-perf-baseline`  
Fecha: 2026-08-11  
Alcance: escena `res://scenes/flight/Flight.tscn`, Starship sandbox por defecto, Godot 4.6.3 mono, .NET 8

## Resumen ejecutivo

El vuelo ya no se bloquea en el hilo principal al generar la LUT atmosférica: el log confirma
`PERF_ATMOS ... stage=queued worker=true`. Sin embargo, el arranque todavía es caro y el
precálculo atmosférico continúa consumiendo CPU durante muchos segundos:

- `SimulationLoaded`: `2269.3–2408.4 ms` en dos ejecuciones secuenciales reproducibles.
- La creación de planetas consume aproximadamente `1763.4–1827.4 ms` incrementales, entre
  `73.7%` y `75.9%` del tiempo hasta `SimulationLoaded`.
- Crear el stack Starship consume `412.3–482.9 ms` incrementales.
- RSS máximo en arranque: `838680–839268 KiB` (`818.9–819.5 MiB`).
- 300 iteraciones headless con `--fixed-fps 60` terminan correctamente en `4.66–4.77 s`.
- 6000 iteraciones terminan en `18.62 s`, pero no aparece `worker_complete`; el worker de LUT
  sigue pendiente al salir de esa corrida.
- No hay evidencia de error de script, acceso inválido entre hilos ni fallo de Starship en
  estas corridas.

La conclusión de este agente es que hay dos problemas distintos que no deben mezclarse:

1. El costo de construcción de escena —principalmente `SpawnPlanets`— retrasa la entrada al
   nivel.
2. El worker atmosférico evita el hitch del hilo principal, pero todavía puede mantener una
   carga CPU y memoria elevada durante una ventana larga; requiere cancelación, cache por hash
   y presupuesto de trabajo antes de considerarse resuelto.

## Reproducción y artefactos

El runner añadido es [tools/perf/flight_baseline.sh](../../tools/perf/flight_baseline.sh).
No instala autoloads, no modifica `project.godot`, no crea harnesses de captura y no cambia
ningún script del juego.

Ejecución corta reproducible:

```bash
OUT_DIR=/tmp/exo_agent0_fixed_seq1 \
FRAMES=300 TIMEOUT_SECONDS=60 FIXED_FPS=60 \
bash tools/perf/flight_baseline.sh
```

Repetición secuencial:

```bash
OUT_DIR=/tmp/exo_agent0_fixed_seq2 \
FRAMES=300 TIMEOUT_SECONDS=60 FIXED_FPS=60 \
bash tools/perf/flight_baseline.sh
```

Corrida larga para detectar trabajo pendiente:

```bash
OUT_DIR=/tmp/exo_agent0_fixed_6000 \
FRAMES=6000 TIMEOUT_SECONDS=60 FIXED_FPS=60 \
bash tools/perf/flight_baseline.sh
```

Cada ejecución conserva `command.txt`, `flight.stdout`, `flight.log`, `flight.time` y
`summary.tsv` en `OUT_DIR`. `flight.time` procede de `/usr/bin/time -v`; la memoria reportada
es RSS máximo del proceso completo Godot/Mono.

## Resultados medidos

### Arranque secuencial

| Fase acumulativa | Corrida 1 | Corrida 2 |
|---|---:|---:|
| Universe cargado | 38.5 ms | 32.8 ms |
| Launch sites cargados | 43.4 ms | 37.5 ms |
| Starship creado | 526.3 ms | 449.8 ms |
| Campaign inicializada | 579.3 ms | 504.5 ms |
| Planetas creados | 2406.7 ms | 2267.9 ms |
| `SimulationLoaded` | 2408.4 ms | 2269.3 ms |
| LUT de densidad | 3.7 ms | 3.5 ms |
| LUT atmosférica | `queued worker=true` | `queued worker=true` |

Costos incrementales derivados de los logs:

| Operación | Corrida 1 | Corrida 2 |
|---|---:|---:|
| Starship después de launch sites | 482.9 ms | 412.3 ms |
| Campaña después de Starship | 53.0 ms | 54.7 ms |
| `SpawnPlanets` después de campaña | 1827.4 ms | 1763.4 ms |

La variación de `SimulationLoaded` entre estas dos corridas es `139.1 ms` (`6.1%` tomando
la primera corrida como referencia). Es suficientemente baja para usar este baseline como
señal inicial, pero no permite todavía atribuir diferencias pequeñas de rendimiento a un
cambio.

### Frames, CPU y memoria

| Corrida | Iteraciones | Wall | Project FPS observado | User CPU | System CPU | RSS máximo |
|---|---:|---:|---:|---:|---:|---:|
| Secuencia 1 | 300 | 4.77 s | no emitido | 5.32 s | 0.98 s | 839248 KiB |
| Secuencia 2 | 300 | 4.66 s | no emitido | 5.15 s | 1.01 s | 839268 KiB |
| Larga | 6000 | 18.62 s | 459 | 32.38 s | 1.76 s | 838592 KiB |

`--fixed-fps 60` fija el delta de simulación, pero no representa un FPS visual real: la escena
se ejecuta con el renderer dummy de headless. `Project FPS` es una señal de iteraciones del
motor, no una aprobación de 60 FPS en GPU. El objetivo de esta corrida es detectar progreso,
errores y carga relativa; el p50/p95/p99 visual debe medirse después con Xvfb y el renderer
del entorno de referencia.

### Precálculo atmosférico

En las tres corridas de 300/6000 iteraciones sólo aparecen:

```text
PERF_ATMOS body=earth stage=density_lut ms=...
PERF_ATMOS body=earth stage=queued worker=true
```

No aparece `stage=worker_complete` antes de que termine la corrida larga de 6000 iteraciones.
Esto no demuestra por sí solo un deadlock: el atlas angular y el scattering múltiple contienen
bucles anidados de integración y pueden superar la duración observada. Sí demuestra que no hay
un límite de tiempo de precálculo suficientemente corto para el arranque y que el worker debe
tratarse como trabajo cancelable y presupuestado.

La memoria elevada no puede atribuirse únicamente a la LUT con esta instrumentación: el RSS
incluye Godot, Mono, recursos importados, shaders, texturas, mallas, escena y arrays del worker.
El siguiente agente debe separar al menos memoria de escena, arrays CPU de LUT y texturas GPU.

## Top 10 de hotspots

Los tres primeros tienen evidencia temporal directa. Los demás son candidatos estáticos
priorizados por operaciones repetidas, recomputación o asignaciones visibles en el código. No
se inventan tiempos para estos candidatos porque las reglas de este agente prohíben añadir
instrumentación a física/render.

1. **Construcción de planetas durante el arranque** — [SimulationBridge.cs:490](../../scripts/SimulationBridge.cs:490).
   `SpawnPlanets` crea una esfera `96 × 48` para cada cuerpo, materiales y nodos de escena, más
   el anillo de Saturno. Es el hotspot de arranque medido: `1.763–1.827 s` incremental y
   `73.7–75.9%` del tiempo hasta `SimulationLoaded`.

2. **Worker de LUT atmosférica de Earth** — [SkyController.cs:384](../../scripts/SkyController.cs:384)
   y [SkyController.cs:424](../../scripts/SkyController.cs:424). Construye transmitancia,
   scattering global, orden experimental opcional y atlas angular. La evidencia de ejecución
   es `worker=true` sin `worker_complete` después de 6000 iteraciones/18.62 s. Es el mayor
   riesgo de CPU sostenida y memoria pendiente, aunque ya no bloquea el hilo principal.

3. **Creación del stack Starship** — [SimulationBridge.cs:448](../../scripts/SimulationBridge.cs:448).
   Carga definiciones, crea seis partes, estados de motores y juntas antes de emitir
   `SimulationLoaded`. Coste incremental medido: `412–483 ms`.

4. **Decisiones globales y substeps en `Universe.Tick`** — [Universe.cs:463](../../ExosphereSimulation/Universe.cs:463).
   Cada tick ejecuta `Any(RequiresBoundedWarpPropagation)`, otra consulta para contacto,
   selecciona el modo de integración y puede entrar en un `while` de substeps. El snapshot
   `_vessels.ToList()` aparece en [Universe.cs:592](../../ExosphereSimulation/Universe.cs:592),
   por lo que la ruta escala en asignaciones con más vessels.

5. **RK4 off-rails y contactos de Starship** — [Universe.cs:949](../../ExosphereSimulation/Universe.cs:949).
   La ruta llama `vessel.Tick`, evalúa contactos iniciales/finales y ejecuta
   `RK4Integrator.StepPosVel` con callback; dentro del callback vuelve a evaluar landing/catch
   contact y aceleración. Es correcto físicamente, pero es el principal candidato de coste por
   frame cuando el vehículo está en atmósfera, bajo thrust o sobre soporte.

6. **Tick de dinámica de Vessel y motores** — [Vessel.cs:592](../../ExosphereSimulation/Vessel.cs:592).
   Avanza spool, consume propelente, recorre crew y motores, calcula gimbal diferencial,
   torque real y aerodinámica. `Parts.GetPitchYawRollAngularAcceleration` y las búsquedas de
   flaps se ejecutan en cada tick; el coste crecerá con clusters y stages.

7. **Readouts y materiales por frame del renderer de Starship** — [VesselRenderer.cs:1147](../../scripts/VesselRenderer.cs:1147).
   Escanea partes, resuelve cuerpo/presión, vuelve a construir `GetEngineReadouts(...).ToDictionary()`
   y escribe parámetros de múltiples materiales en cada frame. La ruta no está protegida por
   dirty flags ni cadence visual.

8. **HUD de motores con LINQ y recomputación redundante** — [EngineGridHUD.cs:66](../../scripts/EngineGridHUD.cs:66).
   Hace `ActiveEngines.ToList`, readouts `ToList`, dos `Select` para copiar arrays y además
   recalcula thrust, flow, Isp, peso y TWR. Es una fuente clara de asignaciones por frame y
   duplica parte del trabajo de telemetría del renderer.

9. **Geometría solar duplicada entre sistemas y luz** —
   [SystemsController.cs:42](../../scripts/SystemsController.cs:42) y
   [SunController.cs:34](../../scripts/SunController.cs:34). Ambos recorren todos los cuerpos
   no solares y llaman `MissionGeometry.LimbDarkenedSolarDiscVisibility` cada frame. El mismo
   estado solar debería ser un snapshot compartido con una cadence definida, sin cambiar la
   física de eclipse.

10. **Tres SubViewport de cockpit siempre redibujados** — [CockpitInstruments.cs:36](../../scripts/CockpitInstruments.cs:36).
    Cada frame se llama `QueueRedraw` sobre tres paneles de `512 × 512`; además son targets de
    render permanentes. Sólo es visible en cockpit, pero puede dominar costo GPU/CPU de UI en la
    vista que el usuario usa para inspeccionar Starship.

## Pruebas y bloqueos

Pasaron:

```text
dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet
dotnet build Exosphere.csproj --nologo -v quiet
bash -n tools/perf/flight_baseline.sh
bash tools/tests/flight_startup_contract_test.sh
bash tools/flight_startup_quick_check.sh
```

Los dos builds terminaron con `0 Warning(s), 0 Error(s)`. El contrato reportó:

```text
PASS asynchronous startup fixture accepted
PASS synchronous startup fixture rejected
flight_startup_quick_check: PASS
```

La suite xUnit no pudo arrancar en el sandbox administrado. Comando y bloqueo:

```bash
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-restore --nologo
```

Resultado:

```text
System.Net.Sockets.SocketException (13): Permission denied
Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.SocketServer.Start
Test Run Aborted.
```

El error ocurre antes de descubrir/ejecutar asserts, al crear el socket de comunicación de
VSTest. Debe repetirse fuera de este sandbox para obtener el conteo xUnit; no se debe marcar
como regresión del baseline.

También se intentó `perf stat`, pero el kernel del entorno tiene `perf_event_paranoid=4` y
rechaza todos los eventos de performance. La medición alternativa usada aquí es `/usr/bin/time
-v`, telemetría de Godot y los logs `PERF_*` ya existentes.

## Recomendación de handoff

El Agente 1 debe mantener la frontera actual: el worker sólo puede producir datos CPU
inmutables y la creación de `Image`/`ImageTexture` debe permanecer en el hilo principal. Debe
añadir cancelación al salir de escena, cache persistente por hash de perfil/resolución/orden y
telemetría separada para `cpuMs`, upload, arrays y texturas. Antes de cualquier optimización de
física, el Agente 4/5 debería medir `SpawnPlanets` y activar LOD/creación diferida sin alterar
la escala física.

La medición de `_Process` por método, p50/p95/p99 real, GPU y asignaciones administradas queda
pendiente de un harness de profiling con permisos para instrumentar o de una captura Xvfb
controlada. Este agente no añadió dicha instrumentación para respetar el ownership solicitado.

Referencias técnicas primarias para el siguiente ciclo:

- [Godot: General optimization tips](https://docs.godotengine.org/en/4.6/tutorials/performance/general_optimization.html)
- [Godot: Using multiple threads](https://docs.godotengine.org/en/stable/tutorials/performance/using_multiple_threads.html)
- [Godot: Node process thread groups](https://docs.godotengine.org/en/stable/classes/class_node.html)
- [Godot: Debugger and profiler](https://docs.godotengine.org/en/stable/tutorials/scripting/debug/debugger_panel.html)
- [.NET profiling tools](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/profilers)
