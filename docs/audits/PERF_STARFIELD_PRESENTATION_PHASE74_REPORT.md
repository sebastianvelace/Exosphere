# Fase 74 — muestra atmosférica del starfield

Fecha: 2026-08-18  
Área: `scripts/StarfieldController.cs`, `tools/tests/starfield_performance_contract_test.sh`

## Hallazgo

El starfield debe recentrarse con la cámara en cada frame para evitar clipping y conservar la
referencia inercial. Sin embargo, también recalculaba por frame cuerpo dominante, altitud,
density y velocidad sólo para decidir alpha y air-streaks. Esas señales visuales toleran una
muestra de 20 Hz.

## Cambio implementado

Se separaron las dos frecuencias:

- `GlobalPosition` y el transform de los streaks siguen la cámara cada frame;
- `SampleSimulationState` actualiza alpha objetivo y emisión de streaks cada `1.0 / 20.0` s;
- un cambio de nave o universo fuerza un refresh inmediato;
- los setters de alpha/visibilidad/emisión permanecen dirty-gated.

Se conserva la malla única de 3.500 estrellas, el fade de 30–80 km, el umbral de densidad y el
umbral de velocidad de los streaks. No se modifican cámara, floating origin ni física orbital.

## Reducción estructural

En un frame rate de 60 Hz:

| Trabajo | Antes | Ahora |
|---|---:|---:|
| cuerpo/altitud para fade | 60/s | 20/s |
| density/velocidad para streaks | hasta 60/s | hasta 20/s |
| recenter de cámara | 60/s | 60/s |
| writes estables de shader/emisión | hasta 60/s | 0 |

El máximo de antigüedad de la señal atmosférica es 50 ms; el movimiento espacial de la cámara
no se degrada.

## Verificación

- `starfield_performance_contract_test.sh`: PASS;
- `dotnet build Exosphere.csproj --no-restore`: 0 warnings, 0 errors;
- la suite completa, xUnit y los smoke tests de Godot son el gate de integración de esta fase.

La medición de FPS/frametime con framebuffer real sigue pendiente por X11/Xvfb. No se declara
una mejora de FPS sin esa evidencia.

## Decisión

Promover como optimización CPU de presentación con gate visual pendiente. La matriz debe cubrir
pad de día/noche, ascenso atravesando 30–80 km, vacío, reentrada, cambio de planeta y cockpit;
debe comprobar que el starfield no parpadea, que los streaks se encienden en atmósfera densa y
que la cámara mantiene su recenter e identidad inercial.
