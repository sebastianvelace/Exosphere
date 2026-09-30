# Ascent surface and atmosphere compositing audit — 2026-09-30

Status: **open visual defects reproduced; correction not implemented in this audit**.
Baseline: `a5ac873` on `main`, after eight ordered launch/entry/navigation commits.
Scope: the four user screenshots, production Earth rendering and real ascent appearance.
No aerodynamics, atmospheric density, trajectory, propulsion or heating laws were changed.

## What the supplied images establish

| Image | Visible defect | Interpretation boundary |
|---|---|---|
| 1 | A gray field replaces most of the coastal context around the mapped patch. Straight mosaic lines are also visible in the imagery. | The coastline, dunes, river bend and industrial campus match `starbase_naip_10km.jpg`, rotated in the chase view. This is Starbase/Boca Chica, not Kennedy/Cape Canaveral. |
| 2 | Sky/cloud-like color covers the hull while the luminous exhaust remains visible. | This does not demonstrate hull transparency or a real intervening cloud. Depth ordering must be checked. |
| 3 | A broad cyan ring has a sharp outer silhouette and nearly uniform brightness. The ocean and clouds lose contrast. | A spherical atmosphere can have a visible curved limb, but density and radiance must fade continuously toward space. |
| 4 | The same ring persists in the wider view and much of the illuminated Earth is pale blue. | `AP 35.0 Mm` describes apoapsis, not the current camera height. It cannot date this frame or calibrate the limb thickness. |

`HUDController.cs:901–912` binds AP/PE to apoapsis/periapsis altitude. The 17 km,
51 km and 238 km AP values in the other screenshots also are **not measured flight
or camera altitudes**. No timestamp, camera FOV or weather metadata accompanied them.

Starbase's source manifest is `data/launch_sites/starbase_terrain.json`, centered at
25.9972° N, 97.1566° W, with USDA NAIP imagery. Kennedy has its own manifest and assets;
changing sites is not a solution for the gray compositing defect. The NAIP texture
itself contains mosaic striping and an unfilled rectangular region: coverage masks
and imagery seams also need attention, independently of atmospheric shading.

## Real visual reference and ascent bands

Primary references inspected on 2026-09-30:

- [NASA Glenn: Earth's atmosphere](https://www.grc.nasa.gov/WWW/K-12/airplane/atmosphere):
  density falls with height; its orbital photograph shows the atmosphere concentrated
  near the limb rather than a uniform opaque bubble.
- [NASA: The Top of the Atmosphere](https://science.nasa.gov/earth/earth-observatory/the-top-of-the-atmosphere-7373/):
  astronaut image ISS013-E-54329 shows a blue-to-dark gradient above clouds. Its 400 mm
  lens, crop and contrast enhancement make it a qualitative reference, not a pixel-width
  target for an unmatched chase camera. The atmosphere has no abrupt optical boundary.
- [NASA: Earth's Atmosphere, a Multi-layered Cake](https://science.nasa.gov/earth/earth-atmosphere/earths-atmosphere-a-multi-layered-cake/):
  most weather/clouds occur in the troposphere; its typical height is about 12 km and
  varies geographically. The stratosphere extends approximately 12–50 km and is largely
  weather-free, with exceptions. Clouds below an ascending vehicle remain visible.

The following are **camera-altitude test bands**, not Flight 14 event times or prescribed
sky-color thresholds. Sun elevation, ray direction, cloud coverage, exposure and FOV
must be matched before comparing captures.

| Camera height | Expected visual behavior | Current behavior to challenge |
|---|---|---|
| Pad–2 km | Coastal terrain and structures retain contrast; distance haze follows the viewing path. Actual exhaust obscures only the volume it occupies. | Gray surroundings, overly distant fog and incomplete regional context. |
| 2–12 km | Ground/cloud features stay geometrically continuous while their detail decreases with distance. Weather can obscure them locally. | Imagery radius and RGB rim blend make a circular island surrounded by a haze-colored field. |
| 12–20 km | Terrain LOD changes preserve the visible surface; they should not create a new optical layer. | Ground is replaced with haze RGB during the camera-height handoff; the compressed globe can cross in front of the hull. |
| 20–50 km | Ordinary weather is predominantly below the observer; upward sky radiance decreases with the remaining optical column. | Background Earth/cloud colors can cover foreground geometry; depth fog uses render distances and vessel density. |
| 50–100 km | Curvature and direction-dependent limb scattering become prominent; the upper sky darkens continuously. | Fixed shell membership changes around 102 km and introduces a geometric outer edge. |
| 150–400 km | Space above the limb is dark; the lit atmospheric gradient, surface and cloud deck remain distinct. | Broad cyan shell and surface in-scatter flatten Earth's contrast. |

The supplied Flight 14 references remain the launch-art target. NASA orbital imagery
supports the atmospheric appearance, but is not a claim of Flight 14-specific weather,
exposure, trajectory or camera timing. The SpaceX Flight 14 URL did not expose usable
flight metadata in this audit; no flight-specific numbers were inferred from it.

## Confirmed code causes

### P1 — Local-to-global terrain handoff fades into a color, not another surface

`FloatingOrigin.cs:91–100` fades the globe between **12 and 18 km camera altitude**.
`EarthGroundController.cs:252` computes the complementary weight. However,
`earth_ground.gdshader:465–469` uses that weight in `mix(haze_color, ground_radiance,
fade * rim)` and outputs `ALPHA = 1.0`. As the weight falls, the local geometry remains
fully covering in its blend and becomes haze-colored instead of revealing a continuous
terrain representation. The radial `rim` does the same at its geometric boundary.

This is distinct from physical extinction. Raising ground brightness or changing the
haze hue would retain the discontinuity. Also, writing `ALPHA`, even 1.0, sends the
material through Godot's transparent pipeline, despite the opaque intent in comments.
The ordering consequences need to be addressed along with the surface LOD design.

### P1 — Compressed Earth depth can put the globe ahead of the vessel

`FloatingOrigin.cs:20, 236–257` puts the globe center **50,000 render units** from the
camera, with radius `50,000 * R / d`. This preserves its angular size, but not its
foreground depth. For a radial camera height h, the nearest globe point is:

`near = 50,000 * h / (R + h)`

At h = 20 km and R = 6,371 km, near is only **156.5 render units**. A ray pitched
28° below horizontal intersects it at approximately **335.2 units**, while the
external camera can be 500 units from the ship. Thus, a physically distant Earth can
be drawn ahead of a nearby hull. Render priority alone cannot repair this geometry.

The globe shader uses alpha blending and depth testing in the foreground viewport;
there is no isolated backdrop depth pass. `CameraController` permits zoom distances
well beyond this intersection. This is a presentation defect; changing the trajectory
or making the ship emissive would hide the cause rather than correct it.

**Controlled framebuffer evidence:** the paused 20 km fixture loses the hull with
the production globe visible. Hiding only the globe mesh layers restores the hull at
unchanged state and camera. Disabling scene fog after hiding the globe produces a much
smaller change. This establishes backdrop occlusion as the dominant cause in this fixture;
it does not prove that every cloud-like pixel in the user screenshots has that cause.

Capture-time telemetry confirms the same geometry and camera height in all three PNGs:

| Case | Camera altitude (m) | Camera-to-vessel origin (render units) | Ray/globe hit (render units) | Globe layers | Scene fog | Visible hull |
|---|---:|---:|---:|---:|---|---|
| Production | 20,715.7 | 510.6 | 323.6 | 1 | enabled, intensity 0.34 | no |
| Globe hidden | 20,715.7 | 510.6 | 323.6 | 0 | enabled, intensity 0.34 | yes |
| Globe hidden, fog off | 20,715.7 | 510.6 | 323.6 | 0 | disabled | yes |

Globe center distance is 50,000.0 and radius is 49,838.0 render units. These compressed
render depths are not physical camera-to-ground distances. Comparing the last two
images gives a mean absolute RGB difference of approximately 2.52/255; the hull stays
visible in both. `AUDIT_SETTLED` records the geometry at capture time, after framing
has converged; the earlier `AUDIT_COMPOSITING` setup lines can contain pre-settle data.

### P1 — Fixed atmosphere shell produces an optical boundary

`SimulationBridge.cs:818–830` adds an Earth shell at **1.016 times globe radius**.
`PlanetMaterials.cs:68–83` gives it sea-level vertical optical depth and a constant
`planet_alpha = 1`. `earth_surface.gdshader:211–230` derives shell color/opacity from
a surface-normal/view cosine. It does not integrate altitude-dependent density along
a camera ray, account for its actual tangent height, or taper density at the outer edge.

The shell spans roughly 102 km but treats its geometrical silhouette as an optical
edge. With back-face culling, the camera is inside this shell below that altitude and
outside it above: this introduces another representation transition unrelated to the
continuous atmospheric column. Parent visibility gates the shell, but the shell's
own alpha does not share the parent's 12–18 km fade weight.

### P2 — Three aerial-perspective paths have inconsistent endpoints

- `earth_ground.gdshader` mixes a distance/rim haze proxy into local terrain.
- `earth_surface.gdshader:250–264` uses sea-level vertical optical depth multiplied by
  a view-angle air-mass proxy, with a broad `smoothstep(0, 0.42, 1 − N·V)` blend.
- `PhaseLightingController.cs:303–365` adds Godot **Depth** fog, from 55 to 3,200 render
  units, maximum intensity 0.34 and sky-color contribution 0.78. Its presence uses
  the vessel's local optical density, rather than the camera-to-fragment column.

The procedural sky supplies another atmosphere representation. Surface in-scatter is
needed where the opaque planet hides the sky; the problem is inconsistent ray segments
and scales, not that all atmosphere terms should simply be removed. The scene fog also
sees compressed planet distances rather than physical camera-to-ground distances. Its
special wider-camera adjustment applies only below 1.2 km vessel altitude.

[Godot's Environment documentation](https://docs.godotengine.org/en/stable/classes/class_environment.html#enum-environment-fogmode)
explicitly describes Depth fog as an artistic start/end curve. In this mode, 0.34 is
maximum fog intensity, **not an extinction coefficient in inverse metres**.
[Godot's spatial shader documentation](https://docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/spatial_shader.html#fragment-built-ins)
confirms that writing ALPHA invokes the transparent pipeline and can introduce sorting
issues. These semantics were checked before interpreting the shader code.

## Reproduction and acceptance boundaries

Full `tools/ci_check.sh` passed before the eight code commits: **902 xUnit tests**,
zero build warnings/errors, contract checks and Godot startup/smoke checks.
That verifies regressions; it does not certify photographic atmosphere fidelity.

A real OpenGL Compatibility/Xvfb matrix completed with six PNGs and
`SUMMARY reason=STARBASE_FAR_OK frames=345` at 640×360:

```bash
GODOT_BIN=/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64 \
  bash tools/visual_playtest.sh --starbase-far --run-id ascent-audit-small \
  --resolution 640x360 --skip-build --max-runtime 300
```

The existing first four far-field cases aim the camera at the ground: camera altitude
is about **716 m**, even when vessel altitude is 2, 5, 8 or 12 km. They validate mapped
context, **not an observer climbing through those atmospheric heights**. The 20 and
40 km cases track the vessel, with measured camera altitudes about 20,716 and 40,716 m.
The 20 km image has no visible hull. These are paused presentation fixtures with
zero atmosphere-relative speed, not ascent or orbital-dynamics acceptance.

A separate temporary diagnostic holds the same 20 km vessel state, view, illumination
and clock, then hides globe layers and finally disables scene fog. It does not change
production rendering or simulation code. Its setup, PNGs and telemetry are retained
locally in `exports/ascent-compositing-review/`. The standard far-field verifier exits nonzero for that deliberately shortened
three-case matrix because required six-case milestones are absent. Its emitted
`STARBASE_FAR_OK` is merely the helper's end-of-array event, not standard acceptance.
The first attempt to add per-PNG metadata had a temporary C# local-name collision;
it was cleaned up and corrected before rerunning. No production source was affected.

An initial 1920×1080 run was stopped because software rendering was unnecessarily slow;
its preserved partial evidence is not a success result. Resolution reduction supports
qualitative occlusion diagnosis, not final pixel-level visual acceptance.

## Recommended implementation sequence

1. **Separate distant-background depth from nearby geometry.** Keep the existing
   physical geodesy and angular radius, but composite scaled planets behind the local
   scene with correct mutual planetary occlusion. A backdrop viewport/depth partition
   is more explicit but has camera, exposure and performance costs; a far-depth shader
   must preserve planet ordering and transparent surface semantics. Verify hull and
   plume occlusion together over the full allowed camera zoom range.
2. **Use one continuous surface through the regional-to-global LOD transition.**
   Blend NAIP/Blue Marble coverage/albedo in a shared geographic representation or
   use complementary coverage with correct depths. Preserve terrain outside mapped
   coverage and apply optical extinction after the surface blend. Mask imagery gaps
   and reduce mosaic seam contrast without inventing launch-site geography.
3. **Replace the fixed cyan shell with a finite spherical optical path.** Reuse the
   existing RGB optical parameters/LUTs, density falloff and sun geometry, terminate
   rays at the surface, and integrate the off-limb atmospheric segment. Fade the outer
   column continuously and avoid applying the same path twice over Earth pixels.
4. **Resolve scene fog and terrain/planet in-scatter against the same physical path.**
   Local aerosols or exhaust can remain separate bounded volumes. Avoid choosing sky
   color from a mission phase or tuning orbital height to make a ring thinner.
5. **Validate actual camera heights and geometry.** Capture nadir and limb views at
   0, 5, 12, 15, 18, 20, 40, 80, 100, 110, 250 and 400 km, with FOV, sun direction,
   exposure, renderer and camera/vessel altitude in telemetry. Check overlap boundaries
   at multiple zoom distances. Then run a continuous rendered ascent and reentry; a
   seeded visual matrix cannot replace that E2E check.

No new visual defect is marked fixed by this report. The next acceptance target is
continuous coastal visibility, an unobscured foreground hull outside real intervening
volumes, and an atmospheric gradient without a hard blue shell edge in both renderers.
