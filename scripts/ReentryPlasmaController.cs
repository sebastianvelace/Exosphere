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
    private ShaderMaterial?     _wakeMat;
    private MeshInstance3D? _halo;
    private ShaderMaterial? _haloMat;
    private readonly List<EdgeGlow> _edgeGlows = new();
    private double _visualSampleTimer;
    private Node3D? _vesselFrame;
    private VesselRenderer? _sheathRenderer;
    private bool _sheathFullStack;

    // Plasma is a presentation effect. Its physical inputs can be sampled at 20 Hz while
    // the deterministic thermal solver continues at the simulation tick rate.
    private const double VisualSamplePeriodSeconds = 1.0 / 20.0;
    // The Sutton-Graves flux range is physically linear, but a linear display response
    // leaves the lower half of the visible regime below one pixel at normal exposure.
    // This bounded exponent changes only presentation contrast; the heat flux and damage
    // equations remain untouched.
    private const double VisualFluxResponseExponent = 0.65;
    // Low-density aero descent still leaves a readable ionised wake. This gain is
    // presentation-only and remains multiplied by the physical visual intensity.
    private const double VisualWakeTailGain = 0.34;
    private const float VisualEdgeOnset = 0.04f;
    private const float VisualEdgeRange = 0.72f;

    // Last presentation sample, exposed only for deterministic visual evidence. These
    // values never feed the simulation, damage model or guidance.
    public double LastHeatFluxWm2 { get; private set; }
    public float LastFluxIntensity01 { get; private set; }
    public float LastVisualFluxInput01 { get; private set; }
    public float LastVisualIntensity01 { get; private set; }
    public float LastShockHeatLevel { get; private set; }
    public bool CoreEffectsVisible { get; private set; }

    private enum EdgeKind { Nose, Belly, Flap }

    private sealed class EdgeGlow
    {
        public MeshInstance3D Mesh = null!;
        public ShaderMaterial Mat = null!;
        public Vector3d LocalPosition;
        public Vector3 BaseScale;
        public float Weight;
        public float Delay;
        public EdgeKind Kind;
        public string? AnchorName;
        public Node3D? Anchor;
    }

    // Heat-flux thresholds (W/m²). Below FLUX_THRESH there is no visible plasma;
    // at/above FLUX_PEAK the glow is saturated white-hot.
    const double FLUX_THRESH = VehicleVisualPhysics.VisibleReentryFluxWm2;
    const double FLUX_PEAK   = VehicleVisualPhysics.SaturatedReentryFluxWm2;

    private const string ShockShaderPath = "res://assets/shaders/reentry_glow.gdshader";

    public override void _Ready()
    {
        var shockShader = GD.Load<Shader>(ShockShaderPath);
        // NoiseTexture2D samples in texels: 4 cycles/texel aliases into a nearly
        // uniform speckle. A few features per tile remain readable on the hull.
        var noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.018f };
        var noiseTex = new NoiseTexture2D { Noise = noise, Width = 256, Height = 256, Seamless = true };

        _shockMat = new ShaderMaterial { Shader = shockShader, RenderPriority = 6 };
        _shockMat.SetShaderParameter("noise_tex", noiseTex);
        _shockMat.SetShaderParameter("heat_level", 0f);
        _shockMat.SetShaderParameter("halo_size", 0.12f);
        _shock = new MeshInstance3D
        {
            Name    = "ReentryShock",
            Mesh    = new SphereMesh { Radius = 0.95f, Height = 1.9f, RadialSegments = 24, Rings = 12 },
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _shock.SetSurfaceOverrideMaterial(0, _shockMat);
        AddChild(_shock);
        // A faint outer optical path gives the sheath depth without a volume
        // raymarch or many particle layers on integrated graphics.
        _haloMat = new ShaderMaterial { Shader = shockShader, RenderPriority = 5 };
        _haloMat.SetShaderParameter("noise_tex", noiseTex);
        _haloMat.SetShaderParameter("effect_kind", 4);
        _haloMat.SetShaderParameter("halo_size", 0.48f);
        _haloMat.SetShaderParameter("opacity_gain", 0.22f);
        _halo = new MeshInstance3D
        {
            Name = "ReentrySheathHalo", Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = _haloMat,
        };
        AddChild(_halo);

        // Trailing wake — a long faint cone of ionised gas behind the vessel.
        _wakeMat = new ShaderMaterial { Shader = shockShader, RenderPriority = 6 };
        _wakeMat.SetShaderParameter("noise_tex", noiseTex);
        _wakeMat.SetShaderParameter("effect_kind", 1);
        _wakeMat.SetShaderParameter("heat_level", 0f);
        _wake = new MeshInstance3D
        {
            Name    = "ReentryWake",
            Mesh    = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 1f, Height = 2f, RadialSegments = 32, CapTop = false, CapBottom = false },
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

        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        if (bridge == null || vessel == null || vessel.IsDestroyed)
        {
            LastHeatFluxWm2 = 0.0;
            LastFluxIntensity01 = 0f;
            LastVisualFluxInput01 = 0f;
            LastVisualIntensity01 = 0f;
            LastShockHeatLevel = 0f;
            SetEffectsVisible(false);
            return;
        }

        // Follow the authoritative floating-origin frame used by the vessel renderer. A
        // second world transform reconstructed here can drift visually during the EDL flip,
        // leaving the shock ring detached from the ship even while the physics is correct.
        SyncToVesselFrame();

        // Pose and destruction visibility must follow every rendered frame. Only the
        // thermal/material sample is throttled; retain overshoot to avoid 15 Hz at 30 FPS.
        _visualSampleTimer -= System.Math.Max(0.0, delta);
        if (_visualSampleTimer > 0.0) return;
        _visualSampleTimer = VisualSamplePeriodSeconds
            + _visualSampleTimer % VisualSamplePeriodSeconds;

        var body = bridge.Universe.GetDominantBody(vessel.Position);

        double density  = body.GetAtmosphericDensity(vessel.Position);
        var    surfVel  = vessel.GetSurfaceVelocity(body);
        double flux     = vessel.ComputeStagnationHeatFlux(density, surfVel);

        double fluxIntensity = System.Math.Clamp(
            (flux - FLUX_THRESH) / (FLUX_PEAK - FLUX_THRESH), 0.0, 1.0);
        string? phaseName = MissionManager.Instance?.Phase.ToString();
        double visualFluxInput = System.Math.Pow(
            fluxIntensity, VisualFluxResponseExponent);
        double intensity = VehicleVisualPhysics.ReentryPlasmaVisualIntensity(
            visualFluxInput, phaseName);
        LastHeatFluxWm2 = double.IsFinite(flux) ? flux : 0.0;
        LastFluxIntensity01 = (float)fluxIntensity;
        LastVisualFluxInput01 = (float)visualFluxInput;
        LastVisualIntensity01 = (float)intensity;

        if (intensity < 0.01)
        {
            LastShockHeatLevel = 0f;
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

        // Vessel body centre in the local frame synchronized above.
        bool hasSH = HasSuperHeavy(vessel);
        Vector3 bodyCentre = new(0f, hasSH ? 30f : 8f, 0f);
        float halfLength = 1f;
        float radius = 1.6f;
        bool shipHull = false;
        if (_vesselFrame is VesselRenderer renderer)
            shipHull = renderer.TryGetReentryHull(out bodyCentre, out halfLength, out radius);

        // Stretch across the actual windward hull, rather than leaving a one-metre
        // fireball at its centre. The projected longitudinal axis collapses smoothly
        // for an axial entry; no orientation or force is assigned to the simulation.
        Vector3 projectedAxis = Vector3.Up - flowDir * Vector3.Up.Dot(flowDir);
        float broadside = projectedAxis.Length();
        Vector3 tangent = broadside > 1e-4f ? projectedAxis / broadside
            : flowDir.Cross(Vector3.Forward).Normalized();
        if (tangent.LengthSquared() < 1e-6f) tangent = Vector3.Right;
        Vector3 crossFlow = tangent.Cross(flowDir).Normalized();
        var bowBasis = new Basis(tangent, flowDir, crossFlow);
        float span = shipHull ? Mathf.Lerp(radius, halfLength, broadside) : 1.5f;
        if (shipHull && _vesselFrame is VesselRenderer hullRenderer)
        {
            // Reuse the actual barrel/ogive, so the shock wraps the windward face
            // rather than forming a detached longitudinal disc beside it.
            if (_sheathRenderer != hullRenderer || _sheathFullStack != hasSH)
            {
                _shock.Mesh = hullRenderer.BuildReentrySheathMesh(0.10f);
                _sheathRenderer = hullRenderer;
                _sheathFullStack = hasSH;
                _halo!.Mesh = _shock.Mesh;
            }
            _shock.Position = bodyCentre;
            _shock.Basis = Basis.Identity;
            _shock.Scale = Vector3.One;
            _shockMat.SetShaderParameter("effect_kind", 3);
            _shockMat.SetShaderParameter("flow_dir", flowDir);
        }
        else
        {
            if (_sheathRenderer != null)
            {
                _shock.Mesh = new SphereMesh { Radius = 0.95f, Height = 1.9f, RadialSegments = 24, Rings = 12 };
                _sheathRenderer = null;
            }
            _shock.Position = bodyCentre + flowDir * (radius + 0.16f);
            _shock.Basis = bowBasis;
            _shockMat.SetShaderParameter("effect_kind", 0);
        }
        if (_halo != null && _haloMat != null)
        {
            if (_halo.Visible != shipHull) _halo.Visible = shipHull;
            _halo.Position = bodyCentre;
            _haloMat.SetShaderParameter("flow_dir", flowDir);
        }
        _wake.Basis = new Basis(tangent, -flowDir, -crossFlow);
        float wakeLength = Mathf.Lerp(3f, 12f, (float)intensity);
        _wake.Position = bodyCentre - flowDir * (radius + wakeLength * 0.5f);
        _wake.Scale = new Vector3(span, wakeLength * 0.5f, radius * 1.25f);

        // Simulation time keeps turbulence stable across captures and frozen on pause.
        float plasmaTime = (float)(bridge.Universe.CurrentTime % 4096.0);
        float flicker = 0.94f + 0.06f * Mathf.Sin(plasmaTime * 17f);
        _shockMat.SetShaderParameter("simulation_time", plasmaTime);
        _wakeMat.SetShaderParameter("simulation_time", plasmaTime);
        _haloMat?.SetShaderParameter("simulation_time", plasmaTime);
        float align     = (float)windward;
        float concentr  = Mathf.Lerp(0.55f, 1.0f, align);
        float exposure  = Mathf.Lerp(1.15f, 1.0f, align);
        float hudGuard  = Mathf.Lerp(0.68f, 1.0f, align);
        float dangerMul = Mathf.Lerp(1.30f, 1.0f, align);

        float heatLevel = Mathf.Clamp(
            (float)intensity * concentr * hudGuard * dangerMul * flicker, 0f, 1f);
        LastShockHeatLevel = heatLevel;
        _shockMat.SetShaderParameter("heat_level", heatLevel);
        _haloMat?.SetShaderParameter("heat_level", heatLevel);

        _wakeMat.SetShaderParameter("heat_level", (float)intensity);
        _wakeMat.SetShaderParameter("opacity_gain", VisualWakeTailGain);

        float thickness = Mathf.Lerp(0.20f, 0.48f, (float)intensity);
        if (!shipHull) _shock.Scale = new Vector3(span, thickness, radius * 1.14f);
        foreach (var edge in _edgeGlows)
            edge.Mat.SetShaderParameter("simulation_time", plasmaTime);

        UpdateLocalizedEdgeGlows((float)intensity, align, exposure, hasSH, flicker, flowDir, shipHull);
    }

    private void BuildLocalizedEdgeGlows()
    {
        const float shipSpanScale = (2f + 11.5f + 4.357f) / 21.25f;
        AddEdgeGlow("NoseLeadingHeat", new Vector3(-1.18f, 19.6f * shipSpanScale, 0.0f),
            new Vector3(0.52f, 1.70f, 0.52f), weight: 1.0f, delay: 0.0f, kind: EdgeKind.Nose);

        AddEdgeGlow("BellyCenterHeat", new Vector3(-1.72f, 9.8f * shipSpanScale, 0.0f),
            new Vector3(0.11f, 5.4f, 0.11f), weight: 0.52f, delay: 0.05f, kind: EdgeKind.Belly);

        // Fallback positions; when available, the live renderer flap transforms own the anchors.
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
        var mat = new ShaderMaterial
        {
            Shader = GD.Load<Shader>(ShockShaderPath),
            RenderPriority = 6,
        };
        mat.SetShaderParameter("noise_tex", _shockMat!.GetShaderParameter("noise_tex"));
        mat.SetShaderParameter("effect_kind", 2);
        mat.SetShaderParameter("heat_level", 0f);

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
            AnchorName = name switch
            {
                "FwdFlapLeftHeat" => "FwdFlapL",
                "FwdFlapRightHeat" => "FwdFlapR",
                "AftFlapLeftHeat" => "AftFlapL",
                "AftFlapRightHeat" => "AftFlapR",
                _ => null,
            },
        });
    }

    private void UpdateLocalizedEdgeGlows(float intensity,
        float align, float exposure, bool hasSH, float flicker, Vector3 flowDir, bool shipHull)
    {
        // The localized cues are authored for standalone Starship. During full-stack
        // ascent/reentry, hide them rather than drawing heat on the booster stack.
        if (hasSH)
        {
            foreach (var edge in _edgeGlows) SetEdgeVisible(edge, false);
            return;
        }

        float edgeBase = Mathf.Clamp((intensity - VisualEdgeOnset) / VisualEdgeRange, 0f, 1f);
        float focus = Mathf.Lerp(0.58f, 1.0f, align);
        float misalign = 1f - align;
        float noseBoost  = Mathf.Lerp(0.55f, 1.20f, misalign);
        float bellyScale = Mathf.Lerp(0.38f, 0.95f, align);
        float flapBoost  = Mathf.Lerp(0.75f, 1.25f, misalign);

        foreach (var edge in _edgeGlows)
        {
            // Nose/barrel now belong to the continuous hull sheath. Extra tubes
            // there would recreate the floating rods visible in the old effect.
            if (shipHull && edge.Kind != EdgeKind.Flap)
            {
                SetEdgeVisible(edge, false);
                continue;
            }
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
            if (edge.AnchorName != null && _vesselFrame != null)
            {
                if (edge.Anchor == null || !GodotObject.IsInstanceValid(edge.Anchor))
                    edge.Anchor = _vesselFrame.GetNodeOrNull<Node3D>(edge.AnchorName);
                if (edge.Anchor != null)
                {
                    edge.Mesh.Position = edge.Anchor.Position + Vector3.Left * 0.08f;
                    edge.Mesh.Basis = edge.Anchor.Basis;
                    if (edge.Anchor is MeshInstance3D blade)
                    {
                        edge.Mesh.Mesh = blade.Mesh;
                        edge.Mesh.Position = blade.Position;
                        edge.Mesh.Scale = blade.Scale;
                        edge.Mat.SetShaderParameter("halo_size", 0.06f);
                        edge.Mat.SetShaderParameter("flow_dir", blade.Basis.Inverse() * flowDir);
                    }
                }
            }
            if (edge.Anchor is not MeshInstance3D)
                edge.Mesh.Scale = edge.BaseScale * (0.75f + 0.35f * k);
            edge.Mat.SetShaderParameter("heat_level", Mathf.Clamp(k * exposure, 0f, 1f));
            edge.Mat.SetShaderParameter("opacity_gain", alphaCap);

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
        CoreEffectsVisible = visible;
        if (_shock != null && _shock.Visible != visible) _shock.Visible = visible;
        if (_wake != null && _wake.Visible != visible) _wake.Visible = visible;
        if (!visible && _halo != null && _halo.Visible) _halo.Visible = false;
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
        if (axis.LengthSquared() < 1e-6f)
        {
            // Parallel and antiparallel vectors both have a zero cross product.
            node.Basis = up.Y >= 0f ? Basis.Identity : new Basis(Vector3.Right, Mathf.Pi);
            return;
        }
        float angle = Vector3.Up.AngleTo(up);
        node.Basis = new Basis(axis.Normalized(), angle);
    }
}
