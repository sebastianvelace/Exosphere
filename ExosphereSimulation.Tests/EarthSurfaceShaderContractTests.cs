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
    public void SurfaceRadianceCannotPaintCameraCenteredAtmosphericContours()
    {
        string shader = Source("assets/shaders/earth_surface.gdshader");
        int coverageStart = shader.IndexOf("vec3 V =", StringComparison.Ordinal);
        Assert.True(coverageStart > shader.IndexOf("ALBEDO = lit;", StringComparison.Ordinal));
        string radiance = shader[..coverageStart];
        // Camera-dependent color is not a surface-lighting input. This rejects the
        // old rings AND replacing them with another Fresnel/view-angle haze term.
        Assert.DoesNotMatch(@"\b(CAMERA_POSITION_WORLD|VIEW|ndotv|limb|FRAGCOORD|SCREEN_UV)\b", radiance);
        Assert.Matches(@"EMISSION\s*=\s*cities\s*\*\s*0\.6\s*;", radiance);
        Assert.DoesNotMatch(@"\b(ALBEDO|EMISSION|lit)\s*[+*/-]?=", shader[coverageStart..]);
    }

    [Fact]
    public void SilhouetteCoverageUsesPixelFootprintAtTangentNotAnInteriorAngularBand()
    {
        string shader = Source("assets/shaders/earth_surface.gdshader");
        Assert.Contains("render_mode cull_back, unshaded, blend_mix;", shader);
        Assert.Contains("float ndotv = dot(N, V);", shader);
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
}
