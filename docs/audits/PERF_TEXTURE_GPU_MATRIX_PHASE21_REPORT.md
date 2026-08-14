# Runner de matriz GPU para texturas — fase 21

Estado: runner implementado; gate físico pendiente  
Fecha: 2026-08-14  
Host actual: sin `nvidia-smi`, `vulkaninfo` o `glxinfo`; las corridas previas observaron llvmpipe

## Qué se automatizó

[`tools/perf/texture_gpu_matrix.sh`](../../tools/perf/texture_gpu_matrix.sh) ejecuta cuatro
variantes en worktrees temporales, sin modificar el checkout de producción:

1. 8K sin mipmaps;
2. 8K con mipmaps;
3. 4K con mipmaps;
4. 2K con mipmaps.

Por variante realiza restore/build opcional, importación Godot, `phase4_gpu_probe`, medición
del caché `.godot/imported` y una captura visual opcional (`smoke`, `cockpit`, `saturn` o
`atmosphere`). Cada resultado queda en `matrix.meta`, `matrix.rows.tsv` y subdirectorios
por variante.

## Gates de seguridad

- El worktree de producción debe estar limpio antes de `--run`.
- Los cambios de `.import` sólo viven en el worktree candidato.
- El runner exige evidencia de adaptador no software por defecto.
- `--allow-software` permite probar el mecanismo en llvmpipe, pero el resultado siempre es
  `BLOCKED`, nunca `PASS` de GPU física.
- `--validate` rechaza filas faltantes, variantes desconocidas, estados inválidos o conteos
  distintos de cuatro.
- El contrato está integrado en `tools/ci_check.sh`.

## Uso en hardware objetivo

Para una medición de render sin captura atmosférica completa:

```bash
tools/perf/texture_gpu_matrix.sh --run \
  --display native --driver vulkan --method forward_plus \
  --resolution 1920x1080 --frames 120 \
  --out-dir /tmp/exo_texture_gpu_matrix_gpu
```

Para añadir la matriz visual completa, usar `--visual-mode atmosphere`. Esa opción es
intencionadamente costosa: no debe sustituirse por un smoke si se pretende decidir la
promoción de 4K.

En este host sólo se verificó el manifiesto seco y el contrato; no se publica una medición
GPU porque el adaptador disponible no es físico.

## Preflight ejecutada en este host

También se ejecutó el gate real sin `--allow-software`:

```bash
tools/perf/texture_gpu_matrix.sh --run --display xvfb \
  --driver opengl3 --method gl_compatibility --frames 20 \
  --timeout 60 --import-timeout 120 \
  --out-dir /tmp/exo_phase21_matrix_blocked
```

Resultado: `status=BLOCKED`, `physical_gpu_gate=BLOCKED` y
`software_renderer_observed=true`. La variante `8k_nomip` llegó a importar 65,076,252 bytes
y obtuvo un probe in-process `MEASURED`; las variantes 8K+mipmaps, 4K+mipmaps y 2K+mipmaps
no se ejecutaron porque el fail-closed detuvo la matriz al detectar llvmpipe. El worktree
temporal se eliminó y `main` quedó limpio.
