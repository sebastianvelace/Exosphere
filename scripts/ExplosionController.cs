namespace Exosphere.Game;

using Godot;
using System.Collections.Generic;

/// <summary>
/// Observes IsDestroyed on the active vessel and spawns a procedural explosion
/// effect at the impact point when a hard crash is detected.
/// Auto-added as a child of the World node by MissionManager._Ready().
/// </summary>
public partial class ExplosionController : Node3D
{
    private bool _exploded = false;
    private GpuParticles3D? _fireParticles;
    private GpuParticles3D? _smokeParticles;
    private OmniLight3D? _flashLight;
    private readonly List<DebrisPiece> _debris = new();
    private readonly RandomNumberGenerator _rng = new();

    private sealed class DebrisPiece
    {
        public MeshInstance3D Node = null!;
        public Vector3 Velocity;
        public Vector3 AngularVelocity;
        public float Remaining;
    }

    public override void _Ready()
    {
        _rng.Randomize();

        // Create fire particles
        _fireParticles = CreateFireParticles();
        AddChild(_fireParticles);

        // Create smoke particles
        _smokeParticles = CreateSmokeParticles();
        AddChild(_smokeParticles);

        // Flash light — starts at zero energy
        _flashLight = new OmniLight3D();
        _flashLight.LightColor = new Color(1.0f, 0.6f, 0.2f);
        _flashLight.LightEnergy = 0.0f;
        _flashLight.OmniRange = 80.0f;
        AddChild(_flashLight);

        // Everything off until crash
        _fireParticles.Emitting = false;
        _smokeParticles.Emitting = false;
        Visible = false;
    }

    public override void _Process(double delta)
    {
        if (_exploded)
        {
            // Fade the flash light out over time
            if (_flashLight != null && _flashLight.LightEnergy > 0.01f)
                _flashLight.LightEnergy -= (float)(delta * 8.0);
            TickDebris((float)delta);
            return;
        }

        var vessel = SimulationBridge.Instance?.ActiveVessel;
        if (vessel == null || !vessel.IsDestroyed) return;

        TriggerExplosion(vessel);
    }

    private void TriggerExplosion(Exosphere.Simulation.Vessel vessel)
    {
        _exploded = true;

        // The floating-origin system keeps the active vessel at render-space origin,
        // so the explosion spawns at Vector3.Zero — exactly where the vessel mesh is.
        GlobalPosition = Vector3.Zero;
        Visible = true;

        // Start particles
        if (_fireParticles != null) _fireParticles.Emitting = true;
        if (_smokeParticles != null) _smokeParticles.Emitting = true;

        // Bright initial flash
        if (_flashLight != null) _flashLight.LightEnergy = 12.0f;

        SpawnDebris();

        // Hide the vessel mesh renderer
        var vesselRenderer = GetTree()?.Root.FindChild("StarshipRenderer", true, false);
        if (vesselRenderer is Node3D vr3d) vr3d.Visible = false;

        GD.Print($"[CRASH] Vessel destroyed at {vessel.CrashImpactSpeed:F0} m/s");

        AudioManager.Instance?.PlayCrash();
    }

    private void SpawnDebris()
    {
        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.55f, 0.58f, 0.60f),
            Metallic = 0.75f,
            Roughness = 0.42f,
        };

        for (int i = 0; i < 22; i++)
        {
            var mesh = new BoxMesh
            {
                Size = new Vector3(
                    _rng.RandfRange(0.4f, 2.4f),
                    _rng.RandfRange(0.2f, 1.2f),
                    _rng.RandfRange(0.4f, 2.0f))
            };
            var node = new MeshInstance3D
            {
                Mesh = mesh,
                Position = Vector3.Zero,
                Rotation = new Vector3(
                    _rng.RandfRange(0f, Mathf.Tau),
                    _rng.RandfRange(0f, Mathf.Tau),
                    _rng.RandfRange(0f, Mathf.Tau)),
            };
            node.SetSurfaceOverrideMaterial(0, mat);
            AddChild(node);

            var dir = new Vector3(
                _rng.RandfRange(-1f, 1f),
                _rng.RandfRange(0.2f, 1.1f),
                _rng.RandfRange(-1f, 1f)).Normalized();

            _debris.Add(new DebrisPiece
            {
                Node = node,
                Velocity = dir * _rng.RandfRange(18f, 85f),
                AngularVelocity = new Vector3(
                    _rng.RandfRange(-7f, 7f),
                    _rng.RandfRange(-7f, 7f),
                    _rng.RandfRange(-7f, 7f)),
                Remaining = _rng.RandfRange(4f, 8f),
            });
        }
    }

    private void TickDebris(float delta)
    {
        for (int i = _debris.Count - 1; i >= 0; i--)
        {
            var d = _debris[i];
            d.Remaining -= delta;
            if (d.Remaining <= 0f)
            {
                d.Node.QueueFree();
                _debris.RemoveAt(i);
                continue;
            }

            d.Velocity += new Vector3(0, -9.8f, 0) * delta;
            d.Node.Position += d.Velocity * delta;
            d.Node.RotateObjectLocal(Vector3.Right, d.AngularVelocity.X * delta);
            d.Node.RotateObjectLocal(Vector3.Up, d.AngularVelocity.Y * delta);
            d.Node.RotateObjectLocal(Vector3.Forward, d.AngularVelocity.Z * delta);

            float alpha = Mathf.Clamp(d.Remaining / 2f, 0f, 1f);
            d.Node.Transparency = 1f - alpha;
        }
    }

    private static GpuParticles3D CreateFireParticles()
    {
        var particles = new GpuParticles3D();
        particles.Amount = 300;
        particles.Lifetime = 2.5f;
        particles.Explosiveness = 0.8f;
        particles.OneShot = true;

        var mat = new ParticleProcessMaterial();
        mat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        mat.EmissionSphereRadius = 8.0f;
        mat.Direction = new Vector3(0, 1, 0);
        mat.Spread = 180.0f;
        mat.InitialVelocityMin = 10.0f;
        mat.InitialVelocityMax = 60.0f;
        mat.Gravity = new Vector3(0, -9.8f, 0);
        mat.ScaleMin = 3.0f;
        mat.ScaleMax = 12.0f;
        mat.Color = new Color(1.0f, 0.4f, 0.05f, 1.0f); // intense orange-red

        particles.ProcessMaterial = mat;
        return particles;
    }

    private static GpuParticles3D CreateSmokeParticles()
    {
        var particles = new GpuParticles3D();
        particles.Amount = 200;
        particles.Lifetime = 6.0f;
        particles.Explosiveness = 0.3f;
        particles.OneShot = true;

        var mat = new ParticleProcessMaterial();
        mat.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere;
        mat.EmissionSphereRadius = 5.0f;
        mat.Direction = new Vector3(0, 1, 0);
        mat.Spread = 60.0f;
        mat.InitialVelocityMin = 2.0f;
        mat.InitialVelocityMax = 15.0f;
        mat.Gravity = new Vector3(0, -0.5f, 0);
        mat.ScaleMin = 8.0f;
        mat.ScaleMax = 25.0f;
        mat.Color = new Color(0.2f, 0.2f, 0.2f, 0.7f); // dark smoke

        particles.ProcessMaterial = mat;
        return particles;
    }
}
