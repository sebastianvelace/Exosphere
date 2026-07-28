namespace Exosphere.Simulation.Construction;

using Exosphere.Simulation.Data;
using Exosphere.Simulation.Parts;
using Exosphere.Simulation.Propulsion;

public sealed class PartCatalog
{
    private readonly Dictionary<string, PartDefinition> _parts;

    private PartCatalog(Dictionary<string, PartDefinition> parts)
    {
        _parts = parts;
    }

    public IReadOnlyDictionary<string, PartDefinition> Parts => _parts;

    public IEnumerable<PartDefinition> AllParts => _parts.Values.OrderBy(p => p.CategoryStr).ThenBy(p => p.Name);

    public PartDefinition this[string id] => _parts[id];

    public bool TryGet(string id, out PartDefinition definition) => _parts.TryGetValue(id, out definition!);

    public static PartCatalog LoadFromDirectory(string partsDirectory)
    {
        var parts = PartDefinition.LoadAllFromDirectory(partsDirectory);
        string? dataDirectory = Directory.GetParent(partsDirectory)?.FullName;
        if (dataDirectory != null)
            ResolveEngineContracts(parts.Values, dataDirectory);
        return new PartCatalog(parts);
    }

    private static void ResolveEngineContracts(
        IEnumerable<PartDefinition> parts,
        string dataDirectory)
    {
        string engineDirectory = Path.Combine(dataDirectory, "engines");
        string clusterDirectory = Path.Combine(dataDirectory, "engine_clusters");
        string provenanceDirectory = Path.Combine(dataDirectory, "provenance");
        if (!Directory.Exists(engineDirectory)
            || !Directory.Exists(clusterDirectory)
            || !Directory.Exists(provenanceDirectory))
            return;

        var provenance = DataProvenanceRegistry.LoadFromDirectory(provenanceDirectory);
        var catalog = EngineDefinitionCatalog.Load(
            engineDirectory, clusterDirectory, provenance);
        foreach (var part in parts.Where(p => p.Category == PartCategory.Engine))
        {
            if (string.IsNullOrWhiteSpace(part.EngineModelId))
                continue;
            if (!catalog.Models.TryGetValue(part.EngineModelId, out var model))
                throw new InvalidDataException(
                    $"Part '{part.Id}' references unknown engine model '{part.EngineModelId}'.");
            part.ResolvedEngineModel = model;

            if (string.IsNullOrWhiteSpace(part.EngineClusterId))
                continue;
            if (!catalog.Clusters.TryGetValue(part.EngineClusterId, out var cluster))
                throw new InvalidDataException(
                    $"Part '{part.Id}' references unknown engine cluster '{part.EngineClusterId}'.");
            if (!string.Equals(cluster.EngineModelId, model.Id, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Part '{part.Id}' engine model and cluster disagree.");
            if (cluster.Engines.Count != System.Math.Max(1, part.EngineCount))
                throw new InvalidDataException(
                    $"Part '{part.Id}' engine count does not match cluster '{cluster.Id}'.");
            part.ResolvedEngineCluster = cluster;
        }
    }
}
