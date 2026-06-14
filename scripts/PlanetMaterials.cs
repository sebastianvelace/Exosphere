namespace Exosphere.Game;

using Godot;

/// <summary>
/// Factory for photorealistic, scale-independent planet materials.
///
/// Earth uses a dedicated procedural shader (<c>earth_surface.gdshader</c>) with
/// continents, clouds, ice caps, a day/night terminator and a blue atmospheric
/// rim. Other bodies use a shared generic body shader
/// (<c>planet_body.gdshader</c>) or a tuned <see cref="StandardMaterial3D"/>.
///
/// All shaders sample detail from the *normalized* local vertex position, so the
/// host <see cref="SphereMesh"/> may be scaled to any radius without altering the
/// surface appearance. The default sun direction matches the Earth shader so the
/// terminators across bodies stay consistent.
/// </summary>
public static class PlanetMaterials
{
    /// <summary>Default sun direction (world space) shared by all body shaders.</summary>
    public static readonly Vector3 DefaultSunDir = new Vector3(0.4f, 0.5f, 0.8f).Normalized();

    private const string EarthShaderPath = "res://assets/shaders/earth_surface.gdshader";
    private const string BodyShaderPath  = "res://assets/shaders/planet_body.gdshader";

    /// <summary>
    /// Photorealistic Earth: procedural oceans/continents, animated clouds, polar
    /// ice, a day/night terminator with faint city lights, and a glowing blue limb.
    /// </summary>
    public static Material CreateEarth()
    {
        var shader = GD.Load<Shader>(EarthShaderPath);
        var mat = new ShaderMaterial { Shader = shader };

        // Sensible defaults so it looks great with zero tuning. Values that match
        // the shader's own defaults are set explicitly for clarity/robustness.
        mat.SetShaderParameter("sun_dir", DefaultSunDir);
        mat.SetShaderParameter("ocean_color", new Color(0.015f, 0.09f, 0.27f));
        mat.SetShaderParameter("ocean_shallow", new Color(0.05f, 0.32f, 0.50f));
        mat.SetShaderParameter("land_low", new Color(0.10f, 0.34f, 0.10f));
        mat.SetShaderParameter("land_high", new Color(0.42f, 0.34f, 0.20f));
        mat.SetShaderParameter("ice_color", new Color(0.93f, 0.96f, 1.0f));
        mat.SetShaderParameter("atmosphere_color", new Color(0.30f, 0.55f, 1.0f));
        mat.SetShaderParameter("sea_level", 0.52f);
        mat.SetShaderParameter("continent_scale", 2.2f);
        mat.SetShaderParameter("cloud_scale", 3.0f);
        mat.SetShaderParameter("cloud_coverage", 0.42f);
        mat.SetShaderParameter("cloud_speed", 0.006f);
        mat.SetShaderParameter("ice_latitude", 0.74f);
        mat.SetShaderParameter("ocean_specular", 0.7f);
        mat.SetShaderParameter("atmosphere_strength", 1.1f);
        mat.SetShaderParameter("night_lights", 1.0f);
        return mat;
    }

    /// <summary>
    /// Returns a nicer-than-flat material for a given body. Earth is delegated to
    /// <see cref="CreateEarth"/>; known bodies get tuned procedural looks; any
    /// unknown body falls back to a softly shaded sphere using <paramref name="baseColor"/>.
    /// </summary>
    public static Material CreatePlanet(string bodyId, Color baseColor)
    {
        switch ((bodyId ?? string.Empty).ToLowerInvariant())
        {
            case "earth":
                return CreateEarth();

            case "moon":
                return RockyBody(
                    surface: new Color(0.42f, 0.42f, 0.44f),
                    detail:  new Color(0.20f, 0.20f, 0.22f),
                    detailScale: 5.0f, roughness: 1.0f);

            case "mercury":
                return RockyBody(
                    surface: new Color(0.46f, 0.42f, 0.38f),
                    detail:  new Color(0.26f, 0.23f, 0.20f),
                    detailScale: 5.5f, roughness: 1.0f);

            case "mars":
                return RockyBody(
                    surface: new Color(0.62f, 0.31f, 0.17f),
                    detail:  new Color(0.34f, 0.16f, 0.10f),   // darker maria/lowlands
                    detailScale: 4.0f, roughness: 0.97f,
                    rimColor: new Color(0.80f, 0.45f, 0.30f), rimStrength: 0.35f);

            case "venus":
                return SmoothCloudBody(
                    surface: new Color(0.90f, 0.78f, 0.52f),
                    detail:  new Color(0.78f, 0.62f, 0.38f),
                    rimColor: new Color(0.95f, 0.80f, 0.55f), rimStrength: 0.8f);

            case "jupiter":
                return BandedBody(
                    surface: new Color(0.82f, 0.70f, 0.55f),
                    detail:  new Color(0.55f, 0.38f, 0.26f),
                    bandCount: 5.0f,
                    rimColor: new Color(0.85f, 0.72f, 0.58f), rimStrength: 0.5f);

            case "saturn":
                return BandedBody(
                    surface: new Color(0.86f, 0.78f, 0.58f),
                    detail:  new Color(0.70f, 0.60f, 0.42f),
                    bandCount: 4.0f,
                    rimColor: new Color(0.88f, 0.80f, 0.62f), rimStrength: 0.45f);

            case "sun":
                return SunBody(baseColor);

            default:
                return FallbackBody(baseColor);
        }
    }

    // ── Shared builders ──────────────────────────────────────────────────────

    private static ShaderMaterial BodyMaterial()
    {
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>(BodyShaderPath) };
        mat.SetShaderParameter("sun_dir", DefaultSunDir);
        return mat;
    }

    private static Material RockyBody(Color surface, Color detail, float detailScale,
                                      float roughness, Color? rimColor = null,
                                      float rimStrength = 0.0f)
    {
        var mat = BodyMaterial();
        mat.SetShaderParameter("mode", 0);
        mat.SetShaderParameter("surface_tint", surface);
        mat.SetShaderParameter("detail_tint", detail);
        mat.SetShaderParameter("detail_scale", detailScale);
        mat.SetShaderParameter("roughness_val", roughness);
        mat.SetShaderParameter("rim_color", rimColor ?? new Color(0.4f, 0.5f, 0.7f));
        mat.SetShaderParameter("rim_strength", rimStrength);
        return mat;
    }

    private static Material BandedBody(Color surface, Color detail, float bandCount,
                                       Color rimColor, float rimStrength)
    {
        var mat = BodyMaterial();
        mat.SetShaderParameter("mode", 1);
        mat.SetShaderParameter("surface_tint", surface);
        mat.SetShaderParameter("detail_tint", detail);
        mat.SetShaderParameter("detail_scale", bandCount);
        mat.SetShaderParameter("roughness_val", 0.85f);
        mat.SetShaderParameter("band_warp", 0.08f);
        mat.SetShaderParameter("rim_color", rimColor);
        mat.SetShaderParameter("rim_strength", rimStrength);
        return mat;
    }

    private static Material SmoothCloudBody(Color surface, Color detail,
                                            Color rimColor, float rimStrength)
    {
        var mat = BodyMaterial();
        mat.SetShaderParameter("mode", 2);
        mat.SetShaderParameter("surface_tint", surface);
        mat.SetShaderParameter("detail_tint", detail);
        mat.SetShaderParameter("detail_scale", 3.5f);
        mat.SetShaderParameter("roughness_val", 0.6f);
        mat.SetShaderParameter("rim_color", rimColor);
        mat.SetShaderParameter("rim_strength", rimStrength);
        return mat;
    }

    /// <summary>Fully emissive, bright star surface — unaffected by scene lighting.</summary>
    private static Material SunBody(Color baseColor)
    {
        // A bright color even if the caller passed something dim.
        var core = new Color(
            Mathf.Max(baseColor.R, 1.0f),
            Mathf.Max(baseColor.G, 0.85f),
            Mathf.Max(baseColor.B, 0.45f));

        return new StandardMaterial3D
        {
            AlbedoColor      = core,
            EmissionEnabled  = true,
            Emission         = core,
            EmissionEnergyMultiplier = 6.0f,
            ShadingMode      = BaseMaterial3D.ShadingModeEnum.Unshaded,
            DisableReceiveShadows = true,
        };
    }

    /// <summary>Soft fallback for unknown bodies — better than a flat unlit color.</summary>
    private static Material FallbackBody(Color baseColor)
    {
        var mat = BodyMaterial();
        mat.SetShaderParameter("mode", 0);
        mat.SetShaderParameter("surface_tint", baseColor);
        mat.SetShaderParameter("detail_tint", baseColor.Darkened(0.4f));
        mat.SetShaderParameter("detail_scale", 4.0f);
        mat.SetShaderParameter("roughness_val", 0.9f);
        mat.SetShaderParameter("rim_strength", 0.0f);
        return mat;
    }
}
