# Optimización de recursos del anillo de Saturno — fase 17

Estado: promovida con gate visual y contrato estático  
Fecha: 2026-08-13  
Alcance: `SimulationBridge.AddSaturnRing`, import de `saturn_ring.png`, harness visual

## Hallazgo

El anillo se construía cuando Saturno pasaba a ser el cuerpo dominante, pero el mapa se
cargaba con:

```text
Image.LoadFromFile → Image.GenerateMipmaps → ImageTexture.CreateFromImage
```

Eso decodificaba en CPU el PNG de 8192×500 y creaba un `ImageTexture` adicional en runtime,
a pesar de que Godot ya tenía un recurso importado para la misma ruta. El coste era diferido,
pero podía producir un spike al viajar a Saturno y mantenía una copia de staging que no era
necesaria para el renderer.

## Cambio

`SimulationBridge` usa ahora `GD.Load<Texture2D>("res://assets/textures/saturn_ring.png")`.
El import se configuró con `mipmaps/generate=true`, coherente con el sampler
`filter_linear_mipmap` de `saturn_ring.gdshader`. La textura sigue siendo RGBA y conserva los
huecos transparentes del anillo; no se modificaron mesh, UV, alpha, shader ni la física.

El recurso importado queda cacheado por Godot y se asigna directamente al `ShaderMaterial`.
No se afirma que el tamaño del archivo `.ctex` sea VRAM: sólo demuestra que el staging manual
desapareció del código y que el import genera el recurso requerido.

## Evidencia

Auditoría estática después del cambio:

| Señal | Resultado |
|---|---:|
| Fuente | 8192×500, 63.27 KiB |
| Estimación RGBA8 base | 15.62 MiB |
| Estimación RGBA8 con mipmaps | 20.83 MiB |
| `.ctex` importado con mipmaps | 56.06 KiB |
| CPU staging manual en `SimulationBridge` | 0 llamadas |
| Triángulos del anillo | 320 |

El `.ctex` anterior sin mipmaps medía 23.01 KiB; el aumento del cache importado es esperado
al almacenar los niveles mip y no debe interpretarse como una medición de memoria de driver.

Playtest renderer-backed bajo Xvfb/llvmpipe:

```text
SUMMARY reason=SATURN_OK frames=170
CAPTURE saturn_ring alt=241072000.0 spd=19568.4 phase=ORBIT
IMAGE slug=saturn_ring mean=0.06676 p95=0.59216 clippedFrac=0.01957
```

La imagen contiene el cuerpo, los anillos y sus bandas transparentes; el gate exige además
`mean > 0.02` y `p95 > 0.20` para impedir que una captura fuera de encuadre pase sólo por
existir como PNG. El coste de render observado sigue siendo llvmpipe y no se usa para declarar
un FPS de hardware.

## Regresión y decisión

- `saturn_ring_contract_test.sh`: PASS; impide volver a introducir la ruta `Image` CPU y
  exige import mipmapped, sampler mipmap y modo visual Saturno.
- `dotnet build Exosphere.csproj --no-restore`: 0 warnings, 0 errors.
- El modo `--saturn` usa `JumpToBody("saturn")`, espera el spawn diferido del cuerpo dominante
  y valida una captura real; no crea una ruta de producción alternativa.
- El cambio se promueve porque elimina una duplicación verificable y conserva alpha/apariencia
  en la captura. No se promueve ninguna conclusión de VRAM o FPS hasta medir un driver GPU
  físico.

## Límite conocido

La escena de prueba muestra Saturno desde un encuadre deliberadamente cercano para validar el
anillo. No sustituye una matriz completa de aproximación ni una medición de memoria del driver;
esas pruebas quedan para el agente de render/GPU en hardware objetivo.

