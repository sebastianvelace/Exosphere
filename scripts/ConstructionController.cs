namespace Exosphere.Game;

using Exosphere.Simulation.Construction;
using Exosphere.Simulation.Parts;
using Godot;
using System.Text.Json;

public partial class ConstructionController : Control
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private PartCatalog? _catalog;
    private VesselAssembly? _assembly;

    private ItemList _catalogList = null!;
    private ItemList _stackList = null!;
    private ItemList _craftBrowser = null!;
    private OptionButton _parentNode = null!;
    private OptionButton _childNode = null!;
    private LineEdit _craftName = null!;
    private Label _stats = null!;
    private Label _status = null!;
    private VesselRenderer? _previewRenderer;

    // ── Direct 3D manipulation (preview picking) ──────────────────────────
    private SubViewport?        _previewViewport;
    private Camera3D?           _previewCamera;
    private VabPickingLayer?    _picking;
    private MeshInstance3D?     _highlight;
    private StandardMaterial3D? _highlightMat;
    private string?             _selectedInstanceId;

    public override void _Ready()
    {
        BuildUi();
        LoadCatalog();
    }

    public VesselAssembly? CurrentAssembly => _assembly;

    private void LoadCatalog()
    {
        string path = ProjectSettings.GlobalizePath("res://data/parts");
        _catalog = PartCatalog.LoadFromDirectory(path);
        _assembly = new VesselAssembly(_catalog);
        _picking?.Configure(_catalog);

        _catalogList.Clear();
        foreach (var part in _catalog.AllParts)
        {
            int idx = _catalogList.AddItem($"{part.Name}  [{part.CategoryStr}]");
            _catalogList.SetItemMetadata(idx, part.Id);
        }

        Refresh();
        RefreshCraftBrowser();
        SetStatus("Catalog loaded.");
    }

    private void BuildUi()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        var root = new HBoxContainer { Name = "Layout" };
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.OffsetLeft = 24;
        root.OffsetTop = 24;
        root.OffsetRight = -24;
        root.OffsetBottom = -24;
        AddChild(root);

        var catalogBox = BuildPanel("Catalog");
        root.AddChild(catalogBox);
        _catalogList = new ItemList { Name = "CatalogList", CustomMinimumSize = new Vector2(380, 0) };
        _catalogList.ItemSelected += _ => { RefreshNodeChoices(); RefreshNodeMarkers(); };
        catalogBox.AddChild(_catalogList);

        // Navegador de craft files guardados / Saved craft-file browser.
        var browserBox = BuildPanel("Saved Crafts");
        root.AddChild(browserBox);
        _craftBrowser = new ItemList { Name = "CraftBrowser", CustomMinimumSize = new Vector2(280, 0) };
        // Doble-click o selección carga el craft / select to load that craft.
        _craftBrowser.ItemActivated += OnCraftBrowserActivated;
        browserBox.AddChild(_craftBrowser);

        var refreshCrafts = new Button { Text = "Refresh List" };
        refreshCrafts.Pressed += RefreshCraftBrowser;
        browserBox.AddChild(refreshCrafts);

        var loadSelected = new Button { Text = "Load Selected" };
        loadSelected.Pressed += OnLoadSelectedCraft;
        browserBox.AddChild(loadSelected);

        var mid = BuildPanel("Assembly");
        root.AddChild(mid);
        _stackList = new ItemList { Name = "StackList", CustomMinimumSize = new Vector2(420, 0) };
        _stackList.ItemSelected += OnStackListSelected;
        mid.AddChild(_stackList);

        var controls = new GridContainer { Columns = 2 };
        mid.AddChild(controls);
        controls.AddChild(new Label { Text = "Craft name" });
        _craftName = new LineEdit { Text = "Constructed Vessel" };
        controls.AddChild(_craftName);
        controls.AddChild(new Label { Text = "Parent node" });
        _parentNode = new OptionButton();
        controls.AddChild(_parentNode);
        controls.AddChild(new Label { Text = "Child node" });
        _childNode = new OptionButton();
        controls.AddChild(_childNode);

        var buttons = new HBoxContainer();
        mid.AddChild(buttons);

        var addRoot = new Button { Text = "Set Root" };
        addRoot.Pressed += OnSetRoot;
        buttons.AddChild(addRoot);

        var attach = new Button { Text = "Attach" };
        attach.Pressed += OnAttach;
        buttons.AddChild(attach);

        var delete = new Button { Text = "Delete" };
        delete.Pressed += OnDelete;
        buttons.AddChild(delete);

        var export = new Button { Text = "Export Vessel" };
        export.Pressed += OnExport;
        buttons.AddChild(export);

        var save = new Button { Text = "Save" };
        save.Pressed += OnSave;
        buttons.AddChild(save);

        var load = new Button { Text = "Load" };
        load.Pressed += OnLoad;
        buttons.AddChild(load);

        var launch = new Button { Text = "Launch" };
        launch.Pressed += OnLaunch;
        buttons.AddChild(launch);

        var info = BuildPanel("Stats / Preview");
        root.AddChild(info);
        _stats = new Label { Name = "Stats", CustomMinimumSize = new Vector2(300, 180) };
        info.AddChild(_stats);

        var viewportContainer = new SubViewportContainer
        {
            Name = "PreviewContainer",
            CustomMinimumSize = new Vector2(360, 360),
            Stretch = true,
        };
        // Recibimos input del ratón sobre la preview para hacer picking 3D.
        // We receive mouse input over the preview to drive 3D picking.
        viewportContainer.GuiInput += OnPreviewGuiInput;
        info.AddChild(viewportContainer);

        var viewport = new SubViewport
        {
            Size = new Vector2I(720, 720),
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            // Hacemos el raycast manualmente sobre DirectSpaceState; no usamos el
            // picking automático de Godot. / We raycast manually against
            // DirectSpaceState; Godot's automatic object picking stays off.
            PhysicsObjectPicking = false,
        };
        viewportContainer.AddChild(viewport);
        _previewViewport = viewport;

        var previewRoot = new Node3D { Name = "PreviewRoot" };
        viewport.AddChild(previewRoot);
        previewRoot.AddChild(new DirectionalLight3D
        {
            Name = "PreviewLight",
            LightEnergy = 1.8f,
            RotationDegrees = new Vector3(-45f, -35f, 0f),
        });
        var camera = new Camera3D
        {
            Name = "PreviewCamera",
            Fov = 32f,
            Current = true,
        };
        previewRoot.AddChild(camera);
        camera.LookAtFromPosition(new Vector3(0f, 28f, 86f), new Vector3(0f, 20f, 0f), Vector3.Up);
        _previewCamera = camera;
        _previewRenderer = new VesselRenderer { Name = "PreviewVessel" };
        previewRoot.AddChild(_previewRenderer);

        // Capa de picking (hermana del renderer): cuerpos de colisión invisibles
        // por pieza y por nodo disponible. / Picking layer (renderer sibling):
        // invisible collision bodies per part and per available node.
        _picking = new VabPickingLayer { Name = "Picking" };
        previewRoot.AddChild(_picking);

        // Resaltado de la pieza seleccionada: una cápsula translúcida.
        // Selected-part highlight: a translucent capsule.
        _highlight = new MeshInstance3D { Name = "Highlight", Visible = false };
        _highlightMat = new StandardMaterial3D
        {
            AlbedoColor     = new Color(1.0f, 0.85f, 0.20f, 0.28f),
            Transparency    = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode     = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode        = BaseMaterial3D.CullModeEnum.Disabled,
            EmissionEnabled = true,
            Emission        = new Color(0.9f, 0.7f, 0.1f),
        };
        previewRoot.AddChild(_highlight);

        _status = new Label { Name = "Status", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        info.AddChild(_status);

        var hint = new Label
        {
            Name = "Hint",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = "Preview: click a part to select. With a catalog part chosen, "
                 + "click a green node marker to attach. [Del] removes the selection.",
        };
        info.AddChild(hint);
    }

    private static VBoxContainer BuildPanel(string title)
    {
        var panel = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        panel.AddChild(new Label { Text = title });
        return panel;
    }

    private void OnSetRoot()
    {
        if (_assembly == null || _catalog == null) return;
        string? partId = SelectedCatalogPartId();
        if (partId == null) { SetStatus("Select a catalog part first."); return; }

        try
        {
            _assembly.AddRoot(partId);
            Refresh();
            SetStatus("Root part set.");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private void OnAttach()
    {
        if (_assembly == null) return;
        string? parentId = SelectedAssemblyInstanceId();
        string? partId = SelectedCatalogPartId();
        string? parentNode = SelectedOptionMetadata(_parentNode);
        string? childNode = SelectedOptionMetadata(_childNode);
        if (parentId == null || partId == null || parentNode == null || childNode == null)
        {
            SetStatus("Select a parent part, catalog part, and compatible nodes.");
            return;
        }

        try
        {
            _assembly.AttachPart(parentId, parentNode, partId, childNode);
            Refresh();
            SetStatus("Part attached.");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private void OnDelete()
    {
        if (_assembly == null) return;
        string? instanceId = SelectedAssemblyInstanceId();
        if (instanceId == null) { SetStatus("Select an assembly part first."); return; }
        _assembly.DeletePart(instanceId);
        Refresh();
        SetStatus("Part removed.");
    }

    private void OnExport()
    {
        if (_assembly == null) return;
        try
        {
            var vessel = _assembly.ToVessel(CraftName());
            SetStatus($"Export ready: {vessel.Name}, {vessel.Parts.Parts.Count} parts.");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private void OnSave()
    {
        if (_assembly == null) return;
        try
        {
            string dir = CraftDirectory();
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, $"{SafeCraftFileName()}.json");
            var craft = _assembly.ToCraft(CraftName());
            System.IO.File.WriteAllText(path, JsonSerializer.Serialize(craft, JsonOptions));
            RefreshCraftBrowser();
            SetStatus($"Saved craft: {path}");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private void OnLoad()
    {
        string path = System.IO.Path.Combine(CraftDirectory(), $"{SafeCraftFileName()}.json");
        if (!System.IO.File.Exists(path))
        {
            SetStatus($"Craft file not found: {path}");
            return;
        }
        LoadCraftFromPath(path);
    }

    // Carga un craft desde una ruta concreta / load a craft from an explicit path.
    private bool LoadCraftFromPath(string path)
    {
        if (_catalog == null) return false;
        try
        {
            var craft = JsonSerializer.Deserialize<VesselCraftDefinition>(
                System.IO.File.ReadAllText(path), JsonOptions);
            if (craft == null)
            {
                SetStatus("Craft file is empty or invalid.");
                return false;
            }

            _assembly = VesselAssembly.FromCraft(_catalog, craft);
            _craftName.Text = craft.Name;
            Refresh();
            SetStatus($"Loaded craft: {craft.Name} ({craft.Parts.Count} parts)");
            return true;
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
            return false;
        }
    }

    // Reconstruye la lista de craft guardados desde user://crafts.
    // Rebuilds the saved-craft list from user://crafts.
    private void RefreshCraftBrowser()
    {
        _craftBrowser.Clear();
        string dir = CraftDirectory();
        if (!System.IO.Directory.Exists(dir))
        {
            int empty = _craftBrowser.AddItem("(no saved crafts)");
            _craftBrowser.SetItemDisabled(empty, true);
            return;
        }

        string[] files = System.IO.Directory.GetFiles(dir, "*.json");
        System.Array.Sort(files);
        if (files.Length == 0)
        {
            int empty = _craftBrowser.AddItem("(no saved crafts)");
            _craftBrowser.SetItemDisabled(empty, true);
            return;
        }

        foreach (string file in files)
        {
            string label = DescribeCraftFile(file);
            int idx = _craftBrowser.AddItem(label);
            _craftBrowser.SetItemMetadata(idx, file);
        }
    }

    // Lee un craft para mostrar nombre + nº de piezas + masa (best-effort).
    // Reads a craft to show name + part count + mass (best-effort).
    private string DescribeCraftFile(string path)
    {
        string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
        try
        {
            var craft = JsonSerializer.Deserialize<VesselCraftDefinition>(
                System.IO.File.ReadAllText(path), JsonOptions);
            if (craft == null) return $"{fileName}  [invalid]";

            string detail = $"{craft.Parts.Count} parts";
            if (_catalog != null && craft.Parts.Count > 0)
            {
                try
                {
                    var metrics = VesselAssembly.FromCraft(_catalog, craft).ComputeMetrics();
                    detail += $", {metrics.WetMass / 1000.0:F1} t";
                }
                catch
                {
                    // Craft no reconstruible (catálogo cambiado); mostramos solo el conteo.
                    // Craft can't be rebuilt (catalog changed); show count only.
                }
            }
            return $"{craft.Name}  [{detail}]";
        }
        catch
        {
            return $"{fileName}  [unreadable]";
        }
    }

    private void OnCraftBrowserActivated(long index) => LoadCraftAt((int)index);

    private void OnLoadSelectedCraft()
    {
        int[] selected = _craftBrowser.GetSelectedItems();
        if (selected.Length == 0) { SetStatus("Select a saved craft first."); return; }
        LoadCraftAt(selected[0]);
    }

    private void LoadCraftAt(int index)
    {
        if (index < 0 || index >= _craftBrowser.ItemCount) return;
        var meta = _craftBrowser.GetItemMetadata(index);
        if (meta.VariantType == Variant.Type.Nil) return; // entrada vacía / empty placeholder
        LoadCraftFromPath(meta.AsString());
    }

    private void OnLaunch()
    {
        if (_assembly == null) return;
        try
        {
            CraftLaunchRequest.Set(_assembly.ToCraft(CraftName()));
            GetTree().ChangeSceneToFile("res://scenes/flight/Flight.tscn");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private void Refresh()
    {
        _stackList.Clear();
        if (_assembly != null && _catalog != null)
        {
            foreach (var part in _assembly.Parts)
            {
                PartDefinition def = _catalog[part.DefinitionId];
                string prefix = part.ParentInstanceId == null ? "ROOT" : "  +";
                int idx = _stackList.AddItem($"{prefix} {def.Name}");
                _stackList.SetItemMetadata(idx, part.InstanceId);
            }

            var m = _assembly.ComputeMetrics();
            _stats.Text =
                $"Wet mass: {m.WetMass / 1000.0:F1} t\n" +
                $"Dry mass: {m.DryMass / 1000.0:F1} t\n" +
                $"Propellant: {m.PropellantMass / 1000.0:F1} t\n" +
                $"SL thrust: {m.SeaLevelThrust / 1000.0:F0} kN\n" +
                $"SL TWR: {m.SeaLevelTwr:F2}\n" +
                $"Vac delta-v: {m.VacuumDeltaV:F0} m/s";

            UpdatePreview();
        }

        RefreshNodeChoices();
    }

    private void UpdatePreview()
    {
        if (_assembly == null || _previewRenderer == null) return;

        try
        {
            if (_assembly.Parts.Count == 0)
            {
                _previewRenderer.Visible = false;
                _picking?.RebuildSelectionBodies(_assembly);
                ClearSelection();
                return;
            }

            _previewRenderer.Visible = true;
            _previewRenderer.BuildFromVessel(_assembly.ToVessel(CraftName()));
        }
        catch
        {
            _previewRenderer.Visible = false;
        }

        // Reconstruimos los cuerpos de picking tras cada cambio de asamblea, y
        // revalidamos la selección/resaltado y los marcadores de nodo.
        // Rebuild picking bodies after every assembly change, then revalidate the
        // selection/highlight and the node markers.
        _picking?.RebuildSelectionBodies(_assembly);
        if (_selectedInstanceId != null && _assembly.Parts.All(p => p.InstanceId != _selectedInstanceId))
            ClearSelection();
        else
            UpdateHighlight();
        RefreshNodeMarkers();
    }

    private void RefreshNodeChoices()
    {
        _parentNode.Clear();
        _childNode.Clear();
        if (_assembly == null || _catalog == null) return;

        string? parentId = SelectedAssemblyInstanceId();
        if (parentId != null)
        {
            foreach (var node in _assembly.AvailableNodes(parentId))
                AddNodeOption(_parentNode, node);
        }

        string? partId = SelectedCatalogPartId();
        if (partId != null && _catalog.TryGet(partId, out var def))
        {
            foreach (var node in def.AttachmentNodes.Where(n => !n.Type.Equals("engine_bell", StringComparison.OrdinalIgnoreCase)))
                AddNodeOption(_childNode, node);
        }
    }

    private static void AddNodeOption(OptionButton option, AttachmentNodeDef node)
    {
        int idx = option.ItemCount;
        option.AddItem($"{node.Id} ({node.Type}, {node.Size})");
        option.SetItemMetadata(idx, node.Id);
    }

    private string? SelectedCatalogPartId()
    {
        int[] selected = _catalogList.GetSelectedItems();
        return selected.Length == 0 ? null : _catalogList.GetItemMetadata(selected[0]).AsString();
    }

    private string? SelectedAssemblyInstanceId()
    {
        // La selección en la preview 3D tiene prioridad; si no, caemos a la lista.
        // The 3D preview selection wins; otherwise fall back to the stack list.
        if (_selectedInstanceId != null) return _selectedInstanceId;
        int[] selected = _stackList.GetSelectedItems();
        return selected.Length == 0 ? null : _stackList.GetItemMetadata(selected[0]).AsString();
    }

    private static string? SelectedOptionMetadata(OptionButton option)
    {
        int selected = option.Selected;
        return selected < 0 ? null : option.GetItemMetadata(selected).AsString();
    }

    // ── Direct 3D manipulation ────────────────────────────────────────────

    // Selección desde la lista textual → sincroniza el resaltado de la preview.
    // Selection from the text list → syncs the preview highlight.
    private void OnStackListSelected(long index)
    {
        string? id = index < 0 || index >= _stackList.ItemCount
            ? null
            : _stackList.GetItemMetadata((int)index).AsString();
        SelectInstance(id, syncList: false);
        RefreshNodeChoices();
    }

    // Click sobre la preview 3D: raycast desde la cámara hacia los cuerpos de
    // picking. Si pega en un marcador de nodo → adjunta; si pega en una pieza →
    // la selecciona; si no pega en nada → deselecciona.
    // Click on the 3D preview: raycast from the camera against the picking
    // bodies. Hit a node marker → attach; hit a part → select; miss → deselect.
    private void OnPreviewGuiInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            HandlePreviewClick(mb.Position);
        }
    }

    private void HandlePreviewClick(Vector2 localPos)
    {
        if (_previewViewport == null || _previewCamera == null || _picking == null) return;

        // La posición viene en coords del contenedor; escalamos al tamaño real
        // del SubViewport (el contenedor estira la imagen).
        // localPos is in container coords; scale it to the SubViewport's real
        // size (the container stretches the rendered image).
        var container = _previewViewport.GetParent<SubViewportContainer>();
        Vector2 vpSize = _previewViewport.Size;
        Vector2 cSize = container.Size;
        Vector2 vpPos = cSize.X > 0 && cSize.Y > 0
            ? new Vector2(localPos.X / cSize.X * vpSize.X, localPos.Y / cSize.Y * vpSize.Y)
            : localPos;

        Vector3 from = _previewCamera.ProjectRayOrigin(vpPos);
        Vector3 dir  = _previewCamera.ProjectRayNormal(vpPos);

        var space = _previewViewport.World3D.DirectSpaceState;
        var query = new PhysicsRayQueryParameters3D
        {
            From = from,
            To   = from + dir * 1000f,
            CollisionMask = _picking.PickCollisionMask,
            CollideWithAreas  = false,
            CollideWithBodies = true,
        };
        var hit = space.IntersectRay(query);
        if (hit.Count == 0)
        {
            ClearSelection();
            RefreshNodeChoices();
            SetStatus("Selection cleared.");
            return;
        }

        var collider = hit["collider"].As<Node>();
        string kind = collider != null ? (string)collider.GetMeta(VabPickingLayer.MetaKind, "") : "";

        if (kind == VabPickingLayer.KindNode)
        {
            string instanceId = (string)collider!.GetMeta(VabPickingLayer.MetaInstance, "");
            string nodeId     = (string)collider.GetMeta(VabPickingLayer.MetaNode, "");
            AttachAtNode(instanceId, nodeId);
        }
        else if (kind == VabPickingLayer.KindPart)
        {
            string instanceId = (string)collider!.GetMeta(VabPickingLayer.MetaInstance, "");
            SelectInstance(instanceId, syncList: true);
            RefreshNodeChoices();
            SetStatus("Part selected.");
        }
    }

    // Adjunta la pieza de catálogo elegida en el nodo clickeado. Resuelve el
    // childNode compatible automáticamente (el primero que encaje).
    // Attaches the chosen catalog part at the clicked node, auto-resolving the
    // first compatible child node.
    private void AttachAtNode(string parentInstanceId, string parentNodeId)
    {
        if (_assembly == null || _catalog == null) return;
        string? partId = SelectedCatalogPartId();
        if (partId == null) { SetStatus("Select a catalog part first."); return; }
        if (!_catalog.TryGet(partId, out var childDef)) { SetStatus("Unknown catalog part."); return; }

        var parentPart = _assembly.Parts.FirstOrDefault(p => p.InstanceId == parentInstanceId);
        if (parentPart == null) return;
        var parentDef  = _catalog[parentPart.DefinitionId];
        var parentNode = parentDef.AttachmentNodes.FirstOrDefault(n => n.Id == parentNodeId);
        if (parentNode == null) return;

        var childNode = childDef.AttachmentNodes
            .FirstOrDefault(n => VesselAssembly.NodesAreCompatible(parentNode, n));
        if (childNode == null) { SetStatus("No compatible node on the catalog part."); return; }

        try
        {
            var attached = _assembly.AttachPart(parentInstanceId, parentNodeId, partId, childNode.Id);
            Refresh();
            SelectInstance(attached.InstanceId, syncList: true);
            SetStatus($"Attached {childDef.Name}.");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    // Selecciona una pieza (o ninguna) y actualiza resaltado, lista y marcadores.
    // Selects a part (or none) and updates highlight, list, and markers.
    private void SelectInstance(string? instanceId, bool syncList)
    {
        _selectedInstanceId = instanceId;
        UpdateHighlight();
        RefreshNodeMarkers();

        if (syncList)
        {
            _stackList.DeselectAll();
            if (instanceId != null)
            {
                for (int i = 0; i < _stackList.ItemCount; i++)
                {
                    if (_stackList.GetItemMetadata(i).AsString() == instanceId)
                    {
                        _stackList.Select(i);
                        break;
                    }
                }
            }
        }
    }

    private void ClearSelection() => SelectInstance(null, syncList: true);

    // Coloca y dimensiona la cápsula de resaltado sobre la pieza seleccionada.
    // Places and sizes the highlight capsule over the selected part.
    private void UpdateHighlight()
    {
        if (_highlight == null || _picking == null) return;
        if (_selectedInstanceId == null
            || !_picking.TryGetPartBounds(_selectedInstanceId, out var center, out var radius, out var halfHeight))
        {
            _highlight.Visible = false;
            return;
        }

        _highlight.Mesh = new CapsuleMesh
        {
            Radius = radius * 1.08f,
            Height = Mathf.Max(halfHeight * 2f, radius * 2f) * 1.04f,
        };
        if (_highlightMat != null)
            _highlight.SetSurfaceOverrideMaterial(0, _highlightMat);
        _highlight.Position = center;
        _highlight.Visible  = true;
    }

    // Muestra los marcadores de nodo si hay pieza seleccionada + pieza de catálogo.
    // Shows node markers when a part is selected and a catalog part is chosen.
    private void RefreshNodeMarkers()
    {
        _picking?.ShowNodeMarkers(_selectedInstanceId, SelectedCatalogPartId());
    }

    // [Del] borra el subárbol seleccionado desde la preview.
    // [Del] removes the selected subtree from the preview.
    public override void _UnhandledKeyInput(InputEvent ev)
    {
        if (ev is InputEventKey k && k.Pressed && k.Keycode == Key.Delete && _selectedInstanceId != null)
        {
            OnDelete();
            GetViewport().SetInputAsHandled();
        }
    }

    private void SetStatus(string message)
    {
        if (_status != null) _status.Text = message;
        GD.Print($"[VAB] {message}");
    }

    private string CraftName()
    {
        string name = _craftName?.Text.Trim() ?? "";
        return string.IsNullOrWhiteSpace(name) ? "Constructed Vessel" : name;
    }

    private string SafeCraftFileName()
    {
        var chars = CraftName()
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_')
            .ToArray();
        string name = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(name) ? "craft" : name;
    }

    private static string CraftDirectory() =>
        ProjectSettings.GlobalizePath("user://crafts");
}
