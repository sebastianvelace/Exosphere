# Allocations de presentación HUD — fase 45

## Alcance

`FlightHudPresenter.Capture` se ejecuta cada frame de vuelo y construía una lista,
iteradores LINQ y un array de alertas incluso cuando no había ninguna alerta. La
corrección mantiene un workspace de alertas, ordena como máximo seis elementos con
insertion sort y devuelve `Array.Empty<FlightAlertSnapshot>()` en el caso común sin
alertas. Cuando sí existen alertas, devuelve una copia propia para que los snapshots
anteriores no sean mutados por la siguiente captura.

No se cambiaron fórmulas orbitales, lecturas de motores, cadencia de simulación,
`Universe`, Godot scripts ni configuración de runtime.

## Medición reproducible

Comando antes y después, con el mismo host y configuración:

```text
SAMPLES=32 WARMUP=8 bash tools/perf/allocations_tick_phase23_benchmark.sh
```

| Señal | Antes | Después | Cambio |
|---|---:|---:|---:|
| `full_single.hud_capture_bytes` | `1,601.5 B` | `961.5 B` | `-40.0%` |
| `full_single` scheduler p95 | `0.0520 ms` | `0.0565 ms` | variación de muestra |
| `mixed_fleet` scheduler p95 | `4.4341 ms` | `5.4049 ms` | no se atribuye al cambio |

La allocation del presenter es una señal administrada, no un frame-time de GPU.
La variación del scheduler mixto impide declarar una mejora de CPU con estas dos
corridas; la decisión se limita a la reducción directa de trabajo temporal del HUD.

## Verificación funcional

- `FlightHudPresenterTests` y `FlightHudPresenterAllocationTests`: **5/5 PASS**.
- El test nuevo verifica que el camino sin alertas usa la colección vacía compartida,
  que las alertas se ordenan igual, que el acknowledgement conserva su estado y que
  un snapshot anterior no cambia después de otra captura.
- La prueba usa una órbita circular sintética para no confundir el fixture con una
  nave a 100 m sin contacto de suelo que produciría una alerta física válida.

## Decisión

`PROMOTE` como optimización de presentación CPU: el cambio es local, mantiene el
contrato de snapshot y no activa física reducida. Debe repetir el benchmark con más
muestras durante la integración completa y conservarse separado de cualquier cambio
de scheduler o de calidad atmosférica.
