namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation;
using Exosphere.Simulation.Math;

/// <summary>
/// Entry, Descent and Landing director + HUD overlay. Activates when the active
/// vessel is descending fast into an atmospheric body, then sequences
/// ENTRY → PEAK HEATING → AERO DESCENT → RETRO BURN → FINAL → TOUCHDOWN, drawing a
/// dedicated descent HUD (radar altimeter, vertical/horizontal speed, g-force,
/// plasma vignette) and flying a retrograde suicide-burn autopilot to a soft landing.
/// </summary>
public partial class EDLController : Control
{
    public static EDLController? Instance { get; private set; }

    private enum Edl { Inactive, Entry, Peak, Aero, Retro, Final, Touchdown }
    private Edl _phase = Edl.Inactive;

    // ── Trigger thresholds ────────────────────────────────────────────────────
    private const double EntrySpeed   = 1200.0;   // m/s surface speed to arm entry
    private const double TouchdownAlt  = 6.0;       // m (legs contact height)
    private const double TouchdownVel  = 3.0;       // m/s (real Starship sets down at ~1-2 m/s)

    // ── Live telemetry (refreshed each frame) ─────────────────────────────────
    private double _alt, _vUp, _horiz, _gForce, _heat;
    private string _bodyName = "";

    private Font _font = null!;
    private bool _legsDeployed;

    public override void _Ready()
    {
        Instance = this;
        _font = ThemeDB.GetFallbackFont();
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        var mission = MissionManager.Instance;
        if (vessel == null || universe == null) { Visible = false; return; }

        var body = universe.GetDominantBody(vessel.Position);
        if (body.Atmosphere == null) { Deactivate(); return; }

        // ── Refresh telemetry ──────────────────────────────────────────────────
        Vector3d up      = (vessel.Position - body.Position).Normalized;
        Vector3d surfVel = vessel.GetSurfaceVelocity(body);
        _alt    = body.GetAltitude(vessel.Position);
        _vUp    = surfVel.Dot(up);                         // + up, − down
        _horiz  = (surfVel - up * _vUp).Magnitude;
        _bodyName = body.Name;

        double mass = vessel.TotalMass;
        _gForce = vessel.GetProperAcceleration(body).Magnitude / 9.80665;

        double density = body.Atmosphere.GetDensity(_alt);
        double speed   = surfVel.Magnitude;
        _heat = density * speed * speed * speed;            // ∝ convective heat flux

        // ── Deactivation guard — runs BEFORE the activation check ─────────────
        // Si salimos de la atmósfera o estamos ascendiendo claramente, reseteamos la
        // EDL sin importar en qué fase interna estemos. Esto evita que una EDL real
        // previa deje la máquina de estados atorada cuando el vessel vuelve al espacio.
        // We only deactivate if we were actually running (not Inactive/Touchdown already
        // holding the vessel on the ground).
        if (_phase != Edl.Inactive && _phase != Edl.Touchdown)
        {
            bool aboveAtmo = _alt > body.Atmosphere.MaxAltitude * 1.05;
            bool ascending = _vUp > 5.0;   // +5 m/s upward — clearly climbing away
            if (aboveAtmo || ascending)
            {
                Deactivate();
                return;
            }
        }

        // ── Activation check ───────────────────────────────────────────────────
        if (_phase == Edl.Inactive)
        {
            bool descending = _vUp < -20.0;
            bool inAtmo     = _alt < body.Atmosphere.MaxAltitude * 1.05;
            if (descending && inAtmo && speed > EntrySpeed && (mission == null || !mission.InDescent))
            {
                _phase = Edl.Entry;
                _legsDeployed = false;
                Visible = true;
                mission?.EnterPhase(MissionPhase.ENTRY);
            }
            else return;
        }

        AdvancePhase(vessel, body, mission, mass, speed, up, surfVel, delta, universe);
        QueueRedraw();   // live telemetry overlay
    }

    private void AdvancePhase(Vessel vessel, CelestialBody body, MissionManager? mission,
        double mass, double speed, Vector3d up, Vector3d surfVel, double delta, Universe universe)
    {
        double aThrustFull = MaxThrustAccel(vessel, body, mass);
        double g = body.GetSurfaceGravity();

        double vDown   = System.Math.Max(0.0, -_vUp);
        double vertFrac = speed > 1e-3 ? vDown / speed : 1.0;

        // Distance the FULL retrograde burn needs to null the WHOLE velocity vector (not just the
        // vertical part) — engines point retrograde, so they kill total speed. Net decel is the
        // thrust minus the along-track gravity component.
        double aBrake   = aThrustFull - g * vertFrac;
        double stopDist = aBrake > 0.5 ? speed * speed / (2.0 * aBrake) : double.MaxValue;

        double atmoTop = body.Atmosphere!.MaxAltitude;

        // Flip to the landing burn LOW, after the belly-flop has bled off velocity aerodynamically
        // (real Starship belly-flops to near terminal velocity, then flips at ~0.5-2 km). Hold the
        // broadside attitude through the whole aero descent — flipping to engines-retrograde high
        // up loses the drag, lets the vessel penetrate deep at hypersonic speed, and burns it up.
        // Gate: drop the belly-flop only once we're within the burn's stopping distance (so a fast
        // arrival still ignites in time) AND below a low flip ceiling so a nominal aero-braked entry
        // doesn't flip prematurely and waste propellant on a huge high-altitude burn.
        // Flip altitude: scale with the burn's stopping distance for a fast arrival, but never
        // below ~800 m so a vessel already at belly-flop terminal velocity (~70-100 m/s) still has
        // comfortable room to flip and null the descent (a too-low flip can't arrest it in time).
        const double FlipCeiling = 8_000.0;
        double flipAlt = aBrake > 0.5
            ? System.Math.Clamp(stopDist * 1.6, 800.0, FlipCeiling)
            : 0.0;   // can't brake yet (still hypersonic) — keep belly-flop
        if (_phase is Edl.Entry or Edl.Peak or Edl.Aero && vDown > 5.0 && _alt <= flipAlt)
        {
            _phase = Edl.Retro;
            mission?.EnterPhase(MissionPhase.RETRO_BURN);
        }

        switch (_phase)
        {
            case Edl.Entry:
                if (_heat > 4.0e7) { _phase = Edl.Peak; mission?.EnterPhase(MissionPhase.PEAK_HEATING); }
                break;
            case Edl.Peak:
                // Heating subsides once we've descended through the dense layer.
                if (_alt < atmoTop * 0.40) { _phase = Edl.Aero; mission?.EnterPhase(MissionPhase.AERO_DESCENT); }
                break;
            case Edl.Aero:
                break;   // retro ignition handled by the physics gate above
            case Edl.Retro:
                if (_alt < 1500.0) { _phase = Edl.Final; mission?.EnterPhase(MissionPhase.FINAL_DESCENT); }
                break;
            case Edl.Final:
                if (_alt < 500.0) _legsDeployed = true;
                if (_alt < TouchdownAlt && System.Math.Abs(_vUp) < TouchdownVel)
                {
                    _phase = Edl.Touchdown;
                    Touchdown(vessel, body, mission);
                    return;
                }
                break;
            case Edl.Touchdown:
                return;
        }

        // ── Attitude: belly-flop in the aero phases, flip-and-burn for the descent ─
        // Entry/Peak/Aero: present the long axis broadside to the airflow (max drag,
        // heat-shield windward) to bleed velocity aerodynamically like real Starship.
        // Retro/Final: flip so the engines (local +Y thrust) point retrograde.
        Vector3d velDir = surfVel.Magnitude > 1e-3 ? surfVel.Normalized : -up;
        Vector3d aimAxis;
        if (_phase is Edl.Entry or Edl.Peak or Edl.Aero)
        {
            Vector3d perp = up - velDir * up.Dot(velDir);   // up-component ⊥ to airflow
            aimAxis = perp.Magnitude > 1e-3 ? perp.Normalized : AnyPerp(velDir);
        }
        else
        {
            aimAxis = -velDir;                              // engines retrograde
        }
        // In the aero phases pitch is not enough: roll the vehicle so the actual tiled
        // local -X belly faces the velocity vector. This keeps rendering, heating and drag
        // on the same physical side of the Ship. During the landing burn only the thrust
        // axis matters, so use the shortest rotation.
        vessel.Orientation = _phase is Edl.Entry or Edl.Peak or Edl.Aero
            ? Exosphere.Simulation.Physics.AerodynamicsModel.ComputeBellyFirstOrientation(aimAxis, velDir)
            : ShortestArc(Vector3d.Up, aimAxis);
        vessel.AngularVelocity = Vector3d.Zero;
        vessel.PitchYawRoll    = Vector3d.Zero;

        // ── Throttle: closed-loop descent-rate profile to a soft touchdown ──────
        // By the time we flip (low, post-belly-flop) the velocity is mostly vertical. Track a
        // target descent rate that follows a constant-deceleration profile easing to ~1.5 m/s at
        // the pad: v_target(alt) = √(2·a·(alt−stopAlt)) + 1.5. Reserve braking authority (use 60%
        // of thrust for the profile) so the closed loop has headroom and the engine spool can keep
        // up — the old minimum-energy "stop exactly at the ground" burn commanded almost no thrust
        // until the last instant and touched down hot.
        if (_phase is Edl.Retro or Edl.Final)
        {
            const double stopAlt = 6.0;
            // Target descent rate: a gentle LINEAR profile that eases to ~1.5 m/s at the pad and
            // is already below the post-belly-flop terminal velocity (~70 m/s) at the flip, so the
            // burn starts braking immediately. Cap it by a constant-deceleration limit so a faster
            // arrival is still braked hard enough. Close the loop with gravity feed-forward.
            double vTargetLin = 1.5 + _alt * 0.06;
            double vTargetMax = System.Math.Sqrt(2.0 * 0.60 * aThrustFull * System.Math.Max(0.0, _alt - stopAlt)) + 1.5;
            double vTarget    = System.Math.Min(vTargetLin, vTargetMax);
            double aCmd = 1.6 * (vDown - vTarget) + g;      // >0 ⇒ thrust up to brake toward profile
            double thr  = aThrustFull > 1e-6 ? aCmd / aThrustFull : 0.0;
            vessel.Throttle = System.Math.Clamp(thr, 0.0, 1.0);
        }
        else
        {
            vessel.Throttle = 0.0;                          // unpowered aero entry
        }
    }

    private void Touchdown(Vessel vessel, CelestialBody body, MissionManager? mission)
    {
        vessel.Throttle = 0.0;
        // Plant the vessel on the surface (reuses the pre-launch ground-hold clamp).
        Vector3d up = (vessel.Position - body.Position).Normalized;
        vessel.IsGroundHeld = true;
        vessel.GroundNormal = up;
        vessel.GroundOffset = System.Math.Max(0.0, _alt);
        vessel.AngularVelocity = Vector3d.Zero;
        vessel.Orientation  = ShortestArc(Vector3d.Up, up);  // stand upright on the legs
        mission?.EnterPhase(MissionPhase.LANDED);
        GD.Print($"[EDL] TOUCHDOWN on {body.Name}  vUp={_vUp:F1} m/s");
    }

    private void Deactivate()
    {
        if (_phase != Edl.Inactive) { _phase = Edl.Inactive; Visible = false; }
    }

    // ── HUD overlay ─────────────────────────────────────────────────────────────

    public override void _Draw()
    {
        if (_phase == Edl.Inactive) return;
        var vp = GetViewportRect().Size;

        // Plasma vignette during high heating.
        if (_phase is Edl.Entry or Edl.Peak)
        {
            float intensity = (float)System.Math.Clamp(_heat / 8.0e7, 0.0, 1.0);
            DrawPlasma(vp, intensity);
        }

        DrawAltimeter(vp);
        DrawTelemetry(vp);
        DrawPhaseBanner(vp);
    }

    private void DrawPlasma(Vector2 vp, float k)
    {
        // Layered translucent bands from screen edge — brighter at the bottom (windward).
        var hot = new Color(1.0f, 0.45f, 0.12f);
        int bands = 7;
        for (int i = 0; i < bands; i++)
        {
            float t = i / (float)(bands - 1);
            float thick = vp.Y * 0.5f * (1f - t);
            float a = k * 0.22f * (1f - t);
            DrawRect(new Rect2(0, vp.Y - thick, vp.X, thick), new Color(hot, a));   // bottom glow
            DrawRect(new Rect2(0, 0, vp.X, thick * 0.5f), new Color(hot, a * 0.4f)); // top
        }
    }

    private void DrawAltimeter(Vector2 vp)
    {
        const float maxAlt = 5000f;
        float x = 70f, top = vp.Y * 0.25f, h = vp.Y * 0.5f, w = 22f;
        DrawRect(new Rect2(x, top, w, h), new Color(0.05f, 0.07f, 0.10f, 0.75f));
        DrawRect(new Rect2(x, top, w, h), new Color(0.45f, 0.65f, 0.95f, 0.6f), false, 1.4f);

        float frac = (float)System.Math.Clamp(_alt / maxAlt, 0, 1);
        float markY = top + h * (1f - frac);
        var col = _legsDeployed ? new Color(0.45f, 1f, 0.6f) : new Color(1f, 0.8f, 0.25f);
        DrawRect(new Rect2(x - 5, markY - 2, w + 10, 4), col);
        Text($"{_alt:F0} m", new Vector2(x + w + 10, markY + 5), col, 16);

        // ticks
        for (int i = 0; i <= 5; i++)
        {
            float ty = top + h * (i / 5f);
            DrawLine(new Vector2(x, ty), new Vector2(x + 6, ty), new Color(0.5f, 0.6f, 0.7f, 0.7f), 1f);
            Text($"{(5 - i) * 1000}", new Vector2(x - 50, ty + 5), new Color(0.55f, 0.62f, 0.72f), 11);
        }
    }

    private void DrawTelemetry(Vector2 vp)
    {
        float x = 120f, y = vp.Y * 0.25f - 70f;
        double vDown = -_vUp;
        Color vsCol = System.Math.Abs(vDown) > 50 ? new Color(1f, 0.35f, 0.3f)
                    : System.Math.Abs(vDown) > 10 ? new Color(1f, 0.82f, 0.3f)
                    : new Color(0.4f, 1f, 0.5f);
        Text("VERTICAL",   new Vector2(x, y),      new Color(0.6f, 0.7f, 0.82f), 13);
        Text($"{vDown:+0;-0} m/s", new Vector2(x, y + 20), vsCol, 22);
        Text("HORIZONTAL", new Vector2(x, y + 52), new Color(0.6f, 0.7f, 0.82f), 13);
        Text($"{_horiz:F0} m/s", new Vector2(x, y + 72), new Color(0.9f, 0.95f, 1f), 20);
        Text("G-FORCE",    new Vector2(x, y + 104), new Color(0.6f, 0.7f, 0.82f), 13);
        Color gCol = _gForce > 4 ? new Color(1f, 0.4f, 0.3f) : new Color(0.85f, 0.9f, 1f);
        Text($"{_gForce:F1} g", new Vector2(x, y + 124), gCol, 20);
    }

    private void DrawPhaseBanner(Vector2 vp)
    {
        string label = _phase switch
        {
            Edl.Entry => "ATMOSPHERIC ENTRY",
            Edl.Peak  => "PEAK HEATING",
            Edl.Aero  => "AERODYNAMIC DESCENT",
            Edl.Retro => "RETRO BURN",
            Edl.Final => "FINAL DESCENT",
            Edl.Touchdown => "TOUCHDOWN",
            _ => "",
        };
        var col = _phase switch
        {
            Edl.Entry => new Color(1f, 0.6f, 0.3f),
            Edl.Peak  => new Color(1f, 0.35f, 0.2f),
            Edl.Aero  => new Color(1f, 0.78f, 0.4f),
            Edl.Retro => new Color(0.5f, 0.85f, 1f),
            Edl.Final => new Color(0.6f, 0.9f, 1f),
            Edl.Touchdown => new Color(0.45f, 1f, 0.6f),
            _ => Colors.White,
        };
        var size = _font.GetStringSize(label, HorizontalAlignment.Center, -1, 30);
        var pos  = new Vector2((vp.X - size.X) * 0.5f, vp.Y * 0.16f);
        DrawString(_font, pos + new Vector2(2, 2), label, HorizontalAlignment.Left, -1, 30, new Color(0, 0, 0, 0.7f));
        DrawString(_font, pos, label, HorizontalAlignment.Left, -1, 30, col);

        string sub = $"EDL · {_bodyName.ToUpperInvariant()}" + (_legsDeployed ? "   LEGS DOWN" : "");
        Text(sub, new Vector2((vp.X - _font.GetStringSize(sub, HorizontalAlignment.Center, -1, 14).X) * 0.5f, vp.Y * 0.16f + 34), new Color(0.7f, 0.78f, 0.88f), 14);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private void Text(string s, Vector2 pos, Color c, int size) =>
        DrawString(_font, pos, s, HorizontalAlignment.Left, -1, size, c);

    private static double MaxThrustAccel(Vessel vessel, CelestialBody body, double mass)
    {
        if (mass <= 0) return 0;
        return vessel.GetMaximumThrust(body) / mass;
    }

    private static Vector3d AnyPerp(Vector3d v)
    {
        var a = System.Math.Abs(v.X) < 0.9 ? Vector3d.Right : Vector3d.Up;
        return v.Cross(a).Normalized;
    }

    private static Quaterniond ShortestArc(Vector3d from, Vector3d to)
    {
        var f = from.Normalized; var t = to.Normalized;
        double dot = f.Dot(t);
        if (dot > 0.99999) return Quaterniond.Identity;
        if (dot < -0.99999)
        {
            Vector3d ax = System.Math.Abs(f.X) < 0.9 ? f.Cross(Vector3d.Right) : f.Cross(Vector3d.Up);
            return Quaterniond.FromAxisAngle(ax.Normalized, System.Math.PI);
        }
        return Quaterniond.FromAxisAngle(f.Cross(t).Normalized,
            System.Math.Acos(System.Math.Clamp(dot, -1.0, 1.0)));
    }
}
