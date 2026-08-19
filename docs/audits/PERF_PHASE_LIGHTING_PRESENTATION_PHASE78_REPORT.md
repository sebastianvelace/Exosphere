# Fase 78 — muestra física de PhaseLightingController

Fecha: 2026-08-18  
Área: `scripts/PhaseLightingController.cs`, `tools/tests/render_cadence_phase23_contract_test.sh`

## Hallazgo

La iluminación de fase ya limitaba la integración costosa de transmitancia directa a 10 Hz,
pero aún consultaba por frame cuerpo dominante, altitud, densidad, velocidad, flujo térmico y
geometría solar. También pedía la velocidad de superficie una segunda vez para calcular la
componente radial durante reentrada.

## Cambio implementado

`SampleLightingState` actualiza esas entradas a 20 Hz y fuerza una muestra cuando cambia la nave
o el universo. Las escrituras dirty-gated del Environment y la luz siguen ejecutándose por
frame para mantener la presentación continua. La velocidad de superficie se reutiliza para el
flujo térmico y para la velocidad radial.

Se conservan:

- el blend atmósfera/espacio;
- los gates de reentrada y sus factores por fase;
- `DirectSolarTransmittance` a 10 Hz con invalidación por cuerpo, horizonte, altitud y dirección;
- cockpit ambient boost, glow reduction y SolarVisibility;
- la propiedad de que PhaseLighting sea el único escritor de energía ambiental.

## Reducción estructural

En un frame rate de 60 Hz:

| Trabajo | Antes | Ahora |
|---|---:|---:|
| cuerpo/altitud/óptica para iluminación | 60/s | 20/s |
| density/velocidad/heat flux | hasta 60/s | hasta 20/s |
| lecturas duplicadas de velocidad por muestra | 2 | 1 |
| adaptación de Environment/DirectionalLight | 60/s | 60/s |
| transmitancia directa | hasta 10/s | hasta 10/s |

La antigüedad máxima de la entrada física es 50 ms; no se cambia el solver ni el scattering.

## Verificación

- contrato `render_cadence_phase23_contract_test.sh`: PASS;
- la suite completa, builds, xUnit y smoke tests de Godot son el gate de integración.

No se publica una ganancia de FPS: el framebuffer reproducible continúa bloqueado por X11/Xvfb.
