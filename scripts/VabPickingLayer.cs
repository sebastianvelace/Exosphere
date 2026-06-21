namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Construction;

/// <summary>
/// Capa de picking 3D para el VAB. NO renderiza el cohete (eso lo hace
/// <see cref="VesselRenderer"/>); en su lugar levanta cuerpos de colisión
/// invisibles — uno por pieza para SELECCIONAR, y uno por attachment-node
/// disponible para ADJUNTAR — derivados directamente de la
/// <see cref="VesselAssembly"/>. Vive como hermano del renderer bajo el mismo
/// PreviewRoot, así comparte espacio local con la preview sin tocar el renderer.
///
/// 3D picking layer for the VAB. It does NOT render the rocket (that is
/// <see cref="VesselRenderer"/>'s job); instead it raises invisible collision
/// bodies — one per part to SELECT, one per available attachment node to
/// ATTACH — derived straight from the <see cref="VesselAssembly"/>. It lives as
/// a sibling of the renderer under the same PreviewRoot, sharing the preview's
/// local space without editing the renderer.
/// </summary>
public partial class VabPickingLayer : Node3D
{
    // Metadata keys used to tag the picking bodies so a raycast hit can be
    // routed back to a part instance or an attachment node.
    public const string MetaKind     = "vab_kind";   // "part" | "node"
    public const string MetaInstance = "vab_inst";   // instance id (both kinds)
    public const string MetaNode     = "vab_node";   // node id (node kind only)

    public const string KindPart = "part";
    public const string KindNode = "node";

    // Collision layer used ONLY by the picking bodies, so the raycast never
    // hits unrelated colliders. Bit 20 is well away from gameplay layers.
    private const uint PickLayer = 1u << 19;

    // Node positions in the JSON are authored in the SAME units the renderer
    // lays parts out in (e.g. Super Heavy "top" at y=20 maps to the renderer's
    // y=20 body top), so we build the picking bodies directly in that space.
    // Las posiciones de nodo del JSON están en las MISMAS unidades en que el
    // renderer coloca las piezas, así que construimos en ese espacio sin escalar.

    public uint PickCollisionMask => PickLayer;

    private PartCatalog?    _catalog;
    private VesselAssembly? _assembly;

    // Approximate collision radius (render units) per part, so the selection
    // body roughly wraps the rendered geometry.
    private static float PartRadius(PartDefinition def) => def.Id switch
    {
        "super_heavy_booster" => 1.7f,
        "starship_engines"    => 1.7f,
        "starship_command"    => 1.7f,
        _ => def.Category switch
        {
            PartCategory.FuelTank => 0.75f,
            PartCategory.Engine   => 0.8f,
            PartCategory.Command  => 0.7f,
            _                     => 0.55f,
        },
    };

    // Approximate collision half-height (render units) per part.
    private static float PartHalfHeight(PartDefinition def) => def.Id switch
    {
        "super_heavy_booster" => 11f,   // SH body spans ~y=2..20
        "starship_engines"    => 8f,    // ship section spans ~16 u
        "starship_command"    => 8f,
        _ => def.Category switch
        {
            PartCategory.FuelTank => MaxStackHalf(def, 1.8f),
            PartCategory.Engine   => 1.0f,
            PartCategory.Command  => 0.8f,
            _                     => 0.4f,
        },
    };

    // Derive a tank's half-height from its own stack-node span when present.
    private static float MaxStackHalf(PartDefinition def, float fallback)
    {
        float max = 0f;
        foreach (var n in def.AttachmentNodes)
        {
            if (n.Type.Equals("stack", System.StringComparison.OrdinalIgnoreCase))
                max = Mathf.Max(max, Mathf.Abs((float)n.Position[1]));
        }
        return max > 0f ? max : fallback;
    }

    public void Configure(PartCatalog catalog) => _catalog = catalog;

    /// <summary>
    /// Reconstruye los cuerpos de selección (uno por pieza) para la asamblea
    /// actual. / Rebuilds the selection bodies (one per part) for the assembly.
    /// </summary>
    public void RebuildSelectionBodies(VesselAssembly assembly)
    {
        _assembly = assembly;
        Clear();
        if (_catalog == null) return;

        var map = BuildPositionMap();
        foreach (var part in _assembly.Parts)
        {
            if (!map.TryGetValue(part.InstanceId, out var pos)) continue;
            var def = _catalog[part.DefinitionId];
            AddPartBody(part.InstanceId, def, pos);
        }
    }

    /// <summary>
    /// Muestra marcadores clickeables en los attachment-nodes DISPONIBLES y
    /// COMPATIBLES con la pieza de catálogo elegida, sobre la pieza seleccionada.
    /// Shows clickable markers on the AVAILABLE attachment nodes of the selected
    /// part that are COMPATIBLE with the chosen catalog part.
    /// </summary>
    public void ShowNodeMarkers(string? selectedInstanceId, string? catalogPartId)
    {
        ClearNodeMarkers();
        if (_catalog == null || _assembly == null) return;
        if (selectedInstanceId == null || catalogPartId == null) return;
        if (!_catalog.TryGet(catalogPartId, out var childDef)) return;

        var map = BuildPositionMap();
        if (!map.TryGetValue(selectedInstanceId, out var parentPos)) return;

        foreach (var node in _assembly.AvailableNodes(selectedInstanceId))
        {
            // Solo nodos donde la pieza de catálogo realmente encaja.
            // Only nodes where the catalog part actually fits.
            bool fits = false;
            foreach (var childNode in childDef.AttachmentNodes)
            {
                if (VesselAssembly.NodesAreCompatible(node, childNode))
                {
                    fits = true;
                    break;
                }
            }
            if (!fits) continue;

            var local = new Vector3((float)node.Position[0], (float)node.Position[1], (float)node.Position[2]);
            AddNodeMarker(selectedInstanceId, node.Id, parentPos + local);
        }
    }

    /// <summary>
    /// Centro y dimensiones aproximadas (en unidades de render) del cuerpo de
    /// selección de una pieza, para colocar el resaltado. / Approximate center
    /// and size (render units) of a part's selection body, for the highlight.
    /// </summary>
    public bool TryGetPartBounds(string instanceId, out Vector3 center, out float radius, out float halfHeight)
    {
        center = Vector3.Zero; radius = 0f; halfHeight = 0f;
        if (_catalog == null) return false;

        var map = BuildPositionMap();
        if (!map.TryGetValue(instanceId, out var pos)) return false;
        var part = FindPart(instanceId);
        if (part == null) return false;

        var def    = _catalog[part.DefinitionId];
        center     = pos;
        radius     = PartRadius(def);
        halfHeight = PartHalfHeight(def);
        return true;
    }

    public void ClearNodeMarkers()
    {
        foreach (var child in GetChildren())
        {
            if (child is Node3D n && (string)n.GetMeta(MetaKind, "") == KindNode)
                n.QueueFree();
        }
    }

    private void Clear()
    {
        foreach (var child in GetChildren()) child.QueueFree();
    }

    // ── Body construction ─────────────────────────────────────────────────

    private void AddPartBody(string instanceId, PartDefinition def, Vector3 pos)
    {
        var body = new StaticBody3D
        {
            Name             = $"Pick_{instanceId}",
            Position         = pos,
            CollisionLayer   = PickLayer,
            CollisionMask    = 0,
            InputRayPickable = true,
        };
        body.SetMeta(MetaKind, KindPart);
        body.SetMeta(MetaInstance, instanceId);

        var shape = new CollisionShape3D
        {
            Shape = new CapsuleShape3D
            {
                Radius = PartRadius(def),
                Height = Mathf.Max(PartHalfHeight(def) * 2f, PartRadius(def) * 2f),
            },
        };
        body.AddChild(shape);
        AddChild(body);
    }

    private void AddNodeMarker(string instanceId, string nodeId, Vector3 pos)
    {
        var body = new StaticBody3D
        {
            Name             = $"Node_{instanceId}_{nodeId}",
            Position         = pos,
            CollisionLayer   = PickLayer,
            CollisionMask    = 0,
            InputRayPickable = true,
        };
        body.SetMeta(MetaKind, KindNode);
        body.SetMeta(MetaInstance, instanceId);
        body.SetMeta(MetaNode, nodeId);

        var shape = new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.9f } };
        body.AddChild(shape);

        // Marcador visible: una pequeña esfera verde semitransparente.
        // Visible marker: a small translucent green sphere.
        var mesh = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.55f, Height = 1.1f, RadialSegments = 16, Rings = 8 },
        };
        mesh.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
        {
            AlbedoColor     = new Color(0.30f, 1.0f, 0.40f, 0.65f),
            Transparency    = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode     = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission        = new Color(0.20f, 0.9f, 0.30f),
        });
        body.AddChild(mesh);
        AddChild(body);
    }

    // ── Position helpers ──────────────────────────────────────────────────

    // Recorre la asamblea desde la raíz acumulando offsets de nodo (mismo
    // algoritmo que PartGraph.ComputePartLocalPositions, pero sobre AssemblyPart).
    // Walks the assembly from the root accumulating node offsets (same algorithm
    // as PartGraph.ComputePartLocalPositions, but over AssemblyPart records).
    private System.Collections.Generic.Dictionary<string, Vector3> BuildPositionMap()
    {
        var map = new System.Collections.Generic.Dictionary<string, Vector3>();
        if (_assembly == null || _catalog == null) return map;

        var rootId = _assembly.RootInstanceId;
        if (rootId == null) return map;

        void Assign(string id, Vector3 pos)
        {
            map[id] = pos;
            var parentPart = FindPart(id);
            if (parentPart == null) return;
            var parentDef = _catalog[parentPart.DefinitionId];

            foreach (var conn in _assembly.Connections)
            {
                if (conn.ParentInstanceId != id) continue;
                var childPart = FindPart(conn.ChildInstanceId);
                if (childPart == null) continue;
                var childDef = _catalog[childPart.DefinitionId];

                Vector3 pOff = NodeOffset(parentDef, conn.ParentNodeId);
                Vector3 cOff = NodeOffset(childDef, conn.ChildNodeId);
                Assign(conn.ChildInstanceId, pos + pOff - cOff);
            }
        }

        Assign(rootId, Vector3.Zero);
        return map;
    }

    private static Vector3 NodeOffset(PartDefinition def, string nodeId)
    {
        foreach (var n in def.AttachmentNodes)
            if (n.Id == nodeId)
                return new Vector3((float)n.Position[0], (float)n.Position[1], (float)n.Position[2]);
        return Vector3.Zero;
    }

    private AssemblyPart? FindPart(string instanceId)
    {
        if (_assembly == null) return null;
        foreach (var p in _assembly.Parts)
            if (p.InstanceId == instanceId) return p;
        return null;
    }
}
