# Fase 70 — muestra cacheada del plasma de reentrada

Fecha: 2026-08-18  
Área: `scripts/ReentryPlasmaController.cs`, `tools/tests/reentry_plasma_performance_contract_test.sh`

## Hallazgo

`ReentryPlasmaController` realizaba en cada `_Process` todas las consultas y escrituras del
efecto, incluso cuando el plasma estaba oculto: cuerpo dominante, densidad, velocidad de
superficie, flujo de calor de estancamiento, orientación, composición del vehículo y
parámetros de seis mallas/materiales. Además, la detección de Super Heavy usaba el enumerable
de compatibilidad de piezas.

El efecto es presentación. El flujo de calor y el daño térmico continúan siendo calculados por
la simulación a su cadencia determinista; el controller sólo consume una muestra para dibujar.

## Cambio implementado

La presentación toma una muestra cada `1.0 / 20.0` s. Dentro de esa muestra se mantienen las
mismas fuentes físicas y gates:

- `ComputeStagnationHeatFlux(density, surfVel)` sigue siendo la fuente del plasma;
- `VehicleVisualPhysics.ReentryPlasmaVisualIntensity` conserva sus umbrales de fase;
- el shock, wake y heat glows localizados se conservan;
- la orientación, concentración windward, flicker, alpha y emisión no cambian de fórmula;
- la detección de Super Heavy usa un recorrido indexado del buffer concreto de piezas.

Las escrituras de `Visible` ahora son dirty-gated, tanto para shock/wake como para los seis
glows localizados. Esto evita invalidaciones repetidas cuando la reentrada está por debajo del
umbral o cuando una parte del vehículo debe permanecer oculta durante el stack completo.

## Reducción estructural

En un frame rate de 60 Hz y con el efecto activo:

| Trabajo | Antes | Ahora |
|---|---:|---:|
| muestras de densidad/velocidad/flujo del efecto | 60/s | 20/s |
| detecciones de Super Heavy | hasta 60/s | hasta 20/s |
| actualizaciones de shader y materiales | hasta 60/s | hasta 20/s |
| setters de visibilidad en estado estable | hasta 60/s | 0 |

El dato visual puede tener hasta 50 ms de antigüedad. No se pausa la física, no se cambia el
solver térmico y no se altera la autoridad de destrucción o captura.

## Verificación

- `reentry_plasma_performance_contract_test.sh`: PASS;
- `dotnet build Exosphere.csproj --no-restore`: 0 warnings, 0 errors;
- la suite completa y los smoke tests de Godot quedan como gate de integración de esta fase.

La captura de framebuffer sigue pendiente en este host por la limitación de X11/Xvfb y los
logs persistentes de Godot. No se afirma una ganancia de FPS sin una medición reproducible.

## Decisión

Promover como optimización CPU de presentación con gate visual pendiente. La siguiente matriz
visual debe cubrir: Starship standalone en reentrada, stack Starship/Super Heavy, transición
por el umbral de calor, actitud alineada y desalineada, separación y apagado del efecto. Debe
comprobar que no hay parpadeo de 20 Hz perceptible, que el glow de flaps aparece durante toda
la reentrada del Ship y que el stack no recibe heat glows localizados incorrectos.
