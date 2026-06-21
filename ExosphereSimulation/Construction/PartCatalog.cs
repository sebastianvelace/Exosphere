namespace Exosphere.Simulation.Construction;

using Exosphere.Simulation.Parts;

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

    public static PartCatalog LoadFromDirectory(string partsDirectory) =>
        new(PartDefinition.LoadAllFromDirectory(partsDirectory));
}
