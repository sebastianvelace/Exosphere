namespace Exosphere.Game;

using System.Collections.Generic;
using Godot;

/// <summary>
/// Night presentation of a launch complex, presentation-only. Real pads read at night
/// through their lights, not their geometry:
/// <list type="bullet">
/// <item>work lights strung up every tower, mast, deck and arm (one MultiMesh, one draw call);</item>
/// <item>red aircraft-warning beacons on the tallest structures, blinking in sync;</item>
/// <item>visible light cones under the floodlights in humid coastal air;</item>
/// <item>cryogenic venting around the vehicle while it is loaded on the pad, day or night.</item>
/// </list>
/// Structures are found by name in the parent complex, so Starbase, LC-39 and LC-36 all get
/// lit without per-site placement tables.
/// </summary>
public partial class PadNightLighting : Node3D
{
    /// <summary>Environment glow floor while the pad is lit at night (read by <see cref="PhaseLightingController"/>).</summary>
    public static float NightGlowFloor { get; private set; }

    /// <summary>
    /// Scene luminance floor while the pad is floodlit at night (read by
    /// <see cref="VisualExposureController"/>): the eye adapts to the lit work area, as it does
    /// to the cockpit's practical lights, instead of to the black sky. Without it the
    /// exposure ran to its ceiling and night read as dusk.
    /// </summary>
    public static double PracticalLuminanceFloor { get; private set; }

    private const float U = 1f / 2.8f;
    private const float LightSpacingM = 4.5f;
    private const int MaxWorkLights = 3000;
    private const float BeaconPeriodSeconds = 1.5f;

    private static readonly string[] StructureNames =
    {
        "TowerCol", "ServiceTower", "TowerDeck", "LightningMast", "LightningTower", "CrewAccessArm",
        "Chopstick", "QuickDisconnect", "Mount", "OLM", "TankFarm", "CommodityTank",
    };

    private readonly List<MeshInstance3D> _cones = new();
    private readonly List<StandardMaterial3D> _ventMaterials = new();
    private MultiMeshInstance3D? _workLights;
    private MultiMeshInstance3D? _beacons;
    private StandardMaterial3D? _beaconMaterial;
    private OmniLight3D? _beaconGlow;
    private CpuParticles3D? _baseVent;
    private CpuParticles3D? _sideVent;
    private float _towerSideX = 1f;
    private float _beaconClock;
    private bool? _lastNight;

    public override void _Ready()
    {
        Name = "PadNightLighting";
        var complex = GetParent<Node3D>();
        var (lights, beacons) = CollectLightPositions(complex);
        // Lamp core plus halo, drawn at a constant on-screen size: a world-sized sprite was a
        // blob up close and sub-pixel from the usual 400 m camera.
        _workLights = BuildDots("PadWorkLights", lights, new Color(1.0f, 0.84f, 0.62f), 0.022f, 3f, out _);
        _beacons = BuildDots("PadAviationBeacons", beacons, new Color(1.0f, 0.10f, 0.06f), 0.04f, 4f, out _beaconMaterial);
        if (beacons.Count > 0)
        {
            var tallest = beacons[0];
            foreach (var b in beacons) if (b.Y > tallest.Y) tallest = b;
            _beaconGlow = new OmniLight3D
            {
                Name = "PadBeaconGlow",
                LightColor = new Color(1f, 0.1f, 0.05f),
                LightEnergy = 2f,
                OmniRange = 12f,
                ShadowEnabled = false,
                Position = tallest,
            };
            AddChild(_beaconGlow);
        }
        if (complex is LaunchPadController pad)
            foreach (var flood in pad.NightFloodlights) AttachCone(flood);
        BuildVents();
        ApplyNight(false);
        GD.Print($"[PadNightLighting] work lights={lights.Count} beacons={beacons.Count} "
            + $"cones={_cones.Count} towerSide={_towerSideX:+0;-0}");
    }

    public override void _ExitTree()
    {
        NightGlowFloor = 0f;
        PracticalLuminanceFloor = 0.0;
    }

    public override void _Process(double delta)
    {
        bool night = SunController.SolarPhase is "CIVIL_TWILIGHT" or "NAUTICAL_TWILIGHT"
            or "ASTRONOMICAL_TWILIGHT" or "NIGHT";
        bool onPad = SimulationBridge.Instance?.ActiveVessel is { } vessel
            && vessel.GetAltitude(SimulationBridge.Instance.Universe.GetDominantBody(vessel.Position)) < 3_000.0;
        bool lit = night && onPad;
        if (_lastNight != lit) ApplyNight(lit);

        if (lit && _beaconMaterial != null)
        {
            // Aviation obstruction beacons: synchronized slow flash.
            _beaconClock = (_beaconClock + (float)delta) % BeaconPeriodSeconds;
            bool on = _beaconClock < 0.45f * BeaconPeriodSeconds;
            _beaconMaterial.EmissionEnergyMultiplier = on ? 4f : 0.15f;
            if (_beaconGlow != null) _beaconGlow.LightEnergy = on ? 2f : 0f;
        }
        UpdateVents();
    }

    private void ApplyNight(bool lit)
    {
        _lastNight = lit;
        if (_workLights != null) _workLights.Visible = lit;
        if (_beacons != null) _beacons.Visible = lit;
        if (_beaconGlow != null) _beaconGlow.Visible = lit;
        foreach (var cone in _cones) cone.Visible = lit;
        bool day = SunController.SolarPhase == "DAY";
        foreach (var vent in _ventMaterials)
            vent.AlbedoColor = day ? new Color(0.95f, 0.96f, 1f) : new Color(0.62f, 0.60f, 0.58f);
        NightGlowFloor = lit ? 0.45f : 0f;
        PracticalLuminanceFloor = lit ? 0.1 : 0.0;
    }

    /// <summary>Work lights on the vertical edges of every tall structure; beacons on their tops.</summary>
    private (List<Vector3> lights, List<Vector3> beacons) CollectLightPositions(Node3D complex)
    {
        var lights = new List<Vector3>();
        var beacons = new List<Vector3>();
        var towerXs = new List<float>();
        var inverse = complex.GlobalTransform.AffineInverse();
        foreach (var mesh in Descendants(complex))
        {
            if (mesh.Mesh == null || !IsStructure(mesh.Name)) continue;
            var box = (inverse * mesh.GlobalTransform) * mesh.Mesh.GetAabb();
            float heightM = box.Size.Y / U;
            var top = box.Position.Y + box.Size.Y;
            if (heightM > 12f)
            {
                towerXs.Add(box.GetCenter().X);
                // Four vertical edges for wide structures, one line for thin masts and columns,
                // always stood off the steel: a lamp inside a column is hidden by it.
                var middle = new Vector2(box.GetCenter().X, box.GetCenter().Z);
                var corners = box.Size.X * box.Size.Z > 4f * U * U
                    ? new[] { new Vector2(box.Position.X, box.Position.Z), new Vector2(box.End.X, box.Position.Z),
                        new Vector2(box.Position.X, box.End.Z), new Vector2(box.End.X, box.End.Z) }
                    : new[] { middle + middle.Normalized() * (0.5f * Mathf.Max(box.Size.X, box.Size.Z) + 0.4f * U) };
                for (int i = 0; i < corners.Length && corners.Length == 4; i++)
                    corners[i] += (corners[i] - middle).Normalized() * 0.4f * U;
                foreach (var corner in corners)
                    for (float y = box.Position.Y + LightSpacingM * U; y < top; y += LightSpacingM * U)
                        lights.Add(new Vector3(corner.X, y, corner.Y));
                if (heightM > 30f)
                    beacons.Add(new Vector3(box.GetCenter().X, top + 0.6f * U, box.GetCenter().Z));
            }
            else
            {
                // Decks, arms and mounts: lights around the top perimeter.
                float perimeterM = 2f * (box.Size.X + box.Size.Z) / U;
                int count = Mathf.Clamp((int)(perimeterM / LightSpacingM), 2, 40);
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)count * 4f;
                    int side = (int)t;
                    float f = t - side;
                    var p = side switch
                    {
                        0 => new Vector3(Mathf.Lerp(box.Position.X, box.End.X, f), top, box.Position.Z),
                        1 => new Vector3(box.End.X, top, Mathf.Lerp(box.Position.Z, box.End.Z, f)),
                        2 => new Vector3(Mathf.Lerp(box.End.X, box.Position.X, f), top, box.End.Z),
                        _ => new Vector3(box.Position.X, top, Mathf.Lerp(box.End.Z, box.Position.Z, f)),
                    };
                    lights.Add(p);
                }
            }
            if (lights.Count >= MaxWorkLights) break;
        }
        if (towerXs.Count > 0)
        {
            float sum = 0f;
            foreach (float x in towerXs) sum += x;
            _towerSideX = Mathf.Sign(sum / towerXs.Count);
            if (_towerSideX == 0f) _towerSideX = 1f;
        }
        return (lights, beacons);
    }

    private static bool IsStructure(string name)
    {
        foreach (var prefix in StructureNames)
            if (name.StartsWith(prefix, System.StringComparison.Ordinal)) return true;
        return false;
    }

    private static IEnumerable<MeshInstance3D> Descendants(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is PadNightLighting) continue;
            if (child is MeshInstance3D mesh) yield return mesh;
            foreach (var inner in Descendants(child)) yield return inner;
        }
    }

    /// <param name="screenSize">Sprite size in fixed-size units (about a fraction of the view height).</param>
    private MultiMeshInstance3D? BuildDots(string name, List<Vector3> positions, Color color, float screenSize,
        float energy, out StandardMaterial3D? material)
    {
        material = null;
        if (positions.Count == 0) return null;
        var gradient = new Gradient();
        gradient.SetColor(0, Colors.White);
        gradient.AddPoint(0.08f, new Color(1f, 1f, 1f, 0.95f));
        gradient.AddPoint(0.3f, new Color(1f, 1f, 1f, 0.18f));
        gradient.SetColor(gradient.GetPointCount() - 1, new Color(1f, 1f, 1f, 0f));
        var lamp = new GradientTexture2D
        {
            Gradient = gradient,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
            Width = 64,
            Height = 64,
        };
        material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            AlbedoColor = color,
            AlbedoTexture = lamp,
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = energy,
            EmissionTexture = lamp,
            FixedSize = true,
        };
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new QuadMesh { Size = new Vector2(screenSize, screenSize), Material = material },
            InstanceCount = positions.Count,
        };
        for (int i = 0; i < positions.Count; i++)
            multimesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, positions[i]));
        var instance = new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = multimesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(instance);
        return instance;
    }

    /// <summary>Additive light cone along the floodlight's beam, fading away from the lamp.</summary>
    private void AttachCone(SpotLight3D flood)
    {
        const float lengthM = 70f;
        float length = lengthM * U;
        float radius = Mathf.Tan(Mathf.DegToRad(flood.SpotAngle)) * length;
        var shader = new Shader
        {
            Code = @"shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, shadows_disabled;
uniform vec3 tint = vec3(1.0, 0.86, 0.68);
uniform float half_length = 1.0;
uniform float strength = 0.12;
varying float along;
void vertex() { along = clamp(VERTEX.y / (2.0 * half_length) + 0.5, 0.0, 1.0); }
void fragment() {
    float facing = abs(dot(normalize(NORMAL), normalize(VIEW)));
    ALBEDO = tint * strength * pow(along, 1.6) * facing * facing;
}",
        };
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("half_length", length * 0.5f);
        var cone = new MeshInstance3D
        {
            Name = "FloodlightCone",
            Mesh = new CylinderMesh { TopRadius = 0.2f * U, BottomRadius = radius, Height = length, Rings = 1, RadialSegments = 24 },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // Small end on the lamp, wide end down the beam (-Z): +Y maps to +Z, then shift.
            Transform = new Transform3D(new Basis(Vector3.Right, Mathf.Pi * 0.5f), new Vector3(0f, 0f, -length * 0.5f)),
        };
        flood.AddChild(cone);
        _cones.Add(cone);
    }

    /// <summary>Cold vapour boiling off the loaded vehicle: around its base and at the tower side.</summary>
    private void BuildVents()
    {
        _baseVent = BuildVent("VentBase", new Vector3(4f, 0.6f, 4f), new Vector3(0f, -0.15f, 0f), 200);
        // Around the hull, not inside it: a box at the axis hid the vapour in the booster.
        _baseVent.EmissionShape = CpuParticles3D.EmissionShapeEnum.Ring;
        _baseVent.EmissionRingAxis = Vector3.Up;
        _baseVent.EmissionRingHeight = 1f * U;
        _sideVent = BuildVent("VentQuickDisconnect", new Vector3(0.8f, 3f, 0.8f), new Vector3(0f, -0.4f, 0f), 120);
    }

    private CpuParticles3D BuildVent(string name, Vector3 extentsM, Vector3 gravityM, int amount)
    {
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(1f, 1f, 1f, 0.55f));
        gradient.SetColor(1, new Color(1f, 1f, 1f, 0f));
        var softDot = new GradientTexture2D
        {
            Gradient = gradient,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
            Width = 64,
            Height = 64,
        };
        var fade = new Gradient();
        fade.SetColor(0, new Color(1f, 1f, 1f, 0f));
        fade.AddPoint(0.15f, new Color(1f, 1f, 1f, 0.5f));
        fade.SetColor(fade.GetPointCount() - 1, new Color(1f, 1f, 1f, 0f));
        var growth = new Curve();
        growth.AddPoint(new Vector2(0f, 0.4f));
        growth.AddPoint(new Vector2(1f, 1f));
        // Unshaded: at night a lit sprite stays black outside the floodlight pools; the vapour
        // reads as the pad lights scattering in it, dimmer at night than in daylight.
        var material = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = softDot,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _ventMaterials.Add(material);
        // CPU particles: GPU particles did not draw on the Compatibility renderer here.
        var particles = new CpuParticles3D
        {
            Name = name,
            Amount = amount,
            Lifetime = 7.0,
            Preprocess = 7.0,
            LocalCoords = true,
            Mesh = new QuadMesh { Size = new Vector2(1.4f * U, 1.4f * U), Material = material },
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = extentsM * U,
            Direction = new Vector3(0f, 1f, 0f),
            Spread = 70f,
            InitialVelocityMin = 0.4f * U,
            InitialVelocityMax = 1.6f * U,
            Gravity = gravityM * U,
            DampingMin = 0.3f,
            DampingMax = 0.8f,
            ScaleAmountMin = 4f,
            ScaleAmountMax = 8f,
            ScaleAmountCurve = growth,
            ColorRamp = fade,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Emitting = false,
        };
        AddChild(particles);
        return particles;
    }

    private void UpdateVents()
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        bool loaded = vessel is { IsGroundHeld: true, IsDestroyed: false };
        if (_baseVent == null || _sideVent == null) return;
        _baseVent.Emitting = loaded;
        _sideVent.Emitting = loaded;
        if (!loaded || vessel == null) return;
        // The complex sits at -interface elevation, so the vehicle datum is at local y = interface.
        float baseY = -Position.Y - GetParent<Node3D>().Position.Y;
        float length = (float)vessel.VehicleLength * U;
        float radius = Mathf.Max(1f, (float)vessel.Parts.MaximumDiameter * 0.5f) * U;
        _baseVent.Position = new Vector3(0f, baseY + 1.5f * U, 0f);
        _baseVent.EmissionRingInnerRadius = radius + 0.5f * U;
        _baseVent.EmissionRingRadius = radius + 4f * U;
        // Upper-stage fill line on the tower side, two thirds up the stack.
        _sideVent.Position = new Vector3(_towerSideX * (radius + 0.8f * U), baseY + 0.62f * length, 0f);
    }
}
