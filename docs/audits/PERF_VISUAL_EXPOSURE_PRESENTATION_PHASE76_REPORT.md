# Fase 76 — muestra física para adaptación de exposición

Fecha: 2026-08-18  
Área: `scripts/VisualExposureController.cs`, `tools/tests/visual_exposure_performance_contract_test.sh`

## Hallazgo

La adaptación ocular ya se actualizaba por frame y tenía dirty-cache para `TonemapExposure` y
`eye_star_gain`, pero antes de adaptar volvía a consultar por frame cuerpo dominante, altitud,
atmósfera, densidad, velocidad y flujo térmico. La integración óptica directa ya estaba
limitada a 10 Hz; el resto de entradas podía seguir la misma frontera de presentación.

## Cambio implementado

`SampleExposureState` toma una muestra a `1.0 / 20.0` s y fuerza refresh al cambiar de nave o
universo. La adaptación continúa por frame consumiendo la última muestra, de manera que su
respuesta temporal no se escalona. La integración `DirectSolarTransmittance` conserva su
cadencia validada de 10 Hz y su invalidación por cuerpo/horizonte/altitud/dirección.

Se conservan:

- luminancia de sky, superficies y plasma;
- `ComputeStagnationHeatFlux` y el cálculo de plasma;
- floor de cockpit y límites de exposición;
- dirty-cache de `TonemapExposure` y `eye_star_gain`.

## Reducción estructural

En un frame rate de 60 Hz:

| Trabajo | Antes | Ahora |
|---|---:|---:|
| cuerpo/altitud/atmósfera para luminancia | 60/s | 20/s |
| densidad/velocidad/heat flux | hasta 60/s | hasta 20/s |
| adaptación ocular | 60/s | 60/s |
| integración de transmitancia directa | hasta 10/s | hasta 10/s |

La entrada visual puede tener hasta 50 ms de antigüedad; la adaptación sigue siendo continua.
No se modifica la física ni el LUT del renderer.

## Verificación

- `visual_exposure_performance_contract_test.sh`: PASS;
- `dotnet build Exosphere.csproj --no-restore`: 0 warnings, 0 errors;
- la suite completa, xUnit y los smoke tests de Godot son el gate de integración de esta fase.

La captura de framebuffer/FPS continúa pendiente por X11/Xvfb. No se declara una mejora de FPS
ni una validación de exposición visual sin una ejecución reproducible.

## Decisión

Promover como optimización CPU de presentación con gate visual pendiente. La matriz debe cubrir
día, terminador, eclipse, noche, reentrada con plasma, cockpit, cambio Earth/Mars/Venus y el
salto `J`; debe comprobar que no hay pumping, clipping nuevo, parpadeo estelar ni desfase entre
plasma, luminancia y adaptación.
