# Hito 4 — Hardware histórico de Apollo 8

La cuarta misión de campaña ya dispone de un stack fechado y trazable. Este
bloque entrega el hardware AS-503/CSM-103; el director de misión lunar, las
maniobras LOI/TEI y el debrief histórico se integrarán en el bloque siguiente.

## AS-503 / CSM-103

`apollo8-saturn5-as503-csm103-1968-12-21` representa:

- Saturn V AS-503 con cinco F-1, cinco J-2 en S-II y un J-2 reiniciable en S-IVB;
- CSM-103 con su AJ10-137/SPS reiniciable como duodécimo motor individual;
- CSM-103, SLA, el artículo de prueba lunar LTA-B y el sistema de escape;
- Instrument Unit y los dos interstages con prioridades de separación explícitas;
- Frank Borman, James Lovell y William Anders;
- LC-39A mediante el sitio offline `kennedy`.

La masa de ignición cierra 2.821.241,122 kg. Se usa esta condición porque el
simulador comienza antes del encendido con los tanques llenos; no se confunde
con los 2.781.694 kg reconstruidos al liberar los brazos de sujeción después del
consumo durante el desarrollo de empuje. Los sobres redondeados de cada sección
suman 110,7 m, consistentes con los 110,6 m aproximados publicados.

La spacecraft completa cierra las 96.272 lb reales. La torre de escape es una
raíz desplegable: al desecharla se conserva masa y el staging posterior deja:

1. S-IC e interstage: 2.181.053,552 kg;
2. S-II e interstage: 474.720,703 kg;
3. S-IVB, IU, SLA y LTA-B: 132.625,111 kg;
4. CSM separado: 63.524 lb / 28.814,002 kg.

## Propulsión

Los doce motores son instancias independientes. El agregado nominal de S-IC
cierra 33.850.000 N a nivel del mar. El S-II cierra 5.004.249 N en vacío.

No se mezcló el rendimiento de los J-2:

- S-II usa 423,7 s, reconstruido durante su tramo de alta mezcla;
- S-IVB usa 903.225 N y 428,8 s de su primera quema reconstruida;
- el J-2 de S-IVB admite exactamente un reinicio, necesario para TLI.
- el SPS conserva reinicios para LOI, circularización, TEI y correcciones.

Los endpoints de J-2 a nivel del mar, el F-1 en vacío, térmica, rates de gimbal
y algunos transitorios se declaran `estimated` o `derived`; no se presentan
como datos publicados.

## Validación

`Apollo8DataTests` comprueba masas, dimensiones, TWR, montajes individuales,
reinicio del S-IVB, conservación durante jettison/staging, cierre del CSM,
round-trip de `CraftDocumentV2`, tripulación, pad y procedencia obligatoria.

La captura visual en framebuffer valida pad y despegue. En vuelo se observan
cinco F-1 activos, TWR de 1,23 al inicio, masa decreciente y las cinco plumas
data-driven.

## Fuentes primarias

- Apollo 8 Mission Report, MSC-PA-R-69-1:
  <https://ntrs.nasa.gov/citations/19700033031>
- Saturn V Launch Vehicle Flight Evaluation Report AS-503:
  <https://ntrs.nasa.gov/search.jsp?R=19690015314>
- Apollo 8 Prelaunch Mission Operation Report:
  <https://www.nasa.gov/wp-content/uploads/static/history/afj/ap08fj/pdf/a08-prelaunch-rep.pdf>
- Apollo spacecraft reference:
  <https://www.nasa.gov/wp-content/uploads/static/history/alsj/CSM_News_Reference_H_Missions.pdf>
