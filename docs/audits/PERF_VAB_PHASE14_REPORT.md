# Fase 14 — preview 3D del VAB bajo demanda

Estado: aplicado; alcance fuera del vuelo; sin cambio de física
Fecha: 2026-08-13

## Problema reproducible

La auditoría de recursos identificó un `SubViewport` de construcción de `1024×1024` con
`RenderTargetUpdateMode.Always`. El preview se oculta cuando la asamblea no tiene piezas,
pero ocultar `_previewRenderer` no detiene por sí solo el procesamiento del nodo. Por tanto,
un VAB recién abierto podía conservar un target de render y un `VesselRenderer` procesando
sin una nave visible.

El caso no se mezcla con el bloqueo inicial de Flight: el VAB y el sandbox son escenas
distintas. Esta fase elimina trabajo inútil del VAB y deja la medición de GPU del vuelo para
la fase de hardware real descrita en `PERF_RENDER_PHASE13_REPORT.md`.

## Cambio

- `ConstructionController` crea el target con `SubViewport.UpdateMode.Disabled`.
- Asamblea vacía: el target queda `Disabled`, `_previewRenderer.Visible=false` y su
  `ProcessMode` queda `Disabled`.
- Asamblea con piezas: el target vuelve a `Always`, el renderer vuelve a `Inherit` y el
  preview se construye con el mismo `BuildFromVessel`.
- Error de construcción: se aplica el mismo apagado seguro y se conserva el mensaje visual
  de preview oculto.
- El picking no se apaga: `HandlePreviewClick` continúa usando
  `_previewViewport.World3D.DirectSpaceState`, por lo que seleccionar/editar una nave
  existente no depende de que el target haya sido actualizado ese frame.
- Se emite `PERF_VAB_PREVIEW stage=update_mode` sólo al cambiar de estado para permitir
  auditoría sin llenar el log por frame.

## Gates ejecutados

- `cockpit_subviewport_contract_test.sh`: PASS; conserva Flight en `3×512` pausado fuera
  de IVA y exige el gating demand-driven del VAB.
- `vab_quick_check.sh`: PASS en 4548 ms; el log observó
  `active=false viewport_mode=disabled renderer_process=disabled`.
- `tools/ci_check.sh`: PASS; 559/559 xUnit, ambos builds en 0 warnings/0 errores,
  `flight_startup_quick_check` PASS y smoke Godot PASS.
- Smoke framebuffer de Flight: PASS (`SMOKE_OK`), captura válida en
  `/tmp/exo_vab_phase14_flight/exo_play_pad.png`; el log conservó lazy planets, worker
  atmosférico y `PERF_COCKPIT ... update=disabled`.

No se declara una ganancia de FPS ni de VRAM: el VAB no tiene un benchmark GPU in-process y
el host disponible usa llvmpipe. El beneficio aceptado aquí es una reducción determinista de
trabajo cuando el preview está vacío, con rollback trivial eliminando el gating de modo.

## Rollback

Revertir el commit restaura el target `Always` y el procesamiento heredado. No hay cambios en
`Universe`, `Vessel`, integración, staging, motores, atmósfera ni datos de vehículos.
