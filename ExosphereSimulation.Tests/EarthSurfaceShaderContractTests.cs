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
    public void SurfaceTransportUsesTheFiniteGeographicRayAndSharedProfiles()
    {
        string surface = Source("assets/shaders/earth_surface.gdshader");
        string ground = Source("assets/shaders/earth_ground.gdshader");
        string transport = Source("assets/shaders/surface_atmosphere.gdshaderinc");
        Assert.Contains("surface_aerial_radiance(lit, world_ray, distance_m", surface);
        Assert.Contains("surface_aerial_radiance(ground_radiance, view_ray, physical_distance_m", ground);
        Assert.Contains("float end = min(distance_m, -b + sqrt(disc));", transport);
        Assert.Contains("if (end <= start) return ground;", transport);
        Assert.Contains("ground * exp(-optical_depth)", transport);
        Assert.Contains("surface_density_lut_enabled", transport);
        Assert.Contains("surface_solar_lut_enabled", transport);
        Assert.DoesNotContain("limb_scatter", surface);
    }

    [Fact]
    public void OpaqueEarthLeavesOffLimbTransportToTheSphericalSky()
    {
        string shader = Source("assets/shaders/earth_surface.gdshader");
        Assert.DoesNotMatch(@"\bALPHA\s*=", shader);
        Assert.DoesNotContain("atmosphere_shell", shader);
        Assert.DoesNotContain("Earth_atmosphere", Source("scripts/SimulationBridge.cs"));
        Assert.DoesNotContain("CreateEarthAtmosphere", Source("scripts/PlanetMaterials.cs"));
        Assert.Contains("float distance_m = surface_distance_m(world_ray);", shader);
        Assert.Contains("if (distance_m < 0.0) discard;", shader);
        Assert.Contains("ALPHA     = clamp(fade, 0.0, 1.0) * rim;", Source("assets/shaders/earth_ground.gdshader"));
    }

    [Fact]
    public void GeographicCoverageWritesPhysicalDepthInsteadOfProxyMeshDepth()
    {
        string shader = Source("assets/shaders/earth_surface.gdshader");
        string transport = Source("assets/shaders/earth_geometry.gdshaderinc");
        Assert.Contains("render_mode cull_disabled, unshaded, fog_disabled;", shader);
        Assert.Contains("INV_PROJECTION_MATRIX", shader);
        Assert.Contains("INV_VIEW_MATRIX", shader);
        Assert.Contains("DEPTH = physical_backdrop_depth", shader);
        Assert.Contains("DEPTH = physical_backdrop_depth", Source("assets/shaders/earth_ground.gdshader"));
        Assert.Contains("float c = h * (2.0 + h);", transport);
        Assert.Contains("c / (-b + sqrt(discriminant))", transport);
        Assert.Contains("CLIP_SPACE_FAR", Source("assets/shaders/backdrop_depth.gdshaderinc"));
        Assert.Contains("new QuadMesh", Source("scripts/SimulationBridge.cs"));
        Assert.DoesNotContain("planet_alpha", shader);
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
        string shader = Source("assets/shaders/earth_surface.gdshader")
            + Source("assets/shaders/surface_atmosphere.gdshaderinc")
            + Source("assets/shaders/earth_ortho.gdshaderinc")
            + Source("assets/shaders/earth_geometry.gdshaderinc");
        string materials = Source("scripts/PlanetMaterials.cs");
        int start = materials.IndexOf("public static Material CreateEarth(", StringComparison.Ordinal);
        int end = materials.IndexOf("return mat;", start, StringComparison.Ordinal);
        string earth = materials[start..end];
        var bindings = Regex.Matches(earth, "SetShaderParameter\\(\"([^\"]+)\"");
        Assert.NotEmpty(bindings);
        foreach (Match binding in bindings)
            Assert.Matches(@"\buniform\s+\w+\s+" + Regex.Escape(binding.Groups[1].Value) + @"\b", shader);
        Assert.DoesNotContain("limb_strength", shader);
        Assert.DoesNotContain("limb_strength", earth);
        Assert.Contains("body?.Atmosphere ?? AtmosphereModel.Earth()", earth);
        Assert.Contains("atmosphere.Optics.VerticalOpticalDepth(0.0)", earth);
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
