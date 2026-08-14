# Límite de profiling GPU y matriz Earth/star — fase 19

Estado: diagnóstico cerrado; no se promueve recorte de calidad en este host  
Fecha: 2026-08-13  
Alcance: probe de render in-process y selección de la siguiente matriz de recursos

## Resultado de medición

Comando reproducible:

```bash
GODOT_BIN=/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64 \
  bash tools/perf/phase4_gpu_probe.sh --run --display xvfb \
  --driver opengl3 --method gl_compatibility --frames 60 --timeout 180 \
  --out-dir /tmp/exo_phase19_gpu_probe
```

| Señal | Resultado |
|---|---:|
| Estado del reporte | `MEASURED` |
| Adaptador | Mesa llvmpipe (LLVM 20.1.2, 256 bits) |
| `real_gpu_observed` | `false` |
| Muestras | 14 |
| Timer GPU viewport p50/p95/p99 | 843.902 / 964.811 / 964.811 ms |
| Timer CPU render p50/p95/p99 | 840.554 / 979.401 / 979.401 ms |
| Draw calls p50/p95/p99 | 15772 / 15775 / 15775 |
| Objetos p50/p95/p99 | 9774 / 9779 / 9779 |
| `video_mem_bytes` in-process | 599302046 |
| VRAM de driver | `NOT_MEASURED` |
| FPS | `NOT_MEASURED` |

`video_mem_bytes` es el contador expuesto por `RenderingServer` en este backend; no se
interpreta como residencia VRAM física. El contrato de `phase4_gpu_probe` mantiene esta
distinción y rechaza informes que inventen FPS/VRAM a partir de campos numéricos sin fuente.

## Matriz Earth/star identificada

Los recursos de mayor riesgo son:

| Recurso | Dimensión | Uso | Import actual |
|---|---:|---|---|
| `earth_day.jpg` | 8192×4096 | superficie Earth | sin mipmaps |
| `earth_night.jpg` | 8192×4096 | luces nocturnas | sin mipmaps |
| `earth_clouds.jpg` | 8192×4096 | superficie y cielo | sin mipmaps |
| `starmap_milkyway_8k.jpg` | 8192×4096 | cielo estelar | sin mipmaps |

Los shaders declaran `filter_linear_mipmap` y el cielo usa `textureLod` para prefiltrar nubes.
Activar mipmaps o bajar a 4K/2K cambia memoria, aliasing y apariencia, por lo que no se
promueve sólo con el tamaño del JPEG. La matriz correcta para el próximo agente es:

1. baseline 8K sin mipmaps;
2. 8K con mipmaps;
3. 4K con mipmaps;
4. 2K con mipmaps sólo como límite de calidad;
5. captura Earth día/terminador/noche, estrellas y cielo, con RSS, contador de render,
   clipping, luminancia y separación rojo/azul del terminador.

## Criterio de continuación

El siguiente agente debe repetir esa matriz en una GPU física con Vulkan u OpenGL fijo y
capturar residencia de memoria por proceso/driver. En este host sólo se pueden validar
contratos, imagen y tiempos de llvmpipe; no se debe elegir 4K/2K ni publicar una meta de FPS.

