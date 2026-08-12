# GPU/recursos — informe del Agente 5

Fecha: 2026-08-12  
Rama: `agent5/gpu-resources`  
Alcance: `scenes/flight/Flight.tscn`, recursos y shaders visuales, y auditoría de
`PlanetMaterials`. No se modificaron `SkyController`, la simulación C# ni los
shaders de atmósfera/planetas.

## Resultado

Se aplicó una única mejora segura en `Flight.tscn`:

```ini
directional_shadow_mode = 1
```

La luz direccional pasa de PSSM de cuatro particiones (valor por defecto de
Godot cuando las sombras direccionales están activadas) a dos particiones. Se
mantienen las sombras cercanas de nave, pad y geometría local, pero se reducen
las vistas de profundidad que el renderer debe producir para esa luz. Godot
explica que el modo de dos particiones es el compromiso entre el modo
ortogonal y cuatro particiones, y que aumentar la distancia máxima de sombras
incrementa el coste: [DirectionalLight3D](https://docs.godotengine.org/en/4.6/classes/class_directionallight3d.html),
[luces y sombras 3D](https://docs.godotengine.org/en/4.6/tutorials/3d/lights_and_shadows.html).

No se modificó `directional_shadow_max_distance`; conserva el valor por defecto
`100.0`, evitando ampliar el área de sombras durante el vuelo orbital.

## Auditoría de la escena

`SimulationBridge.SpawnPlanets()` crea siete esferas de cuerpo (el Sol no se
crea como malla, porque lo representa el cielo) y un anillo para Saturno:

| Recurso | Configuración actual | Estimación estática |
| --- | ---: | ---: |
| Esferas planetarias | `RadialSegments=96`, `Rings=48` | ~9.024 triángulos por esfera, ~63.168 en total |
| Anillo de Saturno | 160 segmentos, dos triángulos por segmento | 320 triángulos |
| Draws de cuerpos | 7 esferas + anillo | 8 superficies/draws, antes de nave, pad, cielo y UI |

La estimación de triángulos es geométrica (`2 × radialSegments × (rings - 1)`)
y no sustituye a una captura del Visual Profiler. Es una carga moderada para
una GPU normal; reducirla exige cambiar el `SphereMesh` que hoy se construye en
C#, fuera del ownership del agente, o introducir LOD explícito con cambios de
integración. Por eso no se hizo una reducción de segmentos sin una prueba A/B
visual.

## Materiales, shaders y texturas

- Earth utiliza un `ShaderMaterial` con `earth_surface.gdshader` y tres mapas
  de `8192×4096`: día, noche y nubes.
- Moon, Mars, Venus, Jupiter y Saturn usan el shader genérico con un mapa de
  superficie cada uno; Mercury permanece procedural.
- Saturno añade un `ImageTexture` del anillo `8192×500`.
- `earth_surface.gdshader` y `planet_body.gdshader` declaran `unshaded` y
  calculan su propia iluminación. En consecuencia, la `DirectionalLight3D`
  no reilumina las esferas planetarias, no altera su terminador y no modifica
  la visibilidad de eclipse suministrada por `solar_visibility`.
- Los shaders siguen teniendo costes que deben medirse antes de tocarse:
  Earth hace tres muestras de textura y FBM; el shader genérico usa FBM y, para
  cuerpos rocosos, una vecindad de cráteres de `3×3×3`. No se cambiaron porque
  podrían modificar la apariencia en baja altitud o en el limbo.

El tamaño de los artefactos importados `.ctex` observados tras el import fue,
aproximadamente:

| Grupo | Tamaño de caché importada |
| --- | ---: |
| Earth día/noche/nubes | 38,2 MiB |
| Texturas de otros planetas | 18,6 MiB |
| Starmap 8K | 3,4 MiB |
| Anillo Saturno | 23 KiB |

Estas cifras son tamaño de disco de la caché importada, no memoria GPU
residente. La compresión/reducción de resolución no se aprobó en este agente:
requiere capturas A/B de Earth día, terminador, totalidad y cielo estelar, más
una medición de VRAM en hardware real.

## Medición y límites de esta ejecución

Entorno: Godot 4.6.3 mono, renderer OpenGL 3, Mesa llvmpipe, Xvfb. La línea
base contractual pudo compilar y abrir la escena, pero el proceso no produjo
telemetría del harness después de su cabecera y tuvo que terminarse por el
límite. El síntoma coincide con el precálculo síncrono de LUT atmosféricas que
pertenece a otro frente de trabajo; no se atribuye a esta modificación de
sombras y no se declara como PASS visual.

Por tanto, este commit no afirma una mejora FPS numérica. La medición
comparativa pendiente, una vez integrado el pipeline de LUT asíncrono, debe
registrar en la misma máquina y resolución:

1. tiempo de frame y de render;
2. tiempo de shadow pass;
3. draw calls y primitivas visibles;
4. VRAM residente y tamaño de texturas;
5. capturas de día, amanecer/atardecer, totalidad y noche.

Godot recomienda separar carga inicial, picos intermitentes y coste por frame y
medir con el profiler antes de optimizar: [General optimization tips](https://docs.godotengine.org/en/4.6/tutorials/performance/general_optimization.html).

## Verificación

- Import de recursos Godot: PASS.
- Build `ExosphereSimulation`: PASS, 0 warnings, 0 errors.
- Build `Exosphere.csproj`: PASS, 0 warnings, 0 errors.
- Escena `Flight.tscn`: propiedad `directional_shadow_mode=1` parseada por el
  import de Godot; no se tocaron recursos binarios.
- `visual_playtest.sh --smoke`: NO PASS en esta rama base; el harness quedó sin
  telemetría al iniciar y fue limpiado. Debe repetirse después de integrar la
  corrección asíncrona de LUT.
- Limpieza: sin `_PlaytestShot.cs`, sin autoload temporal y sin procesos Godot
  vivos al terminar.

## Recomendaciones para la integración

1. Repetir smoke y Visual Profiler después de integrar el agente de LUT; usar
   una captura A/B con exactamente la misma cámara y exposición.
2. Si el perfil confirma coste de geometría, mover el LOD de `SphereMesh` al
   ownership de `SimulationBridge` y usar al menos tres escalones por tamaño
   angular; no cambiar el radio físico, sólo la malla visual.
3. Auditar la importación de las texturas 8K con una prueba de calidad y VRAM;
   no bajar Earth día/noche/nubes mientras no exista evidencia de que el
   terminador y las luces nocturnas permanecen estables.
4. Medir las luces de sombra del pad por separado. Esas luces se crean en
   `LaunchPadController`, que queda deliberadamente fuera de este commit.
