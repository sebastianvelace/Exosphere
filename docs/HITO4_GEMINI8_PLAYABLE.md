# Hito 4 — Gemini 8 jugable

Gemini 8 es la tercera misión ejecutable de la campaña histórica. Queda fijada
a `gemini8-titan2-glv8-1966-03-16`, LC-19, Neil Armstrong, David Scott y Agena
5003; no reutiliza hardware Starship ni una variante genérica.

## Secuencia

El perfil `gemini8-rendezvous-emergency-return` conserva los tiempos publicados
de BECO, separación de Stage I, SECO, separación de Spacecraft 8, rendezvous,
docking, anomalía, desacople, aislamiento del propulsor, retrofire y
splashdown. La guía continua entre eventos es `calibrated`, no telemetría
propietaria.

El LR87 conserva dos cámaras visibles y telemétricas. Su modelo runtime divide
el empuje agregado publicado entre ambas y dimensiona cada rama de alimentación
por encima de su caudal nominal. Una prueba de ignición de cinco segundos exige
que las dos cámaras permanezcan activas y que el stack supere TWR 1,22; este
guard evita que una definición agregada duplicada apague el cohete en la rampa.

Agena se crea mediante el registro de variantes como un vehículo normal con ID
estable `gemini8-agena-5003`. Su masa, estado orbital, piezas y puerto pasan por
`SaveGameV2`. La aproximación final entrega un contacto de 0,10 m/s dentro del
corredor del solver; `TryDock` conserva momento y `Undock` aplica impulsos
iguales y opuestos.

## Falla OAMS-8

El evento determinista reproduce el propulsor 8 atascado:

- el conjunto acoplado aumenta hasta 20°/s;
- tras desacoplar, Spacecraft 8 llega a los 296°/s publicados;
- el RCS de reentrada detiene el giro después del aislamiento;
- la reserva OAMS queda como máximo al 30 %.

La tasa es un efecto externo. `Vessel.Tick` conserva una velocidad angular ya
existente por encima de 20°/s, pero impide que mandos normales o aerodinámica
controlada sigan aumentándola. Así el fallo no desactiva el sobre estable de los
demás vehículos.

## Retorno y aceptación

El impulso retro agregado es 70 m/s y está marcado `calibrated`. Desde la órbita
de rendezvous genera un periapsis entre 40 y 140 km en la prueba headless, en
vez de atravesar la Tierra. La misión exige:

- órbita histórica;
- primer docking;
- tasa alta seguida de control recuperado;
- tripulación y cápsula supervivientes;
- máximo no superior a 310°/s;
- splashdown dentro del corredor de duración.

La evidencia de docking, máximo angular y recuperación sobrevive al round-trip
de misión. Las recompensas siguen usando el ledger idempotente.

## Representación y prueba visual

Titan II tiene bandas y secciones propias; Gemini incluye cápsula cónica,
escudo, ventanas y paracaídas; Agena tiene bus y adaptador de docking
independientes. El chase camera escala con la longitud del vehículo para que
cápsulas y payloads no sean puntos subpíxel.

La prueba reproducible:

```bash
tools/visual_playtest.sh --gemini-docking \
  --out-dir /tmp/exo_gemini8_final \
  --log /tmp/exo_gemini8_final.log
```

captura el hard dock sobre la Tierra iluminada y exige `omega` entre 0,30 y
0,36 rad/s (20°/s). También existe `--gemini --launch` para pad y liftoff.

## Procedencia

Los campos de misión se registran en
`data/provenance/gemini8_titan2_1966.json` como `published`, `calibrated` o
`regulatory_envelope`. Fuentes:

- <https://www.nasa.gov/mission/gemini-viii/>
- <https://www.nasa.gov/history/SP-4002/p3b.htm>
- <https://apollojournals.org/alsj/43455667-Gemini-Program-Mission-Report-Gemini-Viii.pdf>
- <https://ntrs.nasa.gov/api/citations/19760066765/downloads/19760066765.pdf>
