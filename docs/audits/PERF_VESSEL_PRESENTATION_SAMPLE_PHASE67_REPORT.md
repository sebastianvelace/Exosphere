# Fase 67 — muestra cacheada del renderer exterior

Fecha: 2026-08-18  
Área: `scripts/VesselRenderer.cs`, `tools/tests/render_cadence_phase23_contract_test.sh`

## Hallazgo

El exterior de la nave estaba limitado a sus cadences visuales, pero seguía haciendo trabajo
de selección/consulta física en cada `_Process` visible:

- `Universe.GetDominantBody(TargetVessel.Position)` a cada frame;
- `TargetVessel.GetAltitude(body)` a cada frame;
- `TargetVessel.GetAmbientPressure(body)` en cada actualización de plumas a 30 Hz.

Estas lecturas sólo alimentan presentación: no deciden integración orbital, control, staging,
combustible ni fallos de motor.

## Cambio implementado

`VesselRenderer` mantiene el cuerpo dominante, la altitud, la razón de presión atmosférica
`p/p₀` y un timer de muestra a `1.0 / 20.0` s. `RefreshPresentationSample()` se ejecuta al
primer frame visible, cada 50 ms y después de `ClearNodes()`. Plumas, flaps, tren, térmica y
las filas usadas sólo para decidir la intensidad de la pluma reciben los valores desde la
muestra compartida. `Vessel.FillEngineReadoutsAtPressure(...)` evita repetir la consulta física
de presión dentro de ese camino visual. El overload basado en cuerpo sigue intacto para HUD y
simulación. El gate `!Visible` permanece antes de cualquier consulta.

No se tocaron `Universe.Tick`, `Vessel.GetAmbientPressure` ni ecuaciones. Un cambio de cuerpo
dominante puede tardar como máximo 50 ms en reflejarse en el renderer; los consumidores físicos
continúan usando la posición viva.

## Reducción estructural

Para una nave sobre un cuerpo con atmósfera y 60 Hz:

| Consulta | Antes | Ahora | Reducción |
|---|---:|---:|---:|
| selección de cuerpo | 60/s | 20/s | 66.7% |
| altitud explícita | 60/s | 20/s | 66.7% |
| presión de pluma + llenado de filas | 60/s | 20/s | 66.7% |
| lecturas API del bloque | 180/s | 60/s | 66.7% |

La cifra se deriva de las cadences y no es un benchmark de FPS. Las escrituras de materiales,
partículas y las ecuaciones de presentación permanecen intactas para conservar el aspecto.

## Verificación

- `render_cadence_phase23_contract_test.sh`: PASS, incluyendo la muestra a 20 Hz;
- `visual_telemetry_contract_test.sh`: PASS, incluyendo el overload de presión visual;
- `dotnet build ExosphereSimulation/ExosphereSimulation.csproj`: 0 warnings, 0 errors;
- `dotnet build Exosphere.csproj --no-restore`: 0 warnings, 0 errors;
- suite xUnit completa: **702/702 PASS**, 0 omitidos;
- `tools/ci_check.sh`: `CI_EXIT=0`, contratos de optimización `46/46 PASS`;
- Godot 4.6.3 Flight headless/OpenGL3: exit 0 con `--log-file /tmp/exo_phase67_headless.log`;
- el diff no modifica `Universe`, `Vessel` ni el scheduler físico.

El smoke inicial sin `--log-file` falló antes de arrancar la escena porque el entorno no permite
escribir el log persistente `user://logs`; con log temporal terminó correctamente. La validación
de framebuffer y FPS sigue pendiente por la restricción de X11/llvmpipe; no se declara una
ganancia de FPS.

## Decisión

Promover el cache como optimización CPU de presentación. No usar esta muestra para hibernar naves,
pausar sistemas ni cambiar el timestep. La siguiente medición útil es un smoke de framebuffer en
un entorno con Xvfb escribible, comparando frame p95 y la transición de cuerpo/terminador con y
sin el cache.
