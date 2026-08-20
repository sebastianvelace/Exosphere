# Auditoría visual por subsistemas — fases 79–89

**Fecha:** 2026-08-19  ·  **Estado:** **VALIDACIÓN PARCIAL — aceptación visual post-cambio no cerrada**. Cockpit, EDL/catch y legibilidad del casco tienen evidencia histórica; el footprint esférico reduce pero no elimina el banding de baja altitud (P1 abierto). Earth ground, Marte/Venus y el nuevo framing EDL requieren PNG post-cambio.

## Alcance y método

Se ejecutó la matriz visual con `tools/visual_playtest.sh`, usando OpenGL3 sobre
llvmpipe. Las capturas son evidencia de framebuffer, no mockups. Los artefactos
reproducibles quedaron en `/tmp/exo_visual_audit_smoke/`,
`/tmp/exo_visual_audit_ascent/`, `/tmp/exo_visual_audit_edl/`,
`/tmp/exo_visual_audit_cockpit/`, `/tmp/exo_visual_audit_atmosphere/`,
`/tmp/exo_visual_audit_bodies/`, `/tmp/exo_visual_audit_orbit_phase81d/` y
`/tmp/exo_visual_audit_orbit_direct_phase81h/`.

Las evidencias nuevas de esta iteración están en `/tmp/exo_visual_audit_edl_phase82a/`,
`/tmp/exo_visual_audit_edl_phase85/`, `/tmp/exo_visual_audit_cockpit_phase83/`
y `/tmp/exo_visual_audit_atmosphere_phase84b/`. La revalidación EDL posterior al
arreglo de lectura fría del TPS está en `/tmp/exo_visual_audit_edl_phase86/`. La
comparación A/B parcial de
`RadianceSize=256` está en `/tmp/exo_visual_audit_atmosphere_radiance256/`; se detuvo
después de capturar `10km_day`, por diseño, una vez obtenido el caso decisivo.
La fase de filtrado de nubes quedó en `/tmp/exo_visual_audit_atmosphere_phase86/` y
los diagnósticos aislados en `/tmp/exo_visual_audit_atmosphere_low_baseline/`,
`/tmp/exo_visual_audit_atmosphere_low_noclouds/` y
`/tmp/exo_visual_audit_atmosphere_low_mips_lod5/`. La fase 88 añadió la matriz completa
`/tmp/exo_visual_audit_atmosphere_phase88/` y los diagnósticos
`/tmp/exo_visual_audit_atmosphere_low_sphericalfilter/` y
`/tmp/exo_visual_audit_atmosphere_low_spherical_lod6/`. La validación de cuerpos
actualizada está en `/tmp/exo_visual_audit_bodies_phase88/`. La matriz EDL y HUD de
phase88b está en `/tmp/exo_visual_audit_edl_phase88b/`.

Las corridas terminaron con sus gates: `SMOKE_OK`, `ASCENT_ORBIT_OK`, `CAUGHT`,
`COCKPIT_OK`, `ATMOSPHERE_OK`, `ATMOSPHERE_BODIES_OK` y `ORBIT_DIRECT_OK`. El tiempo de frame de esta
VM no se interpreta como FPS de hardware de usuario: la matriz de Venus llegó a
`cpuMs=73304.1` al construir LUT, evidencia de que ese caso debe permanecer fuera
del loop por frame.

## Hallazgos por subsistema

| Subsistema | Evidencia | Severidad | Diagnóstico |
|---|---|---:|---|
| Pad/UI | `exo_play_pad.png`; media 0.0285, `darkFrac=0.7429` | P1 | El overlay `MISSION CONTROLS` cubre la torre y parte del HUD; la escena queda subexpuesta. El scheduler reporta `pending=0`, por lo que no es deuda física. |
| Liftoff/33 motores | `exo_play_liftoff.png`; `runningEngines=33` | P1 visual | La telemetría confirma los 33 motores y el plume se renderiza, pero el vehículo y la plataforma quedan casi negros. No es una falla del conteo: es contraste/material. |
| Max-Q | `exo_play_maxq.png`; `darkFrac=0.9305` | P1 visual / P2 lógica | El casco desaparece salvo por la pluma. El warning de periapsis baja aparece; requiere una revisión separada de guidance/HUD, no debe maquillarse con iluminación. |
| Hotstage/separación | `exo_play_hotstage.png`, `exo_play_separation.png` | P1 visual | Se ven estrellas y plumas, pero no se distinguen bien los vehículos. Los estados registran 33/6 y la separación física progresa. |
| Órbita/cámara | `phase81d`: `visible=True`, `angularDiameterDeg=155.3146`, `cameraForwardCos=0.70719`; `phase81h` captura limpia | PASS parcial | El diagnóstico separó dos causas: la cámara apuntaba fuera del hemisferio útil y el Earth nocturno quedaba subexpuesto. El encuadre quedó corregido; la captura directa final muestra textura terrestre nocturna sin clipping. |
| EDL/plasma | `phase85`: `entry`, `peak_heating`, `retro_burn`, `flip_complete` | P1 visual mitigado | El casco conserva una silueta gris segmentada y el TPS/halo siguen visibles; todavía no alcanza contraste fotográfico alto durante peak-heating. |
| Torre/palillos | `exo_play_caught.png` | PASS físico, P2 visual | `CAUGHT`, `arms=visible`, `physical=armed`, `contacts=2`. Los palillos están implementados y aparecen en la captura final; la nave aún necesita más lectura tonal. |
| Cockpit | `phase83` y `phase84b`: día/noche a 120 km | PASS visual acotado | Los tres instrumentos siguen legibles; el relleno cool separa suelo, marcos y consola. Día muestra el limbo terrestre; noche mantiene lectura sin inundar el parabrisas. |
| Earth/atmósfera | 20 capturas en `phase88` | P1 abierto / mitigación parcial | `10km_day` con footprint esférico, weather LOD 5 y detail LOD 6 registra `p95=0.56863`, `clippedFrac=0.00937`, `darkFrac=0.13218`; mejora frente a phase86, pero aún quedan franjas horizontales finas. El A/B sin nubes elimina por completo las bandas, así que el origen está en la cáscara de nubes/proyección, no en Rayleigh/Mie. Los eclipses conservan `1.0 → 0.351490 → 0.692239 → 0.0`. |
| Mars | baseline de `/tmp/exo_visual_audit_bodies_phase88/` | P2 / pendiente post-cambio | Día bajo legible y noche corregida (`surfaceClippedFrac=0.00000`); 400 km sigue compositivamente vacío. Es evidencia previa a la calibración de terreno orbital y no cierra la aceptación actual. |
| Venus | baseline de `/tmp/exo_visual_audit_bodies_phase88/` | P2 / pendiente post-cambio | El baseline registra `venus_10km_day surfaceClippedFrac=0.08826`, 400 km `0.01576` y noche `0.00000`; requiere repetir tras `.22` y el nuevo rim limitado. |

## Hechos frente a hipótesis

Hechos observados:

1. Ascenso y EDL terminan correctamente; el conteo de motores y el catch no
   contradicen al renderer.
2. Las imágenes repetidas pierden el casco cuando no hay una luz direccional útil.
3. `steel.gdshader` usa metalness alta y no tenía emisión mínima; la luz solar se
   multiplica por `SolarVisibility`, que llega a cero de noche/eclipse.
4. Los tiles mantienen un relleno frío acotado y suman la emisión naranja sólo durante
   calor de reentrada; la ruta fría ya no desactiva la lectura del TPS.

Hipótesis aún no cerradas:

1. El casco del vehículo aún puede tener una ruta de material/oclusiones distinta
   durante max-q, entrada y plasma; requiere bounds, normales y materiales activos.
2. El warning de periapsis puede ser guidance válido durante ascenso o un umbral de
   HUD demasiado agresivo; la foto no demuestra un bug de física.
3. El clipping de Mars/Venus combinaba exposición adaptativa, un piso nocturno fijo
   demasiado alto y rutas de material separadas; la evidencia de fase 80 confirma la
   causa y deja el problema reducido a composición orbital.

4. La captura orbital inicial sin planeta quedó resuelta en fase 81: la telemetría
   probó `cameraForwardCos=-0.31353` en la línea base; tras el encuadre automático
   pasó a `0.70719`. La captura directa final también confirmó que la superficie no
   desaparece al elevar el piso nocturno del shader Earth.

## Corrección aplicada

Se añadió un relleno visual acotado y no físico:

- `steel.gdshader` conserva una emisión base de `0.10 * base_tint` para mantener
  silueta en noche, eclipse y sombra de reentrada.
- La emisión térmica se suma, en lugar de reemplazarla, así que el plasma no pierde
  intensidad.
- Los tiles TPS conservan un relleno oscuro explícito (`0.050, 0.050, 0.060`) aun
  con `glow=0`; el calor se superpone encima.
- Un contrato de CI impide que futuras modificaciones vuelvan a desactivar este
  camino de legibilidad.

Esto no altera radiancia del oráculo, física, `SolarVisibility`, conteo de motores ni
la lógica de captura por palillos. Es una ayuda de presentación del vehículo.

## Resultado post-cambio

Se repitieron las capturas después de activar el relleno. El primer valor `0.018`
no fue suficiente para la silueta; la calibración final quedó en `0.10` para acero y
`0.050, 0.050, 0.060` para TPS. No blanquea el pad ni cambia los gates físicos,
aunque no resuelve por sí sola la lectura del vehículo:

| Corrida | Resultado | Evidencia post-cambio |
|---|---|---|
| Smoke | `SMOKE_OK` | `/tmp/exo_visual_audit_smoke_fill/`; media `0.02847`, `clippedFrac=0.00062`, prácticamente igual al baseline `0.02851/0.00062`. |
| Ascenso Flight 7 | `ASCENT_ORBIT_OK` | `/tmp/exo_visual_audit_ascent_fill/`; `33→6` motores, órbita `166×148 km`, `e=0.001`, sin estado no finito. |
| EDL calibración inicial | `CAUGHT` | `/tmp/exo_visual_audit_edl_fill_v2/`; console `arms=visible`, `physical=armed`, `contacts=2`. `peak_heating darkFrac=0.91858`, `caught clippedFrac=0.00346`. |
| EDL catch físico | `CAUGHT` | `/tmp/exo_visual_audit_edl_phase85/`; el control supera el plateau de ~52 m y termina con `contacts=2`, `relativeSpeed=0.031`, `angularSpeed=0`. |
| Cockpit | `COCKPIT_OK` | `/tmp/exo_visual_audit_cockpit_phase83/` y `/tmp/exo_visual_audit_atmosphere_phase84b/`; paneles legibles de día y noche con relleno interior acotado. |
| Atmósfera baja | `ATMOSPHERE_OK` | `/tmp/exo_visual_audit_atmosphere_phase84b/`; el prefilter reduce la oscuridad de `0.13218` a `0.04469` y el clipping de `0.01097` a `0.01041`, pero persisten bandas. |

La comparación visual confirma una mejora pequeña en bordes y piezas térmicas, pero
el casco permanece casi negro en `maxq`, `entry` y `peak_heating`. Por tanto, el
relleno es una mitigación segura, no el cierre del P1. La causa restante puede ser
encuadre/occlusión o una ruta de material distinta para partes del vehículo; el
próximo diagnóstico debe capturar bounds, normales y materiales activos por malla.

## Fase 80 — cuerpos planetarios y exposición

La segunda matriz aisló los cuerpos no terrestres con seis capturas por corrida y
telemetría de exposición/LUT. La causa estaba dividida en tres rutas:

- `planet_body.gdshader` tenía un piso nocturno fijo de `0.05`, incompatible con la
  adaptación de exposición nocturna (`5.872–5.873` en los casos reproducidos).
- `SunController` sólo propagaba `solar_visibility` a Earth y `EarthGround`; los
  materiales lazy de Marte/Venus podían conservar iluminación directa.
- Marte a baja altura no usa el cuerpo escalado: `MarsTerrainController` construye un
  `StandardMaterial3D` independiente sin contrato de noche/eclipse.

La corrección mantiene el renderer RGB y no toca la física:

- El shader genérico usa `solar_visibility`, `day_gain` y `night_floor`; el directo es
  `day * day_gain * solar_visibility` y el término nocturno queda acotado.
- Venus queda con `dayGain=0.28`, `nightFloor=0.004`; Marte con `dayGain=0.92`,
  `nightFloor=0.006`. Son calibraciones de presentación, no nuevos datos atmosféricos.
- Se añadió `mars_terrain.gdshader` con el mismo contrato de visibilidad para el parche
  de baja altura.
- La caché de `SunController` se invalida sólo cuando cambia el número de hijos de
  `Planets`, cubriendo la creación lazy sin recorrer el árbol en cada frame.
- `tools/tests/planet_body_lighting_contract_test.sh` queda integrado en CI.

La corrida final fue:

```text
OUT_DIR=/tmp/exo_visual_audit_bodies_phase80c
MODE=atmosphere_bodies
RESULT=ATMOSPHERE_BODIES_OK
```

Resultados finales de framebuffer (la columna `surfaceClippedFrac` mide sólo la
región de superficie):

| Caso | Antes | Fase 80 | Resultado |
|---|---:|---:|---|
| Mars 10 km día | `0.00000` | `0.00011` | estable; sin blanco amplio |
| Mars 10 km noche | `0.99688` | `0.00000` | corregido |
| Venus 10 km día | `1.00000` | `0.08826` | mejora fuerte; clipping residual acotado |
| Venus 400 km día | `0.11578` | `0.01576` | corregido |
| Venus 10 km noche | `1.00000` | `0.00000` | corregido |

El coste de LUT continúa fuera del frame loop: Venus registró aproximadamente
`cpuMs=53819.5` en llvmpipe durante la corrida final, mientras el juego siguió
reportando `ATMOSPHERE_BODIES_OK`. Esta VM no se usa para afirmar FPS de hardware.

## Fase 81 — encuadre orbital y legibilidad nocturna de Earth

La instrumentación de `FloatingOrigin` añadió distancia real de cámara, diámetro
angular, coseno de orientación y posición del backdrop para separar composición de
material. La línea base registró Earth presente pero fuera del eje (`visible=True`,
`angularDiameterDeg=155.3172`, `cameraForwardCos=-0.31353`); una corrección parcial a
12° tampoco lo llevó al campo útil (`0.20758`). El encuadre final usa un mínimo de
45° y máximo de 65° sólo al entrar en la presentación orbital, dejando el control
manual libre después.

La corrida física `/tmp/exo_visual_audit_orbit_phase81d/` alcanzó
`ASCENT_ORBIT_OK` con `198×143 km`, `e=0.004`, Earth visible, diámetro angular
`155.3146°` y `cameraForwardCos=0.70719`. Esa captura todavía mostró la esfera casi
negra, por lo que se aisló el shader Earth del problema de cámara.

El shader ahora declara un `night_floor` acotado y configurable. La calibración final
`0.12` mantiene el directo y las ciudades sin cambios, pero evita que el mapa azul
desaparezca bajo la exposición estelar. Se creó `--orbit` como modo de evidencia del
harness: prepara la nave en órbita, aleja la cámara a 400 km y no cambia la ruta normal
del juego. Su captura final `/tmp/exo_visual_audit_orbit_direct_phase81h/` registró:

| Métrica | Resultado |
|---|---:|
| Earth visible / diámetro / orientación | `True / 118.7023° / 0.77872` |
| Luminancia media / píxeles oscuros | `0.02480 / 0.52008` |
| Clipping total / superficie blanca | `0.00060 / 0.00000` |
| `neonGreenFrac` | `0.000000` |

La imagen muestra textura terrestre nocturna legible y un borde azul estrecho, sin
convertir la noche en una superficie blanca. El segundo ascenso de calibración
`phase81e` alcanzó la inserción pero agotó el presupuesto del harness antes del
capture final; queda registrado como timeout de prueba y no como PASS.

## Fases 82–85 — casco, cockpit, banding y catch físico

La fase 82 confirmó que elevar el relleno del casco mejora los bordes y segmentos TPS,
pero no elimina el contraste bajo durante max-Q y peak-heating. La corrida
`/tmp/exo_visual_audit_edl_phase82a/` fue interrumpida deliberadamente en el plateau
de `FINAL_DESCENT` (~52 m), antes de declarar éxito; esto aisló un problema de control,
no una ausencia de palillos.

La fase 85 corrigió ese plateau con una liberación final estrecha: sólo en `Edl.Catch`,
con distancia al contacto `<=1.5 m`, velocidad vertical descendente `<0.65 m/s` y
error horizontal `<=2 m`. Se deselecciona el motor y se entrega la caída final al
modelo de contactos; no hay teletransporte ni bandera de catch artificial. La corrida
terminó `CAUGHT` con dos contactos, velocidad relativa `0.031 m/s` y velocidad angular
`0`. La captura muestra los palillos y el estado `CAUGHT`; el punto de reposo queda
aproximadamente 7.6 m por debajo del datum nominal de 56 m, por lo que la geometría de
penetración/ajuste vertical queda como una revisión P2 posterior.

El cockpit recibió un relleno emissive muy bajo en paredes, marcos y trims, más luces
interiores acotadas (`0.62` y `0.36`). La matriz de día/noche conserva instrumentos
legibles y el limbo terrestre, sin usar esta iluminación para modificar el exterior.

En atmósfera a 10 km se añadió un prefilter dependiente de altura y un muestreo de
detalle coherente. La métrica mejora, pero las franjas horizontales/radiales siguen
visibles. La A/B controlada de `RadianceSize=128` frente a `256` produjo prácticamente
la misma imagen: `clippedFrac=0.01041`, `darkFrac=0.04469` con 128 y `0.04524` con 256,
con el mismo `p95=0.67843` y la misma estructura de bandas. El worker de LUT registró
~13.5 s en ambas corridas de esta VM. Por eso se restauró `128` como configuración
oficial: duplicar el cubemap no compra calidad observable. El siguiente experimento se
centró en el muestreo/proyección de la textura de cobertura.

## Fase 86 — filtrado de la cáscara de nubes a baja altitud

El diagnóstico `--atmosphere-low` fijó el caso determinista `Earth / 10 km / día` para
evitar que una matriz completa ocultara la causa. La ejecución con
`EXO_VISUAL_DISABLE_CLOUDS=1` eliminó las franjas (`clippedFrac=0.00000`, imagen de
gradiente suave), mientras que Rayleigh/Mie permanecieron activos. Esto atribuye el
artefacto a la cáscara de nubes/proyección equirectangular, no a la integración
atmosférica base.

La textura `assets/textures/earth_clouds.jpg` estaba declarada sin mipmaps aunque el
shader solicitaba `textureLod`. Se habilitó su generación y se corrigió el muestreo de
weather/detail a LOD 5 cuando el prefilter dependiente de altura está activo. Con la
importación realmente regenerada, el caso 10 km pasó de `p95=0.67843` y
`clippedFrac=0.01039` a `p95=0.62745` y `clippedFrac=0.01029` en el diagnóstico
aislado; la matriz completa registró `p95=0.63137`, `clippedFrac=0.01030` y
`darkFrac=0.13205`. La imagen muestra menos spokes/radialidad y un horizonte menos
ruidoso, pero conserva bandas horizontales finas: la corrección es una mitigación
parcial, no el cierre del P1.

El `.ctex` pasó de 20,257,252 a 27,657,500 bytes (aprox. +7.4 MB, +36.6%). Es un
coste de memoria de textura aceptable para esta fase y no añade trabajo por frame de
física; se mantiene documentado porque afecta al presupuesto de memoria de render.
El modo `--atmosphere-low` y la variable de proceso `EXO_VISUAL_DISABLE_CLOUDS` son
diagnósticos y no cambian el lanzamiento normal. La matriz completa terminó
`ATMOSPHERE_OK` con 20/20 capturas; los cuatro estados de eclipse mantuvieron
visibilidad `1.0 → 0.351490 → 0.692239 → 0.0`, y cockpit día/noche siguió legible.

Decisión: conservar mipmaps + LOD 5 como mitigación oficial de bajo riesgo, mantener
`RadianceSize=128` y no aumentar muestras ni promover orden 5 por este problema. La
reproyección esférica ya está implementada como mitigación; el banding horizontal
residual demuestra que repetir el filtro sobre la equirectangular no es suficiente para
cerrar el P1. La cobertura debe evolucionar a una representación esférica o a un
integrador que filtre explícitamente por horizonte.

## Fase 87 — lectura fría del TPS durante reentrada

La comparación de `/tmp/exo_visual_audit_edl_phase85/` con
`/tmp/exo_visual_audit_edl_phase86/` encontró una regresión de presentación concreta:
`TileMat()` activaba una emisión base de lectura, pero `CharZone()` la sustituía por
`EmissionEnabled = glow`. En `ENTRY` y `RETRO_BURN`, con `ember` por debajo del umbral,
eso desactivaba por completo el relleno de las losetas y dejaba la cara de la nave
negra contra el espacio. No era una ausencia de motores ni un fallo del modelo
térmico: el HUD seguía reportando el estado físico correcto.

La corrección mantiene siempre la emisión neutra acotada
`(0.050, 0.050, 0.060)` y reserva la emisión naranja para el caso térmico; no toca
temperaturas, daño, flujo, motores ni control EDL. La nueva matriz EDL terminó
`CAUGHT`, `contacts=2`, `relativeSpeed=0.03 m/s` y conservó las cinco capturas. En la
imagen de `RETRO_BURN` el cuerpo completo ya se distingue como silueta gris azulada y
en `CAUGHT` el casco queda legible junto a los palillos; sigue siendo una presentación
oscura en el fondo negro, por lo que el contraste fotográfico máximo queda como P2 de
acabado, no como bloqueo funcional.

El contrato `visual_material_fill_contract_test.sh` ahora rechaza que la ruta fría
vuelva a desactivar el relleno. La corrección es local al renderer y no añade consultas
físicas ni trabajo por frame fuera de la actualización visual ya existente.

## Fase 88 — footprint esférico y filtrado de nubes

El diagnóstico de phase86 confirmó que el artefacto provenía de la cáscara de nubes.
Phase88 conservó la textura con mipmaps y añadió un filtro sobre la esfera de dirección:

- `SkyController.cs` combina `solarPrefilter` y `altitudePrefilter` con `Mathf.Max`.
- El prefiltro de altura se activa gradualmente entre 6 y 45 km.
- `space_sky.gdshader` usa `cloud_weather_spherical_sample(vec3 direction)`.
- El footprint usa tangente y bitangente, con escala `0.035`, además de la muestra central.
- Weather usa `textureLod(..., 5.0)` y detail usa `textureLod(..., 6.0)`.
- `assets/textures/earth_clouds.jpg.import` tiene `mipmaps/generate=true`; el `.ctex`
  regenerado mide `27,657,500` bytes.

La matriz Earth de phase88 terminó `ATMOSPHERE_OK` con 20/20 capturas. En `10km_day`,
el p95 bajó de `0.63137` a `0.56863` (aprox. 9.9%) y el clipping de `0.01030` a
`0.00937` (aprox. 9.0%). El cambio reduce spokes/radialidad, pero no elimina las
franjas horizontales; por eso el P1 continúa abierto. LOD6 no demostró una diferencia
separable frente a LOD5 en esta VM.

El agente de rendimiento no recomienda promover esta variante como coste oficial sin
una medición GPU real: la matriz usa llvmpipe y reporta `PERF_FRAME` muy por encima de
50 ms, aunque el scheduler físico permanece acotado. Se conservan `RadianceSize=128`,
calidad interactiva `0.60` y procesamiento incremental. Esta evidencia valida sólo la
mitigación del banding en el estado anterior; no sustituye la matriz Earth actual ni
la matriz Mars/Venus de phase80c y la revalidación de cuerpos phase88.

La separación de órdenes queda explícita: el runtime sigue en `lutOrder=4`, versión
`rgb-ms-order4-interactive-v21`; la referencia espectral experimental usa
`spectralOrder=5`, `provenance=reconstructed`. Ese orden 5 no está conectado al
renderer oficial.

## Fase 89 — layout de reentrada, alertas y lectura térmica

La matriz EDL de phase88b validó el cambio de presentación con cinco hitos:
`entry`, `peak_heating`, `retro_burn`, `flip_complete` y `caught`, en
`/tmp/exo_visual_audit_edl_phase88b/`. El gate terminó `CAUGHT`; la consola reportó
`contacts=2`, `relativeSpeed=0.03 m/s` y `angularSpeed=0`.

Se corrigió el overlay de EDL con un `EdlOverlayLayout` compartido:

- el rail de altitud usa 0–70 km en `ENTRY`, `PEAK_HEATING` y `AERO_DESCENT`;
- cambia a 0–5 km desde `RETRO_BURN`;
- `TELEMETRY`, `HIGH G` y `THERMAL` tienen reservas separadas, con un gap de 50 px
  en la referencia 1280×720;
- `HIGH G` queda dentro de telemetría y ya no invade la tarjeta térmica;
- la escala responde proporcionalmente entre 1280×720 y 1920×1080.

El HUD general separa `PhaseTitle` de `AlertLane`. Las alertas conservan severidad,
valor, límite, acción y ACK, pero se presentan en dos filas de una línea con elipsis.
En la captura de peak-heating la fase, la alerta crítica, el rail de 70 km y los
valores térmicos son simultáneamente legibles. El contrato visual evita volver a la
concatenación antigua de alerta y título.

También se calibró la emisión térmica del acero de `0.16` a `0.28` como ajuste acotado;
el relleno frío del TPS permanece separado y la emisión sigue siendo aditiva. Las
métricas de framebuffer de phase88b fueron: `peak_heating clippedFrac=0.00226`,
`retro_burn clippedFrac=0.00161`, `caught clippedFrac=0.00330`. La nave es más legible
que en la línea anterior, pero el contraste fotográfico en peak-heating sigue por debajo
del objetivo; no se declara cerrado el P1 de casco.

Los contratos nuevos son `edl_overlay_layout_contract_test.sh` y
`hud_alert_layout_contract_test.sh`, ambos integrados en `tools/ci_check.sh`. Esta fase
no modifica física, guidance, motores, contacto ni la visibilidad de los palillos.

La transición de cámara de phase89 se validó en
`/tmp/exo_visual_audit_camera_phase89b/`. Las capturas `pad`, `liftoff`, `maxq`,
`hotstage` y `separation` conservaron la nave centrada y legible; en `maxq` se
observaron `33/33` motores y la separación se ejecutó con estado físico finito. La
corrida alcanzó inserción (`t=373.7 s`, `alt=153.3 km`, `finite=True`, sin destrucción),
pero el harness llvmpipe fue detenido antes de producir el PNG de órbita. Por rigor,
esto queda como validación parcial de presentación y no como un nuevo
`ASCENT_ORBIT_OK`; el gate orbital reproducible sigue siendo `phase81d`.

## Fase 90 — menú responsive y VAB como estudio visual

El loop de captura se amplió a las escenas de entrada y construcción, que no estaban
cubiertas por `visual_playtest.sh`. La línea base del menú mostró dos fallos de
presentación reproducibles:

- en 1280×720 `PARTIDAS GUARDADAS` quedaba bajo el footer y `AJUSTES` fuera de la
  ventana (`/tmp/exo_visual_cycle90_menu_1280b.png`);
- el dossier mezclaba español e inglés en la misma tarjeta
  (`/tmp/exo_visual_cycle90_menu.png`).

La corrección usa una rama compacta basada en ancho y alto efectivos: oculta el dossier
secundario, reduce únicamente separación/tamaño de navegación hasta 36 px y mantiene
los ocho destinos dentro de la ventana. La captura posterior
`/tmp/exo_visual_cycle90_menu_1280_after.png` muestra todas las opciones y el footer
sin solapamiento. En 1920×1080 se conserva el layout amplio y la tarjeta queda
completamente localizada (`/tmp/exo_visual_cycle90_menu_1920_after.png`). Se añadieron
claves para clasificación, dossier, footer y estado de física; no se cambió la lógica
de navegación.

El VAB se capturó con una plantilla Starship/Super Heavy en un harness temporal:

- la línea base 1920×1080 (`/tmp/exo_visual_cycle90_construction.png`) dejó el acero
  casi negro por tener una única luz direccional;
- el VAB ahora usa una luz key y un fill frío sin sombras, limitado al SubViewport de
  construcción. No entra en el WorldEnvironment ni en la iluminación del vuelo;
- la captura posterior 1920×1080 (`/tmp/exo_visual_cycle90_construction_after.png`)
  conserva la silueta y hace visibles soldaduras, flaps y detalles del booster;
- en 1280×720 (`/tmp/exo_visual_cycle90_construction_1280_after.png`) se ocultan
  herramientas secundarias (históricos, edición avanzada, archivos y crafts
  guardados), se mantienen validación y `LAUNCH`, y el catálogo sigue siendo usable.

La auditoría detectó además que el picking usaba metros sin convertir a las unidades de
render y que el renderer procedural de Starship no sigue el árbol arbitrario del VAB.
Se corrigió la capa con conversión explícita `1 u = 2.8 m`, datum inferior para el
renderer genérico y anclajes visuales para booster, hot-stage, motores y sección de
Starship. La captura con la pieza raíz seleccionada
(`/tmp/exo_visual_cycle90_vab_selection_after.png`) muestra el highlight sobre la
sección superior correspondiente, en vez de sobre el booster. El contrato
`vab_picking_alignment_contract_test.sh` protege escala, datum, marcadores y anclajes.
La captura de click/raycast real `/tmp/exo_visual_cycle91_vab_click_after_world.png`
terminó con `exit=0`, `selected_index=0` y estado `Part selected.`; la inicialización
explícita de `SubViewport.World3D` eliminó el `NullReferenceException` que la prueba
anterior había descubierto. La cobertura de múltiples piezas y la navegación de todas
las herramientas siguen siendo pendientes separadas.

Contratos añadidos a CI en esta fase:

- `main_menu_responsive_contract_test.sh`;
- `vab_preview_lighting_contract_test.sh`;
- `vab_picking_alignment_contract_test.sh`.

No se considera cerrada la auditoría VAB completa: siguen pendientes el click/raycast
sobre varias piezas, el scroll o navegación equivalente para herramientas secundarias
en resoluciones muy bajas y la transición visual al lanzar.

## Fase 91 — corrección terrestre y control de regresiones por captura

La primera corrida aislada después del cambio de Earth está en
`/tmp/exo_visual_cycle91_atmosphere/`. De día, la superficie local mantiene lectura
de terreno y el coste visual no cambia. En amanecer y atardecer, sin embargo, el nuevo
`night_floor=0.032` produjo prácticamente las mismas métricas que phase88:

| Caso | `darkFrac` phase88 | `darkFrac` phase91 con `.032` | Diagnóstico |
|---|---:|---:|---|
| `ground_sunrise` | `0.84573` | `0.84577` | no cerrado |
| `ground_sunset` | `0.79283` | `0.79250` | no cerrado |

Las fotografías `/tmp/exo_visual_cycle91_atmosphere/exo_play_ground_sunrise.png` y
`exo_play_ground_sunset.png` confirman que el hemisferio inferior todavía se aplasta a
negro; el problema no era sólo el término solar directo. Se ajustó el piso indirecto a
`.08`, manteniéndolo acotado, azul-biased y separado de `solar_visibility`. El segundo
diagnóstico confirmó la causa de presentación: el floor en `ALBEDO` dependía todavía
de la luz de escena y se borraba cuando `solar_visibility=0`. Ahora el directo permanece
en `ALBEDO` y el floor indirecto se emite por `EMISSION`, con máximo `.08 × 1.45 × tint`.
La corrección estática está protegida por el contrato Earth; la captura posterior sigue
pendiente.

La corrida `cycle91b` no pudo iniciar Godot: el entorno de captura quedó con
`/tmp/.X11-unix` propiedad de `nobody`, sin servidor X vivo, y Xvfb rechazó el socket.
Por eso la corrección `ALBEDO→EMISSION` queda validada estáticamente pero no como PASS
visual; no se inventa una comparación ni se usa el fallo del harness como evidencia del
juego.

La corrección de Venus reduce el gain diurno de `.28` a `.22` y añade una respuesta de
rim solar más baja para evitar un borde blanco dominante. Su aceptación queda ligada a
la matriz `atmosphere_bodies`, con comparación explícita de `surfaceClippedFrac` frente
a `phase88` (`.08826` en Venus a 10 km). No se modifica la física de Marte o Venus.

El contrato `earth_ground_lighting_contract_test.sh` exige piso acotado, transición de
terminador y término directo gated por visibilidad. El contrato
`planet_body_lighting_contract_test.sh` protege la calibración de Venus y la respuesta
del rim. El banding horizontal de la cáscara de nubes continúa separado de esta fase:
no se declarará cerrado mientras la captura A/B no demuestre una reducción estructural,
no sólo un cambio de luminancia agregada.

## Fase 93 — EDL y cámara de reentrada

La auditoría de reentrada conserva el gate físico de phase88b: `CAUGHT`, dos contactos
y palillos visibles. La nueva corrección es sólo de presentación: el HUD EDL queda
limitado a la escala de referencia `1.0` en 1920×1080 para no cubrir el casco, y la
cámara exterior durante EDL limita la distancia de presentación a `28` unidades con
el suavizado ya existente. No se cambió guidance, contacto ni la liberación final.

La evidencia anterior mostraba una nave demasiado pequeña/oscura en peak-heating y un
catch con el pad muy dominante:

| Captura anterior | `clippedFrac` | `darkFrac` | Estado físico |
|---|---:|---:|---|
| `peak_heating` | `.00226` | `.76894` | EDL activo |
| `retro_burn` | `.00161` | `.85361` | burn activo |
| `caught` | `.00330` | `.62727` | `CAUGHT`, `contacts=2` |

La matriz `phase93` no pudo producir PNG por el mismo bloqueo Xvfb, por lo que la mejora
de encuadre no se promociona visualmente todavía. El contrato
`edl_visual_presentation_contract_test.sh` protege el límite de escala, la distancia
de cámara y la separación del HUD; la repetición posterior debe comparar cobertura del
casco, brazos/palillos y clipping contra esas tres capturas.

## Fase 94 — entrada, cockpit y HUD reversible

La auditoría de entrada conserva evidencia fotográfica suficiente para cerrar los
defectos ya corregidos: el menú 1280×720 post-fix muestra las ocho opciones sin clipping,
el VAB tiene preview iluminado y `LAUNCH` accesible, y el picking de la sección superior
queda alineado. No se añadió código nuevo en esta ronda porque no existe una regresión
post-fix demostrable; siguen pendientes fotos del modal de campaña, VAB vacío/pequeño y
selección de cada pieza.

En cockpit, las capturas phase83/88 muestran instrumentos legibles (`darkFrac≈.30–.31`,
`clippedFrac≈.0037–.0044`) y no contaminan el exterior. Sí se confirmó un defecto de
estado: cambiar a cockpit/clean podía ocultar el countdown de forma permanente. HUD ahora
separa la visibilidad solicitada por la misión del filtro de modo, permitiendo que el
countdown reaparezca al volver al exterior. El contrato
`hud_alert_layout_contract_test.sh` protege esa reversibilidad. La captura posterior
queda pendiente en 1280×720, 2560×1440, reentrada y eclipse; no se declara cerrada sólo
por el contrato.

## Fase 95 — harness de resolución para evidencia comparable

El harness ahora acepta `--resolution WIDTHxHEIGHT`, con límites de `640×360` a
`7680×4320` y máximo de 33.177.600 píxeles. La misma resolución se aplica al framebuffer
Xvfb y a la ventana Godot, evitando que una captura etiquetada como 1280×720 siga
renderizando internamente a 1920×1080. El contrato `visual_playtest_contract_test.sh`
verifica parser, límites y las dos rutas de lanzamiento.

Comandos preparados para la próxima ronda:

```bash
bash tools/visual_playtest.sh --atmosphere --resolution 1280x720
bash tools/visual_playtest.sh --atmosphere-bodies --resolution 2560x1440
bash tools/visual_playtest.sh --edl --resolution 1920x1080
```

Estos comandos todavía no son evidencia post-cambio: el entorno actual no permite crear
el display Xvfb por la propiedad de `/tmp/.X11-unix`. Se conserva el gate fail-closed y
no se aceptan PNG generadas por renderer dummy como sustituto de framebuffer real.

## Fase 92 — evidencia paralela de cielo, planetas y HUD de motores

El frente del cielo aumentó el muestreo de la cáscara de nubes de 20 a 24 pasos y elevó
el jitter determinista de `0.18` a `0.40`. La comparación aislada de 10 km está en
`/tmp/exo_visual_audit_atmosphere_low_sphericalfilter/exo_play_10km_day.png` frente a
`/tmp/exo_visual_cycle92_skyband24/exo_play_10km_day.png` y terminó `ATMOSPHERE_LOW_OK`:

| Métrica de franja | Antes | Después | Cambio |
|---|---:|---:|---:|
| salto medio entre filas | `0.00963` | `0.00888` | `−7.7%` |
| salto máximo | `0.04080` | `0.03422` | `−16.1%` |
| contraste de horizonte | `0.25663` | `0.25907` | `+0.00244` |
| clipping | `0.00948` | `0.01028` | `+0.00080` |

Es una mitigación demostrada, no el cierre del P1: persiste trama fina y el coste de
integración aumenta aproximadamente 20% en esa ruta. La variante de 32 pasos se
descarta por coste en llvmpipe. El contrato `space_sky_banding_contract_test.sh`
protege los 24 pasos, jitter estable y límites del muestreo.

El frente de HUD confirmó la semántica de la captura compartida: rojo significa
`Failed`, gris significa apagado, amarillo significa arranque; `0/33` es presión de
cámara entregada, no el throttle solicitado. Así, `THR 100%` con `0/33` puede ser una
secuencia de `Chill/SpinPrime/Ignition/Ramp`, pero 33 puntos rojos deberían acompañarse
de un indicador `FAIL N`; la imagen del usuario no coincide completamente con la
presentación actual y queda clasificada como discrepancia de captura/estado, no como
prueba de que los motores estén apagados. El contrato
`engine_hud_visual_semantics_contract_test.sh` y los tests de presentación/telemetría
pasaron `7/7`; no se tocó la física de motores.

La calibración de planetas redujo Venus de `.28` a `.22` y limitó el rim diurno a
`.55`. Marte conserva `.012` como valor de material y ahora aplica un floor efectivo
`0.024` sólo para `mode==0` dentro del shader orbital rocoso; Venus y los gigantes no
heredan ese ajuste. La matriz posterior quedó incompleta por el fallo de Xvfb, por lo
que no se declara mejora visual de Venus o del disco orbital de Marte todavía. La
evidencia existente sí aisló un problema de Marte a
`mars_terrain.gdshader`: a 400 km el disco queda con `darkFrac=.96462`, mientras que a
10 km el terreno registra `clippedFrac=.02181` y superficie `.00011`. Esto separa el
problema orbital del shader de terreno y evita ocultarlo dentro de `planet_body`.
El nuevo `mars_terrain_lighting_contract_test.sh` protege el floor mínimo, el rim y la
separación de `solar_visibility`; el ajuste orbital permanece sólo como validación
estática hasta obtener una captura posterior.

## Auditoría focalizada — cielo, estrellas y exposición (cycle92 frente a phase88)

Se auditó exclusivamente `assets/shaders/space_sky.gdshader`, `scripts/SkyController.cs` y
los contratos `space_sky_banding_contract_test.sh`, `sky_runtime_performance_contract_test.sh`,
`visual_exposure_performance_contract_test.sh` y `starfield_performance_contract_test.sh`.
No se modificaron `planet_body`, Earth ni flight, y no se lanzó Xvfb.

La comparación válida de la ruta de nubes es la captura de 10 km diurna:

| Métrica | phase88 (20 pasos) | cycle92 (24 pasos) | Evaluación |
|---|---:|---:|---|
| salto medio entre filas | `.00963` | `.00888` | mejora `7.7%` |
| salto máximo | `.04080` | `.03422` | mejora `16.1%` |
| contraste de horizonte | `.25663` | `.25907` | cambio pequeño `+.00244` |
| clipping global | `.00948` | `.01028` | empeora `+.00080` absoluto (`+8.4%` relativo) |

La foto posterior `/tmp/exo_visual_cycle92_skyband24/exo_play_10km_day.png` muestra una
franja menos estructurada que `/tmp/exo_visual_audit_atmosphere_phase88/exo_play_10km_day.png`,
pero todavía conserva una trama fina en el horizonte. El aumento de clipping es pequeño,
no está concentrado en la región del cielo (`skyClippedFrac=0` en cycle92) y no constituye
evidencia suficiente para revertir los 24 pasos; sí queda registrado como coste visual que
debe vigilarse en la próxima matriz completa.

La evidencia de estrellas no permite un A/B post-cambio: cycle92 sólo contiene la captura
diurna y registra `starCandidateFrac=0`, `sharpStarCount=0`, como corresponde a un cielo
fotópico. El baseline nocturno phase88 registra 39 estrellas nítidas a 10 km
(`sharpStarFrac=.001782`), pero no existe una captura nocturna posterior con 24 pasos. El
código conserva la separación correcta: el shader aplica `star_transmittance`, elimina
estrellas en rayos que golpean el suelo y combina `eye_star_gain`; `VisualExposureController`
actualiza la adaptación continuamente y cambia el gain estelar sólo cuando supera el umbral
dirty de `.005`. Por tanto no se altera `star_energy`, `eye_star_gain` ni el modelo de
exposición.

Decisión: conservar 24 pasos y jitter determinista `.40` como mitigación oficial del banding;
no promover ningún ajuste adicional de estrellas/exposición por falta de evidencia nocturna
post-cambio. La siguiente prueba requerida es una captura phase94 equivalente a `10km_night`
y, si el entorno gráfico vuelve a estar disponible, una repetición de `ground_sunrise`,
`ground_sunset` y los cuatro eclipses para comprobar adaptación y recuperación de estrellas.

## Gates para cerrar la fase

Los gates históricos/revalidados (`SMOKE_OK`, `ASCENT_ORBIT_OK`, `CAUGHT`,
`ATMOSPHERE_BODIES_OK` y `ORBIT_DIRECT_OK` mediante `--verify-only`) confirman que
las rutas físicas y el harness siguen siendo reproducibles. No equivalen a una
aceptación visual post-cambio para todos los subsistemas. La matriz EDL phase88b
terminó `CAUGHT` con cinco capturas:

- el relleno no convierte el pad en una imagen gris; la mejora de silueta del casco es
  pequeña y queda clasificada como mitigación parcial;
- `CAUGHT`, brazos visibles y `contacts=2` se conservan;
- no aparecen NaN, clipping amplio adicional ni warnings de compilación;
- la matriz atmosférica mantiene sus invariantes de eclipse y no se toca la física;
- la matriz phase88 de cuerpos repite `ATMOSPHERE_BODIES_OK`, sin clipping nocturno y con
  clipping diurno de Venus reducido a `0.08826`.
- las transiciones de cámara tienen contrato y evidencia de los cinco hitos iniciales;
  la captura orbital completa queda pendiente por el coste del harness llvmpipe.

Pendientes P1 para la siguiente iteración: contraste fotográfico del casco durante
max-q/entrada, repetición EDL con el nuevo encuadre, banding horizontal residual de
10 km tras el footprint esférico, legibilidad del ground local en terminador y el
shader de terreno/disco orbital de Marte.
La lectura fría del TPS y el catch físico tienen un gate funcional histórico, pero la
aceptación visual actual del casco y del catch sigue abierta. La escena de Venus diurna conserva un
`0.08826` de clipping de superficie en phase88 y se debe comparar de nuevo tras `.22`;
ya no se promoverá el cambio sólo por inspección de código. Cada cambio deberá
acompañarse de capturas antes/después; no se promocionará
una corrección sólo porque mejore una métrica agregada.

El caso puntual de encuadre orbital de `phase81d/phase81h` queda respaldado por su
captura histórica; no cierra la iluminación terrestre actual, el terminador ni la
reentrada post-cambio. El timeout `phase81e` no se presenta como éxito y el gate físico
de ascenso válido sigue siendo `phase81d`.

## Verificación reportada de fases anteriores

- `dotnet build ExosphereSimulation/ExosphereSimulation.csproj --no-restore`: **0 warnings, 0 errors**.
- `dotnet build Exosphere.csproj --no-restore`: **0 warnings, 0 errors**.
- `dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj`: **702/702 PASS**.
- `bash tools/ci_check.sh`: **PASS**, incluyendo startup Godot, render, atmósfera, catch,
  cockpit, materiales, diagnóstico de baja altitud, contratos de HUD/EDL/cámara y los
  builds sin warnings; xUnit reportó `702/702 PASS`.
- Las corridas históricas y sus verificaciones conservan `SMOKE_OK`, `ASCENT_ORBIT_OK`,
  `CAUGHT`, `ATMOSPHERE_BODIES_OK` y `ORBIT_DIRECT_OK`; la última matriz de cuerpos
  disponible está en
  `/tmp/exo_visual_audit_bodies_phase88/` y la captura orbital limpia en
  `/tmp/exo_visual_audit_orbit_direct_phase81h/`. El smoke final post-cambio está en
  `/tmp/exo_visual_audit_smoke_phase81i/` (`mean=0.02852`, `clippedFrac=0.00062`,
  `darkFrac=0.74285`).
- La matriz atmosférica final está en `/tmp/exo_visual_audit_atmosphere_phase88/`;
  el diagnóstico aislado está en `/tmp/exo_visual_audit_atmosphere_low_mips_lod5/` y
  su control sin nubes en `/tmp/exo_visual_audit_atmosphere_low_noclouds/`.
- La revalidación EDL más reciente está en `/tmp/exo_visual_audit_edl_phase88b/`; sus
  gates son `CAUGHT`, `contacts=2`, cinco milestones y
  `edl_overlay_layout_contract_test: PASS`.
- La validación visual de cámara está en `/tmp/exo_visual_audit_camera_phase89b/`; es
  parcial por interrupción antes del PNG orbital y no reemplaza el gate `phase81d`.
- La matriz de entrada/VAB de phase90 está en `/tmp/exo_visual_cycle90_menu_1280_after.png`,
`/tmp/exo_visual_cycle90_menu_1920_after.png`,
`/tmp/exo_visual_cycle90_construction_1280_after.png` y
`/tmp/exo_visual_cycle90_vab_selection_after.png`; los tres contratos nuevos pasan.

### Verificación incremental de fase 92

- Todos los contratos estáticos ejecutados, incluido el contrato de cielo incorporado
  ahora explícitamente en `tools/ci_check.sh`, Earth ground, Mars terrain, planetas y
  HUD de motores: **PASS**.
- Builds de simulación y juego: **0 warnings, 0 errors**.
- xUnit aislado autorizado: **702/702 PASS** (`4 m 3 s` en esta VM).
- `flight_startup_quick_check.sh`: **PASS**; las escenas principales y `Construction.tscn`
  arrancan en headless con exit `0`.
- La captura visual de cielo `cycle92` tiene `ATMOSPHERE_LOW_OK`, fue revalidada con
  `--verify-only` y conserva la PNG posterior de 24 muestras.
- Earth `ALBEDO→EMISSION`, Marte/Venus y EDL/cámara pasan sus contratos y builds,
  pero sus PNG post-cambio siguen pendientes; las métricas citadas para Venus, Marte y
  EDL son baseline o evidencia previa y no se presentan como regresión cerrada.
- `cycle91b` no se contabiliza como PASS visual: Xvfb no pudo crear el display por la
  propiedad del directorio `/tmp/.X11-unix`. La calibración Earth `.08` y la matriz
  Venus/Marte requieren repetir la captura en una sesión con X11/Xvfb reparado.

## Fase 96 — trazabilidad y criterio de aceptación visual

Esta fase corrige una ambigüedad del informe: un contrato estático demuestra que una
ruta de código permanece acotada, pero no demuestra por sí solo que una captura sea
legible. Desde aquí cada resultado se etiqueta como `BASELINE`, `POST_CHANGE`,
`STATIC_ONLY` o `PENDING_XVFB`; sólo `POST_CHANGE` puede cerrar un criterio visual.

### Cobertura requerida para la siguiente matriz

| Subsistema | Resoluciones | Escenas/estados | Evidencia exigida | Estado actual |
|---|---|---|---|---|
| Earth/atmósfera | 1280×720 | 10/30/70/120/400 km día, amanecer, atardecer, noche y eclipses | PNG + `darkFrac`, `clippedFrac`, banding por filas y telemetría de visibilidad | `PENDING_XVFB`; sólo 24 pasos tiene A/B post-cambio |
| Marte/Venus | 2560×1440 | día bajo, órbita y noche por planeta | PNG + identidad de cuerpo, clipping de superficie, luminancia media y disco en cuadro | `PENDING_XVFB`; phase88 es `BASELINE` |
| EDL/catch | 1920×1080 | peak heating, retro burn, catch approach, `CAUGHT` y tres yaw | PNG + `CAUGHT`, contactos=2, ambos brazos, casco en cuadro y clipping local | `PENDING_XVFB`; phase88b es `BASELINE` del nuevo ajuste |
| Menú/VAB | 1280×720 y 1920×1080 | menú, modal, VAB vacío, selección única y múltiple | PNG + clipping de texto, selección y alineación de picking | Parcial: menú/VAB post-cambio disponible; modal/múltiple pendientes |
| Cockpit/HUD | 1920×1080 | exterior, cockpit día/noche, countdown, reentrada y eclipse | PNG + legibilidad de instrumentos, alertas, motores y visibilidad estelar | Parcial: cockpit histórico; countdown post-fix pendiente |

### Umbrales de regresión

Son gates de comparación, no sustituyen revisión humana:

- `clippedFrac` global ≤ `0.02`; una escena que parte de un baseline mayor no puede
  empeorar más de `+0.005` absoluto sin una justificación explícita.
- `darkFrac` del sujeto principal no puede aumentar más de `+0.05` frente al baseline;
  en catch además deben quedar visibles casco y los dos brazos.
- La diferencia media de luminancia entre resoluciones equivalentes debe ser ≤ `0.08`
  y no puede aparecer clipping en texto/UI por encima de `0.01` del área del frame.
- El banding de 10 km se reporta con salto medio y máximo entre filas; el cambio sólo
  se promueve si mantiene la mejora de 24 pasos (`−7.7%` medio y `−16.1%` máximo) sin
  incrementar clipping global más de `0.005`.
- La visibilidad del eclipse debe conservar el orden físico `día > parcial > totalidad`
  y recuperar el nivel diurno después del evento; ningún frame aceptado puede contener
  NaN, infinito o radiancia negativa.
- Rendimiento: el informe debe registrar resolución, renderer, adaptador, tiempo de
  precálculo, memoria LUT y frame time. En llvmpipe esos valores son diagnóstico de
  regresión, no una promesa de FPS de hardware del jugador.

### Estado de evidencia y bloqueo

El estado correcto de esta iteración es `STATIC_ONLY + PENDING_XVFB`, no PASS visual
completo. La causa reproducible es que `xvfb-run` rechaza `/tmp/.X11-unix` porque su
propietario efectivo es `nobody`; las pruebas headless no producen un framebuffer
válido. Los comandos de captura quedan preparados en la Fase 95 y deben repetirse,
sin reutilizar PNG antiguas como evidencia post-cambio, después de reparar el display.

### Verificación de cierre estático de fase 96

- `bash tools/ci_check.sh`: **PASS** con todos los contratos, ambos builds, smoke
  asíncrono de Flight y los dos smoke headless de Godot; los smoke usan logs explícitos
  fuera de `user://`.
- xUnit autorizado: **702/702 PASS**, `3 m 48 s` dentro del CI final.
- `godot_smoke_log_contract_test.sh`: **PASS**; protege las dos rutas de smoke contra
  la regresión de `user://logs`.
- `git diff --check`: **PASS** y no quedan `PlaytestShot`/autoloads temporales en el
  proyecto.
- La aceptación visual framebuffer sigue **PENDING_XVFB**. No se eleva a PASS por los
  smoke headless: esos smoke prueban carga/ejecución, no legibilidad de píxeles.

## Fase 97 — contraste del casco y consistencia del cielo

La revisión de las capturas existentes confirmó un P0 visual acotado: en
`/tmp/exo_visual_audit_edl_phase88b/exo_play_peak_heating.png` y especialmente en la
secuencia de retropropulsión, la Starship queda casi negra mientras la pluma domina la
imagen. No es un fallo de estado de motores ni de guidance; es insuficiente lectura del
material metálico contra el fondo.

Se ejecutaron dos correcciones de presentación, ambas separadas de la física:

- `VesselRenderer` eleva el relleno frío del acero de `0.10` a `0.12`, exactamente el
  máximo declarado por `steel.gdshader`; la emisión térmica permanece aditiva y acotada.
- `space_sky.gdshader` queda alineado con el contrato y la evidencia A/B en `24` muestras
  de nube y jitter determinista `0.40`. También se aclaró el comentario para no confundir
  el cambio de cantidad de muestras con la redistribución del jitter.

Verificación estática de esta fase:

- `visual_material_fill_contract_test.sh`: **PASS**.
- `space_sky_banding_contract_test.sh`: **PASS**.
- `atmosphere_low_altitude_prefilter_contract_test.sh`: **PASS**.
- `sky_runtime_performance_contract_test.sh`: **PASS**; el coste máximo sigue explícito
  (`24 × 5` evaluaciones de densidad de nube por píxel en calidad 1.0).
- Smoke Godot main/Construction: **PASS**.

La nueva lectura del casco y el impacto del relleno `.12` siguen `PENDING_XVFB`: no se
declaran mejoría visual ni ausencia de clipping hasta repetir `PEAK_HEATING`,
`RETRO_BURN` y `CAUGHT` a 1920×1080. El próximo lote de fotos conserva el mismo yaw y
exposición del baseline, e incluye tres yaw adicionales para comprobar ambos palillos.

## Fase 98 — suelo local multi-escala y ciclo solar

Esta fase aborda el suelo de baja altitud y el paso del tiempo sin convertir el
oráculo espectral ni el renderer RGB en una ruta más cara por frame de lo necesario.

### Implementación

- `earth_ground.gdshader` mantiene el mapa Blue Marble como fuente de continentes y
  costa, pero añade variación acotada en escalas macro, regional, terreno, scrub y
  grano local. Las dos escalas finales son necesarias porque una cámara a 20–100 m sólo
  ve decenas de metros; el ruido de kilómetros no podía aparecer en pantalla.
- El shader calcula un relieve normal de baja amplitud para que el detalle responda al
  Sol. El parámetro `terrain_relief_strength=0.18` evita que la malla de 96×96 produzca
  ondas artificiales; no se presenta como un heightmap medido.
- Se carga `earth_night.jpg` en el parche local y sus luces se encienden sólo en el
  lado nocturno geométrico. La luz directa sigue multiplicada por `solar_visibility`,
  por lo que una totalidad no crea luz solar ficticia.
- La coordenada de detalle se deriva de `VERTEX.xz * metres_per_unit` y no de UV2:
  esto elimina la dependencia de la interpolación de UV secundaria del mesh duplicado.
- `SunController` publica elevación solar continua, fase `DAY/CIVIL_TWILIGHT/
  NAUTICAL_TWILIGHT/ASTRONOMICAL_TWILIGHT/NIGHT`, tiempo simulado y escala temporal en
  `PERF_SOLAR_CYCLE`. El HUD Full muestra esa elevación y fase. La física ya mantiene
  la plataforma y la superficie con la rotación de `Universe.CurrentTime`.

### Evidencia reproducible

- `dotnet build Exosphere.csproj --no-restore`: **0 warnings, 0 errores**.
- `earth_ground_lighting_contract_test.sh`: **PASS**.
- `solar_cycle_contract_test.sh`: **PASS**; conserva la prueba de velocidad de rotación
  de `LaunchSite.GetPosition(earth,time)`.
- Captura real llvmpipe parcial en
  `/tmp/exo_play-terrain-realism-v4/exo_play_ground_day.png`: ya no es un plano liso;
  aparecen escalas de costa/suelo y grano local. La ejecución registra
  `PERF_SOLAR_CYCLE ... elevationDeg=44.998 phase=DAY` y `meanFrameMs=160.41` en la
  matriz de 640×360.
- La matriz 640×360 anterior registra `ground_sunrise` en fase `CIVIL_TWILIGHT` y
  `ground_sunset` con transición cálida, pero no se contabiliza como PASS porque la
  ejecución se interrumpió antes de `ground_night`.
- Después de esa captura se redujo el muestreo procedural de tres FBM de dos octavas
  a una muestra por escala, conservando las cinco escalas visibles y reduciendo el
  coste de fragmento. Esta optimización requiere repetir la evidencia 1280×720; la
  PNG v4 no se usa como benchmark final de la versión optimizada.

### Límites y decisión

El suelo es ahora una reconstrucción visual multi-escala sobre un mapa satelital; no es
topografía fotogramétrica ni contiene datos locales de elevación. La aceptación visual
completa queda **PENDING** hasta repetir los cuatro estados (`day`, `sunrise`, `sunset`,
`night`) a 1280×720 y revisar las luces nocturnas y el coste en hardware real. La
telemetría confirma que el amanecer se deriva del tiempo simulado; a x1 un día terrestre
dura 86 164 s simulados; el día solar además incluye el movimiento de la efeméride del
Sol, y el HUD/warp permite acelerarlo para observar amanecer y noche en una sesión.

## Fase 99 — auditoría visual framebuffer y correcciones post-evidencia

Esta fase repite la revisión por subsistema con framebuffer real 1920×1080, renderer
OpenGL3 sobre llvmpipe. Las PNG y los logs se conservan fuera del repositorio para que
la revisión fotográfica sea reproducible; las etiquetas `POST_CHANGE` de esta sección
no reutilizan las capturas históricas de las fases 88–98.

### Lanzamiento, suelo y ciclo solar — `POST_CHANGE`

- Evidencia: `/tmp/exo_visual_launch_surface_v1/` (`pad`, `liftoff`) y
  `/tmp/exo_visual_ground_v1/` (`ground_day`, `ground_sunrise`, `ground_sunset`,
  `ground_night`).
- `LAUNCH_OK`, motores `33/33`; las capturas mantienen el complejo, deluge y Starbase
  en cuadro sin radiancia negativa. `assets/shaders/launch_surface.gdshader` añade
  agregado, weathering y grano en coordenadas de mundo a las superficies dominantes.
- `ATMOSPHERE_GROUND_OK`, cuatro estados de iluminación. El log registra `DAY` a
  `45°`, `CIVIL_TWILIGHT` a `-1°`, transición cálida a `+1°` y `NIGHT` a `-35°`.
  La transición es monotónica en la telemetría solar y el direct-light continúa
  bloqueado por `solar_visibility`.
- Límite: el parche Earth sigue siendo una reconstrucción multi-escala sobre Blue
  Marble; no es fotogrametría ni un heightmap medido de Boca Chica.

### Órbita y transición de cámara — `POST_CHANGE`

- Evidencia: `/tmp/exo_visual_ship_v7/exo_play_ship_vacuum.png` y
  `/tmp/exo_visual_ship_v7.log`.
- `SHIP_OK`; `VISUAL_NODES ship padVisible=False ... rendererVisible=True` confirma
  que la infraestructura no queda flotando en el origen cuando la nave activa está a
  118 km. La causa era que la visibilidad consultaba el booster de retorno aunque la
  cámara seguía al Starship orbital.
- Se corrigió la regla para que el pad siga al vehículo activo en baja altitud o en
  una captura activa. Se redujo el multiplicador de distancia de `EnterShipChaseView`
  de `2.5` a `1.7`: el fuselaje ya es legible en la toma orbital, sin cambiar la física.

### Reentrada y chopsticks — `POST_CHANGE`

- Evidencia: `/tmp/exo_visual_edl_v3/` (`entry`, `peak_heating`, `retro_burn`,
  `flip_complete`, `caught`) y `/tmp/exo_visual_edl_v3.log`.
- `CAUGHT`, `contacts=2`, `arms=visible`, `physical=armed`. El anillo de Max-Q queda
  centrado en la nave durante `ENTRY` y desaparece al bajar la presión dinámica; ya no
  queda como halo desligado. La captura `caught` muestra tower y brazos en la escena.
- `RETRO_BURN` conserva `ENG 3/6`, `THR 40%` y la pluma visible. El relleno térmico
  del casco se mantiene acotado; no se declara que el TPS sea fotogramétrico.
- Repetición posterior al filtro de roles: `/tmp/exo_visual_edl_v4/`, `SUMMARY
  reason=CAUGHT frames=515`; `caught` mantiene los brazos visibles y los dos contactos.
  La captura terminal registra `clippedFrac=0.00302` y `neonGreenFrac=0.000000`.

### Mars, Venus y Saturno — `POST_CHANGE`

- Mars/Venus: `/tmp/exo_visual_bodies_v2/` y `/tmp/exo_visual_bodies_v2.log`, gate
  `ATMOSPHERE_BODIES_OK`. El parche de Mars pasó de 6 km a 120 km y cambió a ruido de
  relieve regional; Mars a 10 km ya no colapsa en una esfera lisa, mientras que el
  parche se oculta en 400 km. Venus conserva sus bandas atmosféricas sin clipping de
  superficie en el caso diurno bajo.
- Saturno: `/tmp/exo_visual_saturn_v2/` y `/tmp/exo_visual_saturn_v2.log`, gate
  `SATURN_OK`. La llegada visual usa el vector hacia el Sol combinado con el eje axial
  de Saturno; `PERF_SOLAR_CYCLE` confirma `DAY`, y la captura contiene cuerpo y anillos
  completos.

### Verificación de la fase 99

- `dotnet build Exosphere.csproj --no-restore`: **0 warnings, 0 errors**.
- `tools/ci_check.sh`: **PASS**; contratos completos, builds sin warnings, smoke de
  arranque y **703/703** pruebas xUnit (`0` fallos, `0` omitidas).
- Contratos de suelo, Mars, Saturno, pad, Max-Q, plasma y optimización: **PASS**;
  la optimización queda en **47/47** comprobaciones.
- `git diff --check`: **PASS**.
- El filtro de visibilidad del complejo ahora exige un vehículo activo terrestre o un
  catch Starship-family validado por rol (`command`/`booster`), evitando que un catch
  ajeno vuelva a insertar el pad en una toma orbital.
- Resultado de fase: **POST_CHANGE / PASS**, con las limitaciones visuales de datos
  reconstruidos descritas arriba.

## Fase 100 — detalle Starbase tower V1.1 y hardening de input HUD

Esta iteración agrega lectura funcional de la torre: rieles/sheaves del carriage,
rub rails, rollers frontales, cables de soporte y cues del upper Ship QD. El objetivo
es que la torre no se lea como una malla genérica en capturas laterales, sino como
hardware operativo de catch/QD.

Evidencia post-cambio:

- `bash tools/visual_playtest.sh --launch --run-id pad-tower-v11-launch2 --skip-build`:
  **PASS**, `SUMMARY reason=LAUNCH_OK`, capturas `pad` y `liftoff`, PNGs verificados.
- La primera repetición detectó un `NullReferenceException` en
  `HUDController._UnhandledInput` durante una transición de escena/input posterior al
  cierre del harness. Se corrigió usando una referencia local nullable a `Viewport`
  antes de marcar input como handled.
- `dotnet build Exosphere.csproj --nologo -v quiet`: **0 warnings, 0 errors**.
- `dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --nologo`:
  **703/703 PASS**.
- `bash tools/ci_check.sh`: **PASS**, incluyendo contratos completos, builds, xUnit,
  startup smoke y VAB smoke.
- `git diff --check`: **PASS** y no quedan `PlaytestShot`/autoloads temporales.

Límite: la captura `liftoff` valida carga, composición y ausencia de geometría rota,
pero todavía no es una comparación fotográfica fina contra metraje Starbase diurno;
eso sigue como tarea visual de referencia.
