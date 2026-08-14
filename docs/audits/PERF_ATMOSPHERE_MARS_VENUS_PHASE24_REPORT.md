# Matriz visual atmosférica Mars/Venus — Phase 24 (N8)

Estado: **INCOMPLETE/BLOCKED** para la validación framebuffer en este host.

Este cambio amplía el harness visual sin modificar el runtime de producción, la física,
los shaders ni `project.godot` de forma permanente. Los 20 casos Earth del modo
`--atmosphere` se mantienen intactos. La nueva matriz se ejecuta explícitamente con:

```text
bash tools/visual_playtest.sh --atmosphere-bodies --run-id <id>
```

## Casos definidos

| Cuerpo | ID | Escena | Altitud | Elevación solar |
|---|---|---|---:|---:|
| Mars | `mars_10km_day` | baja altitud diurna | 10 km | +35° |
| Mars | `mars_400km_day` | órbita diurna / transición al vacío | 400 km | +35° |
| Mars | `mars_10km_night` | baja altitud nocturna | 10 km | −35° |
| Venus | `venus_10km_day` | baja altitud diurna | 10 km | +35° |
| Venus | `venus_400km_day` | órbita diurna / transición al vacío | 400 km | +35° |
| Venus | `venus_10km_night` | baja altitud nocturna | 10 km | −35° |

Cada caso exige una captura `exo_play_<id>.png`, una fila `IMAGE`, una solicitud
`ATMOS_APPLY`, una fila `ATMOS_STATE` y exposición estable. El harness no acepta el
caso hasta que `Universe.GetDominantBody()` coincide con el cuerpo solicitado después de
`SimulationBridge.JumpToBody()`. Esto también cubre la presentación planetaria lazy: una
captura etiquetada como Mars/Venus no puede ser una captura Earth stale.

El estado físico se valida fail-closed contra:

- identidad `body` ↔ ID del caso;
- altitud con tolerancia de 2 m;
- elevación solar con tolerancia de 0.25°;
- `solarVisibility` finita dentro de `[0, 1]`;
- ausencia de eclipse sintético en esta matriz;
- energía espectral finita;
- `exposureSettled=True`;
- resumen único `SUMMARY reason=ATMOSPHERE_BODIES_OK`.

Los casos orbitales están fuera del `MaxAltitude` de Mars (100 km) y Venus (250 km) a
propósito: verifican la transición de limbo atmosférico a espacio y no sólo una copia de
la escena Earth. La referencia espectral sigue usando los perfiles físicos existentes de
cada cuerpo; no se inventan coeficientes nuevos.

## Validación ejecutada

| Prueba | Resultado |
|---|---|
| `bash -n tools/visual_playtest.sh tools/tests/optimization_phase23_contract_test.sh` | PASS |
| contrato Phase 23 + fixtures Mars/Venus válidos/inválidos | PASS, 34/34 |
| build del autoload temporal generado | PASS, 0 warnings, 0 errores |
| corrida real `--atmosphere-bodies --run-id n8-mv-phase24-r1` | **BLOCKED** |

Artefactos de la corrida:

```text
/tmp/exo_play-n8-mv-phase24-r1/run-summary.txt
/tmp/exo_play-n8-mv-phase24-r1.log.console
```

El proceso alcanzó la compilación, pero `xvfb-run` no pudo establecer un display usable:
Godot reportó `X11 Display is not available` y el servidor X informó sockets ocupados o
no eliminables en `/tmp/.X11-unix`. No se generaron PNG, `ATMOS_STATE` ni un resumen
`ATMOSPHERE_BODIES_OK`; por eso no se declara `MARS_VENUS_OK` ni se infiere calidad
visual a partir del contrato sintético.

## Limitaciones y siguiente evidencia requerida

La matriz necesita repetirse en un host con Xvfb funcional o una GPU/display físico. La
salida aceptable debe contener las seis capturas, seis pares `ATMOS_APPLY`/`ATMOS_STATE`,
las seis filas `IMAGE`, exposición estable y `ATMOSPHERE_BODIES_OK`. Después deben
compararse luminancia, clipping, separación rojo/azul y coste de LUT entre Mars y Venus.
