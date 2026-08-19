# Fase 72 — muestra física acotada para AudioManager

Fecha: 2026-08-18  
Área: `scripts/AudioManager.cs`, `tools/tests/audio_manager_performance_contract_test.sh`

## Hallazgo

`AudioManager.UpdateLevels` consultaba por frame el cuerpo dominante, altitud, densidad,
velocidad de superficie, presión dinámica, temperatura, flujo de calor y señales de thrust/drag
para calcular niveles de audio. El generador continuaba llenando sus buffers en cada frame, pero
las entradas físicas eran repetidas y no requerían la frecuencia de render.

## Cambio implementado

Las entradas de nivel se calculan en `SampleAudioLevels` cada `1.0 / 20.0` s. El sample se
fuerza cuando cambia la nave o el universo. El llenado de los cinco generadores sigue siendo
continuo, por lo que no se introduce audio entrecortado ni se reduce la frecuencia de mezcla.

El suavizado de niveles y timbre permanece por frame. Se conservan:

- la división de sonido atmosférico y estructural según densidad;
- `GetDynamicPressure` y el mapeo de Max-Q;
- Mach y temperatura local;
- `ComputeStagnationHeatFlux`;
- `VehicleVisualPhysics.IsVisibleReentryHeating` para sincronizar audio y plasma;
- los tres aportes del buffet y el ambiente de pad.

Dentro de la misma muestra, la velocidad de superficie ahora se reutiliza para el cálculo radial
del gate de reentrada, evitando una segunda lectura idéntica.

## Reducción estructural

En un frame rate de 60 Hz:

| Trabajo | Antes | Ahora |
|---|---:|---:|
| muestras físicas para niveles de audio | 60/s | 20/s |
| llenado de buffers de audio | continuo | continuo |
| suavizado de niveles/timbre | 60/s | 60/s |
| lecturas duplicadas de velocidad por muestra | 2 | 1 |

El máximo de antigüedad de una entrada de nivel es 50 ms. La física, el thrust, el drag y el
estado de vuelo no dependen de este cache.

## Verificación

- `audio_manager_performance_contract_test.sh`: PASS;
- `dotnet build Exosphere.csproj --no-restore`: 0 warnings, 0 errors;
- la suite completa, xUnit y los smoke tests de Godot son el gate de integración de esta fase.

La medición de FPS/latencia de audio con framebuffer real queda pendiente por X11/Xvfb en este
host. No se declara una ganancia de FPS ni una medición de audio sin una captura reproducible.

## Decisión

Promover como optimización CPU de presentación con gate auditivo/visual pendiente. La validación
debe cubrir ignition, ascenso atravesando Max-Q, vacío, entrada en Earth/Mars/Venus, plasma y
apagado de motores; debe confirmar que no hay clics, silencio de un voice, desfase perceptible
entre plasma y audio ni retardo molesto después de throttle o cambio de planeta.
