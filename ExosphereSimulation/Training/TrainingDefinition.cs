namespace Exosphere.Simulation.Training;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>Where a training drill starts.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TrainingStart>))]
public enum TrainingStart
{
    /// <summary>At the apogee of an elliptical Earth orbit (circularization drill).</summary>
    EllipticalEarthOrbit,
    /// <summary>Behind a target vessel in the same low Earth orbit (docking drill).</summary>
    DockingApproach,
    /// <summary>On a hyperbolic approach to the Moon, hours before perilune (LOI drill).</summary>
    LunarArrival,
    /// <summary>In the 15 x 110 km descent orbit minutes before perilune (landing drill).</summary>
    LunarDescentOrbit,
}

/// <summary>What finishes a training drill.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TrainingGoal>))]
public enum TrainingGoal
{
    CircularOrbit,
    Docked,
    LunarOrbit,
    SoftLanding,
}

/// <summary>
/// A short, isolated drill (KSP 2 training-center style): one vehicle, one seeded situation and
/// one goal, with plain-language instructions naming the keys that do it.
/// </summary>
public sealed class TrainingDefinition
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public string Id { get; set; } = "";
    public int Sequence { get; set; }
    public string TitleEn { get; set; } = "";
    public string TitleEs { get; set; } = "";
    public string InstructionsEn { get; set; } = "";
    public string InstructionsEs { get; set; } = "";
    /// <summary>Player vehicle: a variant under data/vehicles, or a part stack top to bottom.</summary>
    public string VehicleVariantFile { get; set; } = "";
    public List<string> VehicleParts { get; set; } = new();
    /// <summary>Target vehicle variant for docking drills.</summary>
    public string TargetVariantFile { get; set; } = "";
    public TrainingStart Start { get; set; }
    public TrainingGoal Goal { get; set; }

    public static TrainingDefinition LoadFromJson(string path)
    {
        var definition = JsonSerializer.Deserialize<TrainingDefinition>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException($"Training '{path}' is empty.");
        definition.Validate();
        return definition;
    }

    public static IReadOnlyList<TrainingDefinition> LoadAll(string directory) =>
        Directory.GetFiles(directory, "*.json").Select(LoadFromJson).OrderBy(d => d.Sequence).ToList();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(TitleEn) || string.IsNullOrWhiteSpace(TitleEs)
            || string.IsNullOrWhiteSpace(InstructionsEn) || string.IsNullOrWhiteSpace(InstructionsEs))
            throw new InvalidDataException($"Training '{Id}' is incomplete.");
        if (string.IsNullOrWhiteSpace(VehicleVariantFile) == (VehicleParts.Count == 0))
            throw new InvalidDataException($"Training '{Id}' needs exactly one of a vehicle variant or a part stack.");
        if (Start == TrainingStart.DockingApproach != !string.IsNullOrWhiteSpace(TargetVariantFile))
            throw new InvalidDataException($"Training '{Id}': a docking drill, and only it, names a target vehicle.");
    }
}
