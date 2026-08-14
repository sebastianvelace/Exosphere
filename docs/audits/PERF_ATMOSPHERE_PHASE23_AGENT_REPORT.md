# Fase 23 P5 — auditoría de atmósfera, workers y validación espectral

Fecha: 2026-08-14  
Base de trabajo: `39b9eaf`  
Alcance: `scripts/SkyController.cs`, oráculo/comparador espectral, contratos atmosféricos y este informe.

## Decisión

Se conserva el renderer RGB de orden 4 como ruta oficial. El orden 5 permanece experimental y
offline; no se conectó al shader ni al frame loop. La validación no justifica promoverlo: Earth y
Mars cumplen finitud, no negatividad, monotonía y O4 no peor que O3, pero Venus no cumple ese
último criterio con la reconstrucción actual.

Se acepta únicamente una mejora de instrumentación/cache de bajo riesgo:

- el cache key del LUT incluye ahora cuerpo/perfil, modo oficial u experimental, resolución y
  pasos de integración de las tablas;
- cancelación y finalización reportan cola, bytes actuales frente a estimados y tiempos sin
  bloquear el hilo principal.

No se cambió la física, la resolución oficial, el orden 4 ni ningún JSON de Venus/Mars.

## Métricas observadas

### Worker RGB runtime

La corrida visual atmosférica fue detenida después de 8/20 capturas por la instrucción de cerrar;
por ello no se declara `ATMOSPHERE_OK` ni se usan sus métricas parciales como gate visual completo.
Su telemetría sí alcanzó una construcción Earth completa:

| Métrica | Valor observado | Interpretación |
|---|---:|---|
| estado | `queued → running → completed` | no hubo fallo ni cambio de perfil |
| cola | 3.1 ms | desde `queued` hasta el inicio del worker |
| CPU del worker | 9,831.9 ms | `Stopwatch` dentro de la construcción |
| elapsed worker observado | 11,669.4 ms | incluye espera hasta que el hilo principal hizo poll del task |
| payload estimado/peak | 362,496 B | cota determinista de vectores CPU simultáneos |
| payload producido/retained | 344,064 B | transmitancia + atlas angular conservados |
| upload estimado | 229,376 B | dos texturas `RGBA32F` que se suben a Godot |
| orden runtime | 4 | `rgb-ms-order4-interactive-v21` |

La composición de bytes es reproducible: transmitancia `64×96×3×8 = 147,456 B`, global
`32×24×3×8 = 18,432 B` y atlas angular
`16×8×8×8×3×8 = 196,608 B`. El global es temporal y se libera después de construir el atlas;
por eso `retainedCpuBytes` es 18,432 B menor que `peakBytes`. El upload es
`64×96×4×4 + 16×512×4×4 = 229,376 B`. Estos valores son contabilidad de payload, no una
medición de RSS ni de VRAM.

El smoke posterior, ya con el cambio aplicado, reprodujo el mismo contrato con variación normal
del host: cola 2.8 ms, CPU 7,931.3 ms, elapsed 8,082.4 ms, retained 344,064 B, peak 362,496 B y
upload 229,376 B.

### Cancelación al salir

La ruta está instrumentada y verificada estáticamente:

1. `_ExitTree()` llama `CancelAtmosphereLutBuild("exit_tree")`.
2. La cancelación llama `CancellationTokenSource.Cancel()` y emite
   `state=cancel_requested`, con `queueMs` y `bytes=producidos/estimados`.
3. El worker comprueba el token entre fases; el poll normal puede emitir
   `state=canceled` y libera el `CancellationTokenSource`.
4. En salida no se espera ni se hace `Wait`/join en el hilo principal; la liberación pendiente
   usa una continuación asíncrona.

La ejecución visual corta no observó dinámicamente `cancel_requested`: el worker terminó antes
de que el smoke saliera (`SMOKE_OK`). Por tanto, el informe no afirma una latencia de cancelación
ni una observación dinámica de `state=canceled`; sólo afirma que el contrato está implementado,
que no bloquea la salida y que el harness limpió sus recursos. La prueba dinámica específica de
interrupción queda pendiente para una corrida posterior si se necesita ese dato.

### Separación oráculo/runtime

- `SkyController` sólo usa las constantes de orden oficial/experimental; no llama
  `SpectralAtmosphereOracle.Build` ni `Evaluate` desde `_Process` o el worker.
- El runtime construye LUT RGB con `MultipleScatteringMaxOrder = 4`.
- `tools/SpectralValidation` construye el oráculo de 9 bandas fuera del juego y escribe CSV.
- El harness puede registrar `spectralOrder=5`, energía y RGB de referencia, pero esos campos son
  telemetría de validación, no una ruta de render.
- Earth, Mars y Venus están marcados `provenance=reconstructed`; no se presentan como datos
  espectrales medidos.

## Validaciones ejecutadas

### Quick-check atmosférico

Comando:

```bash
GODOT_BIN="...Godot_v4.6.3-stable_mono_linux.x86_64" \
  bash tools/atmosphere_quick_check.sh
```

Resultado: **PASS**, 81/81 tests atmosféricos, build de simulación y juego con 0 warnings/0
errores, smoke Godot PASS, 9,869 ms.

### Validación espectral

Comando:

```bash
OUT_DIR=/tmp/exo_phase23_spectral bash tools/spectral_validation.sh
```

Resultado del comparador reducido:

| Cuerpo | O3 abs medio | O4 abs medio | error cromático O4 | finito/monótono | O4 no peor |
|---|---:|---:|---:|---|---|
| Earth | `1.2211e-4` | `1.2063e-4` | `6.2112e-2` | PASS / PASS | PASS |
| Mars | `1.8436e-3` | `1.8433e-3` | `6.8760e-3` | PASS / PASS | PASS |
| Venus | `3.5986e-3` | `8.8438e-3` | `3.9120e-2` | PASS / PASS | FAIL |

El comando terminó con código 0 porque la validación de seguridad exige finitud y monotonía; la
decisión de promoción se reporta separadamente y sigue siendo `order4-official-order5-diagnostic`.
Los CSV quedan en `/tmp/exo_phase23_spectral/`.

### Contrato P5 y build final

```bash
bash -n tools/tests/atmosphere_phase23_contract_test.sh
bash tools/tests/atmosphere_phase23_contract_test.sh
dotnet build Exosphere.csproj --no-restore --nologo -v quiet
```

Resultado: contrato **PASS** y build final **0 warnings / 0 errores**.

La matriz `--atmosphere` no se presenta como PASS completo: fue cancelada voluntariamente tras
8/20 capturas para evitar otra corrida larga en llvmpipe. El harness informó `project.godot
restored`; el smoke final también terminó con `SMOKE_OK` y limpieza correcta.

## Archivos modificados

- `scripts/SkyController.cs`
- `tools/tests/atmosphere_phase23_contract_test.sh`
- `docs/audits/PERF_ATMOSPHERE_PHASE23_AGENT_REPORT.md`

Los cambios no relacionados que ya estaban en el worktree se conservaron intactos.
