namespace Exosphere.Simulation.Construction;

public sealed class VesselCraftDefinition
{
    public string Name { get; set; } = "Constructed Vessel";
    public List<AssemblyPart> Parts { get; set; } = new();
    public List<AssemblyConnection> Connections { get; set; } = new();
}
