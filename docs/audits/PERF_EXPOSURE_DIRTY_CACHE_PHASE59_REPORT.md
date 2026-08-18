# Fase 59 — dirty cache de exposición

Fecha: 2026-08-17  
Estado: **integrada como reducción de invalidaciones; FPS GPU pendiente**

## Hallazgo

`VisualExposureController` mantiene la adaptación ocular y el LUT directo a 10 Hz, pero
escribía `WorldEnvironment.TonemapExposure` en cada frame. Cuando la exposición convergía,
esa asignación era redundante y podía invalidar el postprocesado sin aportar un cambio visible.

## Cambio

La adaptación sigue actualizándose en cada frame y conserva el límite especial del cockpit,
pero `TonemapExposure` sólo se escribe si cambia más de `1e-4` o si el valor actual es NaN.
El gain estelar, la transmitancia, las fórmulas de luminancia y la respuesta temporal no
cambian.

## Verificación

- Contrato atmosférico/sky: **PASS**.
- Build Godot: **PASS**, 0 warnings, 0 errors.
- Suite xUnit: **PASS**, `696/696`, 0 skipped.
- Startup Flight: **PASS**, 60 frames con LUT atmosférico asíncrono.
- CI completo: **PASS**, contratos, builds, suite y smoke incluidos.
- A/B framebuffer/FPS: no se declara; el host sigue bloqueado por X11/llvmpipe.

## Decisión

Promover sólo con CI completo en verde. Es una reducción de invalidaciones de postprocesado,
no una afirmación de FPS.
