# Fase 16 — gate de `VesselRenderer` fuera de cámara

Estado: aplicado; alcance visual IVA; sin cambio de física
Fecha: 2026-08-13

## Problema

La auditoría de memoria/render detectó que `CameraController` oculta `StarshipRenderer`
durante el cockpit, pero `Node3D.Visible=false` no detiene `_Process`. Por ello
`VesselRenderer` seguía consultando cuerpo dominante, presión, flujo, térmica, contacto,
readouts de motores y setters de plumas/materiales aunque el exterior no podía rasterizarse.

La física de `Vessel` y los sistemas de misión permanecen activos. Sólo se pausa el
consumidor visual del exterior.

## Cambio

`VesselRenderer._Process` ahora sale inmediatamente cuando `Visible=false` o no hay
`TargetVessel`:

```csharp
if (!Visible || TargetVessel == null) return;
```

Los timers visuales no avanzan mientras el nodo está oculto. Al volver a exterior, el primer
frame visible ejecuta las cadencias pendientes y sincroniza plumas, flaps, tren, paracaídas
y térmica con el estado físico actual. No se modifica `Vessel.Tick`, timestep, staging,
control, consumo ni trayectoria.

## Gates

- `visual_telemetry_contract_test.sh`: exige el gate de visibilidad y las cadencias previas.
- Build `Exosphere.csproj`: PASS, 0 warnings y 0 errores.
- Captura cockpit candidata: `COCKPIT_OK`, PNG válida, sin `GAP`; las tres pantallas y el
  estado orbital permanecen visibles.
- Baseline válido en el commit padre `e6e2410`: también `COCKPIT_OK` después de importar
  explícitamente `.godot/imported`. Una corrida anterior se descartó por caché de assets
  ausente y no se usa en los números.

Comparación controlada bajo el mismo OpenGL3/Xvfb/llvmpipe, resolución del harness y probe:

| Telemetría | Padre | Candidato | Variación observada |
|---|---:|---:|---:|
| `PERF_FRAME` p50 | 156 ms | 153 ms | -1.9% |
| `PERF_FRAME` p95 | 366 ms | 358 ms | -2.2% |
| `PERF_FRAME` p99 | 1081 ms | 1135 ms | peor/outlier |
| render CPU in-process p50 | 141.427 ms | 140.000 ms | -1.0% |
| render CPU in-process p95 | 345.816 ms | 341.754 ms | -1.2% |
| render CPU in-process p99 | 1011.429 ms | 1066.810 ms | peor/outlier |
| draw calls p50/p95/p99 | 626/2311/2312 | 626/2311/2312 | igual |

La muestra no demuestra una reducción consistente de percentiles altos en llvmpipe; por
ello no se declara una ganancia de FPS. El gate se mantiene como eliminación determinista
de consultas y setters de un nodo que no puede verse, con rollback listo si una GPU real
demuestra regresión. La validación de rendimiento definitiva queda pendiente de hardware
real y no se sustituye por estos p99 ruidosos.

El host llvmpipe permite verificar la ruta visual y contadores, pero no sirve para afirmar
una ganancia de FPS de hardware. La comparación de coste se registra como CPU/render
backend y queda separada de GPU real.

## Rollback

Revertir el commit elimina únicamente el early-out de `VesselRenderer`; la física y la
jerarquía de cámara no requieren migración.
