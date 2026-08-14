# Fase 12 — dirty state de plumas y setters de material

Estado: experimento medido y rechazado; no promovido
Fecha: 2026-08-13
Alcance: `scripts/PlumeSystem.cs`, contrato de runtime de plumas

## Hipótesis medida

`PlumeSystem.UpdateGroup` se ejecuta con cadencia visual de 30 Hz. Para cada unidad que está
firing escribía cinco uniforms (`throttle`, `expansion`, `atmo_pressure`, `throttle_level` y
`energy`) aunque el valor no cambiara. También reasignaba `Visible` en pivots, humo y luces.
Cada setter puede propagar estado al servidor de rendering; por eso el objetivo es eliminar
escrituras redundantes, no bajar la fidelidad del shader ni apagar partículas activas.

## Cambio

- `PlumeUnit` conserva el último valor escrito de cada uniform.
- `SetShaderFloatIfChanged` usa una tolerancia de 0.001, menor que el cambio visual relevante
  de estos parámetros, y escribe el primer valor siempre (`NaN` como estado inicial).
- `Pivot.Visible`, `Smoke.Emitting` y `Light.Visible` sólo se asignan cuando cambian.
- Escala del cono, flicker, energía de luz, partículas, presión, expansión y throttle siguen
  calculándose igual; sólo se evita el setter si su resultado no requiere propagación.
- `EngineVisualPeriodSeconds = 1/30` y el buffer de `EngineReadout` de fase 11 permanecen
  intactos.

No se cambió `Vessel.Tick`, timestep, staging, motor, trayectoria ni ninguna ecuación física.
El dirty state vive en la capa Godot y se descarta al reconstruir la unidad visual.

## Resultado de medición

Se ejecutó el mismo benchmark `renderer_benchmark.sh --mode pad` bajo OpenGL3/Xvfb/llvmpipe,
con 50 muestras y captura PNG válida en ambos commits:

| Métrica | Padre `b9287f9` | Candidato dirty state | Resultado |
|---|---:|---:|---|
| callback p50 | 772 ms | 795 ms | peor (+3.0%) |
| callback p95 | 1013 ms | 1066 ms | peor (+5.2%) |
| callback p99 | 1272 ms | 2858 ms | peor; outlier |
| wall time | 48.84 s | 55.95 s | peor (+14.6%) |
| captura | válida | válida | sin regresión visual de contrato |

La comparación no permite atribuir toda la diferencia a los setters por el ruido y el coste de
llvmpipe, pero sí impide promover el candidato: no produjo una mejora consistente y sus
percentiles fueron peores en la única corrida controlada. La memoria RSS tampoco se usa como
criterio porque los worktrees tuvieron caches/importaciones diferentes (`720708` vs `1232720`
KiB).

## Gates iniciales

- `plume_runtime_contract_test.sh`: PASS durante el experimento.
- Build `Exosphere.csproj`: 0 warnings, 0 errors.
- `git diff --check`: PASS.

El entorno actual usa llvmpipe y el benchmark identifica explícitamente GPU/VRAM como
`NOT_MEASURED`. El dirty cache se retiró del working tree después de la comparación; el
comportamiento oficial de plumas permanece en el commit padre.

## Decisión

La promoción queda descartada: el siguiente agente debe perfilar primero `SetShaderParameter`
y `ParticleProcessMaterial` en hardware/GPU objetivo, con caches/importaciones normalizados y
un p95 que no supere al padre.
