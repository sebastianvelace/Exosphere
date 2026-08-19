# Fase 71 — muestra física acotada para CameraShake

Fecha: 2026-08-18  
Área: `scripts/CameraShake.cs`, `tools/tests/camera_shake_performance_contract_test.sh`

## Hallazgo

La cámara consultaba por frame el cuerpo de referencia, densidad, velocidad de superficie,
thrust y drag del vehículo. Esas entradas sólo alimentan cuatro envolventes cosméticas que ya
se integran con `Damp` y osciladores suaves; no son parte del control, navegación ni solver.
En una cámara a 60 Hz esto duplicaba lecturas de física de presentación durante todo el vuelo.

## Cambio implementado

`CameraShake` toma una muestra de entradas cada `1.0 / 20.0` s y fuerza una muestra inmediata
cuando cambia el `Vessel` o el `Universe`. La integración de `_thrustEnv`, `_buffetEnv`,
`_fovEnv` y `_entryEnv` continúa en cada frame, igual que el ruido, la atenuación por zoom y
los límites de cockpit. Así, el efecto conserva continuidad visual y puede tener como máximo
50 ms de antigüedad física presentada.

Se conservaron exactamente las fuentes y ecuaciones relevantes:

- `q = 0.5 · density · velocity²`;
- `ComputeThrust` y `ComputeDrag` para la carga no gravitacional;
- drag aislado para la carga aerodinámica de reentrada;
- comportamiento fuera de rails y fallback de cuerpo Earth.

## Reducción estructural

En un frame rate de 60 Hz:

| Trabajo | Antes | Ahora |
|---|---:|---:|
| muestras de densidad/velocidad/forces para shake | 60/s | 20/s |
| integración de envolventes y osciladores | 60/s | 60/s |
| lecturas durante nave/escena estable | cada frame | cada 50 ms |

La respuesta a throttle, Max-Q y entrada puede retrasarse hasta 50 ms en la capa cosmética,
pero el solver y los comandos continúan con su cadencia normal. No se cambió ningún valor que
alimente la simulación.

## Verificación

- `camera_shake_performance_contract_test.sh`: PASS;
- `dotnet build Exosphere.csproj --no-restore`: 0 warnings, 0 errors;
- la suite completa, xUnit y los smoke tests de Godot son el gate de integración de esta fase.

La captura de framebuffer/FPS permanece pendiente por X11/Xvfb en este host. No se publica una
ganancia de FPS sin una medición reproducible; la reducción indicada es estructural por
cadencia.

## Decisión

Promover como optimización CPU de presentación con gate visual pendiente. La matriz visual debe
comprobar arranque con throttle, cruce de Max-Q, entrada atmosférica, cambio de cuerpo mediante
`J`, rails, cockpit y activación de Reduced Motion. Se debe confirmar que no hay retardo visible
molesto ni un salto de la cámara al cambiar de nave o escena.
