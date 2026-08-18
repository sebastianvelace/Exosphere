# Fase 58 — dirty cache de sky y lighting atmosférica

Fecha: 2026-08-17  
Estado: **integrada como reducción de invalidaciones; FPS GPU pendiente**

## Hallazgo

El shader atmosférico ya estaba limitado a calidad interactiva `0.60`, sky incremental,
cadencia de 12 Hz y LUT CPU asíncrona. El trabajo repetido restante estaba en la presentación:

- `PhaseLightingController` escribía energía, glow, color y energía de la luz direccional en
  cada frame aunque el valor no hubiera cambiado;
- `SkyController.UpdateEnvironment` reasignaba el color ambiental y `BackgroundEnergyMultiplier`
  en cada actualización de atmósfera.

Los setters de `Environment`/`DirectionalLight3D` pueden invalidar recursos de render aunque
la asignación sea idéntica, por lo que eran invalidaciones evitables separadas del coste del
shader.

## Cambio

Se añadieron comparaciones con tolerancia `1e-4` antes de escribir:

- energía ambiental y parámetros fijos de glow;
- color y energía de la luz direccional;
- color ambiental del sky y energía de fondo.

Los valores calculados, la fórmula de reentrada, la transmitancia directa, visibilidad solar,
orden 4, calidad del shader, LUT y cadencias no cambian. La tolerancia sólo elimina escrituras
sub-perceptuales y conserva actualizaciones mayores.

## Verificación

- Contrato atmosférico: **PASS**.
- Contrato de optimización acumulado: **PASS**, `42/42`.
- Builds Godot/simulación: **PASS**, 0 warnings, 0 errors.
- Suite xUnit: **PASS**, `696/696`, 0 skipped.
- Startup Flight: **PASS**, 60 frames con LUT atmosférico asíncrono.
- CI completo sobre este diff: **PASS**, contratos, builds, suite y smoke incluidos.
- A/B framebuffer/FPS: no se declara; el host sigue bloqueado por X11/llvmpipe.

## Decisión

Promover sólo si CI completo mantiene todos los contratos. La ganancia esperada es reducción
de invalidaciones de presentación, no una cifra de FPS; el coste dominante del sky shader
requiere una comparación framebuffer en GPU física para cuantificarse.
