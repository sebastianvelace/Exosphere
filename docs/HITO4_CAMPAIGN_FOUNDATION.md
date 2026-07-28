# Hito 4 — Fundación de campaña histórica

La campaña histórica ya no es una lista estática del menú. Su primera capa
ejecutable está separada en contratos puros y un adaptador Godot:

- `MissionDefinition` carga objetivos, límites, anomalías, recompensas, hardware
  fijado y fuentes desde `data/missions`.
- `MissionDirector` acumula evidencia sin perder máximos históricos entre frames
  o partidas guardadas.
- `MissionEvaluator` es una función pura que genera un debrief numérico.
- `CampaignService` gestiona prerrequisitos, completados, desbloqueos y un ledger
  de recompensas idempotente.
- `CampaignRuntime` es el único adaptador de vuelo. Lee la física autoritativa y
  no recalcula criterios en la interfaz.
- `CampaignDebriefPanel` solo presenta el resultado producido por el evaluador.

`SaveGameV2.Mission` conserva máximos, fases alcanzadas y la dirección de
superficie del sitio de lanzamiento. Esto permite continuar un vuelo sin
reiniciar el downrange. `SaveGameV2.Campaign` conserva perfil, desbloqueos,
misiones completadas y cada entrada del ledger.

## Secuencia visible

`data/campaigns/historical_nasa_spacex.json` contiene las 16 misiones del plan,
desde Freedom 7 hasta Starship Flight 12/V3. Una entrada solo es jugable cuando
existen simultáneamente:

1. su `MissionDefinition`;
2. la variante histórica exacta;
3. el sitio de lanzamiento;
4. sus prerrequisitos de campaña.

La interfaz distingue `READY`, `LOCKED`, `VEHICLE PENDING` y `PLANNED`.
No sustituye una variante faltante por otro cohete.

## Freedom 7

`data/missions/freedom7_1961.json` fija:

- Mercury-Redstone 3 / spacecraft 7;
- Alan B. Shepard Jr.;
- Cape Canaveral LC-5;
- misión suborbital sin órbitas;
- apogeo, velocidad, downrange, duración, amerizaje, supervivencia y carga
  máxima como evidencia del debrief.

NASA publica 187,42 km de altitud, 487,26 km de rango y 15:22 de duración en el
*Historical Data Book*. La página moderna de la misión publica 116,5 millas,
303 millas, 5.134 mph, Max-Q de 580 psf y 11 g. Exosphere conserva esos valores
como evidencia histórica y usa un corredor de aceptación más ancho y
explícitamente `derived`.

Freedom 7 aparece como `VEHICLE PENDING` hasta que se entregue la variante
Mercury-Redstone 3, el astronauta y LC-5. Esta barrera es deliberada: la campaña
no puede lanzar Falcon, Starship ni un vehículo genérico en lugar del hardware
que realmente voló.

## Verificación

Las pruebas puras cubren:

- carga y validación bilingüe de la definición;
- orden exacto de las 16 misiones;
- éxito con evidencia numérica completa;
- fallo irreversible por exceder 12,5 g;
- round-trip de máximos, fases y origen de downrange;
- recompensas y desbloqueos idempotentes;
- rechazo de progreso no finito y recompensas negativas;
- procedencia obligatoria de los datos Freedom 7.

La captura reproducible del modal usa:

```bash
CAPTURE_MENU_MODAL=campaign \
CAPTURE_MENU_OUTPUT=/tmp/exosphere_campaign.png \
xvfb-run -a -s '-screen 0 1920x1080x24' \
  "$GODOT_BIN" --path . --resolution 1920x1080 \
  --rendering-method gl_compatibility \
  --script res://tools/capture_menu.gd
```

## Fuentes primarias

- <https://www.nasa.gov/mission/mercury-redstone-3-freedom-7/>
- <https://www.nasa.gov/wp-content/uploads/2023/04/sp-4012v2.pdf>
