# Fase 39 — A/B de calidad del sky atmosférico

Fecha: 2026-08-15  
Área: `space_sky.gdshader` mediante `RenderPerformanceProbe`

## Hallazgo

La fase 38 mostró que retirar el sky baja el render de ~1.10 s a ~0.42 s en llvmpipe. El
shader ya tiene el uniforme `atmosphere_quality`, pero el perfil oficial usa `0.60`. Se
midió una variante opt-in de `0.25` sin modificar el valor oficial.

## Medición

Smoke framebuffer, 11 muestras después del calentamiento:

| Perfil | CPU render mediana | GPU mediana | objetos | primitivas | draw calls |
|---|---:|---:|---:|---:|---:|
| Oficial `atmosphere_quality=0.60` | 1,098.077 ms | 1,102.228 ms | 9,774 | 1,218,406 | 15,772 |
| A/B bajo `atmosphere_quality=0.25` | 788.115 ms | 795.604 ms | 9,774 | 1,218,406 | 15,772 |
| A/B `hide_sky` | 416.502 ms | 424.003 ms | 9,773 | 1,218,394 | 15,772 |

La calidad baja reduce aproximadamente 28% el tiempo de render frente al perfil oficial,
conservando el sky, la escena, las sombras y los mismos contadores de geometría. La corrida
terminó `SMOKE_OK` y produjo PNG válida. La inspección de la captura pad no mostró una
regresión obvia en la interfaz ni el complejo de lanzamiento, pero ese caso es demasiado
oscuro para validar terminador, limbo y color atmosférico.

## Cambio

El probe opt-in acepta `EXOSPHERE_RENDER_AB=sky_quality_low` (`0.25`) y
`sky_quality_min` (`0.0`). El juego normal continúa en `0.60`; las variantes sólo existen
cuando también se activa `EXOSPHERE_RENDER_PROBE=1`.

## Decisión

No promover todavía `0.25` como valor oficial. Hace falta ejecutar la matriz visual Earth a
10/30/70/120/400 km, amanecer/atardecer/noche y eclipse, además de Mars/Venus, y medir
separación rojo/azul del terminador, clipping, exposición y `neonGreenFrac`. Si el perfil
bajo conserva esas métricas dentro de límites y mejora el frame en hardware físico, se
convertirá en preset `Low`; el preset normal mantendrá la calidad actual.

## Verificación

- Build Godot: `0 warnings, 0 errors`.
- `render_performance_probe_contract_test`: PASS.
- Smoke A/B: `SMOKE_OK`, PNG válida, 11 muestras de render.
- No se cambió el shader oficial ni la física.
