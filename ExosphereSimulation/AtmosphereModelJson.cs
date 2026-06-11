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

        return new AtmosphereModel
        {
            ScaleHeight         = json.TryGetProperty("scale_height",          out var sh)  ? sh.GetDouble()  : 8500.0,
            SeaLevelDensity     = json.TryGetProperty("sea_level_density",     out var sld) ? sld.GetDouble() : 1.225,
            SeaLevelPressure    = json.TryGetProperty("sea_level_pressure",    out var slp) ? slp.GetDouble() : 101_325.0,
            SeaLevelTemperature = json.TryGetProperty("sea_level_temperature", out var slt) ? slt.GetDouble() : 288.15,
            MaxAltitude         = json.TryGetProperty("max_altitude",          out var ma)  ? ma.GetDouble()  : 140_000.0,
            MolarMass           = json.TryGetProperty("molar_mass",            out var mm)  ? mm.GetDouble()  : 0.0289644,
            Layers              = layers,
        };
    }
}
