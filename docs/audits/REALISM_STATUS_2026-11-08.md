# Estado de realismo del simulador — 2026-11-08

Auditoría de qué modela hoy Exosphere, qué tan cerca está de los vuelos reales y qué falta
para un simulador "totalmente realista". La física tiene prioridad sobre lo visual. Las
afirmaciones salen del código, de las compuertas en `docs/physics/fidelity_gates.md` y de los
residuos medidos en `docs/physics/historical_recalibration.md`.

## Resumen

| Área | Estado |
| --- | --- |
| Dinámica del vehículo rígido | Muy sólida: 6-DoF acoplado RK4, masa variable con transporte de momento, slosh de primer modo, RCS y flaps físicos, compuertas analíticas e históricas (F0–F5 cerradas) |
| Calibración histórica | En curso (F6): perfiles vuelan y pasan sus límites, pero quedan residuos medibles y datos estimados |
| Entorno (gravedad, atmósfera, efemérides) | Es el punto más débil: sin viento, sin efemérides fechadas, solo J2, atmósfera de un solo perfil |
| Estructura y térmica | Cuerpo rígido; calentamiento de estancamiento sin ablación ni conducción |
| Visual | Buena base procedural; casi nada está comparado lado a lado con fotos reales |

## Física: qué está bien

- Integrador 6-DoF acoplado por defecto, con centro de masa, tensor de inercia y contacto en
  las fuerzas de cada etapa RK4; Kepler en raíles y transiciones de SOI por cónicas.
- Masa variable (F4): geometría de masa por pieza, columnas de líquido asentadas, salidas
  declaradas, transporte de momento lineal y angular, jet damping validado contra AS-506.
- Slosh (F5): primer modo por tanque (SP-8009), sin masa ficticia, validado contra frecuencias
  medidas del AS-501.
- Aerodinámica distribuida por pieza para cuerpos esbeltos (flujo cruzado de Jorgensen),
  flaps físicos de Starship, modelo de cápsula con trimado.
- Control solo por actuadores: TVC, RCS declarado por pieza, flaps; sin escritura directa de ω.
- Propulsión con empuje/Isp corregidos por presión, motores individuales con estado propio.
- Procedencia obligatoria para cada dato de fidelidad (`data/provenance`).

## Física: brechas, por prioridad

### P1 — Entorno (mayor ganancia de realismo por unidad de trabajo)

1. **Efemérides fechadas.** La Luna es una órbita kepleriana fija en J2000 y las misiones
   arrancan en J2000, no en su fecha. La geometría Tierra–Luna–Sol de Apollo 8/11 es
   arbitraria. Falta un efemérides tipo JPL DE (Chebyshev) para Sol, Luna y planetas, y
   arrancar cada misión en su época real.
2. **Gravedad lunar con mascons.** SP-238 documenta que el potencial lunar movió la órbita de
   Apollo 11 unas 2,5 mi náuticas; por eso la circularización apuntaba a una órbita no
   circular. Sin un modelo de gravedad lunar (por ejemplo LP150Q / GRAIL truncado) esa
   evolución no existe. En la Tierra solo hay J2: faltan J3/J4 y armónicos para LEO precisa.
3. **Viento y atmósfera variable.** No hay viento en ninguna altitud. Afecta cargas en Max-Q,
   ángulo de ataque en ascenso, regreso del booster, aterrizaje y huella de entrada. Los
   informes de vuelo (FER del AS-506) publican viento en altura del día del lanzamiento. La
   atmósfera es un solo perfil: falta variabilidad diurna/solar para el decaimiento en LEO.
4. **Orientación terrestre.** Ejes J2000 fijos, sin precesión ni nutación; menor para misiones
   cortas, relevante para fechas reales y sitios de aterrizaje.

### P2 — Vehículo

5. **Flexión estructural.** Todo es cuerpo rígido. Saturn V y Starship son esbeltos: los modos
   de flexión interactúan con el TVC y los sensores (el control real los filtra). Sería una
   fase F7 con modos de viga por etapa.
6. **Térmica.** Calentamiento tipo Sutton–Graves sin ablación, sin recesión del escudo, sin
   conducción interna y sin apagón de comunicaciones por plasma.
7. **Motores.** Faltan curvas de encendido/apagado y estrangulación con su dinámica, dinámica
   del actuador del gimbal, desplazamiento de la relación de mezcla (PU) y modos de falla.
8. **Aerodinámica pendiente (F6).** Aletas del S-IC, grid fins de Super Heavy, modelo
   newtoniano de cápsulas romas, interacción pluma–flujo en la base.
9. **Slosh de orden superior y en costa.** Solo primer modo y líquido asentado; en ingravidez
   o baja aceleración el modelo no aplica.
10. **Guiado.** La TLI usa una inyección prograda (la IGM iterativa sigue abierta); Gemini 8
    arranca con el Agena colocado arriba por geometría de escenario.

### P3 — Calibración histórica (F6, residuos medidos)

| Perfil | Residuo actual |
| --- | --- |
| AS-506 inserción | +10 s, −6,7 t de S-IVB, −7,6 t de vehículo frente al FER |
| AS-503 inserción | +15 s frente al primer corte histórico |
| Gemini 8 inserción | +9,5 s frente a SECO |
| Flight 14 descenso | +333 / +935 / −1655 m en anclas de transmisión; desajuste de velocidad hipersónica tardía |
| Friendship 7 | Sustainer efectivo de calibración, no reconstrucción nativa |
| Apollo 11 CSM | Masas y CG ya contra SP-238; propelente del SPS como un solo almacén centrado |
| Falcon 9 / New Glenn / Flight 7 | Sin trayectoria medida contra la que comparar |

## Visual: estado y brechas

Implementado y verificado con capturas reales (xvfb): plumas a nivel del mar y de vacío, nube
de diluvio del pad, plasma de reentrada orientado, grid fins, detalles de casco y torre,
luces nocturnas del pad, mallas procedurales de Mercury/Gemini/Apollo.

Falta, en orden:

1. **Comparación lado a lado con fotos reales** de hot-staging (T+2:39/2:40), encendido,
   pluma de vacío, reentrada, grid fins, casco y torre. Nada de eso está cerrado.
2. **Composición atmósfera–superficie**: la Tierra en espacio escalado tapa el casco en primer
   plano, la transición terreno–bruma es opaca y la capa de atmósfera es cian fija
   (auditoría `ascent_surface_atmosphere_compositing_2026-09-30.md`).
3. **Clima y nubes** con siluetas (cúmulos), reconstrucción más fina de Pad 2 y vista a bordo.
4. **Reentrada**: choque y plasma más ricos frente a referencias IFT.
5. **Agua**: entrada, salpicadura e inundación visibles; captura del booster en torre.
6. **CI visual**: capturas PNG de extremo a extremo con heurísticas, no solo smoke.

## Orden recomendado

1. Efemérides fechadas + época real por misión (desbloquea Apollo con geometría verdadera).
2. Viento en altura por misión (empezar por el perfil publicado del AS-506) y atmósfera variable.
3. Gravedad lunar con mascons y J3/J4 terrestre.
4. Cerrar F6 (tobera del SPS, Atlas nativo, aletas/grid fins/cápsulas newtonianas).
5. F7 flexión estructural; luego térmica con ablación.
6. En paralelo, visual: comparación fotográfica y composición atmosférica.
