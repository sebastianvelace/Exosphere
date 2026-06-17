namespace Exosphere.Game;

using Godot;
using Exosphere.Simulation.Math;

// ── Navball / attitude indicator (T7) ───────────────────────────────────────
// A bottom-centre artificial-horizon disc giving manual-flight feedback: pitch
// ladder + roll, a heading tape, and prograde/retrograde/radial markers plus the
// nose/heading reticle. Drawn self-contained (Control + _Draw); instantiated as a
// child of HUDController. Reads ONLY public getters and derives the local frame in
// the HUD layer, so it does not depend on the data-only NavBallController node.
//
// Local reference frame at the vessel (right-handed, body-relative):
//   up    = radial-out (away from the reference body centre)
//   north = component of the body spin axis perpendicular to up
//   east  = north × up
// Attitude is expressed by projecting the vessel nose into this frame.
public partial class AttitudeNavball : Control
{
    private static readonly Color PanelBg     = new(0.03f, 0.05f, 0.08f, 0.65f);
    private static readonly Color PanelBorder = new(0.28f, 0.55f, 0.85f, 0.45f);
    private static readonly Color SkyCol      = new(0.18f, 0.42f, 0.72f, 1f);
    private static readonly Color GroundCol   = new(0.42f, 0.30f, 0.16f, 1f);
    private static readonly Color HorizonCol  = new(0.95f, 0.97f, 1.00f, 1f);
    private static readonly Color LadderCol   = new(0.85f, 0.90f, 0.98f, 0.75f);
    private static readonly Color Reticle     = new(1.00f, 0.85f, 0.20f, 1f);
    private static readonly Color ProgradeCol = new(0.40f, 1.00f, 0.55f, 1f);
    private static readonly Color RetroCol    = new(1.00f, 0.45f, 0.40f, 1f);
    private static readonly Color RadialCol   = new(0.55f, 0.80f, 1.00f, 1f);
    private static readonly Color LabelDim    = new(0.65f, 0.72f, 0.82f, 1f);
    private static readonly Color ValueBright = new(0.92f, 0.96f, 1.00f, 1f);

    private const float Radius = 78f;

    private Font _font = null!;

    // Cached attitude state (computed in _Process, drawn in _Draw).
    private bool   _valid;
    private double _pitchDeg;     // nose elevation above local horizon
    private double _headingDeg;   // compass heading 0..360 (0 = north)
    private double _rollDeg;      // bank angle, +right-wing-down
    private Vector2 _prograde;    // marker offsets in disc space (px), or NaN if behind
    private Vector2 _retrograde;
    private Vector2 _radialOut;
    private bool   _progradeVisible;
    private bool   _radialVisible;
    private double _speed;

    public override void _Ready()
    {
        _font = ThemeDB.FallbackFont;
        SetAnchorsPreset(LayoutPreset.CenterBottom);
        GrowHorizontal = GrowDirection.Both;
        GrowVertical   = GrowDirection.Begin;
        CustomMinimumSize = new Vector2(2 * Radius + 28, 2 * Radius + 46);
        OffsetLeft  = -(Radius + 14);
        OffsetRight =  (Radius + 14);
        OffsetTop   = -(2 * Radius + 56);
        OffsetBottom = -10;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        var bridge = SimulationBridge.Instance;
        var vessel = bridge?.ActiveVessel;
        var universe = bridge?.Universe;
        if (vessel == null || universe == null) { _valid = false; QueueRedraw(); return; }

        var body = universe.GetDominantBody(vessel.Position);

        // ── Local navigation frame at the vessel ──────────────────────────────
        var up = (vessel.Position - body.Position).Normalized;          // radial out
        var spinAxis = new Vector3d(0, 1, 0);                           // body north (sim convention)
        var north = (spinAxis - up * spinAxis.Dot(up));
        north = north.MagnitudeSquared > 1e-9 ? north.Normalized
                                              : AnyPerpendicular(up);
        var east = north.Cross(up).Normalized;

        // Vessel nose: the +Y axis of the stack points "up" the rocket (see spawn).
        var nose = vessel.Orientation.Rotate(new Vector3d(0, 1, 0)).Normalized;
        var noseRight = vessel.Orientation.Rotate(new Vector3d(1, 0, 0)).Normalized;

        // Pitch: elevation of the nose above the local horizon.
        double sinPitch = System.Math.Clamp(nose.Dot(up), -1.0, 1.0);
        _pitchDeg = System.Math.Asin(sinPitch) * Rad2Deg;

        // Heading: when moving fast enough, use surface velocity projected to horizon
        // (avoids jumps when nose is nearly vertical); otherwise fall back to nose direction.
        var surfVelForHdg = vessel.GetSurfaceVelocity(body);
        double speedForHdg = surfVelForHdg.Magnitude;
        var hdgDir = speedForHdg > 5.0
            ? (surfVelForHdg - up * surfVelForHdg.Dot(up))   // surface velocity on horizon plane
            : (nose - up * nose.Dot(up));                      // nose on horizon plane (fallback)
        if (hdgDir.MagnitudeSquared > 1e-9)
        {
            hdgDir = hdgDir.Normalized;
            double rawHdg = (System.Math.Atan2(hdgDir.Dot(east), hdgDir.Dot(north)) * Rad2Deg + 360.0) % 360.0;
            // Low-pass filter to smooth out numerical jumps; handle 0/360 wrap-around.
            double diff = rawHdg - _headingDeg;
            if (diff > 180) diff -= 360;
            if (diff < -180) diff += 360;
            _headingDeg = (_headingDeg + diff * System.Math.Min(delta * 8.0, 1.0) + 360.0) % 360.0;
        }

        // Roll: bank of the wings about the nose axis, measured against local up.
        var rightInHoriz = noseRight - nose * noseRight.Dot(nose);
        if (rightInHoriz.MagnitudeSquared > 1e-9)
        {
            rightInHoriz = rightInHoriz.Normalized;
            var levelRight = nose.Cross(up).Normalized;       // wings-level reference
            double cosR = System.Math.Clamp(rightInHoriz.Dot(levelRight), -1.0, 1.0);
            double sinR = rightInHoriz.Dot(up);               // +ve → right wing down
            _rollDeg = System.Math.Atan2(sinR, cosR) * Rad2Deg;
        }

        // ── Velocity markers (surface-relative) ───────────────────────────────
        var surfVel = vessel.GetSurfaceVelocity(body);
        _speed = surfVel.Magnitude;
        var prograde = _speed > 0.1 ? surfVel.Normalized : nose;

        (_prograde, _progradeVisible)   = ProjectMarker(prograde, nose, noseRight, up);
        (_retrograde, _)                = ProjectMarker(-prograde, nose, noseRight, up);
        (_radialOut, _radialVisible)    = ProjectMarker(up, nose, noseRight, up);

        _valid = true;
        QueueRedraw();
    }

    // Projects a world direction onto the navball disc as seen from the nose.
    // The disc plane is spanned by the nose-right axis (screen X) and the nose-up
    // (= up component perpendicular to nose) axis (screen -Y). A marker is "visible"
    // when it points into the forward hemisphere (dot(dir, nose) > 0).
    private (Vector2 offset, bool front) ProjectMarker(
        Vector3d dir, Vector3d nose, Vector3d noseRight, Vector3d up)
    {
        var screenUp = (up - nose * up.Dot(nose));
        screenUp = screenUp.MagnitudeSquared > 1e-9 ? screenUp.Normalized
                                                     : noseRight.Cross(nose).Normalized;
        var screenRight = noseRight - nose * noseRight.Dot(nose);
        screenRight = screenRight.MagnitudeSquared > 1e-9 ? screenRight.Normalized
                                                          : nose.Cross(screenUp).Normalized;

        double fwd = dir.Dot(nose);
        double x = dir.Dot(screenRight);
        double y = dir.Dot(screenUp);
        // Scale so a 90° offset sits near the rim.
        float px = (float)(x) * (Radius - 12f);
        float py = (float)(-y) * (Radius - 12f);
        return (new Vector2(px, py), fwd > 0.0);
    }

    public override void _Draw()
    {
        var center = new Vector2(Size.X * 0.5f, Radius + 8f);

        // Outer bezel.
        DrawCircle(center, Radius + 6f, PanelBg);
        DrawArc(center, Radius + 6f, 0, Mathf.Tau, 48, PanelBorder, 1.2f, true);

        if (!_valid)
        {
            DrawArc(center, Radius, 0, Mathf.Tau, 48, LabelDim, 1f, true);
            return;
        }

        // ── Attitude ball: sky/ground split + pitch ladder, rolled & pitched ──
        float pitchPx = (float)(_pitchDeg / 90.0) * Radius;     // horizon vertical shift
        float roll = (float)(_rollDeg * Mathf.Pi / 180.0);
        DrawAttitudeBall(center, pitchPx, roll);

        // Fixed aircraft reticle (nose marker) — always centre.
        DrawReticle(center);

        // Roll pointer at top of bezel.
        DrawRollPointer(center, roll);

        // Velocity / radial markers.
        if (_progradeVisible) DrawProgradeMarker(center + RotateOffset(_prograde, roll), ProgradeCol, true);
        else                  DrawProgradeMarker(center + RotateOffset(_retrograde, roll), RetroCol, false);
        if (_radialVisible)   DrawRadialMarker(center + RotateOffset(_radialOut, roll));

        // Heading tape under the ball + numeric attitude line.
        DrawHeadingTape(new Vector2(center.X, center.Y + Radius + 14f));
        DrawAttitudeText();
    }

    private void DrawAttitudeBall(Vector2 c, float pitchPx, float roll)
    {
        // Sky and ground are built as polygons CLIPPED to the disc, so the ball is always a
        // clean circle (no square corners). Sky = full disc; ground = the circular segment
        // below the horizon. Godot's immediate _Draw has no clip region, so we clip by hand.
        var dir = new Vector2(Mathf.Sin(roll), -Mathf.Cos(roll)); // "up" on the ball
        var rightv = new Vector2(dir.Y, -dir.X);

        // Horizon line is the chord at signed distance pitchPx (along dir) from the centre.
        float pp = Mathf.Clamp(pitchPx, -Radius, Radius);
        var horizonMid = c - dir * pp;
        float half = Mathf.Sqrt(Mathf.Max(0f, Radius * Radius - pp * pp));
        var i1 = horizonMid - rightv * half;   // chord endpoints on the circle
        var i2 = horizonMid + rightv * half;

        // Base disc = sky.
        DrawCircle(c, Radius, SkyCol);

        // Ground = circular segment below the horizon, walked around the rim with the chord
        // intersections inserted at the two crossings (perfect clip, handles any roll/pitch).
        var grd = new System.Collections.Generic.List<Vector2>();
        const int N = 48;
        Vector2 prev = c + new Vector2(Radius, 0f);
        bool prevG = (prev - horizonMid).Dot(dir) < 0f;
        for (int i = 1; i <= N; i++)
        {
            float a = i / (float)N * Mathf.Tau;
            var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Radius;
            bool pG = (p - horizonMid).Dot(dir) < 0f;
            if (prevG) grd.Add(prev);
            if (pG != prevG)
            {
                var mid = (prev + p) * 0.5f;
                grd.Add((i1 - mid).LengthSquared() <= (i2 - mid).LengthSquared() ? i1 : i2);
            }
            prev = p; prevG = pG;
        }
        if (grd.Count >= 3) DrawColoredPolygon(grd.ToArray(), GroundCol);

        // Bezel + horizon line (the chord).
        DrawArc(c, Radius, 0, Mathf.Tau, 64, PanelBorder, 1.4f, true);
        if (half > 0.5f) DrawLine(i1, i2, HorizonCol, 1.6f, true);

        // Pitch ladder: ticks every 30°, within the disc.
        for (int deg = -60; deg <= 60; deg += 30)
        {
            if (deg == 0) continue;
            float off = (float)(deg / 90.0) * Radius;
            var lineMid = horizonMid + dir * off;
            float halfLen = deg % 2 == 0 ? 26f : 16f;
            if ((lineMid - c).Length() > Radius - 6f) continue;
            DrawLine(lineMid - rightv * halfLen, lineMid + rightv * halfLen, LadderCol, 1.1f, true);
        }
    }

    private void DrawReticle(Vector2 c)
    {
        DrawLine(c + new Vector2(-22, 0), c + new Vector2(-8, 0), Reticle, 2.2f, true);
        DrawLine(c + new Vector2(8, 0),   c + new Vector2(22, 0), Reticle, 2.2f, true);
        DrawLine(c + new Vector2(0, -8),  c + new Vector2(0, -2), Reticle, 2.2f, true);
        DrawCircle(c, 2.4f, Reticle);
    }

    private void DrawRollPointer(Vector2 c, float roll)
    {
        // Bank-angle scale arc at the top with a fixed pointer triangle.
        for (int deg = -60; deg <= 60; deg += 30)
        {
            float a = -Mathf.Pi / 2f + deg * Mathf.Pi / 180f;
            var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Radius;
            DrawCircle(p, 1.6f, LabelDim);
        }
        // The moving roll indicator triangle (rotates with the ball "up").
        var top = c + new Vector2(Mathf.Sin(roll), -Mathf.Cos(roll)) * Radius;
        var tl  = top + new Vector2(Mathf.Cos(roll - Mathf.Pi / 2), Mathf.Sin(roll - Mathf.Pi / 2)) * 5f;
        var tr  = top + new Vector2(Mathf.Cos(roll + Mathf.Pi / 2), Mathf.Sin(roll + Mathf.Pi / 2)) * 5f;
        var apex = top - new Vector2(Mathf.Sin(roll), -Mathf.Cos(roll)) * 9f;
        DrawColoredPolygon(new[] { tl, tr, apex }, Reticle);
    }

    private void DrawProgradeMarker(Vector2 p, Color col, bool prograde)
    {
        DrawArc(p, 6f, 0, Mathf.Tau, 16, col, 1.6f, true);
        DrawLine(p + new Vector2(-9, 0), p + new Vector2(-6, 0), col, 1.6f, true);
        DrawLine(p + new Vector2(6, 0),  p + new Vector2(9, 0),  col, 1.6f, true);
        DrawLine(p + new Vector2(0, -9), p + new Vector2(0, -6), col, 1.6f, true);
        DrawCircle(p, 1.4f, col);
        if (!prograde)
            DrawLine(p + new Vector2(0, 6), p + new Vector2(0, 9), col, 1.6f, true);
    }

    private void DrawRadialMarker(Vector2 p)
    {
        DrawArc(p, 4.5f, 0, Mathf.Tau, 12, RadialCol, 1.4f, true);
        DrawCircle(p, 1.2f, RadialCol);
    }

    private void DrawHeadingTape(Vector2 top)
    {
        float w = Radius * 2f;
        var rect = new Rect2(top.X - w / 2f, top.Y, w, 16f);
        DrawRect(rect, new Color(0.05f, 0.07f, 0.10f, 0.7f), true);
        DrawRect(rect, PanelBorder, false, 1f);

        // Tick every 30°, centred on current heading.
        float pxPerDeg = w / 120f;     // show ±60° window
        for (int d = -60; d <= 60; d += 30)
        {
            double tickHdg = _headingDeg + d;
            float x = top.X + d * pxPerDeg;
            string lbl = CompassLabel(((tickHdg % 360) + 360) % 360);
            var sz = _font.GetStringSize(lbl, HorizontalAlignment.Center, -1, 11);
            DrawString(_font, new Vector2(x - sz.X / 2f, top.Y + 12f), lbl,
                HorizontalAlignment.Left, -1, 11, LabelDim);
        }
        // Centre pointer.
        DrawLine(new Vector2(top.X, top.Y), new Vector2(top.X, top.Y + 16f), Reticle, 1.4f, true);
    }

    private void DrawAttitudeText()
    {
        string line = $"PCH {_pitchDeg,4:F0}°   HDG {_headingDeg,3:F0}°   RLL {_rollDeg,4:F0}°";
        var sz = _font.GetStringSize(line, HorizontalAlignment.Center, -1, 11);
        DrawString(_font, new Vector2(Size.X / 2f - sz.X / 2f, Size.Y - 4f), line,
            HorizontalAlignment.Left, -1, 11, ValueBright);
    }

    // Markers are computed in the un-rolled disc frame; rotate them with the ball.
    private static Vector2 RotateOffset(Vector2 v, float roll)
    {
        float cs = Mathf.Cos(roll), sn = Mathf.Sin(roll);
        return new Vector2(v.X * cs - v.Y * sn, v.X * sn + v.Y * cs);
    }

    private static string CompassLabel(double hdg)
    {
        int h = (int)System.Math.Round(hdg / 30.0) * 30 % 360;
        return h switch
        {
            0   => "N", 90  => "E", 180 => "S", 270 => "W",
            _   => $"{h:000}",
        };
    }

    private static Vector3d AnyPerpendicular(Vector3d v)
    {
        var t = System.Math.Abs(v.X) < 0.9 ? new Vector3d(1, 0, 0) : new Vector3d(0, 0, 1);
        return v.Cross(t).Normalized;
    }

    private const double Rad2Deg = 180.0 / System.Math.PI;
}
