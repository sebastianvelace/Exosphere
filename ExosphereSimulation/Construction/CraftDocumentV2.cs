namespace Exosphere.Simulation.Construction;

/// <summary>
/// Authoritative editable vehicle document. Geometry, connections, staging and payloads
/// live in one topology so the VAB, flight spawn and save system cannot silently diverge.
/// SI units and a +Y vehicle datum are mandatory.
/// </summary>
public sealed class CraftDocumentV2
{
    public const int CurrentFormatVersion = 2;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string DocumentId { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Constructed Vessel";
    public string? VehicleVariantId { get; set; }
    public string LengthUnit { get; set; } = "m";
    public string MassUnit { get; set; } = "kg";
    public string VehicleDatum { get; set; } = "root_part_origin_y_up";
    public List<CraftPartInstanceV2> Parts { get; set; } = new();
    public List<AssemblyConnection> Connections { get; set; } = new();
    public List<CraftStageV2> Stages { get; set; } = new();
    public Dictionary<string, List<CraftActionV2>> ActionGroups { get; set; } = new();
    public List<PayloadManifestEntryV2> PayloadManifest { get; set; } = new();
}

public sealed class CraftPartInstanceV2
{
    public string InstanceId { get; set; } = "";
    public string DefinitionId { get; set; } = "";
    public string? ParentInstanceId { get; set; }
    public string? ParentNodeId { get; set; }
    public string? ChildNodeId { get; set; }
    public double RotationDegrees { get; set; }
    public int RadialSymmetry { get; set; } = 1;
    public Dictionary<string, double> ResourcesKg { get; set; } = new();
    public Dictionary<string, string> Configuration { get; set; } = new();
}

public sealed class CraftStageV2
{
    public string StageId { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Stage";
    public int Sequence { get; set; }
    public List<CraftActionV2> Actions { get; set; } = new();
}

public enum CraftActionKind
{
    EngineStart,
    EngineShutdown,
    Separate,
    ReleaseFairing,
    DeployPayload,
    DeployLandingGear,
    DeploySolarPanel,
    DeployParachute,
}

public sealed class CraftActionV2
{
    public CraftActionKind Kind { get; set; }
    public string TargetInstanceId { get; set; } = "";
    public double DelaySeconds { get; set; }
}

public sealed class PayloadManifestEntryV2
{
    public string PayloadId { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Payload";
    public string PartInstanceId { get; set; } = "";
    public double DeclaredMassKg { get; set; }
    public bool BecomesIndependentVessel { get; set; } = true;
}

public static class CraftDocumentMigration
{
    public static CraftDocumentV2 FromLegacy(VesselCraftDefinition legacy)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        return new CraftDocumentV2
        {
            Name = legacy.Name,
            Parts = legacy.Parts.Select(p => new CraftPartInstanceV2
            {
                InstanceId = p.InstanceId,
                DefinitionId = p.DefinitionId,
                ParentInstanceId = p.ParentInstanceId,
                ParentNodeId = p.ParentNodeId,
                ChildNodeId = p.ChildNodeId,
            }).ToList(),
            Connections = legacy.Connections.ToList(),
        };
    }

    public static VesselCraftDefinition ToLegacy(CraftDocumentV2 document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new VesselCraftDefinition
        {
            Name = document.Name,
            Parts = document.Parts.Select(p => new AssemblyPart(
                p.InstanceId, p.DefinitionId, p.ParentInstanceId,
                p.ParentNodeId, p.ChildNodeId)).ToList(),
            Connections = document.Connections.ToList(),
        };
    }
}
