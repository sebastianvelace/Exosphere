# Plan de rendimiento y simulación escalonada — fase 45

Estado: plan preparado después de cerrar la corrección de HUD, salto `J` y captura
Starbase de la fase 44. No activa todavía ningún modo de física reducida.

Fecha: 2026-08-15

## Diagnóstico que justifica la fase

La telemetría actual separa correctamente el coste de física del coste de presentación:

- En el host llvmpipe, el scheduler se mantiene en el orden de milisegundos por frame,
  mientras que el framebuffer llega a aproximadamente un segundo por frame en la escena
  Earth completa.
- El probe de render ya atribuyó el cuello dominante al sky atmosférico; ocultarlo reduce
  drásticamente el coste, pero no es una solución jugable porque elimina el cielo.
- La flota sigue conservando física completa sólo donde la política actual lo exige, pero
  aún falta una política explícita y verificable de interés para sistemas de gameplay.
- Un presupuesto de catch-up ya existe como experimento, pero permanece desactivado porque
  todavía hay que demostrar paridad de eventos, staging, docking, motores, EDL y sistemas
  de nave bajo deuda temporal.

La prioridad de esta fase es quitar trabajo que no cambia el estado observable del jugador.
No se aceptará omitir posiciones, impactos, calentamiento, SOI, comandos o eventos sólo para
obtener un número menor de FPS.

## Objetivo de aceptación

Con una matriz reproducible de pad, liftoff, órbita, mapa, transferencia, reentrada y VAB:

1. reducir el tiempo de llegada al primer frame jugable y los picos de carga de entrada;
2. reducir draw calls, objetos visibles y trabajo del sky/VFX/UI sin degradación visual amplia;
3. reducir el número de naves en `FullPhysics` cuando están lejos y sin eventos próximos,
   manteniendo una trayectoria equivalente al llegar al siguiente deadline físico;
4. mantener el coste por frame de la nave activa y de cualquier contacto próximo dentro del
   presupuesto de 60 Hz en una máquina GPU real de referencia;
5. conservar `0 warnings / 0 errors`, toda la suite xUnit, los contratos y las capturas
   físicas de `CAUGHT`, touchdown, staging y docking.

Los números de llvmpipe se usarán para comparar cambios dentro del mismo host, nunca para
declarar que el juego alcanza 60 FPS en hardware real. Cada resultado debe conservar
`machine`, renderer, resolución, calidad, warm-up, muestras y commit.

## Política propuesta de interés físico

La política se implementará primero detrás de `SimulationInterestPolicy` y con el modo nuevo
desactivado por defecto. Las bandas son una propuesta inicial; sus radios y deadlines deben
calibrarse con el benchmark y no convertirse en constantes mágicas sin evidencia.

| Tier | Criterio inicial | Trabajo permitido | Wake-up obligatorio |
|---|---|---|---|
| `Active` | nave pilotada, seleccionada o controlada por misión | 6-DoF, motores, aero, térmica, soporte vital y contactos a paso normal | cualquier frame de control |
| `Proximity` | cerca de la nave activa, del cuerpo de aterrizaje o de otra nave con interacción | física reducida sólo si el error acotado está probado; contactos y docking a resolución completa | distancia, colisión, docking, atmósfera o comando |
| `EventDriven` | órbita lejana sin contacto ni comando pendiente | propagación Kepler/rails hasta el deadline más próximo; persistir recursos y eventos | periapsis/apoapsis/SOI, burn, eclipse, blackout, thermal deadline, misión |
| `Dormant` | objeto no visible, no seleccionado y sin deadline cercano | snapshot persistente y eventos discretos; no destruir la nave ni perder recursos | selección del jugador, SOI, evento de campaña, comando, proximidad |

Reglas inviolables:

- nunca poner en `EventDriven` o `Dormant` una nave con thrust, control pendiente,
  contacto, docking, reentrada atmosférica, breakup estructural o deadline dentro del paso;
- `Dormant` no significa eliminar el objeto: se conserva masa, combustible, estado orbital,
  temperatura, salud, referencia de cuerpo y eventos pendientes;
- el jugador, `ActiveVessel`, una nave en captura Starbase y cualquier par de docking tienen
  prioridad sobre la reducción de trabajo;
- la promoción/demotion debe ser determinista y auditable en telemetría;
- los cambios de tier se comparan por época física, no por cantidad de ticks o tiempo de pared.

## Despliegue de agentes y ownership

Cada agente trabaja en un worktree o rama aislada y entrega un commit único más un informe.
El integrador no acepta dos agentes escribiendo el mismo archivo de runtime en paralelo.

| Agente | Ownership | Entrega | Dependencias |
|---|---|---|---|
| P0 — perfilado y gates | `tools/perf/`, `tools/tests/`, fixtures y documentación de baseline | benchmark de entrada con warm-up, p50/p95/p99, allocations, draw calls, nodos, scheduler y artefactos JSON; contrato que rechace logs incompletos | ninguna; primero |
| P1 — interés/scheduler | `ExosphereSimulation/Universe.cs`, nueva política CPU y tests de scheduler | tiers detrás de flag, deadlines, wake-up reasons, deuda temporal y paridad contra FullPhysics; no tocar Godot | baseline P0 |
| P2 — sistemas de nave | `scripts/SystemsController.cs`, cadencias explícitas y tests de energía/consumibles/térmica/comms | integrar sólo el intervalo simulado comprometido; actualizar por deadline cuando sea seguro; prueba de acumulación y blackout | contrato de P1; no promover tier sin paridad |
| P3 — render/sky | `scripts/PhaseLightingController.cs`, sky/atmosphere y su probe | cache/invalidation, calidad escalonada opt-in, reducción de trabajo fuera de cámara; capturas Earth/Mars/Venus/terminador | baseline P0; no cambiar física |
| P4 — escena/VFX/UI | `scripts/LaunchPadController.cs`, `VesselRenderer.cs`, VFX, HUD y creación diferida | lazy loading de pad/terreno/VFX, pooling sólo con ownership claro, visibilidad/culling medido; ningún cambio de telemetría física | P0; coordinar con P3 por contrato de nodos |
| P5 — QA visual y fuzz | `tools/visual_playtest.sh`, nuevos contratos y tests de regresión | matriz de escenas, invariantes NaN/clipping/neonGreen, captura de transiciones de tier y pruebas de reentrada/docking | todos los candidatos |
| P6 — revisión de integración | sólo merges, auditoría y `ci_check.sh` | A/B de cada commit, revisión de diff, decisión promote/revert, informe final | P0–P5 |

P1 y P2 no pueden activar una reducción de física sólo porque el benchmark mejore. P3 y P4
no pueden ocultar nodos necesarios para `CAUGHT`, contactos, telemetría o depuración. P5 tiene
autoridad para bloquear la promoción por una regresión visual o por una captura que no esté
respaldada por estado físico.

## Secuencia de trabajo

### Paso 0 — baseline congelado

P0 ejecuta el mismo commit en una matriz mínima:

- Earth pad en frío, liftoff, Max-Q, hot-stage y separación;
- órbita con una nave activa y una flota de rails;
- mapa y salto `J` a Mars/Venus;
- transferencia con blackout y nave no seleccionada;
- reentrada EDL con `ARMED → CAUGHT`;
- VAB y apertura del nivel.

Se guardan al menos 30 muestras después de warm-up y tres repeticiones. Las conclusiones
requieren la mediana y p95; una única corrida no es evidencia.

### Paso 1 — instrumentación antes de optimizar

Todos los dispatches deben publicar:

`vessel_id`, `tier`, `reason`, `sim_start`, `sim_end`, `deadline`, `substeps`,
`position_error_bound`, `allocation_bytes`, `wall_ms` y `wake_reason`.

El log debe permitir demostrar que el tiempo simulado procesado coincide con el reloj y que
ningún deadline fue saltado. Si una métrica no se puede correlacionar con una escena y una
época, el cambio no pasa a la fase de promoción.

### Paso 2 — paridad CPU

Para cada fixture se ejecutan dos universos con el mismo input:

- referencia: FullPhysics sin presupuesto;
- candidato: política por tier, deuda y wake-ups.

Se comparan posición, velocidad, orientación, velocidad angular, masa, propelente,
temperatura, orbital state, contactos, docking, motores y eventos. Las tolerancias se fijan
por magnitud física y se documentan; no se relajan para esconder divergencias.

Casos obligatorios: engine ignition, engine-out, staging, docking/undocking, periapsis dentro
de atmósfera, SOI transition, eclipse/solar visibility, thermal deadline, surface contact,
Starbase dual-pin catch y `JumpToBody`.

### Paso 3 — presentación y carga inicial

El renderer puede diferir trabajo no observable: construir terreno de otro cuerpo, VFX fuera
de cámara, nodos de la plataforma lejana, HUD secundario y muestras del sky que no cambien
la silueta, exposición o terminador dentro del error visual acordado. Cada diferimiento debe
tener un evento de carga y una captura antes/después.

No se hará pooling global ni se moverán nodos entre escenas hasta medir que la creación y la
liberación son realmente un hot path. El objetivo es reducir el pico de entrada, no esconder
una fuga de memoria.

### Paso 4 — promoción gradual

Orden permitido:

1. instrumentación sin cambio de estado;
2. optimizaciones render/VFX opt-in;
3. cadencias de sistemas no críticos con paridad;
4. rails/event-driven sólo para fixtures cubiertos;
5. una bandera de desarrollo para comparar FullPhysics y candidato en vivo;
6. promoción por tier individual, nunca toda la flota a la vez.

La configuración oficial seguirá con el modo nuevo apagado hasta que P6 firme el informe.

## Gates de calidad

Un candidato se rechaza si ocurre cualquiera de estos casos:

- suite xUnit, build, smoke o contrato con fallo;
- NaN, infinito, masa/energía/propelente negativo o deuda temporal que no se drena;
- divergencia física fuera de la tolerancia documentada en cualquier fixture;
- pérdida de un evento de staging, docking, SOI, impacto, calentamiento, engine-out o catch;
- `CAUGHT` visual sin `IsCaught`, brazos cerrándose sin dos pines o nave desaparecida;
- clipping amplio, exposición inestable, terminador/limbo degradado, estrellas ocultas o
  `neonGreenFrac` superior al baseline acordado;
- mejora de un benchmark aislado que empeore el p95 de entrada o el frame activo.

Objetivos cuantitativos iniciales, sujetos a la medición de P0:

- al menos 25% menos tiempo p95 hasta el primer frame jugable;
- al menos 25% menos allocations administradas durante la entrada al nivel;
- al menos 20% menos draw calls/objetos visibles en la escena Earth de referencia;
- sin aumento superior al 5% en p95 de `Universe.Tick` para la nave activa;
- reducción medible de dispatches FullPhysics de naves no activas sólo cuando la paridad CPU
  y todos los wake-ups pasen.

Estos objetivos son de promoción, no permiso para recortar calidad a ciegas. Si el hardware
de referencia no está disponible, se reporta el resultado como parcial y se conserva la
bandera experimental.

## Comandos y artefactos obligatorios

Cada agente debe ejecutar, con un `run-id` propio:

```bash
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-restore
dotnet build Exosphere.csproj --no-restore
bash tools/tests/gameplay_regression_contract_test.sh
bash tools/visual_playtest.sh --smoke --run-id phase45-<agent> --skip-build
```

P0/P3/P4 añaden sus probes reproducibles; P5 ejecuta además `--ascent`, `--edl` y la matriz
de escenarios. Los artefactos deben vivir bajo `/tmp/exo_phase45_<agent>/` y no se commitea
ningún autoload temporal, captura ni `.godot/`.

## Decisión esperada

La fase termina con una tabla por candidato: `PROMOTE`, `KEEP_EXPERIMENTAL` o `REJECT`, con
commit, benchmark, diferencia física, diferencia visual, coste de memoria y riesgos. La
primera promoción recomendada es render/presentación diferida y cadencias de sistemas con
paridad; la política `EventDriven/Dormant` sólo se promueve después de que los fixtures de
deadlines y wake-ups sean completos.
