# VAB builder workflow

**Estado:** funcional V1 · **Fecha:** 2026-07-11

## Flujo de jugador

El ensamblador adopta el principio central de Spaceflight Simulator: elegir una pieza y
colocarla debe ser más rápido que configurar el editor.

1. Pulsa **Starter** o **Starship** para comenzar desde una plantilla, o filtra el catálogo.
2. Doble clic en una pieza: si el vehículo está vacío se convierte en raíz; si existe una
   selección, el VAB conecta automáticamente el primer par libre `bottom↔top` compatible.
3. Para una conexión específica, selecciona la pieza en la lista/preview y pulsa un nodo verde.
4. `Ctrl+Z`, `Ctrl+Y` y `Delete` revierten, rehacen o eliminan el subárbol seleccionado.
5. El bloque **Flight readiness** mantiene Launch desactivado hasta tener control, motor,
   árbol conectado, thrust activo y TWR terrestre superior a 1.

El modo manual Parent node / Child node permanece como herramienta avanzada. Guardar/cargar,
browser de vehículos y lanzamiento usan el mismo `VesselCraftDefinition`, por lo que no hay
una representación paralela solo para UI.

## Validación en el núcleo

`VesselAssembly` ofrece:

- `CompatibleAttachments`: pares de nodos libres válidos y ordenados de forma determinista;
- `AttachPartAutomatically`: conexión rápida con errores accionables;
- `ValidateForLaunch`: errores que bloquean y warnings de margen TWR/delta-v;
- serialización existente para snapshots de undo/redo.

Las pruebas cubren conexión automática, nodo ocupado, validación incompleta/lista, borrado de
subárbol, round-trip y construcción completa de la plantilla Starship.

## Ciclo rápido de desarrollo

```bash
bash tools/vab_quick_check.sh
```

Este comando usa builds incrementales, ejecuta solo `ConstructionRegressionTests` y carga
`Construction.tscn` durante dos frames. Medido localmente: ~5,6 s con caché caliente y ~14 s
tras recompilar los assemblies, frente a más de un minuto de la suite completa. La CI completa
se reserva para el gate antes del commit.

## Pendientes con mayor retorno

1. Drag-and-drop real desde catálogo al nodo, con ghost de colocación.
2. Simetría radial 2×/3×/4× y rotación de piezas.
3. Gizmos de traslado/rotación para piezas radiales y stage organizer visual.
4. Cámara orbit/pan/zoom dedicada y encuadre automático según bounds del craft.
5. Mini ensayo estático desde VAB: ignition de 2 s, thrust/masa/estabilidad sin cargar vuelo.
