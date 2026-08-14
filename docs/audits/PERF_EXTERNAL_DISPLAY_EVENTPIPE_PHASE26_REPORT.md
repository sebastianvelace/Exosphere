# Auditoría externa de display, GPU y EventPipe — Phase 26 / N14

Fecha de ejecución: 2026-08-14 (UTC).

Estado global: **BLOCKED**, con el binario de Godot y los ejecutables auxiliares
presentes, pero sin un display operativo ni un collector EventPipe disponible. Esta
auditoría es sólo preflight: no modifica runtime, shaders, `project.godot`, permisos,
paquetes ni configuración del host. No se hizo commit.

## Alcance y regla de validez

Se comprobaron de forma reproducible:

- resolución de `GODOT_BIN` y versión del ejecutable;
- display X11 nativo y socket Wayland;
- presencia y funcionamiento de `xvfb-run`/`Xvfb`;
- existencia y estado observable de `/tmp/.X11-unix`;
- evidencia de un camino de GPU físico utilizable;
- disponibilidad de `dotnet-trace` y `dotnet-counters`.

Los probes usaron `timeout`, no instalaron herramientas y sólo escribieron sus salidas
en `/tmp/exo_n14_preflight_20260814T194758Z/`.

## Resultado de los gates

| Gate | Resultado | Evidencia reproducible |
|---|---|---|
| Ejecutable Godot 4.6 mono | **PASS** | `/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64` existe, es ejecutable y devuelve `4.6.3.stable.mono.official.7d41c59c4` |
| Variable `GODOT_BIN` | **BLOCKED** | No está definida en el entorno ni en el bash interactivo usado por la sonda |
| X11 nativo | **BLOCKED** | `DISPLAY=:0`, pero `xdpyinfo` y `xrandr --current` devuelven que no pueden abrir `:0`; Godot también reporta `X11 Display is not available` |
| Wayland nativo | **BLOCKED** | `WAYLAND_DISPLAY` no está definido y no existe un socket usable bajo `/run/user/1000` |
| Ejecutable `xvfb-run`/servidor `Xvfb` | **PASS** de instalación, **BLOCKED** de operación | Ambos existen, pero `xvfb-run -a` termina con `Cannot establish any listening sockets` |
| `/tmp/.X11-unix` | **PASS** de existencia, **BLOCKED** de readiness | Existe con modo `1777`, pero su propietario es `nobody:nogroup`; Xvfb informa que el owner debería ser `root` y no crea el display |
| GPU física usable para captura | **BLOCKED** | No existe `/dev/dri`, no están `nvidia-smi`, `vulkaninfo` ni `glxinfo`; `lspci` sólo identifica un VGA AMD Barcelo sin render path accesible |
| `dotnet-trace` | **BLOCKED** | `command -v` no encuentra el ejecutable |
| `dotnet-counters` | **BLOCKED** | `command -v` no encuentra el ejecutable |
| SDK/runtime .NET | **PASS** | SDK `8.0.129`, runtime `Microsoft.NETCore.App 8.0.29` |

El `PASS` de presencia de `xvfb-run`, `Xvfb` y Godot no equivale a un PASS de captura.
La matriz visual sólo puede declararse válida cuando el servidor X acepta conexiones y
Godot produce las seis capturas y el resumen físico esperado. La presencia PCI de un
controlador VGA no es evidencia suficiente de aceleración disponible dentro de la
sesión.

## Evidencia detallada

### Godot y display

La sonda de versión fue exitosa sin inicializar una ventana:

```text
4.6.3.stable.mono.official.7d41c59c4
godot_version_exit=0
```

El entorno de la sonda quedó así:

```text
DISPLAY=:0
WAYLAND_DISPLAY=NOT_SET
XDG_RUNTIME_DIR=/run/user/1000
GODOT_BIN=NOT_SET
```

La prueba nativa falló en los tres puntos disponibles (`:0`, `:99`, `:1024` y `:1025`):

```text
xdpyinfo: unable to open display ":0".
Can't open display :0
```

El intento de inicialización gráfica de Godot bajo el display nativo falló con:

```text
ERROR: X11 Display is not available
WARNING: Display driver x11 failed, falling back to wayland.
ERROR: Can't connect to a Wayland display.
ERROR: Unable to create DisplayServer, all display drivers failed.
```

El binario es, por tanto, válido, pero el gate de captura no lo es.

### Xvfb y sockets

`xvfb-run` y `Xvfb` existen en `/usr/bin`, pero el probe controlado falló incluso
usando un display automático. La salida del servidor fue:

```text
_XSERVTransmkdir: Owner of /tmp/.X11-unix should be set to root
Fatal server error:
Cannot establish any listening sockets - Make sure an X server isn't already running
```

La inspección read-only observó:

```text
path=/tmp/.X11-unix mode=1777 owner=nobody group=nogroup type=directory
X0    socket owner=sebasvelace mode=777
X99   socket owner=sebasvelace mode=777
X1024 socket owner=nobody mode=775
X1025 socket owner=nobody mode=775
```

No había procesos `Xorg`, `Xvfb` ni `Xwayland` activos cuando se revisó la tabla de
procesos. Esto es compatible con sockets residuales o una preparación incompleta del
entorno. N14 no eliminó sockets ni cambió ownership/permisos.

### GPU y driver

`lspci -nn` sí detectó:

```text
04:00.0 VGA compatible controller: Advanced Micro Devices, Inc. [AMD/ATI] Barcelo [1002:15e7]
```

Pero el mismo host no expone `/dev/dri`, `nvidia-smi`, `vulkaninfo` ni `glxinfo`. Sin
display usable no fue posible obtener una cadena de adaptador desde una sesión Godot
no-headless. La evidencia histórica de la matriz GPU previa del repositorio observó
`Mesa - llvmpipe`; este preflight no convierte esa observación previa en un nuevo FPS,
VRAM o resultado de hardware físico.

Conclusión: la matriz física continúa en **BLOCKED**. No se deben publicar FPS, VRAM ni
comparaciones de calidad GPU desde este host.

### EventPipe

La comprobación de herramientas y del catálogo global produjo:

```text
dotnet-trace=BLOCKED_NOT_INSTALLED
dotnet-counters=BLOCKED_NOT_INSTALLED

Package Id      Version      Commands
-------------------------------------
```

El SDK/runtime están instalados, así que el bloqueo es la ausencia del collector, no la
ausencia del runtime .NET. No se instaló ninguna herramienta. El runner Phase 24 ya
mantiene el fallback determinista y fail-closed; sin `dotnet-trace` no se puede
publicar un perfil de métodos calientes, y sin un proceso benchmark de larga duración
no se debe inventar un PID para `dotnet-counters`.

## Comandos de preflight ejecutados

Los siguientes comandos son los probes read-only reproducibles usados por N14, con
salidas preservadas en `/tmp/exo_n14_preflight_20260814T194758Z/`:

```bash
command -v xvfb-run
command -v Xvfb
command -v xdpyinfo
command -v xrandr
command -v xauth
command -v dotnet-trace
command -v dotnet-counters
dotnet --info

timeout 5s env DISPLAY=:0 xdpyinfo
timeout 5s env DISPLAY=:0 xrandr --current

timeout 10s xvfb-run -a -e /tmp/exo_n14_preflight_xvfb.stderr xdpyinfo

timeout 10s /home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64 --version

test -d /dev/dri
timeout 10s lspci -nn
dotnet tool list --global
```

## Repetición exacta en un host válido

### Mars/Venus framebuffer

En un checkout limpio con un servidor X funcional, `xvfb-run` operativo y el binario
Godot disponible, ejecutar:

```bash
cd /path/to/space\ simulator
export GODOT_BIN=/path/to/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64
test -x "$GODOT_BIN"
timeout 10s xvfb-run -a xdpyinfo

OUT_DIR=/tmp/exo_phase26_mars_venus \
LOG=/tmp/exo_phase26_mars_venus.log \
timeout 2100s bash tools/visual_playtest.sh \
  --atmosphere-bodies \
  --run-id phase26-mars-venus \
  --max-runtime 1800
```

El runner ya crea su propio `xvfb-run`; no debe envolverse en otro `xvfb-run`. El gate
visual sólo es válido si el log contiene `SUMMARY reason=ATMOSPHERE_BODIES_OK` y existen
las seis imágenes: `mars_10km_day`, `mars_400km_day`, `mars_10km_night`,
`venus_10km_day`, `venus_400km_day` y `venus_10km_night`. Para revalidar evidencia
preservada sin lanzar Godot:

```bash
GODOT_BIN=/path/to/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64 \
  timeout 60s bash tools/visual_playtest.sh \
  --atmosphere-bodies \
  --run-id phase26-mars-venus \
  --verify-only
```

Un host con GPU física debe demostrar además, fuera del contrato visual, un adaptador
no software (`software_renderer_detected=false`). Un `PASS` de Xvfb por sí solo no
autoriza a presentar sus tiempos como rendimiento de una GPU física.

### EventPipe / rails y projections

Con `dotnet-trace` y `dotnet-counters` ya instalados y en `PATH`, repetir el runner
fail-closed existente:

```bash
cd /path/to/space\ simulator
command -v dotnet-trace
command -v dotnet-counters

OUT_DIR=/tmp/exo_phase26_rails_eventpipe \
SAMPLES=256 \
WARMUP=32 \
TIMEOUT_SEC=120 \
timeout 300s bash tools/perf/rails_eventpipe_phase24.sh
```

La evidencia aceptable para el collector es un archivo no vacío
`/tmp/exo_phase26_rails_eventpipe/rails_mixed.speedscope.json` y
`eventpipe_status=PASS_ARTIFACT_ONLY` en `matrix.meta`. El fallback de allocations puede
seguir siendo `PASS`, pero no sustituye los métodos calientes.

Para repetir exactamente la colección `dotnet-trace` sin el wrapper, usando el
benchmark ya compilado:

```bash
cd /path/to/space\ simulator
timeout 180s dotnet-trace collect \
  --format speedscope \
  --output /tmp/exo_phase26_rails.speedscope.json \
  -- dotnet run \
    --project tools/SchedulerBenchmark/SchedulerBenchmark.csproj \
    --no-build --no-restore -- \
    --samples 256 --warmup 32 \
    --out /tmp/exo_phase26_rails_metrics.tsv
```

`dotnet-counters` requiere un proceso vivo y un PID real. El benchmark actual termina
al completar sus muestras; por eso el runner Phase 24 lo marca `BLOCKED_NO_LONG_LIVED_TARGET`
en vez de adjuntarse a un PID adivinado. Cuando exista un modo benchmark persistente,
el comando deberá conservar esta forma, siempre con un PID observado en la misma
ejecución:

```bash
dotnet-counters monitor --process-id <PID_OBSERVADO> \
  --refresh-interval 1
```

## Decisión y siguiente preflight

N14 no promueve optimizaciones, no declara `MARS_VENUS_OK`, `EVENTPIPE_OK`, FPS ni VRAM.
Para desbloquear la siguiente oleada se necesita un host donde:

1. `xdpyinfo` y el arranque gráfico de Godot abran el display;
2. `xvfb-run` pueda crear un display sin errores de socket, o exista una sesión nativa
   adecuada para el harness;
3. el probe GPU observe un adaptador no software y un render path accesible;
4. `dotnet-trace` esté instalado y produzca un archivo no vacío;
5. exista un proceso persistente si se requiere `dotnet-counters`.

El worktree ya contenía cambios no relacionados de otra oleada (`OrbitalElementsRoundTripTests.cs`
y su informe); N14 los dejó intactos. El único archivo creado por esta auditoría es este
informe.
