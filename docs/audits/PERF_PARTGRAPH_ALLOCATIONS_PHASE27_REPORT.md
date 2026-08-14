# Auditoría de allocations de PartGraph — fase 27

Fecha: 2026-08-14
Área: `ExosphereSimulation/Parts/PartGraph.cs`
Estado: `PASS` local; no es una medición de GPU ni de FPS.

## Hipótesis

El scheduler mixto consulta `Vessel.Parts.ActiveEngines` para clasificar cada nave. Fuera de
`Vessel.Tick`, esa consulta reconstruía la etapa activa mediante:

- `Where(...).ToList()` para localizar desacopladores activos;
- `GetChildren(...).FirstOrDefault()` y `GetChildren(...).Any(...)`, que generaban
  enumeradores LINQ;
- `CollectSubtree`, que creaba una lista y una cola por subárbol.

En `mixed_fleet` esta ruta se ejecuta repetidamente para 16 naves y era un candidato
directo para allocations evitables.

## Implementación

`BuildCurrentStageParts` ahora:

- recorre `_parts` y `_joints` por índice;
- reutiliza `_stageSubtreeScratch` y `_stageTraversalScratch` como buffers privados del
  `PartGraph`;
- mantiene el primer hijo según el orden original de `_joints`;
- mantiene el orden BFS del subárbol y el fallback de devolver todas las partes cuando no
  hay desacoplador activo o la topología no permite seleccionar una etapa.

No se modificaron integradores, fuerzas, masa, propulsante, deadlines, SOI, contactos,
staging mecánico ni contratos públicos.

## Evidencia

Benchmark reproducible:

```bash
OUT_DIR=/tmp/exo_phase27_scheduler_after_256 \
SAMPLES=256 WARMUP=32 \
bash tools/perf/scheduler_phase6_benchmark.sh
```

| Escenario | p95 (80 muestras) | allocations/tick (antes) | allocations/tick (después) | cambio |
|---|---:|---:|---:|---:|
| `full_single` | 0.0477 ms | 5,981.9 B | 5,253.9 B | −12.17% |
| `full_fleet` | 0.1125 ms | 19,971.3 B | 17,371.3 B | −13.01% |
| `rails_fleet` | 0.7421 ms | 190,077.9 B | 186,749.9 B | −1.75% |
| `mixed_fleet` | 3.6976 ms | 718,564.0 B | 598,761.2 B | −16.67% |
| `wake_catchup` | 1.0812 ms | 211,843.3 B | 182,879.3 B | −13.67% |

La medición de 256 muestras posterior registró `mixed_fleet` en 4.0265 ms p95 y
605,960.5 B/tick. Los contadores permanecieron en `dispatches=450`, `projections=396` y
`catchup=0`: el cambio no oculta trabajo físico ni altera la política del scheduler.

Pruebas ejecutadas:

- `PartGraphHotPathTests`: 1/1 PASS;
- pruebas de staging, motores y breakup: 15/15 PASS;
- suite xUnit completa posterior: 585/585 PASS, 0 omitidos;
- `git diff --check`: PASS.

## Decisión

Promover la reducción de allocations como cambio CPU de bajo riesgo, pendiente del CI
integral y de repetir la matriz visual en un host válido. No se declara una mejora de FPS,
VRAM o GPU: el host actual sigue bloqueado por display/GPU física y EventPipe externo.

## Rollback

Revertir el commit que contiene este cambio restaura únicamente la implementación LINQ de
`BuildCurrentStageParts`; no requiere migración de datos ni cambios en `project.godot`.
