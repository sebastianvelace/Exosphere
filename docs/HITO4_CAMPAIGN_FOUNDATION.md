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

Freedom 7 aparece como `READY`/`LISTA`: la variante Mercury-Redstone 3,
Alan Shepard y LC-5 ya existen y el botón lanza exactamente ese hardware. La
barrera permanece para las misiones siguientes: la campaña no puede sustituir
una variante faltante por Falcon, Starship ni un vehículo genérico.

## Friendship 7

La segunda entrada ya tiene definición, Mercury-Atlas 6 / spacecraft 13,
John Glenn y LC-14. Aparece `LOCKED`/`BLOQUEADA` hasta completar Freedom 7 y
entonces pasa a `READY`/`LISTA`. El perfil reproduce la arquitectura
*stage-and-a-half*, la inserción, tres órbitas, retrofire, reentrada y amerizaje.

La evidencia añade revoluciones acumuladas y persistibles. El escudo térmico,
centro aerodinámico y retención del anillo durante staging están definidos por
pieza, no por supuestos exclusivos de Starship. La aceptación completa se
documenta en `docs/HITO4_FRIENDSHIP7_PLAYABLE.md`.

## Verificación

Las pruebas puras cubren:

- carga y validación bilingüe de la definición;
- orden exacto de las 16 misiones;
- éxito con evidencia numérica completa;
- fallo irreversible por exceder 13,0 g;
- round-trip de máximos, fases y origen de downrange;
- recompensas y desbloqueos idempotentes;
- rechazo de progreso no finito y recompensas negativas;
- procedencia obligatoria de los datos Freedom 7 y Friendship 7;
- round-trip y evaluación de las tres órbitas de Friendship 7.

La aceptación adicional de Freedom 7 cubre masa/dimensiones, A-7, crew/sitio,
separación conservativa, paracaídas, amerizaje y una simulación headless
completa. El nominal reproducible obtiene 181,4 km, 2,25 km/s geocéntricos,
480,8 km de downrange, 913 s y 12,85 g.

Friendship 7 añade cierre de masas Atlas/Mercury, separación *stage-and-a-half*,
secuencia histórica, tres órbitas, retrofire, orientación térmica, paracaídas y
splashdown en una simulación headless completa.

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
- <https://www.nasa.gov/mission/mercury-atlas-6-friendship-7/>
