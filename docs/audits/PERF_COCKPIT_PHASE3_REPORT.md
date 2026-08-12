# Fase 3 — Auditoría y optimización de SubViewport del cockpit

## Alcance y ownership

Esta fase se limita a:

- `scripts/CockpitRenderer.cs`;
- `scripts/CockpitInstruments.cs`;
- el contrato `tools/tests/cockpit_subviewport_contract_test.sh`;
- este informe.

`ConstructionController.cs` fue inspeccionado, pero no modificado. La optimización
del preview del VAB queda para una fase con ownership explícito de Construction.

## Medición inicial

La inspección del runtime mostró dos familias independientes:

| Escena | Viewports | Resolución | Modo inicial | Actualización solicitada |
|---|---:|---:|---|---|
| Flight cockpit | 3 | 512×512 cada uno | `Always` | `ScreenPanel.QueueRedraw()` en cada `_Process` |
| Construction preview | 1 | 1024×1024 | `Always` | render continuo del preview 3D |

En Flight, `CockpitInstruments` se crea aunque la cámara no esté en modo cockpit.
Los tres `SubViewport` también permanecían en `Always` cuando `CockpitRenderer.Visible`
era `false`; ocultar el mesh no pausa los viewports porque viven como nodos hermanos
de la geometría del cockpit. El coste mínimo de los tres buffers de color RGBA8 es
aproximadamente 3 MiB, sin contar depth, historial del renderer ni overhead de Godot.

En Construction, el preview debe permanecer interactivo para rotación, encuadre y
picking 3D. Por eso esta fase sólo registra su coste y deja su `Always` sin cambios.

## Cambio aplicado

Flight conserva los tres viewports, sus texturas, materiales y `ScreenPanel` completos.
La única diferencia es el modo de actualización:

- al crear los instrumentos: `Disabled`;
- al entrar en `CameraController.IsCockpitView`: `Always`;
- al salir del cockpit: `Disabled` otra vez.

Al reentrar se mantienen los mismos paneles y se solicita `QueueRedraw()` para los tres.
No se destruyen texturas, no se cambia resolución, color, material, geometría ni la
fuente de telemetría. El cambio es reversible y no puede dejar una pantalla sin su
contenido por una ruta de carga distinta: el material sigue enlazado al mismo texture
RID y los paneles siguen presentes en el árbol.

El runtime emite marcadores de diagnóstico:

```text
PERF_COCKPIT stage=created viewports=3 size=512x512 update=disabled
PERF_COCKPIT stage=update_mode cockpit=true viewports=3 mode=always
PERF_COCKPIT stage=update_mode cockpit=false viewports=3 mode=disabled
```

## Validación

El contrato dedicado comprueba que:

- siguen existiendo exactamente tres pantallas de 512×512;
- el arranque usa `Disabled`;
- la entrada al cockpit restaura `Always`;
- los tres paneles continúan solicitando redraw cuando el cockpit está activo;
- Construction conserva su preview de 1024×1024 en `Always`.

Comandos de aceptación:

```bash
bash -n tools/tests/cockpit_subviewport_contract_test.sh
bash tools/tests/cockpit_subviewport_contract_test.sh
dotnet build ExosphereSimulation/ExosphereSimulation.csproj --no-restore --nologo -v quiet
dotnet build Exosphere.csproj --no-restore --nologo -v quiet
GODOT_BIN=... bash tools/visual_playtest.sh --cockpit --run-id phase3-cockpit --skip-build
```

El smoke debe mostrar los tres instrumentos y, en el log, la transición `disabled →
always`. La ausencia de una captura válida, un marcador `cockpit=true` o cualquiera de
las tres pantallas visibles es un fallo de esta fase, no un resultado aceptable.

## Riesgos visuales y límites

- La transición de modo ocurre en `_Process`; puede existir como máximo un frame de
  latencia al entrar o salir de la vista.
- Mientras el cockpit está inactivo, sus texturas conservan el último frame. Esto es
  intencional: al activarse se fuerza redraw de los tres paneles antes de evaluar el
  resultado visual.
- No se ha tocado el preview Construction. Su `SubViewport` sigue siendo un coste
  continuo y requiere una auditoría separada que respete el picking y la interacción.
- Esta fase no demuestra todavía una mejora de FPS total: sólo elimina trabajo de
  render cuando el usuario está fuera del cockpit. La mejora debe medirse con GPU/CPU
  profiler en una futura matriz exterior-vs-cockpit.

## Decisión

**Aprobado para integrar como optimización reversible de bajo riesgo en Flight.**
No se cambia el aspecto ni la resolución de los instrumentos. La promoción definitiva
de cualquier optimización del preview Construction queda pendiente de su propia
medición y validación de picking.
