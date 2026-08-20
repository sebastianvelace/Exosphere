---
name: rocket-visual-design
description: Cómo dar detalle procedural creíble a un vehículo en VesselRenderer.cs (weld rings, raceway, motores, carenados) sin caer en cilindros planos genéricos ni en el bug de costuras coincidentes. Úsala siempre que agregues una familia de vehículo nueva, mejores el aspecto de una existente, o toques cualquier mesh procedural del cohete.
---

# Diseño visual procedural de cohetes — `scripts/VesselRenderer.cs`

Todo el exterior de las naves es geometría procedural generada en runtime (`MeshInstance3D`/
`ArrayMesh`/`StandardMaterial3D`/`ShaderMaterial`) — no hay assets `.glb`/`.obj`. Esta skill
documenta el patrón que ya funciona bien para Starship, para que una familia nueva o una mejora
no reinvente algo peor ni repita bugs ya resueltos.

## El bug que ya mordió dos veces: costuras coincidentes (z-fighting)

`PartGraph`/`VesselAssembly` apilan piezas con offset EXACTO (gap cero) según los nodos del JSON —
no hay margen. Si dos piezas del mismo radio usan cada una un `CylinderMesh` con `CapTop`/
`CapBottom` en `true` (el default), quedan dos discos de tapa **coincidentes en el mismo plano**,
con normales opuestas. Eso es z-fighting de manual: desde la mayoría de ángulos el depth-test lo
oculta, pero desde ángulos rasantes aparece como una línea blanca brillante que titila.

Encontrado en Falcon 9, New Glenn Y en el Saturno V (que sí tiene tratamiento especial) —
o sea que **no es un problema de una familia, es un problema de cualquier unión cilindro-cilindro
del mismo radio con tapas activas por defecto**.

**Regla:** en cualquier unión entre dos piezas contiguas del mismo radio, ningún extremo debe tener
tapa (`CapTop`/`CapBottom = false` en ambos). Solo los dos extremos verdaderamente expuestos de
todo el stack (la punta del morro y la base de motores) llevan tapa — y solo uno de los dos lados
en cada caso. Determiná "tiene vecino en este nodo" recorriendo la conectividad real de
`VesselAssembly`/`Vessel.Parts`, no adivinando por categoría de pieza.

La forma más robusta de evitar el bug por completo —y la que ya usa Starship— es no generar "una
pieza, un mesh" en absoluto: **un tramo entero del stack (ej. todo el tanque + intertanque) se
construye como UN solo cilindro continuo**, con las bandas/costuras dibujadas encima como detalle
(anillos, cajas) en vez de como piezas de mesh separadas que podrían no coincidir en el borde. Ver
el comentario en `scripts/VesselRenderer.cs:164-168` que explica exactamente esta decisión.

## El patrón Starship — qué reusar antes de inventar algo nuevo

Toda esta caja de herramientas ya es genérica (no depende de nada específico de Starship) y vive
en `scripts/VesselRenderer.cs`:

| Herramienta | Para qué | Notas |
|---|---|---|
| `SteelMat()` / `res://assets/shaders/steel.gdshader` | Metal expuesto con bandas de soldadura, anisotropía de cepillado, gradiente de hollín | Uniforms tuneables: `weld_spacing`, `weld_depth`, `brush_amt`, `soot_y0/y1`, `emit_strength`. Para un casco **pintado** (Falcon 9) en vez de acero expuesto, bajá `brush_amt` casi a cero o usá `Mat()` liso — no fuerces el shader de acero cepillado sobre algo que en la realidad está pintado |
| `AddWeldRing`/`AddWeldRings`/`AddHullRing` | Anillos de refuerzo/costillas horizontales | Cilindros finos "proud" (sobresalientes) apilados a intervalos — así se ve el detalle sin crear una nueva junta de mesh |
| `AddSurfaceBox` | Raceway (conducto de cables), paneles, tomas de venteo, marcas de serie | Caja pegada tangencialmente al casco a un radio/ángulo calculado — la técnica genérica para "algo rectangular pegado a un cilindro" |
| `AddTileBand`/`AddNoseTileShell` | Bandas de losetas térmicas siguiendo la curva del morro | Específico de escudos térmicos tipo Starship — no lo fuerces en un vehículo sin TPS de losetas |
| `VehicleVisualPhysics.TangentOgiveRadius` | Perfil de morro ogival real (no cono recto ni cápsula redondeada) | Un `CapsuleMesh` para el carenado es la señal de que nadie miró la referencia real — casi ningún cohete real tiene morro de cápsula |
| `AddRaptor` (patrón de frustas apiladas) | Base para modelar CUALQUIER motor con detalle: campana, garganta, domo de turbobomba | Adaptá proporciones (una sola campana para Merlin, sin garganta separada; más simple que Raptor), no reinventes la construcción de frustas |
| `AddSHGridFins`/`BuildGridFinPlateMesh` | Base para rejillas aerodinámicas de cualquier tamaño | Escalá proporciones — Falcon 9 usa rejillas más chicas y sin bisagra que Starship |

## Antes de escribir una línea: mirá los datos reales

- `data/parts/<vehicle>_*.json` tiene `length_m`/`diameter_m` reales — no ojees proporciones, leelos.
- `data/engine_clusters/*.json` ya tiene las posiciones reales de montaje de cada motor (patrón
  octaweb de Falcon 9, anillos de Starship, etc.) — usá esas posiciones, no inventes un layout.
- `PartDefinition.VehicleFamily` (`vehicle_family` en JSON) es el campo que ya existe para
  distinguir familias — verificá que esté poblado en TODAS las piezas de la familia nueva antes de
  ramificar `BuildFromVessel` por él (una familia con el campo poblado solo a medias vuelve a caer
  en el fallback genérico sin avisar).

## Cuándo especializar una familia vs. dejarla en el camino genérico

El dispatch vive en `BuildFromVessel` (`scripts/VesselRenderer.cs:99-114`): si la familia no matchea
ningún caso especial, cae a `BuildGenericVessel` → `CreateGenericPartNode`, que arma un
`MeshInstance3D` por pieza con `StandardMaterial3D` plano y color de categoría. Eso está bien
para piezas genéricas sueltas (tanques stock, piezas de sandbox) — **no está bien para un vehículo
real con nombre propio** que un jugador va a reconocer a simple vista (Falcon 9, New Glenn, y
cualquier vehículo histórico/con marca que se agregue después). Si estás agregando ese tipo de
vehículo, andá directo a una rama dedicada tipo `BuildFalcon9Section`, no dejes que caiga en el
genérico "para después".

## Verificación — no alcanza con que compile

Ver la skill `visual-testing` para el patrón completo de captura headless (harness temporal,
limpieza obligatoria). Reglas específicas para trabajo de detalle visual:

- Comparación antes/después en el MISMO ángulo de cámara — si el bug era angle-dependent (como la
  costura blanca), un solo screenshot desde un ángulo que no lo mostraba "confirma" un fix que en
  realidad no arregló nada.
- Metodología del V0.5 (`PLAN_VISUAL_REALISM.md`): referencia real → captura actual → diferencia
  observable explícita → criterio de aceptación, antes de dar por cerrado un ítem. "Implementado"
  no es lo mismo que "verificado contra referencia".
- Si tocás una familia ya cerrada (Starship/Super Heavy), no la toques — está verificada. Un
  vehículo nuevo va en una rama de dispatch separada, nunca modificando `BuildFullStack`/
  `BuildSuperHeavyOnly`/`BuildStarshipSection`.

## GOTCHAS

- `AddGenericEngineBell` ya pone `CapTop=false, CapBottom=false` — es la única función del camino
  genérico que evita el bug de costura por defecto. Es la pista de que el resto del código nunca
  pensó en el problema, no de que esté resuelto en general.
- Un motor de vacío (segunda etapa) tiene campana más larga y de mayor expansión que su
  equivalente a nivel del mar — no uses el mismo mesh escalado sin mirar el dato real de la
  campana en `data/engines/*.json`.
- Hollín/chamuscado estático (un gradiente de material fijo) es suficiente para un motor que no
  tiene modelo térmico dinámico — no repliques el sistema de charring por zona térmica de
  Starship si el vehículo no tiene ese modelo de daño.
