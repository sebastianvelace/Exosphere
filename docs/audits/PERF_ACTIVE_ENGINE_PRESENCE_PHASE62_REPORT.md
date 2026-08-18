# Fase 62 — consulta de presencia de motores sin enumerador de interfaz

Fecha: 2026-08-17  
Estado: **PROMOVIDA como optimización CPU/presentación; FPS GPU pendiente**

## Problema aislado

`LaunchEffectsController`, `EngineStartupController`, `MissionManager`, `AudioManager` y
`CameraShake` sólo necesitaban saber si la etapa actual tenía algún `Part` de motor activo,
pero consultaban `ActiveEngines` como `IEnumerable<Part>`. Esa fachada de compatibilidad podía
boxear el enumerador de `List<Part>` en cada consulta y reconstruía el camino de presencia para
una respuesta booleana.

No se sustituyó por `ActiveEngineCount`: ese contador representa instancias con presión de
cámara y es una señal distinta. La nueva consulta conserva exactamente la semántica de
`ActiveEngines.Any()` —motor sano, seleccionado y perteneciente a la etapa actual.

## Cambio

`PartGraph` expone `HasActiveEngineParts`, implementado como `ActiveEngineList.Count > 0`, y
`Vessel` entrega el wrapper equivalente. Los cinco consumidores de presentación usan ahora
esa propiedad. El enumerable público `ActiveEngines` permanece intacto para HUD, construcción
y callers externos que necesitan enumerar motores.

No cambian:

- selección de etapa, hot-stage, throttle, presión de cámara o fallos;
- fórmulas de thrust, consumo, torque, TVC o scheduler;
- telemetría `ENG`, `FailureCode`, plumas o audio thresholds;
- comportamiento de salto, reentrada y captura por torre.

## Verificación

- prueba focalizada: **6/6 PASS**;
- equivalencia antes/durante/después de hot-stage: PASS;
- regresión de allocations: el camino legado debe superar al nuevo por más de 512 bytes en
  256 consultas, mientras el camino nuevo queda en `0–512` bytes;
- `starship_hotpath_contract_test.sh`: PASS;
- build de simulación y Godot: **0 warnings / 0 errors**;
- CI completa: **698/698 PASS**, 0 omitidos;
- startup Flight: PASS a 60 frames con LUT atmosférico asíncrono;
- contratos gameplay, cadencia y telemetría visual: PASS.

El framebuffer/GPU no se usa para declarar una ganancia de FPS: el host continúa con el
bloqueo de Xvfb descrito en `PERF_FRAMEBUFFER_SMOKE_PHASE61_REPORT.md`.

## Decisión

Promover el cambio como reducción de trabajo/allocations en presentación. Mantener pendiente
la cuantificación de frame p95 en hardware objetivo y continuar con la matriz visual antes de
promover calidad atmosférica, hibernación física o LOD de simulación.

