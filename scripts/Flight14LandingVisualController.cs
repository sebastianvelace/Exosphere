namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation.Math;

/// <summary>Bounded near-sea presentation, driven by the opt-in physical water entry state.</summary>
public partial class Flight14LandingVisualController : Node3D
{
    private MeshInstance3D _sea = null!;
    private ShaderMaterial _material = null!;
    private CpuParticles3D _mist = null!;
    private double _lastTime = double.NaN;

    public override void _Ready()
    {
        // Write sea depth before translucent exhaust/spray. Otherwise distance sorting
        // can paint this large transparent surface over particles above the water.
        _material = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://assets/shaders/landing_ocean.gdshader"), RenderPriority = -100,
        };
        _sea = new MeshInstance3D
        {
            Mesh = BuildSeaMesh(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            CustomAabb = new Aabb(new Vector3(-9000, -25, -9000), new Vector3(18000, 30, 18000)),
        };
        _sea.SetSurfaceOverrideMaterial(0, _material); AddChild(_sea);
        var radial = new GradientTexture2D
        {
            Width = 64, Height = 64, Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1, 0.5f),
            Gradient = new Gradient { Colors = new[] { Colors.White, new Color(1, 1, 1, 0) }, Offsets = new[] { 0f, 1f } },
        };
        var sprayMaterial = new StandardMaterial3D
        {
            AlbedoTexture = radial,
            AlbedoColor = new Color(0.83f, 0.86f, 0.88f, 0.28f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            NoDepthTest = false,
        };
        var spray = new QuadMesh { Size = new Vector2(3, 3), Material = sprayMaterial };
        _mist = new CpuParticles3D
        {
            Amount = 160, Lifetime = 1.8, Emitting = false, Mesh = spray, LocalCoords = true,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Ring,
            EmissionRingRadius = 7, EmissionRingInnerRadius = 2,
            EmissionRingHeight = 0.4f, EmissionRingAxis = Vector3.Up,
            Direction = Vector3.Up, Spread = 80,
            InitialVelocityMin = 2, InitialVelocityMax = 5,
            Gravity = Vector3.Down*0.7f, ScaleAmountMin = 0.4f, ScaleAmountMax = 1.5f,
            Randomness = 0.65f, LifetimeRandomness = 0.25f,
            ColorRamp = new Gradient
            {
                Colors = new[] { new Color(1, 1, 1, 0.1f), Colors.White, new Color(1, 1, 1, 0) },
                Offsets = new[] { 0f, 0.15f, 1f },
            },
        };
        AddChild(_mist); Visible = false;
    }

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        var ship = bridge?.ActiveVessel; var earth = bridge?.Universe.GetBody("earth");
        if (bridge?.Flight14Preview == null || ship == null || earth == null
            || ship.WaterContact is not { } water || ship.GetAltitude(earth) > 1500
            || !water.Covers(earth, ship.Position, bridge.Universe.CurrentTime))
        { Visible = false; _mist.Emitting = false; return; }
        Visible = true;
        var up = earth.GetGeodeticUp(ship.Position);
        var east = earth.RotationAxis.Cross(up).Normalized;
        // +Z is south: east/up/south form a right-handed basis.
        var south = east.Cross(up).Normalized;
        var offset = (earth.GetSurfacePoint(ship.Position, 0)-ship.Position)/2.8;
        GlobalTransform = new Transform3D(new Basis(ToGodot(east), ToGodot(up), ToGodot(south)), ToGodot(offset));
        double time = bridge.Universe.CurrentTime;
        double advance = double.IsFinite(_lastTime) ? System.Math.Max(0, time-_lastTime) : 0;
        _lastTime = time;
        // Procedural waves are optical only. Calm mean sea level is the physical contact surface.
        _material.SetShaderParameter("sim_time", (float)(time%8192));
        _material.SetShaderParameter("sea_radius_m", (float)FloatingOrigin.VisualSurfaceRadiusMetres(earth, ship.Position));
        _material.SetShaderParameter("physical_camera_up", FloatingOrigin.CameraEarthRadialUp);
        _material.SetShaderParameter("physical_camera_altitude_m", (float)FloatingOrigin.CameraEarthRadialAltitudeM);
        _material.SetShaderParameter("physical_planet_radius_m", (float)FloatingOrigin.CameraEarthRadiusM);
        _material.SetShaderParameter("backdrop_far_units", GetViewport().GetCamera3D()?.Far ?? 420_000f);
        double thrust = ship.Parts.GetCurrentThrust(ship.GetAmbientPressure(earth));
        double alignment = ship.Orientation.Rotate(Vector3d.Up).Dot(up);
        double clearance = ship.GetAltitude(earth)+water.LowestPointYM*alignment;
        float proximity = (float)System.Math.Clamp(1-clearance/65, 0, 1);
        float delivered = (float)System.Math.Clamp(thrust/3_000_000, 0, 1)*proximity;
        _material.SetShaderParameter("exhaust_gain", delivered);
        var contact = ship.LastWaterContact;
        bool wet = contact is { } load && (load.LowestPointAltitudeM < 0 || load.HullLowestAltitudeM < 0);
        // Keep the fan over the projected wet hull rather than over the floating-origin
        // datum when the vehicle heels. Before displacement starts, use the nozzle witness.
        var contactOffset = contact is { SubmergedVolumeM3: > 0 } displaced
            ? displaced.BuoyancyCenterOffsetWorld : ship.Orientation.Rotate(Vector3d.Up)*water.LowestPointYM;
        var contactCenter = new Vector2((float)contactOffset.Dot(east), (float)contactOffset.Dot(south));
        var start = contact is { SubmergedVolumeM3: > 0 } wetHull ? wetHull.WetHullStartOffsetWorld : contactOffset;
        var end = contact is { SubmergedVolumeM3: > 0 } wetEnd ? wetEnd.WetHullEndOffsetWorld : contactOffset;
        _material.SetShaderParameter("wet_hull_start_m", new Vector2((float)start.Dot(east), (float)start.Dot(south)));
        _material.SetShaderParameter("wet_hull_end_m", new Vector2((float)end.Dot(east), (float)end.Dot(south)));
        _material.SetShaderParameter("hull_radius_m", (float)water.RadiusM);
        _material.SetShaderParameter("contact_gain", wet ? 1f : 0f);
        _mist.Position = new Vector3(contactCenter.X/2.8f, 0, contactCenter.Y/2.8f);
        _mist.Emitting = delivered > 0.03f || wet && contact is { WetMotionSpeedMps: > 0.6 };
        _mist.SpeedScale = delta > 0 ? (float)System.Math.Clamp(advance/delta, 0, 8) : 0;
    }

    private static Vector3 ToGodot(Vector3d v) => new((float)v.X, (float)v.Y, (float)v.Z);

    private static ArrayMesh BuildSeaMesh()
    {
        // Concentrate triangles near the exhaust footprint, then cover the low-camera
        // horizon without a square boundary. Wave detail lives in the optical normal.
        const int rings = 64, segments = 192;
        const float radiusUnits = 25000f/2.8f;
        var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        for (int ring = 0; ring < rings; ring++)
        {
            float inner = radiusUnits*Mathf.Pow(ring/(float)rings, 3);
            float outer = radiusUnits*Mathf.Pow((ring+1)/(float)rings, 3);
            for (int segment = 0; segment < segments; segment++)
            {
                float a = segment*Mathf.Tau/segments, b = (segment+1)*Mathf.Tau/segments;
                var i0 = new Vector3(inner*Mathf.Cos(a), 0, inner*Mathf.Sin(a));
                var o0 = new Vector3(outer*Mathf.Cos(a), 0, outer*Mathf.Sin(a));
                var o1 = new Vector3(outer*Mathf.Cos(b), 0, outer*Mathf.Sin(b));
                var i1 = new Vector3(inner*Mathf.Cos(b), 0, inner*Mathf.Sin(b));
                Triangle(i0, o0, o1);
                if (ring > 0) Triangle(i0, o1, i1);
            }
        }
        return surface.Commit();

        void Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            surface.SetTangent(new Plane(Vector3.Right, 1));
            surface.SetNormal(Vector3.Up); surface.AddVertex(a);
            surface.SetNormal(Vector3.Up); surface.AddVertex(b);
            surface.SetNormal(Vector3.Up); surface.AddVertex(c);
        }
    }
}
