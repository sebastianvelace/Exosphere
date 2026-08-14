# Fase 27 P2 — allocations de Vessel, PartGraph y HUD

Fecha: 2026-08-14
Estado: `PASS` local; CPU managed, no es una medición de FPS/GPU.

## Cambios integrados

- `PartGraph.Parts` y `Joints` conservan una vista read-only estable en lugar de crear un
  wrapper por acceso.
- La selección de etapa, la búsqueda de hijos, el cálculo de posiciones locales y el
  subárbol usan recorridos indexados y buffers reutilizables.
- `FlightHudPresenter` usa `FillEngineReadouts` y cuenta motores/capacidades con buffers y
  bucles indexados; ya no materializa `GetEngineReadouts().ToArray()` en cada captura.
- El benchmark añade `hud_telemetry_capture` y valida que el snapshot reutilizable permanezca
  finito.

No se tocaron ecuaciones, timestep, selección de motores, consumo, staging, gimbal ni reglas
de propagación.

## Medición

Comando comparable:

```bash
SAMPLES=256 WARMUP=32 OUT_DIR=/tmp/exo_phase27_p2_combined \
  bash tools/perf/allocations_tick_phase23_benchmark.sh
```

Resultados posteriores a la integración combinada:

| Ruta | Resultado |
|---|---:|
| `Flight7VesselTick` | 368.2 B/tick |
| `full_single.vessel_tick` | 88.2 B/tick |
| `full_single.engine_readout_snapshot` | 8.2 B/muestra |
| `full_single.hud_telemetry_capture` | 1,776.2 B/captura |
| `mixed_fleet` | 583,032.0 B/tick |

Los contadores físicos permanecieron en `mixed_fleet=450 dispatches/tick`, `396
projections/tick` y `0 catch-up/tick`; la reducción no oculta trabajo del scheduler.
El informe aislado de P2 midió, con un baseline de 256 muestras, reducciones de 90.7% en
`Vessel.Tick`, 99.1% en `FillEngineReadouts` y 79.2% en la captura completa del HUD. El p95
del scheduler se considera ruidoso y no se presenta como ganancia de FPS.

## Validación

- pruebas focalizadas Starship/PartGraph: `14/14 PASS`;
- suite xUnit: `585/585 PASS`, 0 omitidos;
- contratos completos: `34/34 PASS`;
- builds de simulación y Godot: `0 warnings, 0 errors`;
- startup Flight y smoke Flight/Construction: `PASS`;
- `git diff --check`: `PASS`.

## Decisión

Promover la optimización de allocations. Es un cambio CPU acotado y reversible, separado de
la física. No se infiere una mejora de GPU/VRAM/FPS hasta disponer de framebuffer y adaptador
físico; esa validación continúa bloqueada por el host actual.
