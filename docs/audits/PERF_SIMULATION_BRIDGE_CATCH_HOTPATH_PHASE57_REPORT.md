# Fase 57 — hot path de presentación de torre en SimulationBridge

Fecha: 2026-08-17  
Estado: **integrada como reducción segura de enumeración; FPS GPU pendiente**

## Hallazgo

Después de cada tick, `SimulationBridge._Process` mantiene la torre alineada con el sitio
Starbase y refresca los objetivos de los vehículos que intentan una captura. Esa ruta usaba
consultas LINQ (`FirstOrDefault`/`Any`) sobre `Universe.Vessels` y `Parts.Parts`. Aunque no
cambiaba la física, era trabajo repetido en el callback que conecta el scheduler con la
presentación y podía introducir enumeración por interfaz en cada frame.

## Cambio

Se reemplazaron sólo las consultas del hot path por bucles indexados sobre las vistas estables
de `Universe` y `PartGraph`:

- selección del vehículo que ancla la torre;
- detección de cualquier aproximación activa;
- detección de roles Starship `command` y `ship_engines`;
- actualización de objetivos de captura para todos los vehículos que la intentan.

Los predicados, orden de selección, frecuencia de actualización, criterios de Earth/Starbase
y asignaciones de `CatchTarget*` permanecen iguales. Las consultas LINQ de APIs de creación de
naves y setup histórico, que no se ejecutan por frame, no se modificaron.

## Verificación

- Contrato acumulado de optimización: **PASS**, `42/42`.
- Build Godot C#: **PASS**, 0 warnings, 0 errors.
- Suite xUnit: **PASS**, `696/696`, 0 skipped.
- Startup/headless: **PASS**, Flight alcanzó 60 frames con LUT atmosférico asíncrono.
- `bash tools/ci_check.sh`: **PASS**, contratos, builds, suite y smoke incluidos.
- No se declara una mejora de FPS: el framebuffer físico sigue bloqueado por X11/llvmpipe.

## Decisión

Promover sólo si compilación, suite, startup y contratos pasan. La equivalencia funcional se
debe conservar especialmente para dos vehículos simultáneos (Ship activo y booster en
retorno), donde la actualización debe seguir recorriendo toda la flota.
