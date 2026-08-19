# Fase 68 — dirty-cache del pad y chopsticks

Fecha: 2026-08-18  
Área: `scripts/LaunchPadController.cs`, `tools/tests/launch_pad_performance_contract_test.sh`

## Hallazgo

El pad de lanzamiento es presentación, pero su `_Process` hacía tres trabajos repetidos:

- dos búsquedas `Any` sobre toda la flota por frame (`IsCaught` y
  `IsAttemptingTowerCatch`);
- ocho escrituras de posición de nodos de los chopsticks por frame, incluso en pose estable;
- asignación de `Visible` para cada foco nocturno por frame.

La decisión física de captura ya vive en `Universe`/`Vessel`; el pad sólo comunica visualmente
ese resultado.

## Cambio implementado

- El estado de flota se muestrea a 20 Hz con un recorrido indexado y salida temprana cuando ya
  se conocen ambos flags.
- La interpolación `_chopstickCloseAmount` continúa por frame, pero las posiciones sólo se
  escriben cuando la escala cambia más de `0.0001`.
- Las luces nocturnas usan `_lastNightFloodlightsState` y sólo reciben setters al cruzar el
  umbral `SolarVisibility < 0.20`.
- Un pad convencional sin chopsticks retorna antes de consultar la flota.

La torre no se oculta desde este cambio. La visibilidad durante reentrada y captura permanece
controlada por `SimulationBridge`; `CatchCaptured` sólo procede de `Vessel.IsCaught` y la pose
cerrada sigue siendo `target = CatchCaptured`.

## Reducción estructural

En un pad Starbase visible a 60 Hz y con una flota estable:

| Trabajo | Antes | Ahora |
|---|---:|---:|
| scans de estado de captura | 120/s (2 por frame) | 20/s |
| escrituras de pose tras asentarse | 8 por frame | 0 |
| escrituras de luces estables | una por foco y frame | 0 |

El movimiento durante apertura/cierre conserva la interpolación por frame; por eso no se
presenta una cifra de FPS, sólo la reducción de trabajo redundante.

## Verificación

- `launch_pad_performance_contract_test.sh`: PASS;
- `dotnet build Exosphere.csproj --no-restore`: 0 warnings, 0 errors;
- `tools/ci_check.sh`: `CI_EXIT=0`, xUnit `702/702 PASS`, contratos de optimización `46/46 PASS`;
- startup Flight y Construction headless: PASS, builds sin warnings ni errores;
- el framebuffer real sigue condicionado por X11/llvmpipe y no se usa como evidencia todavía.

## Decisión provisional

El cambio queda promovido como optimización CPU de presentación: CI y contratos pasan, y el
cierre sigue siendo físicamente autoritativo. El playtest framebuffer EDL no se etiqueta PASS
porque el host no produjo harness/capturas debido a X11/Xvfb. No se pausa la física, no se
hiberna el vehículo y no se cambia el timestep.
