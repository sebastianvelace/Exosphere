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

    // JSON node positions are metres. The preview renderer uses 1 Godot unit =
    // 2.8 m, and its generic path applies a bottom datum shift. Keeping the
    // conversion here prevents selection/highlight/markers from drifting away
    // from the rendered geometry. Specialized Starship geometry has fixed visual
    // anchors below because its procedural renderer intentionally ignores the
    // arbitrary order of VAB assembly connections.
    private const float MetresPerUnit = 2.8f;

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
        _ when def.DiameterM > 0.0 => (float)(def.DiameterM / MetresPerUnit) * 0.5f,
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
        _ when def.LengthM > 0.0 => (float)(def.LengthM / MetresPerUnit) * 0.5f,
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
            // Marca TODO nodo disponible: verde si la pieza de catálogo encaja, rojo si
            // no. Antes solo se dibujaban los verdes, así que un nodo incompatible no
            // mostraba nada — el jugador no tenía forma de saber "por qué no puedo pegar
            // acá" sin ya haber intentado el click.
            // Marks EVERY available node: green if the catalog part fits, red if not.
            // Previously only the fitting ones were drawn, so an incompatible node showed
            // nothing at all — the player had no way to see "why can't I attach here"
            // without already having tried the click.
            bool fits = false;
            foreach (var childNode in childDef.AttachmentNodes)
            {
                if (VesselAssembly.NodesAreCompatible(node, childNode))
                {
                    fits = true;
                    break;
                }
            }

            var local = ToRenderUnits(node.Position);
            AddNodeMarker(selectedInstanceId, node.Id, parentPos + local, fits);
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

    /// <summary>
    /// Vertical extent (render units) and largest part radius across the whole current
    /// assembly, for the preview camera to auto-frame the full vehicle instead of a
    /// fixed pose that clips a tall stack or leaves a short one tiny in the corner.
    /// </summary>
    public bool TryGetAssemblyBounds(out float minY, out float maxY, out float maxRadius)
    {
        minY = 0f; maxY = 0f; maxRadius = 0f;
        if (_assembly == null || _catalog == null || _assembly.Parts.Count == 0) return false;

        var map = BuildPositionMap();
        bool any = false;
        foreach (var part in _assembly.Parts)
        {
            if (!map.TryGetValue(part.InstanceId, out var pos)) continue;
            var def = _catalog[part.DefinitionId];
            float r = PartRadius(def);
            float h = PartHalfHeight(def);
            if (!any)
            {
                minY = pos.Y - h;
                maxY = pos.Y + h;
                any = true;
            }
            else
            {
                minY = Mathf.Min(minY, pos.Y - h);
                maxY = Mathf.Max(maxY, pos.Y + h);
            }
            maxRadius = Mathf.Max(maxRadius, r);
        }
        return any;
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

    /// <summary>Meta key read back by ConstructionController to explain a click on a
    /// red (incompatible) marker instead of attempting the attach and throwing.</summary>
    public const string MetaCompatible = "compatible";

    private void AddNodeMarker(string instanceId, string nodeId, Vector3 pos, bool compatible)
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
        body.SetMeta(MetaCompatible, compatible);

        var shape = new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.9f } };
        body.AddChild(shape);

        // Marcador visible: verde y opaco si la pieza elegida encaja acá, rojo y más
        // tenue si no — así el jugador ve el "no" antes de hacer click, no después.
        // Visible marker: green and opaque where the chosen part fits, dim red where it
        // doesn't — so the player sees the "no" before clicking, not after.
        var mesh = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.55f, Height = 1.1f, RadialSegments = 16, Rings = 8 },
        };
        var color = compatible
            ? new Color(0.30f, 1.0f, 0.40f, 0.65f)
            : new Color(1.0f, 0.30f, 0.30f, 0.40f);
        var emission = compatible
            ? new Color(0.20f, 0.9f, 0.30f)
            : new Color(0.9f, 0.20f, 0.20f);
        mesh.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
        {
            AlbedoColor     = color,
            Transparency    = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode     = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission        = emission,
        });
        body.AddChild(mesh);
        AddChild(body);
    }

    // ── Position helpers ──────────────────────────────────────────────────

    // Recorre la asamblea desde la raíz acumulando offsets de nodo en metros y
    // después los convierte al espacio de preview. Para familias con renderer
    // procedural fijo (Starship/Super Heavy), usa los anclajes que corresponden a
    // la geometría dibujada; de otro modo replica el datum inferior del renderer
    // genérico.
    // Walk the assembly in metres, then convert to preview space. Procedural
    // Starship/Super Heavy uses the rendered geometry's fixed anchors; other craft
    // mirror the generic renderer's bottom datum.
    private System.Collections.Generic.Dictionary<string, Vector3> BuildPositionMap()
    {
        var raw = new System.Collections.Generic.Dictionary<string, Vector3>();
        if (_assembly == null || _catalog == null) return raw;

        var rootId = _assembly.RootInstanceId;
        if (rootId == null) return raw;

        void Assign(string id, Vector3 pos)
        {
            raw[id] = pos;
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

        if (HasProceduralStarship())
            return BuildProceduralStarshipMap(raw);

        float minY = float.PositiveInfinity;
        foreach (var part in _assembly.Parts)
        {
            if (!raw.TryGetValue(part.InstanceId, out var pos)) continue;
            var def = _catalog[part.DefinitionId];
            float halfHeight = (float)Math.Max(0.0, def.LengthM * 0.5);
            minY = Mathf.Min(minY, pos.Y - halfHeight);
        }
        if (float.IsPositiveInfinity(minY)) minY = 0f;
        float datumShift = -minY / MetresPerUnit;

        var render = new System.Collections.Generic.Dictionary<string, Vector3>();
        foreach (var pair in raw)
            render[pair.Key] = pair.Value / MetresPerUnit + Vector3.Up * datumShift;
        return render;
    }

    private bool HasProceduralStarship()
    {
        if (_assembly == null) return false;
        return _assembly.Parts.Any(part => _catalog![part.DefinitionId].IsStarshipFamily);
    }

    private System.Collections.Generic.Dictionary<string, Vector3> BuildProceduralStarshipMap(
        System.Collections.Generic.Dictionary<string, Vector3> raw)
    {
        var result = new System.Collections.Generic.Dictionary<string, Vector3>();
        bool hasBooster = _assembly!.Parts.Any(part =>
            _catalog![part.DefinitionId].HasVehicleRole("booster"));
        bool hasShip = _assembly.Parts.Any(part =>
            _catalog![part.DefinitionId].HasVehicleRole("command")
            || _catalog[part.DefinitionId].HasVehicleRole("ship_engines")
            || _catalog[part.DefinitionId].HasVehicleRole("tank"));

        // These anchors mirror VesselRenderer's procedural Flight-7 layout:
        // SH body ~= y 2..23.36, hot-stage ~= 23.36..25.36 and the ship skirt
        // starts at 25.36. They are presentation coordinates, never simulation
        // coordinates; changing the renderer's layout requires changing this map.
        float boosterCenter = 12.7f;
        float shipEngineCenter = hasBooster ? 25.2f : 0.0f;
        float shipSectionCenter = hasBooster ? 34.0f : 8.0f;
        float hotStageCenter = hasBooster ? 24.35f : 0.0f;

        foreach (var part in _assembly.Parts)
        {
            var def = _catalog![part.DefinitionId];
            string role = def.VehicleRole;
            float y;
            if (role.Equals("booster", StringComparison.OrdinalIgnoreCase))
                y = boosterCenter;
            else if (role.Equals("ship_engines", StringComparison.OrdinalIgnoreCase))
                y = shipEngineCenter;
            else if (role.Equals("hotstage", StringComparison.OrdinalIgnoreCase))
                y = hotStageCenter;
            else if (role.Equals("tank", StringComparison.OrdinalIgnoreCase)
                     || role.Equals("command", StringComparison.OrdinalIgnoreCase))
                y = shipSectionCenter;
            else if (def.Id.Equals("starship_landing_gear", StringComparison.OrdinalIgnoreCase))
                y = shipEngineCenter;
            else if (hasBooster && hasShip)
                y = shipSectionCenter;
            else
                y = raw.TryGetValue(part.InstanceId, out var fallback)
                    ? fallback.Y / MetresPerUnit
                    : 0f;
            result[part.InstanceId] = new Vector3(0f, y, 0f);
        }
        return result;
    }

    private static Vector3 ToRenderUnits(double[] position) => new(
        (float)(position[0] / MetresPerUnit),
        (float)(position[1] / MetresPerUnit),
        (float)(position[2] / MetresPerUnit));

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
