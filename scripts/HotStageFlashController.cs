namespace Exosphere.Game;

using Godot;
using System.Linq;

/// <summary>Presentation-only vented exhaust at the hot-stage interface.</summary>
[GlobalClass]
public partial class HotStageFlashController : Node3D
{
    public const float HotStageInterfaceRenderY = 71f / 2.8f;
    private const float Duration = 1.75f;
    private const int PuffCount = 12;
    private readonly MeshInstance3D[] _puffs = new MeshInstance3D[PuffCount];
    private readonly ShaderMaterial[] _materials = new ShaderMaterial[PuffCount];
    private const int JetCount = 12;
    private const float VentRadius = 1.62f;      // render units, ~4.5 m: the hot-stage ring
    private readonly MeshInstance3D[] _jets = new MeshInstance3D[JetCount];
    private readonly ShaderMaterial[] _jetMaterials = new ShaderMaterial[JetCount];
    private float _jetIntensity;
    private MeshInstance3D? _interstagePlume;
    private ShaderMaterial? _interstagePlumeMaterial;
    private float _interstagePlumeOpacity;
    private float _age = Duration + 1f;
    private bool _wired;
    private bool _lastHotStageOverlap;
    private bool _overlapBurstStarted;
    private string _frameName = "ActiveVesselRenderer";
    private Node3D? _vesselFrame;

    public bool IsVesselFrameSynchronized =>
        _vesselFrame != null && GodotObject.IsInstanceValid(_vesselFrame);
    public bool IsInterstagePlumeVisible =>
        _interstagePlume != null && _interstagePlume.Visible;
    public float InterstagePlumeLocalY =>
        _interstagePlume?.Position.Y ?? float.NaN;
    public float InterstagePlumeOpacity => _interstagePlumeOpacity;
    /// <summary>Luminous vented-exhaust level, 0..1, following delivered Ship chamber pressure.</summary>
    public float VentedExhaustIntensity => _jetIntensity;

    public override void _Ready()
    {
        Name = "HotStageFlashController";
        var shader = GD.Load<Shader>("res://assets/shaders/hotstage_gas.gdshader");
        // Soft volume silhouettes avoid both a solid ring and a rectangular flame.
        var mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 16, Rings = 8 };
        for (int i = 0; i < PuffCount; i++)
        {
            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("seed", i * 2.17f);
            material.SetShaderParameter("opacity", 0f);
            _materials[i] = material;
            _puffs[i] = new MeshInstance3D
            {
                Name = $"HotStageGas{i}", Mesh = mesh, MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_puffs[i]);
        }
        _interstagePlumeMaterial = new ShaderMaterial { Shader = shader };
        _interstagePlumeMaterial.SetShaderParameter("seed", 37.0f);
        _interstagePlumeMaterial.SetShaderParameter("opacity", 0f);
        _interstagePlume = new MeshInstance3D
        {
            Name = "HotStagePlume",
            Mesh = new SphereMesh
            {
                Radius = 1f,
                Height = 2f,
                RadialSegments = 24,
                Rings = 12,
            },
            MaterialOverride = _interstagePlumeMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_interstagePlume);
        var flameShader = GD.Load<Shader>("res://assets/shaders/hotstage_flame.gdshader");
        var jetMesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 16, Rings = 8 };
        for (int i = 0; i < JetCount; i++)
        {
            var material = new ShaderMaterial { Shader = flameShader };
            material.SetShaderParameter("seed", i * 1.37f);
            material.SetShaderParameter("intensity", 0f);
            _jetMaterials[i] = material;
            _jets[i] = new MeshInstance3D
            {
                Name = $"HotStageVentJet{i}", Mesh = jetMesh, MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false,
            };
            AddChild(_jets[i]);
        }
        TryWireSignal();
        Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!_wired) TryWireSignal();
        bool overlap = SimulationBridge.Instance?.ActiveVessel?.IsHotStageOverlapping == true;
        if (overlap && !_lastHotStageOverlap)
        {
            _frameName = "ActiveVesselRenderer";
            _vesselFrame = null;
            StartBurst();
            _overlapBurstStarted = true;
        }
        _lastHotStageOverlap = overlap;

        // While attached, the Ship's lit Raptors can only exhaust through the vented ring.
        // Rise with delivered chamber pressure; after separation the jets collapse quickly.
        float shipFraction = overlap ? ShipChamberFraction() : 0f;
        float rate = shipFraction > _jetIntensity ? 6f : 1.8f;
        _jetIntensity = Mathf.MoveToward(_jetIntensity, shipFraction, rate * (float)delta);
        bool jetsActive = _jetIntensity > 0.005f;
        if (_age > Duration && !jetsActive)
        {
            foreach (var jet in _jets) jet.Visible = false;
            return;
        }

        SyncToVesselFrame();
        if (_age <= Duration) _age += (float)delta;
        float t = Mathf.Clamp(_age / Duration, 0f, 1f);
        Visible = IsVesselFrameSynchronized && (t < 1f || jetsActive);
        UpdateVentJets();
        bool plumeVisible = IsVesselFrameSynchronized && t < 1f;
        if (_interstagePlume != null && _interstagePlumeMaterial != null)
        {
            // A broad, tapering gas core makes the ignition-before-separation state
            // readable in a still without introducing a solid emissive ring. The
            // mesh is presentation-only and shares the same soft gas shader as the
            // surrounding puffs.
            float height = 3.8f + 1.4f * t;
            float radius = 1.15f + 0.65f * t;
            _interstagePlume.Position = new Vector3(
                0f,
                HotStageInterfaceRenderY - height * 0.5f,
                0f);
            _interstagePlume.Scale = new Vector3(radius, height * 0.5f, radius);
            _interstagePlumeMaterial.SetShaderParameter("age", t);
            _interstagePlumeOpacity = 0.16f * (1f - t) * (1f - t);
            _interstagePlumeMaterial.SetShaderParameter("opacity", _interstagePlumeOpacity);
            _interstagePlume.Visible = plumeVisible && _interstagePlumeOpacity > 0.005f;
        }
        // Bounded presentation envelope, not gas dynamics: including puff extent,
        // radius stays below 4.1 units (~11.5 m). Density falls during expansion.
        for (int i = 0; i < PuffCount; i++)
        {
            float angle = i * Mathf.Tau / PuffCount + 0.12f * Mathf.Sin(i * 3.1f);
            float radius = 1.6f + 1.35f * t;
            float size = 0.36f + 0.65f * t;
            _puffs[i].Position = new Vector3(
                Mathf.Cos(angle) * radius,
                HotStageInterfaceRenderY - 0.3f - t * (0.6f + 0.3f * (i % 3)),
                Mathf.Sin(angle) * radius);
            _puffs[i].Scale = new Vector3(size, size * 0.7f, size);
            _materials[i].SetShaderParameter("age", t);
            _materials[i].SetShaderParameter("opacity", 0.22f * (1f - t) * (1f - t));
        }
    }

    private void UpdateVentJets()
    {
        float length = 1.4f + 3.6f * _jetIntensity;
        float width = 0.30f + 0.22f * _jetIntensity;
        float time = (float)(SimulationBridge.Instance?.Universe.CurrentTime ?? 0.0);
        for (int i = 0; i < JetCount; i++)
        {
            float angle = (i + 0.5f) * Mathf.Tau / JetCount;
            var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            // Vented gas leaves radially, canted slightly aft toward the booster dome.
            var axis = (outward + Vector3.Down * 0.22f).Normalized();
            var jet = _jets[i];
            jet.Visible = _jetIntensity > 0.005f;
            var tangent = Vector3.Up.Cross(axis).Normalized();
            // Right-handed (x × y = z): a mirrored basis flips the normals the shader's
            // soft edge depends on, and the whole jet then renders fully transparent.
            jet.Basis = new Basis(axis, tangent.Cross(axis).Normalized(), tangent)
                * Basis.FromScale(new Vector3(length * 0.5f, width, width));
            jet.Position = new Vector3(0f, HotStageInterfaceRenderY - 0.25f, 0f)
                + outward * VentRadius + axis * (length * 0.5f);
            _jetMaterials[i].SetShaderParameter("intensity", _jetIntensity);
            _jetMaterials[i].SetShaderParameter("simulation_time", time);
        }
    }

    private static float ShipChamberFraction()
    {
        var vessel = SimulationBridge.Instance?.ActiveVessel;
        if (vessel == null) return 0f;
        double sum = 0.0;
        int count = 0;
        foreach (var part in vessel.Parts.Parts)
        {
            if (!part.Definition.IsStarshipFamily
                || !part.Definition.HasVehicleRole("ship_engines")) continue;
            foreach (var state in part.EngineStates)
            {
                sum += state.ChamberPressureFraction;
                count++;
            }
        }
        return count > 0 ? (float)System.Math.Clamp(sum / count, 0.0, 1.0) : 0f;
    }

    private void TryWireSignal()
    {
        var bridge = SimulationBridge.Instance;
        if (bridge == null) return;
        bridge.VesselStaged += OnVesselStaged;
        _wired = true;
    }

    private void OnVesselStaged(string detachedVesselId)
    {
        var bridge = SimulationBridge.Instance;
        var debris = bridge?.Universe?.Vessels.FirstOrDefault(v => v.Id == detachedVesselId);
        bool detachedBooster = debris?.Parts.Parts.Any(p =>
            p.Definition.IsStarshipFamily && p.Definition.HasVehicleRole("booster")) ?? false;
        bool activeShip = bridge?.ActiveVessel?.Parts.Parts.Any(p =>
            p.Definition.IsStarshipFamily && (p.Definition.HasVehicleRole("ship_engines")
                || p.Definition.HasVehicleRole("command"))) ?? false;
        if (!detachedBooster || !activeShip) return;

        // Bridge creates SHDebris before this signal. The standalone Ship skirt
        // is rebased to Y=0; retaining the stack offset there puts gas past its nose.
        // The departing booster retains the original interface datum.
        _frameName = "SHDebris_" + detachedVesselId[..8];
        _vesselFrame = null;
        if (!_overlapBurstStarted) StartBurst();
        SyncToVesselFrame();
    }

    private void StartBurst()
    {
        _age = 0f;
        // Initialize every puff on the next update before exposing the root.
        Visible = false;
    }

    private void SyncToVesselFrame()
    {
        if (!IsVesselFrameSynchronized)
            _vesselFrame = GetTree().Root.FindChild(_frameName, true, false) as Node3D;
        if (!IsVesselFrameSynchronized) return;
        Position = _vesselFrame!.Position;
        Quaternion = _vesselFrame.Quaternion;
    }

    public override void _ExitTree()
    {
        if (_wired && SimulationBridge.Instance != null)
            SimulationBridge.Instance.VesselStaged -= OnVesselStaged;
    }
}
