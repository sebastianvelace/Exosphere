# Baseline de rendimiento — fase 45

Fecha: 2026-08-15

Estado: `PARTIAL / BLOCKED` para la nueva matriz framebuffer. La rama CPU y la
telemetría histórica sí son utilizables; no se declara una ganancia de FPS con
llvmpipe ni se promueve una calidad visual a partir de una corrida incompleta.

## Resultado ejecutivo

El síntoma de “se traba al iniciar el nivel” no corresponde al scheduler CPU en
la evidencia disponible. La separación existente muestra milisegundos de
`Universe.Tick` frente a aproximadamente un segundo por frame del framebuffer
completo en el host llvmpipe. El cuello dominante está fuera de la física, con
el sky atmosférico como principal sospechoso medido.

Esto no descarta picos de carga inicial, VFX, creación de nodos o drivers; sólo
evita atribuirlos al sistema equivocado. La matriz completa debe repetirse en un
host con framebuffer X11/GPU válido.

## Datos comparables conservados

| Fuente | Escenario | Señal | Resultado | Interpretación |
|---|---|---:|---:|---|
| fase 37 | Earth/playtest | `scheduler_ms` mediana | `4.824 ms` | CPU de scheduler, no frame completo |
| fase 37 | Earth/playtest | `frame_ms` mediana | `~1,011 ms` | llvmpipe/callback completo |
| fase 39 | sky oficial `0.60` | CPU/GPU render mediana | `1,098.077 / 1,102.228 ms` | baseline visual diagnosticado |
| fase 39 | sky experimental `0.25` | CPU/GPU render mediana | `788.115 / 795.604 ms` | mejora diagnóstica, sin gate visual completo |
| fase 40 | sky oficial `0.60` | CPU/GPU render mediana | `1,101.086 / 1,105.361 ms` | repetición del mismo backend |
| fase 40 | sky experimental `0.25` | CPU/GPU render mediana | `940.271 / 944.074 ms` | variación del host; no promoción |
| fase 44 | EDL Starbase | `PERF_FRAME` | `~0.84–0.96 s` | framebuffer llvmpipe durante captura |
| fase 44 | EDL Starbase | scheduler | `~1.6–9.9 ms` | física pequeña frente a presentación |

Los valores de fases 37–40 son históricos y sólo comparables dentro de la misma
configuración de host/renderer; no sustituyen una medición nueva de la fase 45.

## Preflight y bloqueo de framebuffer

Los contratos estáticos de render/sky y la compilación Godot pasan. La nueva A/B
de sky no pudo comenzar porque el host no pudo crear un display X11/Xvfb; no hubo
`PERF_FRAME`, `PERF_GPU`, captura ni `SMOKE_OK`. La ejecución B no se lanzó después
de fallar el preflight A. El harness temporal se limpió y `project.godot` quedó
restaurado.

Esta es una limitación de infraestructura, no una autorización para cambiar el
renderer: el override `sky_quality_low` permanece `KEEP_EXPERIMENTAL`.

## Baseline que falta en hardware válido

P0 debe repetirse con tres corridas por escenario, warm-up explícito y al menos
30 muestras posteriores:

- primer frame jugable y entrada al nivel;
- Earth pad, liftoff, Max-Q, hot-stage y separación;
- órbita con nave activa y flota rails;
- mapa y salto `J` a Mars/Venus;
- transferencia con blackout y nave no seleccionada;
- EDL con `ARMED → CAUGHT`;
- VAB.

Cada artefacto debe conservar commit, resolución, renderer, driver, calidad,
`frame_ms`, `scheduler_ms`, `PERF_GPU`, allocations, objetos, primitivas, draw
calls y memoria de vídeo. Sin esos campos el resultado es diagnóstico parcial.

## Gate de la fase

Este documento permite continuar con la política CPU pura de interés y con
auditorías opt-in de render, pero bloquea:

- activar `EventDriven`/`Dormant` en `Universe`;
- promover `sky_quality_low` o cambiar la cadencia del sky;
- declarar 60 FPS o una reducción de stutter en hardware real.

La promoción requiere completar el baseline framebuffer y luego comparar cada
candidato contra el mismo commit base con los gates de
`PERF_MULTI_AGENT_PHASE45_PLAN.md`.
