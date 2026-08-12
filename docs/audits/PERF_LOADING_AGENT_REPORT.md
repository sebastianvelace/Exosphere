# Fase 2 — auditoría de carga inicial

Fecha: 2026-08-11
Ownership: `scripts/SimulationBridge.cs` y este informe únicamente
Escena: `res://scenes/flight/Flight.tscn`
Renderer: Godot 4.6.3 mono, headless, 300 iteraciones a 60 FPS fijos

## Hallazgo

`SimulationBridge._Ready()` construía un `SphereMesh`, un material y, para Saturno,
un anillo para cada planeta antes de emitir `SimulationLoaded`. El universo contiene
8 cuerpos: 1 Sol y 7 cuerpos con presentación planetaria. En el vuelo sandbox inicial
el vehículo está en la Tierra, por lo que las presentaciones de Moon, Mars, Venus,
Mercury, Jupiter, Saturn y los demás cuerpos no son necesarias para cargar la plataforma
ni para el primer frame de juego.

La física sigue cargando todos los cuerpos en `Universe`; sólo se difiere la capa visual
de los cuerpos que no son el cuerpo dominante del vehículo. La geometría solar y los
cálculos de eclipse continúan recorriendo `Universe.Bodies`, de modo que esta optimización
no elimina cuerpos físicos ni cambia sus posiciones.

## Implementación

`SpawnPlanets()` ahora:

1. Resuelve el cuerpo dominante del vehículo inicial.
2. Crea sincrónicamente sólo su esfera y material.
3. Registra los demás cuerpos como `deferred` en telemetría, sin construir sus recursos.

Después de cada `Universe.Tick`, `SimulationBridge` comprueba si el vehículo cambió de
cuerpo dominante. Si ocurre, encola una única llamada diferida para construir sólo la
presentación del nuevo cuerpo. La operación es idempotente mediante `_spawnedPlanetIds`.
Esto deja un rollback pequeño: restaurar el bucle original de `SpawnPlanets()` elimina
el comportamiento lazy sin modificar la física.

Telemetría añadida:

```text
PERF_PLANETS mode=lazy initial=earth created=1 deferred=6 total=7
PERF_PLANETS stage=queued body=<id> reason=dominant_body_transition
PERF_PLANETS stage=lazy_spawn body=<id> ms=<elapsed>
```

## Medición antes/después

Cada condición se ejecutó dos veces con el mismo comando, 300 iteraciones, 60 FPS fijos
y `tools/perf/flight_baseline.sh`. Las cifras son las emitidas por Godot; no se mezclan
con el coste del worker atmosférico.

| Métrica | Antes A | Antes B | Promedio antes | Después A | Después B | Promedio después | Cambio promedio |
|---|---:|---:|---:|---:|---:|---:|---:|
| `planets_spawned` (ms) | 1781.9 | 1675.4 | 1728.65 | 1289.9 | 1354.1 | 1322.00 | -23.5% |
| `simulation_loaded` (ms) | 1783.2 | 1676.7 | 1729.95 | 1290.9 | 1355.3 | 1323.10 | -23.5% |
| RSS máximo (KiB) | 839432 | 839428 | 839430 | 747812 | 746996 | 747404 | -11.0% |
| Wall time total | 3.46 s | 3.28 s | 3.37 s | 2.90 s | 2.99 s | 2.945 s | -12.6% |

La variación entre las dos muestras del baseline fue menor al 10% para
`simulation_loaded` (106.5 ms de rango, aproximadamente 6.2% del promedio). Las dos
muestras posteriores también permanecieron dentro de ese nivel de ruido (64.4 ms de
rango, aproximadamente 4.9% del promedio).

Los logs posteriores confirmaron:

```text
PERF_PLANETS mode=lazy initial=earth created=1 deferred=6 total=7
PERF_ATMOS body=earth stage=queued worker=true
```

No se observó `SCRIPT ERROR`, error de hilo ni una creación lazy durante el arranque
de plataforma en las dos ejecuciones de 300 frames. Eso sólo valida el escenario
inicial Earth/Starship; todavía no es una medición de transición Earth→Mars ni de una
vista que requiera todos los discos planetarios.

## Validación

- `dotnet build ExosphereSimulation/ExosphereSimulation.csproj --no-restore --nologo -v quiet`: PASS, 0 warnings, 0 errors.
- `dotnet build Exosphere.csproj --no-restore --nologo -v quiet`: PASS, 0 warnings, 0 errors.
- `GODOT_BIN=... bash tools/flight_startup_quick_check.sh`: PASS, alcanzó 60 frames con LUT atmosférico asíncrono.
- Dos ejecuciones de `tools/perf/flight_baseline.sh`: PASS, 300/300 iteraciones cada una.

## Límites y siguiente verificación

La presentación de un cuerpo nuevo se crea en una llamada diferida cuando cambia el
cuerpo dominante. El coste de esa primera presentación todavía no ha sido medido en una
transición interplanetaria, por lo que no se declara que el cambio elimine todos los
hitches de vuelo. La siguiente prueba debe iniciar un vuelo en Mars/Venus y forzar una
transición de cuerpo, verificando `PERF_PLANETS stage=lazy_spawn`, frame time y ausencia
de errores visuales. No debe promoverse un streaming más agresivo hasta contar con esa
captura y una medición de memoria durante la transición.
