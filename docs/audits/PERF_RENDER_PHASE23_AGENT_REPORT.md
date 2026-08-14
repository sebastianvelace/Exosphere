# Fase 23 P4 — auditoría de render y cadencia

Estado: cambio acotado promovido; sin afirmación de FPS o VRAM
Fecha: 2026-08-14
Alcance: `CockpitInstruments.cs`, `VesselRenderer.cs`, política existente de `ConstructionController.cs`

## Decisión

Se promovió un único cambio seguro en `VesselRenderer`: los renderers que no tienen
materiales térmicos (`_shipSteelMats` y `_tileZoneMats`) salen antes del bloque térmico
de 15 Hz. Esto evita consultas de atmósfera, velocidad de superficie, flujo térmico y
temperatura de todas las piezas cuando el resultado no puede modificar ninguna superficie.
La simulación térmica y los controles no cambian; Starship mantiene el camino completo de
reentrada porque `BuildStarshipSection` registra esos materiales.

## Políticas auditadas

- Cockpit: tres `SubViewport` de 512×512, `Disabled` fuera de IVA y actualizaciones
  `Once` limitadas a 30 Hz cuando la cámara está dentro. No se cambia la cadencia de
  controles ni de snapshots.
- Exterior: `VesselRenderer` sale inmediatamente si está oculto; plumas se actualizan a
  30 Hz, estado secundario a 20 Hz y térmica a 15 Hz sólo cuando existe presentación
  térmica. El movimiento visual de flaps/tren sigue interpolando por frame para no
  alterar controles ni introducir saltos visibles.
- VAB: el controlador existente (`ConstructionController.cs`) deja el target y el
  `VesselRenderer` desactivados con asamblea vacía y los reactiva al crear piezas. No se
  modificó en esta fase porque el archivo permitido `ConstructionPreviewController.cs`
  no existe y el controlador real queda fuera del ownership acotado.

## Evidencia y límites

Contrato ejecutado:

```text
tools/tests/render_cadence_phase23_contract_test.sh
render_cadence_phase23_contract_test: PASS
```

Validación focalizada ejecutada:

```text
GODOT_BIN=... bash tools/vab_quick_check.sh
vab_quick_check: PASS; ConstructionRegressionTests 12/12

GODOT_BIN=... OUT_DIR=/tmp/exo_phase23_p4_cockpit \
  bash tools/visual_playtest.sh --cockpit --run-id phase23-p4-cockpit --skip-build
COCKPIT_OK; PNG válida; sin GAP

GODOT_BIN=... OUT_DIR=/tmp/exo_phase23_p4_smoke \
  bash tools/visual_playtest.sh --smoke --run-id phase23-p4-smoke --skip-build
SMOKE_OK; PNG válida; sin GAP
```

Las capturas conservaron las tres pantallas del cockpit y el HUD exterior. En el smoke,
`ENG 0/33` aparece durante el estado pre-launch con throttle 0 y puntos no rojos; esto es
consistente con la semántica actual de startup y no constituye una regresión del cambio P4.

Probe opt-in de render:

```text
adapter_observed=Mesa_-_llvmpipe_(LLVM_20.1.2,_256_bits)
real_gpu_observed=false
software_renderer_detected=true
gpu_vram_bytes=NOT_MEASURED
fps_p50/p95/p99=NOT_MEASURED
```

Resultado de medición física: `BLOCKED`. El probe sí capturó contadores in-process del
backend, pero no existe una GPU física ni una fuente válida de FPS/VRAM en este host. Sus
timers y `video_mem_bytes` quedan como diagnóstico de regresión, no como presupuesto de
hardware ni criterio para bajar resolución/calidad.

La prueba enfocada existente `StarshipPerformanceRegressionTests` no quedó verde por un
cambio ajeno ya presente en el worktree: su auditoría de asignaciones mide 73.656 bytes por
muestra frente al límite 1.000. No se modificó ese archivo por el alcance de esta tarea.

El host de esta sesión usa `llvmpipe`; por tanto, cualquier timer de render o contador backend sólo
sirve para detectar regresiones y no se convierte en FPS, VRAM física ni una mejora de
hardware. La matriz GPU física de fases anteriores permanece `BLOCKED` hasta disponer de
un adaptador real.

## Alcance deliberadamente no modificado

No se tocaron `SimulationBridge`, shaders, imports, `project.godot`, física, controles,
cadencia de entrada, ni la resolución de cockpit/VAB. No se promovió una reducción de
calidad, distancia de dibujado o frecuencia de navegación sin una prueba de input/eventos.

## Reversión

Eliminar el early-out térmico y el contrato/documentación de esta fase revierte el cambio;
no requiere migración de estado ni afecta la simulación.
