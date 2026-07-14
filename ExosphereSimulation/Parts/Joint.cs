namespace Exosphere.Simulation.Parts;

public class Joint
{
    public Part   Parent       { get; }
    public Part   Child        { get; }
    public string ParentNodeId { get; }
    public string ChildNodeId  { get; }

    // Structural load limits (N). Defaults leave headroom for a Flight 7-class stack at
    // Max-Q / near-MECO accel: size-3 nodes scale by size² → ~450 MN tensile / ~225 MN shear.
    // Nominal [G] ascent and belly-flop EDL must not break; overload tests lower these or apply absurd accel.
    public double TensileStrength     { get; set; } = 50_000_000.0;
    public double CompressiveStrength { get; set; } = 100_000_000.0;
    public double ShearStrength       { get; set; } = 25_000_000.0;

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
