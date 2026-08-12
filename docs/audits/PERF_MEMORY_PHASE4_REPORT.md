# Fase 4 — Auditoría de memoria y recursos de render

Fecha: 2026-08-12
Commit auditado: `1d74de0` (`perf: bound runtime LUT work and add renderer metrics`)
Alcance: texturas, configuración de importación, `SubViewport`, materiales, LUT de
atmósfera y nodos que siguen ejecutando trabajo cuando su representación no está en la
cámara.
Regla de esta entrega: únicamente se añade este informe. No se modifican scripts,
escenas, shaders, imports ni otros documentos.

## Conclusión ejecutiva

El riesgo dominante sigue siendo la residencia potencial de texturas grandes:

- Hay **11 imágenes** con **27.52 MiB** en los archivos fuente.
- El límite conservador de decodificación RGBA8 es **621.63 MiB** sin mipmaps y
  **828.84 MiB** con el factor completo de mipmaps `4/3`.
- Los cuatro mapas 8192×4096 (`earth_day`, `earth_night`, `earth_clouds` y
  `starmap_milkyway_8k`) suman **512.00 MiB RGBA8** antes de depth, staging,
  duplicados del driver o buffers temporales.
- Los 11 imports tienen `metadata.vram_texture=false`, `mipmaps/generate=false` y
  `process/size_limit=0` (`assets/textures/*.import:8`, `:26`, `:39`). Esto es una
  configuración observada; no es una lectura de VRAM.
- Los shaders de Earth, cuerpos planetarios, anillo y cielo declaran
  `filter_linear_mipmap` (`assets/shaders/earth_surface.gdshader:19-21`,
  `assets/shaders/planet_body.gdshader:27-30`, `assets/shaders/saturn_ring.gdshader:7-9`,
  `assets/shaders/space_sky.gdshader:6-7`). Existe una incompatibilidad de configuración
  que debe validarse antes de decidir entre generar mipmaps, cambiar el filtro o usar una
  variante por distancia.
- El anillo de Saturno es una duplicación especialmente clara: el runtime lee el PNG con
  `Image.LoadFromFile`, genera mipmaps y crea una `ImageTexture` nueva
  (`scripts/SimulationBridge.cs:595-604`), aunque existe el recurso importado
  `assets/textures/saturn_ring.png.import`.
- En runtime existen tres targets de cockpit de 512×512 y un preview de construcción de
  1024×1024. Cockpit se crea con `Always` pero se desactiva fuera de IVA
  (`scripts/CockpitInstruments.cs:18-40`); el preview permanece en `Always`
  (`scripts/ConstructionController.cs:238-248`).
- `CameraController` oculta el exterior durante el cockpit
  (`scripts/CameraController.cs:369-375`), pero `VesselRenderer._Process` no comprueba
  `Visible` antes de actualizar plumas, flaps, tren y térmica
  (`scripts/VesselRenderer.cs:1174-1243`). Es trabajo CPU fuera de la cámara que merece
  un gate específico antes de implementar pausa.

No se aprueba ninguna reducción de textura, LOD, compresión ni pausa de nodos en esta
fase: los datos de VRAM del driver y el coste GPU siguen sin medirse. Las recomendaciones
de abajo son hipótesis priorizadas y tienen gates de regresión visual explícitos.

## 1. Método reproducible y límites

Auditor estático ejecutado desde la raíz del repositorio:

```bash
bash tools/perf/memory_render_audit.sh > /tmp/exosphere_memory_phase4_audit.txt
```

El auditor declara su alcance read-only y separa estimaciones RGBA8 de la residencia
real (`tools/perf/memory_render_audit.sh:4-15`). Sus cálculos de imagen usan
`width * height * 4` y un límite con mipmaps `4/3`
(`tools/perf/memory_render_audit.sh:60-82`). El resultado de esta ejecución fue:

```text
memory_render_audit version=1
timestamp=2026-08-12T03:35:10Z
mode=read-only-static
image_count=11
source_bytes=28859911 (27.52 MiB)
decoded_rgba8_bytes=651823008 (621.63 MiB)
decoded_rgba8_with_mip_upper_bound_bytes=869097348 (828.84 MiB)
imported_ctex_bytes=64573022 (61.58 MiB)
import_files=16
imported_cache_files=34
imported_cache_bytes=65042404 (62.03 MiB)
```

El caché `.godot/imported` es almacenamiento de importación en disco, no VRAM.
La estimación RGBA8 tampoco demuestra que el driver vaya a usar ese formato: quedan
abiertos compresión, staging, duplicación CPU/GPU, depth, MSAA y residency eviction.

Para el inventario de actualización se usaron además estas búsquedas read-only:

```bash
rg -n 'SubViewport|RenderTargetUpdateMode|QueueRedraw|_Process|Visible|GD.Load<Texture2D>|Image.LoadFromFile|StandardMaterial3D|ShaderMaterial' \
  scripts scenes assets/shaders --glob '*.cs' --glob '*.tscn' --glob '*.gdshader'
rg -n 'mipmaps/generate|process/size_limit|vram_texture|detect_3d/compress_to' \
  assets/textures --glob '*.import'
```

Los números que dicen “medido” en este documento son tamaños de archivo, dimensiones,
conteos de fuentes y aritmética determinista. Los números que dicen “estimado” son
decodificación, buffers hipotéticos o memoria de LUT derivada de las dimensiones del
código.

## 2. Inventario de texturas

La siguiente tabla es la salida de `memory_render_audit.sh`; RGBA8 y RGBA8+mip son
estimaciones, mientras que `imported` es el tamaño del `.ctex` observado en disco.

| Recurso | Uso estático | Dimensiones | Fuente | RGBA8 | RGBA8+mip | `.ctex` |
|---|---|---:|---:|---:|---:|---:|
| `earth_clouds.jpg` | Earth + cielo/cloud coverage | 8192×4096 | 11.08 MiB | 128.00 MiB | 170.67 MiB | 19.32 MiB |
| `earth_day.jpg` | Earth surface + ground patch | 8192×4096 | 4.35 MiB | 128.00 MiB | 170.67 MiB | 13.05 MiB |
| `earth_night.jpg` | Earth city lights | 8192×4096 | 3.00 MiB | 128.00 MiB | 170.67 MiB | 5.88 MiB |
| `starmap_milkyway_8k.jpg` | Sky star field | 8192×4096 | 1.82 MiB | 128.00 MiB | 170.67 MiB | 3.39 MiB |
| `mars.jpg` | Mars body map | 4096×2048 | 1.45 MiB | 32.00 MiB | 42.67 MiB | 7.26 MiB |
| `moon.jpg` | Moon body map | 4096×2048 | 2.99 MiB | 32.00 MiB | 42.67 MiB | 8.38 MiB |
| `jupiter.jpg` | Jupiter body map | 2048×1024 | 487.28 KiB | 8.00 MiB | 10.67 MiB | 1.63 MiB |
| `saturn.jpg` | Saturn body map | 2048×1024 | 195.23 KiB | 8.00 MiB | 10.67 MiB | 568.99 KiB |
| `venus.jpg` | Venus cloud/body map | 2048×1024 | 224.31 KiB | 8.00 MiB | 10.67 MiB | 768.78 KiB |
| `saturn_ring.png` | Saturn annulus | 8192×500 | 63.27 KiB | 15.62 MiB | 20.83 MiB | 23.01 KiB |
| `menu_orbital_dossier.png` | Main menu background | 1672×941 | 1.89 MiB | 6.00 MiB | 8.00 MiB | 1.35 MiB |
| **Total** |  |  | **27.52 MiB** | **621.63 MiB** | **828.84 MiB** | **61.58 MiB** |

### 2.1 Carga y reutilización

- Earth crea un `ShaderMaterial` y asigna las tres imágenes de 8K
  (`scripts/PlanetMaterials.cs:31-47`). `EarthGroundController` vuelve a pedir
  `earth_day.jpg` para el parche bajo, pero usa la misma ruta importada
  (`scripts/EarthGroundController.cs:45-62`); Godot debería reutilizar el recurso
  cargado, aunque la identidad y la residencia deben confirmarse con el profiler.
- Los cuerpos restantes asignan sus mapas mediante `PlanetMaterials.CreatePlanet`
  (`scripts/PlanetMaterials.cs:67-121`, `:140-187`). La presentación inicial actual es
  lazy: `SimulationBridge.SpawnPlanets` crea el cuerpo dominante y difiere los demás
  (`scripts/SimulationBridge.cs:497-524`). Esto limita la geometría/materiales iniciales,
  pero no elimina el presupuesto potencial si se visitan todos los cuerpos durante una
  sesión.
- El cielo obtiene la estrella y la textura de nubes desde los recursos importados
  (`scripts/SkyController.cs:136-165`, `:982-998`). El uso de `GD.Load<Texture2D>` es
  preferible a decodificar de nuevo los JPEG en cada controlador.
- El menú carga su imagen directamente como `TextureRect`
  (`scripts/MainMenu.cs:41-58`). Su tamaño no debe incluirse automáticamente en el
  presupuesto de una sesión Flight si la escena del menú ya fue liberada; debe medirse
  con una captura fría y una caliente.

### 2.2 Import settings observados

Los 11 imports de `assets/textures` comparten:

| Setting | Valor observado | Riesgo/oportunidad |
|---|---|---|
| `metadata.vram_texture` | `false` | No hay evidencia en el import de una variante VRAM comprimida; medir formato/residencia antes de cambiarlo. |
| `compress/mode` | `0` | No se debe inferir el formato GPU sólo desde el JPEG/PNG ni desde el `.ctex`. |
| `mipmaps/generate` | `false` | Choca con los shaders que piden `filter_linear_mipmap`; puede aumentar aliasing y no permite comparar correctamente el coste de mips. |
| `process/size_limit` | `0` | Los mapas 8K no tienen límite de importación; candidatos a variantes 4K/2K por contexto. |
| `detect_3d/compress_to` | `1` | El importador tiene una ruta 3D declarada, pero el resultado real debe inspeccionarse en el profiler/renderer. |

Los archivos fuente de referencia son, por ejemplo, `assets/textures/earth_day.jpg.import:7-40`,
`assets/textures/starmap_milkyway_8k.jpg.import:7-40` y
`assets/textures/saturn_ring.png.import:7-40`; el auditor verificó los 11 con el mismo
conteo (`tools/perf/memory_render_audit.sh:101-107`). También hay 5 imports de fuentes,
por eso el total de `import_files` es 16; no forman parte del presupuesto de texturas.

### 2.3 Prioridad de mip/LOD

1. **Alta — Earth y cielo:** probar una matriz controlada con 8K/4K, con y sin mipmaps,
   porque Earth usa tres muestras de textura y FBM de cinco iteraciones por fragmento
   (`assets/shaders/earth_surface.gdshader:65-105`) y el cielo samplea el mapa estelar y
   nubes en un shader de pantalla completa (`assets/shaders/space_sky.gdshader:339-365`,
   `:421-476`). La prueba debe medir VRAM/RSS y aliasing de terminador, no sólo tamaño de
   JPEG.
2. **Alta — Saturn ring:** no volver a cargar la imagen por `Image.LoadFromFile`.
   Comparar una ruta basada en `GD.Load<Texture2D>` contra la ruta actual, conservando
   mips si el filtro del shader los necesita. La ruta actual puede tener simultáneamente
   la imagen CPU y la texture GPU durante la subida (`scripts/SimulationBridge.cs:601-604`).
3. **Media — Mars/Moon:** 4096×2048 equivale a 32 MiB RGBA8 por mapa; validar 2K en
   órbita y 4K sólo en baja altitud. El shader genérico también ejecuta FBM de hasta 8
   iteraciones y una vecindad de 27 celdas para cráteres
   (`assets/shaders/planet_body.gdshader:59-84`), por lo que bajar textura no mide por sí
   solo el coste total.
4. **Baja — 2K gas giants y menú:** el presupuesto absoluto es menor. El menú puede
   usar una variante UI limitada al tamaño efectivo del panel, pero no debe mezclarse con
   la referencia Flight.

No se recomienda habilitar mipmaps globalmente sin una comparación: añade hasta 33% de
   memoria en el límite RGBA8 y puede elevar el tamaño de importación, aunque normalmente
   mejora el muestreo minificado y puede reducir aliasing/overdraw efectivo.

## 3. `SubViewport` y buffers de render

### 3.1 Inventario

| Owner | Cantidad | Resolución interna | Update mode | Estado | Evidencia |
|---|---:|---:|---|---|---|
| Cockpit screens | 3 | 512×512 | `Always` sólo en IVA; `Disabled` fuera | Texturas y targets siguen asignados fuera de IVA | `scripts/CockpitInstruments.cs:18-40`, `:43-86` |
| Construction preview | 1 | 1024×1024 | `Always` | Siempre actualiza mientras existe el controller, incluso sin vehículo visible | `scripts/ConstructionController.cs:226-248`, `:821-849` |

El auditor confirmó `subviewport_new_expressions=4`, tres slots de cockpit y la
estimación runtime 3+1 (`tools/perf/memory_render_audit.sh:119-128`). No hay
`SubViewport` declarado en las tres escenas `.tscn`; se crean dinámicamente desde C#.

### 3.2 Estimación de targets

Sólo el color RGBA8, sin depth/MSAA/driver alignment:

| Target | Cálculo | Color mínimo |
|---|---:|---:|
| 3× cockpit | `3 × 512 × 512 × 4` | 3.00 MiB |
| Construction | `1024 × 1024 × 4` | 4.00 MiB |
| **Todos activos** |  | **7.00 MiB** |

Si cada target mantiene un depth de 32 bits, el cálculo color+depth sería
aproximadamente **14.00 MiB**. Es un escenario de planificación, no una lectura: el
formato de depth, MSAA, transient attachments y la política del backend no están
declarados en `project.godot` (`project.godot:23-27`) ni medidos por el auditor.

El cockpit usa la misma `ViewportTexture` como `AlbedoTexture` y `EmissionTexture` del
material de cada pantalla (`scripts/CockpitInstruments.cs:62-75`); eso no duplica el
target por sí mismo. Sí conserva tres materiales y las referencias mientras el cockpit
está oculto.

### 3.3 Oportunidades seguras a investigar

- **Cockpit:** la pausa existente es correcta como baseline. El siguiente experimento debe
  comparar 512²/256² y 60/30 Hz sólo dentro de IVA, con texto, retícula y legibilidad como
  gates. No reducir el target fuera de cámara antes de obtener el beneficio medido, porque
  ya no se actualiza.
- **Construction:** `SubViewportContainer.Stretch=true` estira un target fijo 1024²
  (`scripts/ConstructionController.cs:226-242`). Se puede estudiar resolución basada en
  tamaño real del panel, actualización a demanda cuando cambia el craft/cámara y pausa
  cuando el panel no está visible. El picking manual usa `DirectSpaceState`
  (`scripts/ConstructionController.cs:1185-1201`), así que debe permanecer funcional al
  pausar el render.
- **Preview vacío:** aunque `_previewRenderer.Visible=false` cuando no hay piezas,
  el `SubViewport` y su cámara siguen existiendo (`scripts/ConstructionController.cs:827-843`).
  Es un candidato claro para `Disabled` hasta que haya una pieza o una interacción.

## 4. Materiales, mallas y coste asociado

### 4.1 Materiales

- `PlanetMaterials` crea un `ShaderMaterial` por material corporal y comparte las rutas de
  textura importadas (`scripts/PlanetMaterials.cs:31-47`, `:133-173`). No se debe contar
  cada asignación de parámetro como una copia de imagen; hay que confirmar la identidad del
  `Texture2D` en un capture de Godot.
- `LaunchPadController` construye un conjunto pequeño de materiales compartidos y los
  pasa a múltiples piezas (`scripts/LaunchPadController.cs:91-120`). Además cachea el
  material de lattice (`scripts/LaunchPadController.cs:1415-1428`). Esta es la estrategia
  correcta para el complejo civil; el riesgo principal allí es cantidad de nodos/mallas,
  no una textura grande.
- `VesselRenderer.Mat` crea un `StandardMaterial3D` nuevo por llamada
  (`scripts/VesselRenderer.cs:2455-2465`) y el fallback genérico crea uno por pieza
  (`scripts/VesselRenderer.cs:1700-1717`). El inventario estático contó **157 sitios de
  construcción de malla/nodo** en `VesselRenderer.cs` y **148** en
  `LaunchPadController.cs`; son sitios fuente, no instancias runtime garantizadas. Debe
  medirse el número de materiales únicos de un Starship Flight 7 antes de introducir un
  cache por clave de color/roughness/metallic.
- Los materiales del cockpit son tres instancias pequeñas y sin textura propia, pero
  mantienen las referencias a los targets (`scripts/CockpitInstruments.cs:64-75`). Los
  efectos de partículas crean materiales y texturas procedurales en `_Ready`; su consumo
  es secundario frente a los mapas 8K, aunque sus partículas pueden impactar CPU/GPU.

### 4.2 Anillo de Saturno: duplicación verificable

La ruta actual:

```text
saturn_ring.png (fuente/import)
  -> Image.LoadFromFile (CPU)
  -> GenerateMipmaps()
  -> ImageTexture.CreateFromImage (GPU/renderer resource)
```

Está en `scripts/SimulationBridge.cs:595-604`. La tabla estática estima 15.62 MiB
RGBA8 base y 20.83 MiB con mips para 8192×500, aunque el PNG fuente pesa sólo 63.27 KiB.
El shader pide `filter_linear_mipmap` y samplea una vez
(`assets/shaders/saturn_ring.gdshader:7-22`). Este caso debe ser el primer experimento
de eliminación de staging/duplicación, con una captura de Saturno que compruebe anillo,
alpha, filtrado y sombra.

## 5. Nodos que actualizan fuera de cámara

La visibilidad de un `Node3D` no equivale a pausar su `_Process`; este inventario separa
render frustum-culling de trabajo de CPU:

| Nodo | Trabajo fuera de cámara observado | Severidad | Evidencia |
|---|---|---|---|
| `VesselRenderer` exterior | Actualiza plumas a 30 Hz, flaps/tren/paracaídas a 20 Hz y térmica a 15 Hz sin `if (!Visible)` | Alta en IVA | `scripts/VesselRenderer.cs:38-40`, `:1174-1243`; exterior ocultado en `scripts/CameraController.cs:369-375` |
| `LaunchPadController` | Recorre luces nocturnas y anima brazos de catch cada frame aunque el pad esté lejos de cámara | Media | `scripts/LaunchPadController.cs:46-89` |
| `StarfieldController` | Reposiciona star mesh, partículas de streak y consulta simulación cada frame | Media; es parte del cielo activo | `scripts/StarfieldController.cs:64-85` |
| `SkyController` | Poll del worker cada frame, uniformes/solar geometry a 12 Hz y `Sky.ProcessMode.Incremental` | Media; es el environment de cámara | `scripts/SkyController.cs:162-221`, `:224-267` |
| `SunController` | Muestrea visibilidad y alimenta materiales/luz a 20 Hz | Baja/funcional | `scripts/SunController.cs:33-104` |
| `SystemsController` | Tick de life support, power, térmica y comms cada frame | No pausar por cámara: gameplay | `scripts/SystemsController.cs:51-107` |
| `EarthGroundController` | `_Process` cada frame aunque `Visible=false`; tiene early-outs y sólo renderiza bajo Earth | Baja | `scripts/EarthGroundController.cs:83-108` |
| `MarsTerrainController` | `_Process` cada frame aunque `Visible=false`; tiene early-outs por cuerpo/altitud | Baja | `scripts/MarsTerrainController.cs:31-61` |
| `CockpitInstruments` | El controller consulta estado cada frame, pero sus targets están `Disabled` fuera de IVA | Mitigado | `scripts/CockpitInstruments.cs:43-55`, `:81-87` |
| `ConstructionController` preview | Target 1024² `Always`; el renderer puede estar invisible, pero el viewport no se pausa | Alta en VAB | `scripts/ConstructionController.cs:238-248`, `:827-849` |
| `MapViewController` | Sólo redibuja si `Visible` | Correcto | `scripts/MapViewController.cs:277-281` |
| `SystemsHUD` | Sólo hace `QueueRedraw` si el panel está visible y aplica gates de cockpit/mapa | Correcto | `scripts/SystemsHUD.cs:29-35` |

La distinción crítica es `VesselRenderer`: ocultar el exterior en cockpit evita
rasterización, pero no sus consultas a física ni sus escrituras de materiales. La pausa
futura debe estar condicionada a `Visible`, modo cockpit y existencia de efectos activos;
no debe pausar el `Vessel` ni el `SystemsController`, porque eso cambiaría gameplay.

## 6. LUT atmosférico y memoria CPU/GPU estimada

El perfil runtime actual declara dimensiones reducidas y conserva orden 4
(`scripts/SkyController.cs:52-72`). La fórmula de bytes del propio código es explícita:
vectores CPU de 3 canales × `double` (`scripts/SkyController.cs:728-743`) y texturas
RGBA float (`scripts/SkyController.cs:895-966`). Con el perfil v21:

| Recurso | Elementos | CPU estimada | Texture RGBA32F estimada |
|---|---:|---:|---:|
| Transmittance | 64×96×3 doubles | 147,456 B | 64×96×16 = 98,304 B |
| Global seed order 4 | 32×24×3 doubles | 18,432 B | incluido en el worker, no subida final separada |
| Angular atlas | 16×8×8×8×3 doubles | 196,608 B | 16×512×16 = 131,072 B |
| **Retained CPU trans+angular** |  | **344,064 B** |  |
| **Worker peak trans+global+angular** |  | **362,496 B** |  |
| **Upload trans+angular** |  |  | **229,376 B** |

La densidad es una textura 256×1 RGBA32F por cuerpo
(`ExosphereSimulation/AtmosphereDensityLut.cs:56-68`, `scripts/SkyController.cs:922-940`),
aproximadamente 4,096 B por entrada. El cache CPU está limitado a tres resultados y
evicta las texturas asociadas (`scripts/SkyController.cs:590-611`): límite aritmético de
aproximadamente 1.03 MiB de CPU retenida y 0.66 MiB de uploads trans+angular para tres
entradas, sin contar objetos C#, `Image`, alignment o recursos del driver. Es pequeño
frente a los mapas 8K, pero el staging del worker sí explica pausas/CPU históricas y debe
seguir en la instrumentación.

## 7. Estimación y evidencia de RSS

No existe un contador estático de RSS o VRAM. Las referencias dinámicas disponibles son:

- Baseline headless de 300 iteraciones: **747,148–747,336 KiB** de RSS máximo
  (`docs/audits/PERF_MEMORY_RENDER_AGENT_REPORT.md:76-108`).
- Renderer-backed v21 en llvmpipe: **1,246,864 KiB**, con 50 muestras de callback y
  `wall_seconds=60.55` (`docs/audits/PERF_RENDERER_PHASE3_REPORT.md:60-87`).

No son comparables como una regresión directa: el primero es headless y el segundo incluye
Xvfb, framebuffer y renderer CPU. Tampoco permiten separar textura, Mono heap, staging,
meshes, targets ni LUT. La banda de trabajo que debe repetirse en hardware/driver fijo es
por tanto:

```text
RSS_headless_observado       747,148..747,336 KiB  (referencia anterior)
RSS_renderer_llvmpipe        1,246,864 KiB         (referencia anterior)
VRAM_driver                  NOT_MEASURED
texture_residency            NOT_MEASURED
depth/MSAA/transient         NOT_MEASURED
```

La hipótesis de memoria de esta fase no debe expresarse como “el juego usa 621.63 MiB
de VRAM”: 621.63 MiB es sólo la suma RGBA8 de archivos decodificados potenciales.

## 8. Ranking de oportunidades sin implementar

| Prioridad | Oportunidad | Beneficio esperado | Riesgo |
|---|---|---|---|
| P0 | Eliminar `Image.LoadFromFile`/`GenerateMipmaps` manual de Saturn ring y medir recurso único | Evitar staging/duplicación de hasta ~20.83 MiB para ese mapa | Aliasing/alpha del anillo; requiere captura Saturno |
| P0 | Matriz Earth/star 8K vs 4K/2K con import mipmap y formato real | Reducir residencia potencial y aliasing | Terminador, ciudades, nubes y estrellas pierden detalle |
| P1 | Pausar preview VAB cuando vacío/oculto; actualizar a demanda | Eliminar target 1024² y trabajo de preview fuera de interacción | Romper picking, auto-frame o respuesta al cambiar piezas |
| P1 | Gatear `VesselRenderer._Process` en exterior oculto, conservando efectos activos | Reducir consultas y escrituras en IVA | Flaps/tren/plasma pueden quedar obsoletos al volver a exterior |
| P1 | Target cockpit 256² o 30 Hz sólo como variante IVA | Menor fill/target bandwidth | Texto ilegible; sólo aceptar con prueba visual |
| P2 | Cache de materiales procedurales por clave | Menos objetos/materiales y posiblemente draw state | Variantes cambian emisividad/char/selección; requiere identidad de material |
| P2 | LOD de esferas planetarias según tamaño en pantalla | Menos vértices fuera de foco | Silueta, eclipses y transiciones deben conservarse |

## 9. Gates de regresión

Ninguna oportunidad anterior debe fusionarse sin estos gates:

### Gate A — importación y memoria

1. Ejecutar `memory_render_audit.sh` antes/después y guardar el inventario de dimensiones,
   `.ctex`, settings y estimaciones.
2. Medir proceso con `/usr/bin/time -v` en cinco corridas frías y cinco calientes de
   `Flight` y VAB; registrar RSS máximo, tiempo de carga, worker LUT y momento de descarga.
3. En hardware GPU real capturar memoria de proceso y VRAM/residency por recurso. Si el
   backend no expone VRAM, mantener `NOT_MEASURED`; no sustituirla por tamaño `.ctex`.
4. Toda variante debe demostrar una reducción observada o quedar como cambio de calidad,
   no como optimización de memoria.

### Gate B — renderer y buffers

1. Repetir `pad`, `cockpit`, `ascent` y VAB con resolución 1920×1080 y renderer fijo.
2. Separar `cockpit off`, `cockpit on`, `preview vacío` y `preview con Starship`.
3. Registrar `frame_time_p50/p95/p99`, RSS, capturas válidas y, en hardware capaz,
   timestamp GPU. El benchmark ya exige declarar GPU/VRAM como `NOT_MEASURED` cuando no
   existe fuente explícita (`tools/perf/renderer_benchmark.sh:47-58`, `:113-116`).
4. Objetivo de referencia: p95 ≤16.7 ms y p99 ≤33.3 ms en hardware aprobado. El
   llvmpipe actual no puede aprobar ese objetivo por sí solo.

### Gate C — imagen y materiales

Para cada cambio de textura, mip, LOD o material, comparar PNG/frames alineados y exigir:

- `capture_valid=true`, sin NaN, radiancia negativa ni clipping amplio;
- diferencia media de luminancia ≤2% en pad y órbita, salvo tolerancia documentada de la
  variante de calidad;
- terminador Earth y eclipse sin pérdida de separación día/noche;
- nubes sin aliasing de bandas, estrellas visibles en noche y Saturn ring sin halos/alpha
  incorrecto;
- Starship cockpit legible y sin pantalla congelada al entrar/salir de IVA;
- no más de ±5% de `SimulationLoaded` frente a la mediana controlada.

### Gate D — pausa fuera de cámara

1. Instrumentar el número de `_Process`/actualizaciones por modo, no inferirlo desde
   `Visible`.
2. Verificar que cambiar exterior → cockpit → exterior actualiza de inmediato plumas,
   térmica, tren, flaps y material de reentrada.
3. Verificar que la pausa visual no pausa ticks de física, sistemas, comunicaciones,
   guidance ni guardado.
4. En VAB, conservar picking, auto-frame y rebuild después de cada mutación del craft.

## 10. Comprobaciones ejecutadas

En esta fase sólo se ejecutaron comprobaciones read-only o de contrato; no se hizo build
ni se lanzó un juego para evitar confundir el baseline dinámico con una auditoría estática:

```bash
bash -n tools/perf/memory_render_audit.sh
bash tools/perf/memory_render_audit.sh > /tmp/exosphere_memory_phase4_audit.txt
bash tools/tests/cockpit_subviewport_contract_test.sh
bash tools/tests/visual_playtest_contract_test.sh
bash tools/perf/renderer_benchmark_contract_test.sh
git diff --check
```

Resultado esperado y obtenido:

```text
bash -n memory_render_audit.sh                         PASS
memory_render_audit version=1                         PASS
cockpit_subviewport_contract_test.sh                  PASS
visual_playtest_contract_test.sh                      PASS
renderer_benchmark_contract_test.sh                  PASS
git diff --check                                       PASS
```

Estos contratos verifican sintaxis, invariantes de harness y la pausa declarada del
cockpit; no sustituyen el profiler GPU ni prueban residencia de VRAM.

## Decisión de fase

**No cambiar runtime en Fase 4.** El inventario es suficientemente específico para iniciar
experimentos aislados en una fase posterior, pero todavía faltan VRAM/residency, depth y
timestamp GPU. El primer cambio recomendado es el anillo de Saturno por la duplicación
documentada; el segundo es una matriz controlada de importación/mips de Earth y cielo; el
tercero es pausar el preview VAB y gatear el trabajo visual del `VesselRenderer`. Cada uno
debe pasar los gates A–D y conservar el renderer oficial RGB/order 4.
