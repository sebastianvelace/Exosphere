# Fase 7 — arranque y coste del sky runtime

Estado: implementado con gates verdes, pendiente de validación en GPU real
Fecha: 2026-08-12
Alcance: sandbox Flight, Starship inicial, Godot 4.6.3 mono, OpenGL3/Xvfb/llvmpipe

## Diagnóstico

El precálculo atmosférico ya se ejecutaba en un worker, pero el renderer seguía recibiendo
escrituras de uniforms personalizados a cadencia de frame. En particular, la ganancia de
estrellas se escribía desde `VisualExposureController` en cada `_Process`, y
`SkyController` reescribía geometría solar y el prefilter de nubes aunque sus valores no
hubieran cambiado. Eso puede invalidar el sky cubemap incremental y producir un coste de
renderizado repetido.

La segunda fuente de presión es el shader de cielo: la integración visible tiene 28 pasos de
vista, 20 de nubes y una integral solar adicional durante el fallback sin LUT. Esa calidad es
válida como referencia visual, pero no debe ser un límite rígido para el renderer interactivo
en software o hardware integrado.

## Cambios aprobados

- Se añadieron dirty flags para geometría solar, visibilidad solar, dirección/ángulo del
  oclusor, prefilter meteorológico y `eye_star_gain`. Los uniforms sólo se escriben cuando
  cambian más que su umbral visible.
- El sky cubemap runtime quedó en `RadianceSize=128` y `ProcessMode=Incremental`. La variante
  experimental `Realtime + 256` se midió y se descartó: fue más lenta en la corrida de
  referencia.
- `space_sky.gdshader` conserva los máximos físicos actuales, pero usa una calidad interactiva
  explícita `0.60` con mínimos acotados de 12 pasos de vista y 8 de nubes. El LUT RGB oficial,
  el oráculo espectral y el orden 4 no cambian.
- El worker CPU de LUT usa prioridad `BelowNormal` sólo durante su ejecución y restaura el
  hilo del ThreadPool al terminar. No toca objetos Godot ni comparte estado mutable con el
  árbol.
- Se añadió `tools/tests/sky_runtime_performance_contract_test.sh` y se incorporó a CI para
  impedir que se eliminen por accidente los límites, caches o la separación worker/Godot.

## Evidencia

### Arranque headless

Corrida: `tools/perf/flight_baseline.sh`, 300 iteraciones, `--fixed-fps 60`.

| Métrica | Resultado |
|---|---:|
| `SimulationLoaded` | 1347.9 ms |
| Worker atmosférico | encolado sin build síncrono |
| RSS máximo | 742768 KiB |
| Resultado | PASS, 300/300 |

### Framebuffer real de referencia

Corridas smoke de 50 frames bajo Xvfb/llvmpipe. `frame_ms` es el intervalo del callback
instrumentado por el harness, no un contador GPU; por eso no se presenta como FPS de hardware
real.

| Variante | Resultado | Media callback | Media después de 15 frames |
|---|---|---:|---:|
| `Incremental + 128`, calidad 0.60, worker normal | PASS | 996.8 ms | 880.9 ms |
| `Incremental + 128`, calidad 0.60, worker BelowNormal | PASS | 1002.0 ms | 913.2 ms |
| `Realtime + 256` experimental | PASS, descartada | 1106.2 ms | 1060.5 ms |

La variación del entorno llvmpipe es superior a la diferencia entre las dos variantes
incrementales. La decisión se basa en: smoke válido, ausencia de regresión funcional, menor
coste teórico del cubemap 128² y rechazo medido de `Realtime`. La mejora de frame debe
confirmarse con GPU real y profiler de Godot antes de declarar un objetivo de 60 FPS.

## Gates ejecutados

- Ambos proyectos .NET: 0 warnings, 0 errores.
- xUnit: 558 passed, 0 failed, 0 skipped.
- `tools/flight_startup_quick_check.sh`: PASS, 60 frames y LUT asíncrona.
- `tools/tests/sky_runtime_performance_contract_test.sh`: PASS.
- Smoke visual posterior: PASS, captura `exo_play_pad.png` válida y `SMOKE_OK`.
- Matriz visual `--atmosphere`: PASS con 20 capturas (día, amanecer, atardecer, noche,
  10/30/70/120/400 km, eclipse claro/parcial central/parcial de borde/totalidad y cockpit).
  Las escenas reportaron media de 159.98 ms en llvmpipe, máximo de escena 180 ms,
  `max clippedFrac=0.022420` y `max neonGreenFrac=0.000558`; son métricas del entorno de
  referencia y no un objetivo de GPU real.
- `Realtime + 256`: probado y revertido por resultado peor.

## Límites y siguiente fase

Esta fase no altera `Universe.Tick`, el timestep físico, staging, navegación, motores,
contactos ni el orden de scattering oficial. Tampoco demuestra todavía que el RSS de ~742 MiB
sea aceptable en hardware objetivo ni que la transición Earth→Mars/Venus sea libre de hitch.

El despliegue multiagente pendiente está definido en
[`PERFORMANCE_MULTI_AGENT_PLAN.md`](PERFORMANCE_MULTI_AGENT_PLAN.md): baseline/contratos,
pipeline LUT, scheduler/LOD físico, hot paths de Starship, render/UI, GPU/memoria y QA visual.
La siguiente ejecución debe abrir worktrees aislados por ownership, medir dos corridas base por
agente y no promover cambios hasta pasar la matriz de Starship sandbox, ascenso, eclipse y
reentrada.

Rollback de esta fase: revertir el commit que contiene este informe junto con los cambios de
`SkyController`, `VisualExposureController`, `space_sky.gdshader`, CI y el contrato. No se
requiere migración de datos ni cambios en los JSON físicos.
