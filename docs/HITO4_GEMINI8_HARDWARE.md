# Hito 4 — Hardware histórico de Gemini 8

La tercera misión de campaña ya tiene hardware fechado, pero todavía no está
marcada como jugable. Este commit separa los datos Titan/Gemini/Agena del perfil
de misión y de la anomalía OAMS.

## Gemini Space Vehicle 8

`gemini8-titan2-glv8-1966-03-16` representa:

- spacecraft 8 y su puerto de docking;
- módulo de reentrada para Neil Armstrong y David Scott;
- sección de cuatro retrocohetes;
- equipment adapter con propelente OAMS;
- Titan II GLV-8 de dos etapas;
- sitio histórico LC-19.

El stack cierra 154.980 kg y 33,223 m. La spacecraft separada cierra 3.788 kg.
La prioridad de staging es explícita: el interstage se dispara antes que el
anillo spacecraft/Stage II aunque este último aparezca antes en la topología.
Ambas separaciones conservan masa y dejan 33.471 kg tras Stage I y 3.788 kg de
spacecraft tras Stage II.

Los modelos de propulsión son contratos independientes para las dos cámaras
runtime del LR87 y el LR91 de Stage II. Cada cámara LR87 recibe la mitad
`derived` del agregado publicado, y el cluster vuelve a cerrar exactamente
430.000 lbf; esto evita duplicar empuje y caudal al representarlas por separado.
NASA publica 430.000 y 100.000 lbf; Isp, empuje fuera de la condición publicada,
gimbal, térmica y transitorios se mantienen `estimated` o `derived`.

## Agena 5003

`agena-target-5003-gemini8-1966-03-16` representa el target ya insertado en
órbita, no inventa otro lanzamiento dentro de la misión tripulada. Su masa
publicada de inserción es 7.116 lb, redondeada a 3.228 kg. El bus y el Target
Docking Adapter son piezas separadas y el eje del puerto se enfrenta al del
Gemini.

Las dimensiones detalladas del target y el corredor de captura se declaran
`estimated`/`calibrated`; no se presentan como planos de fabricación.

## Datos y pruebas

La procedencia vive en `data/provenance/gemini8_titan2_1966.json`. Las pruebas
comprueban:

- masa, altura, diámetro, TWR y empuje del stack;
- masa de spacecraft;
- orden de staging y conservación;
- masa del Agena y ejes de docking;
- crew, LC-19 y los once campos obligatorios de cada motor.

## Fuentes primarias

- <https://www.nasa.gov/mission/gemini-viii/>
- <https://www.nasa.gov/history/SP-4002/p3b.htm>
- <https://apollojournals.org/alsj/43455667-Gemini-Program-Mission-Report-Gemini-Viii.pdf>
- <https://ntrs.nasa.gov/api/citations/19760066765/downloads/19760066765.pdf>
