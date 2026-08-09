namespace Exosphere.Simulation;

public partial class AtmosphereModel
{
    /// <summary>
    /// Deserialises an AtmosphereModel from a <see cref="System.Text.Json.JsonElement"/>
    /// obtained from a celestial-body JSON file.
    /// </summary>
    public static AtmosphereModel FromJson(System.Text.Json.JsonElement json)
    {
        var layers = new List<AtmosphereLayer>();
        if (json.TryGetProperty("layers", out var layersJson) &&
            layersJson.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var layer in layersJson.EnumerateArray())
            {
                layers.Add(new AtmosphereLayer(
                    AltMin:    layer.TryGetProperty("alt_min",    out var amin) ? amin.GetDouble() : 0.0,
                    AltMax:    layer.TryGetProperty("alt_max",    out var amax) ? amax.GetDouble() : 0.0,
                    TempBase:  layer.TryGetProperty("temp_base",  out var tb)   ? tb.GetDouble()   : 288.15,
                    LapseRate: layer.TryGetProperty("lapse_rate", out var lr)   ? lr.GetDouble()   : 0.0));
            }
            // Hydrostatic walk-up assumes ascending altitude order.
            layers.Sort((a, b) => a.AltMin.CompareTo(b.AltMin));
        }

        var optics = new AtmosphereOptics();
        if (json.TryGetProperty("optics", out var opticalJson))
        {
            optics = new AtmosphereOptics
            {
                RayleighScattering = ReadVector(opticalJson, "rayleigh_scattering"),
                MieScattering = ReadVector(opticalJson, "mie_scattering"),
                MieAbsorption = ReadVector(opticalJson, "mie_absorption"),
                OzoneAbsorption = ReadVector(opticalJson, "ozone_absorption"),
                RayleighScaleHeight = ReadDouble(opticalJson, "rayleigh_scale_height", 8_000.0),
                MieScaleHeight = ReadDouble(opticalJson, "mie_scale_height", 1_200.0),
                OzoneCenterAltitude = ReadDouble(opticalJson, "ozone_center_altitude", 25_000.0),
                OzoneHalfWidth = ReadDouble(opticalJson, "ozone_half_width", 15_000.0),
                AirglowEmission = ReadVector(opticalJson, "airglow_emission"),
                AirglowCenterAltitude = ReadDouble(opticalJson, "airglow_center_altitude", 97_000.0),
                AirglowScaleHeight = ReadDouble(opticalJson, "airglow_scale_height", 6_000.0),
                MieAnisotropy = ReadDouble(opticalJson, "mie_anisotropy", 0.80),
                SunIlluminanceScale = ReadDouble(opticalJson, "sun_illuminance_scale", 20.0),
                SurfaceRefractivity = ReadDouble(opticalJson, "surface_refractivity", 2.77e-4),
                RefractiveScaleHeight = ReadDouble(opticalJson, "refractive_scale_height", 8_000.0),
                LowOrderDiffuseStrength = ReadDouble(opticalJson, "low_order_diffuse_strength", 0.25),
                CloudBaseAltitude = ReadDouble(opticalJson, "cloud_base_altitude", 0.0),
                CloudTopAltitude = ReadDouble(opticalJson, "cloud_top_altitude", 0.0),
                CloudExtinction = ReadDouble(opticalJson, "cloud_extinction", 0.0),
                CloudCoverage = ReadDouble(opticalJson, "cloud_coverage", 0.0),
                CloudWindRadiansPerSecond = ReadDouble(opticalJson, "cloud_wind_radians_per_second", 0.0),
            };
        }

        AerosolClimateState? aerosolClimate = null;
        if (json.TryGetProperty("aerosol_climate", out var aerosolJson)
            && aerosolJson.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            aerosolClimate = new AerosolClimateState
            {
                Aod550 = ReadDouble(aerosolJson, "aod550", 0.08),
                AngstromExponent = ReadDouble(aerosolJson, "angstrom_exponent", 1.0),
                DustFraction = ReadDouble(aerosolJson, "dust_fraction", 0.60),
                MistFraction = ReadDouble(aerosolJson, "mist_fraction", 0.40),
                LatitudeModulation = ReadDouble(aerosolJson, "latitude_modulation", 0.30),
                LatitudePeakDegrees = ReadDouble(aerosolJson, "latitude_peak_degrees", 25.0),
                LatitudeWidthDegrees = ReadDouble(aerosolJson, "latitude_width_degrees", 22.0),
                AltitudeScaleHeightMeters = ReadDouble(aerosolJson, "altitude_scale_height_m", 1_500.0),
                TemporalModulation = ReadDouble(aerosolJson, "temporal_modulation", 0.12),
                TemporalPeriodSeconds = ReadDouble(aerosolJson, "temporal_period_s", 86_400.0),
                TemporalPhaseSeconds = ReadDouble(aerosolJson, "temporal_phase_s", 0.0),
                SeasonalModulation = ReadDouble(aerosolJson, "seasonal_modulation", 0.10),
                SeasonalPeriodSeconds = ReadDouble(aerosolJson, "seasonal_period_s", 31_557_600.0),
                SeasonalPhaseSeconds = ReadDouble(aerosolJson, "seasonal_phase_s", 0.0),
            }.Normalize();
        }

        return new AtmosphereModel
        {
            Optics             = optics,
            AerosolClimate     = aerosolClimate,
            ScaleHeight         = json.TryGetProperty("scale_height",          out var sh)  ? sh.GetDouble()  : 8500.0,
            SeaLevelDensity     = json.TryGetProperty("sea_level_density",     out var sld) ? sld.GetDouble() : 1.225,
            SeaLevelPressure    = json.TryGetProperty("sea_level_pressure",    out var slp) ? slp.GetDouble() : 101_325.0,
            SeaLevelTemperature = json.TryGetProperty("sea_level_temperature", out var slt) ? slt.GetDouble() : 288.15,
            MaxAltitude         = json.TryGetProperty("max_altitude",          out var ma)  ? ma.GetDouble()  : 140_000.0,
            MolarMass           = json.TryGetProperty("molar_mass",            out var mm)  ? mm.GetDouble()  : 0.0289644,
            SurfaceGravity      = json.TryGetProperty("surface_gravity",       out var sg)  ? sg.GetDouble()  : 9.80665,
            GeopotentialRadius  = json.TryGetProperty("geopotential_radius",   out var gr)  ? gr.GetDouble()  : 6_356_766.0,
            ThermosphereScaleHeight = json.TryGetProperty("thermosphere_scale_height", out var tsh) ? tsh.GetDouble() : 0.0,
            ThermosphereScaleHeightGrowth = json.TryGetProperty("thermosphere_scale_height_growth", out var tg) ? tg.GetDouble() : 0.0,
            ThermosphereTopAltitude = json.TryGetProperty("thermosphere_top_altitude", out var tta) ? tta.GetDouble() : 0.0,
            Layers              = layers,
        };
    }

    private static Math.Vector3d ReadVector(
        System.Text.Json.JsonElement json, string name)
    {
        if (!json.TryGetProperty(name, out var value)
            || value.ValueKind != System.Text.Json.JsonValueKind.Array)
            return Math.Vector3d.Zero;
        var values = value.EnumerateArray().Select(v => v.GetDouble()).ToArray();
        return values.Length == 3
            ? new Math.Vector3d(values[0], values[1], values[2])
            : Math.Vector3d.Zero;
    }

    private static double ReadDouble(
        System.Text.Json.JsonElement json, string name, double fallback) =>
        json.TryGetProperty(name, out var value) ? value.GetDouble() : fallback;
}
