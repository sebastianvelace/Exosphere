namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;

/// <summary>
/// Drives the three cockpit display screens (Screen0 centre / Screen1 left / Screen2 right,
/// created by <see cref="CockpitRenderer"/>) with live telemetry. Each screen is a SubViewport
/// rendering a 2D dashboard, wired onto the screen mesh as an unshaded emissive texture.
/// </summary>
public partial class CockpitInstruments : Node
{
    private readonly SubViewport[] _vp   = new SubViewport[3];
    private readonly ScreenPanel[] _pan  = new ScreenPanel[3];
    private bool _wired;

    public override void _Ready()
    {
        for (int i = 0; i < 3; i++)
        {
            var vp = new SubViewport
            {
                Size = new Vector2I(512, 512),
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                TransparentBg = false,
            };
            var p = new ScreenPanel { Which = i };
            p.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            p.CustomMinimumSize = new Vector2(512, 512);
            vp.AddChild(p);
            AddChild(vp);
            _vp[i] = vp; _pan[i] = p;
        }
    }

    public override void _Process(double delta)
    {
        for (int i = 0; i < 3; i++) _pan[i].QueueRedraw();
        if (_wired) return;

        bool all = true;
        for (int i = 0; i < 3; i++)
        {
            if (GetTree().Root.FindChild($"Screen{i}", true, false) is not MeshInstance3D screen) { all = false; continue; }
            var tex = _vp[i].GetTexture();
            var mat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoTexture = tex,
                EmissionEnabled = true,
                EmissionTexture = tex,
                EmissionEnergyMultiplier = 2.6f,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
            };
            screen.SetSurfaceOverrideMaterial(0, mat);
        }
        if (all) _wired = true;
    }
}

/// <summary>One cockpit screen's 2D content (Which: 0 PFD, 1 attitude, 2 engines/orbit).</summary>
public partial class ScreenPanel : Control
{
    public int Which;

    private static readonly Color Bg    = new(0.04f, 0.07f, 0.14f);
    private static readonly Color Cyan  = new(0.45f, 0.85f, 1.00f);
    private static readonly Color White = new(0.92f, 0.97f, 1.00f);
    private static readonly Color Dim   = new(0.55f, 0.65f, 0.78f);
    private static readonly Color Amber = new(1.00f, 0.75f, 0.30f);
    private static readonly Color Green = new(0.40f, 1.00f, 0.55f);
    private Font _font = null!;

    public override void _Ready() => _font = ThemeDB.GetFallbackFont();

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, new Vector2(512, 512)), Bg);
        DrawRect(new Rect2(4, 4, 504, 504), new Color(Cyan, 0.35f), false, 2f);

        var bridge = SimulationBridge.Instance;
        var v = bridge?.ActiveVessel;
        var uni = bridge?.Universe;
        if (v == null || uni == null) { Text(180, 250, "NO SIGNAL", Dim, 26); return; }

        var body   = uni.GetDominantBody(v.Position);
        double alt  = v.GetAltitude(body);
        double spd  = (v.Velocity - body.Velocity).Magnitude;
        var    sv   = v.GetSurfaceVelocity(body);
        double vsp  = sv.Dot((v.Position - body.Position).Normalized);
        double q    = v.GetDynamicPressure(body);

        switch (Which)
        {
            case 0: DrawPfd(spd, alt, vsp); break;
            case 1: DrawAttitude(v, body, sv); break;
            default: DrawEngines(v, body, q); break;
        }
    }

    private void DrawPfd(double spd, double alt, double vsp)
    {
        Text(20, 40, "PRIMARY FLIGHT", Dim, 18);
        Text(20, 90, "SPEED", Cyan, 20);
        Text(20, 150, $"{spd * 3.6:N0}", White, 64);
        Text(360, 165, "KM/H", Dim, 18);
        Text(20, 250, "ALTITUDE", Cyan, 20);
        Text(20, 305, alt >= 1000 ? $"{alt / 1000:N1} km" : $"{alt:N0} m", White, 48);
        Text(20, 380, "VERT SPEED", Cyan, 18);
        Text(20, 425, $"{vsp:+0;-0} m/s", vsp >= 0 ? Green : Amber, 34);
    }

    private void DrawAttitude(Vessel v, CelestialBody body, Vector3d sv)
    {
        Text(20, 40, "ATTITUDE", Dim, 18);
        var c = new Vector2(256, 270);
        float r = 180f;

        var up   = (v.Position - body.Position).Normalized;
        var nose = v.Orientation.Rotate(new Vector3d(0, 1, 0));
        double pitch = System.Math.Asin(System.Math.Clamp(nose.Dot(up), -1, 1)) * 180.0 / System.Math.PI;

        // Sky/ground split shifted by pitch.
        float ph = (float)(pitch / 90.0) * r;
        DrawCircle(c, r, new Color(0.10f, 0.18f, 0.30f));
        DrawRect(new Rect2(c.X - r, c.Y - ph, 2 * r, r + ph + 4), new Color(0.30f, 0.22f, 0.12f));
        DrawLine(new Vector2(c.X - r, c.Y - ph), new Vector2(c.X + r, c.Y - ph), White, 2f);
        DrawArc(c, r, 0, Mathf.Tau, 48, Cyan, 2f);
        // Fixed nose reticle.
        DrawLine(c - new Vector2(28, 0), c - new Vector2(8, 0), Amber, 3f);
        DrawLine(c + new Vector2(8, 0), c + new Vector2(28, 0), Amber, 3f);

        Text(20, 470, $"PITCH {pitch:+0;-0}°", White, 22);
        if (sv.Magnitude > 1)
            Text(300, 470, "PROGRADE", Green, 18);
    }

    private void DrawEngines(Vessel v, CelestialBody body, double q)
    {
        Text(20, 40, "PROPULSION", Dim, 18);
        double thrust = v.GetCurrentThrust(body);
        double tw = v.GetThrustToWeightRatio(body);
        double dv = v.GetCurrentStageDeltaV(body);

        Text(20, 95, "THRUST", Cyan, 18);   Text(250, 95, $"{thrust / 1000:N0} kN", White, 22);
        Text(20, 140, "THROTTLE", Cyan, 18);
        DrawRect(new Rect2(250, 140, 230, 20), new Color(0.15f, 0.18f, 0.24f));
        DrawRect(new Rect2(250, 140, 230 * (float)v.Throttle, 20), Amber);
        Text(20, 195, "TWR", Cyan, 18);     Text(250, 195, $"{tw:N2}", tw >= 1 ? Green : Amber, 22);
        Text(20, 240, "STAGE Δv", Cyan, 18); Text(250, 240, $"{dv:N0} m/s", White, 22);

        Text(20, 320, "DYN q", Cyan, 18);   Text(250, 320, $"{q / 1000:N1} kPa", White, 22);
        if (q > 28000) Text(250, 360, "MAX-Q", Amber, 24);

        var rel = v.Position - body.Position;
        var vel = v.Velocity - body.Velocity;
        var oe = OrbitalElements.FromStateVector(rel, vel, body.GM, body.Id, 0.0);
        Text(20, 420, "APO", Cyan, 18); Text(120, 420, Fmt(oe.Apoapsis - body.Radius), White, 20);
        Text(20, 458, "PER", Cyan, 18); Text(120, 458, Fmt(oe.Periapsis - body.Radius), White, 20);
    }

    private static string Fmt(double m) =>
        System.Math.Abs(m) >= 1e6 ? $"{m / 1e6:N1} Mm" : m >= 1000 ? $"{m / 1000:N0} km" : $"{m:N0} m";

    private void Text(float x, float y, string s, Color c, int size) =>
        DrawString(_font, new Vector2(x, y), s, HorizontalAlignment.Left, -1, size, c);
}
