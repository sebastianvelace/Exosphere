# Fase 75 — muestra física del deluge de lanzamiento

Fecha: 2026-08-18  
Área: `scripts/LaunchEffectsController.cs`, `tools/tests/launch_effects_performance_contract_test.sh`

## Hallazgo

El deluge de Starbase resolvía por frame el cuerpo dominante, altitud, throttle y presencia de
motores para calcular la intensidad objetivo. Después de eso sí debe animar por frame las cinco
capas de partículas, el banco MultiMesh de 160 instancias y la edad del efecto.

## Cambio implementado

La condición física se mueve a `SampleLaunchState` con una cadencia de `1.0 / 20.0` s, y se
refresca inmediatamente al cambiar de nave o universo. La ruta visual conserva por frame:

- suavizado asimétrico de ignición/apagado;
- `DriveAmounts` de todas las capas;
- `DriveImmediateSteam` y el avance de `_ignitionAge`;
- anclaje y orientación usando la última muestra válida;
- `SetEmitting`, que ya era dirty-gated.

Se conservan los gates de Earth, `HasActiveEngineParts`, `MinThrottle` y
`TriggerCeilingM`. El cambio no toca el solver de ignition, el throttle ni la emisión física.

## Reducción estructural

En un frame rate de 60 Hz:

| Trabajo | Antes | Ahora |
|---|---:|---:|
| cuerpo/altitud/gate de activación | 60/s | 20/s |
| consultas de motor/throttle para el gate | hasta 60/s | hasta 20/s |
| animación de partículas | 60/s | 60/s |
| MultiMesh de vapor instantáneo | 60/s | 60/s |

La señal de activación puede tener hasta 50 ms de antigüedad visual, mientras que la animación
del humo no se escalona.

## Verificación

- `launch_effects_performance_contract_test.sh`: PASS;
- `dotnet build Exosphere.csproj --no-restore`: 0 warnings, 0 errors;
- la suite completa, xUnit y los smoke tests de Godot son el gate de integración de esta fase.

La captura de framebuffer/FPS permanece pendiente por X11/Xvfb. No se afirma una mejora de FPS
ni una aceptación visual sin evidencia reproducible del pad.

## Decisión

Promover como optimización CPU de presentación con gate visual pendiente. La matriz debe cubrir
ignition en pad, throttle bajo/alto, liberación de hold-down, subida por 140–550 m, apagado,
engine-out y cambio de planeta; debe comprobar que el deluge aparece y desaparece con la llama,
que el anclaje retrocede con el suelo y que la nube no se escalona por la muestra de 20 Hz.
