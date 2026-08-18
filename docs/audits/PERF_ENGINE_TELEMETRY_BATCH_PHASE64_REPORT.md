# Fase 64 — snapshot por lote de telemetría de motores

Fecha: 2026-08-18  
Área: `PartGraph.FillEngineReadouts`, `Vessel`, `EngineGridHUD` y `FlightHudPresenter`

## Objetivo

Reducir el trabajo CPU redundante de la instrumentación de vuelo sin cambiar la simulación.
En cada actualización, el HUD ya necesitaba las filas por motor y además volvía a consultar la
etapa activa para calcular empuje, flujo másico, Isp y número nominal. Esas consultas repetían
la selección de etapa y la evaluación de los mismos motores.

## Cambio

`PartGraph` conserva el overload existente de `FillEngineReadouts` y añade un overload con
`out EngineTelemetrySummary`. El mismo recorrido que construye las filas ahora entrega:

- número nominal declarado de motores;
- número de filas físicas reportadas;
- empuje total corregido por presión;
- flujo másico total;
- Isp efectivo derivado de esos dos acumulados.

El resultado agregado se guarda junto al cache de filas. Si la muestra siguiente es un cache hit,
la validación de estados conserva la semántica anterior y recupera también el resumen, sin
reevaluar thrust/flujo por separado.

La ruta pública anterior permanece sin cambios para callers que sólo requieren filas. `Vessel`
expone el overload equivalente. `EngineGridHUD` y `FlightHudPresenter` son los consumidores
promovidos; el renderer de plumas sigue usando sólo filas porque no necesita agregados.

## Medición CPU aislada

Fixture: cluster runtime Super Heavy de 33 motores, 100 pasos de arranque, 2.000 muestras
calentadas, .NET 8 en el host local.

```text
EngineTelemetryBatch: samples=2000; legacy=181.879 ms; batch=3.563 ms; reduction=98.04%
```

Esta cifra mide específicamente la eliminación de tres consultas agregadas repetidas por muestra;
no es una promesa de FPS. El coste total de render sigue dominado por el framebuffer llvmpipe,
por lo que cualquier promoción de calidad visual o de presupuesto de frame requiere una GPU
física. La física, consumo, staging, presión y fallos no se modifican.

## Cobertura

- runtime con 33 motores: filas, empuje, flujo, Isp y cache equivalentes;
- fallo de una instancia: 32 motores entregables, fila de fallo conservada;
- cluster agregado de Starship: 6 nominales pero una fila agregada, con selección de 3 motores;
- valores finitos y consistencia contra `GetCurrentThrust`/`GetCurrentMassFlow`/`GetCurrentIsp`;
- HUD principal y grid consumen el resumen para no volver a recorrer la etapa.

Prueba focalizada: **3/3 PASS**. La suite completa terminó en **701/701 PASS**, con builds de
simulación/Godot en 0 warnings y 0 errores, y `flight_startup_quick_check: PASS`.

El smoke framebuffer de esta fase quedó `BLOCKED` por infraestructura: `xvfb` no pudo crear su
listener porque `/tmp/.X11-unix` pertenece a `nobody:nogroup` y el servidor exige propiedad de
`root`. El mismo bloqueo ya afecta las mediciones visuales del host; no se registra como PASS
ni como regresión funcional.

## Decisión

Promover el snapshot por lote como optimización de presentación CPU. No habilitar hibernación
física, bajar la cadencia del universo ni cambiar el preset atmosférico con este resultado.
El siguiente perfil deberá medir el impacto en framebuffer real y revisar la creación diferida
de recursos visuales de planetas, que continúa siendo un candidato independiente del HUD.
