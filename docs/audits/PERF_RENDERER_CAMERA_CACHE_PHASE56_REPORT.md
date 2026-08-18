# Fase 56 — cache de resolución de cámara y renderer

Fecha: 2026-08-17  
Estado: **integrada como reducción segura de trabajo de presentación; FPS GPU pendiente**

## Hallazgo

`CameraController._Process` debía conservar soporte para nodos creados después de `_Ready`
(`ActiveVesselRenderer` y `CockpitRenderer`) y para el nombre legacy `StarshipRenderer` usado
por algunos harnesses. Esa compatibilidad hacía que el resolver pudiera recorrer el árbol de
la escena en cada frame mientras un nodo faltaba o mientras permanecía el fallback legacy.

El renderer exterior ya tenía su propio gate de visibilidad y cadencias para plumas, flaps,
paracaídas y térmica; el problema aislado aquí era la resolución de referencias, no la
simulación ni la actualización de materiales.

## Cambio

`CameraController` conserva las referencias cacheadas y sólo vuelve a consultar el árbol si
una referencia es nula/inválida o si todavía se está usando el fallback `StarshipRenderer`.
Cuando la escena es dinámica, los reintentos se limitan a uno cada `0.25 s`; el camino normal
con referencias válidas retorna antes de cualquier `FindChild`.

El setter de visibilidad también valida instancias Godot antes de acceder a `Visible`, para
que una transición de escena no use una referencia liberada. No se cambiaron la orientación,
el seguimiento, la distancia, el modo cockpit, la física, la entrada ni el renderer oficial.

## Verificación

- Contrato estático de cadencia: **PASS**; comprueba cache, cooldown y retorno temprano.
- `git diff --check`: **PASS**.
- Build Godot C#: **PASS**, 0 warnings, 0 errors.
- Suite xUnit: **PASS**, `696/696`, 0 skipped.
- Startup/headless: **PASS**, Flight alcanzó 60 frames con LUT atmosférico asíncrono.
- `bash tools/ci_check.sh`: **PASS**, contratos, builds, suite y smoke incluidos.
- A/B de framebuffer/FPS: no se declara; el host sigue bloqueado por X11/llvmpipe.

## Decisión

Promover el cambio como optimización de presentación de bajo riesgo. El reintento acotado
preserva la creación lazy de nodos y el fallback del harness, mientras elimina el recorrido
por frame en el estado estable. La siguiente medición debe usar una captura real para separar
el coste residual del sky atmosférico, draw calls y composición de esta mejora puntual.
