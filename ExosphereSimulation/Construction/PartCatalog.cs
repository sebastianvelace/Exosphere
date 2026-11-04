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

    /// <summary>
    /// Fidelity-track fields (nose shape, control surfaces, header tanks) must cite their
    /// source: a part may only declare them with a provenance record that says whether the
    /// value is published, derived, estimated or calibrated. A record that vouches for a
    /// single number must match the data.
    /// </summary>
    private static void ValidateFidelityProvenance(IEnumerable<PartDefinition> parts, DataProvenanceRegistry provenance)
    {
        foreach (var part in parts)
        {
            if (part.NoseLengthM > 0.0)
            {
                var record = provenance.Require(part.Id, "noseLengthM");
                if (record.Value is { } value && System.Math.Abs(value - part.NoseLengthM) > 1e-9)
                    throw new InvalidDataException(
                        $"Part '{part.Id}' nose_length_m {part.NoseLengthM} differs from its provenance value {value}.");
            }
            if (part.BodyFlaps.Count > 0)
                provenance.Require(part.Id, "bodyFlaps");
            if (part.HeaderTanks.Count > 0)
                provenance.Require(part.Id, "headerTanks");
            if (part.DryMassCenterYM != 0.0)
            {
                var record = provenance.Require(part.Id, "dryMassCenterYM");
                if (record.Value is { } value && System.Math.Abs(value - part.DryMassCenterYM) > 1e-9)
                    throw new InvalidDataException(
                        $"Part '{part.Id}' dry_mass_center_y_m {part.DryMassCenterYM} differs from its provenance value {value}.");
            }
        }
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
        ValidateFidelityProvenance(parts, provenance);
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
            var modelIds = cluster.Engines
                .Select(mount => string.IsNullOrWhiteSpace(mount.EngineModelId)
                    ? cluster.EngineModelId
                    : mount.EngineModelId)
                .Distinct(StringComparer.Ordinal);
            part.ResolvedEngineModels = modelIds.ToDictionary(
                id => id,
                id => catalog.Models[id],
                StringComparer.Ordinal);
            part.ResolvedEngineCluster = cluster;
        }
    }
}
