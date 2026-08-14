# Fase 23 — matriz visual atmosférica completa

Fecha: 2026-08-14
Responsable: N2 (playtest y documentación)
Estado global: **INCOMPLETE / BLOCKED**

## Decisión

No se declara `ATMOSPHERE_OK`. La matriz visual completa no pudo terminar en este
entorno y no se fabrican resultados para las capturas que faltan.

La evidencia preservada demuestra que ocho casos Earth sí llegaron a una captura
válida, con estado físico coincidente con la solicitud y exposición estable. Eso no
es suficiente para aprobar la matriz: el contrato exige 20 casos Earth, incluyendo
eclipses y cockpit. Además, el harness actual no tiene casos visuales para Mars o
Venus; su validación espectral offline no sustituye capturas framebuffer de esos
cuerpos.

## Alcance declarado por el harness

`tools/visual_playtest.sh --atmosphere` define actualmente estos 20 casos Earth:

- superficie: `ground_day`, `ground_sunrise`, `ground_sunset`, `ground_night`;
- altitud diurna: `10km_day`, `30km_day`, `70km_day`, `120km_day`, `400km_day`;
- altitud nocturna: `10km_night`, `30km_night`, `70km_night`, `120km_night`, `400km_night`;
- eclipse: `eclipse_clear`, `eclipse_partial_central`, `eclipse_partial_limb`,
  `eclipse_total`;
- cockpit: `cockpit_120km_day`, `cockpit_120km_night`.

No hay selector de cuerpo ni entradas equivalentes para Mars/Venus en este modo. La
matriz solicitada para esos cuerpos —día a baja altitud, día en órbita y noche— queda
por tanto como trabajo pendiente de la herramienta, no como un resultado negativo de
la física.

## Ejecuciones realizadas

### Corridas aisladas N2 de esta sesión

Se usaron IDs exclusivos y un presupuesto máximo de 300 s por lanzamiento:

```text
OUT_DIR=/tmp/exo_play-n2-atmo-phase23-rN \
LOG=/tmp/exo_play-n2-atmo-phase23-rN.log \
bash tools/visual_playtest.sh --atmosphere --run-id n2-atmo-phase23-rN \
  --skip-build --max-runtime 300
```

Resultado: **BLOCKED antes de iniciar la escena en todos los lanzamientos**.

| Run | Evidencia del bloqueo | PNG/telemetría de escena |
|---|---|---:|
| `r1` | Godot cayó de X11 a Wayland por `XDG_RUNTIME_DIR` inválido | 0 |
| `r2` | `XDG_RUNTIME_DIR` retirado; Godot siguió sin display X11 | 0 |
| `r3` | runtime temporal válido; `xvfb-run --auto-servernum` seleccionó un display no accesible | 0 |
| `r4`–`r7` | wrapper temporal y pruebas de selector X11; el socket Xvfb no sobrevivió al lanzamiento | 0 |
| `r8`–`r9` | display fijo; Xvfb reportó `failed to start` por sockets/propietario del directorio | 0 |

En todos los casos el build del proyecto terminó con `0 Warning(s)` y `0 Error(s)`.
Las variaciones de wrapper, `--display-driver x11`, descriptor del lock y display fijo
fueron temporales y se revirtieron; no forman parte del repositorio. El intento de
corregir `/tmp/.X11-unix` con `sudo chown` no pudo ejecutarse porque el entorno exige
contraseña. No se modificaron permisos del sistema.

### Artefacto preservado y revalidación

Se reutilizó únicamente como evidencia el artefacto ya existente de la oleada P5:

```text
OUT_DIR=/tmp/exo_phase23_p5_atmos
LOG=/tmp/exo_phase23_p5_atmos.log
```

La verificación reproducible ejecutada fue:

```text
bash tools/visual_playtest.sh --atmosphere \
  --out-dir /tmp/exo_phase23_p5_atmos \
  --log /tmp/exo_phase23_p5_atmos.log --verify-only
```

Resultado: **exit 1 esperado**. El gate rechazó el artefacto en el primer caso
ausente, `exo_play_400km_day.png`, y no emitió `ATMOSPHERE_OK`. La salida también
enumeró los ocho `CAPTURE` preservados y confirmó que `--verify-only` no acepta una
matriz parcial.

## Evidencia válida preservada: 8/20 Earth

Los ocho PNG son imágenes RGBA 1920×1080 no vacías; ocupan aproximadamente 4.0 MiB
en conjunto. Cada caso tiene `CAPTURE`, `IMAGE` y `ATMOS_STATE`, y los valores
`actualAlt`/`sunElevation` coinciden con `ATMOS_APPLY` dentro de la tolerancia del
contrato. Todos registran `exposureSettled=True`.

| Caso | Altitud | Sol | Mean | Clipped | Sharp stars | Perf reportada |
|---|---:|---:|---:|---:|---:|---:|
| `ground_day` | 20 m | 45° | 0.45791 | 0.02245 | 0.000000 | 160.38 ms / 1 FPS |
| `ground_sunrise` | 20 m | -1° | 0.00847 | 0.00000 | 0.000000 | 160.04 ms / 2 FPS |
| `ground_sunset` | 20 m | 1° | 0.02164 | 0.00497 | 0.000000 | 159.71 ms / 1 FPS |
| `ground_night` | 20 m | -35° | 0.00004 | 0.00000 | 0.000320 | 159.99 ms / 1 FPS |
| `10km_day` | 10,000 m | 35° | 0.23723 | 0.01074 | 0.000000 | 160.22 ms / 1 FPS |
| `30km_day` | 30,000 m | 35° | 0.15448 | 0.00968 | 0.000000 | 159.64 ms / 1 FPS |
| `70km_day` | 70,000 m | 35° | 0.17425 | 0.00553 | 0.000000 | 160.00 ms / 2 FPS |
| `120km_day` | 120,000 m | 35° | 0.18070 | 0.00277 | 0.000000 | 160.46 ms / 2 FPS |

La inspección visual directa de `ground_day` muestra gradiente cielo-superficie y disco
solar sin framebuffer negro. `120km_day` muestra el limbo terrestre y el cielo espacial;
no se observó un framebuffer negro ni una banda verde evidente en esas dos capturas.
Estas observaciones son descriptivas; no convierten la matriz incompleta en un PASS.

Capturas disponibles:

```text
/tmp/exo_phase23_p5_atmos/exo_play_ground_day.png
/tmp/exo_phase23_p5_atmos/exo_play_ground_sunrise.png
/tmp/exo_phase23_p5_atmos/exo_play_ground_sunset.png
/tmp/exo_phase23_p5_atmos/exo_play_ground_night.png
/tmp/exo_phase23_p5_atmos/exo_play_10km_day.png
/tmp/exo_phase23_p5_atmos/exo_play_30km_day.png
/tmp/exo_phase23_p5_atmos/exo_play_70km_day.png
/tmp/exo_phase23_p5_atmos/exo_play_120km_day.png
```

Telemetría y consola:

```text
/tmp/exo_phase23_p5_atmos.log
/tmp/exo_phase23_p5_atmos.log.console
/tmp/exo_phase23_p5_atmos/run-summary.txt
```

El `run-summary.txt` conserva `status=FAIL` y los ocho milestones; no contiene un
resumen de éxito.

## Casos faltantes

En el artefacto preservado faltan 12 casos Earth:

```text
400km_day
10km_night 30km_night 70km_night 120km_night 400km_night
eclipse_clear eclipse_partial_central eclipse_partial_limb eclipse_total
cockpit_120km_day cockpit_120km_night
```

También faltan todas las capturas visuales Mars/Venus. El comando espectral de fases
anteriores sí compara Earth, Mars y Venus en CPU, pero registra otra cosa: no prueba
la presentación visual, el framebuffer, la exposición ni la cámara en esos cuerpos.

## Pruebas y contratos

Ejecutado en esta fase:

```text
bash -n tools/visual_playtest.sh
# PASS

bash tools/tests/atmosphere_phase23_contract_test.sh
# PASS

bash tools/tests/visual_playtest_contract_test.sh
# PASS: 1 fixture válido y 11 inválidos

bash tools/tests/optimization_phase23_contract_test.sh
# summary pass=25 fail=0
```

Los contratos siguen exigiendo todos los PNG, `IMAGE`, `ATMOS_STATE`, exposición
estable y `SUMMARY reason=ATMOSPHERE_OK`; no se relajaron para admitir la corrida
parcial.

## Causa y siguiente condición de cierre

Hay dos bloqueos independientes:

1. El host no pudo proporcionar un Xvfb funcional durante esta sesión. Para repetir
   la corrida se necesita un display Xvfb operativo o corregir el propietario de
   `/tmp/.X11-unix` fuera de este entorno controlado.
2. Aunque el display funcione, el modo `--atmosphere` debe ampliarse para sembrar y
   capturar explícitamente Mars/Venus. Hasta entonces sólo puede cerrarse la matriz
   Earth de 20 casos.

Para cerrar el gate sin falsos positivos se requiere una nueva corrida aislada que
produzca los 20 PNG Earth, sus filas de telemetría y `SUMMARY reason=ATMOSPHERE_OK`,
seguida por capturas visuales equivalentes de Mars/Venus. Debe repetirse
`--verify-only` sobre el mismo artefacto y conservarse el log completo.

## Higiene de cambios

N2 sólo modificó este informe. No se modificaron runtime, shaders, `project.godot` ni
el harness permanente; el autoload temporal fue retirado por el script. Los demás
cambios no confirmados visibles en el checkout pertenecen a otras oleadas/agentes y
no forman parte de este resultado.
