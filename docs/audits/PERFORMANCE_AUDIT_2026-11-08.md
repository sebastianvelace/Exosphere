# Auditoría de rendimiento — 2026-11-08

Objetivo: que el simulador se sienta fluido en equipos sin GPU dedicada mientras la física
mantiene rigor de ingeniería, y que las fases E1–E4 (`docs/physics/environment_and_recalibration_plan.md`)
entren con presupuestos medidos en lugar de suposiciones.

## 1. Arquitectura actual (verificada)

- La simulación avanza en el hilo principal de Godot dentro de `SimulationBridge._Process` con
  `Universe.Tick(delta)`: acumulador de tiempo, pasos fijos deterministas de 20 ms
  (`DeterministicControlPeriodSeconds`), hasta ×4 en física completa, raíles keplerianos y
  costas acotadas en warp alto, y un presupuesto de sub-pasos opcional (`SchedulerBudgetEnabled`).
  Es el diseño correcto para un simulador: el paso físico no depende del frame.
- 78 fases de optimización previas (`docs/audits/PERF_*`, hasta 2026-10-31) cubren la
  presentación: HUD, cielo, plumas, audio, cámara y sol a 20 Hz con caché. Son **anteriores** a
  F1–F5 (aerodinámica distribuida, flaps físicos, RCS por pieza, transporte de masa variable,
  slosh, 6-DoF acoplado por defecto), que nunca se midieron en rendimiento.

## 2. Mediciones de la simulación

Release, un núcleo Intel Xeon del entorno de CI, 6000 pasos de 20 ms tras 50 de calentamiento:

| Escenario | Media | p50 | p95 | p99 | Máx | Asignado por paso | Margen sobre tiempo real |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Flight 14 ascenso, producción | 0,205 ms | 0,186 | 0,271 | 0,388 | 8,0 | **209 KB** | ×97 |
| Saturn V AS-506, 0–200 s | 0,350 ms | 0,313 | 0,512 | 0,656 | 11,6 | **291 KB** | ×57 |
| Flight 14 sin transporte de masa variable | 0,097 ms | 0,089 | 0,126 | 0,202 | 1,7 | 16 KB | ×206 |
| Flight 14 camino dividido legacy | 0,089 ms | 0,074 | 0,127 | 0,194 | 2,6 | 16 KB | ×226 |
| Flight 14 sin aerodinámica distribuida | 0,188 ms | — | — | 0,296 | 2,8 | 209 KB | ×106 |
| Flight 14 sin flaps físicos | 0,191 ms | — | — | 0,300 | 2,5 | 209 KB | ×105 |
| Flight 14 sin gradiente gravitatorio | 0,193 ms | — | — | 0,319 | 3,0 | 209 KB | ×104 |
| Flight 14 sin slosh (no es la configuración de producción) | 0,397 ms | 0,236 | 1,478 | 1,774 | **66,2** | 209 KB | ×50 |

Conclusiones:

1. **El cálculo no es el problema.** A velocidad normal la simulación usa ~0,2–0,35 ms de un
   frame de 16,7 ms (1–2 %); en ×4, ~1,4 ms.
2. **La memoria sí lo es.** 209–291 KB por paso son 10–15 MB/s a ×1 y hasta 60 MB/s a ×4. El
   recolector de basura se dispara a menudo y produce los picos de 8–11 ms: tirones visibles.
   El transporte de masa variable (F4) aporta ~193 KB de esos 209.
3. Aerodinámica distribuida, flaps y gradiente gravitatorio cuestan ~5–10 % cada uno: aceptable.
4. **Anomalía:** con el slosh apagado el paso es más lento y tiene picos de 66 ms. Es un camino
   de respaldo que no es el de producción, pero indica un fallback caro que hay que investigar.

### Perfil de asignaciones (EventPipe, `GCAllocationTick`, Flight 14, 4000 pasos)

| % | Tipo | Origen |
| --- | --- | --- |
| 47,0 | `ResourceStoreLoad[]` | Copias de las cargas de todos los almacenes en cada ventana de fondos |
| 14,5 | `TransportOutlet[]` | Salidas reconstruidas por paso |
| 7,9 | `Entry<string>[]` | Diccionarios con clave de texto en el camino caliente |
| 5,8 | enumerador de `MassKnot` | `foreach` sobre interfaz (boxing) |
| 5,5 | iterador `Where` de `FundedStoreDebit` | LINQ en el camino caliente |
| 2–1 % c/u | `TransportStoreDebit[]`, `HashSet<string>`, `FixedShapeMassComponent[]`, `MassOutflow[]`, `FundedLiquidEngine[]`, cierres | Reconstrucción del modelo por paso |

### Perfil de CPU (muestreo, excluyendo la espera de la sonda)

Crecimiento y copia de listas (`set_Capacity`, `Resize`, `_ZeroMemory`, `_Memmove`) es el mayor
bloque exclusivo. `VariableMassStepModel.TryBuild` se lleva ~23 % del tiempo activo (inclusivo),
`ResourceFundingCapture.ReadStores` ~10 %, `OpenSystemTransport.Sample` ~4 %, la interpolación
de mapas de motor (`EnginePerformanceEvaluator.InterpolatePressure` / `InterpolatePerformanceMap`)
~5 %, la geometría de empuje por instancia ~5 % y la telemetría del scheduler (`Stopwatch`,
`PublishSchedulerTelemetry`) ~5 %.

## 3. Capa de juego y gráficos (revisión estática; Godot no está disponible en este entorno)

- `SimulationBridge._Process` llama a `GetTree().Root.FindChild("AutopilotController", true, false)`
  **en cada frame** (línea ~421), aunque el mismo nodo ya está en caché en `_autopilotController`.
  Hay 62 llamadas a `FindChild`/`GetNodesInGroup` en `scripts/` y 66 usos de LINQ o listas nuevas;
  falta clasificar cuáles están en `_Process`.
- Perfiles gráficos (`scripts/UI/GraphicsSettings.cs`): `Automatic`, `IntegratedGpu` y `Quality`.
  El integrado solo baja la escala 3D a 0,75, quita MSAA, usa FXAA y 2 muestras de luz en nubes.
  No hay resolución dinámica por tiempo de GPU, ni FSR 2, ni ajustes por perfil de sombras,
  volumétricos, nubes o reflejos, ni límite de FPS, ni un perfil "Bajo".
- La línea base de render existente (`PERF_RENDER_BASELINE_PHASE51H_REPORT.md`) se midió con
  renderizado por software (llvmpipe, ~1,7 s por frame); no sirve para juzgar una iGPU real.
  Las herramientas `tools/perf/flight_baseline.sh` y `tools/perf/renderer_benchmark.sh` existen y
  hay que correrlas en hardware real.

## 4. Presupuestos de rendimiento (propuesta, verificables)

| Magnitud | Presupuesto | Cómo se verifica |
| --- | --- | --- |
| Paso de simulación, peor vehículo activo (Saturn V completo, Flight 14 con 28 naves) | media ≤ 0,5 ms, p99 ≤ 1,0 ms en el CPU de CI | Sonda de rendimiento en `ci_quick` (rápida, sin red) |
| Memoria asignada en régimen estable | ≤ 1 KB por paso para la nave activa (objetivo 0) | Prueba con `GC.GetAllocatedBytesForCurrentThread` sobre 500 pasos |
| Recolector durante el vuelo | Sin gen2; gen0 esporádico | Contadores en la sonda |
| Cada capacidad nueva de E1–E4 | ≤ 50 µs por paso (gravedad grado 50 ≤ 15 µs por evaluación) | Microbenchmark por proveedor |
| Frame en iGPU con perfil integrado | objetivo 60 fps (16,7 ms), piso 30 fps; p99 ≤ 25 ms | `tools/perf/flight_baseline.sh` en el equipo de referencia del usuario |
| Warp ×4 en física completa | ≤ 4 ms de simulación por frame | Sonda |

## 5. Plan de optimización (unidades de trabajo, antes de E0)

| Unidad | Cambio | Riesgo | Salida |
| --- | --- | --- | --- |
| **O1** | Cero asignaciones en el camino de masa variable y slosh: buffers reutilizados en `ResourceFundingWindow`/`ResourceFundingCapture`, `VariableMassStepModel` en caché por versión de topología y actualizado en sitio, arreglos de salidas y débitos reutilizados, claves `ResourceStoreId` en vez de texto, sin LINQ ni enumeradores con boxing | Bajo: no cambia la física | Suite completa **bit a bit igual**; ≤ 1 KB por paso; sin picos de GC |
| **O2** | Puntos calientes de CPU: búsqueda binaria o índice en caché en los mapas de motor, caché de la geometría de empuje cuando el gimbal no cambia, telemetría del scheduler solo en diagnóstico, revisar `StressSolver` | Bajo | Bit a bit igual; media de paso −30 % o más |
| **O3** | Investigar el respaldo caro sin slosh (picos de 66 ms) | Medio | Causa documentada y corregida o acotada |
| **O4** | Capa de juego: quitar `FindChild` por frame (usar la caché), clasificar los 62 `FindChild` y 66 LINQ por si están en `_Process` | Bajo | Sin búsquedas de árbol ni asignaciones por frame en el puente |
| **O5** | Prueba de presupuesto en `ci_quick`: asignación por paso y tiempo p99 en los dos escenarios de referencia, con margen para el ruido del CI | Bajo | Regresiones de rendimiento detectadas en minutos |
| **O6** | Gráficos para iGPU: perfil "Bajo"; resolución dinámica por tiempo de GPU (`Viewport.GetMeasuredRenderTimeGpu`); FSR 2.2 en Forward+; sombras, volumétricos, nubes y reflejos por perfil; límite de FPS y vsync configurable | Medio (visual) | Mediciones con `tools/perf/flight_baseline.sh` en el equipo del usuario, antes y después |
| **O7** | Diseño de E1–E4 con presupuesto: estado de cuerpos en caché por instante del paso, orientación terrestre una vez por paso, grado de gravedad por altitud, viento por tabla, NRLMSIS solo sobre 80 km y en caché | — | Se aplica al implementar E1–E4 |
| **O9** | Picos de primera ejecución: el despegue compila ~1078 métodos (~100 ms de JIT) al activarse por primera vez el camino de masa variable; precompilar `ExosphereSimulation` con ReadyToRun en la exportación (medido: 64 → 27 ms en la sonda) y calentar ese camino en la pantalla de carga | Bajo | Validar con la exportación real en el equipo del usuario |
| **O8** (diferido) | Simulación en hilo de trabajo con instantánea doble para la presentación | Alto (determinismo) | Solo si los presupuestos de la sección 4 no se cumplen; hoy la simulación usa 1–2 % del frame |

Orden: O1 → O2 → O5 → O4 → O3 → O6, y luego E0 del plan de entorno. O1 es el que más impacto
tiene en la fluidez (elimina la causa de los tirones) y no cambia ningún resultado físico.

## 6. Reproducción

- Sonda de simulación: un proyecto de consola que crea `Flight14LaunchDiagnostic` y avanza
  pasos de 20 ms con `Stopwatch` y `GC.GetAllocatedBytesForCurrentThread` (O5 la convierte en
  prueba permanente).
- Asignaciones: `dotnet-trace collect --providers "Microsoft-Windows-DotNETRuntime:0x1:5"` y
  agregación de `GCAllocationTick` por tipo con TraceEvent.
- CPU: `dotnet-trace collect --providers "Microsoft-DotNETCore-SampleProfiler,..."` y
  `dotnet-trace report <traza> topN`.

## 7. Resultados

### O1, primera parte (2026-11-08)

Sin cambiar ningún resultado: huellas SHA-256 de los bits de estado idénticas antes y después
(Flight 14, 12 000 pasos con hot staging, `B8C2AC1A…DABD2`; Saturn V AS-506, 20 000 pasos con
staging, `7C35A95E…B3F2`).

| Cambio | Asignado por paso (Flight 14) |
| --- | --- |
| Línea base | 212 KB |
| Arreglos internos en vez de enumeradores de interfaz; captura de almacenes sin arreglos intermedios | 147 KB |
| `TryBuild` sin LINQ, tablas de búsqueda reutilizadas por hilo | 118 KB |
| Hijos de la topología en caché (`StressSolver`), IDs de componente por pieza, buffer de flujos compartido, diccionario con capacidad exacta | 74 KB |
| Capturas de fondos de motores y venteo reutilizadas; listas con capacidad exacta | **60 KB (−72 %)** |

Lo que queda son los datos propios del modelo de cada paso (arreglos inicial/final de la
ventana, componentes y nudos). Llegar a ≤ 1 KB exige un modelo persistente entre pasos que se
actualice en sitio (O1b).

Hallazgo: los picos de 64 ms y 13 ms en pasos fijos (despegue y ~81 s) no son del recolector
sino compilación JIT de primera ejecución (con `TieredCompilation=0` el del despegue sube a
160 ms). Ver O9.

### O4 — búsquedas de árbol por frame (hecho)

Se quitaron las búsquedas recursivas `FindChild` que corrían cada frame: el puente buscaba
`AutopilotController` dos veces por frame (una sin caché cuando no había piloto automático);
ahora es `AutopilotController.Instance` (fijada en `_EnterTree`, borrada en `_ExitTree`), que
también usan `SystemsController` y el cambio de nave. `VesselRenderer.UpdateVehicleLighting`
guarda la luz principal; `CockpitInstruments` reintenta enlazar sus pantallas cada 0,5 s y no
cada frame (una nave sin cabina las buscaba siempre); `CameraController` guarda el
`FloatingOrigin`. Las demás búsquedas son de carga, de eventos o con caché `??=` de nodos que
existen. Los 66 usos de LINQ en `scripts/` quedan para cuando una medición en el equipo del
usuario los señale.

### O9 — calentamiento en la carga (hecho, parte 1)

`Flight/PhysicsWarmup.Prime` vuela un diagnóstico aislado de Flight 14 durante 200 pasos (hasta
el ascenso con masa variable activa) mientras carga la escena; tiene su propio `Universe` y no
toca el estado de juego, y corre una vez por proceso (el código compilado es global). En la
sonda, el pico del despegue desaparece (máximo posterior 18 ms, a ~78 s). Coste de carga medido
en Godot (build Debug, headless): 531 ms dentro de una carga de 8,7 s
(`PERF_STARTUP phase=physics_warmup`). Prueba: `PerformanceBudgetTests` exige que el
calentamiento llegue al camino `active` y que una corrida posterior sea idéntica bit a bit.
Medición en Release tras O1+O9 (Flight 14, 4000 pasos = 80 s de vuelo): media 0,35 ms, p99
1,5 ms, máximo 5,0 ms, 60 KB/paso y **una sola** recolección gen0. Con eso O1b (modelo
persistente, ≤ 1 KB/paso) deja de ser urgente: queda como mejora, no como bloqueo.
El pico de primera ejecución de ~81 s baja a 5,0 ms (dentro de un frame). Límites: no cubre
caminos específicos de otros vehículos (cápsulas, acople). Falta la parte 2: ReadyToRun en la exportación, que se valida con la
exportación real en el equipo del usuario.

Hallazgo de proceso: los vehículos y las piezas reciben GUID aleatorios; una huella de
determinismo no debe incluir identificadores. La física es determinista entre procesos.

