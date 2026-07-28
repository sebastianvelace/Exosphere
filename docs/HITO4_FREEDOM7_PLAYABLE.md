# Hito 4 — Freedom 7 jugable

Freedom 7 es la primera misión histórica completa del catálogo. El flujo del
menú construye la variante fechada
`mercury-redstone3-freedom7-1961-05-05`, la coloca en
`cape_canaveral_lc5`, embarca a `alan-b-shepard-jr` y activa el perfil
`mercury-redstone3-suborbital`.

## Vehículo y procedencia

El preset conserva la masa de lanzamiento publicada por NASA de 29.931 kg y la
altura completa de 25,3 m. El stack contiene torre de escape, spacecraft 7,
paquete de retrocohetes, anillo de separación, adaptador, etapa de propelente y
un A-7. El empuje al nivel del mar es el valor NASA de 346.944 N.

Los valores que las fuentes citadas no publican —Isp, empuje de vacío,
distribución de masa, carga detallada, retrocohetes, coeficientes aerodinámicos
y CdA del paracaídas— permanecen `estimated` o `calibrated` en
`data/provenance/mercury_redstone3_freedom7_vehicle_1961.json`.

La velocidad histórica se evalúa como velocidad geocéntrica, separada de la
velocidad relativa a la atmósfera. Ambas magnitudes se acumulan y persisten en
la evidencia de misión.

## Secuencia física

El perfil no teletransporta el vehículo ni la cápsula:

1. encendido y pitch program con el A-7;
2. MECO a T+142 s;
3. separación del launch vehicle a T+152,5 s;
4. expulsión de la torre como vessel independiente a T+154 s;
5. retrofire a T+284 s;
6. reentrada con escudo ablativo;
7. drogue a 6.705,6 m y main a 3.048 m;
8. amerizaje dentro del sobre declarado de la cápsula.

El paracaídas aporta fuerza de drag real. La transición reefed→full se infla de
forma continua, evitando un impulso numérico. La cápsula declara su propio
límite de splashdown; el simulador no concede aterrizajes blandos globales sobre
la Tierra.

La separación conserva masa, centro de masa y momento lineal, mantiene IDs de
pieza estables y deja cápsula, booster y torre como vehículos persistibles.

## Aceptación reproducible

La simulación headless nominal produce:

- apogeo: 181,4 km;
- velocidad geocéntrica máxima: 2,25 km/s;
- downrange: 480,8 km;
- duración: 913 s;
- carga máxima: 12,85 g;
- resultado: cápsula viva, Alan Shepard vivo y splashdown.

Capturas con framebuffer real:

```bash
bash tools/visual_playtest.sh --mercury --smoke
bash tools/visual_playtest.sh --mercury --launch
```

El modal de campaña se captura con `tools/capture_menu.gd` y muestra
`01 FREEDOM 7 / READY` (`LISTA` en español).

## Fuentes NASA

- <https://www.nasa.gov/history/mercury-redstone-launch-vehicle/>
- <https://www.nasa.gov/mission/mercury-redstone-3-freedom-7/>
- <https://www.nasa.gov/wp-content/uploads/2023/04/sp-4012v2.pdf>
- <https://www.nasa.gov/image-article/60-years-ago-alan-shepard-becomes-first-american-space/>
