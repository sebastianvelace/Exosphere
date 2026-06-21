namespace Exosphere.Game;

using Godot;

/// <summary>
/// First-person cockpit interior (Crew-Dragon style). Built in vessel-LOCAL space and parented to
/// the vessel render node so it inherits the floating-origin orientation. CONTRACT: pilot eye at
/// local (0, 36, 0.6) u, forward = +Y (nose), up = -Z. Open at the front (windshield to space);
/// a raked dashboard below the sightline holds three screens (Screen0 centre / Screen1 left /
/// Screen2 right) that <see cref="CockpitInstruments"/> drives with live telemetry. Visible only
/// in the Cockpit camera mode.
/// </summary>
public partial class CockpitRenderer : Node3D
{
    private const float EyeY = 36f;
    private const float EyeZ = 0.6f;

    public bool CockpitVisible { get => Visible; set => Visible = value; }

    public override void _Ready()
    {
        Visible = false;
        Build();
    }

    private void Build()
    {
        var wall   = Mat(new Color(0.80f, 0.83f, 0.88f), 0.5f, 0.08f);
        var floorM = Mat(new Color(0.46f, 0.49f, 0.54f), 0.7f, 0.05f);
        var panel  = Mat(new Color(0.09f, 0.10f, 0.13f), 0.4f, 0.3f);   // dark console
        var trim   = Mat(new Color(0.30f, 0.34f, 0.40f), 0.4f, 0.45f);
        var frameM = Mat(new Color(0.55f, 0.58f, 0.64f), 0.4f, 0.5f);
        var seatM  = Mat(new Color(0.12f, 0.13f, 0.16f), 0.72f, 0.0f);

        var screenM = Mat(new Color(0.02f, 0.04f, 0.08f), 0.2f, 0f);
        screenM.EmissionEnabled = true; screenM.Emission = new Color(0.06f, 0.12f, 0.22f);

        // ── Enclosing shell — open at the FRONT (windshield). Ceiling/walls/back/floor only. ──
        Spawn("Ceiling", new BoxMesh { Size = new Vector3(2.3f, 2.7f, 0.10f) }, wall,  new Vector3(0, EyeY - 0.4f, -0.85f), new Vector3(-10, 0, 0));
        Spawn("WallL",   new BoxMesh { Size = new Vector3(0.10f, 3.0f, 1.7f) }, wall,  new Vector3(-1.12f, EyeY - 0.2f, 0.15f));
        Spawn("WallR",   new BoxMesh { Size = new Vector3(0.10f, 3.0f, 1.7f) }, wall,  new Vector3( 1.12f, EyeY - 0.2f, 0.15f));
        Spawn("Back",    new BoxMesh { Size = new Vector3(2.3f, 0.10f, 1.9f) }, wall,  new Vector3(0, EyeY - 1.7f, 0.15f));
        Spawn("Floor",   new BoxMesh { Size = new Vector3(2.3f, 3.2f, 0.10f) }, floorM, new Vector3(0, EyeY - 0.1f, 1.05f));

        // ── Dashboard: a wide raked console below the forward sightline ────────────
        Spawn("Dash",     new BoxMesh { Size = new Vector3(2.1f, 0.78f, 0.12f) }, panel, new Vector3(0, EyeY + 1.22f, 1.34f), new Vector3(54, 0, 0));
        Spawn("DashEdge", new BoxMesh { Size = new Vector3(2.15f, 0.06f, 0.16f) }, trim, new Vector3(0, EyeY + 0.86f, 1.12f), new Vector3(54, 0, 0));

        // Three screens on the dashboard, auto-oriented to face the pilot eye.
        var sq = new QuadMesh { Size = new Vector2(0.46f, 0.30f) };
        AddScreen("Screen0", sq, screenM, new Vector3(0f,     EyeY + 1.08f, 1.20f));
        AddScreen("Screen1", sq, screenM, new Vector3(-0.58f, EyeY + 1.06f, 1.22f));
        AddScreen("Screen2", sq, screenM, new Vector3(0.58f,  EyeY + 1.06f, 1.22f));

        // ── Windshield frame (the front is open to space; this just outlines it) ───
        Spawn("WsTop",     new BoxMesh { Size = new Vector3(2.0f, 0.09f, 0.09f) }, frameM, new Vector3(0, EyeY + 1.9f, -0.55f));
        Spawn("WsPillarL", new BoxMesh { Size = new Vector3(0.09f, 1.7f, 0.09f) }, frameM, new Vector3(-1.0f, EyeY + 1.45f, 0.0f),  new Vector3(38, 0, 0));
        Spawn("WsPillarR", new BoxMesh { Size = new Vector3(0.09f, 1.7f, 0.09f) }, frameM, new Vector3( 1.0f, EyeY + 1.45f, 0.0f),  new Vector3(38, 0, 0));
        Spawn("WsCentre",  new BoxMesh { Size = new Vector3(0.05f, 1.6f, 0.05f) }, frameM, new Vector3(0f,    EyeY + 1.5f, -0.05f), new Vector3(38, 0, 0));

        // ── Seats (two side by side, reclined) ─────────────────────────────────────
        for (int s = -1; s <= 1; s += 2)
        {
            float x = s * 0.42f;
            Spawn($"SeatPan{s}",  new BoxMesh { Size = new Vector3(0.5f, 0.55f, 0.16f) }, seatM, new Vector3(x, EyeY - 0.55f, 1.0f));
            Spawn($"SeatBack{s}", new BoxMesh { Size = new Vector3(0.5f, 0.16f, 0.75f) }, seatM, new Vector3(x, EyeY - 0.8f, 0.55f), new Vector3(14, 0, 0));
        }

        // ── Soft interior lighting (sealed hull gets no sun) ───────────────────────
        AddChild(new OmniLight3D { Position = new Vector3(0, EyeY + 0.4f, -0.5f), OmniRange = 5.0f, LightEnergy = 1.7f, LightColor = new Color(0.85f, 0.89f, 1.0f) });
        AddChild(new OmniLight3D { Position = new Vector3(0, EyeY + 1.0f, 0.7f),  OmniRange = 3.0f, LightEnergy = 1.1f, LightColor = new Color(0.6f, 0.75f, 1.0f) });
        _ = EyeZ;
    }

    // Places a screen quad and orients its visible (+Z) face toward the pilot eye.
    private void AddScreen(string name, Mesh mesh, StandardMaterial3D mat, Vector3 pos)
    {
        var eye = new Vector3(0, EyeY, EyeZ);
        Vector3 f = (eye - pos).Normalized();
        Vector3 upRef = new Vector3(0, 0, -1);
        if (Mathf.Abs(f.Dot(upRef)) > 0.95f) upRef = new Vector3(0, 1, 0);
        Vector3 right = upRef.Cross(f).Normalized();
        Vector3 up    = f.Cross(right).Normalized();
        var n = new MeshInstance3D { Name = name, Mesh = mesh };
        n.Transform = new Transform3D(new Basis(right, up, f), pos);
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
