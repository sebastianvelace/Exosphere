namespace Exosphere.Game;

using Godot;

/// <summary>
/// First-person cockpit interior (Crew-Dragon style). Built in vessel-LOCAL space and parented to
/// the vessel render node, so it inherits the floating-origin orientation. CONTRACT: pilot eye at
/// local (0, 36, 0.6) u, forward = +Y (nose), up = -Z. Visible only in the Cockpit camera mode.
/// Exposes three flat screen meshes named Screen0 (centre), Screen1 (left), Screen2 (right) that
/// <see cref="CockpitInstruments"/> drives with live telemetry.
/// </summary>
public partial class CockpitRenderer : Node3D
{
    private const float EyeY = 36f;     // pilot eye height along the ship axis (+Y)
    private const float EyeZ = 0.6f;    // eye forward/down offset (up is -Z, so +Z is down)

    public bool CockpitVisible { get => Visible; set => Visible = value; }

    public override void _Ready()
    {
        Visible = false;
        Build();
    }

    private void Build()
    {
        var wall  = Mat(new Color(0.84f, 0.86f, 0.90f), 0.55f, 0.05f);
        var floor = Mat(new Color(0.58f, 0.60f, 0.64f), 0.7f, 0.05f);
        var dark  = Mat(new Color(0.05f, 0.06f, 0.08f), 0.45f, 0.25f);
        var trim  = Mat(new Color(0.18f, 0.20f, 0.24f), 0.5f, 0.35f);
        var seatM = Mat(new Color(0.10f, 0.11f, 0.14f), 0.75f, 0.0f);

        var ambient = Mat(new Color(0.55f, 0.72f, 1f), 0.3f, 0f);
        ambient.EmissionEnabled = true;
        ambient.Emission = new Color(0.35f, 0.5f, 0.85f);
        ambient.EmissionEnergyMultiplier = 2.0f;

        var screenM = Mat(new Color(0.02f, 0.03f, 0.05f), 0.25f, 0f);
        screenM.EmissionEnabled = true; screenM.Emission = new Color(0.04f, 0.07f, 0.12f);

        // ── Hull tube along the ship axis (+Y), seen from the inside ──────────────
        var hullMat = (StandardMaterial3D)wall.Duplicate();
        hullMat.CullMode = BaseMaterial3D.CullModeEnum.Front;   // render the inner surface
        Spawn("Hull", new CylinderMesh { TopRadius = 1.10f, BottomRadius = 1.10f, Height = 7.0f, RadialSegments = 32 },
              hullMat, new Vector3(0, EyeY, 0));

        // Forward bulkhead (nose end) and aft bulkhead, capping the compartment.
        Spawn("FwdBulkhead", new CylinderMesh { TopRadius = 0.2f, BottomRadius = 1.10f, Height = 0.9f, RadialSegments = 32 },
              wall, new Vector3(0, EyeY + 4.0f, 0));
        Spawn("AftBulkhead", new CylinderMesh { TopRadius = 1.10f, BottomRadius = 1.10f, Height = 0.12f, RadialSegments = 32 },
              trim, new Vector3(0, EyeY - 3.3f, 0));

        // Flat floor strip under the crew (down = +Z).
        Spawn("Floor", new BoxMesh { Size = new Vector3(1.7f, 5.5f, 0.1f) }, floor,
              new Vector3(0, EyeY - 0.2f, 1.05f));

        // Rib rings for structure.
        for (int i = -2; i <= 3; i++)
            Spawn($"Rib{i}", new TorusMesh { InnerRadius = 1.04f, OuterRadius = 1.12f, RingSegments = 8, Rings = 28 },
                  trim, new Vector3(0, EyeY + i * 1.1f, 0), new Vector3(90, 0, 0));

        // ── Console + 3 glass-cockpit screens (ahead, +Y; faced back toward the eye) ──
        Spawn("Console", new BoxMesh { Size = new Vector3(1.7f, 0.7f, 0.5f) }, dark,
              new Vector3(0, EyeY + 1.7f, 0.95f), new Vector3(-18, 0, 0));
        Spawn("ConsoleLip", new BoxMesh { Size = new Vector3(1.8f, 0.08f, 0.6f) }, trim,
              new Vector3(0, EyeY + 1.45f, 0.7f), new Vector3(-18, 0, 0));

        // Screens are thin flat panels perpendicular to +Y, so the eye (looking +Y) sees their face.
        var sb = new BoxMesh { Size = new Vector3(0.62f, 0.02f, 0.42f) };
        AddScreen("Screen0", sb, screenM, new Vector3(0f,    EyeY + 1.55f, 0.62f), new Vector3(-16, 0, 0));
        AddScreen("Screen1", sb, screenM, new Vector3(-0.7f, EyeY + 1.5f,  0.66f), new Vector3(-16, 0, 22));
        AddScreen("Screen2", sb, screenM, new Vector3(0.7f,  EyeY + 1.5f,  0.66f), new Vector3(-16, 0, -22));

        // ── Seats (two side by side, behind/below the console) ────────────────────
        for (int s = -1; s <= 1; s += 2)
        {
            float x = s * 0.42f;
            Spawn($"SeatPan{s}", new BoxMesh { Size = new Vector3(0.5f, 0.5f, 0.18f) }, seatM,
                  new Vector3(x, EyeY - 0.4f, 1.0f));
            Spawn($"SeatBack{s}", new BoxMesh { Size = new Vector3(0.5f, 0.16f, 0.7f) }, seatM,
                  new Vector3(x, EyeY - 0.62f, 0.55f), new Vector3(12, 0, 0));
            Spawn($"SeatHead{s}", new BoxMesh { Size = new Vector3(0.34f, 0.14f, 0.22f) }, seatM,
                  new Vector3(x, EyeY - 0.5f, 0.18f), new Vector3(12, 0, 0));
        }

        // ── Small round windows in the hull wall (exterior shows through the openings) ──
        for (int w = 0; w < 4; w++)
        {
            float ang = Mathf.Pi * (0.25f + 0.5f * w);
            var dir = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
            Spawn($"WinRing{w}", new TorusMesh { InnerRadius = 0.16f, OuterRadius = 0.21f, RingSegments = 6, Rings = 16 },
                  trim, new Vector3(dir.X * 1.05f, EyeY + 0.4f, dir.Z * 1.05f),
                  new Vector3(0, -Mathf.RadToDeg(ang), 90));
        }

        // ── Soft ambient light strips + interior lighting (the sealed hull gets no sun) ──
        for (int i = -1; i <= 1; i += 2)
            Spawn($"LightStrip{i}", new BoxMesh { Size = new Vector3(0.06f, 4.5f, 0.06f) }, ambient,
                  new Vector3(i * 1.0f, EyeY + 0.3f, -0.7f));

        AddChild(new OmniLight3D
        {
            Position = new Vector3(0, EyeY + 0.6f, -0.4f), OmniRange = 5.0f,
            LightEnergy = 1.6f, LightColor = new Color(0.82f, 0.87f, 1.0f),
        });
        AddChild(new OmniLight3D
        {
            Position = new Vector3(0, EyeY + 2.2f, 0.4f), OmniRange = 3.5f,
            LightEnergy = 1.0f, LightColor = new Color(0.9f, 0.92f, 1.0f),
        });
        _ = EyeZ;
    }

    private void AddScreen(string name, Mesh mesh, StandardMaterial3D mat, Vector3 pos, Vector3 rotDeg)
    {
        var n = new MeshInstance3D { Name = name, Mesh = mesh, Position = pos, RotationDegrees = rotDeg };
        n.SetSurfaceOverrideMaterial(0, mat);
        AddChild(n);
    }

    private void Spawn(string name, Mesh mesh, StandardMaterial3D mat, Vector3 pos, Vector3? rotDeg = null)
    {
        var n = new MeshInstance3D { Name = name, Mesh = mesh, Position = pos };
        if (rotDeg is { } r) n.RotationDegrees = r;
        n.MaterialOverride = mat;
        AddChild(n);
    }

    private static StandardMaterial3D Mat(Color albedo, float roughness, float metallic) => new()
    {
        AlbedoColor = albedo, Roughness = roughness, Metallic = metallic,
    };
}
