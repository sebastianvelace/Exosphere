namespace Exosphere.Game;

using Godot;
using System.Collections.Generic;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Physics;

/// <summary>
/// Re-entry plasma glow around the active vessel. Driven by the SAME convective heat
/// flux the simulation uses for thermal damage (<see cref="ThermalModel.ComputeHeatFlux"/>),
/// so the visible fireball tracks the real physics: it ignites only when ρ·v³ heating is
/// significant and brightens from deep orange to white-hot as the flux climbs.
///
/// Resplandor de plasma de reentrada: usa el mismo flujo de calor que el daño térmico
/// del sim, así que la bola de fuego aparece cuando el calentamiento real es alto.
/// </summary>
[GlobalClass]
public partial class ReentryPlasmaController : Node3D
{
    private MeshInstance3D?     _shock;   // bright shock cap on the windward side
    private MeshInstance3D?     _wake;    // trailing ionised wake
    private ShaderMaterial?     _shockMat;
    private StandardMaterial3D? _wakeMat;
    private readonly List<EdgeGlow> _edgeGlows = new();
    private double _visualSampleTimer;
    private Node3D? _vesselFrame;

    // Plasma is a presentation effect. Its physical inputs can be sampled at 20 Hz while
    // the deterministic thermal solver continues at the simulation tick rate.
    private const double VisualSamplePeriodSeconds = 1.0 / 20.0;

    private enum EdgeKind { Nose, Belly, Flap }

    private sealed class EdgeGlow
    {
        public MeshInstance3D Mesh = null!;
        public StandardMaterial3D Mat = null!;
        public Vector3d LocalPosition;
        public Vector3 BaseScale;
        public float Weight;
        public float Delay;
        public EdgeKind Kind;
    }

    // Heat-flux thresholds (W/m²). Below FLUX_THRESH there is no visible plasma;
    // at/above FLUX_PEAK the glow is saturated white-hot.
    const double FLUX_THRESH = VehicleVisualPhysics.VisibleReentryFluxWm2;
    const double FLUX_PEAK   = VehicleVisualPhysics.SaturatedReentryFluxWm2;

    private const string ShockShaderPath = "res://assets/shaders/reentry_glow.gdshader";

    public override void _Ready()
    {
        var shockShader = GD.Load<Shader>(ShockShaderPath);
        var noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 4f };
        var noiseTex = new NoiseTexture2D { Noise = noise, Width = 256, Height = 256, Seamless = true };

        _shockMat = new ShaderMaterial { Shader = shockShader };
        _shockMat.SetShaderParameter("noise_tex", noiseTex);
        _shockMat.SetShaderParameter("heat_level", 0f);
        _shockMat.SetShaderParameter("halo_size", 0.12f);
        _shock = new MeshInstance3D
        {
            Name    = "ReentryShock",
            Mesh    = new SphereMesh { Radius = 0.95f, Height = 1.9f, RadialSegments = 24, Rings = 12 },
            Visible = false,
        };
        _shock.SetSurfaceOverrideMaterial(0, _shockMat);
        AddChild(_shock);

        // Trailing wake — a long faint cone of ionised gas behind the vessel.
        _wakeMat = new StandardMaterial3D
        {
            Transparency             = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode                = BaseMaterial3D.BlendModeEnum.Add,
            AlbedoColor              = new Color(1.0f, 0.30f, 0.08f, 0f),
            EmissionEnabled          = true,
            Emission                 = new Color(1.0f, 0.28f, 0.08f),
            EmissionEnergyMultiplier = 1.4f,
            CullMode                 = BaseMaterial3D.CullModeEnum.Disabled,
            ShadingMode              = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _wake = new MeshInstance3D
        {
            Name    = "ReentryWake",
            Mesh    = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.75f, Height = 10f, RadialSegments = 20 },
            Visible = false,
        };
        _wake.SetSurfaceOverrideMaterial(0, _wakeMat);
        AddChild(_wake);

        BuildLocalizedEdgeGlows();

        // Break-up VFX lives as a sibling effect at the same render origin. We host it
        // here (rather than in SimulationBridge) so the plasma + break-up re-entry
        // effects are created and torn down together. It watches the active vessel's
        // thermal-destruction state on its own.
        // El breakup cuelga del mismo origen de render; observa la destrucción térmica solo.
        AddChild(new ReentryBreakupController { Name = "ReentryBreakup" });
    }

    public override void _Process(double delta)
    {
        if (_shock == null || _wake == null || _shockMat == null || _wakeMat == null) return;

        _visualSampleTimer -= System.Math.Max(0.0, delta);
        if (_visualSampleTimer > 0.0) return;
        _visualSampleTimer = VisualSamplePeriodSeconds;

        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        if (bridge == null || vessel == null || vessel.IsDestroyed)
        {
            SetEffectsVisible(false);
            return;
        }

        // Follow the authoritative floating-origin frame used by the vessel renderer. A
        // second world transform reconstructed here can drift visually during the EDL flip,
        // leaving the shock ring detached from the ship even while the physics is correct.
        SyncToVesselFrame();

        var body = bridge.Universe.GetDominantBody(vessel.Position);

        double density  = body.GetAtmosphericDensity(vessel.Position);
        var    surfVel  = vessel.GetSurfaceVelocity(body);
        double flux     = vessel.ComputeStagnationHeatFlux(density, surfVel);

        double fluxIntensity = System.Math.Clamp(
            (flux - FLUX_THRESH) / (FLUX_PEAK - FLUX_THRESH), 0.0, 1.0);
        string? phaseName = MissionManager.Instance?.Phase.ToString();
        double intensity = VehicleVisualPhysics.ReentryPlasmaVisualIntensity(
            fluxIntensity, phaseName);

        if (intensity < 0.01)
        {
            SetEffectsVisible(false);
            return;
        }

        SetCoreEffectsVisible(true);

        // The plasma root inherits the vessel orientation, so use local airflow coordinates
        // for every child effect. This keeps shock, wake and edge glows attached through
        // belly-flop, flip and the vertical catch approach.
        Vector3 flowDir = ToGodot(vessel.Orientation.Inverse().Rotate(
            new Vector3d(surfVel.X, surfVel.Y, surfVel.Z)));
        flowDir = flowDir.LengthSquared() > 1e-6f ? flowDir.Normalized() : Vector3.Up;

        // ── Windward concentration ─────────────────────────────────────────
        // The visible bow shock should sit on, and brighten with, the face that
        // actually MEETS the flow — exactly the windward face the heat model uses.
        // We express the airflow in the vessel's local frame and read how squarely
        // the ventral heat shield faces it: belly-first (good attitude) lights the
        // ventral cap hard; a bad attitude spreads a hotter, more chaotic glow.
        //
        // Concentración windward: el shock se ata a la cara que encara el flujo
        // (la misma que usa el modelo térmico), brillando con su alineación real.
        Vector3d flowLocal = vessel.Orientation.Inverse().Rotate(
            new Vector3d(surfVel.X, surfVel.Y, surfVel.Z));
        double windward = ThermalModel.WindwardFactor(flowLocal);   // 1 = belly squarely into flow

        // Vessel body centre in render space. The plasma controller is a sibling of
        // the renderer, so it must apply the vessel orientation itself.
        bool hasSH = HasSuperHeavy(vessel);
        Vector3 bodyCentre = new(0f, hasSH ? 30f : 8f, 0f);

        // Shock sits on the windward (leading) face; wake streams out behind.
        _shock.Position = bodyCentre + flowDir * (hasSH ? 5f : 1.35f);
        _wake.Position  = bodyCentre - flowDir * (hasSH ? 7f : 4.0f);

        // Flatten the shock into the flow plane so it reads as a bow cap hugging the
        // windward face rather than a round ball. Squash along the flow axis.
        OrientYAxis(_shock, flowDir);

        // Orient the wake cylinder (+Y axis) to point downstream (away from the flow).
        OrientYAxis(_wake, -flowDir);

        // Flicker so the plasma boils rather than glowing flat.
        float flicker = 0.8f + (float)(GD.Randf() * 0.4f);

        float align     = (float)windward;
        float misalign  = 1f - align;
        float concentr  = Mathf.Lerp(0.55f, 1.0f, align);
        float exposure  = Mathf.Lerp(1.15f, 1.0f, align);
        float hudGuard  = Mathf.Lerp(0.68f, 1.0f, align);
        float dangerMul = Mathf.Lerp(1.30f, 1.0f, align);

        float heatLevel = Mathf.Clamp(
            (float)intensity * concentr * hudGuard * dangerMul * flicker, 0f, 1f);
        _shockMat.SetShaderParameter("heat_level", heatLevel);

        float wakeAlpha = (float)(intensity * 0.20f) * Mathf.Lerp(0.55f, 1.0f, misalign);
        _wakeMat.AlbedoColor              = new Color(1.0f, 0.28f, 0.08f, wakeAlpha);
        _wakeMat.EmissionEnergyMultiplier = (float)(0.85 + intensity * 1.7);

        // Windward cap: flatten along the flow (thin, wide bow shock) and grow with flux.
        float sizeScale = Mathf.Lerp(0.62f, 1.12f, (float)intensity);
        float flatten   = Mathf.Lerp(0.85f, 0.42f, align);       // belly-first = thinner cap
        // Mesh local +Y now points along the flow (set by OrientYAxis above), so
        // squash Y to press the cap onto the windward face.
        _shock.Scale = new Vector3(sizeScale * (1f + 0.4f * align), sizeScale * flatten, sizeScale * (1f + 0.4f * align));

        UpdateLocalizedEdgeGlows((float)intensity, align, exposure, hasSH, flicker);
    }

    private void BuildLocalizedEdgeGlows()
    {
        const float shipSpanScale = (2f + 11.5f + 4.357f) / 21.25f;
        AddEdgeGlow("NoseLeadingHeat", new Vector3(-1.18f, 19.6f * shipSpanScale, 0.0f),
            new Vector3(0.52f, 1.70f, 0.52f), weight: 1.0f, delay: 0.0f, kind: EdgeKind.Nose);

        AddEdgeGlow("BellyCenterHeat", new Vector3(-1.72f, 9.8f * shipSpanScale, 0.0f),
            new Vector3(0.11f, 5.4f, 0.11f), weight: 0.52f, delay: 0.05f, kind: EdgeKind.Belly);

        // Edge anchors track the V1.1 flap layout (smaller forward, longer aft elevons).
        AddEdgeGlow("FwdFlapLeftHeat",  new Vector3(-1.62f, 15.35f * shipSpanScale,  1.12f),
            new Vector3(0.14f, 1.95f, 0.14f), weight: 0.80f, delay: 0.10f, kind: EdgeKind.Flap);
        AddEdgeGlow("FwdFlapRightHeat", new Vector3(-1.62f, 15.35f * shipSpanScale, -1.12f),
            new Vector3(0.14f, 1.95f, 0.14f), weight: 0.80f, delay: 0.13f, kind: EdgeKind.Flap);

        AddEdgeGlow("AftFlapLeftHeat",   new Vector3(-1.78f, 3.85f * shipSpanScale,  1.25f),
            new Vector3(0.22f, 4.05f, 0.22f), weight: 0.78f, delay: 0.14f, kind: EdgeKind.Flap);
        AddEdgeGlow("AftFlapRightHeat",  new Vector3(-1.78f, 3.85f * shipSpanScale, -1.25f),
            new Vector3(0.22f, 4.05f, 0.22f), weight: 0.78f, delay: 0.17f, kind: EdgeKind.Flap);
    }

    private void AddEdgeGlow(string name, Vector3 position, Vector3 baseScale,
        float weight, float delay, EdgeKind kind)
    {
        var mat = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            AlbedoColor = new Color(1.0f, 0.30f, 0.08f, 0f),
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.34f, 0.08f),
            EmissionEnergyMultiplier = 0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };

        Mesh mesh = kind == EdgeKind.Nose
            ? new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 24, Rings = 12 }
            : new CylinderMesh { TopRadius = 0.9f, BottomRadius = 0.9f, Height = 1f, RadialSegments = 16 };

        var glow = new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            Position = position,
            Scale = baseScale,
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = mat,
        };

        AddChild(glow);
        _edgeGlows.Add(new EdgeGlow
        {
            Mesh = glow,
            Mat = mat,
            LocalPosition = new Vector3d(position.X, position.Y, position.Z),
            BaseScale = baseScale,
            Weight = weight,
            Delay = delay,
            Kind = kind,
        });
    }

    private void UpdateLocalizedEdgeGlows(float intensity,
        float align, float exposure, bool hasSH, float flicker)
    {
        // The localized cues are authored for standalone Starship. During full-stack
        // ascent/reentry, hide them rather than drawing heat on the booster stack.
        if (hasSH)
        {
            foreach (var edge in _edgeGlows) SetEdgeVisible(edge, false);
            return;
        }

        float edgeBase = Mathf.Clamp((intensity - 0.08f) / 0.92f, 0f, 1f);
        float focus = Mathf.Lerp(0.58f, 1.0f, align);
        float misalign = 1f - align;
        float noseBoost  = Mathf.Lerp(0.55f, 1.20f, misalign);
        float bellyScale = Mathf.Lerp(0.38f, 0.95f, align);
        float flapBoost  = Mathf.Lerp(0.75f, 1.25f, misalign);

        foreach (var edge in _edgeGlows)
        {
            float k = Mathf.Clamp((edgeBase - edge.Delay) / (1f - edge.Delay), 0f, 1f);
            if (k <= 0.01f)
            {
                SetEdgeVisible(edge, false);
                continue;
            }

            float zoneMul = edge.Kind switch
            {
                EdgeKind.Nose  => noseBoost,
                EdgeKind.Belly => bellyScale,
                EdgeKind.Flap  => flapBoost,
                _              => 1f,
            };
            float alphaCap = edge.Kind switch
            {
                EdgeKind.Nose  => 0.46f,
                EdgeKind.Belly => 0.22f,
                EdgeKind.Flap  => 0.40f,
                _              => 0.45f,
            };

            k *= edge.Weight * focus * flicker * zoneMul;
            SetEdgeVisible(edge, true);
            edge.Mesh.Position = ToGodot(edge.LocalPosition);
            OrientYAxis(edge.Mesh, Vector3.Up);
            edge.Mesh.Scale = edge.BaseScale * (0.75f + 0.65f * k);

            float white = Mathf.Clamp(k * 1.25f, 0f, 1f);
            var col = new Color(
                1.0f,
                0.22f + 0.60f * white,
                0.06f + 0.42f * white,
                Mathf.Min(0.16f + 0.54f * k, alphaCap));

            edge.Mat.AlbedoColor = col;
            edge.Mat.Emission = new Color(col.R, col.G, col.B);
            edge.Mat.EmissionEnergyMultiplier = (2.2f + 8.5f * k) * exposure;
        }
    }

    private void SetEffectsVisible(bool visible)
    {
        SetCoreEffectsVisible(visible);
        foreach (var edge in _edgeGlows)
            SetEdgeVisible(edge, visible);
    }

    private void SetCoreEffectsVisible(bool visible)
    {
        if (_shock != null && _shock.Visible != visible) _shock.Visible = visible;
        if (_wake != null && _wake.Visible != visible) _wake.Visible = visible;
    }

    private static void SetEdgeVisible(EdgeGlow edge, bool visible)
    {
        if (edge.Mesh.Visible != visible) edge.Mesh.Visible = visible;
    }

    private static bool HasSuperHeavy(Vessel vessel)
    {
        var parts = vessel.Parts.Parts;
        for (int partIndex = 0; partIndex < parts.Count; partIndex++)
        {
            var part = parts[partIndex];
            if (part.Definition.IsStarshipFamily
                && part.Definition.HasVehicleRole("booster"))
                return true;
        }
        return false;
    }

    private static Vector3 ToGodot(Vector3d v) =>
        new((float)v.X, (float)v.Y, (float)v.Z);

    private void SyncToVesselFrame()
    {
        if (_vesselFrame == null || !GodotObject.IsInstanceValid(_vesselFrame))
        {
            _vesselFrame = GetTree().Root.FindChild(
                "ActiveVesselRenderer", true, false) as Node3D;
        }

        if (_vesselFrame == null) return;
        Position = _vesselFrame.Position;
        Quaternion = _vesselFrame.Quaternion;
    }

    // Rotate a mesh whose local +Y should point along <paramref name="dir"/>.
    private static void OrientYAxis(Node3D node, Vector3 dir)
    {
        if (dir.LengthSquared() < 1e-6f) return;
        Vector3 up   = dir.Normalized();
        Vector3 axis = Vector3.Up.Cross(up);
        if (axis.LengthSquared() < 1e-6f) { node.Basis = Basis.Identity; return; }
        float angle = Vector3.Up.AngleTo(up);
        node.Basis = new Basis(axis.Normalized(), angle);
    }
}
