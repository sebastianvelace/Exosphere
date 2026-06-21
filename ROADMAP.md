# Exosphere Roadmap

Este es el roadmap vivo del proyecto. Los planes viejos `PLAN_MEJORAS.md`,
`PLAN_MEJORAS_R15.md` y `PLAN_MEJORAS_R16.md` fueron cerrados/retirados. La
auditoria tecnica de fisica vive en `PLAN_REALISM.md`; el proximo frente
visual vive en `PLAN_VISUAL_REALISM.md`.

## Estado Actual

Base tecnica cerrada en `main`:

- Builds .NET/Godot esperados: 0 warnings, 0 errores.
- `ExosphereSimulation.Tests` cubre gravedad, RK4, Kepler, radial/suborbital,
  rails-impact, motores, termica de escudo, aerodinamica, SOI, navegacion y VAB.
- Godot headless carga la escena principal y la escena de construccion.
- CI descarga Godot 4.6.3 mono, compila la capa Godot C#, corre smoke headless y
  mantiene un guard contra harnesses temporales commiteados.
- VAB V1.5 esta conectado al vuelo: catalogo data-driven, preview 3D, node
  picking click-to-attach, save/load de crafts, navegador de crafts y launch al pad.
- Starship/Super Heavy tiene malla procedural con diametro real de 9 m, hot-stage
  ring, grid fins, flaps, tiles windward, motores 33/6 visuales, acero procedural,
  charring termico, bordes de heat shield, patron de tiles, payload-door cues,
  seams longitudinales, pluma liftoff mas densa y Super Heavy separado con anillo
  expuesto/quemado.
- El entorno de lanzamiento tiene una primera pasada costera/industrial con
  caminos, relleno, juntas, bermas y detalles de deluge visibles desde pad.
- Ascenso [G] usa gravity turn mas realista y hot-staging en MECO.
- Reentry/EDL Starship esta validado por telemetria: belly-flop sostenido,
  flip-and-burn bajo y touchdown suave.
- Interplanetario incluye Hohmann, patched-conic SOI transitions, encounter
  prediction, marcador/readout de encuentro y readout de maniobra.

## Prioridad Inmediata

La siguiente etapa no debe abrir un sistema grande nuevo. Primero hay que subir la
fidelidad visual y asegurar que lo existente se pueda validar con capturas:

1. **Visual fidelity Starship/Super Heavy**
   - Primera pasada cerrada: acero inoxidable, weld lines, tile layout, heat-shield
     edge, soot/frost, vents, raceways, grid fins, flaps y engine bay.
   - Siguiente: close-up fino con referencias reales: proporciones de flaps/nariz,
     tiles mas finos, markings no intrusivos y variacion de acero.
   - Mejor separacion visual Ship/Booster despues de hot-staging.
   - Siguiente: hot-staging plume y pluma vacio mas diferenciadas.

2. **Reentry visual**
   - Plasma/shock layer mas fisico, ligado a heat flux y densidad atmosferica.
   - Brillo windward, trail ionizado, tiles que se oscurecen por temperatura y
     breakup mas legible si falla el escudo/orientacion.

3. **Entorno y camaras**
   - Pad costero ya tiene primera pasada visual.
   - Siguiente: iluminacion solar, exposicion, sky/atmosfera y camaras para que
     launch/orbit/reentry/cockpit se lean como escalas reales.

4. **Capturas de aceptacion**
   - Automatizar capturas con framebuffer real para pad, liftoff, Max-Q, staging,
     orbit/map, belly-flop reentry, flip-and-burn, touchdown/crash y cockpit.

## Sistemas Cerrados Que No Se Deben Rehacer Sin Motivo

- RK4/Kepler/on-rails y patched conics.
- Guardas radial/suborbital y destruccion por impacto.
- Heat-shield data-driven con orientacion de flujo.
- Ascenso [G] y EDL R13. Cualquier cambio debe preservar sus telemetrias.
- VAB catalog/assembly/export y picking actual.

## Pendientes Reales

### VAB / Construccion

- Menu principal dedicado.
- Gizmos de arrastre/rotacion para reposicionar piezas en la preview.
- Mejor feedback visual de nodos compatibles/incompatibles.
- Validacion visual de crafts guardados antes de launch.

### Visual Starship/Super Heavy

- Plan detallado: `PLAN_VISUAL_REALISM.md`.
- Capturas de aceptacion con framebuffer real.
- Engine-out real queda fuera de esta etapa porque rompe el contrato actual de
  una parte-motor fisica por etapa.

### Reentry Fisico/Visual

- Per-piece structural breakup.
- Perdida de control si falla una pieza critica.
- Lift aerodinamico y AoA real, no solo drag orientacion-dependiente.
- Atmosfera residual sobre 140 km para decaimiento orbital lento, si se decide
  priorizar realismo orbital fino.

### Interplanetario

- Tests de cruise muy largo.
- Transferencia lunar mas precisa que el Hohmann heliocentrico simplificado.
- Nodos de maniobra arrastrables con mouse.

### Gameplay

- Save/load de mision.
- Misiones/objetivos de progresion.
- Recursos de vida, energia, comunicaciones y termica conectados a fases reales.
- Fallos, damage consequences y recuperacion.

### CI / Visual Testing

- Captura PNG end-to-end en CI usando Xvfb.
- Comparacion minima de screenshots para detectar pantallas negras, UI rota o
  render sin nave.
- Mantener el guard anti-harness: no commitear `scripts/_*Shot.cs`,
  `scripts/*VerifyShot.cs`, `scenes/*VerifyShot.tscn` ni autoloads temporales.

## Orden Recomendado

1. Ejecutar `bash tools/ci_check.sh` antes de tocar visuales.
2. Cerrar el siguiente bloque visual real:
   - hot-staging flash/plume entre etapas;
   - startup/ramp de ignicion;
   - validacion de pluma de vacio contra captura orbital;
   - reentry visual avanzado solo en lo pendiente: nose/leading edges, capturas
     nominal/fallo y legibilidad HUD.
3. Agregar capturas de aceptacion reproducibles con matriz V0.5.
4. Mejorar camara/luz/atmosfera.
5. Recien despues volver a gameplay grande: misiones, save/load, recursos o
   engine-out real.
