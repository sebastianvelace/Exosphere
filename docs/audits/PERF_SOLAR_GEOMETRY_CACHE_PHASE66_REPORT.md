# Fase 66 — snapshot compartido de geometría solar

Fecha: 2026-08-18  
Área: `scripts/SunController.cs`, `scripts/SkyController.cs`

## Hallazgo

`SunController` y `SkyController` calculaban de forma independiente la visibilidad del disco
solar con `MissionGeometry.LimbDarkenedSolarDiscVisibility`. El primero actualizaba a 20 Hz y
el segundo recorría todos los cuerpos en cada actualización del sky a 12 Hz. Con siete cuerpos
no solares, eso representaba 224 evaluaciones por segundo en estado estable, además de repetir
la selección del mismo oclusor.

## Cambio

`SunController` es ahora el dueño de un `SolarGeometrySnapshot` a 20 Hz. El snapshot conserva:

- visibilidad solar total;
- visibilidad excluyendo el cuerpo atmosférico local;
- oclusor seleccionado;
- dirección y radio angular del oclusor.

`SkyController` consume el snapshot a 12 Hz. Si todavía no existe una muestra —primer frame o
reconstrucción de escena— mantiene el cálculo local anterior como fallback único, evitando
uniformes sin inicializar. No se cambió la cadencia de la física, la decisión de eclipse, la
transmitancia atmosférica ni la energía de sistemas.

La muestra continúa siendo de presentación: `SystemsController` sigue leyendo
`SunController.SolarVisibility`, y los cálculos físicos del universo no dependen de este cache.

## Reducción estructural

En estado estable, las evaluaciones caras pasan de:

```text
SunController 20 Hz × 7 + SkyController 12 Hz × 7 = 224 evaluaciones/s
SunController 20 Hz × 7 = 140 evaluaciones/s
```

Es una reducción determinista del **37.5%** de esa operación, no una promesa de FPS. El sky
sigue escribiendo uniforms sólo cuando cambian sus valores y mantiene su proceso incremental.

## Verificación dinámica

Godot 4.6.3 headless Flight terminó con exit 0, build sin warnings/errores y estas marcas:

```text
PERF_SOLAR_GEOMETRY mode=shared cadenceHz=20 skyConsumerHz=12 body=earth shared=True
PERF_SOLAR_GEOMETRY consumer=sky cache_hit=True
```

La primera actualización conserva fallback; las siguientes demuestran que el consumidor usa el
snapshot. No se detectaron `SCRIPT ERROR` ni errores de acceso entre hilos.

## Decisión

Promover el cache como reducción CPU de presentación. No declararlo como mejora de FPS hasta
repetir el smoke de framebuffer en una instalación donde Xvfb pueda crear su socket y medir
frame p95, cromaticidad del terminador y monotonía de eclipses.
