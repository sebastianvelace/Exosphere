namespace Exosphere.Simulation.Construction;

public sealed class VesselCraftDefinition
{
    public string Name { get; set; } = "Constructed Vessel";
    public List<AssemblyPart> Parts { get; set; } = new();
    public List<AssemblyConnection> Connections { get; set; } = new();
    /// <summary>
    /// Optional payload declarations carried by this legacy-compatible craft file.
    /// Keeping the manifest here means the VAB undo/redo snapshots and older JSON
    /// files can round-trip payload intent without requiring a separate sidecar file.
    /// </summary>
    public List<PayloadManifestEntryV2> PayloadManifest { get; set; } = new();
}
