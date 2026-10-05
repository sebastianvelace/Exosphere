namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation.Presentation;

/// <summary>
/// Drives the three cockpit display screens (Screen0 centre / Screen1 left / Screen2 right,
/// created by <see cref="CockpitRenderer"/>) with live telemetry. Each screen is a SubViewport
/// rendering a 2D dashboard, wired onto the screen mesh as an unshaded emissive texture.
/// </summary>
public partial class CockpitInstruments : Node
{
    private readonly SubViewport[] _vp   = new SubViewport[3];
    private readonly ScreenPanel[] _pan  = new ScreenPanel[3];
    private const double CockpitRefreshHz = 30.0;
    private const double CockpitRefreshPeriod = 1.0 / CockpitRefreshHz;
    private bool _wired;
    private bool _cockpitRenderingActive;
    private double _refreshAccumulator;

    public override void _Ready()
    {
        for (int i = 0; i < 3; i++)
        {
            var vp = new SubViewport
            {
                Size = new Vector2I(512, 512),
                RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
                TransparentBg = false,
            };
            var p = new ScreenPanel { Which = i };
            p.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            p.CustomMinimumSize = new Vector2(512, 512);
            vp.AddChild(p);
            AddChild(vp);
            _vp[i] = vp; _pan[i] = p;
        }

        // The screen textures remain allocated and wired while the cockpit is hidden,
        // but their render targets do not need a frame every tick. Cockpit presentation
        // is refreshed at 30 Hz with UpdateMode.Once; physics and HUD snapshots continue
        // at their normal cadence.
        SetViewportUpdateMode(active: false);
        GD.Print("PERF_COCKPIT stage=created viewports=3 size=512x512 update=disabled refreshHz=30");
    }

    public override void _Process(double delta)
    {
        bool cockpitActive = CameraController.Instance?.IsCockpitView == true;
        if (cockpitActive != _cockpitRenderingActive)
        {
            _cockpitRenderingActive = cockpitActive;
            SetViewportUpdateMode(cockpitActive);
            GD.Print($"PERF_COCKPIT stage=update_mode cockpit={cockpitActive.ToString().ToLowerInvariant()} " +
                     $"viewports=3 mode={(cockpitActive ? "once" : "disabled")} refreshHz=30");
        }

        if (_cockpitRenderingActive)
        {
            _refreshAccumulator += delta;
            if (_refreshAccumulator >= CockpitRefreshPeriod)
            {
                _refreshAccumulator %= CockpitRefreshPeriod;
                for (int i = 0; i < 3; i++)
                {
                    _pan[i].QueueRedraw();
                    _vp[i].RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
                }
            }
        }

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
                EmissionEnergyMultiplier = 1.15f,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                DisableReceiveShadows = true,
                NoDepthTest = true,
            };
            screen.SetSurfaceOverrideMaterial(0, mat);
        }
        if (all) _wired = true;
    }

    private void SetViewportUpdateMode(bool active)
    {
        var mode = SubViewport.UpdateMode.Disabled;
        _refreshAccumulator = active ? CockpitRefreshPeriod : 0.0;
        for (int i = 0; i < _vp.Length; i++)
            if (_vp[i] != null)
                _vp[i].RenderTargetUpdateMode = mode;
    }
}

/// <summary>
/// One cockpit screen's 2D content (Which: 0 PFD, 1 attitude, 2 engines/entry). Same flight-test
/// tokens as the exterior HUD: flat near-black, hairline rules, condensed labels, monospaced
/// numbers, and colour used only to carry a state.
/// </summary>
public partial class ScreenPanel : Control
{
    public int Which;

    private static readonly Color Bg    = new(0.020f, 0.021f, 0.023f);
    private static readonly Color Rule  = InterfaceTheme.Edge;
    private static readonly Color White = InterfaceTheme.Text;
    private static readonly Color Dim   = InterfaceTheme.TextMuted;
    private static readonly Color Faint = InterfaceTheme.TextFaint;
    private static readonly Color Amber = InterfaceTheme.Warning;
    private static readonly Color Green = InterfaceTheme.Success;
    private Font _font = null!;
    private Font _labelFont = null!;

    public override void _Ready()
    {
        _font = InterfaceTheme.MonoFont;
        _labelFont = InterfaceTheme.LabelFont;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, new Vector2(512, 512)), Bg);
        DrawRect(new Rect2(4, 4, 504, 504), Rule, false, 1f);

        var snapshot = HUDController.LatestSnapshot;
        if (snapshot == null) { Text(180, 250, "NO SIGNAL", Dim, 26); return; }

        switch (Which)
        {
            case 0: DrawPfd(snapshot); break;
            case 1: DrawAttitude(snapshot); break;
            default: DrawEngines(snapshot); break;
        }
    }

    private void DrawPfd(FlightHudSnapshot snapshot)
    {
        Heading(20, 40, "PRIMARY FLIGHT", 460);
        Label(20, 90, "SPEED");
        Text(20, 150, $"{snapshot.SurfaceSpeedMps * 3.6:N0}", White, 60);
        Label(360, 150, "KM/H");
        Label(20, 210, "MACH");
        Text(150, 210, FormatMach(snapshot.MachNumber), White, 30);
        Label(20, 260, "ALTITUDE");
        Text(
            20,
            312,
            snapshot.AltitudeM >= 1000
                ? $"{snapshot.AltitudeM / 1000:N1} km"
                : $"{snapshot.AltitudeM:N0} m",
            White,
            44);
        Label(20, 380, "VERT SPEED");
        Text(
            20,
            425,
            $"{snapshot.VerticalSpeedMps:+0;-0} m/s",
            snapshot.VerticalSpeedMps >= 0 ? White : Amber,
            32);
        Label(300, 380, "G-LOAD");
        Text(300, 425, double.IsFinite(snapshot.ProperAccelerationG) ? $"{snapshot.ProperAccelerationG:F2} g" : "—",
            snapshot.ProperAccelerationG > 4.0 ? Amber : White, 32);
    }

    private void DrawAttitude(FlightHudSnapshot snapshot)
    {
        Heading(20, 40, "ATTITUDE", 460);
        var c = new Vector2(256, 260);
        float r = 172f;
        double pitch = snapshot.VehiclePitchDeg;

        // Sky/ground split shifted by pitch — two flat tones, white horizon.
        DrawCircle(c, r, new Color(0.145f, 0.155f, 0.170f));
        if (double.IsFinite(pitch))
        {
            float ph = (float)(pitch / 90.0) * r;
            DrawRect(new Rect2(c.X - r, c.Y - ph, 2 * r, r + ph + 4),
                new Color(0.048f, 0.051f, 0.055f));
            DrawLine(new Vector2(c.X - r, c.Y - ph), new Vector2(c.X + r, c.Y - ph), White, 2f);
        }
        else Text(150, 320, "NO ATTITUDE", Dim, 22);
        DrawArc(c, r, 0, Mathf.Tau, 48, InterfaceTheme.EdgeStrong, 1f);
        // Fixed nose reticle.
        DrawLine(c - new Vector2(28, 0), c - new Vector2(8, 0), White, 2f);
        DrawLine(c + new Vector2(8, 0), c + new Vector2(28, 0), White, 2f);

        Label(20, 460, "PITCH");
        Text(110, 460, FormatDegrees(pitch, signed: true), White, 24);
        Label(230, 460, "AOA");
        Text(300, 460, FormatDegrees(snapshot.AngleOfAttackDeg, signed: false), White, 24);
        Label(20, 496, "BANK");
        Text(110, 496, FormatDegrees(snapshot.AerodynamicBankDeg, signed: true), White, 24);
        if (snapshot.SurfaceSpeedMps > 1 && double.IsFinite(snapshot.FlightPathAngleDeg))
            Text(300, 496, "PROGRADE", Green, 18);
    }

    private void DrawEngines(FlightHudSnapshot snapshot)
    {
        Heading(20, 40, "PROPULSION / ENTRY", 460);
        Label(20, 90, "THRUST");
        Text(250, 90, $"{snapshot.CurrentThrustN / 1000:N0} kN", White, 22);
        Label(20, 132, "THROTTLE");
        DrawRect(new Rect2(250, 118, 230, 16), InterfaceTheme.Track);
        DrawRect(new Rect2(250, 118, 230 * (float)snapshot.Throttle, 16), White);
        DrawRect(new Rect2(250, 118, 230, 16), Rule, false, 1f);
        Label(20, 178, "TWR");
        Text(
            250,
            178,
            $"{snapshot.ThrustToWeightRatio:N2}",
            snapshot.ThrustToWeightRatio >= 1 ? White : Amber,
            22);
        Label(20, 220, "STAGE Δv");
        Text(250, 220, double.IsFinite(snapshot.StageDeltaVMps) ? $"{snapshot.StageDeltaVMps:N0} m/s" : "—", White, 22);

        Heading(20, 272, "ENTRY", 460);
        Label(20, 312, "DYN q");
        Text(250, 312, double.IsFinite(snapshot.DynamicPressurePa) ? $"{snapshot.DynamicPressurePa / 1000:N1} kPa" : "—", White, 22);
        if (snapshot.DynamicPressurePa > 28_000)
            Text(400, 312, "MAX-Q", Amber, 20);
        Label(20, 350, "HEAT FLUX");
        Text(250, 350, FormatHeatFlux(snapshot.StagnationHeatFluxWPerM2), White, 22);

        Label(20, 420, "APO");
        Text(
            120,
            420,
            snapshot.ApoapsisAltitudeM is { } apoapsis ? Fmt(apoapsis) : "—",
            White,
            20);
        Label(20, 458, "PER");
        Text(
            120,
            458,
            snapshot.IsImpactTrajectory
                ? "IMPACT"
                : snapshot.PeriapsisAltitudeM is { } periapsis ? Fmt(periapsis) : "—",
            snapshot.IsImpactTrajectory ? InterfaceTheme.Alert : White,
            20);
    }

    private static string Fmt(double m) =>
        System.Math.Abs(m) >= 1e6 ? $"{m / 1e6:N1} Mm" : m >= 1000 ? $"{m / 1000:N0} km" : $"{m:N0} m";

    private static string FormatMach(double mach) =>
        !double.IsFinite(mach) ? "—" : mach >= 10.0 ? $"{mach:F1}" : $"{mach:F2}";

    private static string FormatDegrees(double degrees, bool signed) =>
        !double.IsFinite(degrees)
            ? "—"
            : signed ? $"{degrees:+0;-0}°" : $"{degrees:F0}°";

    private static string FormatHeatFlux(double wattsPerSquareMetre)
    {
        if (!double.IsFinite(wattsPerSquareMetre)) return "—";
        double kilowatts = wattsPerSquareMetre / 1000.0;
        return kilowatts >= 1000.0
            ? $"{kilowatts / 1000.0:F2} MW/m²"
            : $"{kilowatts:F0} kW/m²";
    }

    private void Heading(float x, float y, string s, float width)
    {
        DrawString(_labelFont, new Vector2(x, y), s, HorizontalAlignment.Left, -1, 18, Faint);
        DrawRect(new Rect2(x, y + 8f, width, 1f), Rule);
    }

    private void Label(float x, float y, string s) =>
        DrawString(_labelFont, new Vector2(x, y), s, HorizontalAlignment.Left, -1, 18, Dim);

    private void Text(float x, float y, string s, Color c, int size) =>
        DrawString(_font, new Vector2(x, y), s, HorizontalAlignment.Left, -1, size, c);
}
