# Fase 60 — VFX de motores acotado y dirty cache del suelo local

Fecha: 2026-08-17  
Estado: **integrada como reducción de setters; FPS GPU pendiente**

## Auditoría de plumas

La física conserva 33/39 motores y sus fallos individuales. La presentación no crea un
emisor por motor en Starship: `PlumeSystem` usa cuatro unidades agregadas para Super Heavy y
seis para Ship, y `VesselRenderer` actualiza ese conjunto a 30 Hz. Por ello no se conectó la
telemetría individual del HUD a 39 meshes/particles/lights ni se cambió la semántica de
`ENG`, `FailureCode` o throttle.

## Cambio seguro

`EarthGroundController` escribía `fade`, `haze_color`, `sun_dir` y `horizon_dist` en cada
frame mientras el parche local estaba visible. Ahora conserva esos valores y sólo actualiza
los uniforms escalares/ambientales cuando cambian más de `1e-4` (dirección solar usa un
umbral angular de `1e-10` en distancia cuadrada). `sub_p`, `east_local` y `north_local`
siguen actualizándose cada frame para no congelar el scroll geográfico del terreno.

Cuando el parche deja de ser válido o visible, la cache se invalida para que la siguiente
entrada a baja altitud reprograme todos los uniforms necesarios.

## Verificación

- Contrato sky/VFX: **PASS**.
- Build Godot: **PASS**, 0 warnings, 0 errors.
- Suite xUnit: **PASS**, `696/696`, 0 skipped.
- Startup Flight: **PASS**, 60 frames con LUT atmosférico asíncrono.
- CI completo: **PASS**, contratos, builds, suite y smoke incluidos.
- A/B framebuffer/FPS: no se declara; el host sigue bloqueado por X11/llvmpipe.

## Decisión

Promover sólo con CI completo en verde. El coste de plumas sigue limitado por unidades
agregadas y el cambio de suelo reduce setters redundantes sin alterar geometría, textura,
iluminación, física o telemetría de motores.
