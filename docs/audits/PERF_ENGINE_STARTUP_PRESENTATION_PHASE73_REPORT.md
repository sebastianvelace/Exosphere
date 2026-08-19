# Fase 73 — muestra física del VFX de engine startup

Fecha: 2026-08-18  
Área: `scripts/EngineStartupController.cs`, `tools/tests/engine_startup_performance_contract_test.sh`

## Hallazgo

El efecto de arranque de motores consultaba por frame el cuerpo dominante, altitud, presencia
de motores y composición del vehículo. Sólo puede estar activo durante hold-down en Earth y su
intensidad ya se suaviza visualmente; por tanto, repetir esas consultas a 60 Hz no aportaba
fidelidad física.

## Cambio implementado

La condición física de startup se evalúa en `SampleStartupState` cada `1.0 / 20.0` s y se
refresca inmediatamente al cambiar de nave o universo. `Drive(_sampledThrottle, delta)` sigue
corriendo por frame, conservando ramp-up/ramp-down, flicker, partículas, escala, material y luz.

Se preservan los gates autoritativos:

- cuerpo Earth;
- `IsGroundHeld`;
- `HasActiveEngineParts`;
- throttle superior a `0.01`;
- altitud menor a `MaxStartupAltitudeM`;
- detección de Super Heavy mediante recorrido indexado.

Durante una transición inválida, la intensidad se apaga y el nodo conserva el último anclaje
válido hasta terminar el fade, evitando un salto visual al origen de escena.

## Reducción estructural

En un frame rate de 60 Hz:

| Trabajo | Antes | Ahora |
|---|---:|---:|
| resolución de cuerpo/altitud/gates físicos | 60/s | 20/s |
| enumeración de piezas | hasta 60/s | hasta 20/s |
| Drive y suavizado visual | 60/s | 60/s |
| actualización del anclaje válido | 60/s | reutilizada; muestra cada 50 ms |

El máximo de antigüedad del gate presentado es 50 ms y no se modifica ningún estado de
simulación, hold-down o motor.

## Verificación

- `engine_startup_performance_contract_test.sh`: PASS;
- `dotnet build Exosphere.csproj --no-restore`: 0 warnings, 0 errors;
- la suite completa, xUnit y los smoke tests de Godot son el gate de integración de esta fase.

La captura de framebuffer/FPS sigue pendiente por X11/Xvfb. No se afirma una mejora visual o de
FPS hasta contar con una ejecución reproducible del arranque real.

## Decisión

Promover como optimización CPU de presentación con gate visual pendiente. La matriz debe cubrir
hold-down sin throttle, ignition progresivo, engine-out, liberación de clamps, cambio a Mars y
transición de escena; debe comprobar que llama, deck glow, vapor, chispas y luz se activan y
desactivan sin retraso perceptible ni quedar anclados incorrectamente.
