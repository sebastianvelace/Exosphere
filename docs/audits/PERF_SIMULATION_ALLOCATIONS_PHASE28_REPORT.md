# Informe de allocations de simulación — fase 28

## Alcance

Se midió el tick CPU de `Universe` con el benchmark existente, separando `full_single`,
`rails_fleet`, `mixed_fleet` y `wake_catchup`. La medición no incluye FPS ni memoria de vídeo.

## Resultado

Comando posterior al cambio:

```text
OUT_DIR=/tmp/exo_phase23_alloc_after_hotpath2 SAMPLES=80 WARMUP=10 \
bash tools/perf/allocations_tick_phase23_benchmark.sh
```

| Escenario | p50 ms | p95 ms | p99 ms | B/tick | dispatches | proyecciones | válido |
|---|---:|---:|---:|---:|---:|---:|---|
| `full_single` | 0.0366 | 0.0564 | 0.0727 | 2,734.0 | 1.000 | 0.000 | sí |
| `full_fleet` | 0.1202 | 0.1572 | 0.1852 | 9,403.3 | 4.000 | 0.000 | sí |
| `rails_fleet` | 0.5290 | 0.5616 | 0.6356 | 5,931.3 | 32.000 | 0.000 | sí |
| `mixed_fleet` | 3.2946 | 4.0478 | 5.7673 | 182,965.4 | 450.000 | 396.000 | sí |
| `wake_catchup` | 1.0006 | 1.3890 | 1.6188 | 88,215.9 | 50.013 | 12.375 | sí |

Baseline comparable previo: `mixed_fleet` 583,989.8 B/tick y p95 5.0599 ms con
`SAMPLES=40`, `WARMUP=10`. El valor actual reduce aproximadamente 68.7% las allocations y
20.0% el p95; la diferencia de tamaño de muestra sólo afecta la estabilidad estadística,
no el escenario ni el warm-up.

## Cambios promovidos

- workspace por `Universe` para `PropagateAllBodies`, con `Dictionary`/`HashSet` reutilizados;
- RK4 6-DoF sobre `Vector3d`, sin arrays temporales por subpaso;
- búsqueda indexada de cuerpos y sobrecargas de fuerza sobre `IReadOnlyList` para evitar
  closures y boxing de enumeradores.

## Gates

- `RK4AllocationRegressionTests`: constante exacta y ≤64 B/subpaso: PASS;
- build de la librería: 0 warnings, 0 errors: PASS;
- benchmark de allocations: PASS, todos los estados finitos: PASS;
- no se alteraron los límites de warp, frecuencia de deadlines ni fórmulas físicas;
- validación visual GPU/orbital: pendiente por limitación de X11/llvmpipe del host.
