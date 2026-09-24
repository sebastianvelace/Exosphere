namespace ExosphereSimulation.Tests;

using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

/// <summary>Source contracts only; GPU compilation and appearance need a real capture.</summary>
public sealed class EarthSurfaceShaderContractTests
{
    private static string Source(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Exosphere.csproj")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        string source = File.ReadAllText(Path.Combine(directory!.FullName, relativePath));
        return Regex.Replace(source, @"//[^\r\n]*|/\*[\s\S]*?\*/", "");
    }

    [Fact]
    public void SurfaceRadianceKeepsCameraDependentScatteringBoundedToTheGlobeLimb()
    {
        string shader = Source("assets/shaders/earth_surface.gdshader");
        int cameraPathStart = shader.IndexOf("vec3 V =", StringComparison.Ordinal);
        Assert.True(cameraPathStart > shader.IndexOf("lit += cities;", StringComparison.Ordinal));
        string surfaceRadiance = shader[..cameraPathStart];
        // Direct surface lighting remains independent of the camera. The scaled
        // globe gets a separate, bounded camera-to-surface path at the limb.
        Assert.DoesNotMatch(@"\b(CAMERA_POSITION_WORLD|VIEW|ndotv|limb|FRAGCOORD|SCREEN_UV)\b", surfaceRadiance);
        Assert.Matches(@"EMISSION\s*=\s*cities\s*\*\s*0\.6\s*;", shader);
        Assert.Contains("float view_air_mass = 1.0 / max(", shader);
        Assert.Contains("vec3 view_transmittance = exp(-vertical_optical_depth", shader);
        Assert.Contains("float limb_scatter = smoothstep(0.0, 0.42", shader);
        Assert.Contains("lit = mix(lit, aerial_radiance, limb_scatter);", shader);
    }

    [Fact]
    public void SilhouetteCoverageUsesPixelFootprintAtTangentNotAnInteriorAngularBand()
    {
        string shader = Source("assets/shaders/earth_surface.gdshader");
        Assert.Contains("render_mode cull_back, unshaded, blend_mix;", shader);
        Assert.Contains("float ndotv = dot(N, V);", shader);
        Assert.Contains("vec3 V = normalize(CAMERA_POSITION_WORLD - v_world_pos);", shader);
        Assert.Contains("float limb = 1.0 - ndotv;", shader);
        // The floor is a numerical guard, not a visible angular width.
        Assert.Contains("float limb_aa = max(fwidth(limb), 0.000001);", shader);
        Assert.Contains("float silhouette = smoothstep(0.0, limb_aa, ndotv);", shader);
        Assert.Contains("ALPHA = planet_alpha * silhouette;", shader);
        Assert.Single(Regex.Matches(shader, @"\bALPHA\s*="));
    }

    [Fact]
    public void SurfaceKeepsSpectralSunAttenuationEclipseAndNightLights()
    {
        string shader = Source("assets/shaders/earth_surface.gdshader");
        Assert.Contains("exp(-vertical_optical_depth * min(air_mass, 40.0))", shader);
        Assert.Contains("direct_transmittance * solar_visibility", shader);
        Assert.Contains("float water_fresnel = 0.0204 + 0.9796 * pow(1.0 - view_cosine, 5.0);", shader);
        Assert.Contains("vec3 ocean_sun_reflection = vec3(water_fresnel * sun_glint)", shader);
        Assert.Contains("nightCol * smoothstep(0.07, 0.18, lum) * night * night_lights", shader);
        Assert.Contains("lit += cities;", shader);
    }

    [Fact]
    public void EarthMaterialBindsDeclaredUniformsWithoutAnAtmosphericGlowControl()
    {
        string shader = Source("assets/shaders/earth_surface.gdshader");
        string materials = Source("scripts/PlanetMaterials.cs");
        int start = materials.IndexOf("public static Material CreateEarth()", StringComparison.Ordinal);
        int end = materials.IndexOf("return mat;", start, StringComparison.Ordinal);
        string earth = materials[start..end];
        var bindings = Regex.Matches(earth, "SetShaderParameter\\(\"([^\"]+)\"");
        Assert.NotEmpty(bindings);
        foreach (Match binding in bindings)
            Assert.Matches(@"\buniform\s+\w+\s+" + Regex.Escape(binding.Groups[1].Value) + @"\b", shader);
        Assert.DoesNotContain("limb_strength", shader);
        Assert.DoesNotContain("limb_strength", earth);
        Assert.Contains("AtmosphereModel.Earth().Optics.VerticalOpticalDepth(0.0)", earth);
    }

    [Fact]
    public void EarthSunDirRetriesWhenTheMeshMaterialIsCreatedLate()
    {
        string sun = Source("scripts/SunController.cs");
        Assert.Contains("sunDirectionChanged || materialsNeedRefresh || _earthMat == null", sun);
        Assert.Contains("!IsInstanceValid(_earthMat)", sun);
        Assert.Contains("_earthMat?.SetShaderParameter(\"sun_dir\", sunDir);", sun);
        Assert.DoesNotContain("ToEarthSurfaceSunDirection", sun);
    }
}
