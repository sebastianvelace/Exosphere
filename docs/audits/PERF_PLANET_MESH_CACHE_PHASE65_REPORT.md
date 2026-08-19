# Fase 65 — reutilización de geometría planetaria lazy

Fecha: 2026-08-18  
Área: `scripts/SimulationBridge.cs` y contrato de optimización de presentación

## Hallazgo

La carga lazy ya evitaba crear siete planetas visuales durante el arranque, pero cada cuerpo
que se materializaba después construía otra `SphereMesh` procedural idéntica de 96 segmentos
radiales por 48 anillos. Los materiales siguen siendo diferentes por planeta, pero la
topología de esfera unitaria no lo es.

## Cambio

`SimulationBridge` conserva una única `SphereMesh` compartida por instancia de puente y la
entrega a todos los `MeshInstance3D` planetarios. Los colores y parámetros permanecen aislados
porque se aplican con `SetSurfaceOverrideMaterial` en cada instancia. El registro `_spawnedPlanetIds`
continúa siendo la barrera de idempotencia y Saturno conserva su anillo hijo separado.

La física no cambia: `Universe` continúa cargando todos los cuerpos, las posiciones y los
cálculos de eclipse siguen completos, y sólo la presentación visual se difiere.

Telemetría nueva:

```text
PERF_PLANETS mesh_cache=created radial=96 rings=48 shared=True
PERF_PLANETS mode=lazy initial=earth created=1 deferred=6 total=7
```

En el arranque headless posterior, `mesh_cache=created` apareció una vez y `simulation_loaded`
terminó en 1318.1 ms. El baseline lazy documentado previamente fue 1323.1 ms; la diferencia
está dentro del ruido de una ejecución y no se presenta como ganancia de startup. El beneficio
esperado está en transiciones posteriores, donde se evita regenerar la topología; esa transición
requiere framebuffer para medir el hitch y queda pendiente por el bloqueo actual de Xvfb.

## Verificación

- contrato de optimización: **46/46 PASS**;
- build `Exosphere.csproj --no-restore`: 0 warnings, 0 errors;
- build de tests: 0 warnings, 0 errors;
- `flight_startup_quick_check.sh`: PASS, 60 frames con LUT atmosférico asíncrono;
- Godot headless Flight: exit 0, `mesh_cache=created` una vez, sin `SCRIPT ERROR`.

## Decisión

Promover la reutilización como reducción estructural de CPU/recursos en presentación, sin
atribuir una mejora de FPS todavía. No cambiar LOD, radios, materiales, orden de scattering ni
la política de cuerpos diferidos. El siguiente gate debe medir Earth→Mars/Venus/Saturn en
framebuffer físico y comparar frame p95, memoria y estabilidad de eclipse.
