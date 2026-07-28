# Hito 4 — Apollo 8 jugable

Apollo 8 es la cuarta misión jugable de la campaña histórica. La definición
queda fijada a AS-503, CSM-103, LTA-B, LC-39A y la tripulación Borman/Lovell/
Anders; sandbox conserva acceso libre al preset.

## Secuencia

`Apollo8FlightProfile` registra los eventos publicados desde el despegue hasta
el amerizaje:

- cutoff del F-1 central, OECO y separación S-IC;
- jettison del LES, cutoff/separación S-II e inserción S-IVB;
- coast en parking orbit y segunda ignición S-IVB para TLI;
- separación CSM/S-IVB, cruce ordinario de la SOI lunar y LOI;
- circularización, diez revoluciones lunares y TEI;
- separación CM/SM, entry interface, drogues, mains y splashdown.

El ascenso usa motores, propelente, masa, atmósfera y staging ordinarios. Al
cerrar la inserción se aplica una corrección calibrada al estado publicado de
parking orbit, alineada con el plano del dataset lunar offline. TLI se obtiene
con `LunarTransferPlanner`: Lambert geocéntrico, Luna móvil, B-plane, perilunio
y límite práctico de 4,5 km/s. La nave se propaga sobre esa cónica y entra en
la SOI mediante el resolver patched-conic; no se coloca directamente en la
Luna.

LOI, circularización y TEI descuentan propelente SPS mediante la ecuación del
cohete. Sus estados de cutoff se calibran a los corredores publicados para que
la representación agregada no acumule error de integración como si fuera
telemetría propietaria. TEI genera una cónica Lambert de retorno que intersecta
la interfaz atmosférica; desde allí la cápsula vuelve a física completa.

## SPS y evidencia

CSM-103 incluye ahora una instancia AJ10-137/SPS separada, con una pluma, feed
network, gimbal y reinicios. Su masa y longitud se sustrajeron del agregado del
Service Module, por lo que las masas y dimensiones ya aceptadas no cambiaron.

La campaña persiste `completedLunarOrbits` aparte de `completedOrbits`. El
contador se reinicia al cambiar de cuerpo dominante y rechaza discontinuidades
de más de media órbita por frame. El debrief requiere evidencia de:

- parking orbit, TLI y LOI;
- diez revoluciones lunares;
- TEI;
- tres tripulantes vivos, CM recuperado y splashdown;
- duración y carga de entrada dentro del corredor.

## Pruebas y captura

Las pruebas cubren definición, horarios, round-trip de evidencia lunar,
evaluación positiva/negativa, SPS, staging y un TLI end-to-end que entra en la
SOI con perilunio seguro. El harness añade:

```bash
tools/visual_playtest.sh --apollo8 --launch
tools/visual_playtest.sh --apollo8-lunar
```

La primera ruta valida countdown, perfil histórico, cinco plumas y liftoff. La
segunda separa el CSM mediante el staging real y captura una órbita lunar
circular de 111,1 km con SPS individual.

## Fuentes primarias

- <https://ntrs.nasa.gov/citations/19700033031>
- <https://ntrs.nasa.gov/search.jsp?R=19690015314>
- <https://www.nasa.gov/wp-content/uploads/static/history/alsj/CSM_News_Reference_H_Missions.pdf>
