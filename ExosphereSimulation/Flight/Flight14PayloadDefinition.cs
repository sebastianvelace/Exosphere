namespace Exosphere.Simulation.Flight;

using System.Text.Json;
using Exosphere.Simulation.Math;
using Exosphere.Simulation.Parts;

/// <summary>Source-backed count with explicitly estimated mass, layout and release program.</summary>
public sealed class Flight14PayloadDefinition
{
    public string Id { get; set; } = "";
    public string Status { get; set; } = "";
    public string PartFile { get; set; } = "";
    public int Count { get; set; }
    public string SourceUrl { get; set; } = "";
    public string Assumptions { get; set; } = "";
    public double FirstReleaseMissionSeconds { get; set; }
    public double ReleaseIntervalSeconds { get; set; }
    public double MaximumMissionSeconds { get; set; }
    public double MinimumPeriapsisAltitudeM { get; set; }
    public double MinimumAltitudeM { get; set; }
    public double MaximumReleaseAngularRateRadPerSecond { get; set; }
    public double SettleAfterInsertionSeconds { get; set; }
    public double StowageFirstYM { get; set; }
    public double StowageSpacingM { get; set; }
    public double[] OpeningVelocityLocalMps { get; set; } = [];

    public Vector3d OpeningVelocity => new(OpeningVelocityLocalMps[0],
        OpeningVelocityLocalMps[1], OpeningVelocityLocalMps[2]);

    public static Flight14PayloadDefinition LoadFromJson(string path)
    {
        var result = JsonSerializer.Deserialize<Flight14PayloadDefinition>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Empty Flight 14 payload estimate.");
        result.Validate();
        return result;
    }

    public void Validate()
    {
        double[] positive = [FirstReleaseMissionSeconds, ReleaseIntervalSeconds, MaximumMissionSeconds,
            MinimumPeriapsisAltitudeM, MinimumAltitudeM, MaximumReleaseAngularRateRadPerSecond,
            SettleAfterInsertionSeconds, StowageSpacingM];
        if (string.IsNullOrWhiteSpace(Id) || Status != "engineering-estimate"
            || string.IsNullOrWhiteSpace(Assumptions) || Count is < 1 or > 100
            || string.IsNullOrWhiteSpace(PartFile) || Path.GetFileName(PartFile) != PartFile
            || !Uri.TryCreate(SourceUrl, UriKind.Absolute, out var url) || url.Scheme != "https"
            || positive.Any(v => !double.IsFinite(v) || v <= 0)
            || !double.IsFinite(StowageFirstYM)
            || FirstReleaseMissionSeconds+(Count-1)*ReleaseIntervalSeconds >= MaximumMissionSeconds
            || OpeningVelocityLocalMps is not { Length: 3 }
            || OpeningVelocityLocalMps.Any(v => !double.IsFinite(v))
            || OpeningVelocity.Magnitude is <= 0 or > 10)
            throw new InvalidDataException("Invalid Flight 14 payload assumptions or deployment envelope.");
    }

    /// <summary>Attach real part instances inside the existing nose, preserving the outer hull.</summary>
    public IReadOnlyList<string> AttachTo(Vessel carrier, string dataDirectory)
    {
        Validate();
        var definition = PartDefinition.LoadFromJson(Path.Combine(dataDirectory, "parts", PartFile));
        var root = carrier.Parts.Root ?? throw new ArgumentException("Payload carrier has no root.");
        if (!definition.InternalPayload || definition.Category != PartCategory.Command
            || !double.IsFinite(definition.MassDry) || definition.MassDry <= 0
            || definition.LengthM <= 0 || definition.DiameterM <= 0
            || !definition.AttachmentNodes.Any(n => n.Id == "carrier")
            || !root.Definition.HasVehicleRole("command")
            || definition.DiameterM >= root.Definition.DiameterM
            || StowageSpacingM < definition.LengthM
            || System.Math.Max(System.Math.Abs(StowageFirstYM),
                System.Math.Abs(StowageFirstYM+(Count-1)*StowageSpacingM))
                +definition.LengthM*0.5 > root.Definition.LengthM*0.5
            || carrier.Parts.Parts.Any(p => p.Definition.HasVehicleRole("payload")))
            throw new InvalidDataException("Payload estimate cannot fit the declared carrier bay.");
        // The diagnostic owns this catalog instance. Nodes are internal racks, not new hull sections.
        var ids = new List<string>(Count);
        for (int i = 0; i < Count; i++)
        {
            string nodeId = $"flight14-bay-{i+1:00}";
            var payload = new Part(definition, $"flight14-starlink-v3-{i+1:00}");
            root.Definition.AttachmentNodes.Add(new AttachmentNodeDef {
                Id = nodeId, Position = [0, StowageFirstYM+i*StowageSpacingM, 0], Size = 1, Type = "radial"
            });
            carrier.Parts.AddPart(payload);
            carrier.Parts.AddJoint(new Joint(root, payload, nodeId, "carrier"));
            ids.Add(payload.InstanceId);
        }
        return ids.AsReadOnly();
    }
}
