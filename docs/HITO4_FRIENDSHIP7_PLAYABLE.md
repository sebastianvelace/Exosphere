# Hito 4 — Friendship 7 jugable

Friendship 7 es la segunda misión histórica completa del catálogo. El flujo de
campaña construye la variante fechada
`mercury-atlas6-friendship7-1962-02-20`, la coloca en
`cape_canaveral_lc14`, embarca a `john-h-glenn-jr` y activa el perfil
`mercury-atlas6-three-orbit`. La misión solo se desbloquea después de completar
Freedom 7; nunca sustituye el Atlas por otro vehículo disponible.

## Vehículo y procedencia

El preset cierra la masa publicada del Atlas 109-D (117.979 kg), la masa
completa de la spacecraft 13 (1.934,7 kg) y la altura del stack (29,03 m). El
Atlas usa una arquitectura *stage-and-a-half*: dos LR89 se desprenden con el
paquete booster mientras el LR105 y el tanque común continúan hasta SECO.

Las cifras publicadas, derivadas y calibradas están separadas en
`data/provenance/mercury_atlas6_friendship7_1962.json`. Distribución de masas,
Isp, empuje de vacío, coeficientes aerodinámicos, modelos de paracaídas y
transitorios que no aparecen en las fuentes públicas no se presentan como
datos históricos exactos.

La visualización ya no reutiliza Starship: Atlas tiene tanque metálico
presurizado, soldaduras circunferenciales, marcado `UNITED STATES`, sección de
sustainer y pods LR89 propios. Mercury conserva cápsula, retro pack, adaptador,
anillo y torre como piezas semánticas separadas.

## Secuencia física

El perfil reproduce con física continua las fases críticas:

1. ignición y programa de pitch del Atlas;
2. BECO y desprendimiento del paquete LR89 a T+129,6 s;
3. expulsión de la torre a T+153,3 s;
4. SECO a T+301,4 s y separación de Mercury a T+303,6 s;
5. tres órbitas acumuladas en evidencia persistible;
6. actitud retrógrada y los tres retrocohetes en la ventana histórica;
7. reentrada con escudo orientado por datos;
8. drogue, main y amerizaje dentro del sobre de la cápsula.

Al desprender el booster se conservan masa y momento, y el anillo inferior
permanece con el Atlas. La nave orbital queda exactamente en 1.934,7 kg.
`CompletedOrbits` se calcula a partir del ángulo radial inercial acumulado, se
guarda en `SaveGameV2` y se evalúa sin depender del HUD.

Mercury declara el eje de su escudo y su centro aerodinámico en los datos de
pieza. El solver térmico y el momento aerodinámico ya no asumen la orientación
de Starship. Las piezas no raíz quemadas pueden desprender su subárbol como
debris; destruir una pieza secundaria no elimina silenciosamente toda la nave.

## Corrección del integrador cerca de la superficie

El tramo final reveló una mezcla de épocas: los cuerpos celestes ya estaban en
el final del paso RK4 mientras el vessel todavía estaba en el inicio. Cerca de
la Tierra, su desplazamiento heliocéntrico durante el paso se interpretaba como
velocidad vertical de la cápsula. La integración fuera de rails ahora se
resuelve en estado relativo al cuerpo de referencia y luego reconstruye el
estado inercial, restando la aceleración común de su órbita.

La corrección mantiene el ascenso en la época correcta y elimina la energía
espuria bajo paracaídas. Está cubierta junto con impactos anclados, contactos de
aterrizaje, warp atmosférico, Freedom 7 y Friendship 7.

## Aceptación reproducible

La prueba headless comprueba:

- masa orbital exacta y retención correcta del anillo;
- BECO, torre, SECO, separación, retrofire, drogue y main;
- al menos tres revoluciones completas;
- periapsis posretrofire inferior a 100 km;
- alineación máxima del escudo de al menos 0,95;
- main desplegado entre 8,0 y 8,6 km;
- cápsula y John Glenn vivos tras un splashdown asentado;
- inserción, duración y carga máxima dentro de sus corredores declarados.

Capturas con framebuffer real:

```bash
bash tools/visual_playtest.sh --friendship --smoke
bash tools/visual_playtest.sh --friendship --launch
```

El modal de campaña muestra `02 FRIENDSHIP 7 / BLOQUEADA` hasta que Freedom 7
figure como completada. Gemini 8 permanece `PLANIFICADA`.

## Fuentes primarias

- <https://www.nasa.gov/mission/mercury-atlas-6-friendship-7/>
- <https://www.nasa.gov/history/60-years-ago-john-glenn-the-first-american-to-orbit-the-earth-aboard-friendship-7/>
- <https://www.grc.nasa.gov/WWW/k-12/rocket/gallery/atlas/atlas1.html>
