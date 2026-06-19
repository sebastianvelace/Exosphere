---
name: visual-testing
description: Cómo verificar cambios visuales de Exosphere con una captura headless en Godot. Úsala siempre que un cambio afecte al render (cohete, plumas, planetas, cielo, HUD, cámaras, escala) y necesites comprobar que SE VE bien, no solo que compila. Incluye el harness de autoload temporal y su limpieza obligatoria.
---

# Verificación visual headless

No hay tests automatizados de render. Para confirmar que un cambio visual se ve bien, se hace una
captura PNG con Godot en headless y se revisa con la herramienta de lectura de imágenes.

## Flujo

1. **Compila primero** (build 0/0):
   ```bash
   dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet
   dotnet build Exosphere.csproj --nologo -v quiet
   ```

2. **Crea un helper de captura temporal** `scripts/_XxxShot.cs` (prefijo `_` + sufijo `Shot`): un
   nodo que, tras N frames de estabilización, llama a `GetViewport().GetTexture().GetImage()` →
   `SavePng("/tmp/xxx.png")` y luego `GetTree().Quit()`. Posiciona la cámara/escena para encuadrar
   exactamente lo que quieres verificar.

3. **Regístralo como autoload temporal** en `project.godot` (o añádelo a la escena de prueba).

4. **Corre headless:**
   ```bash
   /home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64 \
     --path . --rendering-driver opengl3
   ```

5. **Revisa el PNG** en `/tmp/` con la tool Read (lee imágenes). Itera sobre el helper hasta que el
   resultado sea correcto.

6. **LIMPIEZA OBLIGATORIA** (no negociable):
   - Borra `scripts/_XxxShot.cs` (y su `.uid` si se generó).
   - Restaura `project.godot`: `git checkout project.godot`.
   - Confirma con `git status` que no queda rastro del harness.

## GOTCHAS

- **Nunca commitees** el helper de captura ni el cambio temporal de `project.godot`. Son andamiaje.
- Deja **frames de estabilización** antes de capturar (plumas GPU, materiales, origen flotante y
  fades por altitud tardan unos frames en asentarse).
- Recuerda la escala `1 u = 2.8 m` y el scaled-space al encuadrar: el planeta no está a su distancia
  real (ver skill `godot-rendering-bridge`).
- Usa nombres de archivo únicos en `/tmp` por iteración para no leer una captura vieja cacheada.
