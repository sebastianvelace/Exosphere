namespace Exosphere.Simulation.Parts;

using System.Text.Json;
using System.Text.Json.Serialization;

public enum PartCategory
{
    Command, Engine, FuelTank, Structure,
    Electrical, Landing, Decoupler, Fairing, RCS
}

public class AttachmentNodeDef
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    [JsonPropertyName("position")]
    public double[] Position { get; set; } = [0, 0, 0];
    [JsonPropertyName("size")]
    public int Size { get; set; } = 1;
    [JsonPropertyName("type")]
    public string Type { get; set; } = "stack";  // stack | radial | engine_bell
}

public class PartDefinition
{
    [JsonPropertyName("id")]          public string Id          { get; set; } = "";
    [JsonPropertyName("name")]        public string Name        { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("category")]    public string CategoryStr { get; set; } = "structure";
    [JsonPropertyName("mass_dry")]    public double MassDry     { get; set; }
    [JsonPropertyName("cost")]        public double Cost        { get; set; }
    [JsonPropertyName("drag_coefficient")] public double DragCoefficient { get; set; } = 0.2;
    [JsonPropertyName("heat_tolerance")]   public double HeatTolerance   { get; set; } = 1200;
    [JsonPropertyName("attachment_nodes")] public List<AttachmentNodeDef> AttachmentNodes { get; set; } = new();

    // Engine
    [JsonPropertyName("thrust_vac")]   public double ThrustVac   { get; set; }
    [JsonPropertyName("thrust_sl")]    public double ThrustSL    { get; set; }
    [JsonPropertyName("isp_vac")]      public double IspVac      { get; set; }
    [JsonPropertyName("isp_sl")]       public double IspSL       { get; set; }
    [JsonPropertyName("gimbal_range")] public double GimbalRange { get; set; }
    [JsonPropertyName("fuel_type")]    public string FuelTypeStr { get; set; } = "";
    [JsonPropertyName("is_rcs")]       public bool IsRCS         { get; set; }

    // Deepest stable throttle the engine can sustain, as a fraction of rated thrust.
    // Real Raptor 2 deep-throttles to ~40 % (full-flow staged combustion stays lit but the
    // turbopumps cannot run arbitrarily slow). This is informational: it is ENFORCED only when
    // the caller opts in (see Part.GetEffectiveThrottle / PartGraph.ClampAscentThrottle), so a
    // suicide-burn controller commanding a continuous low setpoint is never snapped up to it.
    // 0 means "no documented floor" (e.g. throwaway test engines), and the engine may idle to 0.
    [JsonPropertyName("min_throttle")] public double MinThrottle { get; set; }

    // Tank
    [JsonPropertyName("fuel_capacity_lf")]    public double FuelCapacityLF    { get; set; }
    [JsonPropertyName("fuel_capacity_ox")]    public double FuelCapacityOx    { get; set; }
    [JsonPropertyName("fuel_capacity_solid")] public double FuelCapacitySolid { get; set; }
    [JsonPropertyName("fuel_capacity_mono")]  public double FuelCapacityMono  { get; set; }
    [JsonPropertyName("ec_capacity")]         public double ECCapacity        { get; set; }

    // Crew / landing / parachute
    [JsonPropertyName("max_crew")]       public int    MaxCrew       { get; set; }
    [JsonPropertyName("max_load")]       public double MaxLoad       { get; set; }
    [JsonPropertyName("deployable")]     public bool   Deployable    { get; set; }
    [JsonPropertyName("drag_chute")]     public double DragChute     { get; set; }
    [JsonPropertyName("deploy_altitude")]public double DeployAltitude{ get; set; }

    [JsonIgnore]
    public PartCategory Category => CategoryStr.ToLowerInvariant() switch
    {
        "command"     => PartCategory.Command,
        "engine"      => PartCategory.Engine,
        "fuel_tank"   => PartCategory.FuelTank,
        "structure"   => PartCategory.Structure,
        "electrical"  => PartCategory.Electrical,
        "landing"     => PartCategory.Landing,
        "decoupler"   => PartCategory.Decoupler,
        "fairing"     => PartCategory.Fairing,
        "rcs"         => PartCategory.RCS,
        _             => PartCategory.Structure
    };

    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static PartDefinition LoadFromJson(string jsonPath)
    {
        var text = File.ReadAllText(jsonPath);
        return JsonSerializer.Deserialize<PartDefinition>(text, _opts)
               ?? throw new InvalidOperationException($"Failed to parse part JSON: {jsonPath}");
    }

    public static Dictionary<string, PartDefinition> LoadAllFromDirectory(string dirPath)
    {
        var result = new Dictionary<string, PartDefinition>();
        foreach (var file in Directory.GetFiles(dirPath, "*.json"))
        {
            var def = LoadFromJson(file);
            result[def.Id] = def;
        }
        return result;
    }
}
