namespace Exosphere.Game;

using Godot;
using System.Collections.Generic;
using System.Linq;
using Exosphere.Simulation.Physics;

/// <summary>
/// Visual break-up sequence when the active vessel is destroyed by re-entry heating
/// or catastrophic structural overload. On the rising edge of
/// <see cref="Exosphere.Simulation.Vessel.IsDestroyed"/> with a thermal or structural
/// cause, this spawns a shower of glowing, tumbling debris fragments that fan out
/// along the airflow and fade — instead of the model simply freezing in place.
///
/// The simulation owns the destruction decision (Universe sets IsDestroyed); this
/// controller only READS the vessel state on the rising edge and plays the effect.
/// Ground impact (soft-rest / hard crash) does NOT trigger a fireball. Partial
/// structural splits that leave the active vessel alive are rendered via staging
/// debris spawn in <c>SimulationBridge</c>, not here.
///
/// Secuencia visual de breakup: lee IsDestroyed + causa térmica/estructural y dispara
/// fragmentos. Solo LEE el sim; la destrucción la decide Universe.
/// </summary>
[GlobalClass]
public partial class ReentryBreakupController : Node3D
{
    // One flying ember/fragment of the disintegrating ship.
    private sealed class Fragment
    {
        public MeshInstance3D     Node     = null!;
        public StandardMaterial3D Mat      = null!;
        public Vector3            Velocity;   // render units / s
        public Vector3            Spin;       // rad / s
        public float              Age;
        public float              Life;       // seconds before fully faded
        public float              Heat;       // 0..1 initial incandescence
    }

    private readonly List<Fragment> _fragments = new();

    // Tracks the active vessel so we only fire once, on the destruction edge.
    private string? _watchedVesselId;
    private bool    _wasDestroyed;
    private bool    _played;            // breakup already played for the watched vessel

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;

        // Reset edge tracking when the active vessel changes (new launch / new craft).
        if (vessel != null && vessel.Id != _watchedVesselId)
        {
            _watchedVesselId = vessel.Id;
            _wasDestroyed    = vessel.IsDestroyed;
            _played          = false;
        }

        if (vessel != null && !_played)
        {
            bool destroyedNow = vessel.IsDestroyed;
            if (destroyedNow && !_wasDestroyed && ShouldPlayBreakup(vessel))
            {
                SpawnBreakup(vessel);
                _played = true;
            }
            _wasDestroyed = destroyedNow;
        }

        UpdateFragments(dt);
    }

    // ── Cause discrimination ──────────────────────────────────────────────
    // Thermal incineration or structural total-loss plays the fireball. A plain
    // ground impact (soft-rest / hard crash) does NOT qualify.
    private static bool ShouldPlayBreakup(Exosphere.Simulation.Vessel vessel)
    {
        if (vessel.DestructionCause == Exosphere.Simulation.VesselDestructionCause.StructuralBreakup)
            return true;
        if (vessel.DestructionCause == Exosphere.Simulation.VesselDestructionCause.ThermalBreakup)
            return true;
        foreach (var part in vessel.Parts.Parts)
            if (part.IsThermallyBurned) return true;
        return StressSolver.WorstHeatRatio(vessel.Parts) >= 1.0;
    }

    // ── Spawn the debris shower ───────────────────────────────────────────
    private void SpawnBreakup(Exosphere.Simulation.Vessel vessel)
    {
        // Airflow direction in render space, so fragments stream the way the plasma did.
        var    body    = vessel != null ? SimulationBridge.Instance!.Universe.GetDominantBody(vessel.Position) : null;
        var    surfVel = body != null ? vessel!.GetSurfaceVelocity(body) : Exosphere.Simulation.Math.Vector3d.Zero;
        Vector3 flow   = new((float)surfVel.X, (float)surfVel.Y, (float)surfVel.Z);
        flow = flow.LengthSquared() > 1e-6f ? flow.Normalized() : Vector3.Up;

        bool hasSH = vessel!.Parts.Parts.Any(p => p.Definition.Id == "super_heavy_booster");
        Vector3 centre = new(0f, hasSH ? 26f : 8f, 0f);
        float   spread = hasSH ? 10f : 5f;

        const int count = 28;
        for (int i = 0; i < count; i++)
        {
            float scale = (float)GD.RandRange(0.5, 2.2);
            var mat = new StandardMaterial3D
            {
                AlbedoColor              = new Color(0.16f, 0.13f, 0.12f),
                Metallic                 = 0.6f,
                Roughness                = 0.7f,
                EmissionEnabled          = true,
                Emission                 = new Color(1.0f, 0.55f, 0.18f),
                EmissionEnergyMultiplier = 3.0f,
            };
            var node = new MeshInstance3D
            {
                Name = $"Debris{i}",
                Mesh = new BoxMesh { Size = new Vector3(0.4f * scale, 0.6f * scale, 0.3f * scale) },
                Position = centre + new Vector3(
                    (float)GD.RandRange(-1.0, 1.0),
                    (float)GD.RandRange(-spread, spread),
                    (float)GD.RandRange(-1.0, 1.0)),
            };
            node.SetSurfaceOverrideMaterial(0, mat);
            AddChild(node);

            // Fan out: mostly downstream along the flow, plus a sideways scatter.
            Vector3 lateral = new(
                (float)GD.RandRange(-1.0, 1.0), 0f, (float)GD.RandRange(-1.0, 1.0));
            Vector3 vel = flow * (float)GD.RandRange(4.0, 12.0)
                        + lateral.Normalized() * (float)GD.RandRange(2.0, 7.0);

            _fragments.Add(new Fragment
            {
                Node     = node,
                Mat      = mat,
                Velocity = vel,
                Spin     = new Vector3(
                    (float)GD.RandRange(-6.0, 6.0),
                    (float)GD.RandRange(-6.0, 6.0),
                    (float)GD.RandRange(-6.0, 6.0)),
                Age  = 0f,
                Life = (float)GD.RandRange(1.8, 4.0),
                Heat = (float)GD.RandRange(0.6, 1.0),
            });
        }
    }

    // ── Animate and retire fragments ──────────────────────────────────────
    private void UpdateFragments(float dt)
    {
        if (_fragments.Count == 0) return;

        for (int i = _fragments.Count - 1; i >= 0; i--)
        {
            var f = _fragments[i];
            f.Age += dt;
            if (f.Age >= f.Life || !IsInstanceValid(f.Node))
            {
                if (IsInstanceValid(f.Node)) f.Node.QueueFree();
                _fragments.RemoveAt(i);
                continue;
            }

            float k = f.Age / f.Life;     // 0 → 1

            // Drift downstream; gentle drag so the shower slows as it cools.
            f.Velocity *= Mathf.Pow(0.55f, dt);
            f.Node.Position += f.Velocity * dt;
            f.Node.RotateX(f.Spin.X * dt);
            f.Node.RotateY(f.Spin.Y * dt);
            f.Node.RotateZ(f.Spin.Z * dt);

            // Cool from white-hot → orange → dull as it ages, then fade out at the end.
            float glow = f.Heat * (1f - k);
            var hot = new Color(1.0f, 0.35f + 0.5f * glow, 0.10f + 0.4f * glow);
            f.Mat.Emission                 = hot;
            f.Mat.EmissionEnergyMultiplier = 0.4f + 3.0f * glow;

            // Fade the last third of life.
            float alpha = k > 0.66f ? 1f - (k - 0.66f) / 0.34f : 1f;
            if (alpha < 1f)
            {
                f.Mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                var a = f.Mat.AlbedoColor; a.A = alpha; f.Mat.AlbedoColor = a;
            }
        }
    }
}
