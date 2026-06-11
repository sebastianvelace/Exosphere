namespace Exosphere.Simulation.Parts;

public class Joint
{
    public Part   Parent       { get; }
    public Part   Child        { get; }
    public string ParentNodeId { get; }
    public string ChildNodeId  { get; }

    // Tolerancias estructurales (N)
    public double TensileStrength     { get; set; } = 1_000_000.0;
    public double CompressiveStrength { get; set; } = 2_000_000.0;
    public double ShearStrength       { get; set; } = 500_000.0;

    // Cargas actuales (actualizadas por StressSolver cada tick)
    public double CurrentTensileLoad { get; set; }
    public double CurrentShearLoad   { get; set; }

    public bool IsBreaking =>
        CurrentTensileLoad > TensileStrength ||
        CurrentShearLoad   > ShearStrength;

    public Joint(Part parent, Part child, string parentNodeId, string childNodeId)
    {
        Parent       = parent;
        Child        = child;
        ParentNodeId = parentNodeId;
        ChildNodeId  = childNodeId;

        // Escalar tolerancia por tamaño del nodo de unión
        var node = parent.Definition.AttachmentNodes
            .FirstOrDefault(n => n.Id == parentNodeId);
        if (node != null)
        {
            double sizeFactor = System.Math.Pow(node.Size, 2.0);
            TensileStrength     *= sizeFactor;
            CompressiveStrength *= sizeFactor;
            ShearStrength       *= sizeFactor;
        }
    }
}
