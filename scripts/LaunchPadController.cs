namespace Exosphere.Game;

using Godot;

/// <summary>
/// Starbase-inspired Orbital Launch Mount + Mechazilla tower visual.
/// This Node3D is positioned each frame by SimulationBridge so it stays
/// anchored to the Earth surface directly below the vessel.
/// </summary>
public partial class LaunchPadController : Node3D
{
    public static LaunchPadController? Instance { get; private set; }

    public override void _Ready()
    {
        Instance = this;
        BuildEnvironment();
    }

    private void BuildEnvironment()
    {
        var concrete = Mat(new Color(0.22f, 0.20f, 0.16f), 0.96f, 0.0f);
        var steel    = Mat(new Color(0.60f, 0.60f, 0.63f), 0.55f, 0.88f);
        var darkSteel= Mat(new Color(0.35f, 0.35f, 0.38f), 0.65f, 0.80f);
        var burnt    = Mat(new Color(0.10f, 0.09f, 0.08f), 0.98f, 0.0f);

        // ── Ground apron ───────────────────────────────────────────────────
        Spawn("Ground",       new BoxMesh { Size = new Vector3(600f, 0.4f, 600f) },       concrete,  new Vector3(0,     -0.2f,  0));
        Spawn("FlameTrench",  new BoxMesh { Size = new Vector3(24f,  6f,  65f) },          burnt,     new Vector3(0,     -3f,   25f));

        // ── Orbital Launch Mount base ──────────────────────────────────────
        Spawn("OLMBase",      new BoxMesh { Size = new Vector3(16f, 10f, 16f) },           steel,     new Vector3(0,      5f,    0));
        Spawn("OLMPedestal",  new CylinderMesh { TopRadius=4.2f, BottomRadius=4.8f, Height=5f, RadialSegments=32 },
                                                                                           steel,     new Vector3(0,     12.5f,  0));
        Spawn("DelugeRing",   new CylinderMesh { TopRadius=8.5f, BottomRadius=9f,   Height=0.7f, RadialSegments=32 },
                                                                                           darkSteel, new Vector3(0,     10.35f, 0));

        // ── Mechazilla tower ───────────────────────────────────────────────
        Spawn("Tower",        new BoxMesh { Size = new Vector3(11f, 160f, 11f) },          steel,     new Vector3(-20f,  80f,   0));
        Spawn("TowerTop",     new BoxMesh { Size = new Vector3(13f,  4f,  13f) },          darkSteel, new Vector3(-20f, 162f,   0));

        // Catch / launch arms (Mechazilla chopsticks)
        Spawn("ArmUpper",     new BoxMesh { Size = new Vector3(28f, 3.5f, 4f) },           darkSteel, new Vector3(-6f,  115f,  0));
        Spawn("ArmLower",     new BoxMesh { Size = new Vector3(26f, 3.5f, 4f) },           darkSteel, new Vector3(-7f,   88f,  0));

        // Quick-disconnect arm (supplies propellant on the pad)
        Spawn("QDArm",        new BoxMesh { Size = new Vector3(4f,  2f,  18f) },           darkSteel, new Vector3(-14f,  25f, -9f));
        Spawn("QDArmTop",     new BoxMesh { Size = new Vector3(4f,  2f,  18f) },           darkSteel, new Vector3(-14f,  55f, -9f));
    }

    private static StandardMaterial3D Mat(Color albedo, float roughness, float metallic)
    {
        return new StandardMaterial3D { AlbedoColor = albedo, Roughness = roughness, Metallic = metallic };
    }

    private MeshInstance3D Spawn(string name, Mesh mesh, StandardMaterial3D mat, Vector3 pos)
    {
        var node = new MeshInstance3D { Name = name, Mesh = mesh, Position = pos };
        node.SetSurfaceOverrideMaterial(0, mat);
        AddChild(node);
        return node;
    }
}
