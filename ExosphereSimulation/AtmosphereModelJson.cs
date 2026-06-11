namespace Exosphere.Simulation;

public partial class AtmosphereModel
{
    /// <summary>
    /// Deserialises an AtmosphereModel from a <see cref="System.Text.Json.JsonElement"/>
    /// obtained from a celestial-body JSON file.
    /// </summary>
    public static AtmosphereModel FromJson(System.Text.Json.JsonElement json)
    {
        return new AtmosphereModel
        {
            ScaleHeight      = json.TryGetProperty("scale_height",      out var sh)  ? sh.GetDouble()  : 8500.0,
            SeaLevelDensity  = json.TryGetProperty("sea_level_density", out var sld) ? sld.GetDouble() : 1.225,
            SeaLevelPressure = json.TryGetProperty("sea_level_pressure",out var slp) ? slp.GetDouble() : 101_325.0,
            MaxAltitude      = json.TryGetProperty("max_altitude",      out var ma)  ? ma.GetDouble()  : 140_000.0,
            MolarMass        = json.TryGetProperty("molar_mass",        out var mm)  ? mm.GetDouble()  : 0.0289644,
        };
    }
}
