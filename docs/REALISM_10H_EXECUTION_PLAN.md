# Programa de realismo y jugabilidad — ejecución multiagente (10 h)

**Estado:** iniciado en `codex/realism-program`
**Objetivo:** elevar Exosphere hacia una simulación espacial físicamente coherente,
visualmente creíble y jugable, sin ocultar aproximaciones ni romper el flujo existente.

Este documento es el contrato operativo de la campaña. Cada tranche debe dejar una evidencia
reproducible, una nota de límites y un commit pequeño. Las tareas paralelas no pueden editar el
mismo archivo sin que un agente revisor lo autorice; el agente raíz integra únicamente cambios
con pruebas verdes.

## Reglas de coordinación

1. La rama de trabajo es `codex/realism-program`, basada en `origin/main` y con la serie
   atmosférica ya verificada reaplicada. El WIP anterior del usuario permanece guardado en un
   stash reversible y no se descarta.
2. Cada agente declara al inicio: alcance, archivos permitidos, hipótesis físicas, pruebas y
   criterio de aceptación. Si necesita subagentes, debe dividirlos por archivos o por tipo de
   evidencia (CPU, Godot, documentación), nunca por cambios solapados.
3. No se mezclan artefactos de captura/autoload temporales (`scripts/_*`, cambios transitorios en
   `project.godot`) en commits. Las capturas van a `/tmp` con `--run-id` estable.
4. Un commit debe responder a una sola pregunta física o de producto. El mensaje usa
   `type(area): outcome`; antes del commit se ejecutan `git diff --check`, build y las pruebas
   afectadas. El agente raíz hace revisión defect-first del diff.
5. Las fuentes de NASA, SpaceX, FAA, ESA o artículos técnicos se registran con URL, fecha de
   consulta y clasificación: medido, publicado pero aproximado, o hipótesis de juego.

## Tramos y entregables

| Tramo | Frente | Entregable verificable |
|---|---|---|
| 0 | Baseline | árbol limpio reproducible, inventario de WIP, build y conteo de tests |
| 1 | Auditoría | informes de física orbital, atmósfera, motores, EDL, Starbase, UI/VAB y rendimiento |
| 2 | E2E | telemetría y capturas del flujo menú→VAB→pad→órbita→EDL, con fallos clasificados |
| 3 | Datos | tabla de parámetros y fuentes para Tierra/Marte/Venus, Raptor 3, Falcon 9 y New Glenn |
| 4 | Perfil óptico | proveedor común para densidad, transmitancia, refracción y scattering; parity tests |
| 5 | Aerosoles | `AerosolClimateState`, AOD550/Ångström, variación latitudinal/estacional y cache invalidation |
| 6 | Termósfera | extensión coordinada de geometría y LUTs sólo después de la paridad del tramo 4 |
| 7 | Propulsión | masas, Isp, throttling, gimbal, arranque, separación, hot-stage y giro planetario |
| 8 | EDL | thermal load, plasma, belly-flop, flip burn, restart, grid fins, catch y abortos |
| 9 | Visuales | nave, motores/plume, humo desde t=0, materiales, exposición, estrellas y primera persona |
| 10 | Starbase | pad, tower, chopsticks, GSE, tanques, carreteras, edificios, luces y frame coherente |
| 11 | VAB | familias de cohetes, carga útil, stages, pruebas de motores, robótica, misiones y guardado |
| 12 | UX | menú inicial, HUD, tutorial, controles, accesibilidad y feedback de estados |
| 13 | Rendimiento | arranque sin stalls, cachés, precalentamiento incremental y presupuestos CPU/GPU |
| 14 | Verificación | xUnit, invariantes, Godot smoke, matriz framebuffer, E2E y revisión defect-first |
| 15 | Integración | documentación, commits, push/PR, checklist de reproducción y backlog residual |

## Matriz mínima de pruebas físicas

- Conservación de masa, energía y momento durante integración RK4 y separación de etapas.
- T/W, consumo, Isp, presión dinámica `q`, gravedad efectiva y velocidad de escape por cuerpo.
- Continuidad y monotonicidad de presión, temperatura, densidad, profundidad óptica y
  transmitancia; canales finitos y no negativos.
- Geometría esférica: horizonte, refracción, sombra planetaria, rotación terrestre y marcos local
  up/east/north.
- Reentrada: flujo térmico, carga `q`, actitud, flip, encendido/reencendido y contacto seguro;
  ningún aterrizaje puede declararse correcto sólo por distancia.

## Matriz visual y E2E

Cada ejecución usa un `--run-id` único y conserva `run-summary.txt`, log y PNGs. Como mínimo se
capturan suelo día/amanecer/atardecer/noche, 10/30/80/140/400 km, eclipse, cockpit, lanzamiento,
separación, reentrada y aterrizaje. Los gates son `ATMOSPHERE_OK`, ausencia de `GAP`/`FALLBACK`,
telemetría de progreso y presupuesto de frame documentado. Si el backend framebuffer no está
disponible, se registra el bloqueo ambiental y se ejecutan igualmente build, smoke headless,
pruebas CPU y revisión de artefactos previos; no se marca el gate visual como aprobado.

El E2E jugable debe cubrir:

1. abrir menú y seleccionar misión/vehículo;
2. construir una pila válida y añadir una carga útil;
3. ejecutar prueba de motor y corregir un fallo de configuración;
4. lanzar, realizar gravity turn, hot-stage y alcanzar una órbita estable;
5. separar etapas, controlar reentrada, reiniciar motores y aterrizar/capturar;
6. guardar, recargar y comprobar que telemetría, misión y vehículo permanecen consistentes.

## Registro de commits

El agente raíz mantiene una tabla con `commit`, alcance, pruebas y riesgo residual. Un tranche no
se considera terminado si el commit no es reproducible desde una rama limpia. Los commits de
documentación y de tests pueden ser independientes, pero deben enlazar la evidencia que justifican.

## Criterio de salida de la campaña

La campaña termina sólo cuando todos los tramos que se hayan implementado tienen tests y
documentación, el E2E no deja fallos críticos conocidos, los gates visuales están aprobados o
explícitamente bloqueados por el entorno, y el backlog de limitaciones físicas queda priorizado.
“Más realista” no se acepta como criterio subjetivo: cada mejora debe indicar qué variable física,
qué observación visual o qué interacción de jugador cambió y cómo se verificó.
