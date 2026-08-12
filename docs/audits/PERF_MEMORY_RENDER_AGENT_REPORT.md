# Fase 2 — Auditoría de memoria y render

Fecha de auditoría: 2026-08-12
Commit base: `cd94733` (`main`)
Alcance: inventario de recursos, escenas, texturas, LUT, `SubViewport`, planetas, shaders y señales estáticas de allocación.
Regla aplicada: no se modificó código de runtime, escenas, materiales ni configuración del juego.

## Conclusión ejecutiva

El riesgo dominante de memoria es el conjunto de texturas, no la escena `.tscn`:

- 11 imágenes suman **27.52 MiB comprimidos en origen**.
- Su límite superior conservador en RGBA8 sin mipmaps es **621.63 MiB**.
- Con mipmaps, el límite superior sube a **828.84 MiB**.
- Cuatro mapas 8K (`earth_day`, `earth_night`, `earth_clouds` y `starmap_milkyway_8k`) representan **512 MiB RGBA8** antes de contar mips, staging, depth y buffers temporales.
- El caché importado de Godot medido en disco es **62.03 MiB**; no es una medición de VRAM.

El riesgo de render/UI más claro son los targets siempre actualizados:

- Tres `SubViewport` de cockpit de `512×512`.
- Un `SubViewport` de construcción de `1024×1024`.
- El código tiene dos sitios de asignación `UpdateMode.Always`; el número estimado en runtime es cuatro instancias.

La escena Flight crea siete esferas planetarias no solares de `96×48`: **9.024 triángulos por esfera, 63.168 en total**, más **320 triángulos** del anillo de Saturno. El coste de creación de planetas ya fue medido como el mayor coste de startup en el baseline anterior; esta auditoría no cambia esa ruta.

No hay todavía una medición válida de VRAM, draw calls, overdraw ni p95/p99 de frame con renderer real. Por tanto, ninguna hipótesis de GPU se presenta como hecho medido.

## 1. Evidencia medida

### 1.1 Auditor estático reproducible

Comando:

```bash
tools/perf/memory_render_audit.sh > /tmp/exo_memory_render_audit_final.txt
```

Resultado relevante:

| Métrica | Valor medido |
|---|---:|
| Imágenes de textura | 11 |
| Tamaño fuente total | 27.52 MiB |
| Caché `.godot/imported` total | 62.03 MiB |
| Caché `.ctex` de texturas | 61.58 MiB |
| Escenas `.tscn/.scn` | 3 |
| Nodos declarados en escenas | 14 |
| Nodos declarados en `Flight.tscn` | 12 |
| Subrecursos de escenas | 3 |
| `SubViewport` en código | 4 referencias/expresiones detectadas |
| Asignaciones `UpdateMode.Always` | 2 sitios; 4 instancias estimadas |
| Shader files | 9 |
| Shader source total | 76,615 bytes |
| Bucles `for` en shaders | 11 |
| Puntos del starfield | 3.500 |
| Líneas de `Amount =` de partículas | 14 |
| Suma estática de `Amount` declarados | 2.130 |

Los cuatro mapas 8K detectados son:

| Recurso | Dimensiones | RGBA8 estimado | Caché importado medido |
|---|---:|---:|---:|
| `earth_clouds.jpg` | 8192×4096 | 128.00 MiB | 19.32 MiB |
| `earth_day.jpg` | 8192×4096 | 128.00 MiB | 13.05 MiB |
| `earth_night.jpg` | 8192×4096 | 128.00 MiB | 5.88 MiB |
| `starmap_milkyway_8k.jpg` | 8192×4096 | 128.00 MiB | 3.39 MiB |

Configuración estática detectada en los 11 imports de textura:

- `metadata.vram_texture=false`.
- `mipmaps/generate=false`.
- `process/size_limit=0`.

Esto prueba la configuración y el tamaño del caché, no la residencia final en la GPU.

### 1.2 Baseline headless actual

Se ejecutaron dos corridas secuenciales de 300 iteraciones. El renderer headless sirve para startup/RSS/CPU, no para aprobar fluidez visual.

Comandos:

```bash
env GODOT_BIN=/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64 \
  OUT_DIR=/tmp/exo_phase2_baseline_300 FRAMES=300 TIMEOUT_SECONDS=60 FIXED_FPS=60 \
  bash tools/perf/flight_baseline.sh

env GODOT_BIN=/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64 \
  OUT_DIR=/tmp/exo_phase2_baseline_300_repeat FRAMES=300 TIMEOUT_SECONDS=60 FIXED_FPS=60 \
  bash tools/perf/flight_baseline.sh
```

Resultados:

| Métrica | Corrida 1 | Corrida 2 |
|---|---:|---:|
| `Universe` cargado | 26.7 ms | 25.5 ms |
| sitios de lanzamiento | 31.1 ms | 29.1 ms |
| Starship creado | 339.0 ms | 342.2 ms |
| campaña inicializada | 375.4 ms | 379.7 ms |
| planetas creados, fase acumulada | 1278.4 ms | 1301.9 ms |
| `SimulationLoaded` | 1279.4 ms | 1302.9 ms |
| RSS máximo | 747336 KiB | 747148 KiB |
| tiempo de pared | 2.96 s | 3.16 s |
| CPU user | 3.17 s | 3.55 s |
| CPU system | 0.69 s | 0.66 s |
| iteraciones | 300 | 300 |

Ambas corridas emitieron `PERF_ATMOS ... stage=queued worker=true` y no emitieron `worker_complete` antes de finalizar. Esto no demuestra un bloqueo; sí demuestra que el trabajo de LUT no terminó dentro de esta ventana corta.

El reporte anterior registró RSS de aproximadamente **838.6–839.3 MiB** y `SimulationLoaded` de **2269–2408 ms** en otras corridas integradas. La diferencia con los **747 MiB / 1279–1303 ms** actuales no se atribuye a este commit de auditoría: este commit no toca runtime y la variación muestra que se deben controlar caché/importación, versión del entorno y condiciones de ejecución antes de usar diferencias pequeñas como regresión o mejora.

## 2. Ranking medido

Este ranking sólo usa datos observados, no inferencias de VRAM:

1. **RSS del proceso Flight:** 747148–747336 KiB en las dos corridas actuales. Es el indicador de memoria más grande disponible, pero mezcla Godot, Mono, recursos importados, texturas, mallas, LUT y arrays CPU.
2. **Carga de planetas durante startup:** la fase acumulada llegó a 1278.4–1301.9 ms; el baseline histórico la identificó como el mayor incremento de startup, alrededor de 1.76–1.83 s en sus condiciones de ejecución.
3. **Cuatro texturas 8K:** 27.52 MiB de fuentes comprimidas no refleja su coste de muestreo; el inventario calculó 512 MiB de RGBA8 sin mips.
4. **Caché importado de texturas:** 61.58 MiB de `.ctex` medidos, dentro de 62.03 MiB de todo `.godot/imported`.
5. **Trabajo atmosférico:** cola asíncrona confirmada; no hubo `worker_complete` en 300 iteraciones. No se asigna un tiempo o tamaño porque no se midió la memoria del worker por separado.

## 3. Ranking estimado / riesgos no medidos

Estos números son límites o aritmética estática, no lecturas de GPU:

1. **Residencia potencial de texturas:** 621.63 MiB si las 11 imágenes estuvieran en RGBA8 sin mips; 828.84 MiB como límite superior con mipmaps. El formato real puede ser comprimido y puede no residir todo simultáneamente.
2. **Targets de cockpit:** tres buffers color RGBA8 de 512² serían aproximadamente 3 MiB; el preview 1024² aproximadamente 4 MiB. El total estimado de color es 7 MiB antes de depth, MSAA, staging y buffers temporales.
3. **Geometría planetaria:** 63.168 triángulos de esferas no solares más 320 del anillo. El cálculo parte de `RadialSegments=96`, `Rings=48` y siete JSON de cuerpos no solares.
4. **Shader de cielo:** 660 líneas y cuatro sitios de bucle en `space_sky.gdshader`. El coste real depende de resolución, ramas, ocupación y backend; el conteo no equivale a milisegundos.
5. **Partículas:** 2.130 es la suma de literales `Amount` detectados en código, no el número concurrente durante el vuelo. Varios efectos son transitorios o se crean sólo en fases concretas.
6. **Allocaciones administradas:** en los seis archivos visibles auditados no aparecen `ToArray`, `ToList`, `OrderBy` o `SelectMany` dentro de `VesselRenderer`, `SunController`, `SystemsController`, `CockpitInstruments` o `StarfieldController`; `SimulationBridge` conserva dos sitios de cada operación para acciones públicas. El conteo estático no mide bytes por frame ni GC.

## 4. Hipótesis priorizadas

### H1 — presión de memoria de texturas grandes

La combinación de cuatro mapas 8K, `size_limit=0` y `vram_texture=false` merece ser la primera hipótesis de memoria. El dato sólido es el límite RGBA8 de 512 MiB sólo para esos cuatro mapas; falta observar la residencia real y si la carga mantiene copias CPU/GPU.

### H2 — coste de targets siempre actualizados

Los tres paneles de cockpit llaman `QueueRedraw()` cada frame y sus `SubViewport` están en `Always`. El preview de construcción también usa `1024×1024` y `Always`, aunque no pertenece al vuelo Flight. Debe medirse por separado para no atribuir el preview a la sesión sandbox.

### H3 — construcción de planetas, no rasterización, domina el arranque

`SimulationBridge.SpawnPlanets()` crea siete `SphereMesh`, materiales y nodos; Saturno crea además un anillo y carga su imagen. El baseline de startup respalda esta hipótesis como coste de creación, pero no permite afirmar todavía que sea un coste sostenido por frame.

### H4 — el cielo es un candidato de coste por píxel

`space_sky.gdshader` integra vista, nubes y scattering con bucles; su coste puede crecer con resolución y cubrir toda la pantalla. La hipótesis requiere una captura renderer-backed con GPU time o al menos frame time correlacionado con resolución.

### H5 — el worker LUT mantiene CPU/memoria después del arranque

La cola asíncrona evita el bloqueo síncrono, pero la ausencia de `worker_complete` en la corrida larga registrada anteriormente y en las corridas actuales cortas deja abierta la posibilidad de arrays CPU grandes y carga sostenida. La solución futura debe medir/cancelar/cachear, no mover ese trabajo al frame principal.

## 5. Gates para la próxima implementación

No se debe cambiar runtime hasta capturar una referencia renderer-backed con el mismo hardware/driver y condiciones controladas.

### Gate A — memoria y texturas

- Medir RSS, memoria de proceso y residencia/uso de recursos con una captura repetida en frío y caliente.
- Separar `earth_day`, `earth_night`, `earth_clouds`, starmap, LUT CPU, LUT textura, cockpit y mallas.
- Toda reducción de resolución/compresión debe demostrar una reducción de memoria observada, no sólo del JPEG fuente.
- No aceptar una variante si produce clipping amplio, pérdida visible del terminador, pérdida de navegación estelar o una regresión de luminancia fuera de la tolerancia visual existente.

### Gate B — frame budget

- Medir p50/p95/p99 de frame en 1920×1080 para: pad, ascenso, órbita, terminador y cockpit.
- Objetivo de referencia: p95 ≤16.7 ms y p99 ≤33.3 ms en la configuración de calidad aprobada; si el entorno no puede sostenerlo, registrar el límite reproducible y no ocultarlo con headless.
- Medir una variante con cockpit cerrado y otra con tres pantallas activas; la diferencia es el coste atribuible a los `SubViewport`.

### Gate C — startup/planetas

- Repetir al menos 5 corridas frías y 5 calientes de `flight_baseline.sh`.
- No aceptar una implementación que empeore `SimulationLoaded` más de 5% respecto a la mediana controlada.
- Para justificar LOD/streaming de planetas, exigir reducción observable del tiempo de `SpawnPlanets` y conservar silueta, escala angular, eclipse y posición física.

### Gate D — LUT

- Registrar `cpuMs`, bytes de arrays CPU, tiempo de creación/upload de textura y momento de `worker_complete`.
- Exigir cancelación al salir de escena y cache por hash de perfil, resolución y orden antes de promover cambios.
- El renderer oficial debe continuar usando orden 4; una prueba experimental no puede modificar la ruta normal.

### Gate E — allocaciones

- Usar profiler de Godot/.NET o un harness instrumentado separado para obtener bytes asignados por frame y GC p95.
- No inferir “cero allocaciones” a partir de que `rg` no encuentre LINQ: deben existir mediciones de asignación durante vuelo y cockpit.

## 6. Comandos reproducibles y validación realizada

Inventario:

```bash
tools/perf/memory_render_audit.sh
file assets/textures/*
find .godot/imported -maxdepth 1 -type f -name '*.ctex' -printf '%s\t%f\n' | sort -nr
rg -n 'SubViewport|RenderTargetUpdateMode|SphereMesh|RadialSegments|Rings|Amount[[:space:]]*=' scripts scenes assets
```

Checks ejecutados:

```bash
bash -n tools/perf/memory_render_audit.sh
git diff --check
```

No se ejecutó un build C# adicional porque este commit sólo añade documentación y un script shell read-only bajo `tools/perf/`; no cambió runtime, escenas, shaders, imports ni proyectos C#.

## 7. Archivos y límites de esta entrega

Archivos añadidos:

- `tools/perf/memory_render_audit.sh`: diagnóstico estático, read-only.
- `docs/audits/PERF_MEMORY_RENDER_AGENT_REPORT.md`: resultados, hipótesis y gates.

No se modificó ningún archivo de `scripts/`, `ExosphereSimulation/`, `scenes/`, `assets/` o configuración del proyecto.

La siguiente fase debe ser una instrumentación renderer-backed controlada. La auditoría actual ya identifica dónde medir, pero no autoriza todavía a reducir texturas, apagar viewports, cambiar LOD planetario ni alterar el LUT.
