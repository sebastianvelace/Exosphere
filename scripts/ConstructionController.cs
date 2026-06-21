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
    private OptionButton _parentNode = null!;
    private OptionButton _childNode = null!;
    private LineEdit _craftName = null!;
    private Label _stats = null!;
    private Label _status = null!;
    private VesselRenderer? _previewRenderer;

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

        _catalogList.Clear();
        foreach (var part in _catalog.AllParts)
        {
            int idx = _catalogList.AddItem($"{part.Name}  [{part.CategoryStr}]");
            _catalogList.SetItemMetadata(idx, part.Id);
        }

        Refresh();
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
        _catalogList.ItemSelected += _ => RefreshNodeChoices();
        catalogBox.AddChild(_catalogList);

        var mid = BuildPanel("Assembly");
        root.AddChild(mid);
        _stackList = new ItemList { Name = "StackList", CustomMinimumSize = new Vector2(420, 0) };
        _stackList.ItemSelected += _ => RefreshNodeChoices();
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
            CustomMinimumSize = new Vector2(360, 360),
            Stretch = true,
        };
        info.AddChild(viewportContainer);

        var viewport = new SubViewport
        {
            Size = new Vector2I(720, 720),
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        viewportContainer.AddChild(viewport);

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
        _previewRenderer = new VesselRenderer { Name = "PreviewVessel" };
        previewRoot.AddChild(_previewRenderer);

        _status = new Label { Name = "Status", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        info.AddChild(_status);
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
            SetStatus($"Saved craft: {path}");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private void OnLoad()
    {
        if (_catalog == null) return;
        try
        {
            string path = System.IO.Path.Combine(CraftDirectory(), $"{SafeCraftFileName()}.json");
            if (!System.IO.File.Exists(path))
            {
                SetStatus($"Craft file not found: {path}");
                return;
            }

            var craft = JsonSerializer.Deserialize<VesselCraftDefinition>(
                System.IO.File.ReadAllText(path), JsonOptions);
            if (craft == null)
            {
                SetStatus("Craft file is empty or invalid.");
                return;
            }

            _assembly = VesselAssembly.FromCraft(_catalog, craft);
            _craftName.Text = craft.Name;
            Refresh();
            SetStatus($"Loaded craft: {path}");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
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
                return;
            }

            _previewRenderer.Visible = true;
            _previewRenderer.BuildFromVessel(_assembly.ToVessel(CraftName()));
        }
        catch
        {
            _previewRenderer.Visible = false;
        }
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
        int[] selected = _stackList.GetSelectedItems();
        return selected.Length == 0 ? null : _stackList.GetItemMetadata(selected[0]).AsString();
    }

    private static string? SelectedOptionMetadata(OptionButton option)
    {
        int selected = option.Selected;
        return selected < 0 ? null : option.GetItemMetadata(selected).AsString();
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
