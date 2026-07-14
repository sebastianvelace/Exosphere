namespace Exosphere.Simulation.Persistence;

/// <summary>
/// Serializable mid-mission snapshot. Pure DTO — no Godot dependency.
/// </summary>
public sealed class MissionSaveState
{
    public int Version { get; set; } = 1;
    public double CurrentTime { get; set; }
    public string? ActiveVesselId { get; set; }
    public string MissionPhase { get; set; } = "PRE_LAUNCH";
    public int WarpIndex { get; set; }
    public List<VesselSaveState> Vessels { get; set; } = new();
}

public sealed class VesselSaveState
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public double PositionZ { get; set; }

    public double VelocityX { get; set; }
    public double VelocityY { get; set; }
    public double VelocityZ { get; set; }

    public double OrientationW { get; set; } = 1.0;
    public double OrientationX { get; set; }
    public double OrientationY { get; set; }
    public double OrientationZ { get; set; }

    public bool IsOnRails { get; set; }
    public string ReferenceBodyId { get; set; } = "earth";
    public double Throttle { get; set; }
    public bool SASEnabled { get; set; } = true;

    public bool IsGroundHeld { get; set; }
    public double GroundNormalX { get; set; }
    public double GroundNormalY { get; set; }
    public double GroundNormalZ { get; set; }
    public double GroundOffset { get; set; }

    public int RootIndex { get; set; }
    public List<PartSaveState> Parts { get; set; } = new();
    public List<JointSaveState> Joints { get; set; } = new();
}

public sealed class PartSaveState
{
    public string DefinitionId { get; set; } = "";
    public double LiquidFuel { get; set; }
    public double Oxidizer { get; set; }
    public double SolidFuel { get; set; }
    public double Monopropellant { get; set; }
    public double Temperature { get; set; } = 290.0;
    public bool IsStagingActive { get; set; } = true;
    public bool IsBroken { get; set; }
}

public sealed class JointSaveState
{
    public int ParentIndex { get; set; }
    public int ChildIndex { get; set; }
    public string ParentNodeId { get; set; } = "";
    public string ChildNodeId { get; set; } = "";
}
