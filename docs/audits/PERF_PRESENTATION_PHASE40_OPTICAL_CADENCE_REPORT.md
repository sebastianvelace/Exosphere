# Fase 40 — Integración atmosférica acotada y cadencia de iluminación

Fecha: 2026-08-15  
Área: `space_sky.gdshader` y `PhaseLightingController`

## Cambios seguros

1. Las cuatro integraciones realtime del sky convierten la calidad fraccionaria en un
   número entero efectivo de muestras mediante `ceil()`. Los límites normalizados de cada
   segmento usan ese mismo número, por lo que el último segmento termina exactamente en
   `u=1`. Esto evita la sobreintegración que ocurría con `0.25` y pasos de luz `9.5` o de
   nubes `3.5`.
2. `PhaseLightingController` conserva `DirectSolarTransmittance` durante `100 ms` y
   recalcula inmediatamente al cambiar de cuerpo, cruzar el horizonte solar, variar la
   altitud al menos `2 km` o cambiar significativamente la dirección solar. La energía de
   la luz sigue multiplicándose por `SunController.SolarVisibility`, así que el eclipse no
   queda congelado por este cache.

La física, el LUT RGB oficial de orden 4, la exposición y la calidad oficial `0.60` no
cambian. `sky_quality_low=0.25` continúa siendo un override exclusivo del probe.

## Medición A/B reproducible

Smoke framebuffer con el mismo host llvmpipe, shader corregido, cache activo y ocho muestras
posteriores al calentamiento:

| Perfil | CPU render mediana | GPU mediana | objetos | primitivas | draw calls |
|---|---:|---:|---:|---:|---:|
| Oficial `0.60` | 1,101.086 ms | 1,105.361 ms | 9,774–9,776 | 1,218,394–1,218,410 | 15,771–15,772 |
| Diagnóstico `0.25` | 940.271 ms | 944.074 ms | 9,773–9,776 | 1,218,394–1,218,410 | 15,771–15,772 |

La reducción observada en este smoke es aproximadamente `14.6%` GPU. Es una medida del
backend llvmpipe, no una promesa de FPS en hardware físico; tampoco se atribuye toda la
diferencia al cache de iluminación porque el probe mezcla render, subida de LUT y escena.

Ambos perfiles terminaron `SMOKE_OK`, generaron PNG válida y, para el perfil bajo,
registraron `PERF_GPU_AB mode=sky_quality_low applied=true`. La matriz Earth oficial fue
interrumpida voluntariamente después de `7/20` capturas debido al coste del framebuffer en
llvmpipe; dejó `PARTIAL` y restauró el proyecto. Por ello todavía no hay evidencia suficiente
para activar `0.25` en el juego normal.

## Decisión

- Mantener `InteractiveAtmosphereQuality=0.60` como perfil oficial.
- Mantener `sky_quality_low=0.25` sólo como herramienta de comparación.
- No promover `sky_quality_min=0.0`.
- Ejecutar la matriz Earth completa y Mars/Venus en una GPU física antes de crear un preset
  Low. El gate debe comparar por slug luminancia, clipping, `neonGreenFrac`, gradiente y
  calidez del terminador, estrellas nocturnas, eclipse y exposición asentada.

## Verificación

- `sky_runtime_performance_contract_test.sh`: PASS.
- `render_cadence_phase23_contract_test.sh`: PASS, incluye la política de cache óptico.
- `render_performance_probe_contract_test.sh`: PASS.
- `bash -n tools/visual_playtest.sh tools/tests/sky_runtime_performance_contract_test.sh`: PASS.
- `dotnet build Exosphere.csproj --no-restore`: 0 warnings, 0 errores.
- Smoke oficial y bajo: `SMOKE_OK`.
